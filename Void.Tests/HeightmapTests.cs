using System;
using System.Collections.Generic;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// VOID-047 acceptance tests for Phase 1 step 2, the surface heightmap.
///
/// <para>The four properties guarded here are the ones later phases build on
/// without re-checking: a seed reproduces the surface exactly, every column sits
/// inside the Outside layer, adjacent columns never form a cliff, and the shape
/// is driven by JSON rather than by constants in code.</para>
/// </summary>
public class HeightmapTests
{
    /// <summary>Fixed world id; generation takes identity as an input so runs can be compared.</summary>
    private static readonly Guid TestWorldId = new("00000000-0000-0000-0000-0000000000bb");

    /// <summary>Serves one hand-written world-type document, as the real loader would see it.</summary>
    private sealed class InMemoryContentSource : IContentSource
    {
        /// <summary>The single document served, verbatim.</summary>
        private readonly string _json;

        /// <summary>Wraps one JSON body as a content source.</summary>
        public InMemoryContentSource(string json) => _json = json;

        /// <inheritdoc/>
        public string Description => "in-memory world type source";

        /// <inheritdoc/>
        public IEnumerable<ContentDocument> ReadAll() =>
            [new ContentDocument("test_world_types.json", _json)];
    }

    /// <summary>
    /// A world type whose heightmap block a test can vary one field of.
    /// Everything unnamed is valid, so a failure blames the field under test.
    /// </summary>
    private static string WorldTypeJson(
        int octaves = 4,
        double frequency = 1.0 / 512.0,
        double topFraction = 0.45,
        double bottomFraction = 0.80,
        int maxColumnDelta = 3) =>
        $$"""
        {
          "id": "test:world",
          "display_name": "Test World",
          "layer_proportions": {
            "outside": 0.30, "underground": 0.25, "deep": 0.30, "void": 0.15
          },
          "heightmap": {
            "octaves": {{octaves}},
            "frequency": {{frequency}},
            "lacunarity": 2.0,
            "persistence": 0.5,
            "surface_top_fraction": {{topFraction}},
            "surface_bottom_fraction": {{bottomFraction}},
            "max_column_delta": {{maxColumnDelta}}
          },
          "size_preset": "medium",
          "size_presets": [{ "id": "medium", "width_tiles": 800, "height_tiles": 1800 }]
        }
        """;

    /// <summary>
    /// The shipped content with its world types swapped for a hand-written one,
    /// so a test can change a JSON field and generate against it. Everything
    /// else is the real shipped registries: the point is to vary one data file,
    /// not to build a parallel content set.
    /// </summary>
    private static GameContent ContentWithWorldType(string json)
    {
        GameContent shipped = ContentPaths.All();
        Registry<WorldTypeDefinition> worldTypes =
            WorldTypeRegistryLoader.Load(new InMemoryContentSource(json));

        return new GameContent(
            shipped.Blocks, shipped.Walls, shipped.Items, shipped.LootTables,
            shipped.Enemies, shipped.Biomes, shipped.Prefabs, worldTypes);
    }

    /// <summary>Generates the shipped home world's heightmap at a named size preset.</summary>
    private static Heightmap GenerateHome(long seed, string? sizePreset = null)
    {
        GenerationContext context = new(ContentPaths.All(), "void:home", seed, sizePreset);
        WorldGenerator.Generate(context, TestWorldId);
        return context.Heightmap;
    }

    /// <summary>
    /// The core determinism guarantee (CLAUDE.md) applied to terrain: one seed,
    /// one surface. If this goes red, two players on the same seed — or one
    /// player before and after a reload — stand on different ground.
    /// </summary>
    [Fact]
    public void SameSeedProducesAnIdenticalHeightmap()
    {
        Assert.Equal(GenerateHome(seed: -4242).ToArray(), GenerateHome(seed: -4242).ToArray());
    }

    /// <summary>
    /// A different seed must actually reach the noise field. Guards the classic
    /// wiring bug where the sub-stream is derived from a constant and every
    /// world ships the same hills.
    /// </summary>
    [Fact]
    public void DifferentSeedsProduceDifferentHeightmaps()
    {
        Assert.NotEqual(GenerateHome(seed: 1).ToArray(), GenerateHome(seed: 2).ToArray());
    }

    /// <summary>
    /// Every column must sit inside the Outside layer, at every size preset,
    /// with sky left above it — spec §4.1 requires the Outside layer to contain
    /// the sky. A surface that wandered into the Underground layer would bury
    /// the spawn point and put grass on stone.
    /// </summary>
    [Theory]
    [InlineData("small", 1200)]
    [InlineData("medium", 1800)]
    [InlineData("large", 2400)]
    public void EveryColumnStaysInsideTheOutsideLayer(string sizePreset, int heightTiles)
    {
        LayerBoundaries boundaries = LayerBoundaryCalculator.Compute(
            heightTiles, ContentPaths.WorldTypes()["void:home"].LayerProportions);
        Heightmap heightmap = GenerateHome(seed: 99, sizePreset);

        Assert.True(heightmap.Band.MinRow >= 1, "No sky row above the highest possible surface.");
        Assert.True(heightmap.Band.MaxRow < boundaries.OutsideEnd, "Band reaches the Underground layer.");

        for (int x = 0; x < heightmap.Width; x++)
        {
            Assert.InRange(heightmap[x], heightmap.Band.MinRow, heightmap.Band.MaxRow);
        }
    }

    /// <summary>
    /// The bounded-slope guarantee. Later phases (caves, structures, spawn
    /// placement) assume walkable ground; a single-column 200-row cliff is
    /// terrain they would each have to paper over. The bound comes from the
    /// deterministic left-to-right limiter in
    /// <see cref="HeightmapGenerator"/>, not from the noise happening to be
    /// smooth, so it holds for any config that loads.
    /// </summary>
    [Fact]
    public void AdjacentColumnsNeverDifferByMoreThanTheConfiguredCap()
    {
        int cap = ContentPaths.WorldTypes()["void:home"].Heightmap.MaxColumnDelta;
        Heightmap heightmap = GenerateHome(seed: 777);

        for (int x = 1; x < heightmap.Width; x++)
        {
            Assert.True(
                Math.Abs(heightmap[x] - heightmap[x - 1]) <= cap,
                $"Column {x} jumps {Math.Abs(heightmap[x] - heightmap[x - 1])} rows, cap is {cap}.");
        }
    }

    /// <summary>
    /// Even a deliberately violent octave stack cannot break the slope cap: the
    /// limiter is what guarantees it. This is the test that would go red if
    /// someone replaced the limiter with "the noise is smooth enough".
    /// </summary>
    [Fact]
    public void SlopeCapHoldsForAnAggressivelyHighFrequencyConfig()
    {
        GenerationContext context = new(
            ContentWithWorldType(WorldTypeJson(octaves: 8, frequency: 0.5)), "test:world", 5);
        WorldGenerator.Generate(context, TestWorldId);
        Heightmap heightmap = context.Heightmap;

        for (int x = 1; x < heightmap.Width; x++)
        {
            Assert.True(Math.Abs(heightmap[x] - heightmap[x - 1]) <= 3);
        }
    }

    /// <summary>
    /// The data-driven requirement: changing an octave parameter in JSON must
    /// change the terrain with no code change. If this goes red, a tuning value
    /// has been hardcoded and world shape is no longer content.
    /// </summary>
    [Fact]
    public void ChangingAnOctaveParameterInJsonMovesTheTerrain()
    {
        int[] baseline = GenerateWithConfig(WorldTypeJson());
        int[] coarser = GenerateWithConfig(WorldTypeJson(frequency: 1.0 / 64.0));
        int[] fewerOctaves = GenerateWithConfig(WorldTypeJson(octaves: 1));

        Assert.NotEqual(baseline, coarser);
        Assert.NotEqual(baseline, fewerOctaves);
    }

    /// <summary>
    /// Band fractions are content too: narrowing them must narrow the terrain,
    /// which is what lets one world type have a token sky and another a vast one.
    /// </summary>
    [Fact]
    public void BandFractionsInJsonDecideWhereTheSurfaceSits()
    {
        GenerationContext context = new(
            ContentWithWorldType(WorldTypeJson(topFraction: 0.10, bottomFraction: 0.20)),
            "test:world", 11);
        WorldGenerator.Generate(context, TestWorldId);

        // Outside layer is 540 rows at 1800 height, so 0.10-0.20 is rows 54-108.
        Assert.Equal(new SurfaceBand(54, 108), context.Heightmap.Band);
    }

    /// <summary>
    /// Phase output must fail loudly when read out of order. Returning null or
    /// an empty map would let a later phase generate against a flat
    /// world-of-zeros and produce a world that looks generated and is wrong.
    /// </summary>
    [Fact]
    public void ReadingTheHeightmapBeforeItIsGeneratedThrows()
    {
        GenerationContext context = new(ContentPaths.All(), "void:home", 3);

        Assert.Throws<InvalidOperationException>(() => context.Heightmap);
    }

    /// <summary>
    /// Set-once, by the owning phase. A second write means two phases each think
    /// they own the surface — an ordering bug that would silently discard the
    /// first phase's output.
    /// </summary>
    [Fact]
    public void SettingTheHeightmapTwiceThrows()
    {
        GenerationContext context = new(ContentPaths.All(), "void:home", 3);
        WorldGenerator.Generate(context, TestWorldId);

        Assert.Throws<InvalidOperationException>(
            () => context.SetHeightmap(context.Heightmap));
    }

    /// <summary>
    /// Bad heightmap config must be fatal at boot, not at the first generation.
    /// A band that leaves no sky, or an octave count of zero, parses perfectly —
    /// which is exactly why the loader has to check it.
    /// </summary>
    [Theory]
    [InlineData(0, 0.45, 0.80)]    // zero octaves
    [InlineData(4, 0.80, 0.45)]    // band bounds inverted
    [InlineData(4, 0.45, 1.50)]    // band runs past the Outside layer
    [InlineData(4, 0.4500, 0.4501)] // band too thin to shape terrain in
    public void InvalidHeightmapConfigIsRejectedAtLoad(int octaves, double top, double bottom)
    {
        Assert.Throws<ContentLoadException>(
            () => WorldTypeRegistryLoader.Load(
                new InMemoryContentSource(
                    WorldTypeJson(octaves: octaves, topFraction: top, bottomFraction: bottom))));
    }

    /// <summary>Generates the test world type's heightmap from one JSON body.</summary>
    private static int[] GenerateWithConfig(string json)
    {
        GenerationContext context = new(ContentWithWorldType(json), "test:world", 2024);
        WorldGenerator.Generate(context, TestWorldId);
        return context.Heightmap.ToArray();
    }
}
