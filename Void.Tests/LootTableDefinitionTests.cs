using System;
using System.IO;
using System.Text.Json;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// VOID-023 acceptance tests for the loot table schema (loot-table-spec §4-§5)
/// and the cross-registry validation that stops a table granting items that do
/// not exist. Shape only: rolling, rarity selection and Legendary naming are
/// Phase 5 and are not exercised here because they do not exist yet.
/// </summary>
public class LootTableDefinitionTests : IDisposable
{
    private readonly string _root;

    /// <summary>Creates a throwaway content directory per test.</summary>
    public LootTableDefinitionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "void-loot-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    /// <summary>Removes the temp directory so runs do not accumulate leftovers.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Drops a JSON document into the temp content root.</summary>
    private void WriteFile(string json) => File.WriteAllText(Path.Combine(_root, "loot.json"), json);

    /// <summary>Loads the temp root as loot tables, validated against the shipped items.</summary>
    private Registry<LootTableDefinition> LoadTemp() =>
        LootTableRegistryLoader.Load(new DirectoryContentSource(_root), ContentPaths.Items());

    /// <summary>
    /// The shipped loot tables must load clean against the shipped items, with
    /// every field as authored. If this goes red, nothing that drops loot can be
    /// loaded — the game boots into a fatal content error.
    /// </summary>
    [Fact]
    public void ShippedLootTablesLoadAndCarryEveryAuthoredField()
    {
        Registry<LootTableDefinition> tables = ContentPaths.LootTables();

        Assert.Equal(3, tables.Count);
        Assert.Equal(
            new[] { "void:cave_beetle_loot", "void:rabbit_loot", "void:small_skeleton_loot" },
            tables.Ids);

        LootTableDefinition rabbit = tables.Get("void:rabbit_loot");
        GuaranteedDrop drop = Assert.Single(rabbit.GuaranteedDrops);
        Assert.Equal("void:dirt_block", drop.ItemId);
        Assert.Equal(Rarity.Common, drop.Rarity);
        Assert.Equal(1, drop.Count);
        Assert.Null(drop.NameOverride);

        LootEntry entry = Assert.Single(rabbit.Entries);
        Assert.Equal("void:wood_block", entry.ItemId);
        Assert.Equal(0.25f, entry.DropChance);
        Assert.Equal(0.9f, entry.RarityWeights.Common);
        Assert.Equal(0.1f, entry.RarityWeights.Uncommon);
        Assert.Equal(0.0f, entry.RarityWeights.Legendary);
        Assert.Equal(new CountRange(1, 2), entry.CountRange);
        Assert.Null(entry.Conditions);

        LootTableDefinition skeleton = tables.Get("void:small_skeleton_loot");
        Assert.Equal("Chipped Bone Fragment", skeleton.GuaranteedDrops[0].NameOverride);
        Assert.Equal(Rarity.Uncommon, skeleton.GuaranteedDrops[0].Rarity);
        Assert.Equal(2, skeleton.GuaranteedDrops[0].Count);
        Assert.True(skeleton.Entries[0].Conditions?.FirstKillOnly);

        LootTableDefinition beetle = tables.Get("void:cave_beetle_loot");
        Assert.Empty(beetle.GuaranteedDrops);
        Assert.Equal(2, beetle.Entries.Count);
        Assert.Equal(new CountRange(2, 4), beetle.Entries[1].CountRange);
        Assert.Equal("void:underground_explored", beetle.Entries[1].Conditions?.RequiresFlag);
        Assert.Equal("void:beetle_swarm_cleared", beetle.Entries[1].Conditions?.RequiresNoFlag);
    }

    /// <summary>
    /// Round-trip must be lossless, or tooling that rewrites a loot table would
    /// silently delete authored drops. The serialise-compare passes vacuously if
    /// a field is dropped symmetrically, so specific values are asserted on the
    /// reloaded object too.
    /// </summary>
    [Fact]
    public void RoundTripIsByteIdenticalAndKeepsFieldValues()
    {
        foreach (LootTableDefinition table in ContentPaths.LootTables())
        {
            string first = JsonSerializer.Serialize(table, RegistryLoader.Options);
            LootTableDefinition? reloaded =
                JsonSerializer.Deserialize<LootTableDefinition>(first, RegistryLoader.Options);
            Assert.NotNull(reloaded);
            string second = JsonSerializer.Serialize(reloaded, RegistryLoader.Options);

            Assert.Equal(first, second);
            Assert.Equal(table.Id, reloaded.Id);
            Assert.Equal(table.Description, reloaded.Description);
            Assert.Equal(table.GuaranteedDrops.Count, reloaded.GuaranteedDrops.Count);
            Assert.Equal(table.Entries.Count, reloaded.Entries.Count);

            for (int i = 0; i < table.Entries.Count; i++)
            {
                Assert.Equal(table.Entries[i].ItemId, reloaded.Entries[i].ItemId);
                Assert.Equal(table.Entries[i].DropChance, reloaded.Entries[i].DropChance);
                Assert.Equal(table.Entries[i].CountRange, reloaded.Entries[i].CountRange);
                Assert.Equal(
                    table.Entries[i].RarityWeights.Legendary,
                    reloaded.Entries[i].RarityWeights.Legendary);
                Assert.Equal(
                    table.Entries[i].Conditions?.RequiresFlag,
                    reloaded.Entries[i].Conditions?.RequiresFlag);
            }
        }
    }

    /// <summary>
    /// <c>count_range</c> must serialise as the spec's two-element array, not as
    /// an object. Content files are authored by hand against the spec; an object
    /// form would make every existing loot file unreadable.
    /// </summary>
    [Fact]
    public void CountRangeSerialisesAsTwoElementArray()
    {
        string json = JsonSerializer.Serialize(new CountRange(2, 5), RegistryLoader.Options);

        Assert.Equal("[2,5]", json);
        Assert.Equal(new CountRange(2, 5), JsonSerializer.Deserialize<CountRange>(json, RegistryLoader.Options));
    }

    /// <summary>
    /// An item id that does not resolve is fatal, and the message names both the
    /// table and the missing id. Without this, the table loads clean and the
    /// drop simply never appears in play — with nothing anywhere saying why.
    /// </summary>
    [Fact]
    public void UnresolvableItemIdIsFatalAndNamesTableAndItem()
    {
        WriteFile("""
            [{
              "id": "test:table",
              "entries": [
                { "item_id": "void:not_an_item", "drop_chance": 0.5, "count_range": [1, 1], "rarity_weights": { "common": 1.0 } }
              ]
            }]
            """);

        ContentLoadException ex = Assert.Throws<ContentLoadException>(LoadTemp);

        Assert.Contains("test:table", ex.Message, StringComparison.Ordinal);
        Assert.Contains("void:not_an_item", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Same again for a guaranteed drop, which validates through a separate path.</summary>
    [Fact]
    public void UnresolvableGuaranteedDropItemIsFatal()
    {
        WriteFile("""
            [{
              "id": "test:table",
              "guaranteed_drops": [
                { "item_id": "void:not_an_item", "rarity": "rare", "count": 1 }
              ]
            }]
            """);

        ContentLoadException ex = Assert.Throws<ContentLoadException>(LoadTemp);

        Assert.Contains("test:table", ex.Message, StringComparison.Ordinal);
        Assert.Contains("void:not_an_item", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An inverted count range is fatal. Clamping it instead would leave the
    /// entry dropping nothing forever, which reads in play as bad luck rather
    /// than as a content bug.
    /// </summary>
    [Fact]
    public void InvertedCountRangeIsFatal()
    {
        WriteFile("""
            [{
              "id": "test:table",
              "entries": [
                { "item_id": "void:dirt_block", "drop_chance": 0.5, "count_range": [5, 2], "rarity_weights": { "common": 1.0 } }
              ]
            }]
            """);

        Assert.Throws<ContentLoadException>(LoadTemp);
    }

    /// <summary>A negative count is fatal for the same reason; counts cannot be negative.</summary>
    [Fact]
    public void NegativeCountRangeIsFatal()
    {
        WriteFile("""
            [{
              "id": "test:table",
              "entries": [
                { "item_id": "void:dirt_block", "drop_chance": 0.5, "count_range": [-1, 2], "rarity_weights": { "common": 1.0 } }
              ]
            }]
            """);

        Assert.Throws<ContentLoadException>(LoadTemp);
    }

    /// <summary>
    /// A drop chance outside 0.0-1.0 is fatal. Above 1.0 silently means "always"
    /// and below 0.0 silently means "never"; both hide the authoring mistake that
    /// caused them.
    /// </summary>
    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.2)]
    public void DropChanceOutsideZeroToOneIsFatal(double chance)
    {
        WriteFile($$"""
            [{
              "id": "test:table",
              "entries": [
                { "item_id": "void:dirt_block", "drop_chance": {{chance}}, "count_range": [1, 1], "rarity_weights": { "common": 1.0 } }
              ]
            }]
            """);

        ContentLoadException ex = Assert.Throws<ContentLoadException>(LoadTemp);

        Assert.Contains("drop_chance", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A negative rarity weight is fatal. It would shrink the sum the roll
    /// normalises by and skew every other tier of the same entry, with no error
    /// anywhere downstream.
    /// </summary>
    [Fact]
    public void NegativeRarityWeightIsFatal()
    {
        WriteFile("""
            [{
              "id": "test:table",
              "entries": [{
                "item_id": "void:dirt_block",
                "drop_chance": 0.5,
                "rarity_weights": { "common": 1.0, "rare": -0.5 },
                "count_range": [1, 1]
              }]
            }]
            """);

        ContentLoadException ex = Assert.Throws<ContentLoadException>(LoadTemp);

        Assert.Contains("rare", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The generic loader must refuse loot tables. If this goes red, a caller can
    /// build a registry of tables whose item ids were never checked and which
    /// therefore grant nothing at all.
    /// </summary>
    [Fact]
    public void GenericRegistryLoaderRefusesLootTables()
    {
        DirectoryContentSource source = ContentPaths.Source("loot_tables");

        Assert.Throws<InvalidOperationException>(() => RegistryLoader.Load<LootTableDefinition>(source));
        Assert.Throws<InvalidOperationException>(
            () => RegistryLoader.LoadInto(new RegistryBuilder<LootTableDefinition>(), source));
    }

    /// <summary>
    /// <see cref="CountRange"/> validates in its constructor, so a bad range is
    /// fatal on <b>every</b> construction path, not only the JSON one. Code that
    /// builds a range directly — future loot tooling, tests, Phase 5 rolling —
    /// must hit the same guard; if this goes red, an inverted range can be built
    /// in memory and only the data files are actually protected.
    /// </summary>
    [Theory]
    [InlineData(5, 2)]
    [InlineData(-1, 3)]
    [InlineData(1, -3)]
    [InlineData(-2, -1)]
    public void CountRangeConstructorRejectsBadBoundsDirectly(int min, int max)
    {
        Assert.Throws<ContentLoadException>(() => new CountRange(min, max));
    }

    /// <summary>
    /// The defaults an author gets by omitting optional fields must all be the
    /// safe-on-omission choice: a Common-only weight set with a non-zero sum, a
    /// range of exactly one, and a guaranteed drop of one Common item. The unsafe
    /// alternatives are silent — all-zero weights make the rarity roll divide by
    /// a zero sum, a [0, 0] range drops nothing, and a count of 0 grants nothing,
    /// none of which reports an error anywhere.
    /// </summary>
    [Fact]
    public void OmittedOptionalFieldsTakeSafeDefaults()
    {
        WriteFile("""
            [{
              "id": "test:defaults",
              "guaranteed_drops": [ { "item_id": "void:dirt_block" } ],
              "entries": [ {
                "item_id": "void:stone_block",
                "drop_chance": 0.5,
                "rarity_weights": { "common": 1.0 }
              } ]
            }]
            """);

        LootTableDefinition table = LoadTemp().Get("test:defaults");

        GuaranteedDrop drop = Assert.Single(table.GuaranteedDrops);
        Assert.Equal(1, drop.Count);
        Assert.Equal(Rarity.Common, drop.Rarity);
        Assert.Null(drop.NameOverride);

        LootEntry entry = Assert.Single(table.Entries);
        Assert.Equal(new CountRange(1, 1), entry.CountRange);
        Assert.Null(entry.Conditions);

        // Weights are authored, never inferred: an unwritten tier stays zero.
        Assert.Equal(1.0f, entry.RarityWeights.Common);
        Assert.Equal(0.0f, entry.RarityWeights.Uncommon);
        Assert.Equal(0.0f, entry.RarityWeights.Rare);
        Assert.Equal(0.0f, entry.RarityWeights.Legendary);
    }

    /// <summary>
    /// An entry that leaves out <c>drop_chance</c> is refused rather than
    /// defaulted to zero. Zero is a legal value, so it cannot double as "not
    /// written" — an entry that loads clean and never drops anything for the
    /// life of the game is exactly what the range check exists to prevent.
    /// </summary>
    [Fact]
    public void OmittedDropChanceIsFatal()
    {
        WriteFile("""
            [{
              "id": "test:no_chance",
              "entries": [ {
                "item_id": "void:stone_block",
                "rarity_weights": { "common": 1.0 }
              } ]
            }]
            """);

        Assert.Throws<ContentLoadException>(() => LoadTemp());
    }

    /// <summary>
    /// A partially authored weights block does not acquire a tier nobody wrote.
    /// Defaulting common to 1 would turn <c>{"rare": 0.5}</c> into common 1 /
    /// rare 0.5 — an invented common drop, twice as likely as the rare one the
    /// author actually asked for, with no error anywhere.
    /// </summary>
    [Fact]
    public void PartiallyAuthoredWeightsDoNotInventACommonTier()
    {
        WriteFile("""
            [{
              "id": "test:partial_weights",
              "entries": [ {
                "item_id": "void:stone_block",
                "drop_chance": 0.5,
                "rarity_weights": { "rare": 0.5 }
              } ]
            }]
            """);

        LootEntry entry = Assert.Single(LoadTemp().Get("test:partial_weights").Entries);

        Assert.Equal(0.0f, entry.RarityWeights.Common);
        Assert.Equal(0.5f, entry.RarityWeights.Rare);
    }

    /// <summary>
    /// Weights that are all zero are refused. There is nothing to normalise by,
    /// so the rarity roll has no tier to land on — the same silent nothing a
    /// negative weight causes, reached by omission instead of by typo.
    /// </summary>
    [Fact]
    public void AllZeroRarityWeightsAreFatal()
    {
        WriteFile("""
            [{
              "id": "test:zero_weights",
              "entries": [ {
                "item_id": "void:stone_block",
                "drop_chance": 0.5,
                "rarity_weights": { "common": 0.0 }
              } ]
            }]
            """);

        ContentLoadException error = Assert.Throws<ContentLoadException>(() => LoadTemp());
        Assert.Contains("all zero", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Guards the write half of the round trip on the fields the existing
    /// round-trip test does not read back. A field dropped on <i>serialise</i> is
    /// invisible to a serialise-compare — both strings lack it — and invisible to
    /// the shipped-file load test, which never serialises. Losing
    /// <c>name_override</c> or <c>first_kill_only</c> that way would turn a boss
    /// trophy into a repeatable generic drop the first time tooling rewrote the
    /// file.
    /// </summary>
    [Fact]
    public void RoundTripPreservesGuaranteedDropAndConditionFields()
    {
        LootTableDefinition original = ContentPaths.LootTables().Get("void:small_skeleton_loot");

        string json = JsonSerializer.Serialize(original, RegistryLoader.Options);
        LootTableDefinition? reloaded =
            JsonSerializer.Deserialize<LootTableDefinition>(json, RegistryLoader.Options);
        Assert.NotNull(reloaded);

        GuaranteedDrop drop = Assert.Single(reloaded.GuaranteedDrops);
        Assert.Equal("void:stone_block", drop.ItemId);
        Assert.Equal(Rarity.Uncommon, drop.Rarity);
        Assert.Equal(2, drop.Count);
        Assert.Equal("Chipped Bone Fragment", drop.NameOverride);

        LootEntry entry = Assert.Single(reloaded.Entries);
        Assert.True(entry.Conditions?.FirstKillOnly);
        Assert.Equal(0.6f, entry.RarityWeights.Common);
        Assert.Equal(0.3f, entry.RarityWeights.Uncommon);
        Assert.Equal(0.09f, entry.RarityWeights.Rare);
        Assert.Equal(0.01f, entry.RarityWeights.Legendary);

        // The flag-gated table exercises the other two condition fields.
        LootTableDefinition beetle = ContentPaths.LootTables().Get("void:cave_beetle_loot");
        LootConditions? conditions = JsonSerializer.Deserialize<LootTableDefinition>(
            JsonSerializer.Serialize(beetle, RegistryLoader.Options), RegistryLoader.Options)
            ?.Entries[1].Conditions;

        Assert.Equal("void:underground_explored", conditions?.RequiresFlag);
        Assert.Equal("void:beetle_swarm_cleared", conditions?.RequiresNoFlag);
    }
}
