using System;

namespace Void;

/// <summary>
/// Deterministic gradient (Perlin) noise in 1D and 2D — the base field every
/// world-generation phase layers on top of (VOID-045, epic W3).
///
/// Sampling is <b>stateless and hash-based</b>. Lattice gradients are derived by
/// mixing the instance seed with the integer lattice coordinates through
/// SplitMix64, so a coordinate always yields the same value no matter when, on
/// what thread, or in what order it is sampled. Nothing here draws from an
/// <see cref="Rng"/> during sampling; an <see cref="Rng"/> is used only to
/// supply the seed at construction, and even then it is not advanced. This is a
/// hard requirement: parallel or out-of-order chunk generation must produce
/// identical worlds.
///
/// <para><b>Floating-point determinism decision (MVP target: x86-64 Windows and
/// Linux, .NET).</b> This implementation relies on IEEE-754 <c>double</c>
/// semantics and does <i>not</i> use fixed-point. That is safe on the target set
/// because .NET specifies IEEE-754 arithmetic and x86-64 add / subtract /
/// multiply / divide are correctly rounded, hence bit-identical across those
/// platforms. The real reproducibility hazards are elsewhere, and this file
/// avoids all of them by rule:</para>
/// <list type="bullet">
///   <item><description>No transcendental libm calls in the sampling path — no
///   <c>Pow</c>, <c>Sin</c>, <c>Cos</c>, <c>Exp</c>, <c>Log</c>, <c>Sqrt</c>.
///   These are not required to be correctly rounded and do differ between
///   platforms and runtime versions. Octave frequency and amplitude stepping
///   uses iterative multiplication instead of <c>Math.Pow</c>.</description></item>
///   <item><description>No <c>MathF</c> and no <c>float</c> anywhere: <c>double</c>
///   throughout, so there is no single-precision intermediate whose widening
///   could vary.</description></item>
///   <item><description>No fused multiply-add (<c>Math.FusedMultiplyAdd</c>,
///   <c>Vector*.MultiplyAddEstimate</c>). FMA changes results by skipping an
///   intermediate rounding, so it must never appear in generation code.</description></item>
///   <item><description>Only <c>Math.Floor</c> and <c>Math.Clamp</c> are used from
///   <c>Math</c>; both are exact operations, not approximations.</description></item>
/// </list>
/// <para>If the target set ever grows to a platform without correctly-rounded
/// hardware doubles, this decision must be revisited — see
/// <c>docs/world-generation-spec.md</c> §14.</para>
/// </summary>
public sealed class PerlinNoise
{
    /// <summary>
    /// 1 / sqrt(2), as a literal so no <c>Math.Sqrt</c> call reaches the sampling
    /// path. Length of the diagonal gradient components, making all eight 2D
    /// gradients unit vectors.
    /// </summary>
    private const double InvSqrt2 = 0.70710678118654752440;

    /// <summary>
    /// sqrt(2), as a literal for the same reason. Classic 2D Perlin with
    /// unit-length gradients is bounded by sqrt(2)/2, so multiplying by sqrt(2)
    /// maps the raw field onto [-1, 1].
    /// </summary>
    private const double Sqrt2 = 1.41421356237309504880;

    /// <summary>
    /// 1D Perlin with gradients of +/-1 is bounded by 0.5, so doubling maps it
    /// onto [-1, 1].
    /// </summary>
    private const double Scale1D = 2.0;

    /// <summary>
    /// Odd mixing constants folded into the lattice coordinates before the
    /// SplitMix64 finaliser. Distinct per axis so (x, y) and (y, x) hash apart.
    /// </summary>
    private const ulong CoordMixX = 0x9E3779B97F4A7C15UL;
    private const ulong CoordMixY = 0xC2B2AE3D27D4EB4FUL;

    /// <summary>
    /// Largest magnitude a scaled coordinate may reach. Beyond this, doubles no
    /// longer resolve individual lattice cells and the field degenerates; the
    /// sampler throws rather than silently returning a flat or aliased result.
    /// </summary>
    private const double MaxCoordinate = 1.0e15;

    /// <summary>
    /// The eight unit gradients used in 2D: four axis-aligned and four diagonal.
    /// Kept as flat x/y pairs so gradient lookup is two array reads and no
    /// struct copy. Order is fixed and load-bearing — changing it changes every
    /// generated world.
    /// </summary>
    private static readonly double[] Gradients2D =
    {
         1.0,       0.0,
        -1.0,       0.0,
         0.0,       1.0,
         0.0,      -1.0,
         InvSqrt2,  InvSqrt2,
        -InvSqrt2,  InvSqrt2,
         InvSqrt2, -InvSqrt2,
        -InvSqrt2, -InvSqrt2,
    };

    /// <summary>
    /// The seed every lattice hash is mixed with. Exposed so callers can derive
    /// further related fields reproducibly (for example one seed per fBm octave).
    /// </summary>
    public ulong Seed { get; }

    /// <summary>
    /// Creates a noise field for the given seed. Two instances with the same
    /// seed are interchangeable — the field is a pure function of (seed, coord).
    /// </summary>
    public PerlinNoise(ulong seed)
    {
        Seed = seed;
    }

    /// <summary>
    /// Creates a noise field from an RNG sub-stream, the intended call shape:
    /// <c>new PerlinNoise(rootRng.Derive("heightmap"))</c>. Takes
    /// <see cref="Rng.Seed"/> only and deliberately does <b>not</b> draw from
    /// <paramref name="rng"/>, so constructing a field never perturbs whatever
    /// else shares that stream and construction order cannot matter.
    /// </summary>
    /// <exception cref="ArgumentNullException">If <paramref name="rng"/> is null.</exception>
    public PerlinNoise(Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        Seed = rng.Seed;
    }

    /// <summary>
    /// Quintic fade 6t^5 - 15t^4 + 10t^3 (Perlin's improved interpolant). Its
    /// first and second derivatives vanish at 0 and 1, which is what removes the
    /// visible lattice creasing the original cubic fade produced. Written as
    /// nested multiplication: no <c>Math.Pow</c>.
    /// </summary>
    private static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

    /// <summary>Linear interpolation. Ordered so the endpoints are reproduced exactly at t = 0 and t = 1.</summary>
    private static double Lerp(double a, double b, double t) => a + t * (b - a);

    /// <summary>
    /// Floor to a lattice index. <c>Math.Floor</c> is an exact IEEE operation,
    /// not a libm approximation, so it is safe in the deterministic path.
    /// </summary>
    private static long FastFloor(double value) => (long)Math.Floor(value);

    /// <summary>
    /// Rejects coordinates too large for doubles to resolve lattice cells, and
    /// rejects NaN/infinity, which would otherwise poison the whole field
    /// silently.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If out of range or non-finite.</exception>
    private static void ValidateCoordinate(double value, string paramName)
    {
        if (!double.IsFinite(value) || value < -MaxCoordinate || value > MaxCoordinate)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Noise coordinates must be finite and within +/-{MaxCoordinate:G}.");
        }
    }

    /// <summary>
    /// Hashes a 1D lattice index with the seed. Pure function — this is what
    /// makes sampling order-independent.
    /// </summary>
    private ulong Hash(long x)
    {
        unchecked
        {
            SplitMix64 mixer = new SplitMix64(Seed ^ ((ulong)x * CoordMixX));
            return mixer.Next();
        }
    }

    /// <summary>Hashes a 2D lattice index with the seed. Pure function, as above.</summary>
    private ulong Hash(long x, long y)
    {
        unchecked
        {
            SplitMix64 mixer = new SplitMix64(
                Seed ^ ((ulong)x * CoordMixX) ^ ((ulong)y * CoordMixY));
            return mixer.Next();
        }
    }

    /// <summary>
    /// Samples the 1D field. Result is in [-1, 1] and is exactly 0 at every
    /// integer coordinate — an inherent property of gradient noise, so callers
    /// wanting non-zero values at whole tiles must offset or scale their input.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="x"/> is non-finite or out of range.</exception>
    public double Sample(double x)
    {
        ValidateCoordinate(x, nameof(x));

        long x0 = FastFloor(x);
        double fx = x - x0;

        // Gradient is +1 or -1, chosen by one hash bit.
        double g0 = (Hash(x0) & 1UL) == 0UL ? 1.0 : -1.0;
        double g1 = (Hash(x0 + 1L) & 1UL) == 0UL ? 1.0 : -1.0;

        double n0 = g0 * fx;
        double n1 = g1 * (fx - 1.0);

        double value = Lerp(n0, n1, Fade(fx)) * Scale1D;

        // The bound above is exact; the clamp only absorbs rounding slack at the
        // extremes so the documented range is guaranteed, never to mask a bug.
        return Math.Clamp(value, -1.0, 1.0);
    }

    /// <summary>
    /// Samples the 2D field. Result is in [-1, 1] and is exactly 0 at every
    /// integer lattice point, same caveat as the 1D overload.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If either coordinate is non-finite or out of range.</exception>
    public double Sample(double x, double y)
    {
        ValidateCoordinate(x, nameof(x));
        ValidateCoordinate(y, nameof(y));

        long x0 = FastFloor(x);
        long y0 = FastFloor(y);
        double fx = x - x0;
        double fy = y - y0;

        double n00 = DotGradient(x0, y0, fx, fy);
        double n10 = DotGradient(x0 + 1L, y0, fx - 1.0, fy);
        double n01 = DotGradient(x0, y0 + 1L, fx, fy - 1.0);
        double n11 = DotGradient(x0 + 1L, y0 + 1L, fx - 1.0, fy - 1.0);

        double u = Fade(fx);
        double v = Fade(fy);

        double value = Lerp(Lerp(n00, n10, u), Lerp(n01, n11, u), v) * Sqrt2;

        // As in the 1D case: rounding slack only.
        return Math.Clamp(value, -1.0, 1.0);
    }

    /// <summary>
    /// Dot product of the lattice corner's gradient with the offset to the sample
    /// point. The gradient index takes three bits off the top of the hash, which
    /// are the highest-quality bits of the SplitMix64 output.
    /// </summary>
    private double DotGradient(long cx, long cy, double dx, double dy)
    {
        int index = (int)(Hash(cx, cy) >> 61) * 2;
        return Gradients2D[index] * dx + Gradients2D[index + 1] * dy;
    }
}
