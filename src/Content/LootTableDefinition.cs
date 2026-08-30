using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Definition backing one loot table id (VOID-023), per loot-table-spec §4.
///
/// A loot table is what anything droppable points at — enemies, bosses, chests,
/// breakables, ores and prefab containers (spec §10). It holds two independent
/// halves: <see cref="GuaranteedDrops"/>, which always fire, and
/// <see cref="Entries"/>, each rolled on its own.
///
/// <para><b>This ticket ships the schema, not the rolls.</b> Rarity rolling,
/// stat rolling, Legendary name generation and first-kill tracking are Phase 5.
/// What is fixed here is only the shape the data is authored in.</para>
///
/// <para><b>Load through <see cref="LootTableRegistryLoader"/>, never
/// <c>RegistryLoader.Load&lt;LootTableDefinition&gt;</c> directly.</b> Every
/// <c>item_id</c> below names an entry in the item registry, so parsing the JSON
/// says nothing about whether the table can actually grant anything.</para>
/// </summary>
public sealed class LootTableDefinition : ICrossRegistryValidated
{
    /// <summary>
    /// Stable unique string id, e.g. <c>void:rabbit_loot</c>. JSON key <c>id</c>,
    /// as for every other content type, rather than spec §4's
    /// <c>loot_table_id</c>.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Free-text note for editors and debug output, or null. Never shown to
    /// players and never validated.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Drops that always fire, with no chance and no rarity roll. May be empty.
    /// Order is the authored order, which is the order drops are granted in.
    /// JSON key <c>guaranteed_drops</c>.
    /// </summary>
    [JsonPropertyName("guaranteed_drops")]
    public IReadOnlyList<GuaranteedDrop> GuaranteedDrops { get; init; } = [];

    /// <summary>
    /// Weighted entries, each rolled independently. May be empty — a table of
    /// guaranteed drops alone is legal. Order is the authored order, so a table
    /// evaluates identically on every machine.
    /// </summary>
    public IReadOnlyList<LootEntry> Entries { get; init; } = [];
}
