namespace Void;

/// <summary>
/// One named world size a world type may be generated at
/// (world-generation-spec §5).
///
/// <para>Sizes live in JSON rather than in a code table on purpose: MVP ships
/// Medium (6400x1800) only, but adding Small (4200x1200) or Large (8400x2400)
/// must be a content change and nothing else. Nothing in generation branches on
/// the preset <see cref="Id"/> — it is carried into
/// <see cref="WorldManifest.SizePreset"/> purely so a saved world records which
/// named size produced it.</para>
///
/// <para>Chunk counts are not stored here; they are derived from the tile
/// extents at generation time by <see cref="WorldGenerator"/>, rounding up so
/// the last partial chunk row/column still exists.</para>
/// </summary>
public sealed class WorldSizePreset
{
    /// <summary>Preset name as written to the manifest, e.g. <c>medium</c>.</summary>
    public string Id { get; init; } = string.Empty;

    // Tile extents of the whole world. Both must be positive, and the height
    // must be large enough that every layer proportion yields at least one row;
    // WorldTypeRegistryLoader enforces both and refuses the load otherwise.
    public int WidthTiles { get; init; }

    public int HeightTiles { get; init; }
}
