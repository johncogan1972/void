using System;

namespace Void;

/// <summary>
/// The outcome of reading one save file: its header, its decoded payload, and
/// whether the integrity hash matched (save-format-spec §8).
///
/// A mismatch is deliberately *not* fatal at this layer — the payload is always
/// present, and the caller decides whether to warn, refuse, or continue.
/// </summary>
/// <param name="Envelope">The parsed 96-byte header.</param>
/// <param name="Payload">The decoded raw payload bytes.</param>
/// <param name="IntegrityOk">
/// True when SHA-256 of <paramref name="Payload"/> equals the stored hash.
/// </param>
public sealed record SaveLoadResult(SaveEnvelope Envelope, byte[] Payload, bool IntegrityOk)
{
    /// <summary>Payload length in bytes.</summary>
    public int PayloadLength => Payload.Length;

    /// <summary>Convenience view over the payload.</summary>
    public ReadOnlySpan<byte> PayloadSpan => Payload;
}
