using System.Collections.Generic;

namespace Void;

/// <summary>
/// A single JSON document handed to the loader: its logical name (used in error
/// messages) and its raw text.
/// </summary>
/// <param name="Name">
/// Logical file name, e.g. <c>blocks/stone.json</c>. Only ever used for
/// diagnostics — never for ordering, which is by id.
/// </param>
/// <param name="Json">Raw, unparsed document text.</param>
public readonly record struct ContentDocument(string Name, string Json);

/// <summary>
/// Abstraction over "somewhere JSON documents come from" (VOID-006).
///
/// This indirection exists so the registry core stays free of engine types: at
/// runtime documents come out of the Godot PAK via
/// <c>GodotContentSource</c>, while tests and tools use
/// <see cref="DirectoryContentSource"/> with plain <c>System.IO</c>.
/// </summary>
public interface IContentSource
{
    /// <summary>
    /// Yields every document in the source. Enumeration order is explicitly
    /// unspecified; the loader sorts by id, so callers must not rely on it.
    /// </summary>
    IEnumerable<ContentDocument> ReadAll();

    /// <summary>Human-readable description of the source, for error messages.</summary>
    string Description { get; }
}
