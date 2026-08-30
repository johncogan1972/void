using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// One always-fires drop in a loot table (VOID-023), per loot-table-spec §4.
///
/// Guaranteed drops skip both rolls: no drop chance, and no rarity roll — the
/// rarity is authored directly. They are how a boss reliably yields its trophy
/// and recipe.
/// </summary>
public sealed class GuaranteedDrop
{
    /// <summary>
    /// Item to grant. Must resolve against the item registry; a dangling id is a
    /// fatal load error, because a guaranteed drop that grants nothing is
    /// invisible in play. JSON key <c>item_id</c>.
    /// </summary>
    [JsonPropertyName("item_id")]
    public string ItemId { get; init; } = string.Empty;

    /// <summary>
    /// Fixed rarity for the granted item, replacing the rarity roll entirely.
    /// Defaults to <see cref="Rarity.Common"/>.
    /// </summary>
    public Rarity Rarity { get; init; } = Rarity.Common;

    /// <summary>Number granted. Fixed, not a range — that is what makes it guaranteed.</summary>
    public int Count { get; init; } = 1;

    /// <summary>
    /// Fixed display name, which suppresses Legendary name generation (Phase 5)
    /// for this drop. Null means "generate or use the item's own name" — the
    /// normal case. JSON key <c>name_override</c>.
    /// </summary>
    [JsonPropertyName("name_override")]
    public string? NameOverride { get; init; }
}
