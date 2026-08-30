using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Definition backing one enemy id (VOID-023).
///
/// The <c>EnemyRegistry</c> that biome spawn pools resolve against
/// (<see cref="BiomeEnemySpawn.EnemyId"/>), and the owner of the one loot table
/// an enemy type drops from (loot-table-spec §10).
///
/// <para><b>This is the base shape only.</b> Health, damage, defence, movement,
/// AI and behaviour all belong to Phase 9 combat, and are deliberately absent
/// rather than guessed. Authored content built against a wrong stat block is
/// far more expensive to undo than a field added later.</para>
///
/// <para><b>Load through <see cref="EnemyRegistryLoader"/>, never
/// <c>RegistryLoader.Load&lt;EnemyDefinition&gt;</c> directly.</b>
/// <see cref="LootTableId"/> names an entry in another registry, so parsing an
/// enemy proves nothing about whether it can actually drop anything.</para>
/// </summary>
public sealed class EnemyDefinition : ICrossRegistryValidated
{
    /// <summary>Stable unique string id, e.g. <c>void:rabbit</c>. JSON key <c>id</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-facing name, for UI and debug overlays. JSON key <c>display_name</c>.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Texture path, e.g. <c>res://assets/enemies/rabbit.png</c>. JSON key
    /// <c>sprite</c>. Not validated at load time; art may lag the data.
    /// </summary>
    [JsonPropertyName("sprite")]
    public string SpritePath { get; init; } = string.Empty;

    /// <summary>
    /// Id of the single loot table this enemy type rolls on death
    /// (loot-table-spec §10), or <c>null</c> for an enemy that drops nothing —
    /// a legal and deliberate state, not a missing value. When set it must
    /// resolve against the loot table registry; a dangling id is a fatal load
    /// error, because the alternative is an enemy that silently never drops.
    /// JSON key <c>loot_table_id</c>.
    /// </summary>
    [JsonPropertyName("loot_table_id")]
    public string? LootTableId { get; init; }
}
