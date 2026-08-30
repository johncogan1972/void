using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Where the world's main boss arena was placed, and which prefab built it
/// (world-data-model-spec §4).
///
/// Generation output, written once and never updated — whether the boss is
/// still alive lives in <see cref="WorldManifest.MainBossKilled"/>, so that
/// clearing a boss never rewrites its location.
/// </summary>
/// <param name="X">Tile column of the lair anchor point.</param>
/// <param name="Y">Tile row of the lair anchor point.</param>
/// <param name="PrefabId">
/// Prefab registry id (§5). Stored as the id, not the resolved prefab, so a
/// world saved against one content build still names what it used.
/// </param>
public sealed record BossLair(
    [property: JsonPropertyOrder(0), JsonRequired] int X,
    [property: JsonPropertyOrder(1), JsonRequired] int Y,
    [property: JsonPropertyOrder(2), JsonRequired] string PrefabId);
