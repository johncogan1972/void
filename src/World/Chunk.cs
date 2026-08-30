using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;

namespace Void;

/// <summary>
/// One 64x64 chunk of world: the streaming unit, the save unit, and the format
/// of a <c>&lt;x&gt;_&lt;y&gt;.chunk</c> file (VOID-020,
/// world-data-model-spec §3, §9.2, §9.3).
///
/// A class rather than a struct on purpose: it owns a 4,096-element
/// <see cref="Tile"/> array (32 KB), so copy-by-value would be a disaster and
/// streaming needs a stable reference to hand around.
///
/// <b>Serialised payload layout</b> — little-endian throughout, and the payload
/// handed to <see cref="SaveFile"/>, which owns compression, obfuscation,
/// integrity hashing and atomic writing. Nothing here duplicates that:
///
/// <code>
/// header    32 bytes  chunk_x i32, chunk_y i32, format_version u16, flags u16,
///                     biome_primary u16, layer_primary u8, reserved[15] = 0
/// metadata  variable  ore_density u8[4], structure_ref_count u16,
///                     structure_refs u16[count], walkable_ratio u8,
///                     special_flags u32
/// tiles     32 KB     4096 x 8 bytes, row-major (index = y * 64 + x)
/// entities  2 bytes   entity_count u16, always 0 in this build
/// </code>
///
/// The entity section is reserved and deliberately empty: the persistent-entity
/// schema is a later ticket, and inventing one here would bake a guess into the
/// save format. The count is written so the section exists to grow into, and a
/// non-zero count on read is a hard failure rather than a skipped payload of
/// unknown length.
/// </summary>
public sealed class Chunk
{
    /// <summary>Chunk dimensions in tiles, and their product. Fixed by the format.</summary>
    public const int Width = 64;
    public const int Height = 64;
    public const int TileCount = Width * Height;

    /// <summary>Size of the serialised chunk header, in bytes. Fixed by the format.</summary>
    public const int HeaderSize = 32;

    /// <summary>
    /// Schema version written into the chunk header.
    /// </summary>
    /// <remarks>
    /// Non-zero from day one, per §9.2: version 0 is reserved as "nobody set
    /// this". <see cref="ReadFrom"/> rejects any other value outright instead of
    /// parsing optimistically. Bumping this is a save migration, not an edit.
    /// </remarks>
    public const ushort CurrentFormatVersion = 1;

    /// <summary>Fixed width of the metadata <c>ore_density</c> hint, in bytes (one per ore tier).</summary>
    public const int OreDensityLength = 4;

    /// <summary>Bytes of header plus fixed metadata plus tiles plus the entity count.</summary>
    private const int FixedPayloadSize =
        HeaderSize
        + OreDensityLength + sizeof(ushort) + sizeof(byte) + sizeof(uint)
        + (TileCount * Tile.SizeInBytes)
        + sizeof(ushort);

    private readonly Tile[] _tiles = new Tile[TileCount];
    private readonly byte[] _oreDensity = new byte[OreDensityLength];

    /// <summary>
    /// Creates an all-air chunk at the given chunk coordinate.
    /// </summary>
    /// <remarks>
    /// No fill pass: a zeroed <see cref="Tile"/> is already air with no wall, no
    /// liquid and no damage (see <see cref="Tile.Air"/>), so the freshly
    /// allocated array is valid as-is.
    /// </remarks>
    public Chunk(int chunkX, int chunkY)
    {
        ChunkX = chunkX;
        ChunkY = chunkY;
    }

    /// <summary>World chunk coordinates. Tile coordinates are these times 64.</summary>
    public int ChunkX { get; }
    public int ChunkY { get; }

    /// <summary>
    /// Schema version this chunk claims. Defaults to
    /// <see cref="CurrentFormatVersion"/> and is written verbatim; settable only
    /// so that migration code and format tests can construct other versions.
    /// </summary>
    public ushort FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>
    /// Chunk-level flags. <see cref="ChunkFlags.CurrentlyLoaded"/> is kept here
    /// but masked off when writing — see that flag's remarks.
    /// </summary>
    public ChunkFlags Flags { get; set; }

    /// <summary>Dominant biome for this chunk, as a biome registry id.</summary>
    public ushort BiomePrimary { get; set; }

    /// <summary>Vertical band this chunk belongs to.</summary>
    public WorldLayer LayerPrimary { get; set; }

    /// <summary>
    /// Per-tier ore density hint, exactly <see cref="OreDensityLength"/> bytes.
    /// Written to disk as-is, so its length is part of the format and the span
    /// is fixed-length by construction.
    /// </summary>
    public Span<byte> OreDensity => _oreDensity;

    /// <summary>
    /// Ids of structures whose bounds intersect this chunk.
    /// </summary>
    /// <remarks>
    /// <b>Order is part of the file</b> and is preserved verbatim on round-trip;
    /// keep it deterministic (sorted at the point of generation) so two runs of
    /// the same seed produce byte-identical chunks. Count is serialised as a
    /// <c>ushort</c>, so more than 65,535 refs cannot be written.
    /// </remarks>
    public List<ushort> StructureRefs { get; } = new List<ushort>();

    /// <summary>Proportion of walkable space, 0-255. Feeds spawn budgeting.</summary>
    public byte WalkableRatio { get; set; }

    /// <summary>Content hints used to find chunks without loading their tiles.</summary>
    public ChunkSpecialFlags SpecialFlags { get; set; }

    /// <summary>
    /// The tile array in serialisation order. Exposed for bulk generation
    /// passes; index it with <see cref="Index"/>, never with your own arithmetic.
    /// </summary>
    public Span<Tile> Tiles => _tiles;

    /// <summary>Size in bytes this chunk will occupy as a serialised payload.</summary>
    public int SerializedSize => FixedPayloadSize + (StructureRefs.Count * sizeof(ushort));

    /// <summary>
    /// Row-major tile index: <c>y * 64 + x</c>.
    /// </summary>
    /// <remarks>
    /// Hardcoded and load-bearing. Generation writes rows contiguously and chunk
    /// streaming memcpy's whole rows, and the same order is the on-disk tile
    /// order, so switching to column-major would be a save migration as well as
    /// a performance regression.
    /// </remarks>
    public static int Index(int x, int y) => (y * Width) + x;

    /// <summary>
    /// Tile at a chunk-local coordinate.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If x or y is outside 0-63. Thrown rather than clamped: a clamp turns an
    /// off-by-one in a generation pass into a smeared edge column that looks
    /// plausible and is nearly impossible to trace back.
    /// </exception>
    public Tile this[int x, int y]
    {
        get
        {
            ThrowIfOutOfRange(x, y);
            return _tiles[Index(x, y)];
        }

        set
        {
            ThrowIfOutOfRange(x, y);
            _tiles[Index(x, y)] = value;
        }
    }

    /// <summary>
    /// File name for a chunk at the given chunk coordinate, per §9.3. Invariant
    /// culture: a locale that formats negative numbers differently must not
    /// rename half the world's files.
    /// </summary>
    public static string ChunkFileName(int chunkX, int chunkY) =>
        string.Create(CultureInfo.InvariantCulture, $"{chunkX}_{chunkY}.chunk");

    /// <summary>
    /// Serialises the chunk into <paramref name="destination"/>, which must be
    /// at least <see cref="SerializedSize"/> bytes. Returns the bytes written.
    /// </summary>
    /// <exception cref="ArgumentException">If the span is too short.</exception>
    /// <exception cref="InvalidOperationException">
    /// If there are more structure refs than the <c>ushort</c> count can carry.
    /// </exception>
    public int WriteTo(Span<byte> destination)
    {
        if (StructureRefs.Count > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"A chunk can carry at most {ushort.MaxValue} structure refs, has {StructureRefs.Count}.");
        }

        int size = SerializedSize;
        if (destination.Length < size)
        {
            throw new ArgumentException(
                $"Need {size} bytes to write a chunk, got {destination.Length}.",
                nameof(destination));
        }

        // Header. The reserved tail is written as zero every time so that an
        // untouched chunk hashes identically across builds.
        BinaryPrimitives.WriteInt32LittleEndian(destination, ChunkX);
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], ChunkY);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], FormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(
            destination[10..], (ushort)(Flags & ~ChunkFlags.CurrentlyLoaded));
        BinaryPrimitives.WriteUInt16LittleEndian(destination[12..], BiomePrimary);
        destination[14] = (byte)LayerPrimary;
        destination[15..HeaderSize].Clear();

        int offset = HeaderSize;

        // Metadata.
        _oreDensity.CopyTo(destination[offset..]);
        offset += OreDensityLength;

        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], (ushort)StructureRefs.Count);
        offset += sizeof(ushort);

        foreach (ushort structureRef in StructureRefs)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], structureRef);
            offset += sizeof(ushort);
        }

        destination[offset] = WalkableRatio;
        offset += sizeof(byte);

        BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], (uint)SpecialFlags);
        offset += sizeof(uint);

        // Tile data, in row-major index order — see Index.
        for (int i = 0; i < TileCount; i++)
        {
            _tiles[i].WriteTo(destination[offset..]);
            offset += Tile.SizeInBytes;
        }

        // Reserved entity section: always empty in this build.
        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], 0);
        offset += sizeof(ushort);

        return offset;
    }

    /// <summary>Serialises the chunk to a fresh payload buffer.</summary>
    public byte[] Serialize()
    {
        byte[] payload = new byte[SerializedSize];
        WriteTo(payload);
        return payload;
    }

    /// <summary>
    /// Parses a chunk payload.
    /// </summary>
    /// <param name="source">Exactly the payload produced by <see cref="WriteTo"/>.</param>
    /// <param name="fileName">Name used in exception messages.</param>
    /// <exception cref="SaveFormatException">
    /// If the buffer is truncated, carries a <c>format_version</c> this build has
    /// no parser for (§9.2 requires loud failure, never optimistic parsing), or
    /// declares entities this build cannot represent. Every one of these would
    /// otherwise yield a half-populated chunk that generation and streaming would
    /// happily treat as real world.
    /// </exception>
    public static Chunk ReadFrom(ReadOnlySpan<byte> source, string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        Require(source, 0, HeaderSize, fileName, "chunk header");

        ushort formatVersion = BinaryPrimitives.ReadUInt16LittleEndian(source[8..]);
        if (formatVersion != CurrentFormatVersion)
        {
            throw new SaveFormatException(
                fileName,
                $"Chunk format_version {formatVersion} is not supported by this build "
                + $"(expected {CurrentFormatVersion}).");
        }

        Chunk chunk = new Chunk(
            BinaryPrimitives.ReadInt32LittleEndian(source),
            BinaryPrimitives.ReadInt32LittleEndian(source[4..]))
        {
            FormatVersion = formatVersion,
            Flags = (ChunkFlags)BinaryPrimitives.ReadUInt16LittleEndian(source[10..]),
            BiomePrimary = BinaryPrimitives.ReadUInt16LittleEndian(source[12..]),
            LayerPrimary = ReadLayer(source[14], fileName),
        };

        int offset = HeaderSize;

        Require(source, offset, OreDensityLength, fileName, "ore_density");
        source.Slice(offset, OreDensityLength).CopyTo(chunk._oreDensity);
        offset += OreDensityLength;

        Require(source, offset, sizeof(ushort), fileName, "structure_ref_count");
        int structureRefCount = BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
        offset += sizeof(ushort);

        Require(source, offset, structureRefCount * sizeof(ushort), fileName, "structure_refs");
        for (int i = 0; i < structureRefCount; i++)
        {
            chunk.StructureRefs.Add(BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]));
            offset += sizeof(ushort);
        }

        Require(source, offset, sizeof(byte) + sizeof(uint), fileName, "chunk metadata tail");
        chunk.WalkableRatio = source[offset];
        offset += sizeof(byte);
        chunk.SpecialFlags = (ChunkSpecialFlags)BinaryPrimitives.ReadUInt32LittleEndian(source[offset..]);
        offset += sizeof(uint);

        Require(source, offset, TileCount * Tile.SizeInBytes, fileName, "tile data");
        for (int i = 0; i < TileCount; i++)
        {
            chunk._tiles[i] = Tile.ReadFrom(source[offset..]);
            offset += Tile.SizeInBytes;
        }

        Require(source, offset, sizeof(ushort), fileName, "entity_count");
        ushort entityCount = BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
        if (entityCount != 0)
        {
            throw new SaveFormatException(
                fileName,
                $"Chunk declares {entityCount} entities; entity payloads are not supported by "
                + "this build. The persistent-entity schema does not exist yet, so the section "
                + "cannot be skipped safely either.");
        }

        offset += sizeof(ushort);

        // Nothing may follow the entity section. Trailing bytes mean the writer
        // knew a longer format than this build does, so the sections that did
        // parse cannot be trusted to mean what they appear to mean.
        if (offset != source.Length)
        {
            throw new SaveFormatException(
                fileName,
                $"Chunk payload has {source.Length - offset} unexpected trailing bytes "
                + $"after {offset} bytes of known sections.");
        }

        return chunk;
    }

    /// <summary>
    /// Parses the header's <c>layer_primary</c> byte.
    /// </summary>
    /// <remarks>
    /// Validated rather than cast blindly: an unknown band would otherwise flow
    /// silently into generation and content lookups, which select tables by
    /// layer and would fall through to nothing.
    /// </remarks>
    private static WorldLayer ReadLayer(byte value, string fileName) => value switch
    {
        (byte)WorldLayer.Outside => WorldLayer.Outside,
        (byte)WorldLayer.Underground => WorldLayer.Underground,
        (byte)WorldLayer.Deep => WorldLayer.Deep,
        (byte)WorldLayer.Void => WorldLayer.Void,
        _ => throw new SaveFormatException(
            fileName, $"Chunk layer_primary {value} is not a known world layer (expected 0-3)."),
    };

    /// <summary>
    /// Encodes the chunk and writes it to <paramref name="path"/> through the
    /// save envelope — zstd, obfuscation, integrity hash and atomic write all
    /// belong to <see cref="SaveFile"/>. Returns the header written.
    /// </summary>
    /// <param name="seedInput">
    /// World seed. A keystream input, so loading uses the header's copy; passing
    /// the wrong seed here produces a file that fails its integrity check.
    /// </param>
    /// <remarks>
    /// The envelope is given this chunk's <see cref="FormatVersion"/>, not
    /// <see cref="CurrentFormatVersion"/>. The two must agree: format_version is
    /// also a keystream input, so an envelope that disagrees with the payload it
    /// wraps would be decrypted against the wrong key at the first migration.
    /// </remarks>
    public SaveEnvelope Save(string path, ulong seedInput, bool debug = false, uint? fileSalt = null) =>
        SaveFile.Save(path, SaveFileKind.Chunk, FormatVersion, seedInput, Serialize(), debug, fileSalt);

    /// <summary>
    /// Reads a chunk file written by <see cref="Save"/>.
    /// </summary>
    /// <exception cref="SaveFormatException">
    /// If the envelope is structurally invalid, carries some other payload kind,
    /// or the chunk payload itself does not parse.
    /// </exception>
    /// <exception cref="SaveIntegrityException">If the payload hash does not match.</exception>
    public static Chunk Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        SaveLoadResult result = SaveFile.Load(path);
        if (result.Envelope.FileKind != SaveFileKind.Chunk)
        {
            throw new SaveFormatException(
                path, $"Expected a chunk file, header says {result.Envelope.FileKind}.");
        }

        return ReadFrom(result.Payload, path);
    }

    /// <summary>Rejects a buffer too short for the section about to be read.</summary>
    private static void Require(
        ReadOnlySpan<byte> source, int offset, int length, string fileName, string section)
    {
        if (source.Length - offset < length)
        {
            throw new SaveFormatException(
                fileName,
                $"Chunk payload is truncated: need {length} bytes at offset {offset} for "
                + $"{section}, only {Math.Max(0, source.Length - offset)} remain.");
        }
    }

    /// <summary>Rejects a chunk-local coordinate outside the 64x64 grid.</summary>
    private static void ThrowIfOutOfRange(int x, int y)
    {
        if ((uint)x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, $"Chunk x must be 0-{Width - 1}.");
        }

        if ((uint)y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, $"Chunk y must be 0-{Height - 1}.");
        }
    }
}
