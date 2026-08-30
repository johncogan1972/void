using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// Envelope, keystream and round-trip coverage for VOID-007
/// (save-format-spec §4, §6-§8, §14).
/// </summary>
public sealed class SaveEnvelopeTests
{
    /// <summary>
    /// Builds a repeatable pseudo-payload of a given length.
    /// </summary>
    private static byte[] Payload(int length, byte seed = 0)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)((i * 31 + seed) & 0xFF);
        }

        return bytes;
    }

    /// <summary>
    /// The core guarantee: whatever goes in comes out unchanged, at every size
    /// including empty and larger-than-one-buffer.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(97)]
    [InlineData(400_000)]
    public void RoundTripsPayloadByteIdentical(int length)
    {
        byte[] payload = Payload(length, 7);

        byte[] file = SaveFile.Encode(SaveFileKind.Chunk, 1, 0xDEADBEEFCAFEF00DUL, payload);
        SaveLoadResult result = SaveFile.Decode(file, "roundtrip");

        Assert.True(result.IntegrityOk);
        Assert.Equal(payload, result.Payload);
        Assert.Equal(SaveFileKind.Chunk, result.Envelope.FileKind);
        Assert.Equal(SaveFlags.Obfuscated | SaveFlags.Compressed, result.Envelope.Flags);
        Assert.Equal((uint)length, result.Envelope.PayloadSize);
    }

    /// <summary>
    /// Compression is really applied, not just flagged. Chunk data is the bulk of a
    /// save, so a silently disabled codec would cost players disk, not correctness.
    /// </summary>
    [Fact]
    public void CompressibleDataActuallyShrinks()
    {
        byte[] payload = new byte[400_000]; // all zeroes: highly compressible
        byte[] file = SaveFile.Encode(SaveFileKind.Chunk, 1, 1UL, payload);

        SaveEnvelope envelope = SaveEnvelope.Read(file, "compress");
        Assert.True(envelope.CompressedSize < envelope.PayloadSize);
        Assert.Equal(payload, SaveFile.Decode(file, "compress").Payload);
    }

    /// <summary>
    /// The header is a fixed 96 bytes (save-format-spec §4). Every offset below
    /// depends on this, as does any future migration reading an old file.
    /// </summary>
    [Fact]
    public void HeaderIsExactlyNinetySixBytes()
    {
        Assert.Equal(96, SaveEnvelope.HeaderSize);

        byte[] file = SaveFile.Encode(SaveFileKind.Entity, 1, 0UL, Array.Empty<byte>(), debug: true);
        Assert.Equal(SaveEnvelope.HeaderSize, file.Length); // empty debug body
    }

    /// <summary>
    /// Pins every field to its documented byte offset, little-endian.
    ///
    /// This is the on-disk contract with saves that already exist on players’
    /// machines. A field that moved would make old saves unreadable, so the layout
    /// is asserted against the spec table rather than against the writer.
    /// </summary>
    [Fact]
    public void EveryFieldLandsAtItsSpecOffset()
    {
        byte[] hash = new byte[32];
        for (int i = 0; i < 32; i++)
        {
            hash[i] = (byte)(0xA0 + i);
        }

        SaveEnvelope envelope = new SaveEnvelope(
            formatVersion: 0x1234,
            envelopeVersion: 0x5678,
            fileKind: SaveFileKind.WorldManifest,
            flags: SaveFlags.Obfuscated | SaveFlags.Compressed,
            payloadSize: 0x11223344,
            compressedSize: 0x55667788,
            seedInput: 0x0123456789ABCDEFUL,
            fileSalt: 0x99AABBCC,
            integrityHash: hash);

        byte[] buffer = new byte[SaveEnvelope.HeaderSize];
        envelope.Write(buffer);

        Assert.Equal(0x5641534DU, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0)));
        Assert.Equal((ushort)0x1234, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(4)));
        Assert.Equal((ushort)0x5678, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(6)));
        Assert.Equal((byte)3, buffer[8]);
        Assert.Equal((byte)3, buffer[9]);
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(10)));
        Assert.Equal(0x11223344U, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(12)));
        Assert.Equal(0x55667788U, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(16)));
        Assert.Equal(0x0123456789ABCDEFUL, BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(20)));
        Assert.Equal(0x99AABBCCU, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(28)));
        Assert.Equal(hash, buffer.AsSpan(40, 32).ToArray());

        // Every reserved slot is zero.
        foreach (int offset in new[] { 32, 33, 34, 35, 36, 37, 38, 39 })
        {
            Assert.Equal((byte)0, buffer[offset]);
        }

        for (int i = 72; i < 96; i++)
        {
            Assert.Equal((byte)0, buffer[i]);
        }
    }

    /// <summary>
    /// The first four bytes read MSAV in a hex editor, which is what makes a save
    /// identifiable when someone sends you a corrupt file.
    /// </summary>
    [Fact]
    public void MagicBytesOnDiskSpellMsav()
    {
        byte[] file = SaveFile.Encode(SaveFileKind.Character, 1, 5UL, Payload(16));

        Assert.Equal((byte)'M', file[0]);
        Assert.Equal((byte)'S', file[1]);
        Assert.Equal((byte)'A', file[2]);
        Assert.Equal((byte)'V', file[3]);
        Assert.Equal("MSAV", Encoding.ASCII.GetString(file, 0, 4));
    }

    /// <summary>
    /// Per-file salt makes two saves of identical content differ on disk, while both
    /// still decode to the same payload with an unchanged content hash.
    /// </summary>
    [Fact]
    public void SaltRandomisationChangesBytesButNotContent()
    {
        byte[] payload = Payload(2048, 3);

        byte[] a = SaveFile.Encode(SaveFileKind.Chunk, 1, 42UL, payload);
        byte[] b = SaveFile.Encode(SaveFileKind.Chunk, 1, 42UL, payload);

        Assert.NotEqual(a, b);

        SaveEnvelope ea = SaveEnvelope.Read(a, "a");
        SaveEnvelope eb = SaveEnvelope.Read(b, "b");
        Assert.NotEqual(ea.FileSalt, eb.FileSalt);
        Assert.Equal(ea.IntegrityHash.ToArray(), eb.IntegrityHash.ToArray());

        Assert.Equal(payload, SaveFile.Decode(a, "a").Payload);
        Assert.Equal(payload, SaveFile.Decode(b, "b").Payload);
    }

    /// <summary>
    /// Tampering is detected and reported — but the decoded payload is still handed
    /// back on the exception, so a recovery path can salvage a damaged save instead
    /// of the player losing it entirely.
    /// </summary>
    [Fact]
    public void TamperedBodyIsDetectedAndPayloadStillReachable()
    {
        byte[] payload = Payload(4096, 11);
        byte[] file = SaveFile.Encode(SaveFileKind.Chunk, 1, 9UL, payload, debug: true);

        file[SaveEnvelope.HeaderSize + 100] ^= 0xFF;

        SaveIntegrityException thrown = Assert.Throws<SaveIntegrityException>(
            () => SaveFile.Decode(file, "tampered"));
        Assert.Equal(payload.Length, thrown.Payload.Length);

        SaveLoadResult result = SaveFile.Decode(file, "tampered", allowIntegrityMismatch: true);
        Assert.False(result.IntegrityOk);
        Assert.Equal(payload.Length, result.Payload.Length);
        Assert.NotEqual(payload, result.Payload);
    }

    /// <summary>
    /// A file that is not a save is rejected by name, not parsed as garbage.
    /// </summary>
    [Fact]
    public void WrongMagicIsRejected()
    {
        byte[] file = SaveFile.Encode(SaveFileKind.Chunk, 1, 1UL, Payload(32));
        file[0] = (byte)'X';

        SaveFormatException thrown = Assert.Throws<SaveFormatException>(
            () => SaveFile.Decode(file, "badmagic"));
        Assert.Contains("magic", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A future or zero envelope version is refused rather than misread. This is the
    /// hook the migration path will take over (save-format-spec §14).
    /// </summary>
    [Fact]
    public void UnknownEnvelopeVersionIsRejected()
    {
        byte[] file = SaveFile.Encode(SaveFileKind.Chunk, 1, 1UL, Payload(32));
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(6), 99);

        SaveFormatException thrown = Assert.Throws<SaveFormatException>(
            () => SaveFile.Decode(file, "badversion"));
        Assert.Contains("envelope version", thrown.Message, StringComparison.OrdinalIgnoreCase);

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(6), 0);
        Assert.Throws<SaveFormatException>(() => SaveFile.Decode(file, "zeroversion"));
    }

    /// <summary>
    /// A file too short to hold a header fails cleanly instead of reading past the end.
    /// </summary>
    [Fact]
    public void TruncatedHeaderIsRejected()
    {
        Assert.Throws<SaveFormatException>(() => SaveEnvelope.Read(new byte[95], "short"));
    }

    /// <summary>
    /// Debug saves skip obfuscation and compression so the bytes can be inspected
    /// directly — the payload appears verbatim after the header.
    /// </summary>
    [Fact]
    public void DebugModeStoresPayloadInPlaintext()
    {
        byte[] payload = Encoding.UTF8.GetBytes("the guide is called Aelis and this must be greppable");
        byte[] file = SaveFile.Encode(SaveFileKind.CampaignManifest, 1, 77UL, payload, debug: true);

        SaveEnvelope envelope = SaveEnvelope.Read(file, "debug");
        Assert.True(envelope.IsDebug);
        Assert.Equal(SaveFlags.Debug, envelope.Flags);
        Assert.Equal(envelope.PayloadSize, envelope.CompressedSize);
        Assert.Equal(SHA256.HashData(payload), envelope.IntegrityHash.ToArray());

        // Plaintext proof: the payload appears verbatim in the file bytes.
        int index = file.AsSpan().IndexOf(payload);
        Assert.Equal(SaveEnvelope.HeaderSize, index);

        SaveLoadResult result = SaveFile.Decode(file, "debug");
        Assert.True(result.IntegrityOk);
        Assert.Equal(payload, result.Payload);
    }

    /// <summary>
    /// A body shorter than the header claims is a truncated file — caught rather
    /// than decoded into a plausible-looking partial world.
    /// </summary>
    [Fact]
    public void BodyLengthMismatchIsRejected()
    {
        byte[] file = SaveFile.Encode(SaveFileKind.Chunk, 1, 1UL, Payload(64));
        byte[] truncated = file.AsSpan(0, file.Length - 1).ToArray();

        Assert.Throws<SaveFormatException>(() => SaveFile.Decode(truncated, "truncbody"));
    }

    /// <summary>
    /// The keystream is a pure function of salt, seed and format version, and is a
    /// true stream: a short fill is a prefix of a longer one, so a save can be
    /// decoded in chunks.
    /// </summary>
    [Fact]
    public void KeystreamIsDeterministicForFixedInputs()
    {
        byte[] first = new byte[64];
        byte[] second = new byte[64];
        Keystream.Fill(0xABCDEF0123456789UL, 0x11223344, 3, first);
        Keystream.Fill(0xABCDEF0123456789UL, 0x11223344, 3, second);
        Assert.Equal(first, second);

        byte[] otherSalt = new byte[64];
        Keystream.Fill(0xABCDEF0123456789UL, 0x11223345, 3, otherSalt);
        Assert.NotEqual(first, otherSalt);

        byte[] otherSeed = new byte[64];
        Keystream.Fill(0xABCDEF012345678AUL, 0x11223344, 3, otherSeed);
        Assert.NotEqual(first, otherSeed);

        byte[] otherFormat = new byte[64];
        Keystream.Fill(0xABCDEF0123456789UL, 0x11223344, 4, otherFormat);
        Assert.NotEqual(first, otherFormat);

        // A shorter fill is a prefix of a longer one: the generator is a stream.
        byte[] shorter = new byte[13];
        Keystream.Fill(0xABCDEF0123456789UL, 0x11223344, 3, shorter);
        Assert.Equal(first.AsSpan(0, 13).ToArray(), shorter);
    }

    /// <summary>
    /// An all-zero keystream would XOR to plaintext and obfuscate nothing.
    /// </summary>
    [Fact]
    public void KeystreamIsNotAllZeroes()
    {
        byte[] bytes = new byte[256];
        Keystream.Fill(0UL, 0U, 0, bytes);
        Assert.Contains(bytes, b => b != 0);
    }

    /// <summary>
    /// The degenerate all-zero state falls back to the documented SplitMix64
    /// expansion rather than emitting zeros forever.
    /// </summary>
    [Fact]
    public void AllZeroStateFallsBackToNonZeroSequence()
    {
        Xoshiro256PlusPlus rng = new Xoshiro256PlusPlus(new byte[32]);
        ulong first = rng.Next();
        Assert.NotEqual(0UL, first);

        // The documented fallback is SplitMix64 expansion of seed 0.
        Assert.Equal(new Xoshiro256PlusPlus(0UL).Next(), first);
    }

    /// <summary>
    /// The raw-state constructor needs exactly 32 bytes; anything else is a caller bug.
    /// </summary>
    [Fact]
    public void StateSpanConstructorRejectsWrongLength()
    {
        Assert.Throws<ArgumentException>(() => new Xoshiro256PlusPlus(new byte[31]));
    }

    /// <summary>
    /// Guard against the span constructor perturbing the seeded path — that path
    /// produces the golden vectors and world generation itself.
    /// </summary>
    [Fact]
    public void SingleSeedConstructorStillMatchesVoid005Behaviour()
    {
        // Guard: the span ctor must not have perturbed the seeded path.
        Rng rng = new Rng(12345UL);
        Xoshiro256PlusPlus direct = new Xoshiro256PlusPlus(12345UL);
        Assert.Equal(direct.Next(), rng.NextULong());
    }
}
