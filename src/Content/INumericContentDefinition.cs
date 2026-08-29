namespace Void;

/// <summary>
/// Contract for content definitions that also carry a stable <b>numeric</b> key
/// (VOID-018).
///
/// Tile records store <c>block_id</c> / <c>wall_id</c> as <c>uint16</c>
/// (world-data-model-spec §2), so those registries need a compact numeric key in
/// addition to the human-readable string <see cref="IContentDefinition.Id"/>.
/// The string id is what data authors reference; the numeric id is what the tile
/// array and save files store.
///
/// Two rules make this safe:
/// <list type="bullet">
/// <item>The numeric id is <b>declared explicitly in JSON</b>. It is never
/// derived from load order, file name, or position in an array — those would all
/// make existing saves reinterpret their tiles when data files are reordered.</item>
/// <item>Once shipped, a numeric id is <b>stable forever</b> (spec §8). Removing
/// an entry retires its number; it is never silently reused, because every saved
/// world already refers to it.</item>
/// </list>
/// </summary>
public interface INumericContentDefinition : IContentDefinition
{
    /// <summary>
    /// Stable numeric registry key stored in tile records. Unique within the
    /// registry; duplicates are a fatal load error naming both files.
    /// </summary>
    ushort NumericId { get; }
}
