using System;
using System.Collections.Generic;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// VOID-056 acceptance tests for Phase 2 step 5, terrain materialisation.
///
/// <para>These guard the properties every later step of Phases 2 and 3 assumes
/// without re-checking: a seed reproduces the tiles exactly, the column rule
/// holds everywhere, every id written resolves, and the world has no holes for
/// cave carving to fall through.</para>
///
/// <para>The <c>small</c> preset (4200 x 1200) is used throughout: it exercises
/// the same code as Medium at a fraction of the tiles, and — being 4200 wide,
/// which is not a multiple of 64 — it also exercises the ragged right-hand edge
/// that a Medium world's 6400 columns would hide.</para>
/// </summary>
public class TerrainMaterializerTests
{
    /// <summary>Fixed world id; generation takes identity as an input so runs can be compared.</summary>
    private static readonly Guid TestWorldId = new("00000000-0000-0000-0000-0000000000cc");

    /// <summary>
    /// Runs Phase 1 over the shipped content and hands back what step 5 needs.
    /// Uses the real <c>data/</c> tree rather than a fixture, so a palette that
    /// stops resolving in shipped content turns these tests red.
    /// </summary>
    private static (GenerationContext Context, LayerBoundaries Boundaries) World(long seed)
    {
        GenerationContext context = new(ContentPaths.All(), "void:home", seed, "small");
        WorldManifest manifest = WorldGenerator.Generate(context, TestWorldId);
        return (context, manifest.LayerBoundaries);
    }

    /// <summary>
    /// The determinism criterion, and the reason this step may never draw from an
    /// RNG. If two runs of one seed disagree by a single tile, every world an
    /// existing save refers to regenerates differently and the save is wrong.
    /// </summary>
    [Fact]
    public void SameSeedProducesIdenticalTiles()
    {
        (GenerationContext contextA, LayerBoundaries boundsA) = World(4242);
        (GenerationContext contextB, LayerBoundaries boundsB) = World(4242);

        // Chunks spread across the world and down through the layers, so a
        // difference confined to one band still fails.
        foreach ((int cx, int cy) in new[] { (0, 0), (17, 4), (40, 9), (65, 18) })
        {
            Chunk a = TerrainMaterializer.MaterializeChunk(contextA, boundsA, cx, cy);
            Chunk b = TerrainMaterializer.MaterializeChunk(contextB, boundsB, cx, cy);

            Assert.Equal(a.Serialize(), b.Serialize());
        }
    }

    /// <summary>
    /// Chunk order must not affect chunk content. This is what lets the world
    /// viewer and chunk streaming materialise on demand: if generating a chunk
    /// alone differed from generating it after its neighbours, every consumer
    /// would have to reproduce a whole-world traversal to get correct tiles.
    /// </summary>
    [Fact]
    public void ChunkContentDoesNotDependOnGenerationOrder()
    {
        (GenerationContext context, LayerBoundaries bounds) = World(7);

        Chunk alone = TerrainMaterializer.MaterializeChunk(context, bounds, 12, 6);

        // Generate its neighbours first, then it, in the opposite order.
        TerrainMaterializer.MaterializeChunk(context, bounds, 13, 6);
        TerrainMaterializer.MaterializeChunk(context, bounds, 11, 6);
        Chunk afterNeighbours = TerrainMaterializer.MaterializeChunk(context, bounds, 12, 6);

        Assert.Equal(alone.Serialize(), afterNeighbours.Serialize());
    }

    /// <summary>
    /// No gaps and no floating rows: every tile above the surface is air, every
    /// tile from the surface down is solid. Cave carving in W5 subtracts from
    /// this, so a hole here becomes a hole in the finished world that nothing
    /// downstream would flag.
    /// </summary>
    [Fact]
    public void EveryColumnIsAirAboveTheSurfaceAndSolidBelowIt()
    {
        (GenerationContext context, LayerBoundaries bounds) = World(31);

        Heightmap heightmap = context.Heightmap;
        int heightTiles = context.SizePreset.HeightTiles;
        int chunksY = (heightTiles + Chunk.Height - 1) / Chunk.Height;

        // A full column stack, so the assertion covers every row of the world
        // from sky to the bottom of the void rather than one chunk's worth.
        const int ChunkX = 9;

        for (int chunkY = 0; chunkY < chunksY; chunkY++)
        {
            Chunk chunk = TerrainMaterializer.MaterializeChunk(context, bounds, ChunkX, chunkY);

            for (int localX = 0; localX < Chunk.Width; localX++)
            {
                int worldX = (ChunkX * Chunk.Width) + localX;
                int surfaceY = heightmap[worldX];

                for (int localY = 0; localY < Chunk.Height; localY++)
                {
                    int worldY = (chunkY * Chunk.Height) + localY;
                    if (worldY >= heightTiles)
                    {
                        break;
                    }

                    Tile tile = chunk[localX, localY];

                    if (worldY < surfaceY)
                    {
                        Assert.True(tile.IsAir, $"Column {worldX} row {worldY} is above the surface ({surfaceY}) but is not air.");
                    }
                    else
                    {
                        Assert.False(tile.IsAir, $"Column {worldX} row {worldY} is at or below the surface ({surfaceY}) but is air.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The column rule itself: surface block at the surface row, exactly
    /// <c>subsurface_depth</c> rows of subsurface block under it, base block
    /// below that. Checked against the biome's own palette, so a palette wired to
    /// the wrong field — base block where subsurface belongs — fails here rather
    /// than looking merely odd in the viewer.
    /// </summary>
    [Fact]
    public void ColumnFollowsTheBiomePaletteBands()
    {
        (GenerationContext context, LayerBoundaries bounds) = World(88);

        Registry<BiomeDefinition> biomes = context.Content.Biomes;
        Registry<BlockDefinition> blocks = context.Content.Blocks;
        int defaultDepth = context.WorldType.Terrain.DefaultSubsurfaceDepth;

        // Surface rows sit inside the Outside layer, so a chunk row containing
        // the surface band is where the three-band rule is visible at all.
        for (int chunkX = 0; chunkX < 4; chunkX++)
        {
            for (int localX = 0; localX < Chunk.Width; localX++)
            {
                int worldX = (chunkX * Chunk.Width) + localX;
                int surfaceY = context.Heightmap[worldX];

                BiomeDefinition biome = biomes[context.BiomeMap[worldX]];
                int depth = biome.SubsurfaceDepth ?? defaultDepth;

                int chunkY = surfaceY / Chunk.Height;
                Chunk chunk = TerrainMaterializer.MaterializeChunk(context, bounds, chunkX, chunkY);

                Assert.Equal(
                    blocks[biome.Palette.SurfaceBlock].NumericId,
                    chunk[localX, surfaceY % Chunk.Height].BlockId);

                // The subsurface band, then the first row past it. Rows are read
                // through a chunk lookup by world row so a band that straddles a
                // chunk boundary is still checked.
                for (int offset = 1; offset <= depth; offset++)
                {
                    Assert.Equal(
                        blocks[biome.Palette.SubsurfaceBlock].NumericId,
                        BlockAt(context, bounds, chunkX, localX, surfaceY + offset));
                }

                Assert.Equal(
                    blocks[biome.Palette.BaseBlock].NumericId,
                    BlockAt(context, bounds, chunkX, localX, surfaceY + depth + 1));
            }
        }
    }

    /// <summary>
    /// Walls back the solid world and stop at the sky. A missing wall reads as a
    /// hole through to nothing the moment a cave opens behind it; a wall in open
    /// sky paints the horizon.
    /// </summary>
    [Fact]
    public void WallsExistAtAndBelowTheSurfaceAndNowhereAbove()
    {
        (GenerationContext context, LayerBoundaries bounds) = World(12);

        Registry<BiomeDefinition> biomes = context.Content.Biomes;
        Registry<WallDefinition> walls = context.Content.Walls;

        const int ChunkX = 3;

        for (int localX = 0; localX < Chunk.Width; localX++)
        {
            int worldX = (ChunkX * Chunk.Width) + localX;
            int surfaceY = context.Heightmap[worldX];
            BiomeDefinition biome = biomes[context.BiomeMap[worldX]];
            ushort expected = walls[biome.Palette.WallDefault].NumericId;

            int chunkY = surfaceY / Chunk.Height;
            Chunk chunk = TerrainMaterializer.MaterializeChunk(context, bounds, ChunkX, chunkY);

            Assert.Equal(expected, chunk[localX, surfaceY % Chunk.Height].WallId);
            Assert.Equal(ContentIds.NoWall, chunk[localX, (surfaceY - 1) % Chunk.Height].WallId);
        }
    }

    /// <summary>
    /// Underground rows take the surface column's <c>underground_variant</c>
    /// palette, which is the pairing rule VOID-048 enforces at load. Filling the
    /// underground from the surface biome instead would put meadow stone under a
    /// snow mountain — plausible enough to ship unnoticed.
    ///
    /// <para><b>This pins the rule; it cannot yet catch it being broken.</b>
    /// Every shipped surface biome and its underground variant currently declare
    /// the same <c>base_block</c> and the same <c>wall_default</c> — meadow and
    /// root_hollows are both stone and dirt_wall, forest and root_tangle both
    /// mossy_stone and root_wall, frostreach and frozen_halls both stone and
    /// ice_wall — so at depth the two palettes are indistinguishable and this
    /// assertion would also pass against the surface one. It is here so that the
    /// day an underground palette diverges, the rule is already under test rather
    /// than being noticed in the viewer. A test that discriminates today would
    /// have to invent content, which would prove something about the fixture
    /// rather than about the world the game generates.</para>
    /// </summary>
    [Fact]
    public void UndergroundRowsUseTheUndergroundVariantPalette()
    {
        (GenerationContext context, LayerBoundaries bounds) = World(2024);

        Registry<BiomeDefinition> biomes = context.Content.Biomes;
        Registry<BlockDefinition> blocks = context.Content.Blocks;

        // Well inside the underground layer, so the row is unambiguous.
        int worldY = bounds.OutsideEnd + 10;
        int chunkY = worldY / Chunk.Height;
        const int ChunkX = 5;

        Chunk chunk = TerrainMaterializer.MaterializeChunk(context, bounds, ChunkX, chunkY);

        for (int localX = 0; localX < Chunk.Width; localX++)
        {
            int worldX = (ChunkX * Chunk.Width) + localX;
            BiomeDefinition underground =
                biomes[context.BiomeMap.UndergroundBiomeAt(worldX, biomes)];

            Assert.Equal(
                blocks[underground.Palette.BaseBlock].NumericId,
                chunk[localX, worldY % Chunk.Height].BlockId);
            Assert.Equal(
                context.Content.Walls[underground.Palette.WallDefault].NumericId,
                chunk[localX, worldY % Chunk.Height].WallId);
        }
    }

    /// <summary>
    /// Every id written must resolve. Tiles store raw numbers, so an unresolvable
    /// id is not a crash — it is a world full of blocks the renderer and the
    /// mining code silently cannot describe.
    /// </summary>
    [Fact]
    public void EveryBlockAndWallIdWrittenResolves()
    {
        (GenerationContext context, LayerBoundaries bounds) = World(555);

        Registry<BlockDefinition> blocks = context.Content.Blocks;
        Registry<WallDefinition> walls = context.Content.Walls;

        foreach ((int cx, int cy) in new[] { (0, 3), (20, 8), (50, 14), (65, 18) })
        {
            Chunk chunk = TerrainMaterializer.MaterializeChunk(context, bounds, cx, cy);

            foreach (Tile tile in chunk.Tiles)
            {
                Assert.True(blocks.TryGetByNumericId(tile.BlockId, out _), $"Block id {tile.BlockId} does not resolve.");
                Assert.True(walls.TryGetByNumericId(tile.WallId, out _), $"Wall id {tile.WallId} does not resolve.");
            }
        }
    }

    /// <summary>
    /// A per-biome <c>subsurface_depth</c> must win over the world-type default,
    /// and a biome that states none must take the default. This is the whole
    /// reason the field is nullable; a regression here silently flattens every
    /// biome to one band thickness, which looks fine and is not what was authored.
    /// </summary>
    [Fact]
    public void PerBiomeDepthOverridesTheWorldTypeDefault()
    {
        // Seed 2 is chosen because it puts all three surface biomes in a small
        // world, so both the overriding biome (Frostreach) and a defaulting one
        // are present. Frostreach needs the cold tail of the temperature field
        // and is genuinely absent from many seeds.
        (GenerationContext context, LayerBoundaries bounds) = World(2);

        Registry<BiomeDefinition> biomes = context.Content.Biomes;
        Registry<BlockDefinition> blocks = context.Content.Blocks;
        int defaultDepth = context.WorldType.Terrain.DefaultSubsurfaceDepth;

        // Frostreach is the shipped biome that overrides the default; the rest
        // take it. Both paths must appear in shipped content or the override is
        // untested in the world the game actually generates.
        Assert.NotNull(biomes["void:frostreach"].SubsurfaceDepth);
        Assert.Null(biomes["void:meadow"].SubsurfaceDepth);
        Assert.NotEqual(defaultDepth, biomes["void:frostreach"].SubsurfaceDepth!.Value);

        // Scan the whole width for one column of each kind rather than sweeping
        // a fixed prefix: which biome a seed puts in its first chunks is not
        // something this test should depend on.
        int overrideColumn = -1;
        int defaultColumn = -1;

        for (int x = 0; x < context.SizePreset.WidthTiles && (overrideColumn < 0 || defaultColumn < 0); x++)
        {
            bool overrides = biomes[context.BiomeMap[x]].SubsurfaceDepth is not null;

            if (overrides && overrideColumn < 0)
            {
                overrideColumn = x;
            }
            else if (!overrides && defaultColumn < 0)
            {
                defaultColumn = x;
            }
        }

        Assert.True(overrideColumn >= 0, "This seed generated no biome that overrides the default depth.");
        Assert.True(defaultColumn >= 0, "This seed generated no biome that takes the default depth.");

        foreach (int worldX in new[] { overrideColumn, defaultColumn })
        {
            BiomeDefinition biome = biomes[context.BiomeMap[worldX]];
            int depth = biome.SubsurfaceDepth ?? defaultDepth;
            int surfaceY = context.Heightmap[worldX];
            int chunkX = worldX / Chunk.Width;
            int localX = worldX % Chunk.Width;

            // The last row of the band is subsurface; the next one is not. Both
            // halves matter: only the second catches a band that runs too deep.
            Assert.Equal(
                blocks[biome.Palette.SubsurfaceBlock].NumericId,
                BlockAt(context, bounds, chunkX, localX, surfaceY + depth));
            Assert.Equal(
                blocks[biome.Palette.BaseBlock].NumericId,
                BlockAt(context, bounds, chunkX, localX, surfaceY + depth + 1));
        }
    }

    /// <summary>
    /// The world's width is not a multiple of the chunk width, so the last chunk
    /// column hangs over the edge. Those columns must stay air: filling them
    /// would put solid tiles outside the world, which the manifest's dimensions
    /// say do not exist, and every bounds check downstream would disagree with
    /// the tiles on disk.
    /// </summary>
    [Fact]
    public void ColumnsAndRowsOutsideTheWorldStayAir()
    {
        (GenerationContext context, LayerBoundaries bounds) = World(1);

        int widthTiles = context.SizePreset.WidthTiles;
        int heightTiles = context.SizePreset.HeightTiles;

        // Small is 4200 x 1200: 4200 = 65 chunks + 40 columns, 1200 = 18 chunks
        // + 48 rows, so the bottom-right chunk is ragged on both axes.
        Assert.NotEqual(0, widthTiles % Chunk.Width);
        Assert.NotEqual(0, heightTiles % Chunk.Height);

        int lastChunkX = widthTiles / Chunk.Width;
        int lastChunkY = heightTiles / Chunk.Height;

        Chunk chunk = TerrainMaterializer.MaterializeChunk(context, bounds, lastChunkX, lastChunkY);

        for (int localX = 0; localX < Chunk.Width; localX++)
        {
            for (int localY = 0; localY < Chunk.Height; localY++)
            {
                bool outside =
                    (lastChunkX * Chunk.Width) + localX >= widthTiles
                    || (lastChunkY * Chunk.Height) + localY >= heightTiles;

                if (outside)
                {
                    Assert.True(chunk[localX, localY].IsAir, $"Tile ({localX}, {localY}) is outside the world but is not air.");
                }
            }
        }
    }

    /// <summary>
    /// A chunk coordinate past the grid is a caller bug, and an empty chunk would
    /// let a loop run off the edge of the world producing plausible sky forever.
    /// </summary>
    [Fact]
    public void ChunkCoordinateOutsideTheWorldIsFatal()
    {
        (GenerationContext context, LayerBoundaries bounds) = World(2);

        int chunksX = (context.SizePreset.WidthTiles + Chunk.Width - 1) / Chunk.Width;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => TerrainMaterializer.MaterializeChunk(context, bounds, chunksX, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TerrainMaterializer.MaterializeChunk(context, bounds, -1, 0));
    }

    /// <summary>
    /// The whole-world loop must cover the chunk grid exactly — the same count
    /// the manifest's dimensions promise. A short world here would mean saved
    /// worlds with missing chunk files.
    /// </summary>
    [Fact]
    public void MaterializeWorldCoversTheWholeChunkGrid()
    {
        GenerationContext context = new(ContentPaths.All(), "void:home", 3, "small");
        WorldManifest manifest = WorldGenerator.Generate(context, TestWorldId);

        IReadOnlyList<Chunk> chunks =
            TerrainMaterializer.MaterializeWorld(context, manifest.LayerBoundaries);

        Assert.Equal(manifest.Dimensions.ChunksX * manifest.Dimensions.ChunksY, chunks.Count);

        HashSet<(int, int)> seen = [];
        foreach (Chunk chunk in chunks)
        {
            Assert.True(seen.Add((chunk.ChunkX, chunk.ChunkY)), "A chunk coordinate was materialised twice.");
        }
    }

    /// <summary>
    /// Chunk metadata this step can honestly know is set; metadata belonging to
    /// later phases is left alone. A walkable ratio invented before caves exist
    /// would be a number Phase 5 has no reason to distrust.
    /// </summary>
    [Fact]
    public void ChunkMetadataIsSetOnlyWhereThisStepKnowsIt()
    {
        (GenerationContext context, LayerBoundaries bounds) = World(77);

        Chunk surface = TerrainMaterializer.MaterializeChunk(context, bounds, 4, 3);
        // Rounded UP: LayerAt reads a chunk's top row, so the chunk containing
        // the first Deep row still starts in the Underground band.
        Chunk deep = TerrainMaterializer.MaterializeChunk(
            context, bounds, 4, (bounds.UndergroundEnd + Chunk.Height - 1) / Chunk.Height);

        Assert.Equal(WorldLayer.Outside, surface.LayerPrimary);
        Assert.Equal(WorldLayer.Deep, deep.LayerPrimary);

        Assert.True(surface.BiomePrimary < context.Content.Biomes.Count);

        Assert.Equal(0, surface.WalkableRatio);
        Assert.Empty(surface.StructureRefs);
        Assert.Equal(default, surface.SpecialFlags);
    }

    /// <summary>
    /// A negative depth is refused at load, in both places it can be authored.
    /// Materialisation would read it as starting the base fill above the surface
    /// row and invert the column — terrain that generates rather than fails.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-8)]
    public void NegativeSubsurfaceDepthIsFatalAtLoad(int depth)
    {
        Assert.Throws<ContentLoadException>(
            () => BiomeRegistryLoader.Load(
                new SingleDocumentSource(BiomeJson(depth)),
                ContentPaths.Blocks(),
                ContentPaths.Walls()));

        Assert.Throws<ContentLoadException>(
            () => WorldTypeRegistryLoader.Load(new SingleDocumentSource(WorldTypeJson(depth))));
    }

    /// <summary>Block id at a world row, read through the chunk that owns that row.</summary>
    private static ushort BlockAt(
        GenerationContext context, LayerBoundaries bounds, int chunkX, int localX, int worldY)
    {
        Chunk chunk = TerrainMaterializer.MaterializeChunk(
            context, bounds, chunkX, worldY / Chunk.Height);
        return chunk[localX, worldY % Chunk.Height].BlockId;
    }

    /// <summary>Serves one hand-written document, as the real loader would see it.</summary>
    private sealed class SingleDocumentSource : IContentSource
    {
        private readonly string _json;

        /// <summary>Wraps one JSON body as a content source.</summary>
        public SingleDocumentSource(string json) => _json = json;

        /// <inheritdoc/>
        public string Description => "in-memory source";

        /// <inheritdoc/>
        public IEnumerable<ContentDocument> ReadAll() => [new ContentDocument("test.json", _json)];
    }

    /// <summary>
    /// A minimal underground biome carrying the depth under test. Underground
    /// rather than surface so it needs no <c>underground_variant</c> of its own,
    /// keeping the document about the one field being validated.
    /// </summary>
    private static string BiomeJson(int depth) =>
        $$"""
        [{
          "id": "test:cave",
          "display_name": "Test Cave",
          "layer_category": "underground",
          "palette": {
            "surface_block": "void:stone",
            "subsurface_block": "void:stone",
            "base_block": "void:stone",
            "wall_default": "void:dirt_wall",
            "wall_ambient": []
          },
          "subsurface_depth": {{depth}},
          "underground_variant": null
        }]
        """;

    /// <summary>A minimal world type carrying the default depth under test.</summary>
    private static string WorldTypeJson(int depth) =>
        $$"""
        [{
          "id": "test:world",
          "display_name": "Test World",
          "layer_proportions": {
            "outside": 0.30, "underground": 0.25, "deep": 0.30, "void": 0.15
          },
          "terrain": { "default_subsurface_depth": {{depth}} },
          "size_preset": "medium",
          "size_presets": [{ "id": "medium", "width_tiles": 6400, "height_tiles": 1800 }]
        }]
        """;
}
