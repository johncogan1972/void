using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Void;

namespace Void.Tests;

/// <summary>
/// VOID-024 acceptance tests: the prefab schema, its registry entry, and the
/// validation that stops a malformed structure reaching the placement engine.
/// Engine-free, like the rest of <c>Void.Tests</c>.
/// </summary>
public class PrefabDefinitionTests : IDisposable
{
    /// <summary>
    /// Throwaway content directory for this test instance. Per-instance and
    /// randomly named so the tests stay independent under xunit's parallel
    /// runner; <c>Dispose</c> removes it.
    /// </summary>
    private readonly string _root;

    /// <summary>Creates a throwaway content directory per test.</summary>
    public PrefabDefinitionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "void-prefab-tests-" + Path.GetRandomFileName());
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

    /// <summary>Loads the temp root as prefabs, validated against the shipped blocks and walls.</summary>
    private Registry<PrefabDefinition> LoadTempPrefabs() =>
        PrefabRegistryLoader.Load(
            new DirectoryContentSource(_root), ContentPaths.Blocks(), ContentPaths.Walls());

    /// <summary>
    /// A valid 2x2 prefab with the given fields substituted, so each failure
    /// test differs from a passing document in exactly one place.
    /// </summary>
    private static string PrefabJson(
        string id = "test:hut",
        string dimensions = """{ "width": 2, "height": 2 }""",
        string blockIds = "[2, 2, 2, 2]",
        string wallIds = "[2, 2, 2, 2]",
        string markers = """[{ "type": "entrance", "x": 0, "y": 1 }]""",
        string weight = "1.0") =>
        $$"""
        {
          "prefab_id": "{{id}}",
          "category": "ruin",
          "dimensions": {{dimensions}},
          "block_ids": {{blockIds}},
          "wall_ids": {{wallIds}},
          "markers": {{markers}},
          "weight": {{weight}}
        }
        """;

    /// <summary>
    /// The shipped prefab tree must load against the shipped block and wall
    /// registries. If this goes red, the game boots into a prefab registry that
    /// either failed or holds tiles that resolve to nothing — every structure
    /// placed in a world would be wrong or absent.
    /// </summary>
    [Fact]
    public void ShippedPrefabsLoadAgainstShippedBlocksAndWalls()
    {
        Registry<PrefabDefinition> prefabs = ContentPaths.Prefabs();

        Assert.NotEmpty(prefabs);

        foreach (PrefabDefinition prefab in prefabs)
        {
            Assert.True(prefab.Width > 0 && prefab.Height > 0);
            Assert.Equal(prefab.TileCount, prefab.BlockIds.Count);
            Assert.Equal(prefab.TileCount, prefab.WallIds.Count);
        }
    }

    /// <summary>
    /// Marker metadata must survive the load intact — including after the
    /// loader has disposed the JsonDocument it parsed from. If this goes red,
    /// a chest's loot table id is unreadable (or throws) at placement time,
    /// which is exactly when the world is being built and cannot recover.
    /// </summary>
    [Fact]
    public void ShippedChestMarkerCarriesUsableMetadata()
    {
        PrefabDefinition ruin = ContentPaths.Prefabs()["void:ruin_stone_small"];
        PrefabMarker chest = ruin.Markers.Single(m => m.Type == PrefabMarkerType.Chest);

        Assert.Equal("void:cave_beetle_loot", chest.Metadata["loot_table_id"].GetString());
        Assert.Equal(2, chest.Metadata["tier"].GetInt32());
        Assert.False(chest.Metadata["locked"].GetBoolean());
    }

    /// <summary>
    /// A prefab must round-trip through <c>RegistryLoader.Options</c> without
    /// losing markers or altering metadata values — including non-string ones,
    /// which are the values a naive "everything is a string" map would quietly
    /// mangle. Tooling (the Tiled converter) reads and writes this format, so a
    /// lossy round trip silently rewrites shipped content.
    /// </summary>
    [Fact]
    public void RoundTripPreservesMarkersAndMetadataValues()
    {
        PrefabDefinition original = ContentPaths.Prefabs()["void:shrine_wood_small"];

        string json = JsonSerializer.Serialize(original, RegistryLoader.Options);
        PrefabDefinition? copy = JsonSerializer.Deserialize<PrefabDefinition>(json, RegistryLoader.Options);

        Assert.NotNull(copy);
        Assert.Equal(original.Id, copy!.Id);
        Assert.Equal(original.Category, copy.Category);
        Assert.Equal(original.Width, copy.Width);
        Assert.Equal(original.Height, copy.Height);
        Assert.Equal(original.Weight, copy.Weight);
        Assert.Equal(original.BlockIds, copy.BlockIds);
        Assert.Equal(original.WallIds, copy.WallIds);
        Assert.Equal(original.Constraints.AllowedLayers, copy.Constraints.AllowedLayers);
        Assert.Equal(original.Constraints.MinSpacing.SameCategory, copy.Constraints.MinSpacing.SameCategory);

        Assert.Equal(original.Markers.Count, copy.Markers.Count);
        for (int i = 0; i < original.Markers.Count; i++)
        {
            PrefabMarker before = original.Markers[i];
            PrefabMarker after = copy.Markers[i];

            Assert.Equal(before.Type, after.Type);
            Assert.Equal(before.X, after.X);
            Assert.Equal(before.Y, after.Y);
            Assert.Equal(before.Metadata.Count, after.Metadata.Count);

            foreach (KeyValuePair<string, JsonElement> entry in before.Metadata)
            {
                Assert.Equal(entry.Value.GetRawText(), after.Metadata[entry.Key].GetRawText());
            }
        }

        // The non-string value specifically: a float that came back as a string
        // (or as 30) would still compare equal on Count alone.
        PrefabMarker spawner = copy.Markers.Single(m => m.Type == PrefabMarkerType.Spawner);
        Assert.Equal(30.5, spawner.Metadata["interval_seconds"].GetDouble());
    }

    /// <summary>
    /// A <c>block_ids</c> array of the wrong length is fatal. This is the
    /// schema's most dangerous error: a short or long array shears every row
    /// against the wrong stride, producing a plausible-looking but scrambled
    /// structure with no error anywhere downstream.
    /// </summary>
    [Fact]
    public void WrongLengthBlockIdsIsFatal()
    {
        WriteFile("hut.json", PrefabJson(blockIds: "[2, 2, 2]"));

        ContentLoadException error = Assert.Throws<ContentLoadException>(LoadTempPrefabs);

        Assert.Contains("test:hut", error.Message, StringComparison.Ordinal);
        Assert.Contains("block_ids", error.Message, StringComparison.Ordinal);
        Assert.Contains("3", error.Message, StringComparison.Ordinal);
        Assert.Contains("4", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same check on <c>wall_ids</c>. Guarded separately because checking
    /// only the foreground array is exactly how half a rule survives a
    /// refactor — and a sheared wall layer is invisible until a player looks
    /// behind the structure.
    /// </summary>
    [Fact]
    public void WrongLengthWallIdsIsFatal()
    {
        WriteFile("hut.json", PrefabJson(wallIds: "[2, 2, 2, 2, 2]"));

        ContentLoadException error = Assert.Throws<ContentLoadException>(LoadTempPrefabs);

        Assert.Contains("wall_ids", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A zero or negative dimension is fatal in its own right. Without this,
    /// a 0-wide prefab would satisfy the length check with an empty array and
    /// then divide by zero when anything indexed it.
    /// </summary>
    [Fact]
    public void NonPositiveDimensionIsFatal()
    {
        WriteFile("hut.json", PrefabJson(
            dimensions: """{ "width": 0, "height": 2 }""", blockIds: "[]", wallIds: "[]", markers: "[]"));

        ContentLoadException error = Assert.Throws<ContentLoadException>(LoadTempPrefabs);

        Assert.Contains("dimensions", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unrecognised marker <c>type</c> is a load failure naming the file,
    /// not a silent fallback to the zero enum member. A typo'd "chest" that
    /// deserialised as <c>boss_spawn</c> would spawn a boss in a starter ruin.
    /// This relies on the shared converter's <c>allowIntegerValues: false</c>
    /// and on <c>RegistryLoader.Parse</c> rewrapping its JsonException.
    /// </summary>
    [Fact]
    public void UnknownMarkerTypeIsFatal()
    {
        WriteFile("hut.json", PrefabJson(markers: """[{ "type": "treasure", "x": 0, "y": 0 }]"""));

        ContentLoadException error = Assert.Throws<ContentLoadException>(LoadTempPrefabs);

        Assert.Equal("hut.json", error.FileName);
    }

    /// <summary>
    /// A marker outside the footprint is fatal on every edge. Each is listed
    /// because an off-by-one guard usually covers the low edge or the high
    /// edge, not both — and a chest one tile past the wall is placed into
    /// whatever the generator already put there.
    /// </summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    public void MarkerOutsideBoundsIsFatal(int x, int y)
    {
        WriteFile("hut.json", PrefabJson(
            markers: $$"""[{ "type": "chest", "x": {{x}}, "y": {{y}} }]"""));

        ContentLoadException error = Assert.Throws<ContentLoadException>(LoadTempPrefabs);

        Assert.Contains("marker[0]", error.Message, StringComparison.Ordinal);
        Assert.Contains($"({x},{y})", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>block_id</c> that no block registry entry claims is fatal, and the
    /// message locates it. This is the cross-registry half of the load, and the
    /// reason the prefab registry depends on VOID-018: an unresolvable id would
    /// otherwise become a hole in the structure at generation time.
    /// </summary>
    [Fact]
    public void UnresolvableBlockIdIsFatal()
    {
        WriteFile("hut.json", PrefabJson(blockIds: "[2, 2, 2, 60000]"));

        ContentLoadException error = Assert.Throws<ContentLoadException>(LoadTempPrefabs);

        Assert.Contains("block_id", error.Message, StringComparison.Ordinal);
        Assert.Contains("60000", error.Message, StringComparison.Ordinal);
        Assert.Contains("x=1, y=1", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Same for the background layer; see <see cref="UnresolvableBlockIdIsFatal"/>.</summary>
    [Fact]
    public void UnresolvableWallIdIsFatal()
    {
        WriteFile("hut.json", PrefabJson(wallIds: "[2, 999, 2, 2]"));

        ContentLoadException error = Assert.Throws<ContentLoadException>(LoadTempPrefabs);

        Assert.Contains("wall_id", error.Message, StringComparison.Ordinal);
        Assert.Contains("999", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A negative weight is fatal. Weighted selection sums these, so one
    /// negative variant silently distorts every other variant's odds instead of
    /// simply never being picked.
    /// </summary>
    [Fact]
    public void NegativeWeightIsFatal()
    {
        WriteFile("hut.json", PrefabJson(weight: "-1.0"));

        ContentLoadException error = Assert.Throws<ContentLoadException>(LoadTempPrefabs);

        Assert.Contains("weight", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A weight too large for <c>float</c> is fatal. This is a real path, not a
    /// theoretical one: <c>System.Text.Json</c> does not reject an out-of-range
    /// literal, it folds <c>1e39</c> to <c>float.PositiveInfinity</c>, so the
    /// value reaches the loader looking like an ordinary positive weight. It
    /// shares the failure mode the negative case guards, but worse — variant
    /// selection sums every weight, and a non-finite total makes the comparison
    /// against a seeded draw meaningless, so <i>no</i> prefab is ever chosen and
    /// the structures simply stop appearing in generated worlds. The
    /// <c>IsFinite</c> check is what catches it; <c>weight &lt; 0f</c> alone
    /// lets infinity and <c>NaN</c> straight through.
    /// </summary>
    [Fact]
    public void NonFiniteWeightIsFatal()
    {
        WriteFile("hut.json", PrefabJson(weight: "1e39"));

        ContentLoadException error = Assert.Throws<ContentLoadException>(LoadTempPrefabs);

        Assert.Contains("weight", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The generic loader refuses prefabs outright. It sees one source and
    /// cannot resolve a numeric tile id against the block registry, so letting
    /// it return would hand back a registry that parsed cleanly and describes
    /// structures made of tiles that do not exist.
    /// </summary>
    [Fact]
    public void GenericLoaderRefusesPrefabsBecauseItCannotValidateThem()
    {
        WriteFile("hut.json", PrefabJson());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => RegistryLoader.Load<PrefabDefinition>(new DirectoryContentSource(_root)));

        // The message has to name the way out, or the next caller just deletes the call.
        Assert.Contains("loader", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The builder-merging entry point is guarded too — closing only
    /// <c>Load</c> leaves the identical hole one method along.
    /// </summary>
    [Fact]
    public void GenericLoadIntoRefusesPrefabsToo()
    {
        RegistryBuilder<PrefabDefinition> builder = new();

        Assert.Throws<InvalidOperationException>(
            () => RegistryLoader.LoadInto(builder, new DirectoryContentSource(_root)));
    }

    /// <summary>
    /// Registry iteration is ordinal-sorted by id, never load order. Prefab
    /// selection feeds world generation, so an order that depended on which
    /// file the filesystem yielded first would make the same seed generate
    /// different worlds on different machines.
    /// </summary>
    [Fact]
    public void RegistryIterationIsOrdinalSortedNotLoadOrder()
    {
        WriteFile("z-first.json", PrefabJson(id: "test:zeta"));
        WriteFile("a-second.json", PrefabJson(id: "test:alpha"));
        WriteFile("m-third.json", PrefabJson(id: "test:Mid"));

        string[] ids = LoadTempPrefabs().Select(p => p.Id).ToArray();

        // Ordinal, so uppercase 'M' sorts before every lowercase id.
        Assert.Equal(new[] { "test:Mid", "test:alpha", "test:zeta" }, ids);
    }

    /// <summary>
    /// <c>TileIndex</c> strides by the prefab's own width. The chunk grid is 64
    /// wide and a prefab is not: a caller reaching for that constant reads a
    /// completely different tile with no error, which is the silent bug this
    /// helper exists to prevent.
    /// </summary>
    [Fact]
    public void TileIndexUsesThePrefabsOwnWidthNotTheChunkStride()
    {
        PrefabDefinition ruin = ContentPaths.Prefabs()["void:ruin_stone_small"];

        Assert.Equal(5, ruin.Width);
        Assert.Equal(17, ruin.TileIndex(2, 3));
        Assert.NotEqual((3 * 64) + 2, ruin.TileIndex(2, 3));

        // And the doorway really is at (2,3): air in a wall of stone. If the
        // stride were wrong this would read a solid tile.
        Assert.Equal(ContentIds.AirBlock, ruin.BlockIds[ruin.TileIndex(2, 3)]);
        Assert.Equal((ushort)2, ruin.BlockIds[ruin.TileIndex(0, 3)]);
    }

    /// <summary>
    /// Out-of-range coordinates throw rather than returning a plausible index.
    /// Wrapping silently is how a marker written one tile past the edge ends up
    /// stamped into the opposite side of the structure.
    /// </summary>
    [Fact]
    public void TileIndexRejectsOutOfRangeCoordinates()
    {
        PrefabDefinition ruin = ContentPaths.Prefabs()["void:ruin_stone_small"];

        Assert.Throws<ArgumentOutOfRangeException>(() => ruin.TileIndex(5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ruin.TileIndex(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => ruin.TileIndex(-1, 0));
    }

    /// <summary>
    /// Omitted optional blocks default to permissive, never null and never
    /// restrictive. A prefab with no <c>constraints</c> must be placeable
    /// anywhere; the alternative — an empty allow-list read as "no biomes" —
    /// makes it silently never appear.
    /// </summary>
    [Fact]
    public void OmittedConstraintsDefaultToPermissive()
    {
        WriteFile("hut.json", PrefabJson());

        PrefabDefinition hut = LoadTempPrefabs()["test:hut"];

        Assert.Empty(hut.Constraints.AllowedBiomes);
        Assert.Empty(hut.Constraints.AllowedLayers);
        Assert.False(hut.Constraints.RequiresGround);
        Assert.Equal(0, hut.Constraints.MinSpacing.AnyCategory);
        Assert.Equal(0, hut.Constraints.ClearanceAbove);
        Assert.Empty(hut.Markers[0].Metadata);
    }
}
