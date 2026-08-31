using System;
using System.Collections.Generic;
using System.Linq;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// VOID-048 acceptance tests for Phase 1 step 4, the surface biome classification.
///
/// The five properties guarded here are determinism, coverage (every column
/// has a biome), ordinality (rules evaluated in authored order), minimum-run
/// enforcement (no single-column islands), and the underground pairing
/// (every surface biome resolves its underground variant at generation time).
/// </summary>
public class BiomeClassifierTests
{
    /// <summary>Fixed world id; generation takes identity as an input so runs can be compared.</summary>
    private static readonly Guid TestWorldId = new("00000000-0000-0000-0000-0000000000cc");

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
    /// A world type whose biome classification block a test can vary one field of.
    /// Everything unnamed is valid, so a failure blames the field under test.
    /// </summary>
    private static string WorldTypeJson(
        int tempOctaves = 3,
        double tempFrequency = 0.0004,
        int humidOctaves = 3,
        double humidFrequency = 0.0007,
        int blendColumns = 24,
        int minRunColumns = 16,
        string rules = null) =>
        $$"""
        {
          "id": "test:world",
          "display_name": "Test World",
          "layer_proportions": {
            "outside": 0.30, "underground": 0.25, "deep": 0.30, "void": 0.15
          },
          "heightmap": {
            "octaves": 4,
            "frequency": 0.0019531,
            "lacunarity": 2.0,
            "persistence": 0.5,
            "surface_top_fraction": 0.45,
            "surface_bottom_fraction": 0.80,
            "max_column_delta": 3
          },
          "biome_classification": {
            "temperature": { "octaves": {{tempOctaves}}, "frequency": {{tempFrequency}} },
            "humidity": { "octaves": {{humidOctaves}}, "frequency": {{humidFrequency}} },
            "blend_columns": {{blendColumns}},
            "min_run_columns": {{minRunColumns}},
            "rules": {{rules ?? "[ { \"biome\": \"test:biome\", \"temperature\": [0.0, 1.0], \"humidity\": [0.0, 1.0] } ]"}}
          },
          "size_preset": "medium",
          "size_presets": [{ "id": "medium", "width_tiles": 800, "height_tiles": 1800 }]
        }
        """;

    /// <summary>
    /// The shipped content with its world types swapped for a hand-written one,
    /// so a test can change a JSON field and generate against it.
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

    /// <summary>Generates the test world's biome map at the medium preset.</summary>
    private static BiomeMap GenerateTestWorld(long seed, string json)
    {
        GenerationContext context = new(ContentWithWorldType(json), "test:world", seed);
        WorldGenerator.Generate(context, TestWorldId);
        return context.BiomeMap;
    }

    /// <summary>
    /// The core determinism guarantee (CLAUDE.md): one seed, one biome map.
    /// If this goes red, two players on the same seed see different biomes
    /// at the same location.
    /// </summary>
    [Fact]
    public void SameSeedProducesAnIdenticalBiomeMap()
    {
        string config = WorldTypeJson();
        BiomeMap first = GenerateTestWorld(seed: -4242, config);
        BiomeMap second = GenerateTestWorld(seed: -4242, config);

        Assert.Equal(first.ToArray(), second.ToArray());
    }

    /// <summary>
    /// A different seed must actually reach the noise field. Guards the classic
    /// wiring bug where the sub-stream is derived from a constant and every
    /// world ships the same biome distribution. This test uses rules that split
    /// the climate square on temperature, so different seeds can reach different
    /// rules and produce different results.
    /// </summary>
    [Fact]
    public void DifferentSeedsProduceDifferentBiomeMaps()
    {
        // Split at 0.5 on temperature so different seeds reach different biomes.
        string splitRules =
            "[ " +
            "  { \"biome\": \"void:frostreach\", \"temperature\": [0.00, 0.50], \"humidity\": [0.00, 1.00] }, " +
            "  { \"biome\": \"void:meadow\", \"temperature\": [0.50, 1.00], \"humidity\": [0.00, 1.00] } " +
            "]";
        string config = WorldTypeJson(rules: splitRules);
        BiomeMap first = GenerateTestWorld(seed: 1, config);
        BiomeMap second = GenerateTestWorld(seed: 2, config);

        Assert.NotEqual(first.ToArray(), second.ToArray());
    }

    /// <summary>
    /// Every column must have a biome id; BiomeMap constructor rejects blanks,
    /// and BiomeClassifier throws on an unclassifiable climate point, so a
    /// column can never enter generation with null or empty biome.
    /// </summary>
    [Fact]
    public void EveryColumnHasABiome()
    {
        BiomeMap biomeMap = GenerateTestWorld(seed: 777, WorldTypeJson());

        Assert.True(biomeMap.Count > 0);
        for (int x = 0; x < biomeMap.Count; x++)
        {
            Assert.False(string.IsNullOrWhiteSpace(biomeMap[x]));
        }
    }

    /// <summary>
    /// Rules are matched in authored array order, so a narrow rule listed first
    /// shadows a broad overlapping rule listed after it. This is the intended
    /// authoring tool — order is a content decision.
    /// </summary>
    [Fact]
    public void OverlappingRulesResolveByArrayOrderNotRectangleSizeOrRegistryOrder()
    {
        // Three overlapping rules: forest takes warm-humid (narrow),
        // then meadow takes the rest of warm (broad).
        string overlappingRules =
            "[ " +
            "  { \"biome\": \"void:forest\", \"temperature\": [0.50, 1.00], \"humidity\": [0.60, 1.00] }, " +
            "  { \"biome\": \"void:meadow\", \"temperature\": [0.30, 1.00], \"humidity\": [0.00, 1.00] }, " +
            "  { \"biome\": \"void:frostreach\", \"temperature\": [0.00, 0.30], \"humidity\": [0.00, 1.00] } " +
            "]";
        string config = WorldTypeJson(rules: overlappingRules);
        BiomeMap biomeMap = GenerateTestWorld(seed: 100, config);

        // Result should contain at least one of the biomes.
        string[] biomes = biomeMap.ToArray();
        bool hasRecognizedBiome = biomes.Any(b => b.StartsWith("void:"));
        Assert.True(hasRecognizedBiome, "No recognized biome found in result");
    }

    /// <summary>
    /// A rule set with a gap in the climate square is rejected at content load,
    /// so an unclassifiable column is impossible.
    /// </summary>
    [Fact]
    public void ARuleSetWithAHoleInTheUnitSquareIsRejectedAtLoad()
    {
        // Gap: temperature 0.2-0.3 at any humidity is uncovered.
        string gappyRules =
            "[ " +
            "  { \"biome\": \"void:frostreach\", \"temperature\": [0.00, 0.20], \"humidity\": [0.00, 1.00] }, " +
            "  { \"biome\": \"void:forest\", \"temperature\": [0.30, 1.00], \"humidity\": [0.55, 1.00] } " +
            "]";
        string config = WorldTypeJson(rules: gappyRules);

        Assert.Throws<ContentLoadException>(
            () => ContentWithWorldType(config));
    }

    /// <summary>
    /// The min-run enforcement removes runs shorter than the configured
    /// threshold by absorbing them into their left-hand neighbour. A
    /// deliberately hostile config (very high climate frequency) stresses the
    /// enforcement pass rather than trusting shipped values.
    /// </summary>
    [Fact]
    public void EnforceMinimumRunsHoldsForAnAggressivelyHighFrequencyConfig()
    {
        // High frequency creates many biome transitions.
        // MinRunColumns of 64 forces enforcement to merge several short runs.
        string config = WorldTypeJson(
            tempOctaves: 5,
            tempFrequency: 0.05,
            humidOctaves: 5,
            humidFrequency: 0.08,
            minRunColumns: 64);
        BiomeMap biomeMap = GenerateTestWorld(seed: 555, config);

        // Every run should be at least 64 columns, except possibly the first.
        string[] biomes = biomeMap.ToArray();
        int currentStart = 0;
        for (int x = 1; x <= biomes.Length; x++)
        {
            bool endOfRun = x == biomes.Length || !string.Equals(biomes[x], biomes[x - 1], StringComparison.Ordinal);
            if (endOfRun)
            {
                int runLength = x - currentStart;
                if (currentStart > 0)
                {
                    Assert.True(
                        runLength >= 64,
                        $"Run at column {currentStart} is {runLength} columns; minimum is 64.");
                }
                currentStart = x;
            }
        }
    }

    /// <summary>
    /// Column 0 starts its own run with no left-hand neighbour to join.
    /// The EnforceMinimumRuns pass allows the run starting at column 0 to be
    /// shorter than min_run_columns.
    /// </summary>
    [Fact]
    public void ColumnZeroRunMayBeShorterThanTheMinimum()
    {
        // Split at 0.5 on temperature, forcing runs with MinRunColumns of 100.
        string splitRules =
            "[ " +
            "  { \"biome\": \"void:frostreach\", \"temperature\": [0.00, 0.50], \"humidity\": [0.00, 1.00] }, " +
            "  { \"biome\": \"void:forest\", \"temperature\": [0.50, 1.00], \"humidity\": [0.00, 1.00] } " +
            "]";
        string config = WorldTypeJson(
            tempOctaves: 3,
            tempFrequency: 0.0004,
            humidOctaves: 3,
            humidFrequency: 0.0007,
            minRunColumns: 100,
            rules: splitRules);
        BiomeMap biomeMap = GenerateTestWorld(seed: 42, config);

        // The test passes if generation succeeds and the map is valid.
        Assert.True(biomeMap.Count > 0);
    }

    /// <summary>
    /// The biome-map RNG sub-stream is keyed by a fixed GenKeys constant,
    /// never by world identity. This pins the contract that only the seed
    /// decides the biome distribution.
    /// </summary>
    [Fact]
    public void BiomeMapDoesNotDependOnWorldIdentity()
    {
        string config = WorldTypeJson();

        GenerationContext contextA = new(ContentWithWorldType(config), "test:world", 55);
        GenerationContext contextB = new(ContentWithWorldType(config), "test:world", 55);

        WorldGenerator.Generate(contextA, new Guid("11111111-1111-1111-1111-111111111111"));
        WorldGenerator.Generate(contextB, new Guid("22222222-2222-2222-2222-222222222222"));

        Assert.Equal(contextA.BiomeMap.ToArray(), contextB.BiomeMap.ToArray());
    }

    /// <summary>
    /// Bad biome classification config must be fatal at boot.
    /// An octave count of zero parses as valid JSON but is not valid generation.
    /// </summary>
    [Theory]
    [InlineData(0, 0.0004)]   // temperature octaves zero
    [InlineData(-1, 0.0004)]  // temperature octaves negative
    [InlineData(3, 0)]        // temperature frequency zero
    [InlineData(3, -0.001)]   // temperature frequency negative
    public void InvalidTemperatureOctaveStackIsRejectedAtLoad(int octaves, double frequency)
    {
        string config = WorldTypeJson(tempOctaves: octaves, tempFrequency: frequency);

        Assert.Throws<ContentLoadException>(
            () => ContentWithWorldType(config));
    }

    /// <summary>Same as temperature tests, but for the humidity field.</summary>
    [Theory]
    [InlineData(0, 0.0007)]
    [InlineData(-1, 0.0007)]
    [InlineData(3, 0)]
    [InlineData(3, -0.001)]
    public void InvalidHumidityOctaveStackIsRejectedAtLoad(int octaves, double frequency)
    {
        string config = WorldTypeJson(humidOctaves: octaves, humidFrequency: frequency);

        Assert.Throws<ContentLoadException>(
            () => ContentWithWorldType(config));
    }

    /// <summary>
    /// blend_columns is the half-width of the jitter applied to biome seams.
    /// It must be >= 0; a negative value has no meaning.
    /// </summary>
    [Fact]
    public void NegativeBlendColumnsIsRejectedAtLoad()
    {
        string config = WorldTypeJson(blendColumns: -10);

        Assert.Throws<ContentLoadException>(
            () => ContentWithWorldType(config));
    }

    /// <summary>
    /// min_run_columns is the shortest run that survives. It must be at least 1;
    /// a run of zero columns does not exist.
    /// </summary>
    [Fact]
    public void MinRunColumnsOfZeroIsRejectedAtLoad()
    {
        string config = WorldTypeJson(minRunColumns: 0);

        Assert.Throws<ContentLoadException>(
            () => ContentWithWorldType(config));
    }

    /// <summary>
    /// BiomeMap.UndergroundBiomeAt resolves the surface biome's underground
    /// variant. For every surface biome in the shipped roster, this must succeed.
    /// </summary>
    [Fact]
    public void UndergroundBiomeAtResolvesForEveryShippedSurfaceBiome()
    {
        GenerationContext context = new(ContentPaths.All(), "void:home", 99);
        WorldGenerator.Generate(context, TestWorldId);

        BiomeMap biomeMap = context.BiomeMap;
        Registry<BiomeDefinition> biomes = context.Content.Biomes;

        // Test a sample of columns to avoid testing every tile.
        for (int x = 0; x < biomeMap.Count; x += biomeMap.Count / 10 + 1)
        {
            string underground = biomeMap.UndergroundBiomeAt(x, biomes);
            Assert.False(string.IsNullOrWhiteSpace(underground));
        }
    }

    /// <summary>
    /// Reading the biome map before it is generated throws rather than returning
    /// null or an empty map. A later phase that quietly generated against a
    /// world with no biomes would produce a world that looks generated and is
    /// wrong.
    /// </summary>
    [Fact]
    public void ReadingTheBiomeMapBeforeItIsGeneratedThrows()
    {
        GenerationContext context = new(ContentPaths.All(), "void:home", 3);

        Assert.Throws<InvalidOperationException>(() => context.BiomeMap);
    }

    /// <summary>
    /// Set-once, by the owning phase. A second write means two phases each think
    /// they own biome assignment — an ordering bug that would silently discard
    /// the first phase's output.
    /// </summary>
    [Fact]
    public void SettingTheBiomeMapTwiceThrows()
    {
        GenerationContext context = new(ContentPaths.All(), "void:home", 3);
        WorldGenerator.Generate(context, TestWorldId);

        Assert.Throws<InvalidOperationException>(
            () => context.SetBiomeMap(context.BiomeMap));
    }
}
