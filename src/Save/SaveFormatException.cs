using System;

namespace Void;

/// <summary>
/// Thrown when a save file cannot be parsed at all: bad magic, an envelope
/// version this build has no parser for, a truncated header, or a body whose
/// length disagrees with the header. These are structural failures, so unlike
/// an integrity-hash mismatch (<see cref="SaveIntegrityException"/>) there is
/// no payload to hand back and no "continue anyway" option.
/// </summary>
public sealed class SaveFormatException : Exception
{
    /// <summary>Path or logical name of the offending file, if known.</summary>
    public string? FileName { get; }

    /// <summary>Creates an exception with a pre-composed message.</summary>
    public SaveFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception naming the offending file.</summary>
    public SaveFormatException(string fileName, string message, Exception? inner = null)
        : base($"[{fileName}] {message}", inner)
    {
        FileName = fileName;
    }
}
