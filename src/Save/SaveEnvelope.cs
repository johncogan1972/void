using System;
using System.Buffers.Binary;

namespace Void;

/// <summary>
/// The 96-byte envelope header that prefixes every save file
/// (save-format-spec §4). Little-endian throughout.
///
/// <code>
///  off  size  field
///    0     4  magic            "MSAV", ASCII, in that byte order on disk
///    4     2  format_version   payload schema version
///    6     2  envelope_version envelope schema version
///    8     1  file_kind        <see cref="SaveFileKind"/>
///    9     1  flags            <see cref="SaveFlags"/>
///   10     2  reserved
///   12     4  payload_size     uncompressed payload length
///   16     4  compressed_size  body length on disk
///   20     8  seed_input       key-derivation input (§7)
///   28     4  file_salt        key-derivation input, random per write (§7)
///   32     8  reserved
///   40    32  integrity_hash   SHA-256 of the raw payload (§8)
///   72    12  reserved
///   84    12  reserved
/// </code>
///
/// All reserved bytes are written as zero and ignored on read, so a future
/// field can claim a reserved slot without an envelope-version bump (§9).
/// </summary>
public sealed class SaveEnvelope
{
    /// <summary>Header size in bytes. Fixed by the format; never compute it.</summary>
    public const int HeaderSize = 96;

    /// <summary>Length of <see cref="IntegrityHash"/> (SHA-256).</summary>
    public const int HashSize = 32;

    /// <summary>
    /// The magic value as a little-endian uint32, chosen so the on-disk bytes
    /// read 'M', 'S', 'A', 'V' in order.
    /// </summary>
    public const uint Magic = 0x5641534DU;

    /// <summary>The envelope schema version this build writes.</summary>
    public const ushort CurrentEnvelopeVersion = 1;

    // Field offsets, quoted straight from the spec table above.
    internal const int OffsetMagic = 0;
    internal const int OffsetFormatVersion = 4;
    internal const int OffsetEnvelopeVersion = 6;
    internal const int OffsetFileKind = 8;
    internal const int OffsetFlags = 9;
    internal const int OffsetPayloadSize = 12;
    internal const int OffsetCompressedSize = 16;
    internal const int OffsetSeedInput = 20;
    internal const int OffsetFileSalt = 28;
    internal const int OffsetIntegrityHash = 40;

    private readonly byte[] _integrityHash;

    /// <summary>Payload schema version (§9).</summary>
    public ushort FormatVersion { get; }

    /// <summary>Envelope schema version (§9).</summary>
    public ushort EnvelopeVersion { get; }

    /// <summary>Which payload this file carries.</summary>
    public SaveFileKind FileKind { get; }

    /// <summary>How the body was encoded.</summary>
    public SaveFlags Flags { get; }

    /// <summary>Uncompressed payload length in bytes.</summary>
    public uint PayloadSize { get; }

    /// <summary>Body length on disk in bytes.</summary>
    public uint CompressedSize { get; }

    /// <summary>Key-derivation input: the world's or campaign's seed (§7).</summary>
    public ulong SeedInput { get; }

    /// <summary>Key-derivation input, randomised on every write (§7).</summary>
    public uint FileSalt { get; }

    /// <summary>SHA-256 of the raw payload, pre-compression and pre-XOR (§8).</summary>
    public ReadOnlyMemory<byte> IntegrityHash => _integrityHash;

    /// <summary>True when the body is a verbatim payload copy (§14).</summary>
    public bool IsDebug => (Flags & SaveFlags.Debug) != 0;

    /// <summary>Builds a header. The hash must be exactly 32 bytes.</summary>
    /// <exception cref="ArgumentException">If the hash is the wrong length.</exception>
    public SaveEnvelope(
        ushort formatVersion,
        ushort envelopeVersion,
        SaveFileKind fileKind,
        SaveFlags flags,
        uint payloadSize,
        uint compressedSize,
        ulong seedInput,
        uint fileSalt,
        ReadOnlySpan<byte> integrityHash)
    {
        if (integrityHash.Length != HashSize)
        {
            throw new ArgumentException(
                $"Integrity hash must be exactly {HashSize} bytes (was {integrityHash.Length}).",
                nameof(integrityHash));
        }

        FormatVersion = formatVersion;
        EnvelopeVersion = envelopeVersion;
        FileKind = fileKind;
        Flags = flags;
        PayloadSize = payloadSize;
        CompressedSize = compressedSize;
        SeedInput = seedInput;
        FileSalt = fileSalt;
        _integrityHash = integrityHash.ToArray();
    }

    /// <summary>
    /// Writes the header into <paramref name="destination"/>, which must be at
    /// least <see cref="HeaderSize"/> bytes. Every byte in the first 96 is
    /// written, reserved slots included, so a reused buffer cannot leak.
    /// </summary>
    /// <exception cref="ArgumentException">If the destination is too small.</exception>
    public void Write(Span<byte> destination)
    {
        if (destination.Length < HeaderSize)
        {
            throw new ArgumentException(
                $"Destination must be at least {HeaderSize} bytes (was {destination.Length}).",
                nameof(destination));
        }

        Span<byte> header = destination[..HeaderSize];
        header.Clear();

        BinaryPrimitives.WriteUInt32LittleEndian(header[OffsetMagic..], Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header[OffsetFormatVersion..], FormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(header[OffsetEnvelopeVersion..], EnvelopeVersion);
        header[OffsetFileKind] = (byte)FileKind;
        header[OffsetFlags] = (byte)Flags;
        BinaryPrimitives.WriteUInt32LittleEndian(header[OffsetPayloadSize..], PayloadSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header[OffsetCompressedSize..], CompressedSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header[OffsetSeedInput..], SeedInput);
        BinaryPrimitives.WriteUInt32LittleEndian(header[OffsetFileSalt..], FileSalt);
        _integrityHash.CopyTo(header.Slice(OffsetIntegrityHash, HashSize));
    }

    /// <summary>
    /// Parses a header from the first <see cref="HeaderSize"/> bytes of
    /// <paramref name="source"/>. Reserved bytes are ignored.
    /// </summary>
    /// <param name="source">Bytes to parse.</param>
    /// <param name="fileName">Name used in exception messages.</param>
    /// <exception cref="SaveFormatException">
    /// If the source is truncated, the magic is wrong, or the envelope version
    /// is one this build has no parser for.
    /// </exception>
    public static SaveEnvelope Read(ReadOnlySpan<byte> source, string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        if (source.Length < HeaderSize)
        {
            throw new SaveFormatException(
                fileName,
                $"Truncated envelope: expected at least {HeaderSize} bytes, got {source.Length}.");
        }

        ReadOnlySpan<byte> header = source[..HeaderSize];

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header[OffsetMagic..]);
        if (magic != Magic)
        {
            throw new SaveFormatException(
                fileName,
                $"Bad magic 0x{magic:X8}; expected 0x{Magic:X8} (\"MSAV\"). Not a save file.");
        }

        ushort envelopeVersion = BinaryPrimitives.ReadUInt16LittleEndian(header[OffsetEnvelopeVersion..]);
        if (envelopeVersion == 0 || envelopeVersion > CurrentEnvelopeVersion)
        {
            throw new SaveFormatException(
                fileName,
                $"Unsupported envelope version {envelopeVersion}; this build reads 1..{CurrentEnvelopeVersion}.");
        }

        return new SaveEnvelope(
            BinaryPrimitives.ReadUInt16LittleEndian(header[OffsetFormatVersion..]),
            envelopeVersion,
            (SaveFileKind)header[OffsetFileKind],
            (SaveFlags)header[OffsetFlags],
            BinaryPrimitives.ReadUInt32LittleEndian(header[OffsetPayloadSize..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[OffsetCompressedSize..]),
            BinaryPrimitives.ReadUInt64LittleEndian(header[OffsetSeedInput..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[OffsetFileSalt..]),
            header.Slice(OffsetIntegrityHash, HashSize));
    }
}
