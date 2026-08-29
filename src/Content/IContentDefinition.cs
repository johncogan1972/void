namespace Void;

/// <summary>
/// Contract every JSON-backed content definition satisfies (VOID-006).
///
/// The loader keys entries generically off <see cref="Id"/>, so a new content
/// type needs only a POCO implementing this interface — and a new *entry* of an
/// existing type needs only a JSON file, no code change at all.
///
/// Ids are compared and sorted with <see cref="System.StringComparer.Ordinal"/>
/// throughout, so registry iteration order is culture-independent and stable
/// across machines. That matters because registry order feeds world generation.
/// </summary>
public interface IContentDefinition
{
    /// <summary>
    /// Stable, unique identifier, e.g. <c>void:stone</c>. Must be non-empty and
    /// non-whitespace; the loader rejects definitions that omit it.
    /// </summary>
    string Id { get; }
}
