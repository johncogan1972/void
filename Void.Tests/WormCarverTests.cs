using System;
using System.Collections.Generic;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// VOID-065 acceptance tests for Phase 2 step 7, Perlin worm cave carving.
///
/// <para>What breaks in the real game if these go red: either the world is solid
/// rock with nowhere to go, or it is carved differently on two machines running
/// the same seed. The third thing guarded here is subtler and easier to lose —
/// that materialising a chunk still depends on nothing but its coordinate. Worms
/// are the first generation feature whose input spans chunks, so they are the
/// first thing that could quietly break the pull-based contract that chunk
/// streaming is going to be built on.</para>
/// </summary>
public class WormCarverTests
{
    /// <summary>Fixed world id; generation takes identity as an input so runs compare.</summary>
    private static readonly Guid TestWorldId = new("00000000-0000-0000-0000-0000000000dd");

    /// <summary>Generates the shipped home world, small preset, at a seed.</summary>
    private static (GenerationContext Context, LayerBoundaries Boundaries) World(long seed)
    {
        GenerationContext context = new(ContentPaths.All(), "void:home", seed, "small");
        WorldManifest manifest = WorldGenerator.Generate(context, TestWorldId);
        return (context, manifest.LayerBoundaries);
    }

    /// <summary>
    /// The shipped world actually gets caves. The whole point of the ticket: a
    /// world of solid rock is one nobody can explore, and it would still pass
    /// every other test in the suite.
    /// </summary>
    [Fact]
    public void TheShippedWorldIsCarved()
    {
        (GenerationContext context, _) = World(20260901);

        Assert.True(
            context.CaveNetwork.StampCount > 1000,
            $"Only {context.CaveNetwork.StampCount} carve stamps; the world is effectively solid.");
    }

    /// <summary>
    /// One seed, one set of tunnels. If this goes red, two players on the same
    /// seed explore different caves, and a chunk re-materialised after eviction
    /// disagrees with the one that was there before.
    /// </summary>
    [Fact]
    public void CarvingIsDeterministic()
    {
        (GenerationContext a, LayerBoundaries boundsA) = World(4242);
        (GenerationContext b, LayerBoundaries boundsB) = World(4242);

        Assert.Equal(a.CaveNetwork.StampCount, b.CaveNetwork.StampCount);

        // Compared as materialised tiles rather than as stamp arrays: the tiles
        // are what a player stands in, and they are what a save records.
        for (int chunkY = 3; chunkY < 6; chunkY++)
        {
            Chunk left = TerrainMaterializer.MaterializeChunk(a, boundsA, 9, chunkY);
            Chunk right = TerrainMaterializer.MaterializeChunk(b, boundsB, 9, chunkY);

            Assert.Equal(left.Serialize(), right.Serialize());
        }
    }

    /// <summary>
    /// Materialising a chunk still depends on nothing but its coordinate, with
    /// carving in the path.
    /// </summary>
    /// <remarks>
    /// This is the property the whole CaveNetwork design exists to protect, and
    /// the one a future change is most likely to break by reaching for the
    /// neighbouring chunk's tiles. Chunk streaming loads a 9x9 window in whatever
    /// order the player moves, so a chunk that came out differently depending on
    /// what was built before it would corrupt worlds in a way only long play
    /// would surface.
    /// </remarks>
    [Fact]
    public void AChunkIsTheSameWhicheverOrderItIsBuiltIn()
    {
        (GenerationContext context, LayerBoundaries bounds) = World(31);

        Chunk alone = TerrainMaterializer.MaterializeChunk(context, bounds, 20, 8);

        // Build every neighbour first, then the chunk again.
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                TerrainMaterializer.MaterializeChunk(context, bounds, 20 + dx, 8 + dy);
            }
        }

        Chunk afterNeighbours = TerrainMaterializer.MaterializeChunk(context, bounds, 20, 8);

        Assert.Equal(alone.Serialize(), afterNeighbours.Serialize());
    }

    /// <summary>
    /// A tunnel opens onto the biome's background wall, not onto a hole through
    /// to nothing (<c>cave-generation-spec</c> §3.2).
    /// </summary>
    /// <remarks>
    /// Also the first test in the suite that proves the wall layer carries
    /// anything a player will ever see. Before carving, every wall tile in the
    /// world sat behind a block; VOID-057 could only confirm the layer was
    /// populated by counting cells.
    /// </remarks>
    [Fact]
    public void CarvingClearsBlocksAndLeavesWallsStanding()
    {
        (GenerationContext context, LayerBoundaries bounds) = World(20260901);

        int carvedWithWall = 0;

        for (int chunkY = 4; chunkY < 10 && carvedWithWall < 50; chunkY++)
        {
            Chunk chunk = TerrainMaterializer.MaterializeChunk(context, bounds, 12, chunkY);

            for (int localY = 0; localY < Chunk.Height; localY++)
            {
                for (int localX = 0; localX < Chunk.Width; localX++)
                {
                    int worldX = (12 * Chunk.Width) + localX;
                    int worldY = (chunkY * Chunk.Height) + localY;

                    if (!context.CaveNetwork.IsCarved(worldX, worldY))
                    {
                        continue;
                    }

                    Tile tile = chunk[localX, localY];
                    Assert.True(tile.IsAir, $"Carved tile ({worldX}, {worldY}) is still solid.");

                    if (tile.WallId != ContentIds.NoWall)
                    {
                        carvedWithWall++;
                    }
                }
            }
        }

        Assert.True(
            carvedWithWall > 0,
            "No carved tile anywhere kept its wall, so tunnels open onto nothing.");
    }

    /// <summary>
    /// Worms stay inside the world. A stamp outside it is wasted work at best,
    /// and at worst indexes a chunk that does not exist.
    /// </summary>
    [Fact]
    public void CarvingStaysInsideTheWorld()
    {
        (GenerationContext context, _) = World(777);

        int width = context.SizePreset.WidthTiles;
        int height = context.SizePreset.HeightTiles;

        // Sampled on a grid rather than exhaustively: 5 million tiles per seed is
        // not what a unit test is for, and a worm that escaped would escape by
        // hundreds of tiles, not by one.
        for (int x = -200; x < width + 200; x += 7)
        {
            for (int y = -200; y < height + 200; y += 7)
            {
                if (x >= 0 && x < width && y >= 0 && y < height)
                {
                    continue;
                }

                Assert.False(
                    context.CaveNetwork.IsCarved(x, y),
                    $"Tile ({x}, {y}) is outside the world but was carved.");
            }
        }
    }

    /// <summary>
    /// Layer densities are honoured: the deep layer asks for twice the worms of
    /// the underground and must come out more open. Catches a carver that ignores
    /// its per-layer config, which would otherwise look like plausible caves.
    /// </summary>
    [Fact]
    public void DeepIsMoreOpenThanUnderground()
    {
        (GenerationContext context, LayerBoundaries bounds) = World(20260901);

        double underground = CarvedFraction(context, bounds.OutsideEnd, bounds.UndergroundEnd);
        double deep = CarvedFraction(context, bounds.UndergroundEnd, bounds.DeepEnd);

        Assert.True(underground > 0.0, "The underground layer has no caves at all.");
        Assert.True(
            deep > underground,
            $"Deep is carved {deep:P2} and underground {underground:P2}; deep asks for the denser "
            + "network, so it should be the more open of the two.");
    }

    /// <summary>Fraction of sampled tiles in a row range that fall inside a tunnel.</summary>
    private static double CarvedFraction(GenerationContext context, int topRow, int bottomRow)
    {
        int carved = 0;
        int total = 0;

        for (int y = topRow; y < bottomRow; y += 3)
        {
            for (int x = 0; x < context.SizePreset.WidthTiles; x += 3)
            {
                total++;
                if (context.CaveNetwork.IsCarved(x, y))
                {
                    carved++;
                }
            }
        }

        return total == 0 ? 0.0 : (double)carved / total;
    }

    /// <summary>
    /// A world type with no <c>caves</c> block generates solid, exactly as every
    /// world did before carving existed. Absence has to stay meaningful, both so
    /// the field can be introduced to an existing world type without changing it
    /// and because a sealed slab is a legitimate thing for a portal world to be.
    /// </summary>
    [Fact]
    public void NoCavesBlockLeavesTheWorldSolid()
    {
        Assert.Equal(0, CaveNetwork.Empty.StampCount);
        Assert.False(CaveNetwork.Empty.IsCarved(0, 0));
        Assert.False(CaveNetwork.Empty.IsCarved(1234, 5678));
    }

    /// <summary>
    /// Reading the network before the cave phase has run throws rather than
    /// returning an empty one. Silence here would materialise a solid world and
    /// look exactly like caves that were configured but did not fire.
    /// </summary>
    [Fact]
    public void ReadingTheNetworkBeforeCarvingThrows()
    {
        GenerationContext context = new(ContentPaths.All(), "void:home", 1, "small");

        Assert.Throws<InvalidOperationException>(() => context.CaveNetwork);
    }
}
