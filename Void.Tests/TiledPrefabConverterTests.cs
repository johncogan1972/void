using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Void;

namespace Void.Tests;

/// <summary>
/// VOID-026 acceptance tests: the Tiled <c>.tmx</c> to prefab JSON converter,
/// and the guard that keeps the committed generated files in step with the maps
/// they came from.
///
/// <para>The staleness test is the load-bearing one. Nothing else notices that
/// an author edited a map and forgot to regenerate — the generated JSON would
/// keep loading happily while describing a structure that no longer exists.
/// Engine-free, like the rest of <c>Void.Tests</c>.</para>
/// </summary>
public class TiledPrefabConverterTests
{
    /// <summary>Id of the sample map's prefab. Matches <c>prefab_id</c> in ruin_stone_small.tmx.</summary>
    private const string SampleId = "void:ruin_stone_small_tiled";

    /// <summary>The authored maps, absolute paths, ordinal-sorted so failures name the same map everywhere.</summary>
    private static IReadOnlyList<string> AuthoredMaps()
    {
        string folder = Path.Combine(ContentPaths.RepoRoot(), "content", "tiled");
        List<string> maps = new(Directory.GetFiles(folder, "*.tmx"));
        maps.Sort(StringComparer.Ordinal);
        return maps;
    }

    /// <summary>The shipped GID mapping, as the tool uses it.</summary>
    private static TilesetMap ShippedTilesets() =>
        TilesetMap.FromFile(Path.Combine(ContentPaths.RepoRoot(), "content", "tiled", "tileset_map.json"));

    /// <summary>
    /// Every authored map must reconvert to exactly the bytes committed under
    /// <c>data/prefabs/generated/</c>.
    ///
    /// <para>Red here means someone edited a <c>.tmx</c> (or the converter) and
    /// did not run <c>Void.Tools tmx-convert</c>. Fix it by regenerating, never
    /// by editing the generated file: the game loads the JSON, so a stale file
    /// means the world is built from a structure the author no longer has.</para>
    /// </summary>
    [Fact]
    public void GeneratedPrefabsMatchTheirSourceMaps()
    {
        TilesetMap tilesets = ShippedTilesets();
        Assert.NotEmpty(AuthoredMaps());

        foreach (string map in AuthoredMaps())
        {
            string expectedPath = Path.Combine(
                ContentPaths.RepoRoot(), "data", "prefabs", "generated",
                TiledPrefabConverter.OutputFileName(map));

            Assert.True(File.Exists(expectedPath), $"{Path.GetFileName(map)} has no generated output; run Void.Tools tmx-convert.");

            // The committed file is compared with CRLF folded away: a Windows
            // checkout can rewrite line endings on the way to disk, which is not
            // the staleness this test exists to catch.
            string committed = File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Equal(TiledPrefabConverter.ConvertFile(map, tilesets), committed);
        }
    }

    /// <summary>
    /// Converting the same map twice must produce the same bytes. Guards the
    /// determinism rule directly: if anything in the converter leaked dictionary
    /// or hash order into the output, this is where it shows up, rather than as a
    /// prefab that differs between two players' worlds.
    /// </summary>
    [Fact]
    public void ConversionIsIdempotent()
    {
        TilesetMap tilesets = ShippedTilesets();

        foreach (string map in AuthoredMaps())
        {
            Assert.Equal(
                TiledPrefabConverter.ConvertFile(map, tilesets),
                TiledPrefabConverter.ConvertFile(map, tilesets));
        }
    }

    /// <summary>
    /// The generated prefab must survive the real boot path — no separate load
    /// route for generated content, and no chance of the converter emitting a
    /// shape the loader rejects only in the shipped game.
    /// </summary>
    [Fact]
    public void GeneratedPrefabLoadsThroughTheBootPath()
    {
        PrefabDefinition prefab = ContentPaths.Prefabs().Get(SampleId);

        Assert.Equal("ruin", prefab.Category);
        Assert.Equal(7, prefab.Width);
        Assert.Equal(5, prefab.Height);
        Assert.Equal(0.75f, prefab.Weight);
        Assert.Equal(new[] { LayerCategory.Surface, LayerCategory.Underground }, prefab.Constraints.AllowedLayers);
        Assert.True(prefab.Constraints.RequiresGround);
        Assert.Equal(48, prefab.Constraints.MinSpacing.SameCategory);
        Assert.Equal(2, prefab.Constraints.ClearanceAbove);
    }

    /// <summary>
    /// Spot-checks the drawn structure through <see cref="PrefabDefinition.TileIndex"/>.
    /// A converter that transposed the map, or used the wrong row stride, would
    /// still produce a document of the right length that loads without complaint;
    /// only reading specific coordinates catches it.
    /// </summary>
    [Fact]
    public void GeneratedTileDataMatchesTheDrawnMap()
    {
        PrefabDefinition prefab = ContentPaths.Prefabs().Get(SampleId);

        // Stone shell, wood pillars on row 2, doorway at the bottom of column 3.
        Assert.Equal(2, prefab.BlockIds[prefab.TileIndex(0, 0)]);
        Assert.Equal(0, prefab.BlockIds[prefab.TileIndex(3, 2)]);
        Assert.Equal(5, prefab.BlockIds[prefab.TileIndex(0, 2)]);
        Assert.Equal(5, prefab.BlockIds[prefab.TileIndex(6, 2)]);
        Assert.Equal(0, prefab.BlockIds[prefab.TileIndex(3, 4)]);
        Assert.Equal(2, prefab.BlockIds[prefab.TileIndex(2, 4)]);

        // Walls are stone everywhere except the doorway, which is open to the sky.
        Assert.Equal(2, prefab.WallIds[prefab.TileIndex(3, 0)]);
        Assert.Equal(0, prefab.WallIds[prefab.TileIndex(3, 4)]);
    }

    /// <summary>
    /// Markers must arrive sorted by y, then x, with their metadata types intact.
    /// The sample map deliberately saves its objects in a different order; if the
    /// converter passed Tiled's order through, two authors saving the same map
    /// would produce different bytes.
    /// </summary>
    [Fact]
    public void GeneratedMarkersAreSortedAndTyped()
    {
        PrefabDefinition prefab = ContentPaths.Prefabs().Get(SampleId);

        Assert.Equal(
            new[] { PrefabMarkerType.Spawner, PrefabMarkerType.Chest, PrefabMarkerType.Entrance },
            prefab.Markers.Select(m => m.Type));

        PrefabMarker chest = prefab.Markers[1];
        Assert.Equal(1, chest.X);
        Assert.Equal(3, chest.Y);
        Assert.Equal("void:cave_beetle_loot", chest.Metadata["loot_table_id"].GetString());
        Assert.Equal(2, chest.Metadata["tier"].GetInt32());
        Assert.Equal(JsonValueKind.False, chest.Metadata["locked"].ValueKind);
        Assert.Equal(30.5f, prefab.Markers[0].Metadata["interval_seconds"].GetSingle());
    }

    // --- loud failures -------------------------------------------------------
    //
    // Inline fixtures, not committed maps: each of these is a broken map, and a
    // broken map on disk would be one more thing an author could mistake for
    // real content. Every case below is one a silent default would turn into a
    // structure that differs from what was drawn, with nothing downstream able
    // to tell.

    /// <summary>Mapping used by the inline fixtures: one tileset, one mapped tile.</summary>
    private static TilesetMap FixtureTilesets() =>
        TilesetMap.FromJson(
            """{ "tilesets": { "test_tiles": { "block_ids": { "0": 2 }, "wall_ids": { "0": 2 } } } }""",
            "fixture-tilesets.json");

    /// <summary>
    /// A 2x1 map with the given parts substituted, so each failure test differs
    /// from a converting document in exactly one place.
    /// </summary>
    private static string MapXml(
        string blocksLayer = """<layer name="blocks" width="2" height="1"><data encoding="csv">1,1</data></layer>""",
        string wallsLayer = """<layer name="walls" width="2" height="1"><data encoding="csv">1,1</data></layer>""",
        string markers = "") =>
        $"""
         <?xml version="1.0" encoding="UTF-8"?>
         <map version="1.10" orientation="orthogonal" width="2" height="1" tilewidth="16" tileheight="16" infinite="0">
          <properties>
           <property name="prefab_id" value="test:hut"/>
           <property name="category" value="ruin"/>
          </properties>
          <tileset firstgid="1" name="test_tiles"/>
          {blocksLayer}
          {wallsLayer}
          {markers}
         </map>
         """;

    /// <summary>Converts an inline fixture; external tilesets are not used, so resolving one is a test bug.</summary>
    private static string ConvertFixture(string xml) =>
        TiledPrefabConverter.Convert(
            xml, "fixture.tmx", FixtureTilesets(),
            static source => throw new InvalidOperationException($"unexpected external tileset '{source}'"));

    /// <summary>The embedded-tileset path must convert, since not every author uses an external .tsx.</summary>
    [Fact]
    public void EmbeddedTilesetConverts()
    {
        Assert.Contains("\"block_ids\"", ConvertFixture(MapXml()), StringComparison.Ordinal);
    }

    /// <summary>
    /// A tile with no entry in the mapping must fail, naming the tileset, local
    /// id and coordinate. Dropping it instead would put a hole in the structure
    /// that reads as a design mistake, not a data one.
    /// </summary>
    [Fact]
    public void UnmappedGidFails()
    {
        TiledConversionException ex = Assert.Throws<TiledConversionException>(
            () => ConvertFixture(MapXml(
                blocksLayer: """<layer name="blocks" width="2" height="1"><data encoding="csv">1,7</data></layer>""")));

        Assert.Contains("test_tiles", ex.Message, StringComparison.Ordinal);
        Assert.Contains("local id 6", ex.Message, StringComparison.Ordinal);
        Assert.Contains("(1,0)", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A flipped tile must fail rather than being un-flipped by masking. The
    /// prefab format cannot express the flip, so masking would ship a mirrored
    /// structure that matches neither the map nor the author's intent.
    /// </summary>
    [Fact]
    public void FlippedTileFails()
    {
        // 0x80000001: horizontal flip bit over tileset-local id 0.
        TiledConversionException ex = Assert.Throws<TiledConversionException>(
            () => ConvertFixture(MapXml(
                blocksLayer: """<layer name="blocks" width="2" height="1"><data encoding="csv">1,2147483649</data></layer>""")));

        Assert.Contains("flipped or rotated", ex.Message, StringComparison.Ordinal);
        Assert.Contains("(1,0)", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both tile layers are required. A map missing <c>blocks</c> would otherwise
    /// convert to a prefab made entirely of air.
    /// </summary>
    [Fact]
    public void MissingBlocksLayerFails()
    {
        TiledConversionException ex = Assert.Throws<TiledConversionException>(
            () => ConvertFixture(MapXml(blocksLayer: "")));

        Assert.Contains("No tile layer named 'blocks'", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Base64 layer data must fail with an instruction, not a parse error: it is
    /// Tiled's default, so this is the first wall a new author hits.
    /// </summary>
    [Fact]
    public void NonCsvLayerEncodingFails()
    {
        TiledConversionException ex = Assert.Throws<TiledConversionException>(
            () => ConvertFixture(MapXml(
                blocksLayer: """<layer name="blocks" width="2" height="1"><data encoding="base64">AQAAAAEAAAA=</data></layer>""")));

        Assert.Contains("Tile Layer Format", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An object class that is not a <see cref="PrefabMarkerType"/> must fail at
    /// conversion, listing the valid types. The loader would also reject it, but
    /// only after the bad file was committed as if it were content.
    /// </summary>
    [Fact]
    public void UnknownMarkerTypeFails()
    {
        TiledConversionException ex = Assert.Throws<TiledConversionException>(
            () => ConvertFixture(MapXml(
                markers: """<objectgroup name="markers"><object id="1" class="teleporter" x="0" y="0"/></objectgroup>""")));

        Assert.Contains("'teleporter' is not a marker type", ex.Message, StringComparison.Ordinal);
        Assert.Contains("boss_spawn", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A marker outside the footprint must fail here rather than at load, so the
    /// author sees the pixel position they actually dragged it to.
    /// </summary>
    [Fact]
    public void OutOfBoundsMarkerFails()
    {
        TiledConversionException ex = Assert.Throws<TiledConversionException>(
            () => ConvertFixture(MapXml(
                markers: """<objectgroup name="markers"><object id="1" class="chest" x="160" y="0"/></objectgroup>""")));

        Assert.Contains("outside the map's", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A misspelled map property must fail rather than being ignored: ignoring it
    /// leaves the constraint at its default, so a prefab an author restricted to
    /// caverns quietly becomes placeable anywhere.
    /// </summary>
    [Fact]
    public void UnrecognisedMapPropertyFails()
    {
        string xml = MapXml().Replace(
            """<property name="category" value="ruin"/>""",
            """<property name="category" value="ruin"/><property name="constraint_requires_grnd" type="bool" value="true"/>""",
            StringComparison.Ordinal);

        TiledConversionException ex = Assert.Throws<TiledConversionException>(() => ConvertFixture(xml));

        Assert.Contains("constraint_requires_grnd", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A layer name that is not a <see cref="LayerCategory"/> must fail. It would
    /// otherwise be valid JSON that simply never matches, i.e. a prefab that
    /// silently never places.
    /// </summary>
    [Fact]
    public void UnknownAllowedLayerFails()
    {
        string xml = MapXml().Replace(
            """<property name="category" value="ruin"/>""",
            """<property name="category" value="ruin"/><property name="constraint_allowed_layers" value="surface,sky"/>""",
            StringComparison.Ordinal);

        TiledConversionException ex = Assert.Throws<TiledConversionException>(() => ConvertFixture(xml));

        Assert.Contains("'sky'", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A layer that is not the map's size must fail: the tile arrays would be the
    /// wrong length, and the prefab loader's length check would blame the
    /// generated file rather than the map that produced it.
    /// </summary>
    [Fact]
    public void LayerSizeMismatchFails()
    {
        TiledConversionException ex = Assert.Throws<TiledConversionException>(
            () => ConvertFixture(MapXml(
                blocksLayer: """<layer name="blocks" width="3" height="1"><data encoding="csv">1,1,1</data></layer>""")));

        Assert.Contains("but the map is 2x1", ex.Message, StringComparison.Ordinal);
    }
}
