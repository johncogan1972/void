using System;
using System.Collections.Generic;
using System.Linq;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// VOID-046 acceptance tests for the generation scaffold: phase ordering,
/// sub-stream keys, and the layer boundaries written into the manifest.
///
/// <para>These guard the two properties every later phase will be built on —
/// that a seed reproduces a world exactly, and that one phase's stream is
/// unaffected by what the other phases do.</para>
/// </summary>
public class WorldGeneratorTests
{
    /// <summary>
    /// Fixed world id used throughout. Generation takes identity as an input
    /// precisely so these tests can compare two runs byte for byte.
    /// </summary>
    private static readonly Guid TestWorldId = new("00000000-0000-0000-0000-0000000000aa");

    /// <summary>Generates the shipped home world at a named size preset.</summary>
    private static WorldManifest GenerateHome(long seed, string? sizePreset = null) =>
        WorldGenerator.Generate(
            new GenerationContext(ContentPaths.All(), "void:home", seed, sizePreset), TestWorldId);

    /// <summary>
    /// Medium's boundaries are stated in world-generation-spec §4 as 0-540 /
    /// 540-990 / 990-1530 / 1530-1800. They are the reference every content
    /// spec's depth figures are written against, so drift here silently moves
    /// every ore tier and biome band in the game.
    /// </summary>
    [Fact]
    public void MediumHomeWorldBoundariesMatchTheSpec()
    {
        WorldManifest manifest = GenerateHome(seed: 1234);

        Assert.Equal(new LayerBoundaries(540, 990, 1530), manifest.LayerBoundaries);
        Assert.Equal(new WorldDimensions(6400, 1800, 100, 29), manifest.Dimensions);
        Assert.Equal("medium", manifest.SizePreset);
    }

    /// <summary>
    /// Chunk counts round up: 1800 rows is 28.125 chunks, and spec §5's
    /// edge-padded count of 29 is what streaming bounds-checks against. Rounding
    /// down would leave the bottom 8 rows of the void in no chunk at all.
    /// </summary>
    [Fact]
    public void ChunkCountsRoundUpToCoverThePartialBottomRow()
    {
        WorldManifest manifest = GenerateHome(seed: 7);

        Assert.Equal(29, manifest.Dimensions.ChunksY);
        Assert.True(manifest.Dimensions.ChunksY * Chunk.Height >= manifest.Dimensions.HeightTiles);
    }

    /// <summary>
    /// The core determinism guarantee (CLAUDE.md): one seed, one world. Compared
    /// as serialised bytes rather than field by field, so a future field that
    /// sneaks in a clock or a fresh Guid is caught by this test rather than by a
    /// player whose reloaded world differs.
    /// </summary>
    [Fact]
    public void SameSeedGeneratesAByteIdenticalManifest()
    {
        Assert.Equal(GenerateHome(seed: -998877).Serialize(), GenerateHome(seed: -998877).Serialize());
    }

    /// <summary>A different seed must actually reach the generator, not be ignored.</summary>
    [Fact]
    public void SeedIsCarriedIntoTheManifestAndIntoTheMasterStream()
    {
        WorldManifest manifest = GenerateHome(seed: -1);
        GenerationContext context = new(ContentPaths.All(), "void:home", -1);

        Assert.Equal(-1, manifest.Seed);
        Assert.Equal(unchecked((ulong)-1L), context.Master.Seed);
    }

    /// <summary>
    /// Phases must be free to derive their streams in any order, because they
    /// will be written, reordered and run in isolation. Deriving the whole key
    /// set in reverse and getting the same draws is what makes that safe — the
    /// alternative, a generator threaded from phase to phase, would move every
    /// ore in the world when someone added a draw to the heightmap.
    /// </summary>
    [Fact]
    public void DerivingSubStreamsInReverseOrderChangesNothing()
    {
        GenerationContext context = new(ContentPaths.All(), "void:home", 42);

        Dictionary<string, ulong[]> forward = new(StringComparer.Ordinal);
        foreach (string key in GenKeys.All)
        {
            forward[key] = Draw(context.Stream(key));
        }

        foreach (string key in GenKeys.All.Reverse())
        {
            Assert.Equal(forward[key], Draw(context.Stream(key)));
        }
    }

    /// <summary>First few draws of a stream, enough to catch a shifted sequence.</summary>
    private static ulong[] Draw(Rng rng) =>
        [rng.NextULong(), rng.NextULong(), rng.NextULong(), rng.NextULong()];

    /// <summary>
    /// Two keys resolving to the same text would make two subsystems share a
    /// stream — they would generate in lockstep, which looks like plausible
    /// output and is never reported.
    /// </summary>
    [Fact]
    public void GenKeysAreAllDistinct()
    {
        Assert.Equal(GenKeys.All.Count, GenKeys.All.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A world type overriding the proportions must move the boundaries with no
    /// code change — that is the entire point of the config being data. Uses the
    /// real loader and the real generator, so nothing about this path is a test
    /// shortcut.
    /// </summary>
    [Fact]
    public void OverridingLayerProportionsMovesTheBoundaries()
    {
        const string SkinnySky = """
            {
              "id": "test:portal_void_heavy",
              "display_name": "Void Heavy",
              "layer_proportions": {
                "outside": 0.10, "underground": 0.20, "deep": 0.30, "void": 0.40
              },
              "biome_classification": {
                "temperature": { "octaves": 3, "frequency": 0.0004 },
                "humidity": { "octaves": 3, "frequency": 0.0007 },
                "blend_columns": 24,
                "min_run_columns": 16,
                "rules": [
                  { "biome": "test:biome", "temperature": [0.0, 1.0], "humidity": [0.0, 1.0] }
                ]
              },
              "size_preset": "medium",
              "size_presets": [{ "id": "medium", "width_tiles": 6400, "height_tiles": 1800 }]
            }
            """;

        GameContent content = WithWorldTypes(WorldTypeDefinitionTests.Load(SkinnySky));
        WorldManifest manifest = WorldGenerator.Generate(
            new GenerationContext(content, "test:portal_void_heavy", 5), TestWorldId);

        Assert.Equal(new LayerBoundaries(180, 540, 1080), manifest.LayerBoundaries);
    }

    /// <summary>
    /// The shipped content with its world-type registry swapped, so an override
    /// test can use the real generator without editing the shipped data file.
    /// </summary>
    private static GameContent WithWorldTypes(Registry<WorldTypeDefinition> worldTypes)
    {
        GameContent shipped = ContentPaths.All();
        return new GameContent(
            shipped.Blocks,
            shipped.Walls,
            shipped.Items,
            shipped.LootTables,
            shipped.Enemies,
            shipped.Biomes,
            shipped.Prefabs,
            worldTypes);
    }

    /// <summary>
    /// Rounding is load-bearing and must be cumulative-then-floor: independent
    /// per-layer rounding on a height that does not divide evenly accumulates
    /// error and can leave a row belonging to no layer. Thirds of 1000 rows is
    /// the case that tells the two rules apart.
    /// </summary>
    [Fact]
    public void BoundariesFloorTheRunningTotalSoTheyNeverDrift()
    {
        LayerProportions thirds = new()
        {
            Outside = 1.0 / 3.0,
            Underground = 1.0 / 3.0,
            Deep = 1.0 / 6.0,
            VoidLayer = 1.0 / 6.0,
        };

        LayerBoundaries b = LayerBoundaryCalculator.Compute(1000, thirds);

        Assert.Equal(new LayerBoundaries(333, 666, 833), b);
    }

    /// <summary>
    /// Phases 2-5 do not exist, so the manifest's spawn and boss fields are
    /// placeholders. This test exists to go red when phase 4 lands and someone
    /// forgets to remove the placeholder — real output must never be row 0 with
    /// an unresolvable prefab id.
    /// </summary>
    [Fact]
    public void SpawnAndBossLairAreExplicitPlaceholdersUntilPhaseFour()
    {
        WorldManifest manifest = GenerateHome(seed: 3);

        Assert.Equal(new TilePosition(0, 0), manifest.PlayerSpawn);
        Assert.Equal(WorldGenerator.UnassignedPrefabId, manifest.MainBossLair.PrefabId);
        Assert.False(ContentPaths.Prefabs().Contains(WorldGenerator.UnassignedPrefabId));
    }

    /// <summary>
    /// Asking for a size or world type that does not exist must throw rather
    /// than fall back: quietly generating Medium when the player chose Large is
    /// only discoverable once the world exists.
    /// </summary>
    [Fact]
    public void UnknownWorldTypeOrSizePresetIsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new GenerationContext(ContentPaths.All(), "void:not_a_world_type", 1));
        Assert.Throws<ArgumentException>(
            () => new GenerationContext(ContentPaths.All(), "void:home", 1, "enormous"));
    }

    /// <summary>
    /// Selecting another declared preset must change the world's extents with no
    /// code change — MVP ships Medium, but Small and Large have to work the day
    /// they are switched on.
    /// </summary>
    [Fact]
    public void SelectingAnotherSizePresetChangesTheWorldExtents()
    {
        WorldManifest small = GenerateHome(seed: 11, sizePreset: "small");

        Assert.Equal(new WorldDimensions(4200, 1200, 66, 19), small.Dimensions);
        Assert.Equal(new LayerBoundaries(360, 660, 1020), small.LayerBoundaries);
    }
}
