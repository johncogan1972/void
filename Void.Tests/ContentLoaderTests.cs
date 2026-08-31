using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Void;

namespace Void.Tests;

/// <summary>
/// VOID-025 acceptance tests for <see cref="ContentLoader"/>: the single boot
/// path that loads all seven registries, in order, and fails loudly on any
/// reference that does not resolve.
///
/// <para>Engine-free, like the rest of <c>Void.Tests</c> — the loader takes a
/// source factory precisely so these tests can drive the exact code path the
/// game boots with, using <see cref="DirectoryContentSource"/> instead of
/// <c>GodotContentSource</c>.</para>
/// </summary>
public class ContentLoaderTests
{
    /// <summary>
    /// Serves a fixed set of documents. Used to inject one deliberately broken
    /// definition alongside the shipped ones.
    /// </summary>
    private sealed class InMemoryContentSource : IContentSource
    {
        /// <summary>Documents to serve, in the order given. Never reordered here.</summary>
        private readonly IReadOnlyList<ContentDocument> _documents;

        /// <summary>Wraps a fixed document list; the caller owns the ordering.</summary>
        public InMemoryContentSource(IReadOnlyList<ContentDocument> documents) => _documents = documents;

        /// <inheritdoc/>
        public string Description => "in-memory test source";

        /// <inheritdoc/>
        public IEnumerable<ContentDocument> ReadAll() => _documents;
    }

    /// <summary>
    /// Decorator that re-splits every JSON array document into one document per
    /// entry and yields them in reverse order.
    /// </summary>
    /// <remarks>
    /// This is the whole point of the determinism test: the shipped tree keeps
    /// each registry in a single array file, so simply re-reading it could never
    /// prove that load order does not leak into registry order. Exploding and
    /// reversing produces a source that means exactly the same content in a
    /// genuinely different order — two players whose files differ only in
    /// authoring order must still generate the same world from one seed.
    /// </remarks>
    private sealed class ReversedContentSource : IContentSource
    {
        /// <summary>Source whose documents are exploded and re-ordered.</summary>
        private readonly IContentSource _inner;

        /// <summary>
        /// Whether to reverse after exploding. The <c>false</c> case exists only
        /// so a test can compare exploded-forward against exploded-reversed and
        /// prove the reversal is really happening; production never uses it.
        /// </summary>
        private readonly bool _reverse;

        /// <summary>Explodes array documents, optionally reversing the result.</summary>
        public ReversedContentSource(IContentSource inner, bool reverse = true)
        {
            _inner = inner;
            _reverse = reverse;
        }

        /// <inheritdoc/>
        public string Description => $"reversed({_inner.Description})";

        /// <inheritdoc/>
        public IEnumerable<ContentDocument> ReadAll()
        {
            List<ContentDocument> exploded = new();

            foreach (ContentDocument document in _inner.ReadAll())
            {
                using JsonDocument parsed = JsonDocument.Parse(document.Json, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });

                if (parsed.RootElement.ValueKind != JsonValueKind.Array)
                {
                    exploded.Add(document);
                    continue;
                }

                int index = 0;
                foreach (JsonElement element in parsed.RootElement.EnumerateArray())
                {
                    exploded.Add(new ContentDocument(
                        $"{document.Name}#{index++}", element.GetRawText()));
                }
            }

            if (_reverse)
            {
                exploded.Reverse();
            }

            return exploded;
        }
    }

    /// <summary>
    /// A factory over the shipped tree with one folder's documents replaced by
    /// the shipped ones plus <paramref name="extra"/>.
    /// </summary>
    private static Func<string, IContentSource> WithExtra(
        string folder, string documentName, string json) =>
        candidate =>
        {
            if (!string.Equals(candidate, folder, StringComparison.Ordinal))
            {
                return ContentPaths.Source(candidate);
            }

            List<ContentDocument> documents = ContentPaths.Source(folder).ReadAll().ToList();
            documents.Add(new ContentDocument(documentName, json));
            return new InMemoryContentSource(documents);
        };

    /// <summary>Builds a biome fixture with overridable palette, vegetation and spawn pool.</summary>
    private static string BiomeJson(
        string surfaceBlock = "void:grass",
        string vegetationTrees = "",
        string enemies = "") =>
        $$"""
        {
          "id": "test:broken",
          "display_name": "Broken",
          "layer_category": "surface",
          "palette": {
            "surface_block": "{{surfaceBlock}}",
            "subsurface_block": "void:dirt",
            "base_block": "void:stone",
            "wall_default": "void:dirt_wall",
            "wall_ambient": []
          },
          "vegetation": { "trees": [{{vegetationTrees}}], "plants": [], "decorations": [] },
          "enemies": [{{enemies}}],
          // Names a shipped variant rather than null: this fixture is added
          // alongside the real biome documents, and since VOID-048 a surface
          // biome without an underground_variant is fatal at load. Leaving it
          // null would make every test using this helper fail on the pairing
          // rule before reaching the dangling ref it is actually about.
          "underground_variant": "void:root_hollows"
        }
        """;

    /// <summary>
    /// The shipped tree must load through the real boot path with all seven
    /// registries populated. If this goes red the game boots into a fatal
    /// content error and no world can be generated at all.
    /// </summary>
    [Fact]
    public void ShippedContentLoadsWithEverySevenRegistriesPopulated()
    {
        GameContent content = ContentLoader.LoadAll(static folder => ContentPaths.Source(folder));

        Assert.NotEmpty(content.Blocks);
        Assert.NotEmpty(content.Walls);
        Assert.NotEmpty(content.Items);
        Assert.NotEmpty(content.LootTables);
        Assert.NotEmpty(content.Enemies);
        Assert.NotEmpty(content.Biomes);
        Assert.NotEmpty(content.Prefabs);
    }

    /// <summary>
    /// Registry iteration order must depend only on the ids, never on the order
    /// the documents arrived in. Registry order feeds world generation, so a
    /// load-order leak here is the same seed producing different worlds on two
    /// machines whose content files were merged differently.
    /// </summary>
    [Fact]
    public void RegistryOrderIsIdenticalAcrossDifferentlyOrderedSources()
    {
        GameContent forward = ContentLoader.LoadAll(static folder => ContentPaths.Source(folder));
        GameContent reversed = ContentLoader.LoadAll(
            static folder => new ReversedContentSource(ContentPaths.Source(folder)));

        Assert.Equal(forward.Blocks.Ids, reversed.Blocks.Ids);
        Assert.Equal(forward.Walls.Ids, reversed.Walls.Ids);
        Assert.Equal(forward.Items.Ids, reversed.Items.Ids);
        Assert.Equal(forward.LootTables.Ids, reversed.LootTables.Ids);
        Assert.Equal(forward.Enemies.Ids, reversed.Enemies.Ids);
        Assert.Equal(forward.Biomes.Ids, reversed.Biomes.Ids);
        Assert.Equal(forward.Prefabs.Ids, reversed.Prefabs.Ids);

        // Guards the guard. Without this, a decorator that stopped reordering
        // would leave every assert above comparing two identical orderings —
        // green forever, proving nothing. Compare the decorator against itself
        // with reversal disabled, so only the reversal can make them differ:
        // the exploding, which both sides do, cannot.
        string[] explodedForward = ExplodedIds(reverse: false);
        string[] explodedReversed = ExplodedIds(reverse: true);

        Assert.NotEmpty(explodedForward);
        Assert.Equal(explodedForward.Reverse(), explodedReversed);
    }

    /// <summary>
    /// Ids of the shipped blocks in the order the decorator actually hands them
    /// to the loader — the ids, not the document names, because a name differs
    /// between the two modes purely from exploding.
    /// </summary>
    private static string[] ExplodedIds(bool reverse) =>
        new ReversedContentSource(ContentPaths.Source("blocks"), reverse)
            .ReadAll()
            // Same options the decorator and the production loader use. Parsing
            // with the defaults instead works right up until a shipped block
            // carries an interior comment, then fails on the content rather than
            // on anything this test is about -- which is exactly what VOID-048's
            // snow/ice entries tripped.
            .Select(static d => JsonDocument.Parse(d.Json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }).RootElement.GetProperty("id").GetString()!)
            .ToArray();

    /// <summary>
    /// <see cref="ContentLoader.LoadOrder"/> must describe what
    /// <see cref="ContentLoader.LoadAll"/> really does. The list is the only
    /// place a reader can see the dependency order, so if the two drift it
    /// becomes a confident lie: someone reorders the loads, breaks a
    /// cross-reference, and the documented order still says it is fine.
    /// </summary>
    [Fact]
    public void LoadAllRequestsFoldersInTheDeclaredLoadOrder()
    {
        List<string> requested = new();

        ContentLoader.LoadAll(folder =>
        {
            requested.Add(folder);
            return ContentPaths.Source(folder);
        });

        Assert.Equal(ContentLoader.LoadOrder, requested);
    }

    /// <summary>
    /// A biome palette naming a block nobody registered must stop boot, naming
    /// both. Downgraded to a warning it would generate a world of air instead.
    /// </summary>
    [Fact]
    public void BiomeNamingMissingBlockFailsLoudly()
    {
        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => ContentLoader.LoadAll(
                WithExtra("biomes", "test_broken.json", BiomeJson(surfaceBlock: "void:not_a_block"))));

        Assert.Contains("test:broken", ex.Message, StringComparison.Ordinal);
        Assert.Contains("void:not_a_block", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A loot table entry naming a missing item must stop boot. In play it would
    /// be a table that silently grants nothing — invisible until someone farmed
    /// the drop for an hour.
    /// </summary>
    [Fact]
    public void LootTableNamingMissingItemFailsLoudly()
    {
        const string Json = """
            {
              "id": "test:broken_table",
              "entries": [
                { "item_id": "void:not_an_item", "drop_chance": 0.5,
                  "rarity_weights": { "common": 1.0 } }
              ]
            }
            """;

        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => ContentLoader.LoadAll(WithExtra("loot_tables", "test_broken.json", Json)));

        Assert.Contains("test:broken_table", ex.Message, StringComparison.Ordinal);
        Assert.Contains("void:not_an_item", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An enemy naming a missing loot table must stop boot rather than be
    /// treated as "drops nothing" — that is what a null table already means, so
    /// conflating them would hide the typo forever.
    /// </summary>
    [Fact]
    public void EnemyNamingMissingLootTableFailsLoudly()
    {
        const string Json = """
            {
              "id": "test:broken_enemy",
              "display_name": "Broken",
              "sprite": "res://art/enemies/broken.png",
              "loot_table_id": "void:not_a_table"
            }
            """;

        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => ContentLoader.LoadAll(WithExtra("enemies", "test_broken.json", Json)));

        Assert.Contains("test:broken_enemy", ex.Message, StringComparison.Ordinal);
        Assert.Contains("void:not_a_table", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A biome spawn pool naming a missing enemy must stop boot. This is one
    /// half of the deferred check VOID-025 wired in; unwired, the biome would
    /// simply spawn nothing.
    /// </summary>
    [Fact]
    public void BiomeNamingMissingEnemyFailsLoudly()
    {
        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => ContentLoader.LoadAll(WithExtra(
                "biomes",
                "test_broken.json",
                BiomeJson(enemies: """{ "enemy_id": "void:not_an_enemy", "weight": 1.0 }"""))));

        Assert.Contains("test:broken", ex.Message, StringComparison.Ordinal);
        Assert.Contains("void:not_an_enemy", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the deferred check: a vegetation prefab that does not
    /// exist. This is exactly the failure the shipped meadow had before VOID-025
    /// emptied its lists, so it must stay red-on-dangle.
    /// </summary>
    [Fact]
    public void BiomeNamingMissingPrefabFailsLoudly()
    {
        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => ContentLoader.LoadAll(WithExtra(
                "biomes",
                "test_broken.json",
                BiomeJson(vegetationTrees: """{ "prefab": "void:not_a_prefab", "weight": 1.0 }"""))));

        Assert.Contains("test:broken", ex.Message, StringComparison.Ordinal);
        Assert.Contains("void:not_a_prefab", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Regression guard on the shipped tree itself: every vegetation prefab and
    /// spawn enemy authored in <c>data/biomes</c> must resolve. Someone adding a
    /// biome ref without the prefab goes red here, in seconds, rather than in a
    /// failed boot.
    /// </summary>
    [Fact]
    public void ShippedBiomesHaveNoDanglingPrefabOrEnemyRefs()
    {
        GameContent content = ContentLoader.LoadAll(static folder => ContentPaths.Source(folder));

        BiomeRegistryLoader.ValidateDeferredReferences(
            content.Biomes, content.Prefabs.Ids, content.Enemies.Ids);
    }
}
