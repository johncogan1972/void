using System;
using System.IO;
using Void;

namespace Void.Tests;

/// <summary>
/// Locates and loads the <b>shipped</b> content tree for the content tests
/// (VOID-023, VOID-024).
///
/// Tests must exercise the real <c>data/</c> files rather than a copy: the whole
/// point of the item, enemy, loot table and prefab registries is that block
/// drops, biome spawn pools and prefab tile data resolve against what actually
/// ships, and a copied fixture would let the shipped file rot without anything
/// going red.
/// </summary>
internal static class ContentPaths
{
    /// <summary>
    /// Walks up from the test assembly to the repository root, identified by the
    /// shipped block data.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// If the tree is not found — fatal, because every caller would otherwise
    /// silently assert against nothing.
    /// </exception>
    public static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "data", "blocks", "blocks.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    /// <summary>A source over one shipped content folder, e.g. <c>data/items</c>.</summary>
    public static DirectoryContentSource Source(string folder) =>
        new(Path.Combine(RepoRoot(), "data", folder));

    /// <summary>The shipped item registry. Items reference nothing, so this is the root of the chain.</summary>
    public static Registry<ItemDefinition> Items() =>
        RegistryLoader.Load<ItemDefinition>(Source("items"));

    /// <summary>The shipped loot tables, validated against the shipped items.</summary>
    public static Registry<LootTableDefinition> LootTables() =>
        LootTableRegistryLoader.Load(Source("loot_tables"), Items());

    /// <summary>The shipped enemies, validated against the shipped loot tables.</summary>
    public static Registry<EnemyDefinition> Enemies() =>
        EnemyRegistryLoader.Load(Source("enemies"), LootTables());

    /// <summary>The shipped blocks. Numeric ids only; references nothing.</summary>
    public static Registry<BlockDefinition> Blocks() =>
        RegistryLoader.Load<BlockDefinition>(Source("blocks"));

    /// <summary>The shipped walls. Numeric ids only; references nothing.</summary>
    public static Registry<WallDefinition> Walls() =>
        RegistryLoader.Load<WallDefinition>(Source("walls"));

    /// <summary>
    /// The shipped prefabs, validated against the shipped blocks and walls.
    /// Loaded through <see cref="PrefabRegistryLoader"/> on purpose: the tile
    /// arrays hold raw numeric ids, so nothing else proves they resolve.
    /// </summary>
    public static Registry<PrefabDefinition> Prefabs() =>
        PrefabRegistryLoader.Load(Source("prefabs"), Blocks(), Walls());
}
