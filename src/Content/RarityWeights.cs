namespace Void;

/// <summary>
/// Relative rarity weights for one loot entry (VOID-023), per loot-table-spec
/// §4 and §5.
///
/// <para><b>Weights are relative, not probabilities.</b> They need not sum to 1;
/// the roll normalises by their sum. Authoring them to sum to 1 is a convenience
/// so they read as percentages, nothing more.</para>
///
/// <para><b>Zero means "skip this tier"</b> (spec §5), not "unset". An entry
/// that can only ever roll Common gives Common a weight and leaves the other
/// three at zero — which is exactly what the defaults here do, so an omitted
/// <c>rarity_weights</c> block is a Common-only entry rather than a silent
/// all-tiers-equal one.</para>
///
/// <para>Negative weights are rejected at load (see
/// <see cref="LootTableRegistryLoader"/>): they would subtract from the sum and
/// skew every other tier, with no error anywhere.</para>
/// </summary>
public sealed class RarityWeights
{
    /// <summary>
    /// Weight for the baseline tier. Zero skips it, like any other tier.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> defaulted to 1. Doing so is safe only for an
    /// entry that omits the block entirely; for one that authors part of it,
    /// <c>{"rare": 0.5}</c> would silently become common 1 / rare 0.5 — an
    /// invented common drop that no one wrote and no error mentions. The block
    /// is required instead, and the loader rejects one whose weights are all
    /// zero, so "cannot roll a tier" is a load failure rather than an entry that
    /// never fires.
    /// </remarks>
    public float Common { get; init; }

    /// <summary>Weight for <see cref="Rarity.Uncommon"/>. Zero skips the tier.</summary>
    public float Uncommon { get; init; }

    /// <summary>Weight for <see cref="Rarity.Rare"/>. Zero skips the tier.</summary>
    public float Rare { get; init; }

    /// <summary>
    /// Weight for <see cref="Rarity.Legendary"/>. Zero skips the tier. Spec §5
    /// puts typical values at 0.001-0.01; anything much larger is almost
    /// certainly a misplaced decimal point rather than an intent.
    /// </summary>
    public float Legendary { get; init; }
}
