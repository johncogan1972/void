using System;
using System.Collections.Generic;
using Godot;

namespace Void;

/// <summary>
/// <see cref="IContentSource"/> backed by Godot's virtual filesystem (VOID-006).
///
/// The runtime counterpart to <see cref="DirectoryContentSource"/>: after export,
/// <c>res://data/...</c> lives inside the PAK and is reachable only through
/// <see cref="DirAccess"/> / <see cref="Godot.FileAccess"/>. This type is kept
/// deliberately thin and is the <b>only</b> file in the content layer that
/// touches Godot, so the xunit suite (which runs with no engine initialised)
/// never loads it.
/// </summary>
public sealed class GodotContentSource : IContentSource
{
    private readonly string _root;
    private readonly string _extension;

    /// <inheritdoc/>
    public string Description => $"Godot path '{_root}'";

    /// <summary>Creates a source over a Godot path such as <c>res://data/blocks</c>.</summary>
    /// <param name="root">Godot-style directory path.</param>
    /// <param name="extension">File extension to accept, without the dot.</param>
    public GodotContentSource(string root, string extension = "json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        _root = root.TrimEnd('/');
        _extension = extension;
    }

    /// <inheritdoc/>
    public IEnumerable<ContentDocument> ReadAll()
    {
        List<string> files = new();
        Collect(_root, string.Empty, files);

        // Stable order for diagnostics only; the registry sorts by id.
        files.Sort(StringComparer.Ordinal);

        foreach (string relative in files)
        {
            string absolute = $"{_root}/{relative}";
            string text = Godot.FileAccess.GetFileAsString(absolute);

            Error error = Godot.FileAccess.GetOpenError();
            if (error != Error.Ok)
            {
                throw new ContentLoadException(relative, $"Could not be read: {error}.");
            }

            yield return new ContentDocument(relative, text);
        }
    }

    private void Collect(string directory, string prefix, List<string> files)
    {
        using DirAccess? dir = DirAccess.Open(directory);
        if (dir is null)
        {
            throw new ContentLoadException(
                $"Content directory not found: '{directory}' ({DirAccess.GetOpenError()}).");
        }

        foreach (string file in dir.GetFiles())
        {
            // Exported resources are served under a ".remap" alias; strip it so
            // the logical name still ends in the real extension.
            string logical = file.EndsWith(".remap", StringComparison.Ordinal)
                ? file[..^".remap".Length]
                : file;

            if (logical.EndsWith($".{_extension}", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(prefix + logical);
            }
        }

        foreach (string sub in dir.GetDirectories())
        {
            Collect($"{directory}/{sub}", $"{prefix}{sub}/", files);
        }
    }
}
