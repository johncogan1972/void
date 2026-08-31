using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Void;

namespace Void.Tests;

/// <summary>
/// VOID-018 acceptance tests: block and wall definitions, and the numeric-id
/// support they added to the content layer. Engine-free, like the rest of
/// <c>Void.Tests</c>.
/// </summary>
public class BlockWallDefinitionTests : IDisposable
{
    private readonly string _root;

    /// <summary>
    /// Creates a throwaway content directory per test.
    /// </summary>
    public BlockWallDefinitionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "void-blockwall-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Removes the temp directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Drops a JSON document into the temp content root.
    /// </summary>
    private void WriteFile(string name, string json)
    {
        string path = Path.Combine(_root, name);
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads the temp root as blocks, through the engine-free filesystem source.
    /// </summary>
    private Registry<BlockDefinition> LoadBlocks() =>
        RegistryLoader.Load<BlockDefinition>(new DirectoryContentSource(_root));

    /// <summary>
    /// Loads the temp root as walls, through the engine-free filesystem source.
    /// </summary>
    private Registry<WallDefinition> LoadWalls() =>
        RegistryLoader.Load<WallDefinition>(new DirectoryContentSource(_root));

    private const string AirJson = """
        {
          "id": "void:air",
          "block_id": 0,
          "display_name": "Air",
          "sprite": "",
          "hardness": 0,
          "drop_item_id": null,
          "collision": "none",
          "blocks_light": false
        }
        """;

    private const string StoneJson = """
        {
          "id": "void:stone",
          "block_id": 2,
          "display_name": "Stone",
          "sprite": "res://assets/tiles/stone.png",
          "hardness": 30,
          "drop_item_id": "void:stone_block",
          "collision": "solid",
          "blocks_light": true
        }
        """;

    private const string PlatformJson = """
        {
          "id": "void:wood_platform",
          "block_id": 7,
          "display_name": "Wood Platform",
          "sprite": "res://assets/tiles/wood_platform.png",
          "hardness": 5,
          "drop_item_id": "void:wood_platform_item",
          "collision": "platform",
          "blocks_light": false
        }
        """;

    private const string StoneWallJson = """
        {
          "id": "void:stone_wall",
          "wall_id": 2,
          "display_name": "Stone Wall",
          "sprite": "res://assets/tiles/stone_wall.png"
        }
        """;

    /// <summary>
    /// Every declared field survives the load, including the enum and the nullable
    /// drop. A field that silently failed to bind would ship as a default value.
    /// </summary>
    [Fact]
    public void BlocksLoadFromJsonWithEveryFieldPopulated()
    {
        WriteFile("blocks.json", $"[{AirJson}, {StoneJson}, {PlatformJson}]");

        Registry<BlockDefinition> registry = LoadBlocks();

        BlockDefinition stone = registry.Get("void:stone");
        Assert.Equal((ushort)2, stone.NumericId);
        Assert.Equal("Stone", stone.DisplayName);
        Assert.Equal("res://assets/tiles/stone.png", stone.SpritePath);
        Assert.Equal(30, stone.Hardness);
        Assert.Equal("void:stone_block", stone.DropItemId);
        Assert.Equal(BlockCollision.Solid, stone.Collision);
        Assert.True(stone.BlocksLight);

        BlockDefinition platform = registry.Get("void:wood_platform");
        Assert.Equal(BlockCollision.Platform, platform.Collision);
        Assert.False(platform.BlocksLight);
    }

    /// <summary>
    /// Air is a real registry entry, not an absence (world-data-model-spec §2).
    ///
    /// A tile holding air still carries wall, liquid and flags — that is how an
    /// interior room exists at all. If air were modelled as "no entry", every
    /// walled-in empty tile would lose its wall.
    /// </summary>
    [Fact]
    public void AirIsARealEntryAtNumericZero()
    {
        WriteFile("blocks.json", $"[{AirJson}, {StoneJson}]");

        Registry<BlockDefinition> registry = LoadBlocks();

        BlockDefinition air = registry.GetByNumericId(ContentIds.AirBlock);
        Assert.Equal("void:air", air.Id);
        Assert.Equal(BlockCollision.None, air.Collision);
        Assert.Equal(0, air.Hardness);
        Assert.Null(air.DropItemId);
    }

    /// <summary>
    /// Same rule for wall 0: "no wall" is an entry that can be looked up, which NPC
    /// housing validity depends on being able to ask about (GDD §5.5).
    /// </summary>
    [Fact]
    public void NoWallIsARealEntryAtNumericZero()
    {
        WriteFile("walls.json", $$"""
            [
              { "id": "void:no_wall", "wall_id": 0, "display_name": "No Wall", "sprite": "" },
              {{StoneWallJson}}
            ]
            """);

        Registry<WallDefinition> registry = LoadWalls();

        Assert.True(registry.TryGetByNumericId(ContentIds.NoWall, out WallDefinition none));
        Assert.Equal("void:no_wall", none.Id);
        Assert.Equal("Stone Wall", registry.GetByNumericId(2).DisplayName);
    }

    /// <summary>
    /// The numeric id is declared in the JSON and never derived.
    ///
    /// Saved worlds store the raw number, so an id that shifted with load order or
    /// array position would silently reinterpret every existing world. Declared out
    /// of order across files that sort the other way, so any such reliance fails.
    /// </summary>
    [Fact]
    public void NumericIdComesFromJsonNotLoadOrder()
    {
        // Declared out of order, across files whose names sort the other way, so
        // any reliance on load order or array position would show up here.
        WriteFile("01_later.json", """
            [
              { "id": "void:d", "block_id": 40 },
              { "id": "void:c", "block_id": 30 }
            ]
            """);
        WriteFile("02_earlier.json", """
            [
              { "id": "void:b", "block_id": 20 },
              { "id": "void:a", "block_id": 10 }
            ]
            """);

        Registry<BlockDefinition> registry = LoadBlocks();

        Assert.Equal((ushort)10, registry.Get("void:a").NumericId);
        Assert.Equal((ushort)20, registry.Get("void:b").NumericId);
        Assert.Equal((ushort)30, registry.Get("void:c").NumericId);
        Assert.Equal((ushort)40, registry.Get("void:d").NumericId);

        // String-id ordering is unchanged by the addition of numeric ids.
        Assert.Equal(new[] { "void:a", "void:b", "void:c", "void:d" }, registry.Ids);
    }

    /// <summary>
    /// Colliding numeric ids are fatal and name both files. Last-writer-wins would
    /// make which block you get depend on filesystem enumeration order.
    /// </summary>
    [Fact]
    public void DuplicateNumericIdNamesNumberAndBothFiles()
    {
        WriteFile("a_first.json", """{ "id": "void:one", "block_id": 12 }""");
        WriteFile("b_second.json", """{ "id": "void:two", "block_id": 12 }""");

        ContentLoadException ex = Assert.Throws<ContentLoadException>(() => LoadBlocks());

        Assert.Contains("12", ex.Message, StringComparison.Ordinal);
        Assert.Contains("a_first.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("b_second.json", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The string-id check still applies to numeric definitions: distinct numbers do
    /// not excuse a duplicate id.
    /// </summary>
    [Fact]
    public void DuplicateStringIdWithDistinctNumericsStillNamesIdAndBothFiles()
    {
        WriteFile("a_first.json", """{ "id": "void:dupe", "block_id": 12 }""");
        WriteFile("b_second.json", """{ "id": "void:dupe", "block_id": 13 }""");

        ContentLoadException ex = Assert.Throws<ContentLoadException>(() => LoadBlocks());

        Assert.Contains("void:dupe", ex.Message, StringComparison.Ordinal);
        Assert.Contains("a_first.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("b_second.json", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The numeric check lives in the shared builder, so walls inherit it rather
    /// than reimplementing it.
    /// </summary>
    [Fact]
    public void DuplicateWallNumericIdIsAlsoFatal()
    {
        WriteFile("a.json", """{ "id": "void:w1", "wall_id": 4 }""");
        WriteFile("b.json", """{ "id": "void:w2", "wall_id": 4 }""");

        ContentLoadException ex = Assert.Throws<ContentLoadException>(() => LoadWalls());

        Assert.Contains("4", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unmapped number is a bug in data or code — it throws with the number in
    /// the message, while the probing form stays silent for callers expecting misses.
    /// </summary>
    [Fact]
    public void UnknownNumericIdThrowsNamingTheNumber()
    {
        WriteFile("blocks.json", StoneJson);
        Registry<BlockDefinition> registry = LoadBlocks();

        ContentLoadException ex = Assert.Throws<ContentLoadException>(() => registry.GetByNumericId(999));

        Assert.Contains("999", ex.Message, StringComparison.Ordinal);
        Assert.False(registry.TryGetByNumericId(999, out _));
    }

    /// <summary>
    /// Numeric lookup on a string-only registry is a programming error, and the
    /// message has to say why rather than reporting an empty result.
    /// </summary>
    [Fact]
    public void NumericLookupOnNonNumericRegistryThrowsClearly()
    {
        RegistryBuilder<SampleDefinition> builder = new();
        builder.Add(new SampleDefinition { Id = "void:a" }, "a.json");
        Registry<SampleDefinition> registry = builder.Build();

        InvalidOperationException ex =
            Assert.Throws<InvalidOperationException>(() => registry.GetByNumericId(0));

        Assert.Contains(nameof(INumericContentDefinition), ex.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => registry.TryGetByNumericId(0, out _));
    }

    /// <summary>
    /// A block survives JSON round-trip with no field dropped, across the three
    /// shapes that differ structurally: air (null drop, empty sprite), a solid, and
    /// a platform. See AssertRoundTrips for how "identical" is judged.
    /// </summary>
    [Theory]
    [InlineData(AirJson)]
    [InlineData(StoneJson)]
    [InlineData(PlatformJson)]
    public void BlockDefinitionRoundTripsWithoutLosingAField(string sourceJson)
    {
        AssertRoundTrips<BlockDefinition>(sourceJson);
    }

    /// <summary>
    /// The same guarantee for walls, which have a smaller field set.
    /// </summary>
    [Fact]
    public void WallDefinitionRoundTripsWithoutLosingAField()
    {
        AssertRoundTrips<WallDefinition>(StoneWallJson);
    }

    /// <summary>
    /// Deserialises <paramref name="sourceJson"/>, re-serialises with the same
    /// options, and compares both sides normalised (parsed to a
    /// <see cref="JsonNode"/> and re-emitted with sorted property order) so the
    /// assertion is about content, not key order or whitespace. Also asserts
    /// every key present in the source survives the trip, which is what actually
    /// catches a property the loader silently drops.
    /// </summary>
    private static void AssertRoundTrips<T>(string sourceJson)
    {
        T? value = JsonSerializer.Deserialize<T>(sourceJson, RegistryLoader.Options);
        Assert.NotNull(value);

        string emitted = JsonSerializer.Serialize(value, RegistryLoader.Options);

        List<string> sourceKeys = PropertyKeys(sourceJson);
        List<string> emittedKeys = PropertyKeys(emitted);

        foreach (string key in sourceKeys)
        {
            Assert.Contains(key, emittedKeys);
        }

        Assert.Equal(Normalise(sourceJson), Normalise(emitted));
    }

    private static List<string> PropertyKeys(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<string> keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    /// <summary>Canonical form: properties ordinal-sorted, no insignificant whitespace.</summary>
    private static string Normalise(string json)
    {
        JsonNode? node = JsonNode.Parse(json);
        Assert.NotNull(node);
        return Canonical(node).ToJsonString();
    }

    private static JsonNode Canonical(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                List<string> names = obj.Select(p => p.Key).ToList();
                names.Sort(StringComparer.Ordinal);

                JsonObject sorted = new();
                foreach (string name in names)
                {
                    JsonNode? child = obj[name];
                    sorted[name] = child is null ? null : Canonical(child);
                }

                return sorted;
            }

            case JsonArray array:
            {
                JsonArray copy = new();
                foreach (JsonNode? child in array)
                {
                    copy.Add(child is null ? null : Canonical(child));
                }

                return copy;
            }

            default:
                return JsonNode.Parse(node.ToJsonString())!;
        }
    }

    /// <summary>
    /// The real data/ files load — not just hand-written fixtures.
    ///
    /// This is what catches a typo in shipped content, which unit fixtures never
    /// would. Requires the repo layout on disk; see RepositoryRoot.
    /// </summary>
    [Fact]
    public void ShippedBlockAndWallDataLoads()
    {
        // Through the real boot path, not a second chain over the same files:
        // a parallel loader here could stay green while the one that actually
        // boots the game broke (VOID-025).
        Registry<BlockDefinition> blocks = ContentPaths.Blocks();
        Registry<WallDefinition> walls = ContentPaths.Walls();

        Assert.NotEmpty(blocks);
        Assert.NotEmpty(walls);

        Assert.Equal("void:air", blocks.GetByNumericId(ContentIds.AirBlock).Id);
        Assert.Equal(BlockCollision.None, blocks.GetByNumericId(ContentIds.AirBlock).Collision);
        Assert.Equal("void:no_wall", walls.GetByNumericId(ContentIds.NoWall).Id);

        // Uniqueness is enforced at load, but assert it explicitly so a
        // regression in the builder cannot pass unnoticed.
        Assert.Equal(blocks.Count, blocks.Select(b => b.NumericId).Distinct().Count());
        Assert.Equal(walls.Count, walls.Select(w => w.NumericId).Distinct().Count());
    }

    /// <summary>
    /// Walks up from the test assembly's output directory until it finds
    /// <c>Void.sln</c>. Tests run from <c>bin/Debug/netX/</c>, so there is no
    /// fixed relative path to the repo that survives a TFM or config change.
    /// </summary>
    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Void.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail(
            $"Could not locate the repository root: no 'Void.sln' found walking up from " +
            $"'{AppContext.BaseDirectory}'.");
        throw new InvalidOperationException("unreachable");
    }
}
