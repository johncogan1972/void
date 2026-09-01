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
        int maxColumnDelta = 3,
        string? detail = null) =>
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
            "max_column_delta": {{maxColumnDelta}}{{(detail is null ? "" : ",\n    \"detail\": " + detail)}}
          },
        {{WorldTypeDefinitionTests.ClassificationBlock}}
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
    [InlineData(4, -0.10, 0.80)]   // top fraction negative -- must fail like every other out-of-(0,1) fraction
    [InlineData(4, 1.10, 1.50)]    // top fraction >= 1 -- would leave no sky above the surface at all
    [InlineData(4, 0.50, 0.50)]    // top equals bottom exactly -- a single-row band, not the strictly-inverted case above
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

    /// <summary>
    /// max_column_delta is validated at boot the same way octaves are: a value
    /// of 0 parses fine as JSON and would flatten the whole world to a single
    /// row (see <see cref="HeightmapConfig.MaxColumnDelta"/>). The existing
    /// invalid-config theory only varies octaves and band fractions, so this
    /// specific field was never actually exercised at load time.
    /// </summary>
    [Fact]
    public void MaxColumnDeltaOfZeroIsRejectedAtLoad()
    {
        Assert.Throws<ContentLoadException>(
            () => WorldTypeRegistryLoader.Load(
                new InMemoryContentSource(WorldTypeJson(maxColumnDelta: 0))));
    }

    /// <summary>
    /// A band of exactly <see cref="SurfaceBand.MinimumRows"/> must be accepted,
    /// not just rejected one row below it. The shipped theory only proves the
    /// "too thin" side of the boundary; without this, an off-by-one that moved
    /// the cutoff to 9 rows would ship without a single red test.
    /// </summary>
    [Fact]
    public void SurfaceBandOfExactlyTheMinimumRowCountIsAccepted()
    {
        // Outside layer at "medium" is 540 rows (0.30 * 1800). floor(540*0.10)=54,
        // floor(540*0.113)=61, an inclusive span of 8 rows -- SurfaceBand.MinimumRows exactly.
        Registry<WorldTypeDefinition> loaded = WorldTypeRegistryLoader.Load(
            new InMemoryContentSource(WorldTypeJson(topFraction: 0.10, bottomFraction: 0.113)));

        Assert.True(loaded.Contains("test:world"));
    }

    /// <summary>
    /// The heightmap's RNG sub-stream is keyed by a fixed <see cref="GenKeys"/>
    /// constant, never by world identity. If a future change threaded the world
    /// id into the stream derivation, re-loading the same save (same seed, same
    /// world id assigned once at creation) would still match -- but two calls
    /// with the *same seed and different ids*, which is what a "new world,
    /// re-rolled seed" flow can do, would silently diverge. This pins the
    /// contract that only the seed decides the terrain.
    /// </summary>
    [Fact]
    public void HeightmapDoesNotDependOnWorldIdentity()
    {
        GenerationContext contextA = new(ContentPaths.All(), "void:home", 55);
        GenerationContext contextB = new(ContentPaths.All(), "void:home", 55);

        WorldGenerator.Generate(contextA, new Guid("11111111-1111-1111-1111-111111111111"));
        WorldGenerator.Generate(contextB, new Guid("22222222-2222-2222-2222-222222222222"));

        Assert.Equal(contextA.Heightmap.ToArray(), contextB.Heightmap.ToArray());
    }

    /// <summary>
    /// A detail block for <see cref="WorldTypeJson"/>. Defaults are the shape
    /// VOID-061 settled on: a short-wavelength field a few rows tall.
    /// </summary>
    private static string DetailJson(
        double amplitudeRows, int octaves = 2, double frequency = 1.0 / 16.0) =>
        $$"""
        { "octaves": {{octaves}}, "frequency": {{frequency}},
          "lacunarity": 2.0, "persistence": 0.5, "amplitude_rows": {{amplitudeRows}} }
        """;

    /// <summary>Generates against a hand-written world type at a fixed seed.</summary>
    private static Heightmap GenerateWith(string json, long seed = 31337)
    {
        GenerationContext context = new(ContentWithWorldType(json), "test:world", seed);
        WorldGenerator.Generate(context, TestWorldId);
        return context.Heightmap;
    }

    /// <summary>
    /// Surface detail is additive, so omitting the block and asking for zero rows
    /// of it must be the same world. If this goes red, adding the field to a
    /// world type silently regenerates its terrain even when it is switched off —
    /// which would make the block impossible to introduce to an existing world
    /// type without moving everyone's ground.
    /// </summary>
    [Fact]
    public void ZeroAmplitudeDetailIsTheSameSurfaceAsNoDetailBlock()
    {
        Assert.Equal(
            GenerateWith(WorldTypeJson()).ToArray(),
            GenerateWith(WorldTypeJson(detail: DetailJson(0.0))).ToArray());
    }

    /// <summary>
    /// Detail displaces the surface by at most its configured amplitude (plus one
    /// row, which the floor can contribute). This is the guarantee that makes it
    /// a texture rather than a second landscape: it must not quietly reshape the
    /// hills the base octaves authored.
    /// </summary>
    [Fact]
    public void DetailStaysWithinItsConfiguredAmplitude()
    {
        const double amplitude = 4.0;
        int[] plain = GenerateWith(WorldTypeJson()).ToArray();
        int[] textured = GenerateWith(WorldTypeJson(detail: DetailJson(amplitude))).ToArray();

        for (int x = 0; x < plain.Length; x++)
        {
            Assert.True(
                Math.Abs(textured[x] - plain[x]) <= amplitude + 1,
                $"Column {x} moved {Math.Abs(textured[x] - plain[x])} rows, "
                + $"amplitude is {amplitude}.");
        }
    }

    /// <summary>
    /// Detail draws from its own stream, so configuring it cannot change what the
    /// base field produces. Tested by varying only the detail octaves: if the two
    /// shared a stream, the base shape would shift underneath and columns would
    /// move far further than either amplitude allows.
    /// </summary>
    [Fact]
    public void DetailDoesNotDisturbTheBaseField()
    {
        int[] a = GenerateWith(WorldTypeJson(detail: DetailJson(3.0, octaves: 2))).ToArray();
        int[] b = GenerateWith(WorldTypeJson(detail: DetailJson(3.0, octaves: 4))).ToArray();

        // Both are the same base shape with at most 3 rows of different texture,
        // so no column can differ by more than the two amplitudes plus a floor.
        for (int x = 0; x < a.Length; x++)
        {
            Assert.True(Math.Abs(a[x] - b[x]) <= 7, $"Column {x} differs by {Math.Abs(a[x] - b[x])}.");
        }

        // ...and they must not be identical, or the detail stream is not being read.
        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// The slope cap and the band still hold with detail switched on. Detail is
    /// applied before the limiter precisely so it cannot introduce a cliff or
    /// push a column out of the Outside layer; this is what proves the ordering.
    /// </summary>
    [Fact]
    public void DetailRespectsTheBandAndTheSlopeCap()
    {
        Heightmap heightmap = GenerateWith(
            WorldTypeJson(maxColumnDelta: 2, detail: DetailJson(12.0, octaves: 3, frequency: 0.25)));

        for (int x = 0; x < heightmap.Width; x++)
        {
            Assert.InRange(heightmap[x], heightmap.Band.MinRow, heightmap.Band.MaxRow);
        }

        for (int x = 1; x < heightmap.Width; x++)
        {
            Assert.True(
                Math.Abs(heightmap[x] - heightmap[x - 1]) <= 2,
                $"Column {x} jumps {Math.Abs(heightmap[x] - heightmap[x - 1])} rows with detail on.");
        }
    }

    /// <summary>Detail is part of the seed's identity, not a per-run flourish.</summary>
    [Fact]
    public void DetailIsDeterministic()
    {
        string json = WorldTypeJson(detail: DetailJson(4.0));
        Assert.Equal(GenerateWith(json, 99).ToArray(), GenerateWith(json, 99).ToArray());
    }

    /// <summary>
    /// The staircase regression itself (VOID-061). The shipped surface used to be
    /// flat in 81% of columns and to move by at most one row in the rest, so
    /// quantising it produced evenly spaced single-row steps — a visible
    /// staircase. This asserts the shipped world type still produces a surface
    /// with real slope variety, which is the thing a future tuning change could
    /// silently undo.
    /// </summary>
    [Fact]
    public void ShippedSurfaceIsNotAStaircase()
    {
        int[] surface = GenerateHome(seed: 20260901).ToArray();

        int flat = 0;
        int stepped = 0;
        for (int x = 1; x < surface.Length; x++)
        {
            if (surface[x] == surface[x - 1])
            {
                flat++;
            }
            else if (Math.Abs(surface[x] - surface[x - 1]) >= 2)
            {
                stepped++;
            }
        }

        double flatFraction = (double)flat / (surface.Length - 1);

        // Deliberately loose. The exact tuning is terrain design and belongs in
        // JSON; what must not come back is a surface that is flat almost
        // everywhere and can only ever step by one row.
        Assert.True(flatFraction < 0.75, $"Surface is flat in {flatFraction:P1} of columns.");
        Assert.True(stepped > 0, "No column anywhere steps by more than one row.");
    }

    /// <summary>
    /// A negative amplitude is always a typo — it generates the same terrain as
    /// its positive counterpart with the field mirrored — so it is refused at
    /// load rather than silently accepted.
    /// </summary>
    [Fact]
    public void NegativeDetailAmplitudeIsRejectedAtLoad()
    {
        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => WorldTypeRegistryLoader.Load(
                new InMemoryContentSource(WorldTypeJson(detail: DetailJson(-1.0)))));

        Assert.Contains("amplitude_rows", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bad detail octave stack is refused at load with a message naming the
    /// world type, the same way the base stack is — one definition of a valid
    /// stack, and an error an author can act on.
    /// </summary>
    [Fact]
    public void InvalidDetailOctavesAreRejectedAtLoad()
    {
        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => WorldTypeRegistryLoader.Load(
                new InMemoryContentSource(WorldTypeJson(detail: DetailJson(4.0, octaves: 0)))));

        Assert.Contains("test:world", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The per-biome roughness of VOID-061, measured on a real generated world
    /// rather than on the config that asked for it.
    /// </summary>
    /// <remarks>
    /// This is the test that proves the wiring, and it is deliberately
    /// end-to-end: the biome map has to be generated before the heightmap, the
    /// heightmap has to look each column's biome up, and each biome has to get
    /// its own field. Break any one of those and every column generates at the
    /// world type's default instead — terrain that still looks generated, which
    /// is exactly the kind of failure a config assertion would not catch.
    ///
    /// <para>Measured as the fraction of a biome's columns that are flat.
    /// Frostreach asks for more amplitude and an extra octave than Meadow, so it
    /// must come out visibly less flat over the world.</para>
    /// </remarks>
    [Fact]
    public void FrostreachGeneratesRougherGroundThanMeadow()
    {
        GenerationContext context = new(ContentPaths.All(), "void:home", 20260901, "small");
        WorldGenerator.Generate(context, TestWorldId);

        Heightmap surface = context.Heightmap;
        BiomeMap biomes = context.BiomeMap;

        Dictionary<string, int> flat = new(StringComparer.Ordinal);
        Dictionary<string, int> total = new(StringComparer.Ordinal);

        // Only interior columns of a biome run count: a column whose neighbour
        // belongs to another biome is measuring the boundary, not the biome.
        for (int x = 1; x < surface.Width - 1; x++)
        {
            string biome = biomes[x];
            if (biomes[x - 1] != biome || biomes[x + 1] != biome)
            {
                continue;
            }

            total[biome] = total.GetValueOrDefault(biome) + 1;
            if (surface[x] == surface[x - 1])
            {
                flat[biome] = flat.GetValueOrDefault(biome) + 1;
            }
        }

        // The seed is fixed, so this is a statement about a specific world; both
        // biomes are present in it and each covers enough ground to measure.
        Assert.True(total.GetValueOrDefault("void:meadow") > 200, "Too little meadow to measure.");
        Assert.True(total.GetValueOrDefault("void:frostreach") > 200, "Too little Frostreach to measure.");

        double meadowFlat = (double)flat.GetValueOrDefault("void:meadow") / total["void:meadow"];
        double frostFlat = (double)flat.GetValueOrDefault("void:frostreach") / total["void:frostreach"];

        Assert.True(
            frostFlat < meadowFlat,
            $"Frostreach is flat in {frostFlat:P1} of its columns and meadow in {meadowFlat:P1}; "
            + "Frostreach asks for the rougher surface, so it should be the less flat one.");
    }
}
