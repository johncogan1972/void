using System;
using System.Collections.Generic;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// VOID-046 acceptance tests for the world-type registry: the config that
/// decides a world's height split and available sizes.
///
/// <para>Everything checked here is arithmetic that JSON parsing cannot catch.
/// A world type that parses but has bad proportions produces a world that
/// generates successfully and is silently wrong — the whole reason
/// <see cref="WorldTypeRegistryLoader"/> exists.</para>
/// </summary>
public class WorldTypeDefinitionTests
{
    /// <summary>Serves one hand-written world-type document.</summary>
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
    /// One world type, with the proportions and presets a test wants to break.
    /// Everything not named is valid, so a failing load blames exactly the field
    /// under test.
    /// </summary>
    private static string WorldTypeJson(
        double outside = 0.30,
        double underground = 0.25,
        double deep = 0.30,
        double voidLayer = 0.15,
        string defaultPreset = "medium",
        string presets = """{ "id": "medium", "width_tiles": 6400, "height_tiles": 1800 }""") =>
        $$"""
        {
          "id": "test:world",
          "display_name": "Test World",
          "layer_proportions": {
            "outside": {{outside}},
            "underground": {{underground}},
            "deep": {{deep}},
            "void": {{voidLayer}}
          },
          "size_preset": "{{defaultPreset}}",
          "size_presets": [{{presets}}]
        }
        """;

    /// <summary>Loads one hand-written world type through the real loader.</summary>
    public static Registry<WorldTypeDefinition> Load(string json) =>
        WorldTypeRegistryLoader.Load(new InMemoryContentSource(json));

    /// <summary>
    /// The shipped home world must still carry the spec §4 defaults. If someone
    /// edits the data file, every existing home world's layer boundaries move —
    /// this is the test that makes that a deliberate act.
    /// </summary>
    [Fact]
    public void ShippedHomeWorldTypeCarriesTheSpecDefaultProportions()
    {
        WorldTypeDefinition home = ContentPaths.WorldTypes().Get("void:home");

        Assert.Equal(0.30, home.LayerProportions.Outside, 10);
        Assert.Equal(0.25, home.LayerProportions.Underground, 10);
        Assert.Equal(0.30, home.LayerProportions.Deep, 10);
        Assert.Equal(0.15, home.LayerProportions.VoidLayer, 10);
        Assert.Equal("medium", home.SizePreset);
    }

    /// <summary>
    /// Small and Large must already be data, not a future code change: MVP
    /// generates Medium only, but the sizes have to be selectable without
    /// touching C#.
    /// </summary>
    [Fact]
    public void ShippedHomeWorldTypeDeclaresAllThreeSizePresets()
    {
        WorldTypeDefinition home = ContentPaths.WorldTypes().Get("void:home");

        Assert.Equal((4200, 1200), Extent(home, "small"));
        Assert.Equal((6400, 1800), Extent(home, "medium"));
        Assert.Equal((8400, 2400), Extent(home, "large"));
    }

    /// <summary>Tile extents of one named preset, for compact assertions.</summary>
    private static (int Width, int Height) Extent(WorldTypeDefinition worldType, string presetId)
    {
        WorldSizePreset preset = Assert.IsType<WorldSizePreset>(worldType.FindSizePreset(presetId));
        return (preset.WidthTiles, preset.HeightTiles);
    }

    /// <summary>
    /// Proportions that do not sum to 1 leave rows in no layer or in two. Left
    /// unchecked, generation would run happily and place nothing in the missing
    /// band, which no later stage reports.
    /// </summary>
    [Fact]
    public void ProportionsThatDoNotSumToOneFailLoudly()
    {
        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => Load(WorldTypeJson(deep: 0.20)));

        Assert.Contains("test:world", ex.Message, StringComparison.Ordinal);
        Assert.Contains("sum", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sum check runs on doubles, and 0.3 has no exact binary form. A strict
    /// <c>== 1.0</c> would reject the shipped defaults, so the tolerance must
    /// accept ordinary decimal fractions — including a set whose exact sum is
    /// one ulp short of 1.
    /// </summary>
    [Fact]
    public void ProportionsSummingToOneOnlyWithinToleranceAreAccepted()
    {
        Registry<WorldTypeDefinition> loaded =
            Load(WorldTypeJson(outside: 0.1, underground: 0.2, deep: 0.3, voidLayer: 0.4));

        Assert.True(loaded.Contains("test:world"));
    }

    /// <summary>
    /// A proportion small enough to floor to zero rows squashes a whole layer.
    /// It must fail at content load, not become a world with no sky.
    /// </summary>
    [Fact]
    public void ProportionsProducingAZeroHeightLayerFailLoudly()
    {
        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => Load(WorldTypeJson(outside: 0.0001, underground: 0.5499)));

        Assert.Contains("zero rows", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The zero-height check depends on height, so it must run per preset: a
    /// split that is fine on Large can vanish a layer on Small, and that must
    /// surface at boot rather than the first time someone picks Small.
    /// </summary>
    [Fact]
    public void ZeroHeightLayerIsDetectedAtTheSmallestDeclaredPreset()
    {
        // 0.0005 of 2400 rows is 1 row (fine); of 1200 rows it is 0 (not).
        const string BothPresets = """
            { "id": "large", "width_tiles": 8400, "height_tiles": 2400 },
            { "id": "small", "width_tiles": 4200, "height_tiles": 1200 }
            """;

        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => Load(WorldTypeJson(
                outside: 0.0005, underground: 0.5495, defaultPreset: "large", presets: BothPresets)));

        Assert.Contains("'small'", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A default size preset naming nothing means the world type cannot be
    /// generated without an explicit size — a boot failure is far cheaper than
    /// discovering it at world creation.
    /// </summary>
    [Fact]
    public void DefaultSizePresetMustNameADeclaredPreset()
    {
        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => Load(WorldTypeJson(defaultPreset: "enormous")));

        Assert.Contains("enormous", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// World types name no ids in other registries, so they must not be marked
    /// <see cref="ICrossRegistryValidated"/> — that marker exists to force a
    /// two-registry loader, and claiming it here would be a lie about what the
    /// type needs.
    /// </summary>
    [Fact]
    public void WorldTypeDefinitionIsNotCrossRegistryValidated()
    {
        Assert.False(typeof(ICrossRegistryValidated).IsAssignableFrom(typeof(WorldTypeDefinition)));
    }
}
