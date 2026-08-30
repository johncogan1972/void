using System;
using System.Collections.Generic;

namespace Void;

/// <summary>
/// The one entry point that turns the shipped <c>data/</c> tree into a frozen,
/// fully cross-validated <see cref="GameContent"/> (VOID-025).
///
/// <para>Engine-free on purpose: it takes a factory from folder name to
/// <see cref="IContentSource"/>, so the game passes <c>GodotContentSource</c>
/// over <c>res://data/</c> and the tests pass <see cref="DirectoryContentSource"/>
/// over the repository's <c>data/</c> — the same code path both times. Tests
/// must never chain the individual loaders themselves; a second load path is a
/// path that can drift from the one that actually boots.</para>
///
/// <para><b>Failure is always fatal.</b> There is no retry over unresolved
/// references and no downgrade to a warning. A retry loop would let a genuine
/// content bug settle into a partial load, and world generation reading a
/// partial registry produces a broken world rather than an error.</para>
/// </summary>
public static class ContentLoader
{
    // One folder per registry, named here rather than spelled inline so the
    // source factory, LoadOrder and LoadAll cannot disagree about a name. These
    // are directory names under data/ and are matched ordinally; renaming one
    // renames the shipped folder too.
    public const string BlocksFolder = "blocks";
    public const string WallsFolder = "walls";
    public const string ItemsFolder = "items";
    public const string LootTablesFolder = "loot_tables";
    public const string EnemiesFolder = "enemies";
    public const string BiomesFolder = "biomes";
    public const string PrefabsFolder = "prefabs";

    /// <summary>
    /// The load order, declared once and in full, because it is the part of
    /// content loading a reader most needs to see. Each step may only reference
    /// registries already loaded above it:
    /// <list type="number">
    /// <item><c>blocks</c>, <c>walls</c> — numeric ids, reference nothing.</item>
    /// <item><c>items</c> — references nothing.</item>
    /// <item><c>loot_tables</c> — every entry names an item.</item>
    /// <item><c>enemies</c> — each names at most one loot table.</item>
    /// <item><c>biomes</c> — palette names blocks and walls; variant names a biome.</item>
    /// <item><c>prefabs</c> — tile arrays hold raw block and wall numeric ids.</item>
    /// </list>
    /// Biome vegetation prefabs and spawn-pool enemies close the one cycle in
    /// the graph, so they are checked after everything is loaded — see
    /// <see cref="BiomeRegistryLoader.ValidateDeferredReferences"/>.
    /// <para><see cref="LoadAll"/> below walks these folders in exactly this
    /// order, and <c>LoadAllRequestsFoldersInTheDeclaredLoadOrder</c> asserts
    /// the two agree — without that test this list could quietly become a lie,
    /// which is worse than not having it.</para>
    /// </summary>
    public static IReadOnlyList<string> LoadOrder { get; } = new[]
    {
        BlocksFolder,
        WallsFolder,
        ItemsFolder,
        LootTablesFolder,
        EnemiesFolder,
        BiomesFolder,
        PrefabsFolder,
    };

    /// <summary>
    /// Loads every registry, in <see cref="LoadOrder"/>, and cross-validates
    /// them.
    /// </summary>
    /// <param name="sourceFactory">
    /// Maps a folder name from <see cref="LoadOrder"/> to the source to read it
    /// from. Called once per folder, in order; must not return null.
    /// </param>
    /// <exception cref="ContentLoadException">
    /// On any malformed document, duplicate id, or reference that does not
    /// resolve. The message names both the referrer and the missing id, and the
    /// load stops there — never a partial <see cref="GameContent"/>.
    /// </exception>
    public static GameContent LoadAll(Func<string, IContentSource> sourceFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);

        // 1. Blocks and walls: raw numeric ids, referenced by everything below.
        Registry<BlockDefinition> blocks =
            RegistryLoader.Load<BlockDefinition>(Source(sourceFactory, BlocksFolder));
        Registry<WallDefinition> walls =
            RegistryLoader.Load<WallDefinition>(Source(sourceFactory, WallsFolder));

        // 2. Items: referenced by loot tables, reference nothing themselves.
        Registry<ItemDefinition> items =
            RegistryLoader.Load<ItemDefinition>(Source(sourceFactory, ItemsFolder));

        // 3. Loot tables: every guaranteed drop and weighted entry names an item.
        Registry<LootTableDefinition> lootTables =
            LootTableRegistryLoader.Load(Source(sourceFactory, LootTablesFolder), items);

        // 4. Enemies: each names at most one loot table.
        Registry<EnemyDefinition> enemies =
            EnemyRegistryLoader.Load(Source(sourceFactory, EnemiesFolder), lootTables);

        // 5. Biomes: palette resolves against blocks and walls here; the prefab
        //    and enemy refs cannot be checked yet because prefabs load below.
        Registry<BiomeDefinition> biomes =
            BiomeRegistryLoader.Load(Source(sourceFactory, BiomesFolder), blocks, walls);

        // 6. Prefabs: tile arrays resolve against the block and wall numeric ids.
        Registry<PrefabDefinition> prefabs =
            PrefabRegistryLoader.Load(Source(sourceFactory, PrefabsFolder), blocks, walls);

        // 7. The deferred half of biome validation, now that both registries it
        //    needs exist. Runs last rather than being retried mid-sequence: a
        //    dangling vegetation prefab or spawn enemy is a content bug and must
        //    stop boot, not shrink the biome to whatever happened to resolve.
        BiomeRegistryLoader.ValidateDeferredReferences(biomes, prefabs.Ids, enemies.Ids);

        return new GameContent(blocks, walls, items, lootTables, enemies, biomes, prefabs);
    }

    /// <summary>
    /// Asks the factory for one folder's source and refuses a null answer, so a
    /// factory with a missing case fails naming the folder instead of throwing a
    /// bare <see cref="NullReferenceException"/> inside a loader.
    /// </summary>
    private static IContentSource Source(Func<string, IContentSource> factory, string folder)
    {
        IContentSource? source = factory(folder);

        if (source is null)
        {
            throw new ContentLoadException(
                $"Content source factory returned null for folder '{folder}'.");
        }

        return source;
    }
}
