namespace Void;

/// <summary>
/// The kinds of special tile a prefab can mark for the placement engine
/// (VOID-024), per world-data-model-spec §5.
///
/// Serialised as a snake_case string (<c>"boss_spawn"</c>, <c>"chest"</c>) by
/// the shared <c>JsonStringEnumConverter</c> in
/// <see cref="RegistryLoader.Options"/>, which is configured with
/// <c>allowIntegerValues: false</c>. That is load-bearing twice over: prefab
/// JSON stays readable, reordering these members can never repoint shipped
/// content at a different marker kind, and an unrecognised <c>type</c> string is
/// a hard parse failure rather than a silently-zero <see cref="BossSpawn"/>.
/// The converter throws <c>JsonException</c>, which
/// <c>RegistryLoader.Parse</c> catches and rewraps as a
/// <see cref="ContentLoadException"/> naming the file — so no hand-rolled
/// parsing is needed here to get a diagnosable error.
/// </summary>
public enum PrefabMarkerType
{
    /// <summary>Where a boss is placed; only meaningful in boss-lair prefabs.</summary>
    BossSpawn = 0,

    /// <summary>Where the player enters. Used by the generator's reachability check.</summary>
    Entrance = 1,

    /// <summary>Loot chest site. Metadata carries the loot table id.</summary>
    Chest = 2,

    /// <summary>Enemy spawner point. Metadata carries the spawn configuration.</summary>
    Spawner = 3,

    /// <summary>Placeholder filled from a decoration set at placement time.</summary>
    Decoration = 4,
}
