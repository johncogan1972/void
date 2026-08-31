using System;

namespace Void;

/// <summary>
/// Fractional Brownian motion: a sum of <see cref="PerlinNoise"/> octaves at
/// rising frequency and falling amplitude (VOID-045, epic W3). This is the field
/// world generation actually samples — heightmaps, biome masks, ore density.
///
/// The result is normalised by the <b>sum of the octave amplitudes</b>, never by
/// observed extrema, so the output interval is fixed up front and identical for
/// every seed and every sample count: <c>[-Amplitude, +Amplitude]</c> from
/// <see cref="FbmParameters.Amplitude"/> (so [-1, 1] by default). Downstream
/// thresholds — "solid below 0.3" — therefore stay meaningful when parameters or
/// seeds change. Normalising by observed min/max would make a threshold depend
/// on which samples happened to be taken, which is not reproducible.
///
/// Sampling is stateless and order-independent, inherited from
/// <see cref="PerlinNoise"/>: no draw from an <see cref="Rng"/> happens during
/// sampling, so chunks may be generated in any order or in parallel.
///
/// The floating-point determinism rules this obeys are documented on
/// <see cref="PerlinNoise"/>. The one specific to this type: octave frequency and
/// amplitude are stepped by <b>iterative multiplication</b>, never
/// <c>Math.Pow(lacunarity, octave)</c>. <c>Math.Pow</c> is a libm call that is not
/// correctly rounded and may differ between platforms and runtime versions.
/// </summary>
public sealed class FbmNoise
{
    /// <summary>
    /// Per-octave seed stride. Octave seeds are <c>SplitMix64(seed + i*stride)</c>
    /// rather than reused, so octaves are statistically independent instead of
    /// sharing a lattice and reinforcing each other's grid artefacts.
    /// </summary>
    private const ulong OctaveSeedStride = 0x9E3779B97F4A7C15UL;

    /// <summary>
    /// One field per octave, index 0 being the lowest frequency. Fixed at
    /// construction; iteration is by index, so order is explicit and stable.
    /// </summary>
    private readonly PerlinNoise[] _octaves;

    /// <summary>
    /// Reciprocal of the amplitude sum, premultiplied by
    /// <see cref="FbmParameters.Amplitude"/>. Computed once so sampling is a
    /// single multiply and the normalisation constant cannot drift between calls.
    /// </summary>
    private readonly double _normalisation;

    /// <summary>The layering configuration this instance was built with.</summary>
    public FbmParameters Parameters { get; }

    /// <summary>The base seed all octave seeds were derived from.</summary>
    public ulong Seed { get; }

    /// <summary>
    /// Builds the octave stack for a seed. Deterministic: same seed and
    /// parameters give the same field on every machine and run.
    /// </summary>
    public FbmNoise(ulong seed, FbmParameters parameters)
    {
        Seed = seed;
        Parameters = parameters;

        _octaves = new PerlinNoise[parameters.Octaves];
        double amplitude = 1.0;
        double amplitudeSum = 0.0;

        for (int i = 0; i < parameters.Octaves; i++)
        {
            unchecked
            {
                SplitMix64 mixer = new SplitMix64(seed + ((ulong)i * OctaveSeedStride));
                _octaves[i] = new PerlinNoise(mixer.Next());
            }

            amplitudeSum += amplitude;

            // Iterative step, not Math.Pow — see the type comment.
            amplitude *= parameters.Persistence;
        }

        _normalisation = parameters.Amplitude / amplitudeSum;
    }

    /// <summary>
    /// Builds the octave stack from an RNG sub-stream — the intended call shape,
    /// <c>new FbmNoise(rootRng.Derive("heightmap"), parameters)</c>. Reads
    /// <see cref="Rng.Seed"/> without drawing, so construction never advances the
    /// stream and construction order is irrelevant.
    /// </summary>
    /// <exception cref="ArgumentNullException">If <paramref name="rng"/> is null.</exception>
    public FbmNoise(Rng rng, FbmParameters parameters)
        : this(RequireRng(rng).Seed, parameters)
    {
    }

    /// <summary>Null guard usable from a constructor initialiser, where statements cannot run.</summary>
    private static Rng RequireRng(Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return rng;
    }

    /// <summary>
    /// Samples the 1D fBm field. Guaranteed within
    /// [-<see cref="FbmParameters.Amplitude"/>, +<see cref="FbmParameters.Amplitude"/>].
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="x"/> is non-finite or out of the base field's coordinate range.</exception>
    public double Sample(double x)
    {
        double frequency = Parameters.Frequency;
        double amplitude = 1.0;
        double total = 0.0;

        for (int i = 0; i < _octaves.Length; i++)
        {
            total += _octaves[i].Sample(x * frequency) * amplitude;
            frequency *= Parameters.Lacunarity;
            amplitude *= Parameters.Persistence;
        }

        return Clamp(total * _normalisation);
    }

    /// <summary>
    /// Samples the 2D fBm field. Same guaranteed interval as the 1D overload.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If either coordinate is non-finite or out of the base field's coordinate range.</exception>
    public double Sample(double x, double y)
    {
        double frequency = Parameters.Frequency;
        double amplitude = 1.0;
        double total = 0.0;

        for (int i = 0; i < _octaves.Length; i++)
        {
            total += _octaves[i].Sample(x * frequency, y * frequency) * amplitude;
            frequency *= Parameters.Lacunarity;
            amplitude *= Parameters.Persistence;
        }

        return Clamp(total * _normalisation);
    }

    /// <summary>
    /// Samples the field remapped to [0, <see cref="FbmParameters.Amplitude"/>].
    /// Provided because most consumers (density masks, ore thresholds) want a
    /// non-negative field and would otherwise each rewrite the same remap
    /// slightly differently.
    /// </summary>
    public double SampleUnit(double x) => (Sample(x) + Parameters.Amplitude) * 0.5;

    /// <inheritdoc cref="SampleUnit(double)"/>
    public double SampleUnit(double x, double y) => (Sample(x, y) + Parameters.Amplitude) * 0.5;

    /// <summary>
    /// Enforces the documented interval. The amplitude-sum normalisation already
    /// bounds the result mathematically; this only absorbs floating-point slack
    /// at the extremes, so it must never be relied on to hide an out-of-range bug.
    /// </summary>
    private double Clamp(double value) => Math.Clamp(value, -Parameters.Amplitude, Parameters.Amplitude);
}
