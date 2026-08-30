using System;

namespace Void;

/// <summary>
/// Boot-time loader for the loot table registry (VOID-023).
///
/// Loot tables are cross-registry by nature: every guaranteed drop and every
/// weighted entry names an item, and a table whose ids do not resolve looks
/// perfectly loaded while granting nothing at all. This type is the only way to
/// build the registry — <c>RegistryLoader.Load&lt;LootTableDefinition&gt;</c>
/// refuses the type outright — so an unvalidated loot table registry cannot
/// escape into the game.
///
/// <para>Load the item registry first; loot tables cannot be validated without
/// it. Engine-free, like the rest of the content layer.</para>
/// </summary>
public static class LootTableRegistryLoader
{
    /// <summary>
    /// Parses every loot table document in <paramref name="source"/> and
    /// validates it against the already-loaded item registry.
    /// </summary>
    /// <exception cref="ContentLoadException">
    /// On malformed JSON, a duplicate id, an <c>item_id</c> that is not a
    /// registered item, a <c>drop_chance</c> outside 0.0-1.0, or a negative
    /// rarity weight. All are fatal: each of them fails silently in play — as
    /// loot that never drops, or a tier that never rolls — so there is no later
    /// point at which the mistake announces itself.
    /// </exception>
    public static Registry<LootTableDefinition> Load(
        IContentSource source,
        Registry<ItemDefinition> items)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(items);

        Registry<LootTableDefinition> tables = RegistryLoader.LoadUnvalidated<LootTableDefinition>(source);
        Validate(tables, items);
        return tables;
    }

    /// <summary>
    /// Cross-registry and range checks, run on every load. Private because a
    /// loot table registry must never exist in an unvalidated state.
    /// </summary>
    private static void Validate(Registry<LootTableDefinition> tables, Registry<ItemDefinition> items)
    {
        // Ordinal-sorted registry order, so the table blamed for a multi-error
        // content drop is the same one on every machine.
        foreach (LootTableDefinition table in tables)
        {
            for (int i = 0; i < table.GuaranteedDrops.Count; i++)
            {
                GuaranteedDrop drop = table.GuaranteedDrops[i];
                CheckItem(table, $"guaranteed_drops[{i}]", drop.ItemId, items);

                if (drop.Count < 1)
                {
                    throw new ContentLoadException(
                        $"Loot table '{table.Id}' field 'guaranteed_drops[{i}].count' is {drop.Count}; " +
                        "a guaranteed drop must grant at least one item.");
                }
            }

            for (int i = 0; i < table.Entries.Count; i++)
            {
                LootEntry entry = table.Entries[i];
                CheckItem(table, $"entries[{i}]", entry.ItemId, items);
                CheckDropChance(table, i, entry);
                CheckWeights(table, i, entry.RarityWeights);
            }
        }
    }

    /// <summary>Resolves one item id, or fails naming the table, the field and the id.</summary>
    private static void CheckItem(
        LootTableDefinition table, string field, string itemId, Registry<ItemDefinition> items)
    {
        if (!items.Contains(itemId))
        {
            throw new ContentLoadException(
                $"Loot table '{table.Id}' field '{field}' names item '{itemId}', " +
                "which is not a registered item.");
        }
    }

    /// <summary>
    /// Enforces spec §4's 0.0-1.0 range on a drop chance. NaN is rejected by the
    /// same comparison, which is intended: it would make the roll's outcome
    /// depend on comparison direction.
    /// </summary>
    private static void CheckDropChance(LootTableDefinition table, int index, LootEntry entry)
    {
        if (!(entry.DropChance >= 0.0f && entry.DropChance <= 1.0f))
        {
            throw new ContentLoadException(
                $"Loot table '{table.Id}' field 'entries[{index}].drop_chance' is {entry.DropChance}, " +
                "which is outside the required range 0.0-1.0.");
        }
    }

    /// <summary>
    /// Rejects negative rarity weights. A negative weight shrinks the sum the
    /// roll normalises by, skewing every other tier of the same entry, and
    /// nothing downstream would report it.
    /// </summary>
    private static void CheckWeights(LootTableDefinition table, int index, RarityWeights weights)
    {
        CheckWeight(table, index, "common", weights.Common);
        CheckWeight(table, index, "uncommon", weights.Uncommon);
        CheckWeight(table, index, "rare", weights.Rare);
        CheckWeight(table, index, "legendary", weights.Legendary);

        // All four at zero leaves nothing to normalise by, so the rarity roll
        // has no tier to land on. Authored, loads clean, never fires — the same
        // silent nothing a negative weight causes, reached by omission instead.
        if (weights.Common + weights.Uncommon + weights.Rare + weights.Legendary <= 0.0f)
        {
            throw new ContentLoadException(
                $"Loot table '{table.Id}' field 'entries[{index}].rarity_weights' is all zero; " +
                "at least one tier must carry a positive weight or the entry can never roll.");
        }
    }

    /// <summary>One weight, named in the failure so the author can find it.</summary>
    private static void CheckWeight(LootTableDefinition table, int index, string tier, float weight)
    {
        if (!(weight >= 0.0f))
        {
            throw new ContentLoadException(
                $"Loot table '{table.Id}' field 'entries[{index}].rarity_weights.{tier}' is {weight}; " +
                "rarity weights must be zero or positive.");
        }
    }
}
