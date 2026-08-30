using System;
using System.IO;
using Void;

namespace Void.Tests;

/// <summary>
/// Locates the <b>shipped</b> content tree and loads it through the real boot
/// path (VOID-023, VOID-024, VOID-025).
///
/// <para>Tests must exercise the real <c>data/</c> files rather than a copy: the
/// whole point of the registries is that block drops, biome spawn pools and
/// prefab tile data resolve against what actually ships, and a copied fixture
/// would let the shipped file rot without anything going red.</para>
///
/// <para>Just as deliberately, this type no longer chains the individual loaders
/// itself — it calls <see cref="ContentLoader.LoadAll"/> with a
/// <see cref="DirectoryContentSource"/> factory, which is exactly what the game
/// does with a <c>GodotContentSource</c> factory. A second load path in the
/// tests would be a path that can drift from the one that boots.</para>
/// </summary>
internal static class ContentPaths
{
    // Loading the whole tree per test property would re-parse every file dozens
    // of times across the suite. The result is immutable, so one shared load is
    // safe; it is lazy so a test that never touches content pays nothing, and
    // Lazy's default thread safety covers xunit's parallel collections.
    private static readonly Lazy<GameContent> Loaded = new(
        static () => ContentLoader.LoadAll(static folder => Source(folder)));

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

    /// <summary>
    /// The whole shipped content set, loaded and cross-validated exactly as the
    /// game loads it. Shared across tests, so treat it as read-only.
    /// </summary>
    public static GameContent All() => Loaded.Value;

    /// <summary>The shipped items.</summary>
    public static Registry<ItemDefinition> Items() => All().Items;

    /// <summary>The shipped loot tables, validated against the shipped items.</summary>
    public static Registry<LootTableDefinition> LootTables() => All().LootTables;

    /// <summary>The shipped enemies, validated against the shipped loot tables.</summary>
    public static Registry<EnemyDefinition> Enemies() => All().Enemies;

    /// <summary>The shipped blocks, keyed by string id and by numeric id.</summary>
    public static Registry<BlockDefinition> Blocks() => All().Blocks;

    /// <summary>The shipped walls.</summary>
    public static Registry<WallDefinition> Walls() => All().Walls;

    /// <summary>The shipped biomes, with every palette, prefab and enemy ref resolved.</summary>
    public static Registry<BiomeDefinition> Biomes() => All().Biomes;

    /// <summary>The shipped prefabs, whose tile arrays resolve against blocks and walls.</summary>
    public static Registry<PrefabDefinition> Prefabs() => All().Prefabs;
}
