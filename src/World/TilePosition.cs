using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// A bare tile coordinate in one world's tile space (world-data-model-spec §4).
///
/// Absolute tile coordinates, not chunk-local: divide by <see cref="Chunk.Width"/>
/// and <see cref="Chunk.Height"/> to find the containing chunk. Y grows downward,
/// matching the layer boundaries in <see cref="LayerBoundaries"/>.
///
/// Used wherever the manifest stores a plain point — player spawn today, more
/// later. A record so two positions compare by value.
/// </summary>
/// <param name="X">Tile column, 0 to <c>width_tiles - 1</c>.</param>
/// <param name="Y">Tile row, 0 at the top of the sky, growing downward.</param>
public sealed record TilePosition(
    [property: JsonPropertyOrder(0), JsonRequired] int X,
    [property: JsonPropertyOrder(1), JsonRequired] int Y);
