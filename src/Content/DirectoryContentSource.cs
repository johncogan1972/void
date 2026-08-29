using System;
using System.Collections.Generic;
using System.IO;

namespace Void;

/// <summary>
/// <see cref="IContentSource"/> over a real filesystem directory (VOID-006).
///
/// Uses only <c>System.IO</c>, so it works in unit tests and command-line tools
/// where no Godot engine is initialised. The exported game uses
/// <c>GodotContentSource</c> instead, because <c>res://</c> lives inside a PAK.
/// </summary>
public sealed class DirectoryContentSource : IContentSource
{
    private readonly string _root;
    private readonly string _pattern;

    /// <inheritdoc/>
    public string Description => $"directory '{_root}'";

    /// <summary>Creates a source over <paramref name="root"/> and its subdirectories.</summary>
    /// <param name="root">Directory to scan. Must exist.</param>
    /// <param name="pattern">Glob for candidate files. Defaults to <c>*.json</c>.</param>
    public DirectoryContentSource(string root, string pattern = "*.json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        _root = root;
        _pattern = pattern;
    }

    /// <inheritdoc/>
    public IEnumerable<ContentDocument> ReadAll()
    {
        if (!Directory.Exists(_root))
        {
            throw new ContentLoadException($"Content directory not found: '{_root}'.");
        }

        // Sorted purely so failures are reported in a stable order; correctness
        // of registry ordering does not depend on it.
        string[] files = Directory.GetFiles(_root, _pattern, SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        foreach (string file in files)
        {
            string name = Path.GetRelativePath(_root, file).Replace('\\', '/');

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException ex)
            {
                throw new ContentLoadException(name, $"Could not be read: {ex.Message}", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new ContentLoadException(name, $"Could not be read: {ex.Message}", ex);
            }

            yield return new ContentDocument(name, text);
        }
    }
}
