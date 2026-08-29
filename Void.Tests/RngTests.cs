using System;
using System.Collections.Generic;
using System.Linq;
using Void;

namespace Void.Tests;

/// <summary>
/// VOID-005 acceptance tests. The golden vectors here are the load-bearing
/// part: see <see cref="GoldenVectorTests"/> for their provenance.
/// </summary>
public class RngTests
{
    private const ulong TestSeed = 0x0123456789ABCDEFUL;

    /// <summary>
    /// Draws <paramref name="count"/> raw values, advancing the generator.
    /// </summary>
    private static ulong[] Take(Rng rng, int count)
    {
        ulong[] result = new ulong[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = rng.NextULong();
        }

        return result;
    }

    /// <summary>
    /// The foundation of world reproducibility: one seed, one sequence, always.
    /// </summary>
    [Fact]
    public void SameSeedProducesIdenticalSequence()
    {
        ulong[] a = Take(new Rng(TestSeed), 64);
        ulong[] b = Take(new Rng(TestSeed), 64);

        Assert.Equal(a, b);
    }

    /// <summary>
    /// Neighbouring and pathological seeds must not collapse onto the same stream,
    /// or distinct worlds would generate identically.
    /// </summary>
    [Theory]
    [InlineData(0UL, 1UL)]
    [InlineData(1UL, 2UL)]
    [InlineData(TestSeed, TestSeed + 1UL)]
    [InlineData(ulong.MaxValue, 0UL)]
    public void DifferentSeedsProduceDifferentSequences(ulong left, ulong right)
    {
        ulong[] a = Take(new Rng(left), 16);
        ulong[] b = Take(new Rng(right), 16);

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// xoshiro cannot recover from an all-zero state — it emits zeros forever. Seed
    /// expansion has to make that state unreachable for every seed, including 0.
    /// </summary>
    [Fact]
    public void AllZeroStateIsAvoided()
    {
        // Sanity: no seed should yield a dead (always-zero) generator.
        foreach (ulong seed in new ulong[] { 0UL, 1UL, ulong.MaxValue, TestSeed })
        {
            Assert.Contains(Take(new Rng(seed), 8), v => v != 0UL);
        }
    }

    // --- sub-streams ---------------------------------------------------------

    /// <summary>
    /// A named sub-stream depends only on parent seed and key.
    ///
    /// Generation phases are derived in whatever order the pipeline runs, so if
    /// creation order leaked into the stream, reordering two phases would silently
    /// change every world.
    /// </summary>
    [Fact]
    public void SubStreamIsIndependentOfCreationOrder()
    {
        Rng rootA = new Rng(TestSeed);
        Rng terrainFirst = rootA.Derive("terrain");
        rootA.Derive("caves");
        rootA.Derive("ore");

        Rng rootB = new Rng(TestSeed);
        rootB.Derive("ore");
        rootB.Derive("caves");
        Rng terrainLast = rootB.Derive("terrain");

        Assert.Equal(Take(terrainFirst, 32), Take(terrainLast, 32));
    }

    /// <summary>
    /// Sibling streams cannot influence each other. Terrain drawing more or fewer
    /// values must never shift what caves produces.
    /// </summary>
    [Fact]
    public void SubStreamIsIndependentOfDrawOrderOfSiblings()
    {
        Rng root = new Rng(TestSeed);
        Rng terrain = root.Derive("terrain");
        Rng caves = root.Derive("caves");

        // Exhaust a sibling heavily before touching the stream under test.
        for (int i = 0; i < 10_000; i++)
        {
            caves.NextULong();
        }

        ulong[] afterSiblingDraws = Take(terrain, 32);
        ulong[] pristine = Take(new Rng(TestSeed).Derive("terrain"), 32);

        Assert.Equal(pristine, afterSiblingDraws);
    }

    /// <summary>
    /// Deriving is a pure read of the parent seed. If it consumed parent state,
    /// adding a new generation phase would change the output of every existing one.
    /// </summary>
    [Fact]
    public void DerivingDoesNotAdvanceTheParent()
    {
        Rng withDerives = new Rng(TestSeed);
        withDerives.Derive("a");
        withDerives.Derive("b");

        Assert.Equal(Take(new Rng(TestSeed), 16), Take(withDerives, 16));
    }

    /// <summary>
    /// Distinct keys give distinct streams, empty key included.
    /// </summary>
    [Fact]
    public void DifferentKeysProduceDifferentSubStreams()
    {
        Rng root = new Rng(TestSeed);

        Assert.NotEqual(Take(root.Derive("terrain"), 16), Take(root.Derive("caves"), 16));
        Assert.NotEqual(Take(root.Derive(""), 16), Take(root.Derive("terrain"), 16));
    }

    /// <summary>
    /// The key alone does not determine the stream — the world seed still has to
    /// reach it, or every world would share terrain.
    /// </summary>
    [Fact]
    public void SameKeyUnderDifferentSeedsDiffers()
    {
        Assert.NotEqual(
            Take(new Rng(1UL).Derive("terrain"), 16),
            Take(new Rng(2UL).Derive("terrain"), 16));
    }

    /// <summary>
    /// Derivation composes: a stream derived from a derived stream is reproducible
    /// to any depth, which is how per-chunk streams hang off per-phase ones.
    /// </summary>
    [Fact]
    public void NestedDerivationIsStable()
    {
        ulong[] a = Take(new Rng(TestSeed).Derive("region").Derive("chunk:3,7"), 16);
        ulong[] b = Take(new Rng(TestSeed).Derive("region").Derive("chunk:3,7"), 16);

        Assert.Equal(a, b);
    }

    /// <summary>
    /// A null key is a programming error, not an unnamed stream.
    /// </summary>
    [Fact]
    public void DeriveRejectsNullKey()
    {
        Rng root = new Rng(TestSeed);
        Assert.Throws<ArgumentNullException>(() => root.Derive(null!));
    }

    // --- range draws ---------------------------------------------------------

    /// <summary>
    /// Range is half-open [min, max): both endpoints of the reachable set occur, and
    /// max itself never does.
    /// </summary>
    [Fact]
    public void NextIntStaysInHalfOpenRange()
    {
        Rng rng = new Rng(TestSeed);
        bool sawMin = false;
        bool sawMaxMinusOne = false;

        for (int i = 0; i < 50_000; i++)
        {
            int v = rng.NextInt(-5, 5);
            Assert.InRange(v, -5, 4);
            sawMin |= v == -5;
            sawMaxMinusOne |= v == 4;
        }

        Assert.True(sawMin);
        Assert.True(sawMaxMinusOne);
    }

    /// <summary>
    /// A one-value range is legal and constant — the degenerate case callers hit at
    /// the edges of a table without special-casing it.
    /// </summary>
    [Fact]
    public void NextIntSingleValueRangeIsConstant()
    {
        Rng rng = new Rng(TestSeed);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(7, rng.NextInt(7, 8));
        }
    }

    /// <summary>
    /// The full int span exercises the width where a naive (max - min) computation
    /// overflows.
    /// </summary>
    [Fact]
    public void NextIntHandlesFullIntSpanWithoutOverflow()
    {
        Rng rng = new Rng(TestSeed);
        for (int i = 0; i < 1_000; i++)
        {
            int v = rng.NextInt(int.MinValue, int.MaxValue);
            Assert.InRange(v, int.MinValue, int.MaxValue - 1);
        }
    }

    /// <summary>
    /// An empty or inverted range has no valid answer, so it throws rather than
    /// inventing one.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(5, 4)]
    public void NextIntRejectsEmptyRange(int min, int max)
    {
        Rng rng = new Rng(TestSeed);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(min, max));
    }

    /// <summary>
    /// Range reduction is part of the deterministic contract, not just the raw draw.
    /// </summary>
    [Fact]
    public void NextIntIsReproducible()
    {
        Rng x = new Rng(TestSeed);
        Rng y = new Rng(TestSeed);
        for (int i = 0; i < 200; i++)
        {
            Assert.Equal(x.NextInt(0, 1000), y.NextInt(0, 1000));
        }
    }

    /// <summary>
    /// Rejects modulo bias.
    ///
    /// Ranges that do not divide 2^64 evenly are where a naive reduction skews
    /// toward low values — which would quietly bias every ore roll and spawn table
    /// in the game rather than failing outright.
    /// </summary>
    [Fact]
    public void NextIntIsUnbiasedAcrossBuckets()
    {
        // A range that does not divide 2^64 evenly is the case where modulo
        // bias would show up; 3, 7 and 1000 are all non-powers of two.
        foreach (int span in new[] { 3, 7, 1000 })
        {
            const int Draws = 300_000;
            int[] counts = new int[span];
            Rng rng = new Rng(TestSeed).Derive($"bias:{span}");

            for (int i = 0; i < Draws; i++)
            {
                counts[rng.NextInt(0, span)]++;
            }

            double expected = (double)Draws / span;
            double chiSquare = counts.Sum(c => (c - expected) * (c - expected) / expected);

            // Generous bound: for span-1 degrees of freedom this is far above
            // any plausible fair-sampling value, but well below the drift a
            // biased modulo reduction would create.
            Assert.True(chiSquare < 5.0 * span, $"span {span} chi-square {chiSquare}");
        }
    }

    // --- doubles and bools ---------------------------------------------------

    /// <summary>
    /// Doubles stay in [0, 1) and average near the middle. Noise functions assume
    /// the upper bound is exclusive.
    /// </summary>
    [Fact]
    public void NextDoubleStaysInUnitInterval()
    {
        Rng rng = new Rng(TestSeed);
        double sum = 0.0;
        const int Draws = 100_000;

        for (int i = 0; i < Draws; i++)
        {
            double v = rng.NextDouble();
            Assert.True(v >= 0.0 && v < 1.0, $"out of range: {v}");
            sum += v;
        }

        Assert.InRange(sum / Draws, 0.49, 0.51);
    }

    /// <summary>
    /// Coin flips are fair and repeatable.
    /// </summary>
    [Fact]
    public void NextBoolIsBalancedAndReproducible()
    {
        Rng a = new Rng(TestSeed);
        Rng b = new Rng(TestSeed);
        int trues = 0;
        const int Draws = 100_000;

        for (int i = 0; i < Draws; i++)
        {
            bool v = a.NextBool();
            Assert.Equal(v, b.NextBool());
            if (v)
            {
                trues++;
            }
        }

        Assert.InRange(trues / (double)Draws, 0.49, 0.51);
    }

    // --- weighted choice -----------------------------------------------------

    /// <summary>
    /// A weighted candidate, used to prove ordering does not affect selection.
    /// </summary>
    private sealed record Entry(string Id, double Weight);

    /// <summary>
    /// Weighted selection sorts its candidates rather than trusting the order it
    /// received them in.
    /// </summary>
    [Fact]
    public void WeightedChoiceIgnoresInsertionOrder()
    {
        Entry[] forward =
        {
            new Entry("copper", 3.0),
            new Entry("iron", 2.0),
            new Entry("gold", 1.0),
            new Entry("adamant", 0.5),
        };
        Entry[] shuffled = { forward[3], forward[1], forward[0], forward[2] };

        Rng a = new Rng(TestSeed).Derive("ore");
        Rng b = new Rng(TestSeed).Derive("ore");

        for (int i = 0; i < 500; i++)
        {
            Assert.Equal(
                a.ChooseWeighted(forward, e => e.Id, e => e.Weight).Id,
                b.ChooseWeighted(shuffled, e => e.Id, e => e.Weight).Id);
        }
    }

    /// <summary>
    /// The determinism rule in practice: a Dictionary enumerates in hash order,
    /// which is not guaranteed stable across runtimes. Selection must sort, or two
    /// players with the same seed could roll different loot.
    /// </summary>
    [Fact]
    public void WeightedChoiceIgnoresDictionaryHashOrder()
    {
        Dictionary<string, double> forward = new()
        {
            ["copper"] = 3.0,
            ["iron"] = 2.0,
            ["gold"] = 1.0,
        };
        Dictionary<string, double> reverse = new()
        {
            ["gold"] = 1.0,
            ["iron"] = 2.0,
            ["copper"] = 3.0,
        };

        Rng a = new Rng(TestSeed);
        Rng b = new Rng(TestSeed);

        for (int i = 0; i < 500; i++)
        {
            Assert.Equal(a.ChooseWeighted(forward), b.ChooseWeighted(reverse));
        }
    }

    /// <summary>
    /// Weights actually govern the distribution — order-independence is not bought
    /// by ignoring them.
    /// </summary>
    [Fact]
    public void WeightedChoiceRespectsWeights()
    {
        Dictionary<string, double> weights = new()
        {
            ["common"] = 90.0,
            ["rare"] = 9.0,
            ["legendary"] = 1.0,
        };

        Rng rng = new Rng(TestSeed).Derive("loot");
        Dictionary<string, int> counts = new()
        {
            ["common"] = 0,
            ["rare"] = 0,
            ["legendary"] = 0,
        };

        const int Draws = 200_000;
        for (int i = 0; i < Draws; i++)
        {
            counts[rng.ChooseWeighted(weights)]++;
        }

        Assert.InRange(counts["common"] / (double)Draws, 0.88, 0.92);
        Assert.InRange(counts["rare"] / (double)Draws, 0.08, 0.10);
        Assert.InRange(counts["legendary"] / (double)Draws, 0.005, 0.015);
    }

    /// <summary>
    /// A zero weight means impossible, not rare. Disabled content must never drop.
    /// </summary>
    [Fact]
    public void WeightedChoiceNeverPicksZeroWeightEntries()
    {
        Dictionary<string, double> weights = new()
        {
            ["never"] = 0.0,
            ["always"] = 1.0,
            ["nope"] = 0.0,
        };

        Rng rng = new Rng(TestSeed);
        for (int i = 0; i < 5_000; i++)
        {
            Assert.Equal("always", rng.ChooseWeighted(weights));
        }
    }

    /// <summary>
    /// Empty tables, negative weights, NaN and all-zero totals are authoring errors
    /// and fail loudly rather than silently picking something.
    /// </summary>
    [Fact]
    public void WeightedChoiceRejectsBadInput()
    {
        Rng rng = new Rng(TestSeed);

        Assert.Throws<ArgumentException>(
            () => rng.ChooseWeighted(new Dictionary<string, double>()));
        Assert.Throws<ArgumentException>(
            () => rng.ChooseWeighted(new Dictionary<string, double> { ["a"] = 0.0 }));
        Assert.Throws<ArgumentException>(
            () => rng.ChooseWeighted(new Dictionary<string, double> { ["a"] = -1.0 }));
        Assert.Throws<ArgumentException>(
            () => rng.ChooseWeighted(new Dictionary<string, double> { ["a"] = double.NaN }));
        Assert.Throws<ArgumentException>(() => rng.ChooseWeighted(
            new[] { new Entry("dup", 1.0), new Entry("dup", 1.0) }, e => e.Id, e => e.Weight));
        Assert.Throws<ArgumentNullException>(
            () => rng.ChooseWeighted<Entry>(null!, e => e.Id, e => e.Weight));
    }
}
