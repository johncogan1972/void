using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// One entry in a biome's enemy spawn pool (VOID-022), per
/// world-data-model-spec §6.
/// </summary>
public sealed class BiomeEnemySpawn
{
    /// <summary>
    /// Enemy content id, e.g. <c>void:rabbit</c>. JSON key <c>enemy_id</c>.
    /// Not resolved at biome load time — the enemy registry lands in VOID-023;
    /// see <c>BiomeRegistryLoader.ValidateDeferredReferences</c>.
    /// </summary>
    [JsonPropertyName("enemy_id")]
    public string EnemyId { get; init; } = string.Empty;

    /// <summary>
    /// Relative spawn weight inside this biome's pool. Relative, not a
    /// probability: entries are drawn in proportion to weight and need not sum
    /// to 1.
    /// </summary>
    public float Weight { get; init; }

    /// <summary>
    /// Time window this entry is eligible in. Defaults to
    /// <see cref="SpawnTimeOfDay.Any"/> so omitting the field spawns the enemy
    /// around the clock. JSON key <c>time_of_day</c>.
    /// </summary>
    [JsonPropertyName("time_of_day")]
    public SpawnTimeOfDay TimeOfDay { get; init; } = SpawnTimeOfDay.Any;
}
