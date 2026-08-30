using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// A world's size, in tiles and in chunks (world-data-model-spec §4).
///
/// Both forms are stored rather than derived, because they are written once at
/// generation time and read on every load: the chunk counts are what streaming
/// bounds-checks against, and recomputing them would silently paper over a
/// world whose tile size is not a whole number of chunks.
/// </summary>
/// <param name="WidthTiles">World width in tiles.</param>
/// <param name="HeightTiles">World height in tiles, sky row 0 down to the bottom of the void.</param>
/// <param name="ChunksX">Chunk columns; normally <c>width_tiles / Chunk.Width</c>.</param>
/// <param name="ChunksY">Chunk rows; normally <c>height_tiles / Chunk.Height</c>.</param>
public sealed record WorldDimensions(
    [property: JsonPropertyOrder(0), JsonRequired] int WidthTiles,
    [property: JsonPropertyOrder(1), JsonRequired] int HeightTiles,
    [property: JsonPropertyOrder(2), JsonRequired] int ChunksX,
    [property: JsonPropertyOrder(3), JsonRequired] int ChunksY);
