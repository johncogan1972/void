using System;

namespace Void;

/// <summary>
/// Thrown when the SHA-256 of a decoded payload does not match the hash in the
/// envelope header (save-format-spec §8). The file parsed fine; its contents
/// have been modified.
///
/// Per GDD §10.3 this is never a hard block at the *game* level — the player is
/// warned and may continue. That decision belongs to the caller, so the library
/// default is the safe one (throw) and the caller opts in via
/// <c>allowIntegrityMismatch</c>, which returns the payload with
/// <see cref="SaveLoadResult.IntegrityOk"/> set to <c>false</c> instead.
/// </summary>
public sealed class SaveIntegrityException : Exception
{
    /// <summary>Path or logical name of the offending file, if known.</summary>
    public string? FileName { get; }

    /// <summary>
    /// The decoded payload. Reachable even on failure so a caller that catches
    /// this can still offer "load anyway" without a second read.
    /// </summary>
    public byte[] Payload { get; }

    /// <summary>Creates an exception naming the offending file and carrying its payload.</summary>
    public SaveIntegrityException(string fileName, byte[] payload)
        : base($"[{fileName}] Integrity hash mismatch: this save file appears to have been modified.")
    {
        FileName = fileName;
        Payload = payload;
    }
}
