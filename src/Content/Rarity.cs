namespace Void;

/// <summary>
/// The four loot rarity tiers of loot-table-spec §5 (VOID-023).
///
/// Serialised as snake_case strings (<c>"legendary"</c>) rather than integers,
/// so data files stay readable and reordering the enum can never silently
/// repoint authored content at a different tier.
///
/// <para>Declaration order is load-bearing: the rarity roll of spec §5 walks the
/// weights common → uncommon → rare → legendary, subtracting each in turn. The
/// roll itself is Phase 5 work; the order it depends on is fixed here.</para>
/// </summary>
public enum Rarity
{
    /// <summary>Baseline tier. No stat roll bonus; the bulk of all drops.</summary>
    Common = 0,

    /// <summary>One step up from baseline.</summary>
    Uncommon = 1,

    /// <summary>Notable drop; meaningfully better than baseline.</summary>
    Rare = 2,

    /// <summary>
    /// Top tier, and deliberately very rare — spec §5 puts typical weights at
    /// 0.001 to 0.01. Legendary drops get generated names (Phase 5), unless a
    /// <see cref="GuaranteedDrop.NameOverride"/> fixes the name.
    /// </summary>
    Legendary = 3,
}
