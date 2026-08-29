namespace Void;

/// <summary>
/// The handful of numeric content ids that code is allowed to know about
/// (VOID-018).
///
/// Everything else resolves through the registries by string id. These two are
/// fixed by the tile format itself (world-data-model-spec §2): a freshly
/// allocated, zero-filled tile array is air with no wall, so <c>0</c> carries
/// meaning at the format level and not merely in data.
/// </summary>
public static class ContentIds
{
    /// <summary>Numeric <c>block_id</c> of air — empty foreground space.</summary>
    public const ushort AirBlock = 0;

    /// <summary>Numeric <c>wall_id</c> meaning "no background wall".</summary>
    public const ushort NoWall = 0;
}
