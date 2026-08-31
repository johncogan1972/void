using System;

namespace Void;

/// <summary>
/// The inclusive row range the surface may occupy, resolved from a world type's
/// <see cref="HeightmapConfig"/> band fractions and the Outside layer's height
/// (VOID-047, world-generation-spec §4.1 and §6 Phase 1 step 2).
///
/// <para>Shared deliberately between <see cref="WorldTypeRegistryLoader"/> and
/// <see cref="HeightmapGenerator"/>, exactly as
/// <see cref="LayerBoundaryCalculator"/> is: boot validates the fractions by
/// running this very computation at every declared size preset, so a rule boot
/// checks can never be a rule generation does not apply.</para>
///
/// <para>Pure arithmetic on its two inputs — no engine types, no randomness.</para>
/// </summary>
/// <param name="MinRow">Topmost row the surface may reach; always at least 1, so sky exists above it.</param>
/// <param name="MaxRow">Bottommost row the surface may reach; always inside the Outside layer.</param>
public readonly record struct SurfaceBand(int MinRow, int MaxRow)
{
    /// <summary>
    /// Fewest rows a usable surface band may span. Below this the heightmap
    /// quantises to a handful of distinct elevations and the "terrain" is a
    /// staircase; it is also the floor beneath which a slope limiter cannot
    /// express any slope at all. Rejected at content load rather than clamped,
    /// because the result would generate happily and look wrong.
    /// </summary>
    public const int MinimumRows = 8;

    /// <summary>Number of distinct rows in the band; the band is inclusive at both ends.</summary>
    public int RowCount => MaxRow - MinRow + 1;

    /// <summary>
    /// <b>The rounding rule, stated once because it is load-bearing:</b> each
    /// bound is <c>floor(outsideEnd * fraction)</c>, matching
    /// <see cref="LayerBoundaryCalculator"/>'s flooring so the two never disagree
    /// about which side of a boundary a row is on. Changing it moves the surface
    /// of every existing seed.
    /// </summary>
    /// <param name="outsideEnd">
    /// <see cref="LayerBoundaries.OutsideEnd"/> — the first row *below* the
    /// Outside layer, so the layer is rows [0, outsideEnd). Must be positive.
    /// </param>
    /// <param name="config">Band fractions; validated here, which is why this is the only resolver.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If the fractions are non-finite, not strictly ordered inside (0, 1), or
    /// resolve to a band that reaches row 0, reaches the Underground layer, or
    /// spans fewer than <see cref="MinimumRows"/> rows. Fatal rather than
    /// clamped: a surface with no sky above it is not a world, and silently
    /// nudging it would hide an authoring mistake forever.
    /// </exception>
    public static SurfaceBand Compute(int outsideEnd, HeightmapConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outsideEnd);

        double top = config.SurfaceTopFraction;
        double bottom = config.SurfaceBottomFraction;

        if (!double.IsFinite(top) || top <= 0.0 || top >= 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config), top,
                "surface_top_fraction must be finite and in (0, 1); it is a fraction of the "
                + "Outside layer's height measured down from row 0.");
        }

        if (!double.IsFinite(bottom) || bottom <= top || bottom >= 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config), bottom,
                "surface_bottom_fraction must be finite, greater than surface_top_fraction and "
                + "below 1, so the surface stays inside the Outside layer.");
        }

        SurfaceBand band = new(
            (int)Math.Floor(outsideEnd * top),
            (int)Math.Floor(outsideEnd * bottom));

        if (band.MinRow < 1 || band.MaxRow >= outsideEnd || band.RowCount < MinimumRows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outsideEnd), outsideEnd,
                $"Surface band fractions {top}/{bottom} resolve to rows {band.MinRow}-{band.MaxRow} "
                + $"in an Outside layer of {outsideEnd} rows, which leaves no sky above, reaches "
                + $"the Underground layer, or spans fewer than {MinimumRows} rows.");
        }

        return band;
    }

    /// <summary>
    /// Whether a row is a legal surface elevation. Used by tests and by any
    /// later phase that overlays the heightmap (macro features, W4) and must
    /// stay inside the same band.
    /// </summary>
    public bool Contains(int row) => row >= MinRow && row <= MaxRow;
}
