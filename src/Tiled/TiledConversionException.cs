using System;

namespace Void;

/// <summary>
/// Fatal error while converting an authored Tiled <c>.tmx</c> into a runtime
/// prefab document (VOID-026).
///
/// Separate from <see cref="ContentLoadException"/> because it is thrown by
/// build-time tooling and by the staleness test, never on the game's boot path:
/// by the time the game reads <c>data/prefabs/generated/</c> the conversion has
/// already happened and the loader's own validation takes over.
///
/// <para>Every conversion problem is fatal and names the offending thing — the
/// file, and where relevant the tileset, local tile id and tile coordinate.
/// Nothing is skipped or silently defaulted: a dropped tile or a quietly
/// unflipped structure produces a prefab that does not match what the author
/// drew, and no later stage can tell that it happened.</para>
/// </summary>
public sealed class TiledConversionException : Exception
{
    /// <summary>Message already carrying the source file name, as callers build it.</summary>
    public TiledConversionException(string message)
        : base(message)
    {
    }

    /// <summary>Prefixes <paramref name="message"/> with the offending file.</summary>
    public TiledConversionException(string sourceName, string message)
        : base($"{sourceName}: {message}")
    {
    }

    /// <summary>Wraps a lower-level failure (XML, IO) without losing its detail.</summary>
    public TiledConversionException(string sourceName, string message, Exception inner)
        : base($"{sourceName}: {message}", inner)
    {
    }
}
