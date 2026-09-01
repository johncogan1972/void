using System;
using System.Collections.Generic;
using System.Linq;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// VOID-060 acceptance tests for the interleaved band between two surface
/// biomes.
///
/// <para>What breaks in the real game if these go red: a biome boundary becomes
/// a straight vertical cut running the full height of the world — the artefact
/// the VOID-057 viewer surfaced and this ticket exists to remove. The properties
/// guarded here are the ones that make the band read as a border rather than as
/// a seam: it exists at all, it is not the same width everywhere, it never
/// invents a third biome, and it does not disturb the column-level rules that
/// earlier tickets rely on.</para>
/// </summary>
public class BiomeTransitionTests
{
    /// <summary>Fixed world id; generation takes identity as an input so runs compare.</summary>
    private static readonly Guid TestWorldId = new("00000000-0000-0000-0000-0000000000cc");

    /// <summary>Generates the shipped home world at a seed and hands back its biome map.</summary>
    private static (BiomeMap Map, GenerationContext Context) World(long seed)
    {
        GenerationContext context = new(ContentPaths.All(), "void:home", seed, "small");
        WorldGenerator.Generate(context, TestWorldId);
        return (context.BiomeMap, context);
    }

    /// <summary>Start column of every internal boundary, left to right.</summary>
    private static List<int> Boundaries(BiomeMap map)
    {
        List<int> boundaries = new();
        for (int x = 1; x < map.Count; x++)
        {
            if (!string.Equals(map[x], map[x - 1], StringComparison.Ordinal))
            {
                boundaries.Add(x);
            }
        }

        return boundaries;
    }

    /// <summary>
    /// Every boundary in the shipped world actually gets a band. A boundary with
    /// no band is the old hard seam, so this is the regression test for the
    /// ticket itself.
    /// </summary>
    [Fact]
    public void EveryBoundaryInTheShippedWorldIsBlended()
    {
        (BiomeMap map, _) = World(20260901);
        List<int> boundaries = Boundaries(map);

        Assert.NotEmpty(boundaries);

        foreach (int boundary in boundaries)
        {
            Assert.NotNull(map.BlendBiomeAt(boundary));
            Assert.True(
                map.BlendWeightAt(boundary) > 0.0,
                $"Boundary at column {boundary} has no blend weight, so it is still a hard seam.");
        }
    }

    /// <summary>
    /// Bands are not all the same width. A single width would trade one uniform
    /// artefact for another — every border in the world looking like the same
    /// border — which is the specific thing this ticket was asked to avoid.
    /// </summary>
    [Fact]
    public void BandWidthsVaryBetweenBoundaries()
    {
        // Two seeds pooled: a small world has only a handful of boundaries, and
        // the claim is about the mechanism rather than about one world.
        List<int> widths = new();

        foreach (long seed in new long[] { 20260901, 777 })
        {
            (BiomeMap map, _) = World(seed);

            foreach (int boundary in Boundaries(map))
            {
                int width = 0;
                while (boundary + width + 1 < map.Count
                    && map.BlendBiomeAt(boundary + width + 1) is not null)
                {
                    width++;
                }

                widths.Add(width);
            }
        }

        Assert.True(widths.Count >= 4, "Too few boundaries to say anything about variety.");
        Assert.True(
            widths.Distinct().Count() > 1,
            $"Every band is {widths[0]} columns wide; the width is supposed to be drawn per boundary.");
    }

    /// <summary>
    /// A blended column mixes exactly the two biomes that meet there, never a
    /// third. Getting this wrong would scatter an unrelated biome's palette
    /// through a border — terrain that still looks generated, which is the kind
    /// of failure that survives to a player.
    /// </summary>
    [Fact]
    public void ABlendedColumnOnlyEverMixesItsTwoNeighbours()
    {
        (BiomeMap map, _) = World(20260901);

        foreach (int boundary in Boundaries(map))
        {
            string left = map[boundary - 1];
            string right = map[boundary];

            for (int x = 0; x < map.Count; x++)
            {
                if (map.BlendBiomeAt(x) is not string blend)
                {
                    continue;
                }

                // Only inspect columns belonging to this boundary's band.
                if (Math.Abs(x - boundary) > 64)
                {
                    continue;
                }

                Assert.True(
                    string.Equals(blend, left, StringComparison.Ordinal)
                        || string.Equals(blend, right, StringComparison.Ordinal),
                    $"Column {x} blends towards '{blend}', which is neither '{left}' nor '{right}'.");
            }
        }
    }

    /// <summary>
    /// The weight peaks at the boundary and never exceeds a half-and-half mix. A
    /// weight above 0.5 would mean the column had simply become the other biome —
    /// a boundary in a different place rather than a blend.
    /// </summary>
    [Fact]
    public void WeightPeaksAtTheBoundaryAndNeverExceedsHalf()
    {
        (BiomeMap map, _) = World(20260901);

        for (int x = 0; x < map.Count; x++)
        {
            Assert.InRange(map.BlendWeightAt(x), 0.0, 0.5);
        }

        foreach (int boundary in Boundaries(map))
        {
            double atBoundary = map.BlendWeightAt(boundary);

            // Compare against a column near the band's outer edge, which must be
            // weaker; equal weights everywhere would be a step, not a gradient.
            int edge = Math.Min(map.Count - 1, boundary + 40);
            if (map.BlendBiomeAt(edge) is null)
            {
                Assert.True(atBoundary > 0.0);
                continue;
            }

            Assert.True(
                atBoundary >= map.BlendWeightAt(edge),
                $"Boundary column {boundary} is mixed more weakly than the edge of its band.");
        }
    }

    /// <summary>
    /// The per-tile choice varies down a column as well as across. Without the
    /// row in the hash, every tile in a column would decide the same way and the
    /// band would be full-height stripes — a seam made of wider seams, which is
    /// not what "blended" means.
    /// </summary>
    [Fact]
    public void TheInterleaveVariesVerticallyNotJustHorizontally()
    {
        (BiomeMap map, GenerationContext context) = World(20260901);

        int boundary = Boundaries(map)[0];
        int surface = context.Heightmap[boundary];

        // A column right on the boundary is the most strongly mixed, so if any
        // column varies down its length it is this one.
        HashSet<bool> choices = new();
        for (int y = surface; y < surface + 200; y++)
        {
            choices.Add(map.TakesBlendAt(boundary, y));
        }

        Assert.Equal(2, choices.Count);
    }

    /// <summary>
    /// Blending is part of the seed's identity, and identical across runs. If the
    /// dither drifted, two players on one seed would see different ground at
    /// every border, and a chunk re-materialised after eviction would not match
    /// the one that was there before.
    /// </summary>
    [Fact]
    public void TheInterleaveIsDeterministic()
    {
        (BiomeMap a, _) = World(4242);
        (BiomeMap b, _) = World(4242);

        int boundary = Boundaries(a)[0];

        for (int y = 0; y < 500; y++)
        {
            Assert.Equal(a.TakesBlendAt(boundary, y), b.TakesBlendAt(boundary, y));
        }

        Assert.Equal(a.ToArray(), b.ToArray());
    }

    /// <summary>
    /// Blending happens below the resolution the run-length rule works at, so it
    /// cannot reintroduce the single-column islands that rule exists to remove.
    /// This is the guarantee that let the blend be added without the two features
    /// fighting — the reason the earlier design rejected dithering outright.
    /// </summary>
    [Fact]
    public void BlendingDoesNotBreakTheMinimumRunRule()
    {
        (BiomeMap map, GenerationContext context) = World(20260901);
        int minRun = context.WorldType.BiomeClassification.MinRunColumns;

        int runStart = 0;
        for (int x = 1; x <= map.Count; x++)
        {
            bool endOfRun = x == map.Count
                || !string.Equals(map[x], map[x - 1], StringComparison.Ordinal);

            if (!endOfRun)
            {
                continue;
            }

            // The run starting at column 0 has no left-hand neighbour to merge
            // into, so it is the one run allowed to be short.
            if (runStart > 0)
            {
                Assert.True(
                    x - runStart >= minRun,
                    $"Run at columns {runStart}-{x - 1} is {x - runStart} long, below min_run {minRun}.");
            }

            runStart = x;
        }
    }

    /// <summary>
    /// A world type with no transition block generates hard seams exactly as it
    /// did before this feature existed. Absence has to stay meaningful, or the
    /// field could not be introduced to an existing world type without changing
    /// its terrain.
    /// </summary>
    [Fact]
    public void NoTransitionBlockMeansNoBlending()
    {
        BiomeMap map = new(["a", "a", "b", "b"]);

        for (int x = 0; x < map.Count; x++)
        {
            Assert.Null(map.BlendBiomeAt(x));
            Assert.Equal(0.0, map.BlendWeightAt(x));
            Assert.False(map.TakesBlendAt(x, 0));
            Assert.Equal(map[x], map.BiomeAt(x, 0));
        }
    }

    /// <summary>
    /// An inverted width range is always a typo, so it is refused at load rather
    /// than silently swapped — swapping would generate a world the author did not
    /// ask for and had no way to notice.
    /// </summary>
    [Theory]
    [InlineData(30, 5, "min_columns")]
    [InlineData(-1, 40, "min_columns")]
    public void AnUnusableTransitionRangeIsRejectedAtLoad(int min, int max, string expected)
    {
        string json = $$"""
            {
              "id": "test:world",
              "display_name": "Test World",
              "layer_proportions": {
                "outside": 0.30, "underground": 0.25, "deep": 0.30, "void": 0.15
              },
              "biome_classification": {
                "temperature": { "octaves": 3, "frequency": 0.0004 },
                "humidity": { "octaves": 3, "frequency": 0.0007 },
                "blend_columns": 24,
                "min_run_columns": 16,
                "transition": { "min_columns": {{min}}, "max_columns": {{max}} },
                "rules": [
                  { "biome": "test:biome", "temperature": [0.0, 1.0], "humidity": [0.0, 1.0] }
                ]
              },
              "size_preset": "medium",
              "size_presets": [{ "id": "medium", "width_tiles": 6400, "height_tiles": 1800 }]
            }
            """;

        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => WorldTypeDefinitionTests.Load(json));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }
}
