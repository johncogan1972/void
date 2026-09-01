using System;

namespace Void;

/// <summary>
/// JSON-facing tuning for Phase 1's surface heightmap (VOID-047,
/// world-generation-spec §6, Phase 1 step 2).
///
/// <para>This is the whole reason the heightmap has no constants in code: the
/// octave stack and the slice of the Outside layer the surface may occupy are
/// authored per world type, so a flat plains world and a jagged portal world
/// differ by JSON alone.</para>
///
/// <para>The octave fields exist separately from <see cref="FbmParameters"/>
/// because that type is a validated <c>readonly record struct</c> with no
/// parameterless shape <c>System.Text.Json</c> can populate. This class is the
/// deserialisable half; <see cref="ToFbmParameters"/> is the <b>single</b> place
/// the conversion happens, so its validation is the only validation, and
/// <see cref="WorldTypeRegistryLoader"/> merely translates the throw into a
/// message that blames the data file.</para>
///
/// <para>The surface band is expressed as fractions of the Outside layer rather
/// than row numbers so it scales across size presets untouched; see
/// <see cref="SurfaceBand"/> for how they resolve to rows.</para>
/// </summary>
public sealed class HeightmapConfig
{
    /// <summary>
    /// A gentle, playable default: chunk-of-the-world-scale rolling hills within
    /// the middle of the Outside band. Present so a world type that omits
    /// <c>heightmap</c> entirely still generates something sane rather than
    /// failing on zero octaves — but a real entry states all of it, because
    /// these numbers are terrain design and belong in data.
    /// </summary>
    public static HeightmapConfig Default { get; } = new HeightmapConfig();

    /// <summary>
    /// Number of fBm octaves. Range and meaning are
    /// <see cref="FbmParameters.Octaves"/>'; validated there, not here.
    /// </summary>
    public int Octaves { get; init; } = 4;

    /// <summary>
    /// Base octave frequency in lattice cells per <b>tile column</b> — the
    /// heightmap samples the field at the raw column index. 1/512 puts the
    /// lowest octave's features at roughly eight chunks wide, which is a
    /// hill range rather than a bump.
    /// </summary>
    public double Frequency { get; init; } = 1.0 / 512.0;

    /// <summary>Per-octave frequency multiplier; see <see cref="FbmParameters.Lacunarity"/>.</summary>
    public double Lacunarity { get; init; } = 2.0;

    /// <summary>Per-octave amplitude multiplier; see <see cref="FbmParameters.Persistence"/>.</summary>
    public double Persistence { get; init; } = 0.5;

    /// <summary>
    /// Highest row the surface may reach, as a fraction of the Outside layer's
    /// height measured down from row 0. Everything above it stays sky, which
    /// spec §4.1 requires the Outside layer to contain — a surface allowed to
    /// touch row 0 would leave a world with no air above it.
    /// </summary>
    public double SurfaceTopFraction { get; init; } = 0.45;

    /// <summary>
    /// Lowest row the surface may reach, same units as
    /// <see cref="SurfaceTopFraction"/> and necessarily below it. Kept under 1
    /// so the surface can never sit on the Underground boundary, where a
    /// dirt-to-stone transition would have no room to exist.
    /// </summary>
    public double SurfaceBottomFraction { get; init; } = 0.80;

    /// <summary>
    /// Hard cap on <c>|surface[x] - surface[x-1]|</c>, in rows, enforced by
    /// <see cref="HeightmapGenerator"/>'s slope limiter. Must be at least 1: a
    /// cap of 0 would flatten the whole world to one row. This is a playability
    /// constraint, not an aesthetic one — a single-column cliff of 40 rows is
    /// terrain the player cannot walk and later phases would have to repair.
    /// </summary>
    public int MaxColumnDelta { get; init; } = 3;

    /// <summary>
    /// Optional high-frequency roughness added on top of the base shape
    /// (VOID-061). JSON key <c>detail</c>.
    ///
    /// <para>Null means no detail, which is exactly the surface this generator
    /// produced before the field existed — the term is additive, so absence and
    /// a zero amplitude are the same world.</para>
    ///
    /// <para>It is a separate field rather than more octaves here because fBm
    /// trades hill amplitude for roughness; see <see cref="SurfaceDetailConfig"/>
    /// for the measurements.</para>
    /// </summary>
    public SurfaceDetailConfig? Detail { get; init; }

    /// <summary>
    /// Builds the validated octave parameters. Amplitude is deliberately not
    /// configurable: the heightmap consumes
    /// <see cref="FbmNoise.SampleUnit(double)"/> and maps [0, 1] onto the
    /// surface band, so the band fractions are the only vertical scale, and a
    /// second one would just be a confusing way to say the same thing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If any octave value is out of range. Left to bubble: callers inside
    /// generation want the throw, and the content loader catches it to name the
    /// offending world type.
    /// </exception>
    public FbmParameters ToFbmParameters() =>
        new FbmParameters(Octaves, Frequency, Lacunarity, Persistence);
}
