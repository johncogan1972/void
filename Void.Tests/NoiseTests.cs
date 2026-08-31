using System;
using System.Collections.Generic;
using Void;

namespace Void.Tests;

/// <summary>
/// VOID-045 acceptance tests for the deterministic noise primitives. These guard
/// the properties world generation depends on: identical output for identical
/// seeds, independence from sampling order, a bounded output interval, and
/// configuration that fails loudly instead of generating a subtly wrong world.
/// Frozen numeric vectors live in <see cref="NoiseGoldenVectorTests"/>.
/// </summary>
public class NoiseTests
{
    private const ulong TestSeed = 0x0123456789ABCDEFUL;

    /// <summary>
    /// A fixed, arbitrary-looking traversal order over a 16x16 grid. Used to prove
    /// order-independence; it is a constant so the test itself stays deterministic.
    /// The stride is coprime with 256, so it visits every cell exactly once.
    /// </summary>
    private static IEnumerable<(int X, int Y)> ShuffledGrid(int size)
    {
        int count = size * size;
        const int stride = 97;
        for (int i = 0; i < count; i++)
        {
            int cell = (i * stride + 13) % count;
            yield return (cell % size, cell / size);
        }
    }

    /// <summary>
    /// The base guarantee: the field is a pure function of (seed, coordinate), so
    /// two independently constructed instances must agree everywhere. If this
    /// fails, reloading a world regenerates different terrain.
    /// </summary>
    [Fact]
    public void SameSeedProducesIdenticalField()
    {
        PerlinNoise a = new PerlinNoise(TestSeed);
        PerlinNoise b = new PerlinNoise(TestSeed);

        for (int i = 0; i < 200; i++)
        {
            double x = i * 0.37;
            double y = i * -0.11;
            Assert.Equal(a.Sample(x), b.Sample(x));
            Assert.Equal(a.Sample(x, y), b.Sample(x, y));
        }
    }

    /// <summary>
    /// Distinct seeds must produce distinct fields, or every world would look the
    /// same. Checked as "not all samples equal" rather than per-sample, because
    /// gradient noise is legitimately zero at lattice points for any seed.
    /// </summary>
    [Fact]
    public void DifferentSeedsProduceDifferentFields()
    {
        PerlinNoise a = new PerlinNoise(TestSeed);
        PerlinNoise b = new PerlinNoise(TestSeed + 1UL);

        bool anyDifferent = false;
        for (int i = 0; i < 200 && !anyDifferent; i++)
        {
            anyDifferent = a.Sample(i * 0.37, i * 0.19) != b.Sample(i * 0.37, i * 0.19);
        }

        Assert.True(anyDifferent);
    }

    /// <summary>
    /// The hard acceptance criterion: sampling must not advance shared state, so
    /// a grid sampled forward, backward and in a scrambled order must give the
    /// same values. This is what allows chunks to be generated out of order or in
    /// parallel without changing the world.
    /// </summary>
    [Fact]
    public void SamplingIsOrderIndependent()
    {
        const int size = 16;
        PerlinNoise noise = new PerlinNoise(TestSeed);
        double[,] forward = new double[size, size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                forward[x, y] = noise.Sample(x * 0.25, y * 0.25);
            }
        }

        // Reverse traversal, on a fresh instance, must reproduce it exactly.
        PerlinNoise reversed = new PerlinNoise(TestSeed);
        for (int y = size - 1; y >= 0; y--)
        {
            for (int x = size - 1; x >= 0; x--)
            {
                Assert.Equal(forward[x, y], reversed.Sample(x * 0.25, y * 0.25));
            }
        }

        // And so must a scrambled traversal on the original instance.
        foreach ((int x, int y) in ShuffledGrid(size))
        {
            Assert.Equal(forward[x, y], noise.Sample(x * 0.25, y * 0.25));
        }
    }

    /// <summary>
    /// Same as above for the octave stack, which is where an accidental shared
    /// accumulator or cached RNG draw would most plausibly creep in.
    /// </summary>
    [Fact]
    public void FbmSamplingIsOrderIndependent()
    {
        const int size = 16;
        FbmNoise noise = new FbmNoise(TestSeed, new FbmParameters(octaves: 5, frequency: 0.05));
        double[,] forward = new double[size, size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                forward[x, y] = noise.Sample(x, y);
            }
        }

        foreach ((int x, int y) in ShuffledGrid(size))
        {
            Assert.Equal(forward[x, y], noise.Sample(x, y));
        }
    }

    /// <summary>
    /// Constructing a field from an RNG must not draw from it. If it did, adding a
    /// noise field to a generation phase would silently shift every later draw on
    /// that sub-stream and change unrelated parts of the world.
    /// </summary>
    [Fact]
    public void ConstructionDoesNotAdvanceTheRng()
    {
        Rng untouched = new Rng(TestSeed).Derive("heightmap");
        ulong[] expected = { untouched.NextULong(), untouched.NextULong(), untouched.NextULong() };

        Rng used = new Rng(TestSeed).Derive("heightmap");
        _ = new PerlinNoise(used);
        _ = new FbmNoise(used, FbmParameters.Default);

        Assert.Equal(expected[0], used.NextULong());
        Assert.Equal(expected[1], used.NextULong());
        Assert.Equal(expected[2], used.NextULong());
    }

    /// <summary>
    /// Sub-stream keys are how generation phases stay independent. The same key
    /// must give the same field (reload safety); different keys must not give the
    /// same field (or the cave mask would be a copy of the heightmap).
    /// </summary>
    [Fact]
    public void SubStreamKeysSelectIndependentFields()
    {
        Rng root = new Rng(TestSeed);
        PerlinNoise heightA = new PerlinNoise(root.Derive("heightmap"));
        PerlinNoise heightB = new PerlinNoise(root.Derive("heightmap"));
        PerlinNoise caves = new PerlinNoise(root.Derive("caves"));

        bool anyDifferent = false;
        for (int i = 0; i < 200; i++)
        {
            double x = i * 0.37;
            double y = i * 0.19;
            Assert.Equal(heightA.Sample(x, y), heightB.Sample(x, y));
            anyDifferent |= heightA.Sample(x, y) != caves.Sample(x, y);
        }

        Assert.True(anyDifferent);
    }

    /// <summary>
    /// Raw gradient noise is documented as [-1, 1]; downstream thresholds assume
    /// it. Sampled off-lattice at an irrational-ish step so the walk does not sit
    /// on lattice points where noise is trivially zero.
    /// </summary>
    [Fact]
    public void PerlinOutputStaysInDocumentedRange()
    {
        PerlinNoise noise = new PerlinNoise(TestSeed);

        for (int i = 0; i < 20000; i++)
        {
            double x = i * 0.0137;
            double y = i * -0.0071;

            double v1 = noise.Sample(x);
            double v2 = noise.Sample(x, y);

            Assert.InRange(v1, -1.0, 1.0);
            Assert.InRange(v2, -1.0, 1.0);
        }
    }

    /// <summary>
    /// The fBm interval is normalised by the amplitude sum, so it must hold for
    /// any octave count and persistence — not just the default. A drift here
    /// would silently move every threshold-based decision in world gen.
    /// </summary>
    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(4, 0.5)]
    [InlineData(8, 0.9)]
    [InlineData(6, 0.25)]
    public void FbmOutputStaysInNormalisedRange(int octaves, double persistence)
    {
        FbmNoise noise = new FbmNoise(
            TestSeed,
            new FbmParameters(octaves, frequency: 0.03, lacunarity: 2.0, persistence: persistence));

        for (int i = 0; i < 5000; i++)
        {
            Assert.InRange(noise.Sample(i * 0.13), -1.0, 1.0);
            Assert.InRange(noise.Sample(i * 0.13, i * -0.07), -1.0, 1.0);
            Assert.InRange(noise.SampleUnit(i * 0.13, i * -0.07), 0.0, 1.0);
        }
    }

    /// <summary>
    /// Amplitude is an output gain applied after normalisation, so the interval
    /// scales with it exactly. Guards against amplitude being folded into the
    /// per-octave sum, which would make the interval depend on octave count.
    /// </summary>
    [Fact]
    public void FbmAmplitudeScalesTheInterval()
    {
        FbmParameters unit = new FbmParameters(4, 0.03);
        FbmParameters scaled = new FbmParameters(4, 0.03, amplitude: 50.0);

        FbmNoise a = new FbmNoise(TestSeed, unit);
        FbmNoise b = new FbmNoise(TestSeed, scaled);

        for (int i = 0; i < 1000; i++)
        {
            double x = i * 0.13;
            Assert.InRange(b.Sample(x), -50.0, 50.0);
            Assert.Equal(a.Sample(x) * 50.0, b.Sample(x), 12);
        }
    }

    /// <summary>
    /// fBm must actually vary — a normalisation bug that divided by too much would
    /// still pass the range assertions while flattening terrain to nothing.
    /// </summary>
    [Fact]
    public void FbmProducesMeaningfulVariation()
    {
        FbmNoise noise = new FbmNoise(TestSeed, new FbmParameters(4, 0.05));

        double min = double.MaxValue;
        double max = double.MinValue;
        for (int i = 0; i < 5000; i++)
        {
            double v = noise.Sample(i * 0.13, i * 0.29);
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }

        Assert.True(max - min > 0.5, $"fBm range was only {max - min:F4}; the field is too flat to be useful.");
    }

    /// <summary>
    /// Bad world-gen config must fail at construction, not produce a degenerate
    /// world. Each case is one documented constraint from <see cref="FbmParameters"/>.
    /// </summary>
    [Theory]
    [InlineData(0, 0.1, 2.0, 0.5, 1.0)]                        // octaves below 1
    [InlineData(-3, 0.1, 2.0, 0.5, 1.0)]                       // negative octaves
    [InlineData(FbmParameters.MaxOctaves + 1, 0.1, 2.0, 0.5, 1.0)] // above the ceiling
    [InlineData(4, 0.0, 2.0, 0.5, 1.0)]                        // zero frequency
    [InlineData(4, -0.1, 2.0, 0.5, 1.0)]                       // negative frequency
    [InlineData(4, double.NaN, 2.0, 0.5, 1.0)]                 // non-finite frequency
    [InlineData(4, double.PositiveInfinity, 2.0, 0.5, 1.0)]    // non-finite frequency
    [InlineData(4, 0.1, 0.0, 0.5, 1.0)]                        // zero lacunarity
    [InlineData(4, 0.1, double.NaN, 0.5, 1.0)]                 // non-finite lacunarity
    [InlineData(4, 0.1, 2.0, 0.0, 1.0)]                        // zero persistence
    [InlineData(4, 0.1, 2.0, 1.5, 1.0)]                        // persistence above 1
    [InlineData(4, 0.1, 2.0, double.NaN, 1.0)]                 // non-finite persistence
    [InlineData(4, 0.1, 2.0, 0.5, 0.0)]                        // zero amplitude
    [InlineData(4, 0.1, 2.0, 0.5, double.NaN)]                 // non-finite amplitude
    public void InvalidParametersThrow(
        int octaves, double frequency, double lacunarity, double persistence, double amplitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FbmParameters(octaves, frequency, lacunarity, persistence, amplitude));
    }

    /// <summary>The edge values of the documented ranges must be accepted, not rejected off by one.</summary>
    [Fact]
    public void BoundaryParametersAreAccepted()
    {
        _ = new FbmParameters(1, double.Epsilon, 2.0, 1.0);
        _ = new FbmParameters(FbmParameters.MaxOctaves, 0.1, 2.0, 1.0);
    }

    /// <summary>
    /// Non-finite or absurdly large coordinates must be rejected rather than
    /// returning NaN, which would propagate into tile data and corrupt a save.
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1.0e18)]
    [InlineData(-1.0e18)]
    public void InvalidCoordinatesThrow(double coordinate)
    {
        PerlinNoise noise = new PerlinNoise(TestSeed);

        Assert.Throws<ArgumentOutOfRangeException>(() => noise.Sample(coordinate));
        Assert.Throws<ArgumentOutOfRangeException>(() => noise.Sample(coordinate, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => noise.Sample(0.0, coordinate));
    }

    /// <summary>
    /// Gradient noise is zero at integer lattice points by construction. Recorded
    /// as a test because callers who sample exactly on tile centres and get a flat
    /// field need this to be a known property, not a mystery.
    /// </summary>
    [Fact]
    public void GradientNoiseIsZeroAtLatticePoints()
    {
        PerlinNoise noise = new PerlinNoise(TestSeed);

        for (int i = -8; i <= 8; i++)
        {
            Assert.Equal(0.0, noise.Sample(i), 12);
            Assert.Equal(0.0, noise.Sample(i, -i), 12);
        }
    }

    /// <summary>
    /// The field must be continuous: neighbouring samples cannot jump. A broken
    /// fade or lattice-index calculation shows up here as a seam between chunks.
    /// </summary>
    [Fact]
    public void FieldIsContinuousAcrossLatticeBoundaries()
    {
        PerlinNoise noise = new PerlinNoise(TestSeed);
        double previous = noise.Sample(-4.0, 2.5);

        for (int i = 1; i <= 4000; i++)
        {
            double x = -4.0 + i * 0.002;
            double current = noise.Sample(x, 2.5);
            Assert.True(
                Math.Abs(current - previous) < 0.05,
                $"Discontinuity of {Math.Abs(current - previous):F4} at x={x:F3}.");
            previous = current;
        }
    }

    /// <summary>
    /// Both constructors must agree, since callers mix them freely; the RNG
    /// overload is documented as using nothing but <see cref="Rng.Seed"/>.
    /// </summary>
    [Fact]
    public void RngAndSeedConstructorsAgree()
    {
        Rng rng = new Rng(TestSeed).Derive("ore");
        PerlinNoise fromRng = new PerlinNoise(rng);
        PerlinNoise fromSeed = new PerlinNoise(rng.Seed);
        FbmNoise fbmFromRng = new FbmNoise(rng, FbmParameters.Default);
        FbmNoise fbmFromSeed = new FbmNoise(rng.Seed, FbmParameters.Default);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(fromSeed.Sample(i * 0.37, i * 0.19), fromRng.Sample(i * 0.37, i * 0.19));
            Assert.Equal(fbmFromSeed.Sample(i * 0.37, i * 0.19), fbmFromRng.Sample(i * 0.37, i * 0.19));
        }
    }

    /// <summary>Null RNG is a programming error and must surface immediately.</summary>
    [Fact]
    public void NullRngThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new PerlinNoise((Rng)null!));
        Assert.Throws<ArgumentNullException>(() => new FbmNoise((Rng)null!, FbmParameters.Default));
    }

    /// <summary>
    /// The core fBm algorithm: each octave is sampled at rising frequency and
    /// falling amplitude, summed, then normalised. Verify that the FbmNoise
    /// implementation matches a hand-written octave loop so any change to the
    /// algorithm is caught, not silently verified by golden vectors alone.
    /// </summary>
    [Fact]
    public void FbmSampleEqualsHandSummedOctaves()
    {
        FbmParameters p = new FbmParameters(octaves: 6, frequency: 0.07, lacunarity: 2.3, persistence: 0.45, amplitude: 2.5);
        FbmNoise fbm = new FbmNoise(TestSeed, p);

        // Build the octaves manually, mirroring FbmNoise constructor logic.
        var octaves = new PerlinNoise[p.Octaves];
        double amplitudeSum = 0.0;
        double amplitude = 1.0;
        for (int i = 0; i < p.Octaves; i++)
        {
            unchecked
            {
                SplitMix64 mixer = new SplitMix64(TestSeed + ((ulong)i * 0x9E3779B97F4A7C15UL));
                octaves[i] = new PerlinNoise(mixer.Next());
            }
            amplitudeSum += amplitude;
            amplitude *= p.Persistence;
        }
        double normalisation = p.Amplitude / amplitudeSum;

        // Sample at multiple points and verify hand-summed octaves match fBm.Sample().
        for (int i = 0; i < 100; i++)
        {
            double x = i * 0.29;
            double y = i * -0.13;

            // Hand-sum the octaves.
            double handSum1D = 0.0;
            double handSum2D = 0.0;
            double freq = p.Frequency;
            double amp = 1.0;
            for (int j = 0; j < p.Octaves; j++)
            {
                handSum1D += octaves[j].Sample(x * freq) * amp;
                handSum2D += octaves[j].Sample(x * freq, y * freq) * amp;
                freq *= p.Lacunarity;
                amp *= p.Persistence;
            }
            handSum1D *= normalisation;
            handSum2D *= normalisation;

            // Clamp to match FbmNoise's Clamp implementation.
            handSum1D = Math.Clamp(handSum1D, -p.Amplitude, p.Amplitude);
            handSum2D = Math.Clamp(handSum2D, -p.Amplitude, p.Amplitude);

            Assert.Equal(handSum1D, fbm.Sample(x), 12);
            Assert.Equal(handSum2D, fbm.Sample(x, y), 12);
        }
    }
}
