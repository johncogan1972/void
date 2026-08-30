using System;

namespace Void;

/// <summary>
/// Boot-time loader for the enemy registry (VOID-023).
///
/// An enemy names the one loot table it drops from, so parsing an enemy document
/// proves only that the JSON is well formed. This type is the only way to build
/// the registry — <c>RegistryLoader.Load&lt;EnemyDefinition&gt;</c> refuses the
/// type outright — so an enemy registry holding a loot table id that resolves to
/// nothing cannot reach the game.
///
/// <para>Load loot tables (and therefore items) first. Engine-free, like the
/// rest of the content layer.</para>
/// </summary>
public static class EnemyRegistryLoader
{
    /// <summary>
    /// Parses every enemy document in <paramref name="source"/> and validates it
    /// against the already-loaded loot table registry.
    /// </summary>
    /// <exception cref="ContentLoadException">
    /// On malformed JSON, a duplicate id, or a non-null <c>loot_table_id</c>
    /// that is not a registered loot table. The last is fatal rather than
    /// downgraded to "drops nothing", because that is exactly what an enemy with
    /// a null table already means — silently conflating the two would hide the
    /// typo forever.
    /// </exception>
    public static Registry<EnemyDefinition> Load(
        IContentSource source,
        Registry<LootTableDefinition> lootTables)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lootTables);

        Registry<EnemyDefinition> enemies = RegistryLoader.LoadUnvalidated<EnemyDefinition>(source);
        Validate(enemies, lootTables);
        return enemies;
    }

    /// <summary>
    /// Cross-registry check, run on every load. Private because an enemy
    /// registry must never exist in an unvalidated state.
    /// </summary>
    private static void Validate(
        Registry<EnemyDefinition> enemies, Registry<LootTableDefinition> lootTables)
    {
        // Ordinal-sorted registry order, so the enemy blamed for a multi-error
        // content drop is the same one on every machine.
        foreach (EnemyDefinition enemy in enemies)
        {
            // Null is a legal, deliberate "this enemy drops nothing".
            if (enemy.LootTableId is null)
            {
                continue;
            }

            if (!lootTables.Contains(enemy.LootTableId))
            {
                throw new ContentLoadException(
                    $"Enemy '{enemy.Id}' names loot_table_id '{enemy.LootTableId}', " +
                    "which is not a registered loot table.");
            }
        }
    }
}
