using System;

namespace Void;

/// <summary>
/// Phase 1's surface elevation: one row index per world column (VOID-047,
/// world-generation-spec §6, Phase 1 step 2).
///
/// <para>Deliberately a plain per-column array rather than anything cleverer,
/// because later phases layer onto it: macro features (mountains, valleys,
/// plateaus, oceans) are epic W4 and <i>overlay</i> these values, while biome
/// classification and structure placement only read them.</para>
///
/// <para><b>Overhangs are not modelled and were not forgotten.</b> One Y per
/// column cannot represent them by construction. Per spec §17's W14 note,
/// overhangs emerge for MVP wherever cave carving happens to intersect the
/// surface; a purpose-built overhang pass is post-MVP.</para>
///
/// <para>Immutable once built. A phase that wants to modify the surface
/// produces a new instance from the old one, so no phase can be surprised by a
/// value another phase changed underneath it.</para>
/// </summary>
public sealed class Heightmap
{
    /// <summary>
    /// Surface row per column, indexed by x. Copied in on construction so the
    /// caller's buffer cannot alias — otherwise "immutable" would depend on the
    /// caller's manners.
    /// </summary>
    private readonly int[] _surfaceY;

    /// <summary>
    /// Wraps a fully populated column array.
    /// </summary>
    /// <param name="surfaceY">
    /// One row index per column, length equal to the world width. Every entry
    /// must already lie inside <paramref name="band"/>; this checks it, because
    /// a surface outside the Outside layer is the one failure mode of this
    /// whole step and must not reach a save file.
    /// </param>
    /// <param name="band">The band the values were generated into, carried for later phases.</param>
    /// <exception cref="ArgumentException">If the array is empty or any column falls outside the band.</exception>
    public Heightmap(int[] surfaceY, SurfaceBand band)
    {
        ArgumentNullException.ThrowIfNull(surfaceY);

        if (surfaceY.Length == 0)
        {
            throw new ArgumentException("A heightmap needs at least one column.", nameof(surfaceY));
        }

        for (int x = 0; x < surfaceY.Length; x++)
        {
            if (!band.Contains(surfaceY[x]))
            {
                throw new ArgumentException(
                    $"Column {x} has surface row {surfaceY[x]}, outside the surface band "
                    + $"{band.MinRow}-{band.MaxRow}.",
                    nameof(surfaceY));
            }
        }

        _surfaceY = (int[])surfaceY.Clone();
        Band = band;
    }

    /// <summary>The row range every column is guaranteed to sit inside.</summary>
    public SurfaceBand Band { get; }

    /// <summary>Number of columns, always the world's width in tiles.</summary>
    public int Width => _surfaceY.Length;

    /// <summary>
    /// Surface row of one column: the topmost solid tile, so <c>y - 1</c> is air.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If <paramref name="x"/> is outside [0, <see cref="Width"/>). Loud rather
    /// than wrapped or clamped, because an off-by-one at the world edge would
    /// otherwise generate a plausible column and hide the bug.
    /// </exception>
    public int this[int x]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(x);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, _surfaceY.Length);
            return _surfaceY[x];
        }
    }

    /// <summary>
    /// A copy of the whole column array, for phases that scan it and for tests.
    /// A copy rather than the buffer itself: handing out the array would make
    /// this type mutable through the back door.
    /// </summary>
    public int[] ToArray() => (int[])_surfaceY.Clone();
}
