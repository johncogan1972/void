using System;

namespace Void;

/// <summary>
/// Thrown when content cannot be loaded (VOID-006). Every message names the
/// offending logical file so a bad JSON drop is diagnosable without a debugger.
/// Content problems are always fatal: silently skipping a broken definition
/// would make world generation depend on which files happened to parse.
/// </summary>
public sealed class ContentLoadException : Exception
{
    /// <summary>Logical name of the file that caused the failure, if known.</summary>
    public string? FileName { get; }

    /// <summary>Creates an exception with a pre-composed message.</summary>
    public ContentLoadException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception naming the offending file.</summary>
    public ContentLoadException(string fileName, string message, Exception? inner = null)
        : base($"[{fileName}] {message}", inner)
    {
        FileName = fileName;
    }
}
