using System;
using System.IO;
using System.Security.Cryptography;
using ZstdSharp;

namespace Void;

/// <summary>
/// Save-file envelope facade (save-format-spec §4, §6-§8, §10.1, §14).
///
/// Ship path: serialise → SHA-256 → zstd(level 3) → XOR keystream →
/// header + body → atomic write. Debug path (§14) stores the payload verbatim
/// with the hash still computed, so a save can be read in a hex editor.
///
/// Encoding and decoding are pure functions of their inputs with exactly one
/// exception: <c>file_salt</c>, which is drawn from the OS CSPRNG on every
/// write. See <see cref="NewFileSalt"/>.
/// </summary>
public static class SaveFile
{
    /// <summary>zstd compression level (§6).</summary>
    public const int CompressionLevel = 3;

    /// <summary>
    /// Draws a fresh <c>file_salt</c> from the OS entropy pool.
    ///
    /// THIS IS THE ONE DELIBERATE NON-DETERMINISM IN THE CODEBASE. It exists so
    /// that two writes of the same chunk produce different bytes on disk,
    /// defeating "diff two saves to find what changed" (§7). It affects only the
    /// obfuscation keystream: the payload, its hash, and everything decoded back
    /// out are unchanged by it.
    ///
    /// It must NEVER be consumed by world generation, or by anything else that
    /// must reproduce from a seed. World-gen randomness comes from
    /// <see cref="Rng"/> alone (CLAUDE.md, determinism rules).
    /// </summary>
    public static uint NewFileSalt()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    /// <summary>
    /// Builds a complete save file (header + body) in memory.
    /// </summary>
    /// <param name="kind">Payload kind, stored in the header.</param>
    /// <param name="formatVersion">Payload schema version; also a keystream input.</param>
    /// <param name="seedInput">World or campaign seed; a keystream input (§7).</param>
    /// <param name="payload">Raw serialised payload bytes.</param>
    /// <param name="debug">When true, writes a plaintext debug file (§14).</param>
    /// <param name="fileSalt">
    /// Salt override. Leave null for the normal random draw; supply a value only
    /// from tests that need byte-reproducible output.
    /// </param>
    public static byte[] Encode(
        SaveFileKind kind,
        ushort formatVersion,
        ulong seedInput,
        ReadOnlySpan<byte> payload,
        bool debug = false,
        uint? fileSalt = null)
    {
        byte[] hash = SHA256.HashData(payload);
        uint salt = fileSalt ?? NewFileSalt();

        byte[] body;
        SaveFlags flags;

        if (debug)
        {
            // §14: raw payload, no zstd, no XOR. Hash is still stored.
            body = payload.ToArray();
            flags = SaveFlags.Debug;
        }
        else
        {
            using Compressor compressor = new Compressor(CompressionLevel);
            body = compressor.Wrap(payload).ToArray();
            Keystream.ApplyXor(seedInput, salt, formatVersion, body);
            flags = SaveFlags.Obfuscated | SaveFlags.Compressed;
        }

        SaveEnvelope envelope = new SaveEnvelope(
            formatVersion,
            SaveEnvelope.CurrentEnvelopeVersion,
            kind,
            flags,
            (uint)payload.Length,
            (uint)body.Length,
            seedInput,
            salt,
            hash);

        byte[] file = new byte[SaveEnvelope.HeaderSize + body.Length];
        envelope.Write(file);
        body.CopyTo(file.AsSpan(SaveEnvelope.HeaderSize));
        return file;
    }

    /// <summary>
    /// Parses a complete save file from memory and decodes its payload.
    /// </summary>
    /// <param name="file">Header plus body.</param>
    /// <param name="fileName">Name used in exception messages.</param>
    /// <param name="allowIntegrityMismatch">
    /// When false (the safe default) a hash mismatch throws
    /// <see cref="SaveIntegrityException"/>, which still carries the payload.
    /// When true the payload is returned with
    /// <see cref="SaveLoadResult.IntegrityOk"/> set to false and the caller
    /// decides what to tell the player (§8).
    /// </param>
    /// <exception cref="SaveFormatException">If the file is structurally invalid.</exception>
    /// <exception cref="SaveIntegrityException">On mismatch, unless opted out.</exception>
    public static SaveLoadResult Decode(
        ReadOnlySpan<byte> file,
        string fileName,
        bool allowIntegrityMismatch = false)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        SaveEnvelope envelope = SaveEnvelope.Read(file, fileName);
        ReadOnlySpan<byte> body = file[SaveEnvelope.HeaderSize..];

        if (body.Length != envelope.CompressedSize)
        {
            throw new SaveFormatException(
                fileName,
                $"Body length {body.Length} does not match header compressed_size {envelope.CompressedSize}.");
        }

        byte[] payload;
        if (envelope.IsDebug)
        {
            payload = body.ToArray();
        }
        else
        {
            byte[] compressed = body.ToArray();
            if ((envelope.Flags & SaveFlags.Obfuscated) != 0)
            {
                Keystream.ApplyXor(
                    envelope.SeedInput, envelope.FileSalt, envelope.FormatVersion, compressed);
            }

            if ((envelope.Flags & SaveFlags.Compressed) != 0)
            {
                try
                {
                    using Decompressor decompressor = new Decompressor();
                    payload = decompressor.Unwrap(compressed).ToArray();
                }
                catch (Exception e) when (e is not SaveFormatException)
                {
                    throw new SaveFormatException(
                        fileName, "Body could not be decompressed; the file is corrupt.", e);
                }
            }
            else
            {
                payload = compressed;
            }
        }

        if (payload.Length != envelope.PayloadSize)
        {
            throw new SaveFormatException(
                fileName,
                $"Decoded payload length {payload.Length} does not match header payload_size {envelope.PayloadSize}.");
        }

        bool integrityOk = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(payload), envelope.IntegrityHash.Span);

        if (!integrityOk && !allowIntegrityMismatch)
        {
            throw new SaveIntegrityException(fileName, payload);
        }

        return new SaveLoadResult(envelope, payload, integrityOk);
    }

    /// <summary>
    /// Encodes and writes a save file to <paramref name="path"/> atomically
    /// (§10.1). Returns the header that was written.
    /// </summary>
    public static SaveEnvelope Save(
        string path,
        SaveFileKind kind,
        ushort formatVersion,
        ulong seedInput,
        ReadOnlySpan<byte> payload,
        bool debug = false,
        uint? fileSalt = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] file = Encode(kind, formatVersion, seedInput, payload, debug, fileSalt);
        AtomicWriter.Write(path, file);
        return SaveEnvelope.Read(file, path);
    }

    /// <summary>Reads and decodes a save file from disk.</summary>
    /// <exception cref="SaveFormatException">If the file is structurally invalid.</exception>
    /// <exception cref="SaveIntegrityException">On mismatch, unless opted out.</exception>
    public static SaveLoadResult Load(string path, bool allowIntegrityMismatch = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Decode(File.ReadAllBytes(path), path, allowIntegrityMismatch);
    }
}
