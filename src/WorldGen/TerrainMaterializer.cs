using System;
using System.Collections.Generic;

namespace Void;

/// <summary>
/// Phase 2 step 5: fills chunks with tiles from Phase 1's heightmap and biome
/// map (VOID-056, world-generation-spec §6).
///
/// <para>This is the step that turns per-column metadata into world. Phase 1
/// produces arrays — a surface row per column, a biome id per column — and every
/// later step of Phases 2 and 3 carves, floods or scatters <i>tiles</i>. Nothing
/// owned that conversion before this existed, so caves had nothing to carve out
/// of. What it produces is deliberately boring: solid, uncarved terrain that is
/// correct.</para>
///
/// <para><b>No randomness, and deliberately no <see cref="GenKeys"/> key.</b>
/// The output is a pure function of the heightmap, the biome map, the layer
/// boundaries and the biome palettes — all of which are already determined by
/// the seed. Deriving a stream here would add a key that nothing draws from,
/// which is worse than misleading: a later edit would "obviously" be allowed to
/// draw from it and would silently reorder nothing, since a purely derived step
/// has no draw order to preserve. Palette variation that genuinely needs
/// randomness (<see cref="BiomePalette.WallAmbient"/>) is a later ticket and
/// brings its own key.</para>
///
/// <para><b>Chunk-at-a-time is the primitive.</b>
/// <see cref="MaterializeChunk"/> is a pure function of the chunk coordinate:
/// chunks may be generated in any order, independently, on any thread, and
/// re-generated later to the same bytes. That is what lets the world viewer
/// (VOID-057) and chunk streaming materialise only what they need instead of
/// holding a whole world — a Medium world is 2,900 chunks, about 92 MB of tile
/// data. <see cref="MaterializeWorld"/> is a thin loop over the primitive for
/// tests and tooling, not the path the game takes.</para>
///
/// <para>Engine-free: pure arithmetic over content, so the whole step is
/// testable under <c>dotnet test</c>.</para>
/// </summary>
public static class TerrainMaterializer
{
    /// <summary>
    /// Fills one chunk of solid terrain.
    /// </summary>
    /// <param name="context">
    /// Supplies the heightmap, the biome map, the registries and the world's
    /// tile extents. Both phase outputs must already be set; reading either
    /// early throws from the context, which is the intended failure.
    /// </param>
    /// <param name="boundaries">
    /// The world's layer boundaries, from Phase 1 step 3. Passed rather than
    /// recomputed so that the rows this step calls "underground" are exactly the
    /// rows the manifest records — recomputing invites the two to drift.
    /// </param>
    /// <param name="chunkX">Chunk column. Chunk-space, not tiles.</param>
    /// <param name="chunkY">Chunk row. Chunk-space, not tiles.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If the chunk coordinate is outside the world's chunk grid. Loud rather
    /// than an empty chunk: a caller looping past the edge would otherwise get
    /// plausible-looking air and never learn its bounds were wrong.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// If a palette names a block or wall that does not resolve. The content
    /// loaders prove every palette id resolves at boot, so reaching this means
    /// the registries passed here are not the ones the biome map was built
    /// against. Fatal rather than a fallback tile, which would bury the mistake
    /// under terrain that looks generated.
    /// </exception>
    public static Chunk MaterializeChunk(
        GenerationContext context, LayerBoundaries boundaries, int chunkX, int chunkY)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(boundaries);

        int widthTiles = context.SizePreset.WidthTiles;
        int heightTiles = context.SizePreset.HeightTiles;

        ThrowIfChunkOutOfRange(chunkX, chunkY, widthTiles, heightTiles);

        Heightmap heightmap = context.Heightmap;
        BiomeMap biomeMap = context.BiomeMap;
        Registry<BiomeDefinition> biomes = context.Content.Biomes;

        Chunk chunk = new Chunk(chunkX, chunkY);

        // One resolved palette per biome id, not per tile. A chunk is 4,096
        // tiles over at most 64 columns, so resolving inside the row loop would
        // be four thousand registry lookups to answer at most a couple of
        // distinct questions.
        Dictionary<string, ResolvedPalette> palettes = new(StringComparer.Ordinal);

        int originX = chunkX * Chunk.Width;
        int originY = chunkY * Chunk.Height;

        for (int localX = 0; localX < Chunk.Width; localX++)
        {
            int worldX = originX + localX;

            // Columns past the world's right edge stay air. World width is not
            // required to be a whole number of chunks and WorldDimensions rounds
            // the chunk count up, so the last chunk column legitimately hangs
            // over the edge. Air is what a tile outside the world means, and a
            // zeroed chunk is already air, so this is a skip rather than a fill.
            if (worldX >= widthTiles)
            {
                break;
            }

            int surfaceY = heightmap[worldX];
            ResolvedPalette surfacePalette =
                Resolve(palettes, biomeMap[worldX], context, biomes);
            ResolvedPalette undergroundPalette =
                Resolve(palettes, biomeMap.UndergroundBiomeAt(worldX, biomes), context, biomes);

            // Inside a transition band the column also carries the biome on the
            // other side (VOID-060). Both palettes are resolved once per column
            // and the choice is made per tile, rather than resolving inside the
            // row loop: the pair is fixed for the whole column, and only which of
            // the two applies varies down it.
            string? blendId = biomeMap.BlendBiomeAt(worldX);
            ResolvedPalette blendSurfacePalette = surfacePalette;
            ResolvedPalette blendUndergroundPalette = undergroundPalette;

            if (blendId is not null)
            {
                blendSurfacePalette = Resolve(palettes, blendId, context, biomes);
                blendUndergroundPalette = Resolve(
                    palettes, BiomeMap.UndergroundVariantOf(blendId, biomes), context, biomes);
            }

            for (int localY = 0; localY < Chunk.Height; localY++)
            {
                int worldY = originY + localY;

                // Same rule as the width: rows past the bottom of the world stay
                // air rather than wrapping into the first row of nothing.
                if (worldY >= heightTiles)
                {
                    break;
                }

                // Deterministic from the coordinate, not from a draw, so this
                // step stays a pure function of the chunk coordinate and needs no
                // stream of its own -- see BiomeMap.TakesBlendAt.
                bool blended = blendId is not null && biomeMap.TakesBlendAt(worldX, worldY);

                chunk.Tiles[Chunk.Index(localX, localY)] = TileAt(
                    worldY,
                    surfaceY,
                    boundaries,
                    blended ? blendSurfacePalette : surfacePalette,
                    blended ? blendUndergroundPalette : undergroundPalette);
            }
        }

        chunk.BiomePrimary = DominantBiomeNumericId(biomeMap, originX, widthTiles, biomes);
        chunk.LayerPrimary = LayerAt(originY, boundaries);

        // WalkableRatio, OreDensity, StructureRefs and SpecialFlags are left at
        // their defaults on purpose. Every one of them describes a property of
        // the *finished* chunk, and this chunk is not finished: caves have not
        // been carved, so nothing is walkable yet; ores and structures are
        // Phase 3. Writing a plausible value here would be a number later phases
        // would have to know to distrust.
        return chunk;
    }

    /// <summary>
    /// Materialises every chunk of the world, in explicit row-major chunk order.
    /// </summary>
    /// <remarks>
    /// For tests and offline tooling. The game must not call this: a Medium
    /// world is 100 x 29 chunks and about 92 MB of tile data, and chunk
    /// streaming exists precisely so that no one holds all of it. Order is fixed
    /// and explicit so the sequence is reproducible, but nothing about the
    /// output depends on it — see <see cref="MaterializeChunk"/>.
    /// </remarks>
    public static IReadOnlyList<Chunk> MaterializeWorld(
        GenerationContext context, LayerBoundaries boundaries)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(boundaries);

        int chunksX = CeilDiv(context.SizePreset.WidthTiles, Chunk.Width);
        int chunksY = CeilDiv(context.SizePreset.HeightTiles, Chunk.Height);

        List<Chunk> chunks = new(chunksX * chunksY);

        for (int chunkY = 0; chunkY < chunksY; chunkY++)
        {
            for (int chunkX = 0; chunkX < chunksX; chunkX++)
            {
                chunks.Add(MaterializeChunk(context, boundaries, chunkX, chunkY));
            }
        }

        return chunks;
    }

    /// <summary>
    /// The column rule, in one place: air above the surface row, the surface
    /// block at it, the subsurface band beneath it, base block below that.
    ///
    /// <para>Walls follow the blocks — <c>wall_default</c> at and below the
    /// surface, none in open sky — so that a cave carved later opens onto a
    /// walled background rather than a hole through to the void.</para>
    ///
    /// <para><b>Which palette applies is decided per row, not per column.</b>
    /// Rows at or below <see cref="LayerBoundaries.OutsideEnd"/> belong to the
    /// underground layer and take the column's underground variant; rows above
    /// it take the surface biome. The surface row and its subsurface band sit
    /// inside the Outside layer by construction (the heightmap's band is a slice
    /// of it), so in practice the switch happens well below them and only the
    /// base fill changes palette — but expressing it as one row-level rule keeps
    /// that a consequence rather than a second special case that could disagree.
    /// </para>
    /// </summary>
    private static Tile TileAt(
        int worldY,
        int surfaceY,
        LayerBoundaries boundaries,
        ResolvedPalette surface,
        ResolvedPalette underground)
    {
        if (worldY < surfaceY)
        {
            return Tile.Air;
        }

        ResolvedPalette palette = worldY >= boundaries.OutsideEnd ? underground : surface;

        ushort blockId =
            worldY == surfaceY ? palette.SurfaceBlock
            : worldY <= surfaceY + palette.SubsurfaceDepth ? palette.SubsurfaceBlock
            : palette.BaseBlock;

        return new Tile(blockId, palette.WallDefault);
    }

    /// <summary>
    /// The chunk's dominant surface biome as a numeric block-free id.
    /// </summary>
    /// <remarks>
    /// <para>Biomes carry no numeric id of their own — nothing in the save format
    /// stores one — so the value written is the biome's index in the
    /// ordinal-sorted registry. That is stable within a build and is exactly what
    /// <c>biome_primary</c> is for: a hint for finding chunks without loading
    /// their tiles, not a durable reference.</para>
    /// <para>Dominance is by column count across the chunk. <b>Ties break
    /// towards the leftmost column</b>, which is arbitrary but must be stated,
    /// because a tie resolved by dictionary order would make the chunk header
    /// differ between runs of the same seed.</para>
    /// </remarks>
    private static ushort DominantBiomeNumericId(
        BiomeMap biomeMap, int originX, int widthTiles, Registry<BiomeDefinition> biomes)
    {
        string best = biomeMap[originX];
        int bestCount = 0;

        // Ordered scan with a strict >, so the first id to reach a count is the
        // one that keeps it. Counting into a dictionary and taking the max would
        // reintroduce exactly the hash-order tie this avoids.
        Dictionary<string, int> counts = new(StringComparer.Ordinal);

        for (int x = originX; x < originX + Chunk.Width && x < widthTiles; x++)
        {
            string id = biomeMap[x];
            counts.TryGetValue(id, out int count);
            counts[id] = ++count;

            if (count > bestCount)
            {
                bestCount = count;
                best = id;
            }
        }

        int index = IndexOf(biomes.Ids, best);

        return index >= 0 && index <= ushort.MaxValue
            ? (ushort)index
            : throw new InvalidOperationException(
                $"Biome '{best}' is not in the biome registry passed here, so the chunk's "
                + "biome_primary cannot be resolved.");
    }

    /// <summary>
    /// The layer a chunk belongs to, taken from its <b>top</b> row.
    /// </summary>
    /// <remarks>
    /// A 64-row chunk can straddle a boundary, and <c>layer_primary</c> is one
    /// byte. The top row is chosen rather than the midpoint because layer
    /// boundaries are where a band <i>begins</i>, so this reads as "the band this
    /// chunk starts in" — a rule a human can apply by hand from the chunk
    /// coordinate alone.
    /// </remarks>
    private static WorldLayer LayerAt(int worldY, LayerBoundaries boundaries) =>
        worldY < boundaries.OutsideEnd ? WorldLayer.Outside
        : worldY < boundaries.UndergroundEnd ? WorldLayer.Underground
        : worldY < boundaries.DeepEnd ? WorldLayer.Deep
        : WorldLayer.Void;

    /// <summary>
    /// Resolves a biome's palette to numeric ids once and caches it for the rest
    /// of the chunk. The cache is keyed ordinally, like every id comparison in
    /// the content layer, so it cannot depend on the machine's culture.
    /// </summary>
    private static ResolvedPalette Resolve(
        Dictionary<string, ResolvedPalette> cache,
        string biomeId,
        GenerationContext context,
        Registry<BiomeDefinition> biomes)
    {
        if (cache.TryGetValue(biomeId, out ResolvedPalette cached))
        {
            return cached;
        }

        if (!biomes.TryGet(biomeId, out BiomeDefinition biome))
        {
            throw new InvalidOperationException(
                $"Biome '{biomeId}' is not in the biome registry passed here, so its palette "
                + "cannot be resolved.");
        }

        ResolvedPalette resolved = new ResolvedPalette(
            BlockId(context, biome, "surface_block", biome.Palette.SurfaceBlock),
            BlockId(context, biome, "subsurface_block", biome.Palette.SubsurfaceBlock),
            BlockId(context, biome, "base_block", biome.Palette.BaseBlock),
            WallId(context, biome, "wall_default", biome.Palette.WallDefault),
            biome.SubsurfaceDepth ?? context.WorldType.Terrain.DefaultSubsurfaceDepth);

        cache[biomeId] = resolved;
        return resolved;
    }

    /// <summary>Palette block id to its numeric form; a dangling id is fatal.</summary>
    private static ushort BlockId(
        GenerationContext context, BiomeDefinition biome, string field, string blockId) =>
        context.Content.Blocks.TryGet(blockId, out BlockDefinition block)
            ? block.NumericId
            : throw new InvalidOperationException(
                $"Biome '{biome.Id}' palette.{field} is '{blockId}', which is not a registered "
                + "block. Content load proves every palette id resolves, so the registries here "
                + "are not the ones this world was generated against.");

    /// <summary>Palette wall id to its numeric form; a dangling id is fatal.</summary>
    private static ushort WallId(
        GenerationContext context, BiomeDefinition biome, string field, string wallId) =>
        context.Content.Walls.TryGet(wallId, out WallDefinition wall)
            ? wall.NumericId
            : throw new InvalidOperationException(
                $"Biome '{biome.Id}' palette.{field} is '{wallId}', which is not a registered "
                + "wall. Content load proves every palette id resolves, so the registries here "
                + "are not the ones this world was generated against.");

    /// <summary>
    /// Ordinal index of an id in the registry's sorted id list, or -1. A linear
    /// scan: it runs once per chunk over a handful of biomes, and the sorted
    /// order is the thing being indexed into, so a hash lookup would answer a
    /// different question.
    /// </summary>
    private static int IndexOf(IReadOnlyList<string> ids, string id)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Rejects a chunk coordinate outside the world's chunk grid.</summary>
    private static void ThrowIfChunkOutOfRange(
        int chunkX, int chunkY, int widthTiles, int heightTiles)
    {
        int chunksX = CeilDiv(widthTiles, Chunk.Width);
        int chunksY = CeilDiv(heightTiles, Chunk.Height);

        if ((uint)chunkX >= (uint)chunksX)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkX), chunkX, $"World is {chunksX} chunks wide.");
        }

        if ((uint)chunkY >= (uint)chunksY)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkY), chunkY, $"World is {chunksY} chunks tall.");
        }
    }

    /// <summary>Integer division rounding up. Both arguments are positive here.</summary>
    private static int CeilDiv(int value, int divisor) => (value + divisor - 1) / divisor;

    /// <summary>
    /// A biome's palette with every id already resolved to the numbers a
    /// <see cref="Tile"/> stores, plus the subsurface depth that applies to it.
    ///
    /// <para>A struct because it is copied per column and never stored beyond a
    /// chunk; the whole point of it is to keep registry lookups out of the row
    /// loop.</para>
    /// </summary>
    /// <param name="SurfaceBlock">Numeric block id placed at the surface row.</param>
    /// <param name="SubsurfaceBlock">Numeric block id of the band below it.</param>
    /// <param name="BaseBlock">Numeric block id of the bulk fill.</param>
    /// <param name="WallDefault">Numeric wall id placed at and below the surface.</param>
    /// <param name="SubsurfaceDepth">
    /// Rows of subsurface block, already defaulted from the world type. Never
    /// negative; both content loaders reject that.
    /// </param>
    private readonly record struct ResolvedPalette(
        ushort SurfaceBlock,
        ushort SubsurfaceBlock,
        ushort BaseBlock,
        ushort WallDefault,
        int SubsurfaceDepth);
}
