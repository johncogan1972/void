namespace Void;

/// <summary>
/// Tile footprint of a prefab (VOID-024), per world-data-model-spec §5's
/// nested <c>dimensions</c> block.
///
/// Its own type because the JSON nests it, and because
/// <see cref="PrefabDefinition.Width"/> is the stride for the prefab's tile
/// arrays: every index into <c>block_ids</c> / <c>wall_ids</c> is
/// <c>y * Width + x</c>, and nothing about the 64-wide chunk grid applies.
/// Both values must be strictly positive; <see cref="PrefabRegistryLoader"/>
/// treats zero or negative as a fatal load error, because a prefab with no
/// footprint would sail through every length check by matching an empty array.
/// </summary>
public sealed class PrefabDimensions
{
    /// <summary>Tile width, and the row stride of the tile arrays. Must be &gt; 0.</summary>
    public int Width { get; init; }

    /// <summary>Tile height. Must be &gt; 0.</summary>
    public int Height { get; init; }
}
