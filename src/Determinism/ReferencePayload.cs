using System;
using System.IO;
using System.Security.Cryptography;

namespace Void.Determinism;

/// <summary>
/// The reference-seed harness (VOID-008): build a canonical byte payload from a
/// fixed seed, hash it, and compare against a committed golden value.
///
/// This is the mechanism that makes the determinism rule enforceable rather
/// than aspirational. A change to any generator that feeds
/// <see cref="Current"/> moves the hash and fails CI, so it can only land as a
/// deliberate, reviewed edit to the golden constant.
/// </summary>
public static class ReferencePayload
{
    /// <summary>
    /// The canonical world seed. Arbitrary but frozen — changing it invalidates
    /// the golden hash for no benefit.
    /// </summary>
    public const ulong ReferenceSeed = 0x5645524946594D45UL;

    /// <summary>
    /// Magic written at the head of every payload, so bytes from this harness
    /// can never be mistaken for anything else.
    /// </summary>
    private const string Magic = "VOID-REFERENCE-PAYLOAD";

    /// <summary>
    /// The payload source in force. Swap this (and the golden hash, in the same
    /// commit) when a later phase has something better to hash — Phase 2 is
    /// expected to point it at a generated world.
    /// </summary>
    public static IReferencePayloadSource Current { get; } = new RngDrawPayloadSource();

    /// <summary>
    /// Builds the payload bytes: a header identifying the harness and the
    /// source, then whatever the source writes.
    /// </summary>
    public static byte[] Build(IReferencePayloadSource source, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(source);

        using MemoryStream stream = new MemoryStream();
        PayloadWriter writer = new PayloadWriter(stream);

        writer.WriteString(Magic);
        writer.WriteString(source.Id);
        writer.WriteInt32(source.Version);
        writer.WriteUInt64(seed);

        source.Write(writer, seed);

        return stream.ToArray();
    }

    /// <summary>Builds the payload for <see cref="Current"/> at the reference seed.</summary>
    public static byte[] Build() => Build(Current, ReferenceSeed);

    /// <summary>
    /// SHA-256 of the payload, as uppercase hex. Uppercase because that is what
    /// the golden constant holds; comparisons are on this exact form.
    /// </summary>
    public static string Hash(IReferencePayloadSource source, ulong seed) =>
        Convert.ToHexString(SHA256.HashData(Build(source, seed)));

    /// <summary>Hashes the payload for <see cref="Current"/> at the reference seed.</summary>
    public static string Hash() => Hash(Current, ReferenceSeed);
}
