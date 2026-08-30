using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// One independently rolled entry in a loot table (VOID-023), per
/// loot-table-spec §4.
///
/// <para><b>Entries roll independently</b>, they are not a weighted pool: each
/// entry checks its own <see cref="DropChance"/>, then rolls a rarity, then a
/// count. Spec §4 chose this deliberately — it reads as "5% chance to drop a
/// torch" rather than as a share of N picks.</para>
/// </summary>
public sealed class LootEntry
{
    /// <summary>
    /// Item this entry can grant. Must resolve against the item registry.
    /// JSON key <c>item_id</c>.
    /// </summary>
    [JsonPropertyName("item_id")]
    public string ItemId { get; init; } = string.Empty;

    /// <summary>
    /// Probability this entry fires at all, in 0.0-1.0 inclusive. Validated at
    /// load: a value outside that range is fatal, because both ends fail
    /// silently in play — above 1.0 reads as "always", below 0.0 as "never", and
    /// neither says anything about the typo that caused it.
    /// JSON key <c>drop_chance</c>.
    /// </summary>
    /// <remarks>
    /// Required, with no default. There is no safe value to assume: leaving it
    /// out would otherwise mean 0.0, an entry that is authored, loads clean, and
    /// never drops anything for the life of the game — the silent failure this
    /// type's range check exists to prevent, arriving through the one door the
    /// check does not cover.
    /// </remarks>
    [JsonPropertyName("drop_chance")]
    [JsonRequired]
    public required float DropChance { get; init; }

    /// <summary>
    /// Relative tier weights for the rarity roll. JSON key
    /// <c>rarity_weights</c>.
    /// </summary>
    /// <remarks>
    /// Required. An entry with no weights cannot pick a tier, so there is
    /// nothing sensible to default to — see <see cref="RarityWeights"/> for why
    /// defaulting one tier to 1 is worse than demanding the block.
    /// </remarks>
    [JsonPropertyName("rarity_weights")]
    [JsonRequired]
    public required RarityWeights RarityWeights { get; init; }

    /// <summary>
    /// Inclusive count range granted when the entry fires. Defaults to exactly
    /// one. JSON key <c>count_range</c>, authored as <c>[min, max]</c>.
    /// </summary>
    [JsonPropertyName("count_range")]
    public CountRange CountRange { get; init; } = new CountRange(1, 1);

    /// <summary>
    /// Optional gates on the entry, or null when it is unconditional. Null
    /// rather than an all-null instance so the round-trip preserves what the
    /// author wrote. JSON key <c>conditions</c>.
    /// </summary>
    public LootConditions? Conditions { get; init; }
}
