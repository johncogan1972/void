using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Void;

namespace Void.Tests;

/// <summary>
/// Frozen output vectors for the VOID-045 noise primitives. Together with
/// <see cref="GoldenVectorTests"/> these are the contract that keeps generated
/// worlds reproducible: if a value here moves, every world that used noise
/// generates different terrain.
///
/// PROVENANCE — read before touching anything here.
///
/// These vectors are <b>self-generated and project-defined</b>, and there is no
/// honest way to claim otherwise. Unlike the SplitMix64 vectors in
/// <see cref="GoldenVectorTests"/>, there is no published reference output to
/// anchor against: gradient noise has no single canonical numeric definition,
/// and this implementation makes project-specific choices that no third party
/// shares — the eight unit gradients and their fixed order, the SplitMix64
/// lattice hash and its per-axis mixing constants, the sqrt(2) output scaling,
/// the per-octave seed stride, and normalisation by the amplitude sum. The
/// numbers below were produced by running this implementation once, inspected
/// for plausibility (in range, non-degenerate, continuous), and frozen.
///
/// What they therefore guard is <i>change</i>, not <i>correctness</i>. Correctness
/// is covered by the property tests in <see cref="NoiseTests"/> — range,
/// continuity, order-independence, lattice zeroes. A failure here means the
/// generator changed; that is either a deliberate act (regenerate in the same
/// commit as the generator change, and say why in the commit message) or a bug
/// that would have silently rewritten every existing world.
///
/// Regenerating these is never the fix for a red build you did not intend.
/// </summary>
public class NoiseGoldenVectorTests
{
    /// <summary>
    /// The world seed all vectors here derive from. Deliberately not near zero,
    /// so the vectors do not sit in a corner of the seed space.
    /// </summary>
    private const ulong WorldSeed = 0x0123456789ABCDEFUL;

    /// <summary>
    /// The sub-stream key the frozen fields use. Part of the contract: changing
    /// the key changes every value below.
    /// </summary>
    private const string GoldenKey = "noise-golden";

    /// <summary>
    /// Precision the frozen scalars are compared at. 15 decimal places is inside
    /// double's ~15-17 significant digits, so it pins the value without failing on
    /// the last-bit noise of a literal round-trip.
    /// </summary>
    private const int Precision = 15;

    /// <summary>
    /// SHA-256 over the frozen sample grid, hex, uppercase. Covers 4096 2D
    /// samples plus 256 1D samples; the individual vectors below exist so a
    /// failure here is diagnosable rather than just "the hash moved".
    /// </summary>
    private const string GoldenHash =
        "05C9B94EB369DAD52AE4852E7D4FA4E74E9011B991B8547CE88FC6EA670DDC8A";

    /// <summary>The frozen fBm configuration: 4 octaves at chunk scale, classic lacunarity and persistence.</summary>
    private static FbmParameters GoldenParameters =>
        new FbmParameters(octaves: 4, frequency: 1.0 / 64.0, lacunarity: 2.0, persistence: 0.5);

    /// <summary>Builds the frozen gradient field. Derivation is part of the contract.</summary>
    private static PerlinNoise GoldenPerlin() => new PerlinNoise(new Rng(WorldSeed).Derive(GoldenKey));

    /// <summary>Builds the frozen fBm field over the same sub-stream.</summary>
    private static FbmNoise GoldenFbm() => new FbmNoise(new Rng(WorldSeed).Derive(GoldenKey), GoldenParameters);

    /// <summary>
    /// Pins the seed the noise field is actually built from. If sub-stream
    /// derivation changes, this fails first and explains why everything else did.
    /// </summary>
    [Fact]
    public void GoldenFieldSeedIsPinned()
    {
        Assert.Equal(0x9A3B0018949A513EUL, GoldenPerlin().Seed);
    }

    /// <summary>
    /// Frozen 2D gradient samples, chosen off-lattice and spread across positive,
    /// negative and large coordinates so a broken floor, fade or gradient table
    /// shows up in at least one of them.
    /// </summary>
    [Theory]
    [InlineData(0.5, 0.5, 0.1767766952966368)]
    [InlineData(1.25, -3.75, 0.16897929592792982)]
    [InlineData(-7.125, 12.875, -0.03488842599123235)]
    [InlineData(100.3333, 0.6666, 0.018516778540474392)]
    public void Perlin2DSamplesAreFrozen(double x, double y, double expected)
    {
        Assert.Equal(expected, GoldenPerlin().Sample(x, y), Precision);
    }

    /// <summary>Frozen 1D gradient samples, same reasoning as the 2D set.</summary>
    [Theory]
    [InlineData(0.3125, 0.2649879455566406)]
    [InlineData(1.25, 0.603515625)]
    [InlineData(-3.75, -0.603515625)]
    [InlineData(42.125, -0.2178955078125)]
    public void Perlin1DSamplesAreFrozen(double x, double expected)
    {
        Assert.Equal(expected, GoldenPerlin().Sample(x), Precision);
    }

    /// <summary>
    /// Frozen fBm samples. These additionally pin the octave seed stride, the
    /// iterative frequency/amplitude stepping and the amplitude-sum
    /// normalisation, none of which the raw gradient vectors touch.
    /// </summary>
    [Theory]
    [InlineData(0.5, 0.5, -0.020066029663715353)]
    [InlineData(1.25, -3.75, 0.007526728530566518)]
    [InlineData(-7.125, 12.875, 0.040460206834822356)]
    [InlineData(100.3333, 0.6666, -0.04639522097985291)]
    public void Fbm2DSamplesAreFrozen(double x, double y, double expected)
    {
        Assert.Equal(expected, GoldenFbm().Sample(x, y), Precision);
    }

    /// <summary>Frozen 1D fBm samples, same reasoning as the 2D set.</summary>
    [Theory]
    [InlineData(0.3125, 0.010367087975488706)]
    [InlineData(1.25, 0.03917550327613147)]
    [InlineData(-3.75, -0.04459086401038803)]
    [InlineData(42.125, 0.36125348713281485)]
    public void Fbm1DSamplesAreFrozen(double x, double expected)
    {
        Assert.Equal(expected, GoldenFbm().Sample(x), Precision);
    }

    /// <summary>
    /// Whole-field hash: 4096 samples over a 64x64 chunk-sized grid plus a 256
    /// sample 1D sweep. Catches drift the handful of point vectors above would
    /// miss — a gradient reordering, an off-by-one in the lattice hash, a changed
    /// clamp. Doubles are written little-endian explicitly so the hash is
    /// byte-order independent, and the traversal order is fixed row-major.
    /// </summary>
    [Fact]
    public void SampleGridHashIsFrozen()
    {
        FbmNoise noise = GoldenFbm();
        byte[] buffer = new byte[sizeof(double)];

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(buffer, noise.Sample(x, y));
                hash.AppendData(buffer);
            }
        }

        for (int i = 0; i < 256; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(buffer, noise.Sample(i * 0.5 - 64.0));
            hash.AppendData(buffer);
        }

        string actual = Convert.ToHexString(hash.GetHashAndReset());

        Assert.True(
            string.Equals(GoldenHash, actual, StringComparison.Ordinal),
            $"Noise field hash changed. Expected {GoldenHash}, got {actual}. " +
            "Every world generated with noise now differs. Read the provenance note on this class " +
            "before updating the constant.");
    }
}
