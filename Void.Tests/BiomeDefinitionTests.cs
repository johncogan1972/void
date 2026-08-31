using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Void;

namespace Void.Tests;

/// <summary>
/// VOID-022 acceptance tests: the biome schema, its registry entry, and the
/// cross-registry validation that stops a broken biome reaching world
/// generation. Engine-free, like the rest of <c>Void.Tests</c>.
/// </summary>
public class BiomeDefinitionTests : IDisposable
{
    private readonly string _root;

    /// <summary>Creates a throwaway content directory per test.</summary>
    public BiomeDefinitionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "void-biome-tests-" + Path.GetRandomFileName());
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
    private void WriteFile(string name, string json) =>
        File.WriteAllText(Path.Combine(_root, name), json);

    /// <summary>
    /// Walks up from the test assembly to the repository root, identified by the
    /// shipped content tree. Tests must exercise the *shipped* JSON, not a copy,
    /// or the data file could rot without anything going red.
    /// </summary>
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "data", "biomes", "biomes.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// The shipped block and wall registries, taken from the real boot load
    /// (VOID-025) rather than loaded again here. A second chain over the same
    /// files would be a path that can drift from the one the game actually
    /// boots, which is the whole thing <c>ContentPaths</c> exists to prevent.
    /// </summary>
    private static Registry<BlockDefinition> ShippedBlocks() => ContentPaths.Blocks();

    /// <inheritdoc cref="ShippedBlocks"/>
    private static Registry<WallDefinition> ShippedWalls() => ContentPaths.Walls();

    /// <summary>Loads the temp root as biomes, validated against the shipped blocks and walls.</summary>
    private Registry<BiomeDefinition> LoadTempBiomes() =>
        BiomeRegistryLoader.Load(new DirectoryContentSource(_root), ShippedBlocks(), ShippedWalls());

    /// <summary>
    /// A minimal, valid underground biome the surface fixtures can pair with.
    /// Kept separate from the shipped data so a content edit cannot silently
    /// change what a validation test is asserting.
    /// </summary>
    private const string UndergroundJson = """
        {
          "id": "test:hollows",
          "display_name": "Hollows",
          "layer_category": "underground",
          "palette": {
            "surface_block": "void:dirt",
            "subsurface_block": "void:dirt",
            "base_block": "void:stone",
            "wall_default": "void:dirt_wall",
            "wall_ambient": []
          },
          "underground_variant": null
        }
        """;

    /// <summary>Builds a surface biome fixture with overridable palette and variant.</summary>
    private static string SurfaceJson(
        string id = "test:field",
        string surfaceBlock = "void:grass",
        string wallDefault = "void:dirt_wall",
        string? undergroundVariant = "test:hollows") =>
        $$"""
        {
          "id": "{{id}}",
          "display_name": "Field",
          "layer_category": "surface",
          "palette": {
            "surface_block": "{{surfaceBlock}}",
            "subsurface_block": "void:dirt",
            "base_block": "void:stone",
            "wall_default": "{{wallDefault}}",
            "wall_ambient": []
          },
          "underground_variant": {{(undergroundVariant is null ? "null" : $"\"{undergroundVariant}\"")}}
        }
        """;

    /// <summary>
    /// The shipped biome file must load clean against the shipped block and wall
    /// registries. If this goes red the game boots into a fatal content error —
    /// no world can be generated at all.
    /// </summary>
    [Fact]
    public void ShippedBiomesLoadAndCarryEveryAuthoredField()
    {
        Registry<BiomeDefinition> biomes = ContentPaths.Biomes();

        // VOID-048 took the roster to three surface biomes and their three
        // underground variants. Listed in full, in ordinal order, because the
        // order is the registry's determinism guarantee and not an accident of
        // how the file happens to be written.
        Assert.Equal(6, biomes.Count);
        Assert.Equal(
            new[]
            {
                "void:forest", "void:frostreach", "void:frozen_halls",
                "void:meadow", "void:root_hollows", "void:root_tangle",
            },
            biomes.Ids);

        BiomeDefinition meadow = biomes.Get("void:meadow");
        Assert.Equal("Meadow", meadow.DisplayName);
        Assert.Equal(LayerCategory.Surface, meadow.LayerCategory);
        Assert.Equal("void:grass", meadow.Palette.SurfaceBlock);
        Assert.Equal("void:dirt", meadow.Palette.SubsurfaceBlock);
        Assert.Equal("void:stone", meadow.Palette.BaseBlock);
        Assert.Equal("void:dirt_wall", meadow.Palette.WallDefault);
        Assert.Empty(meadow.Palette.WallAmbient);
        Assert.Equal("void:root_hollows", meadow.UndergroundVariant);
        // Emptied in VOID-025 because the oak/flower/grass prefabs it named do
        // not exist yet and boot now validates the refs; VOID-026 restores them
        // with the real prefabs. Asserted rather than ignored so re-adding a ref
        // has to come with the prefab.
        Assert.Empty(meadow.Vegetation.Trees);
        Assert.Empty(meadow.Vegetation.Plants);
        Assert.Empty(meadow.Vegetation.Decorations);
        Assert.Equal(1.2f, meadow.OreBiases.Multiplier("void:copper"));
        Assert.Equal(0.9f, meadow.OreBiases.Multiplier("void:iron"));
        Assert.Equal(4, meadow.Enemies.Count);
        Assert.Equal("void:rabbit", meadow.Enemies[0].EnemyId);
        Assert.Equal(SpawnTimeOfDay.Day, meadow.Enemies[0].TimeOfDay);
        Assert.Equal(SpawnTimeOfDay.Night, meadow.Enemies[2].TimeOfDay);
        Assert.Empty(meadow.Hazards);

        BiomeDefinition hollows = biomes.Get("void:root_hollows");
        Assert.Equal("Root Hollows", hollows.DisplayName);
        Assert.Equal(LayerCategory.Underground, hollows.LayerCategory);
        Assert.Null(hollows.UndergroundVariant);
        Assert.Equal(new[] { "void:stone_wall" }, hollows.Palette.WallAmbient);
        Assert.Equal(1.4f, hollows.OreBiases.Multiplier("void:copper"));
        Assert.Empty(hollows.Vegetation.Trees);
    }

    /// <summary>
    /// Ore ids nobody biased must come back as 1.0. If this goes red, biomes
    /// silently stop generating every ore they did not explicitly mention.
    /// </summary>
    [Fact]
    public void UnbiasedOresDefaultToOne()
    {
        OreBiasTable table = new(new[] { new KeyValuePair<string, float>("void:copper", 1.2f) });

        Assert.Equal(OreBiasTable.DefaultMultiplier, table.Multiplier("void:silver"));
        Assert.False(table.Contains("void:silver"));
    }

    /// <summary>
    /// Ore biases must iterate in ordinal-sorted key order no matter how the JSON
    /// was authored. A generator walking them in file or hash order would make
    /// the same seed produce different worlds on different machines.
    /// </summary>
    [Fact]
    public void OreBiasesIterateSortedRegardlessOfAuthoredOrder()
    {
        WriteFile("biomes.json", """
            [{
              "id": "test:hollows",
              "display_name": "Hollows",
              "layer_category": "underground",
              "palette": {
                "surface_block": "void:dirt",
                "subsurface_block": "void:dirt",
                "base_block": "void:stone",
                "wall_default": "void:dirt_wall",
                "wall_ambient": []
              },
              "ore_biases": { "void:zinc": 0.5, "void:copper": 1.2, "void:iron": 0.9 }
            }]
            """);

        BiomeDefinition biome = LoadTempBiomes().Get("test:hollows");

        Assert.Equal(
            new[] { "void:copper", "void:iron", "void:zinc" },
            biome.OreBiases.Select(static bias => bias.Key).ToArray());
        Assert.Equal(0.5f, biome.OreBiases.Multiplier("void:zinc"));
    }

    /// <summary>
    /// Definition → JSON → definition must be byte-identical. Round-tripping is
    /// how tooling rewrites content files; a lossy field would quietly delete
    /// authored data the next time a file was saved.
    /// </summary>
    [Fact]
    public void RoundTripIsByteIdentical()
    {
        Registry<BiomeDefinition> biomes = ContentPaths.Biomes();

        foreach (BiomeDefinition biome in biomes)
        {
            string first = JsonSerializer.Serialize(biome, RegistryLoader.Options);
            BiomeDefinition? reloaded = JsonSerializer.Deserialize<BiomeDefinition>(first, RegistryLoader.Options);
            Assert.NotNull(reloaded);
            string second = JsonSerializer.Serialize(reloaded, RegistryLoader.Options);

            Assert.Equal(first, second);
        }
    }

    /// <summary>
    /// The post-MVP ambient fields must survive a round-trip as null rather than
    /// materialising as empty strings — generation and audio treat null as "this
    /// biome deliberately has none".
    /// </summary>
    [Fact]
    public void NullAmbientFieldsRoundTripAsNull()
    {
        WriteFile("biomes.json", $"[{UndergroundJson}]");
        BiomeDefinition biome = LoadTempBiomes().Get("test:hollows");

        Assert.Null(biome.Ambient.MusicTheme);
        Assert.Null(biome.Ambient.ParticleEffect);
        Assert.Null(biome.Ambient.LightTint);

        string json = JsonSerializer.Serialize(biome, RegistryLoader.Options);
        BiomeDefinition? reloaded = JsonSerializer.Deserialize<BiomeDefinition>(json, RegistryLoader.Options);

        Assert.NotNull(reloaded);
        Assert.Null(reloaded.Ambient.MusicTheme);
        Assert.Null(reloaded.Ambient.ParticleEffect);
        Assert.Null(reloaded.Ambient.LightTint);
    }

    /// <summary>
    /// A light tint that is authored must survive as four components in order.
    /// Silent component loss would tint the world wrong with no error anywhere.
    /// </summary>
    [Fact]
    public void LightTintRoundTripsAsFourComponents()
    {
        WriteFile("biomes.json", """
            [{
              "id": "test:hollows",
              "display_name": "Hollows",
              "layer_category": "underground",
              "palette": {
                "surface_block": "void:dirt",
                "subsurface_block": "void:dirt",
                "base_block": "void:stone",
                "wall_default": "void:dirt_wall",
                "wall_ambient": []
              },
              "ambient": { "light_tint": [1.0, 0.98, 0.92, 1.0] }
            }]
            """);

        BiomeDefinition biome = LoadTempBiomes().Get("test:hollows");

        Assert.Equal(new BiomeLightTint(1.0f, 0.98f, 0.92f, 1.0f), biome.Ambient.LightTint);
    }

    /// <summary>
    /// An underground_variant naming a biome that does not exist is fatal, and
    /// the message names both the offending biome and the missing id. Without
    /// this, the underground generator would find no biome beneath a surface
    /// column and produce a hole through the world.
    /// </summary>
    [Fact]
    public void MissingUndergroundVariantIsFatal()
    {
        WriteFile("biomes.json", $"[{SurfaceJson(undergroundVariant: "test:nowhere")}]");

        ContentLoadException ex = Assert.Throws<ContentLoadException>(LoadTempBiomes);

        Assert.Contains("test:field", ex.Message, StringComparison.Ordinal);
        Assert.Contains("test:nowhere", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pairing a surface biome to another *surface* biome is fatal. The
    /// underground generator places the variant directly below ground level, so
    /// a surface variant would stack open-air terrain underground.
    /// </summary>
    [Fact]
    public void UndergroundVariantPointingAtSurfaceBiomeIsFatal()
    {
        WriteFile("biomes.json",
            $"[{SurfaceJson(undergroundVariant: "test:other")},{SurfaceJson(id: "test:other", undergroundVariant: null)}]");

        ContentLoadException ex = Assert.Throws<ContentLoadException>(LoadTempBiomes);

        Assert.Contains("test:other", ex.Message, StringComparison.Ordinal);
        Assert.Contains("underground", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A palette block id that does not resolve is fatal. A typo here would
    /// otherwise surface as an empty or wrong-tiled world long after load.
    /// </summary>
    [Fact]
    public void UnresolvablePaletteBlockIsFatal()
    {
        WriteFile("biomes.json", $"[{SurfaceJson(surfaceBlock: "void:not_a_block", undergroundVariant: null)}]");

        ContentLoadException ex = Assert.Throws<ContentLoadException>(LoadTempBiomes);

        Assert.Contains("void:not_a_block", ex.Message, StringComparison.Ordinal);
        Assert.Contains("surface_block", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Same again for walls, which resolve through a separate registry.</summary>
    [Fact]
    public void UnresolvablePaletteWallIsFatal()
    {
        WriteFile("biomes.json", $"[{SurfaceJson(wallDefault: "void:not_a_wall", undergroundVariant: null)}]");

        ContentLoadException ex = Assert.Throws<ContentLoadException>(LoadTempBiomes);

        Assert.Contains("void:not_a_wall", ex.Message, StringComparison.Ordinal);
        Assert.Contains("wall_default", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The deferred prefab/enemy check must be fatal once the registries exist
    /// (VOID-023, VOID-024). If this goes red, a biome could name a prefab that
    /// never spawns and nothing would say so.
    /// </summary>
    [Fact]
    public void DeferredReferenceValidationIsFatalOnDanglingRefs()
    {
        // The vegetation half uses a fixture, not the shipped file: no shipped
        // biome names a prefab any more (VOID-025 emptied meadow's lists), and
        // the check must stay proven regardless of what content happens to hold.
        WriteFile("biomes.json", $$"""
            [{
              "id": "test:grove",
              "display_name": "Grove",
              "layer_category": "surface",
              "palette": {
                "surface_block": "void:grass",
                "subsurface_block": "void:dirt",
                "base_block": "void:stone",
                "wall_default": "void:dirt_wall",
                "wall_ambient": []
              },
              "vegetation": {
                "trees": [{ "prefab": "test:missing_oak", "weight": 1.0 }],
                "plants": [],
                "decorations": []
              },
              "underground_variant": null
            }]
            """);

        // Vegetation refs parse into PrefabRef before they are ever resolved, and
        // this is now the only fixture in the suite carrying one: the shipped
        // meadow lists were emptied until VOID-026 authors real prefabs. Assert
        // the parsed shape here, or `weight` could silently bind to 0 and every
        // restored vegetation entry would be unselectable with nothing red.
        PrefabRef tree = Assert.Single(LoadTempBiomes()["test:grove"].Vegetation.Trees);
        Assert.Equal("test:missing_oak", tree.Prefab);
        Assert.Equal(1.0f, tree.Weight);

        ContentLoadException prefabEx = Assert.Throws<ContentLoadException>(
            () => BiomeRegistryLoader.ValidateDeferredReferences(
                LoadTempBiomes(), Array.Empty<string>(), Array.Empty<string>()));
        Assert.Contains("prefab", prefabEx.Message, StringComparison.Ordinal);
        Assert.Contains("test:missing_oak", prefabEx.Message, StringComparison.Ordinal);
        Assert.Contains("test:grove", prefabEx.Message, StringComparison.Ordinal);

        Registry<BiomeDefinition> shipped = ContentPaths.Biomes();

        string[] prefabs = shipped
            .SelectMany(static b => b.Vegetation.Trees.Concat(b.Vegetation.Plants).Concat(b.Vegetation.Decorations))
            .Select(static p => p.Prefab)
            .ToArray();

        // Which biome gets reported is derived, not hardcoded: the guarantee
        // under test is that validation walks the registry in ordinal order and
        // so blames the same biome on every machine. Naming a biome literally
        // here would instead break every time the roster grows a new
        // alphabetically-earlier entry -- which is precisely what VOID-048 did
        // when 'void:forest' displaced 'void:meadow' as the first spawn pool.
        BiomeDefinition firstWithEnemies = shipped.First(static b => b.Enemies.Count > 0);

        ContentLoadException enemyEx = Assert.Throws<ContentLoadException>(
            () => BiomeRegistryLoader.ValidateDeferredReferences(shipped, prefabs, Array.Empty<string>()));
        Assert.Contains("enemy", enemyEx.Message, StringComparison.Ordinal);
        Assert.Contains(firstWithEnemies.Id, enemyEx.Message, StringComparison.Ordinal);
        Assert.Contains(firstWithEnemies.Enemies[0].EnemyId, enemyEx.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And it must pass when every ref resolves — otherwise wiring VOID-023 and
    /// VOID-024 in would break a perfectly valid content set.
    /// </summary>
    [Fact]
    public void DeferredReferenceValidationPassesWhenRefsResolve()
    {
        Registry<BiomeDefinition> biomes = ContentPaths.Biomes();

        string[] prefabs = biomes
            .SelectMany(static b => b.Vegetation.Trees.Concat(b.Vegetation.Plants).Concat(b.Vegetation.Decorations))
            .Select(static p => p.Prefab)
            .ToArray();
        string[] enemies = biomes.SelectMany(static b => b.Enemies).Select(static e => e.EnemyId).ToArray();

        BiomeRegistryLoader.ValidateDeferredReferences(biomes, prefabs, enemies);
    }

    /// <summary>
    /// Ore keys must sort by <c>StringComparer.Ordinal</c>, not by culture. The
    /// two disagree on case and on punctuation weight, so a culture-sensitive
    /// comparer would order these four keys differently under a non-invariant
    /// locale — the same seed generating a different world on a French machine.
    /// This is the regression that made <c>ImmutableSortedDictionary</c> unusable
    /// here; if it goes red the table has drifted back to a cultural comparison.
    /// </summary>
    [Fact]
    public void OreBiasesSortOrdinallyNotByCulture()
    {
        OreBiasTable table = new(new[]
        {
            new KeyValuePair<string, float>("void:coal", 1.0f),
            new KeyValuePair<string, float>("void:copper", 1.1f),
            new KeyValuePair<string, float>("void:Copper", 1.2f),
            new KeyValuePair<string, float>("void:co_al", 1.3f),
        });

        // Ordinal: 'C' (0x43) before 'c' (0x63), and '_' (0x5F) before 'a' (0x61).
        // A culture-sensitive sort produces coal, co_al, copper, Copper instead.
        Assert.Equal(
            new[] { "void:Copper", "void:co_al", "void:coal", "void:copper" },
            table.Select(static bias => bias.Key).ToArray());
        Assert.Equal(1.2f, table.Multiplier("void:Copper"));
        Assert.Equal(1.1f, table.Multiplier("void:copper"));
    }

    /// <summary>
    /// Two documents authoring the same ore keys in different orders must yield
    /// byte-for-byte the same iteration sequence. Sorting once is not enough on
    /// its own: if any authoring order leaked through — via hash order or file
    /// order — two players with identically-meaning content files would generate
    /// divergent worlds from one seed.
    /// </summary>
    [Fact]
    public void OreBiasOrderIsIdenticalAcrossLoadsOfDifferentlyAuthoredDocuments()
    {
        const string Head = """
            {
              "id": "test:hollows",
              "display_name": "Hollows",
              "layer_category": "underground",
              "palette": {
                "surface_block": "void:dirt",
                "subsurface_block": "void:dirt",
                "base_block": "void:stone",
                "wall_default": "void:dirt_wall",
                "wall_ambient": []
              },
            """;

        BiomeDefinition? forward = JsonSerializer.Deserialize<BiomeDefinition>(
            Head + """ "ore_biases": { "void:copper": 1.2, "void:iron": 0.9, "void:zinc": 0.5 } }""",
            RegistryLoader.Options);
        BiomeDefinition? reversed = JsonSerializer.Deserialize<BiomeDefinition>(
            Head + """ "ore_biases": { "void:zinc": 0.5, "void:iron": 0.9, "void:copper": 1.2 } }""",
            RegistryLoader.Options);

        Assert.NotNull(forward);
        Assert.NotNull(reversed);
        Assert.Equal(forward.OreBiases.ToArray(), reversed.OreBiases.ToArray());
        Assert.Equal(
            new[] { "void:copper", "void:iron", "void:zinc" },
            reversed.OreBiases.Select(static bias => bias.Key).ToArray());
    }

    /// <summary>
    /// Hazards and populated ambient fields must load and round-trip with their
    /// values intact. No shipped biome authors either yet, so without this test
    /// the whole hazard path and the non-null ambient path are unexercised: a
    /// field dropped symmetrically in both read and write would still pass the
    /// serialize/deserialize/serialize comparison, and the first portal-world
    /// biome would silently generate with no hazards.
    /// </summary>
    [Fact]
    public void HazardsAndPopulatedAmbientSurviveLoadAndRoundTrip()
    {
        WriteFile("biomes.json", """
            [{
              "id": "test:ashfall",
              "display_name": "Ashfall",
              "layer_category": "void",
              "palette": {
                "surface_block": "void:stone",
                "subsurface_block": "void:stone",
                "base_block": "void:stone",
                "wall_default": "void:stone_wall",
                "wall_ambient": []
              },
              "ambient": {
                "music_theme": "void:ashfall_theme",
                "particle_effect": "void:falling_ash",
                "light_tint": [0.8, 0.4, 0.3, 1.0]
              },
              "hazards": [
                { "type": "heat", "intensity": 0.75 },
                { "type": "ash_choke", "intensity": 0.25 }
              ]
            }]
            """);

        BiomeDefinition biome = LoadTempBiomes().Get("test:ashfall");

        Assert.Equal("void:ashfall_theme", biome.Ambient.MusicTheme);
        Assert.Equal("void:falling_ash", biome.Ambient.ParticleEffect);
        Assert.Equal(new BiomeLightTint(0.8f, 0.4f, 0.3f, 1.0f), biome.Ambient.LightTint);
        Assert.Equal(2, biome.Hazards.Count);
        Assert.Equal("heat", biome.Hazards[0].Type);
        Assert.Equal(0.75f, biome.Hazards[0].Intensity);
        Assert.Equal("ash_choke", biome.Hazards[1].Type);
        Assert.Equal(0.25f, biome.Hazards[1].Intensity);

        string json = JsonSerializer.Serialize(biome, RegistryLoader.Options);
        BiomeDefinition? reloaded = JsonSerializer.Deserialize<BiomeDefinition>(json, RegistryLoader.Options);

        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded.Hazards.Count);
        Assert.Equal("ash_choke", reloaded.Hazards[1].Type);
        Assert.Equal(0.25f, reloaded.Hazards[1].Intensity);
        Assert.Equal(biome.Ambient.LightTint, reloaded.Ambient.LightTint);
        Assert.Equal(json, JsonSerializer.Serialize(reloaded, RegistryLoader.Options));
    }
    /// <summary>The shipped biome directory, as a content source.</summary>
    private static DirectoryContentSource BiomeSource() =>
        new DirectoryContentSource(Path.Combine(RepoRoot(), "data", "biomes"));

    /// <summary>
    /// The generic loader refuses biomes outright. It sees one source and
    /// cannot check a palette id against the block registry, so letting it
    /// return would hand back a registry that parsed cleanly and resolves to
    /// nothing — the failure would not surface until generation produced a
    /// world of air, a long way from the call that caused it.
    /// </summary>
    [Fact]
    public void GenericLoaderRefusesBiomesBecauseItCannotValidateThem()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => RegistryLoader.Load<BiomeDefinition>(BiomeSource()));

        // The message has to name the way out, or the next caller just deletes the call.
        Assert.Contains("loader", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The builder-merging entry point is guarded too. Closing only
    /// <c>Load</c> would leave the identical hole one method along, which is
    /// how a rule that exists in one place becomes a rule that exists nowhere.
    /// </summary>
    [Fact]
    public void GenericLoadIntoRefusesBiomesToo()
    {
        RegistryBuilder<BiomeDefinition> builder = new();

        Assert.Throws<InvalidOperationException>(
            () => RegistryLoader.LoadInto(builder, BiomeSource()));
    }
}
