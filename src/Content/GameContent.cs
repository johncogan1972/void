using System;

namespace Void;

/// <summary>
/// The content registries of world-data-model-spec §7 plus the world-type
/// configuration of world-generation-spec §4, loaded once at boot and frozen
/// (VOID-025, VOID-046).
///
/// <para>Produced only by <see cref="ContentLoader.LoadAll"/>, which loads the
/// registries in dependency order and cross-validates them; an instance of this
/// type therefore means "every id in the shipped content resolves". Nothing
/// here is mutable, so it is safe to hand the same instance to world
/// generation, the server and every client session.</para>
///
/// <para><b>Why these are plain <see cref="Registry{T}"/> instances and not
/// named registry classes.</b> The spec lists <c>BlockRegistry</c>,
/// <c>BiomeRegistry</c> and friends as concepts, not as types to write. The
/// generic registry already provides id lookup, numeric-id lookup and the
/// ordinal-sorted iteration that world generation depends on, so seven
/// subclasses would each add a name and no behaviour. If a registry ever grows
/// genuinely type-specific behaviour, give <i>that</i> one a class — do not add
/// the other six for symmetry.</para>
/// </summary>
public sealed class GameContent
{
    /// <summary>
    /// Creates the aggregate. Internal because a <see cref="GameContent"/> is
    /// meaningful only if the registries in it were validated against each
    /// other, and <see cref="ContentLoader"/> is the only code that does that.
    /// </summary>
    internal GameContent(
        Registry<BlockDefinition> blocks,
        Registry<WallDefinition> walls,
        Registry<ItemDefinition> items,
        Registry<LootTableDefinition> lootTables,
        Registry<EnemyDefinition> enemies,
        Registry<BiomeDefinition> biomes,
        Registry<PrefabDefinition> prefabs,
        Registry<WorldTypeDefinition> worldTypes)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(walls);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(lootTables);
        ArgumentNullException.ThrowIfNull(enemies);
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(prefabs);
        ArgumentNullException.ThrowIfNull(worldTypes);

        Blocks = blocks;
        Walls = walls;
        Items = items;
        LootTables = lootTables;
        Enemies = enemies;
        Biomes = biomes;
        Prefabs = prefabs;
        WorldTypes = worldTypes;
    }

    /// <summary>
    /// Tile blocks, keyed by string id and by the raw <c>uint16</c> stored in
    /// every saved tile. Referenced by biome palettes and prefab tile arrays.
    /// </summary>
    public Registry<BlockDefinition> Blocks { get; }

    /// <summary>Background walls; numeric ids, same stability rules as blocks.</summary>
    public Registry<WallDefinition> Walls { get; }

    /// <summary>Items. References nothing, so it is a root of the load order.</summary>
    public Registry<ItemDefinition> Items { get; }

    /// <summary>Loot tables, every entry of which resolves to an item.</summary>
    public Registry<LootTableDefinition> LootTables { get; }

    /// <summary>Enemies, each with at most one resolved loot table.</summary>
    public Registry<EnemyDefinition> Enemies { get; }

    /// <summary>
    /// Biomes. Fully resolved: palette blocks and walls, underground variant,
    /// vegetation prefabs and spawn-pool enemies have all been checked.
    /// </summary>
    public Registry<BiomeDefinition> Biomes { get; }

    /// <summary>Prefabs, whose tile arrays resolve against blocks and walls.</summary>
    public Registry<PrefabDefinition> Prefabs { get; }

    /// <summary>
    /// World templates: layer proportions and size presets, already checked to
    /// sum to 1 and to leave no zero-height layer at any declared preset. Read
    /// by <see cref="GenerationContext"/> at the start of generation.
    /// </summary>
    public Registry<WorldTypeDefinition> WorldTypes { get; }
}
