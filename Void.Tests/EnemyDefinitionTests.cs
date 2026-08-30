using System;
using System.IO;
using System.Text.Json;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// VOID-023 acceptance tests for the enemy schema and the cross-registry check
/// tying an enemy to its one loot table (loot-table-spec §10). Stats, AI and
/// behaviour are Phase 9 and deliberately absent, so nothing here asserts them.
/// </summary>
public class EnemyDefinitionTests : IDisposable
{
    private readonly string _root;

    /// <summary>Creates a throwaway content directory per test.</summary>
    public EnemyDefinitionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "void-enemy-tests-" + Path.GetRandomFileName());
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
    private void WriteFile(string json) => File.WriteAllText(Path.Combine(_root, "enemies.json"), json);

    /// <summary>Loads the temp root as enemies, validated against the shipped loot tables.</summary>
    private Registry<EnemyDefinition> LoadTemp() =>
        EnemyRegistryLoader.Load(new DirectoryContentSource(_root), ContentPaths.LootTables());

    /// <summary>
    /// The shipped enemy file must load clean against the shipped loot tables,
    /// with every field as authored. If this goes red, no biome can spawn
    /// anything — the spawn pools resolve against this registry.
    /// </summary>
    [Fact]
    public void ShippedEnemiesLoadAndCarryEveryAuthoredField()
    {
        Registry<EnemyDefinition> enemies = ContentPaths.Enemies();

        Assert.Equal(5, enemies.Count);
        Assert.Equal(
            new[]
            {
                "void:cave_beetle", "void:deer", "void:grey_wolf", "void:rabbit", "void:small_skeleton",
            },
            enemies.Ids);

        EnemyDefinition rabbit = enemies.Get("void:rabbit");
        Assert.Equal("Rabbit", rabbit.DisplayName);
        Assert.Equal("res://assets/enemies/rabbit.png", rabbit.SpritePath);
        Assert.Equal("void:rabbit_loot", rabbit.LootTableId);

        // Null is a deliberate "drops nothing yet", not a missing value.
        Assert.Null(enemies.Get("void:grey_wolf").LootTableId);
    }

    /// <summary>
    /// Every <c>enemy_id</c> in the shipped biome spawn pools must resolve.
    /// VOID-022 left those refs dangling on purpose; if this goes red, a biome
    /// names a creature that cannot be spawned and generation silently produces
    /// an empty world.
    /// </summary>
    [Fact]
    public void EveryShippedBiomeSpawnResolvesToAnEnemy()
    {
        Registry<EnemyDefinition> enemies = ContentPaths.Enemies();
        Registry<BiomeDefinition> biomes = ContentPaths.Biomes();

        foreach (BiomeDefinition biome in biomes)
        {
            foreach (BiomeEnemySpawn spawn in biome.Enemies)
            {
                Assert.True(
                    enemies.Contains(spawn.EnemyId),
                    $"Biome '{biome.Id}' spawns '{spawn.EnemyId}', which is not a registered enemy.");
            }
        }
    }

    /// <summary>
    /// Round-trip must be lossless, or tooling rewriting an enemy file would
    /// silently drop its loot table. The serialise-compare passes vacuously if a
    /// field is dropped symmetrically, so values are asserted on the reloaded
    /// object too.
    /// </summary>
    [Fact]
    public void RoundTripIsByteIdenticalAndKeepsFieldValues()
    {
        foreach (EnemyDefinition enemy in ContentPaths.Enemies())
        {
            string first = JsonSerializer.Serialize(enemy, RegistryLoader.Options);
            EnemyDefinition? reloaded = JsonSerializer.Deserialize<EnemyDefinition>(first, RegistryLoader.Options);
            Assert.NotNull(reloaded);
            string second = JsonSerializer.Serialize(reloaded, RegistryLoader.Options);

            Assert.Equal(first, second);
            Assert.Equal(enemy.Id, reloaded.Id);
            Assert.Equal(enemy.DisplayName, reloaded.DisplayName);
            Assert.Equal(enemy.SpritePath, reloaded.SpritePath);
            Assert.Equal(enemy.LootTableId, reloaded.LootTableId);
        }
    }

    /// <summary>
    /// A loot table id that does not resolve is fatal, naming both the enemy and
    /// the missing table. Downgrading it to "drops nothing" would make a typo
    /// indistinguishable from the legal null case, and the enemy would silently
    /// never drop anything for the life of the project.
    /// </summary>
    [Fact]
    public void UnresolvableLootTableIdIsFatal()
    {
        WriteFile("""
            [{ "id": "test:ghoul", "display_name": "Ghoul", "loot_table_id": "void:not_a_table" }]
            """);

        ContentLoadException ex = Assert.Throws<ContentLoadException>(LoadTemp);

        Assert.Contains("test:ghoul", ex.Message, StringComparison.Ordinal);
        Assert.Contains("void:not_a_table", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A null loot table id must load fine — an enemy that drops nothing is a
    /// legitimate design, and rejecting it would force fake empty tables into the
    /// content set.
    /// </summary>
    [Fact]
    public void NullLootTableIdLoadsFine()
    {
        WriteFile("""
            [{ "id": "test:ghoul", "display_name": "Ghoul", "loot_table_id": null }]
            """);

        Assert.Null(LoadTemp().Get("test:ghoul").LootTableId);
    }

    /// <summary>
    /// The generic loader must refuse enemies. If this goes red, a caller can
    /// build an enemy registry whose loot table ids were never checked.
    /// </summary>
    [Fact]
    public void GenericRegistryLoaderRefusesEnemies()
    {
        DirectoryContentSource source = ContentPaths.Source("enemies");

        Assert.Throws<InvalidOperationException>(() => RegistryLoader.Load<EnemyDefinition>(source));
        Assert.Throws<InvalidOperationException>(
            () => RegistryLoader.LoadInto(new RegistryBuilder<EnemyDefinition>(), source));
    }
}
