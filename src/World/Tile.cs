using System;
using System.Buffers.Binary;

namespace Void;

/// <summary>
/// One tile of the world, packed into exactly 8 bytes (VOID-019,
/// world-data-model-spec §2).
///
/// The packing is not a micro-optimisation. A Medium world is 11,520,000 tiles,
/// so a byte per tile is 11.5 MB resident: 8 bytes is ~92 MB where the spec's
/// unpacked 9-byte field table would be ~104 MB, and ~138 MB once padded to 12
/// for alignment.
///
/// <b>Bit layout</b> — the storage is a single <c>ulong</c>, and the bit offsets
/// are chosen so that writing it little-endian produces this byte layout, which
/// is the on-disk chunk format:
///
/// <code>
/// byte 0-1  block_id      bits  0-15   uint16
/// byte 2-3  wall_id       bits 16-31   uint16
/// byte 4    liquid        bits 32-35   liquid_type  (low nibble)
///                         bits 36-39   liquid_level (high nibble)
/// byte 5-6  flags         bits 40-55   uint16
/// byte 7    damage        bits 56-63   uint8
/// </code>
///
/// <b>Liquid resolution is 0–15, not 0–255.</b> The spec's field table sums to 9
/// bytes; §2 resolves that by packing <c>liquid_type</c> and <c>liquid_level</c>
/// into one byte as two nibbles, and that is what this implements. Liquid fill
/// reads as a visual gradient rather than a measurement, so 16 levels is enough,
/// and the byte buys ~46 MB back on a Medium world. This is baked into the save
/// format: widening it later is a migration, not an edit.
///
/// A default <c>Tile</c> is all zeroes, which is exactly air with no wall, no
/// liquid, no flags and no damage — see <see cref="Air"/>.
/// </summary>
public readonly struct Tile : IEquatable<Tile>
{
    /// <summary>Serialised size of one tile, in bytes. Fixed by the format.</summary>
    public const int SizeInBytes = 8;

    /// <summary>Largest storable liquid level. The field is a nibble.</summary>
    public const byte MaxLiquidLevel = 15;

    /// <summary>Largest storable liquid type value. The field is a nibble.</summary>
    public const byte MaxLiquidType = 15;

    // Bit offsets into the packed word. Quoted from the layout table above; the
    // masks are derived rather than written out so the two cannot disagree.
    private const int ShiftBlockId = 0;
    private const int ShiftWallId = 16;
    private const int ShiftLiquidType = 32;
    private const int ShiftLiquidLevel = 36;
    private const int ShiftFlags = 40;
    private const int ShiftDamage = 56;

    private const ulong MaskUInt16 = 0xFFFFUL;
    private const ulong MaskNibble = 0xFUL;
    private const ulong MaskByte = 0xFFUL;

    private readonly ulong _bits;

    /// <summary>Wraps an already-packed word. Private so the layout stays internal to this type.</summary>
    private Tile(ulong bits) => _bits = bits;

    /// <summary>
    /// Air: no block, no wall, no liquid, no flags, no damage.
    /// </summary>
    /// <remarks>
    /// Identical to <c>default(Tile)</c>, which is deliberate — a zeroed chunk
    /// buffer is already a valid chunk of air, so allocation needs no fill pass.
    /// Air is a real block id (0), not an absence; see world-data-model-spec §2.
    /// </remarks>
    public static Tile Air => default;

    /// <summary>
    /// Builds a tile from its fields.
    /// </summary>
    /// <param name="blockId">Foreground block; 0 is air.</param>
    /// <param name="wallId">Background wall; 0 is no wall.</param>
    /// <param name="liquidType">Liquid occupying the tile.</param>
    /// <param name="liquidLevel">Fill amount, 0–15. Not 0–255 — see the type remarks.</param>
    /// <param name="flags">Tile flags.</param>
    /// <param name="damage">Mining damage state; 0 is pristine.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If <paramref name="liquidLevel"/> or <paramref name="liquidType"/> exceeds
    /// its nibble. Silently truncating would turn a full tile into an empty one.
    /// </exception>
    public Tile(
        ushort blockId,
        ushort wallId = 0,
        LiquidType liquidType = LiquidType.None,
        byte liquidLevel = 0,
        TileFlags flags = TileFlags.None,
        byte damage = 0)
    {
        ThrowIfLiquidTypeOutOfRange(liquidType);
        ThrowIfLiquidLevelOutOfRange(liquidLevel);

        _bits =
            ((ulong)blockId << ShiftBlockId)
            | ((ulong)wallId << ShiftWallId)
            | (((ulong)liquidType & MaskNibble) << ShiftLiquidType)
            | (((ulong)liquidLevel & MaskNibble) << ShiftLiquidLevel)
            | (((ulong)flags & MaskUInt16) << ShiftFlags)
            | ((ulong)damage << ShiftDamage);
    }

    /// <summary>Foreground block id. 0 is air, which is a real registry entry.</summary>
    public ushort BlockId => (ushort)((_bits >> ShiftBlockId) & MaskUInt16);

    /// <summary>Background wall id. 0 is no wall, which is a real registry entry.</summary>
    public ushort WallId => (ushort)((_bits >> ShiftWallId) & MaskUInt16);

    /// <summary>Liquid occupying this tile.</summary>
    public LiquidType LiquidType => (LiquidType)((_bits >> ShiftLiquidType) & MaskNibble);

    /// <summary>Fill amount, 0–15. See the type remarks for why this is not 0–255.</summary>
    public byte LiquidLevel => (byte)((_bits >> ShiftLiquidLevel) & MaskNibble);

    /// <summary>Tile flags.</summary>
    public TileFlags Flags => (TileFlags)((_bits >> ShiftFlags) & MaskUInt16);

    /// <summary>Mining damage state. 0 is pristine.</summary>
    public byte Damage => (byte)((_bits >> ShiftDamage) & MaskByte);

    /// <summary>True if no block occupies this tile. It may still hold a wall or liquid.</summary>
    public bool IsAir => BlockId == ContentIds.AirBlock;

    /// <summary>True if any liquid is present at a non-zero level.</summary>
    public bool HasLiquid => LiquidType != LiquidType.None && LiquidLevel > 0;

    /// <summary>The packed representation, for callers writing bulk tile arrays.</summary>
    public ulong PackedValue => _bits;

    /// <summary>Rebuilds a tile from a packed word produced by <see cref="PackedValue"/>.</summary>
    public static Tile FromPacked(ulong packed) => new Tile(packed);

    /// <summary>Returns a copy with a different block id.</summary>
    public Tile WithBlockId(ushort blockId) => Replace(blockId, ShiftBlockId, MaskUInt16);

    /// <summary>Returns a copy with a different wall id.</summary>
    public Tile WithWallId(ushort wallId) => Replace(wallId, ShiftWallId, MaskUInt16);

    /// <summary>Returns a copy with a different mining damage state.</summary>
    public Tile WithDamage(byte damage) => Replace(damage, ShiftDamage, MaskByte);

    /// <summary>Returns a copy with a different flag set, replacing all bits.</summary>
    public Tile WithFlags(TileFlags flags) => Replace((ulong)flags, ShiftFlags, MaskUInt16);

    /// <summary>
    /// Returns a copy with a different liquid.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If either value exceeds its nibble.</exception>
    public Tile WithLiquid(LiquidType liquidType, byte liquidLevel)
    {
        ThrowIfLiquidTypeOutOfRange(liquidType);
        ThrowIfLiquidLevelOutOfRange(liquidLevel);

        ulong cleared = _bits
            & ~(MaskNibble << ShiftLiquidType)
            & ~(MaskNibble << ShiftLiquidLevel);

        return new Tile(cleared
            | (((ulong)liquidType & MaskNibble) << ShiftLiquidType)
            | (((ulong)liquidLevel & MaskNibble) << ShiftLiquidLevel));
    }

    /// <summary>True if every bit in <paramref name="flags"/> is set.</summary>
    public bool HasFlags(TileFlags flags) => (Flags & flags) == flags;

    /// <summary>Returns a copy with <paramref name="flags"/> added, leaving other bits alone.</summary>
    public Tile WithFlagsSet(TileFlags flags) => WithFlags(Flags | flags);

    /// <summary>Returns a copy with <paramref name="flags"/> removed, leaving other bits alone.</summary>
    public Tile WithFlagsCleared(TileFlags flags) => WithFlags(Flags & ~flags);

    /// <summary>
    /// Writes the tile as 8 little-endian bytes.
    /// </summary>
    /// <param name="destination">At least <see cref="SizeInBytes"/> bytes.</param>
    /// <remarks>
    /// Endianness is explicit rather than inherited from the host, so a save
    /// written on one architecture reads identically on another — the same rule
    /// the save envelope and payload writer follow.
    /// </remarks>
    /// <exception cref="ArgumentException">If the span is too short.</exception>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < SizeInBytes)
        {
            throw new ArgumentException(
                $"Need {SizeInBytes} bytes to write a tile, got {destination.Length}.",
                nameof(destination));
        }

        BinaryPrimitives.WriteUInt64LittleEndian(destination, _bits);
    }

    /// <summary>Reads a tile from 8 little-endian bytes.</summary>
    /// <param name="source">At least <see cref="SizeInBytes"/> bytes.</param>
    /// <exception cref="ArgumentException">If the span is too short.</exception>
    public static Tile ReadFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length < SizeInBytes)
        {
            throw new ArgumentException(
                $"Need {SizeInBytes} bytes to read a tile, got {source.Length}.",
                nameof(source));
        }

        return new Tile(BinaryPrimitives.ReadUInt64LittleEndian(source));
    }

    /// <summary>Value equality over the packed word.</summary>
    public bool Equals(Tile other) => _bits == other._bits;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Tile other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _bits.GetHashCode();

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Tile left, Tile right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Tile left, Tile right) => !left.Equals(right);

    /// <summary>Debug rendering. Not a serialisation format — do not parse it.</summary>
    public override string ToString() =>
        $"Tile(block={BlockId}, wall={WallId}, liquid={LiquidType}:{LiquidLevel}, "
        + $"flags={Flags}, damage={Damage})";

    /// <summary>Replaces one field in the packed word, leaving the others untouched.</summary>
    private Tile Replace(ulong value, int shift, ulong mask) =>
        new Tile((_bits & ~(mask << shift)) | ((value & mask) << shift));

    /// <summary>Rejects a liquid level that would not survive the nibble.</summary>
    private static void ThrowIfLiquidLevelOutOfRange(byte liquidLevel)
    {
        if (liquidLevel > MaxLiquidLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(liquidLevel),
                liquidLevel,
                $"Liquid level is a nibble: 0-{MaxLiquidLevel}. The packed tile has no room "
                + "for 0-255; see world-data-model-spec §2.");
        }
    }

    /// <summary>Rejects a liquid type that would not survive the nibble.</summary>
    private static void ThrowIfLiquidTypeOutOfRange(LiquidType liquidType)
    {
        if ((byte)liquidType > MaxLiquidType)
        {
            throw new ArgumentOutOfRangeException(
                nameof(liquidType),
                liquidType,
                $"Liquid type is a nibble: 0-{MaxLiquidType}.");
        }
    }
}
