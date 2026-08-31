using System;

namespace Void;

/// <summary>
/// Turns a world height plus a world type's <see cref="LayerProportions"/> into
/// the concrete row indices of <see cref="LayerBoundaries"/>
/// (world-generation-spec §6, Phase 1 step 3).
///
/// <para>Shared deliberately between <see cref="WorldTypeRegistryLoader"/> and
/// <see cref="WorldGenerator"/>: content load validates the proportions by
/// running this exact computation at every declared size preset, so what boot
/// checks and what generation produces can never be two different rules.</para>
///
/// <para>Engine-free and side-effect-free — pure arithmetic on its two inputs.</para>
/// </summary>
public static class LayerBoundaryCalculator
{
    /// <summary>
    /// <b>The rounding rule, stated once because it is load-bearing:</b> the
    /// proportions are accumulated into a running total and each boundary is
    /// <c>floor(height * cumulative_fraction)</c>. Rounding the cumulative
    /// fraction rather than each layer independently means boundaries cannot
    /// drift as they add up, and the void layer — which has no stored end —
    /// absorbs whatever remainder the flooring left. Changing this rule moves
    /// every boundary in every world, existing saves included.
    ///
    /// <para>Medium (1800 rows, 30/25/30/15) therefore gives 540 / 990 / 1530,
    /// matching spec §4.</para>
    /// </summary>
    /// <param name="heightTiles">World height in rows; must be positive.</param>
    /// <param name="proportions">
    /// Assumed already validated (sums to 1, no zero-height layer). This method
    /// does not re-check: it is called during that very validation, and a second
    /// check here would have to duplicate the failure messages.
    /// </param>
    public static LayerBoundaries Compute(int heightTiles, LayerProportions proportions)
    {
        ArgumentNullException.ThrowIfNull(proportions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightTiles);

        double cumulative = proportions.Outside;
        int outsideEnd = (int)Math.Floor(heightTiles * cumulative);

        cumulative += proportions.Underground;
        int undergroundEnd = (int)Math.Floor(heightTiles * cumulative);

        cumulative += proportions.Deep;
        int deepEnd = (int)Math.Floor(heightTiles * cumulative);

        return new LayerBoundaries(outsideEnd, undergroundEnd, deepEnd);
    }
}
