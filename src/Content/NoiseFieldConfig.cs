using System;

namespace Void;

/// <summary>
/// JSON-facing octave stack for one named noise field (VOID-048,
/// world-generation-spec §6, Phase 1 step 4).
///
/// <para>Exists for the same reason <see cref="HeightmapConfig"/>'s octave
/// fields do: <see cref="FbmParameters"/> is a validated
/// <c>readonly record struct</c> with no parameterless shape
/// <c>System.Text.Json</c> can populate, so the deserialisable half has to be a
/// separate class. <see cref="ToFbmParameters"/> is the <b>single</b> place the
/// conversion happens, so its validation is the only validation and a loader
/// merely translates the throw into a message that blames the data file.</para>
///
/// <para><see cref="HeightmapConfig"/> keeps its octave fields flat rather than
/// nesting one of these, because its authored JSON block is flat and changing
/// that shape would break every existing world-type document for no gain. New
/// blocks that need more than one field — biome classification needs two —
/// nest this instead of repeating the four properties per field.</para>
/// </summary>
public sealed class NoiseFieldConfig
{
    /// <summary>
    /// Number of fBm octaves. Range and meaning are
    /// <see cref="FbmParameters.Octaves"/>'; validated there, not here.
    /// </summary>
    public int Octaves { get; init; } = 3;

    /// <summary>
    /// Base octave frequency in lattice cells per <b>tile column</b> — callers
    /// sample these fields at the raw column index, so a frequency of 1/2500 is
    /// a feature about a third of a Medium world wide.
    /// </summary>
    public double Frequency { get; init; } = 1.0 / 2048.0;

    /// <summary>Per-octave frequency multiplier; see <see cref="FbmParameters.Lacunarity"/>.</summary>
    public double Lacunarity { get; init; } = 2.0;

    /// <summary>Per-octave amplitude multiplier; see <see cref="FbmParameters.Persistence"/>.</summary>
    public double Persistence { get; init; } = 0.5;

    /// <summary>
    /// Builds the validated octave parameters. Amplitude is deliberately not
    /// configurable: every consumer of this type reads
    /// <see cref="FbmNoise.SampleUnit(double)"/> and works in [0, 1], so a second
    /// scale would only be a confusing way to say the same thing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If any octave value is out of range. Left to bubble: callers inside
    /// generation want the throw, and the content loader catches it to name the
    /// offending world type and field.
    /// </exception>
    public FbmParameters ToFbmParameters() =>
        new FbmParameters(Octaves, Frequency, Lacunarity, Persistence);
}
