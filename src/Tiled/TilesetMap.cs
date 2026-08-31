using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Void;

/// <summary>
/// The authored bridge between what a tile looks like in Tiled and what it is
/// in the game: tileset name plus local tile id to a numeric <c>block_id</c> or
/// <c>wall_id</c> (VOID-026).
///
/// Loaded from <c>content/tiled/tileset_map.json</c>. This mapping is data, never
/// code — adding a tile to a tileset must not require a converter change — and it
/// is keyed by <b>tileset name and local id</b> rather than by global id, because
/// a GID depends on the order tilesets happen to be added to a map and would
/// silently repoint every tile the moment an author inserts a tileset.
///
/// <para>Blocks and walls are mapped separately: the same drawn tile can mean
/// <c>void:stone</c> on the <c>blocks</c> layer and <c>void:stone_wall</c> on the
/// <c>walls</c> layer, and the two registries number independently.</para>
///
/// <para>Local id 0 of a tileset is a real tile. The <i>empty</i> tile is GID 0,
/// which never reaches this type — <see cref="TiledPrefabConverter"/> maps it to
/// id 0 (air / no wall) before any lookup.</para>
/// </summary>
public sealed class TilesetMap
{
    /// <summary>
    /// Per-tileset lookups, keyed ordinally by the tileset's Tiled name. Two
    /// dictionaries per tileset rather than one entry type, because a tileset may
    /// legitimately map a tile for one layer kind and not the other.
    /// </summary>
    private readonly Dictionary<string, Dictionary<int, ushort>> _blocks;
    private readonly Dictionary<string, Dictionary<int, ushort>> _walls;

    private TilesetMap(
        Dictionary<string, Dictionary<int, ushort>> blocks,
        Dictionary<string, Dictionary<int, ushort>> walls)
    {
        _blocks = blocks;
        _walls = walls;
    }

    /// <summary>Reads a mapping document from disk.</summary>
    /// <exception cref="TiledConversionException">
    /// If the file is missing, unreadable or malformed. Fatal rather than an
    /// empty mapping: an empty mapping would make every tile in every map an
    /// unmapped-GID failure, burying the real cause.
    /// </exception>
    public static TilesetMap FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new TiledConversionException(path, $"Tileset map could not be read: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new TiledConversionException(path, $"Tileset map could not be read: {ex.Message}", ex);
        }

        return FromJson(json, Path.GetFileName(path));
    }

    /// <summary>
    /// Parses a mapping document. <paramref name="sourceName"/> only ever appears
    /// in error messages, so tests can pass a fixture name.
    /// </summary>
    /// <remarks>
    /// Shape, with every id a decimal string key so the file stays a plain JSON
    /// object:
    /// <code>
    /// { "tilesets": { "ruin_tiles": { "block_ids": { "0": 2 }, "wall_ids": { "0": 2 } } } }
    /// </code>
    /// </remarks>
    /// <exception cref="TiledConversionException">On any malformed part of the document.</exception>
    public static TilesetMap FromJson(string json, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        Dictionary<string, Dictionary<int, ushort>> blocks = new(StringComparer.Ordinal);
        Dictionary<string, Dictionary<int, ushort>> walls = new(StringComparer.Ordinal);

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (!document.RootElement.TryGetProperty("tilesets", out JsonElement tilesets)
                || tilesets.ValueKind != JsonValueKind.Object)
            {
                throw new TiledConversionException(
                    sourceName, "Root must be an object with a 'tilesets' object.");
            }

            foreach (JsonProperty tileset in tilesets.EnumerateObject())
            {
                blocks[tileset.Name] = ReadIds(tileset, "block_ids", sourceName);
                walls[tileset.Name] = ReadIds(tileset, "wall_ids", sourceName);
            }
        }
        catch (JsonException ex)
        {
            throw new TiledConversionException(sourceName, $"Malformed JSON: {ex.Message}", ex);
        }

        return new TilesetMap(blocks, walls);
    }

    /// <summary>
    /// Numeric block id for <paramref name="localId"/> in <paramref name="tileset"/>.
    /// False means "unmapped", which the converter turns into a fatal error naming
    /// the tile — it is never a reason to drop the tile.
    /// </summary>
    public bool TryGetBlockId(string tileset, int localId, out ushort id) =>
        TryGet(_blocks, tileset, localId, out id);

    /// <inheritdoc cref="TryGetBlockId"/>
    public bool TryGetWallId(string tileset, int localId, out ushort id) =>
        TryGet(_walls, tileset, localId, out id);

    /// <summary>True if the mapping knows the tileset at all, so the converter can say which of the two mistakes was made.</summary>
    public bool HasTileset(string tileset) => _blocks.ContainsKey(tileset);

    private static bool TryGet(
        Dictionary<string, Dictionary<int, ushort>> source, string tileset, int localId, out ushort id)
    {
        if (source.TryGetValue(tileset, out Dictionary<int, ushort>? ids) && ids.TryGetValue(localId, out id))
        {
            return true;
        }

        id = 0;
        return false;
    }

    /// <summary>
    /// Reads one <c>block_ids</c> / <c>wall_ids</c> object. Absent is legal and
    /// yields an empty map: a tileset used only for walls has no block mapping.
    /// </summary>
    private static Dictionary<int, ushort> ReadIds(
        JsonProperty tileset, string field, string sourceName)
    {
        Dictionary<int, ushort> ids = new();

        if (tileset.Value.ValueKind != JsonValueKind.Object)
        {
            throw new TiledConversionException(
                sourceName, $"Tileset '{tileset.Name}' must be an object.");
        }

        if (!tileset.Value.TryGetProperty(field, out JsonElement map))
        {
            return ids;
        }

        if (map.ValueKind != JsonValueKind.Object)
        {
            throw new TiledConversionException(
                sourceName, $"Tileset '{tileset.Name}' field '{field}' must be an object.");
        }

        foreach (JsonProperty entry in map.EnumerateObject())
        {
            // Keys are decimal local tile ids; parsed invariantly so the file
            // means the same thing under every machine locale.
            if (!int.TryParse(entry.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int localId)
                || localId < 0)
            {
                throw new TiledConversionException(
                    sourceName,
                    $"Tileset '{tileset.Name}' field '{field}' has key '{entry.Name}'; " +
                    "keys must be non-negative decimal local tile ids.");
            }

            if (entry.Value.ValueKind != JsonValueKind.Number
                || !entry.Value.TryGetUInt16(out ushort value))
            {
                throw new TiledConversionException(
                    sourceName,
                    $"Tileset '{tileset.Name}' field '{field}' key '{entry.Name}' must be a " +
                    "numeric id in [0,65535].");
            }

            ids[localId] = value;
        }

        return ids;
    }
}
