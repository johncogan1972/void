using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Void;

namespace Void.Tests;

/// <summary>
/// VOID-019 acceptance tests: the 8-byte packed tile.
///
/// The size and byte-layout assertions here are the on-disk contract for every
/// chunk file. If one of them goes red, either the packing changed — which is a
/// save migration, not an edit — or a field is corrupting its neighbours.
/// </summary>
public class TileTests
{
    /// <summary>
    /// The packing rationale in one assertion: 8 bytes, not the spec table's 9.
    ///
    /// A Medium world is 11,520,000 tiles, so each byte here is 11.5 MB resident.
    /// </summary>
    [Fact]
    public void TileIsExactlyEightBytes()
    {
        Assert.Equal(8, Unsafe.SizeOf<Tile>());
        Assert.Equal(8, Tile.SizeInBytes);
    }

    /// <summary>
    /// A zeroed tile is air with no wall, no liquid and no damage.
    ///
    /// Chunk buffers are allocated zeroed, so this is what makes a fresh chunk
    /// valid without a fill pass.
    /// </summary>
    [Fact]
    public void DefaultTileIsAir()
    {
        Tile tile = default;

        Assert.Equal(Tile.Air, tile);
        Assert.Equal(ContentIds.AirBlock, tile.BlockId);
        Assert.Equal(ContentIds.NoWall, tile.WallId);
        Assert.Equal(LiquidType.None, tile.LiquidType);
        Assert.Equal(0, tile.LiquidLevel);
        Assert.Equal(TileFlags.None, tile.Flags);
        Assert.Equal(0, tile.Damage);
        Assert.True(tile.IsAir);
        Assert.False(tile.HasLiquid);
    }

    /// <summary>
    /// Every field round-trips at the extremes of its declared range.
    ///
    /// Boundary values are the ones that expose a mask that is one bit too small
    /// or a shift that overlaps the next field.
    /// </summary>
    [Theory]
    [InlineData(0, 0, LiquidType.None, 0, TileFlags.None, 0)]
    [InlineData(ushort.MaxValue, ushort.MaxValue, LiquidType.LiquidVoid, 15, (TileFlags)0xFFFF, byte.MaxValue)]
    [InlineData(1, 2, LiquidType.Water, 7, TileFlags.PlayerPlaced, 3)]
    [InlineData(ushort.MaxValue, 0, LiquidType.None, 15, TileFlags.None, byte.MaxValue)]
    [InlineData(0, ushort.MaxValue, LiquidType.Lava, 0, (TileFlags)0xFFFF, 0)]
    public void EveryFieldRoundTripsAtItsBoundaries(
        ushort blockId,
        ushort wallId,
        LiquidType liquidType,
        byte liquidLevel,
        TileFlags flags,
        byte damage)
    {
        Tile tile = new Tile(blockId, wallId, liquidType, liquidLevel, flags, damage);

        Assert.Equal(blockId, tile.BlockId);
        Assert.Equal(wallId, tile.WallId);
        Assert.Equal(liquidType, tile.LiquidType);
        Assert.Equal(liquidLevel, tile.LiquidLevel);
        Assert.Equal(flags, tile.Flags);
        Assert.Equal(damage, tile.Damage);
    }

    /// <summary>
    /// Setting one field never disturbs another.
    ///
    /// Starts from a tile with every field at its maximum, so a mistake in any
    /// mask shows up as a neighbour losing bits rather than as a plausible value.
    /// </summary>
    [Fact]
    public void MutatingOneFieldLeavesTheOthersIntact()
    {
        Tile full = new Tile(
            ushort.MaxValue,
            ushort.MaxValue,
            LiquidType.LiquidVoid,
            Tile.MaxLiquidLevel,
            (TileFlags)0xFFFF,
            byte.MaxValue);

        Tile changed = full.WithBlockId(42);
        Assert.Equal(42, changed.BlockId);
        Assert.Equal(ushort.MaxValue, changed.WallId);
        Assert.Equal(LiquidType.LiquidVoid, changed.LiquidType);
        Assert.Equal(Tile.MaxLiquidLevel, changed.LiquidLevel);
        Assert.Equal((TileFlags)0xFFFF, changed.Flags);
        Assert.Equal(byte.MaxValue, changed.Damage);

        changed = full.WithWallId(0);
        Assert.Equal(0, changed.WallId);
        Assert.Equal(ushort.MaxValue, changed.BlockId);
        Assert.Equal(byte.MaxValue, changed.Damage);

        changed = full.WithDamage(1);
        Assert.Equal(1, changed.Damage);
        Assert.Equal(ushort.MaxValue, changed.BlockId);
        Assert.Equal((TileFlags)0xFFFF, changed.Flags);

        changed = full.WithLiquid(LiquidType.Water, 3);
        Assert.Equal(LiquidType.Water, changed.LiquidType);
        Assert.Equal(3, changed.LiquidLevel);
        Assert.Equal(ushort.MaxValue, changed.BlockId);
        Assert.Equal(ushort.MaxValue, changed.WallId);
        Assert.Equal(byte.MaxValue, changed.Damage);
    }

    /// <summary>
    /// Liquid type and level share a byte, so each must be settable without
    /// clobbering the other — the specific risk the nibble packing introduces.
    /// </summary>
    [Fact]
    public void LiquidTypeAndLevelDoNotClobberEachOther()
    {
        Tile tile = Tile.Air.WithLiquid(LiquidType.Lava, Tile.MaxLiquidLevel);
        Assert.Equal(LiquidType.Lava, tile.LiquidType);
        Assert.Equal(Tile.MaxLiquidLevel, tile.LiquidLevel);

        tile = tile.WithLiquid(LiquidType.None, 0);
        Assert.Equal(LiquidType.None, tile.LiquidType);
        Assert.Equal(0, tile.LiquidLevel);

        tile = tile.WithLiquid(LiquidType.PoisonGas, 1);
        Assert.Equal(LiquidType.PoisonGas, tile.LiquidType);
        Assert.Equal(1, tile.LiquidLevel);
    }

    /// <summary>
    /// A liquid level above 15 is rejected rather than truncated.
    ///
    /// Silent truncation is the dangerous failure: level 16 would wrap to 0 and
    /// turn a full tile into an empty one, which reads as a flow bug much later.
    /// </summary>
    [Theory]
    [InlineData(16)]
    [InlineData(255)]
    public void LiquidLevelAboveANibbleIsRejected(byte level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Tile(1, 0, LiquidType.Water, level));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Tile.Air.WithLiquid(LiquidType.Water, level));
    }

    /// <summary>A liquid type outside the nibble is rejected the same way.</summary>
    [Fact]
    public void LiquidTypeAboveANibbleIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Tile(1, 0, (LiquidType)16, 1));
    }

    /// <summary>
    /// Flags are individually settable and clearable without touching neighbours
    /// or each other.
    /// </summary>
    [Fact]
    public void FlagBitsSetAndClearIndependently()
    {
        Tile tile = new Tile(5, 6, LiquidType.Water, 4, TileFlags.None, 7);

        tile = tile.WithFlagsSet(TileFlags.PlayerPlaced);
        Assert.True(tile.HasFlags(TileFlags.PlayerPlaced));
        Assert.False(tile.HasFlags(TileFlags.Wire));

        tile = tile.WithFlagsSet(TileFlags.Wire | TileFlags.PartOfPrefab);
        Assert.True(tile.HasFlags(TileFlags.PlayerPlaced | TileFlags.Wire | TileFlags.PartOfPrefab));

        tile = tile.WithFlagsCleared(TileFlags.Wire);
        Assert.False(tile.HasFlags(TileFlags.Wire));
        Assert.True(tile.HasFlags(TileFlags.PlayerPlaced));
        Assert.True(tile.HasFlags(TileFlags.PartOfPrefab));

        // The surrounding fields are untouched by all of that.
        Assert.Equal(5, tile.BlockId);
        Assert.Equal(6, tile.WallId);
        Assert.Equal(LiquidType.Water, tile.LiquidType);
        Assert.Equal(4, tile.LiquidLevel);
        Assert.Equal(7, tile.Damage);
    }

    /// <summary>
    /// Each field lands at its documented byte offset, little-endian.
    ///
    /// This is the on-disk chunk layout. Asserting the bytes rather than just the
    /// round-trip is what catches a reordering that is self-consistent in memory
    /// but silently breaks every save already on a player's disk.
    /// </summary>
    [Fact]
    public void FieldsLandAtTheirDocumentedByteOffsets()
    {
        Tile tile = new Tile(
            blockId: 0x1234,
            wallId: 0x5678,
            liquidType: (LiquidType)0x3,
            liquidLevel: 0xA,
            flags: (TileFlags)0x9BCD,
            damage: 0xEF);

        byte[] buffer = new byte[Tile.SizeInBytes];
        tile.WriteTo(buffer);

        Assert.Equal((ushort)0x1234, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(0)));
        Assert.Equal((ushort)0x5678, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(2)));

        // Liquid shares one byte: type in the low nibble, level in the high one.
        Assert.Equal(0xA3, buffer[4]);

        Assert.Equal((ushort)0x9BCD, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(5)));
        Assert.Equal(0xEF, buffer[7]);
    }

    /// <summary>
    /// Serialisation round-trips through a byte span and is explicitly
    /// little-endian, so a save moves between architectures unchanged.
    /// </summary>
    [Fact]
    public void WriteAndReadRoundTripThroughBytes()
    {
        Tile original = new Tile(
            0xABCD,
            0x1234,
            LiquidType.PoisonWater,
            9,
            TileFlags.PlayerPlaced | TileFlags.Structural,
            200);

        byte[] buffer = new byte[Tile.SizeInBytes];
        original.WriteTo(buffer);

        Assert.Equal(original, Tile.ReadFrom(buffer));
        Assert.Equal(original.PackedValue, BinaryPrimitives.ReadUInt64LittleEndian(buffer));
        Assert.Equal(original, Tile.FromPacked(original.PackedValue));
    }

    /// <summary>
    /// A short span is a caller bug and fails loudly, rather than writing a
    /// partial tile that would corrupt the following one in a chunk array.
    /// </summary>
    [Fact]
    public void ShortSpansAreRejected()
    {
        byte[] tooShort = new byte[Tile.SizeInBytes - 1];

        Assert.Throws<ArgumentException>(() => Tile.Air.WriteTo(tooShort));
        Assert.Throws<ArgumentException>(() => Tile.ReadFrom(tooShort));
    }

    /// <summary>
    /// Writing many tiles back to back produces a dense array with no padding —
    /// the property the ~92 MB budget for a Medium world depends on.
    /// </summary>
    [Fact]
    public void TilesPackDenselyInSequence()
    {
        const int count = 64;
        byte[] buffer = new byte[count * Tile.SizeInBytes];

        for (int i = 0; i < count; i++)
        {
            new Tile((ushort)i, (ushort)(i * 2), LiquidType.Water, (byte)(i % 16))
                .WriteTo(buffer.AsSpan(i * Tile.SizeInBytes));
        }

        for (int i = 0; i < count; i++)
        {
            Tile read = Tile.ReadFrom(buffer.AsSpan(i * Tile.SizeInBytes));

            Assert.Equal((ushort)i, read.BlockId);
            Assert.Equal((ushort)(i * 2), read.WallId);
            Assert.Equal((byte)(i % 16), read.LiquidLevel);
        }
    }

    /// <summary>
    /// Air with a wall is a real, distinct state — it is how an interior room
    /// exists (world-data-model-spec §2). Air must not imply "empty tile".
    /// </summary>
    [Fact]
    public void AirCanStillCarryAWallAndLiquid()
    {
        Tile roomInterior = Tile.Air.WithWallId(2).WithLiquid(LiquidType.Water, 6);

        Assert.True(roomInterior.IsAir);
        Assert.Equal(2, roomInterior.WallId);
        Assert.True(roomInterior.HasLiquid);
        Assert.NotEqual(Tile.Air, roomInterior);
    }

    /// <summary>
    /// HasLiquid means "actually wet": a type with zero level, or a level with no
    /// type, is not liquid. Flow code drains to one of those two states.
    /// </summary>
    [Fact]
    public void HasLiquidRequiresBothATypeAndALevel()
    {
        Assert.False(Tile.Air.WithLiquid(LiquidType.Water, 0).HasLiquid);
        Assert.False(Tile.Air.WithLiquid(LiquidType.None, 5).HasLiquid);
        Assert.True(Tile.Air.WithLiquid(LiquidType.Water, 1).HasLiquid);
    }

    /// <summary>
    /// Equality is over the whole packed word, so tiles can be compared directly
    /// when diffing a chunk for replication.
    /// </summary>
    [Fact]
    public void EqualityComparesEveryField()
    {
        Tile a = new Tile(1, 2, LiquidType.Water, 3, TileFlags.Wire, 4);
        Tile b = new Tile(1, 2, LiquidType.Water, 3, TileFlags.Wire, 4);
        Tile c = b.WithDamage(5);

        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a != c);
        Assert.False(a.Equals(c));
    }
}
