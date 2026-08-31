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
    public const string WorldTypesFolder = "world_types";

    // Spelled once so the Registries table below reads as a column of
    // declarations rather than a column of bare "true"s.
    private const bool Required = true;

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
    /// <item><c>world_types</c> — arithmetic validation is self-contained; the
    /// biome ids their classification rules name are resolved in the closing
    /// step below.</item>
    /// </list>
    /// Biome vegetation prefabs and spawn-pool enemies close the one cycle in
    /// the graph, so they are checked after everything is loaded — see
    /// <see cref="BiomeRegistryLoader.ValidateDeferredReferences"/> — and world
    /// types' biome references are resolved in the same closing step.
    /// <para><see cref="LoadAll"/> below walks these folders in exactly this
    /// order, and <c>LoadAllRequestsFoldersInTheDeclaredLoadOrder</c> asserts
    /// the two agree — without that test this list could quietly become a lie,
    /// which is worse than not having it.</para>
    /// <para>The same table also declares which registries the game refuses to
    /// boot without (VOID-014). All eight are required today: every one of them
    /// is load-bearing for world generation, and an empty one has never yet
    /// meant anything but a path or packaging mistake. A registry that can
    /// genuinely ship empty gets <c>required: false</c> here — and nothing else
    /// changes, because the check reads this table rather than a list of its
    /// own.</para>
    /// </summary>
    public static IReadOnlyList<ContentRegistrySpec> Registries { get; } = new[]
    {
        new ContentRegistrySpec(BlocksFolder, Required, static c => c.Blocks.Count),
        new ContentRegistrySpec(WallsFolder, Required, static c => c.Walls.Count),
        new ContentRegistrySpec(ItemsFolder, Required, static c => c.Items.Count),
        new ContentRegistrySpec(LootTablesFolder, Required, static c => c.LootTables.Count),
        new ContentRegistrySpec(EnemiesFolder, Required, static c => c.Enemies.Count),
        new ContentRegistrySpec(BiomesFolder, Required, static c => c.Biomes.Count),
        new ContentRegistrySpec(PrefabsFolder, Required, static c => c.Prefabs.Count),
        new ContentRegistrySpec(WorldTypesFolder, Required, static c => c.WorldTypes.Count),
    };

    /// <summary>
    /// The folder names alone, in load order. Derived from
    /// <see cref="Registries"/> rather than written out a second time, so the
    /// order a reader sees and the order that is checked cannot drift apart.
    /// </summary>
    public static IReadOnlyList<string> LoadOrder { get; } =
        Array.ConvertAll((ContentRegistrySpec[])Registries, static spec => spec.Folder);

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

        // Where each folder was actually read from, kept purely so the
        // empty-registry failure in step 8 can name the path it searched. The
        // likely causes of an empty registry are all path or packaging
        // problems, so the folder name on its own would not be enough to act on.
        Dictionary<string, string> searchedPaths = new(StringComparer.Ordinal);

        // 1. Blocks and walls: raw numeric ids, referenced by everything below.
        Registry<BlockDefinition> blocks =
            RegistryLoader.Load<BlockDefinition>(Source(sourceFactory, BlocksFolder, searchedPaths));
        Registry<WallDefinition> walls =
            RegistryLoader.Load<WallDefinition>(Source(sourceFactory, WallsFolder, searchedPaths));

        // 2. Items: referenced by loot tables, reference nothing themselves.
        Registry<ItemDefinition> items =
            RegistryLoader.Load<ItemDefinition>(Source(sourceFactory, ItemsFolder, searchedPaths));

        // 3. Loot tables: every guaranteed drop and weighted entry names an item.
        Registry<LootTableDefinition> lootTables =
            LootTableRegistryLoader.Load(Source(sourceFactory, LootTablesFolder, searchedPaths), items);

        // 4. Enemies: each names at most one loot table.
        Registry<EnemyDefinition> enemies =
            EnemyRegistryLoader.Load(Source(sourceFactory, EnemiesFolder, searchedPaths), lootTables);

        // 5. Biomes: palette resolves against blocks and walls here; the prefab
        //    and enemy refs cannot be checked yet because prefabs load below.
        Registry<BiomeDefinition> biomes =
            BiomeRegistryLoader.Load(Source(sourceFactory, BiomesFolder, searchedPaths), blocks, walls);

        // 6. Prefabs: tile arrays resolve against the block and wall numeric ids.
        Registry<PrefabDefinition> prefabs =
            PrefabRegistryLoader.Load(Source(sourceFactory, PrefabsFolder, searchedPaths), blocks, walls);

        // 7. World types: layer proportions, size presets and biome
        //    classification. Their only cross-registry references are resolved
        //    in step 8, so their position in the order is free; they load last
        //    of the parsing steps to keep the reference chain above intact.
        Registry<WorldTypeDefinition> worldTypes =
            WorldTypeRegistryLoader.Load(Source(sourceFactory, WorldTypesFolder, searchedPaths));

        // 8. The deferred half of biome validation, now that both registries it
        //    needs exist. Runs last rather than being retried mid-sequence: a
        //    dangling vegetation prefab or spawn enemy is a content bug and must
        //    stop boot, not shrink the biome to whatever happened to resolve.
        BiomeRegistryLoader.ValidateDeferredReferences(biomes, prefabs.Ids, enemies.Ids);

        //    World types close the same way: every biome classification rule
        //    names a surface biome (VOID-048). Checked here rather than inside
        //    the world-type loader so that loader stays callable with nothing
        //    but its own documents.
        WorldTypeRegistryLoader.ValidateDeferredReferences(worldTypes, biomes);

        GameContent content = new(blocks, walls, items, lootTables, enemies, biomes, prefabs, worldTypes);

        // 9. Nothing above notices a registry that simply loaded nothing —
        //    an empty folder is a clean parse and resolves no references. This
        //    is the catch-all for "the content did not arrive at all", and it
        //    runs last because a partially-missing tree usually trips a
        //    cross-reference first, with a more specific message.
        RequiredContentValidator.Validate(content, Registries, searchedPaths);

        return content;
    }

    /// <summary>
    /// Asks the factory for one folder's source and refuses a null answer, so a
    /// factory with a missing case fails naming the folder instead of throwing a
    /// bare <see cref="NullReferenceException"/> inside a loader.
    /// </summary>
    /// <param name="searchedPaths">
    /// Recorded into, not read: the source's description is captured here as
    /// the load happens, because by the time step 9 finds an empty registry the
    /// source object is gone.
    /// </param>
    private static IContentSource Source(
        Func<string, IContentSource> factory,
        string folder,
        Dictionary<string, string> searchedPaths)
    {
        IContentSource? source = factory(folder);

        if (source is null)
        {
            throw new ContentLoadException(
                $"Content source factory returned null for folder '{folder}'.");
        }

        searchedPaths[folder] = source.Description;
        return source;
    }
}
