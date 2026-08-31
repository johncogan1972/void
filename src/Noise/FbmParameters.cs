using System;

namespace Void;

/// <summary>
/// Immutable, validated configuration for fractional Brownian motion layering
/// over a base noise field (VOID-045, epic W3).
///
/// Every value is validated in the constructor, so an <see cref="FbmParameters"/>
/// that exists is always usable — <see cref="FbmNoise"/> never has to re-check.
/// These values are intended to come from JSON world-gen config, which is why
/// validation throws loudly rather than clamping: a typo'd config should fail at
/// load, not quietly generate a different world.
/// </summary>
public readonly record struct FbmParameters
{
    /// <summary>
    /// Hard ceiling on octaves. Past roughly 24 the added octave's amplitude is
    /// below double precision on the accumulated sum and only costs time; the
    /// limit also stops a bad config from stalling generation.
    /// </summary>
    public const int MaxOctaves = 32;

    /// <summary>
    /// Number of noise layers summed. Must be in [1, <see cref="MaxOctaves"/>].
    /// One octave is plain base noise.
    /// </summary>
    public int Octaves { get; }

    /// <summary>
    /// Scale applied to input coordinates for the first octave, in lattice cells
    /// per input unit. Must be finite and greater than zero. For tile-space
    /// input, 1/64 gives features on the order of a chunk.
    /// </summary>
    public double Frequency { get; }

    /// <summary>
    /// Frequency multiplier between consecutive octaves. Must be finite and
    /// greater than zero; values above 1 (classically 2.0) are what make later
    /// octaves add detail rather than repeat the first.
    /// </summary>
    public double Lacunarity { get; }

    /// <summary>
    /// Amplitude multiplier between consecutive octaves. Must be in (0, 1]:
    /// at or below zero an octave would vanish or invert, and above 1 later
    /// octaves would dominate, turning the result into high-frequency hash.
    /// </summary>
    public double Persistence { get; }

    /// <summary>
    /// Output gain applied <i>after</i> normalisation. Must be finite and
    /// greater than zero. It is the half-range of the result: output is
    /// guaranteed within [-Amplitude, +Amplitude], so the default of 1 gives
    /// [-1, 1].
    /// </summary>
    public double Amplitude { get; }

    /// <summary>
    /// A conventional starting point: 4 octaves, chunk-scale features, classic
    /// lacunarity 2 and persistence 0.5, unit amplitude.
    /// </summary>
    public static FbmParameters Default { get; } =
        new FbmParameters(octaves: 4, frequency: 1.0 / 64.0, lacunarity: 2.0, persistence: 0.5);

    /// <summary>
    /// Validates and stores the layering parameters.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If octaves is outside [1, <see cref="MaxOctaves"/>], if frequency,
    /// lacunarity or amplitude is non-finite or not greater than zero, or if
    /// persistence is non-finite or outside (0, 1].
    /// </exception>
    public FbmParameters(
        int octaves,
        double frequency,
        double lacunarity = 2.0,
        double persistence = 0.5,
        double amplitude = 1.0)
    {
        if (octaves < 1 || octaves > MaxOctaves)
        {
            throw new ArgumentOutOfRangeException(
                nameof(octaves), octaves, $"Octaves must be in [1, {MaxOctaves}].");
        }

        RequirePositiveFinite(frequency, nameof(frequency));
        RequirePositiveFinite(lacunarity, nameof(lacunarity));
        RequirePositiveFinite(amplitude, nameof(amplitude));

        if (!double.IsFinite(persistence) || persistence <= 0.0 || persistence > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(persistence), persistence, "Persistence must be finite and in (0, 1].");
        }

        Octaves = octaves;
        Frequency = frequency;
        Lacunarity = lacunarity;
        Persistence = persistence;
        Amplitude = amplitude;
    }

    /// <summary>Shared guard for the three "finite and &gt; 0" fields.</summary>
    private static void RequirePositiveFinite(double value, string paramName)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                paramName, value, $"{paramName} must be finite and greater than zero.");
        }
    }
}
