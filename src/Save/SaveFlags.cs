using System;

namespace Void;

/// <summary>
/// Envelope header flag bits (save-format-spec §4). Written as a single byte.
/// Ship saves carry <c>Obfuscated | Compressed</c>; debug saves carry
/// <c>Debug</c> alone and store the payload verbatim (§14).
/// </summary>
[Flags]
public enum SaveFlags : byte
{
    /// <summary>No flags — an unprocessed body with none of the guarantees below.</summary>
    None = 0,

    /// <summary>Bit 0: body has been XORed with the derived keystream (§7).</summary>
    Obfuscated = 1,

    /// <summary>Bit 1: body has been zstd-compressed (§6).</summary>
    Compressed = 2,

    /// <summary>Bit 2: developer mode — body is the raw payload, no zstd, no XOR (§14).</summary>
    Debug = 4,
}
