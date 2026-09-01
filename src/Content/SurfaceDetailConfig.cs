using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// The high-frequency roughness added on top of the heightmap's base shape
/// (VOID-061), optional per world type.
///
/// <para><b>Why this exists as a separate field rather than more octaves on the
/// base stack.</b> fBm normalises to a fixed total amplitude, so making the base
/// stack rougher — more octaves, or higher persistence — takes amplitude away
/// from the hills to pay for the detail: measured on the shipped config, pushing
/// persistence to 0.70 cut the world's elevation range from 83 rows to 71 while
/// roughening it. Detail added afterwards, in rows, leaves the hills alone; the
/// same measurement puts the range at 87 rows <i>with</i> the roughness. The two
/// scales are independent terrain decisions and this keeps them independently
/// authorable.</para>
///
/// <para><b>What it fixes.</b> Without it the shipped surface changes by at most
/// one row per column and is flat in 81% of columns, so quantising a smooth ramp
/// to integer rows produces evenly-spaced single-row steps — a visible staircase
/// (found in the VOID-057 viewer). Detail of a few rows at a tile-scale
/// wavelength moves where each step lands, which is what breaks the regularity.
/// It does not, and is not meant to, make the terrain steeper on average.</para>
///
/// <para>Omit the whole block, or set <see cref="AmplitudeRows"/> to 0, for the
/// smooth surface — this is additive, so absence is exactly the old
/// behaviour.</para>
/// </summary>
public sealed class SurfaceDetailConfig
{
    /// <summary>
    /// Octaves in the detail stack. Range is <see cref="FbmParameters.Octaves"/>';
    /// validated there. Small on purpose — this is a texture, not a second
    /// landscape, and its octaves halve in amplitude from an already small base.
    /// </summary>
    public int Octaves { get; init; } = 2;

    /// <summary>
    /// Base detail frequency in lattice cells per <b>tile column</b>, same units
    /// as <see cref="HeightmapConfig.Frequency"/>. This is the field's most
    /// load-bearing number: it sets how far apart the steps are, so it wants to
    /// be near the tile scale. 1/16 puts a feature every sixteen columns, which
    /// is short enough to break up a slope and long enough not to read as noise.
    /// </summary>
    public double Frequency { get; init; } = 1.0 / 16.0;

    /// <summary>Per-octave frequency multiplier; see <see cref="FbmParameters.Lacunarity"/>.</summary>
    public double Lacunarity { get; init; } = 2.0;

    /// <summary>Per-octave amplitude multiplier; see <see cref="FbmParameters.Persistence"/>.</summary>
    public double Persistence { get; init; } = 0.5;

    /// <summary>
    /// Peak displacement in <b>rows</b>, applied as ±this value around the base
    /// surface. JSON key <c>amplitude_rows</c>.
    ///
    /// <para>In rows rather than as a fraction, unlike the surface band, because
    /// its job is to be comparable to one tile: the staircase is a
    /// row-quantisation artefact, so the useful question is "how many rows of
    /// wobble", and a fraction of the band would silently change that answer
    /// between size presets.</para>
    ///
    /// <para>0 disables the term entirely and is the default, so a world type
    /// that omits this block generates exactly as it did before the field
    /// existed. Must not be negative — a negative amplitude is the same terrain
    /// as its positive counterpart with the field mirrored, so it is a typo
    /// rather than a choice.</para>
    /// </summary>
    [JsonPropertyName("amplitude_rows")]
    public double AmplitudeRows { get; init; }

    /// <summary>
    /// Builds the validated octave parameters for the detail field.
    /// </summary>
    /// <remarks>
    /// Amplitude is left at the <see cref="FbmParameters"/> default of 1 and the
    /// vertical scale comes from <see cref="AmplitudeRows"/> instead, so there is
    /// one place that decides how tall the detail is.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If any octave value is out of range. Left to bubble so the content loader
    /// can name the world type it came from.
    /// </exception>
    public FbmParameters ToFbmParameters() =>
        new FbmParameters(Octaves, Frequency, Lacunarity, Persistence);
}
