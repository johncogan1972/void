using System;
using System.Buffers.Binary;
using System.IO;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// VOID-020 acceptance tests: the chunk struct and its file format
/// (world-data-model-spec §3, §9.2, §9.3).
///
/// Everything here is the on-disk contract for a streamed world. A red test in
/// this file means either a save migration was made by accident, or a chunk
/// would come back from disk subtly different from the one that was written —
/// which surfaces in game as terrain that changes when it is evicted and
/// reloaded, not as a crash.
/// </summary>
public sealed class ChunkTests
{
    /// <summary>Seed used as the envelope keystream input. Fixed, never random.</summary>
    private const ulong TestSeed = 0x0123456789ABCDEFUL;

    /// <summary>
    /// Builds a chunk with every header and metadata field non-default and a
    /// varied, position-dependent tile field, so a round-trip that drops or
    /// transposes anything is visible.
    /// </summary>
    private static Chunk BuildPopulatedChunk()
    {
        Chunk chunk = new Chunk(-12, 34)
        {
            Flags = ChunkFlags.Modified | ChunkFlags.ContainsPlayerStructures,
            BiomePrimary = 0xBEEF,
            LayerPrimary = WorldLayer.Deep,
            WalkableRatio = 200,
            SpecialFlags = ChunkSpecialFlags.ContainsBossLair | ChunkSpecialFlags.ContainsWaterBody,
        };

        chunk.OreDensity[0] = 1;
        chunk.OreDensity[1] = 2;
        chunk.OreDensity[2] = 3;
        chunk.OreDensity[3] = 250;

        chunk.StructureRefs.Add(7);
        chunk.StructureRefs.Add(11);
        chunk.StructureRefs.Add(65535);

        for (int y = 0; y < Chunk.Height; y++)
        {
            for (int x = 0; x < Chunk.Width; x++)
            {
                chunk[x, y] = new Tile(
                    blockId: (ushort)((y * Chunk.Width) + x),
                    wallId: (ushort)(x * 3),
                    liquidType: LiquidType.Water,
                    liquidLevel: (byte)(y & 0xF),
                    flags: (x & 1) == 0 ? TileFlags.PlayerPlaced : TileFlags.None,
                    damage: (byte)(x & 0xFF));
            }
        }

        return chunk;
    }

    /// <summary>Asserts two chunks agree on every field, tile for tile.</summary>
    private static void AssertSameChunk(Chunk expected, Chunk actual)
    {
        Assert.Equal(expected.ChunkX, actual.ChunkX);
        Assert.Equal(expected.ChunkY, actual.ChunkY);
        Assert.Equal(expected.FormatVersion, actual.FormatVersion);
        Assert.Equal(expected.BiomePrimary, actual.BiomePrimary);
        Assert.Equal(expected.LayerPrimary, actual.LayerPrimary);
        Assert.Equal(expected.WalkableRatio, actual.WalkableRatio);
        Assert.Equal(expected.SpecialFlags, actual.SpecialFlags);
        Assert.Equal(expected.OreDensity.ToArray(), actual.OreDensity.ToArray());
        Assert.Equal(expected.StructureRefs, actual.StructureRefs);
        Assert.Equal(expected.Tiles.ToArray(), actual.Tiles.ToArray());
    }

    /// <summary>Runs a body against a unique temp path and always deletes it.</summary>
    private static void WithTempFile(Action<string> body)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".chunk");
        try
        {
            body(path);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// The whole point of the ticket: a chunk written to disk comes back
    /// identical. If this fails, evicting and reloading a chunk changes the
    /// world under the player.
    /// </summary>
    [Fact]
    public void RoundTripsThroughSaveFile()
    {
        WithTempFile(path =>
        {
            Chunk original = BuildPopulatedChunk();
            SaveEnvelope envelope = original.Save(path, TestSeed);

            Assert.Equal(SaveFileKind.Chunk, envelope.FileKind);
            Assert.Equal(Chunk.CurrentFormatVersion, envelope.FormatVersion);

            AssertSameChunk(original, Chunk.Load(path));
        });
    }

    /// <summary>
    /// The header is exactly 32 bytes, so the metadata section starts where every
    /// reader — including future migrators — expects it to.
    /// </summary>
    [Fact]
    public void HeaderIsExactlyThirtyTwoBytes()
    {
        Assert.Equal(32, Chunk.HeaderSize);

        Chunk chunk = new Chunk(0, 0);
        byte[] payload = chunk.Serialize();

        // Header, then ore_density(4) + count(2) + walkable(1) + special(4),
        // then 4096 tiles, then entity_count(2).
        Assert.Equal(32 + 11 + (Chunk.TileCount * Tile.SizeInBytes) + 2, payload.Length);

        // The 15 reserved bytes are written as zero.
        for (int i = 15; i < Chunk.HeaderSize; i++)
        {
            Assert.Equal(0, payload[i]);
        }
    }

    /// <summary>
    /// Tile order on disk is row-major, index = y * 64 + x. Generation, chunk
    /// streaming and lighting all walk rows; a transposed layout would mirror
    /// every world across its diagonal on reload.
    /// </summary>
    [Fact]
    public void TileOrderIsRowMajor()
    {
        Chunk chunk = new Chunk(0, 0);
        chunk[5, 9] = new Tile(blockId: 0x1234);

        Assert.Equal((9 * 64) + 5, Chunk.Index(5, 9));

        byte[] payload = chunk.Serialize();
        int tileBase = Chunk.HeaderSize + 11;
        int offset = tileBase + (((9 * 64) + 5) * Tile.SizeInBytes);

        Assert.Equal((ushort)0x1234, BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset)));
    }

    /// <summary>
    /// The transient "currently loaded" bit never reaches disk, while the
    /// persistent bits do. A chunk that loads already claiming to be resident
    /// would confuse the streaming set's bookkeeping on the very first load.
    /// </summary>
    [Fact]
    public void CurrentlyLoadedFlagIsNotPersisted()
    {
        WithTempFile(path =>
        {
            Chunk chunk = new Chunk(1, 1)
            {
                Flags = ChunkFlags.Modified
                    | ChunkFlags.ContainsPlayerStructures
                    | ChunkFlags.CurrentlyLoaded,
            };

            chunk.Save(path, TestSeed);
            Chunk loaded = Chunk.Load(path);

            Assert.Equal(ChunkFlags.Modified | ChunkFlags.ContainsPlayerStructures, loaded.Flags);

            // The in-memory chunk keeps its bit; only the bytes lose it.
            Assert.True(chunk.Flags.HasFlag(ChunkFlags.CurrentlyLoaded));
        });
    }

    /// <summary>
    /// A single flipped byte in the body fails the envelope's integrity check
    /// rather than loading a chunk with one corrupted region of terrain.
    /// </summary>
    [Fact]
    public void CorruptedFileFailsIntegrityRatherThanLoading()
    {
        WithTempFile(path =>
        {
            BuildPopulatedChunk().Save(path, TestSeed);

            byte[] file = File.ReadAllBytes(path);
            file[^1] ^= 0xFF;
            File.WriteAllBytes(path, file);

            Assert.ThrowsAny<Exception>(() => Chunk.Load(path));
        });
    }

    /// <summary>
    /// A payload that ends mid-chunk throws instead of returning a chunk whose
    /// tail is silently air — a partial world is worse than a refused load.
    /// </summary>
    [Fact]
    public void TruncatedPayloadThrows()
    {
        byte[] payload = BuildPopulatedChunk().Serialize();

        SaveFormatException error = Assert.Throws<SaveFormatException>(
            () => Chunk.ReadFrom(payload.AsSpan(0, payload.Length - 1), "truncated"));

        Assert.Contains("truncated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A format_version this build has no parser for fails loudly (§9.2), rather
    /// than being read optimistically against the wrong field layout.
    /// </summary>
    [Fact]
    public void UnknownFormatVersionThrows()
    {
        Chunk chunk = BuildPopulatedChunk();
        chunk.FormatVersion = (ushort)(Chunk.CurrentFormatVersion + 1);

        Assert.Throws<SaveFormatException>(() => Chunk.ReadFrom(chunk.Serialize(), "future"));
    }

    /// <summary>
    /// The entity section is reserved and empty in this build. A file claiming
    /// entities was written by something that knows a schema this code does not,
    /// so it cannot be skipped safely and must not be guessed at.
    /// </summary>
    [Fact]
    public void NonZeroEntityCountThrows()
    {
        byte[] payload = BuildPopulatedChunk().Serialize();
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(payload.Length - 2), 1);

        SaveFormatException error = Assert.Throws<SaveFormatException>(
            () => Chunk.ReadFrom(payload, "entities"));

        Assert.Contains("entity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Out-of-range indexing throws instead of clamping or wrapping. An
    /// off-by-one in a generation pass must be a stack trace, not a smeared edge
    /// column that looks like intentional terrain.
    /// </summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(64, 0)]
    [InlineData(0, 64)]
    public void OutOfRangeIndexingThrows(int x, int y)
    {
        Chunk chunk = new Chunk(0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => chunk[x, y]);
        Assert.Throws<ArgumentOutOfRangeException>(() => chunk[x, y] = Tile.Air);
    }

    /// <summary>
    /// A fresh chunk is all air with no metadata. Generation relies on the zeroed
    /// allocation being a valid empty chunk rather than needing a fill pass.
    /// </summary>
    [Fact]
    public void DefaultChunkIsAllAir()
    {
        Chunk chunk = new Chunk(3, -4);

        Assert.Equal(Chunk.CurrentFormatVersion, chunk.FormatVersion);
        Assert.Equal(ChunkFlags.None, chunk.Flags);
        Assert.Equal(ChunkSpecialFlags.None, chunk.SpecialFlags);
        Assert.Empty(chunk.StructureRefs);
        Assert.Equal(Chunk.TileCount, chunk.Tiles.Length);

        for (int y = 0; y < Chunk.Height; y++)
        {
            for (int x = 0; x < Chunk.Width; x++)
            {
                Assert.Equal(Tile.Air, chunk[x, y]);
                Assert.True(chunk[x, y].IsAir);
            }
        }
    }

    /// <summary>
    /// Chunk file naming is the §9.3 contract; the world's chunk directory is
    /// addressed by coordinate, so a change here orphans every existing chunk.
    /// </summary>
    [Fact]
    public void ChunkFileNameMatchesSpec()
    {
        Assert.Equal("0_0.chunk", Chunk.ChunkFileName(0, 0));
        Assert.Equal("-12_34.chunk", Chunk.ChunkFileName(-12, 34));
    }

    /// <summary>
    /// A round-trip is byte-identical, not merely field-equal. Re-serialising a
    /// loaded chunk must reproduce the original payload exactly, or the same
    /// chunk saved twice would hash differently and defeat the reference-seed
    /// determinism check.
    /// </summary>
    [Fact]
    public void RoundTripReserializesToIdenticalBytes()
    {
        WithTempFile(path =>
        {
            Chunk original = BuildPopulatedChunk();
            byte[] expected = original.Serialize();

            original.Save(path, TestSeed);
            byte[] actual = Chunk.Load(path).Serialize();

            Assert.Equal(expected, actual);
        });
    }

    /// <summary>
    /// Corrupting a byte inside the tile data is caught by the envelope's
    /// integrity hash. Written in debug mode so the flipped byte lands in the
    /// payload verbatim: this pins the failure to the hash check rather than to
    /// zstd happening to reject the stream, which is the guarantee that matters
    /// when a chunk file rots on disk.
    /// </summary>
    [Fact]
    public void CorruptedTileDataFailsIntegrityCheck()
    {
        WithTempFile(path =>
        {
            BuildPopulatedChunk().Save(path, TestSeed, debug: true);

            byte[] file = File.ReadAllBytes(path);
            int tileByte = SaveEnvelope.HeaderSize + Chunk.HeaderSize + 11
                + (Chunk.Index(20, 30) * Tile.SizeInBytes);
            file[tileByte] ^= 0xFF;
            File.WriteAllBytes(path, file);

            Assert.Throws<SaveIntegrityException>(() => Chunk.Load(path));
        });
    }

    /// <summary>
    /// A chunk file cut short — a crash mid-write, a truncating copy — is
    /// refused at the envelope, not loaded with its trailing tiles silently air.
    /// </summary>
    [Fact]
    public void TruncatedFileThrowsRatherThanLoadingPartialTiles()
    {
        WithTempFile(path =>
        {
            BuildPopulatedChunk().Save(path, TestSeed);

            byte[] file = File.ReadAllBytes(path);
            File.WriteAllBytes(path, file[..(file.Length / 2)]);

            Assert.Throws<SaveFormatException>(() => Chunk.Load(path));
        });
    }

    /// <summary>
    /// An undefined layer_primary byte is refused instead of becoming a
    /// nonsense <see cref="WorldLayer"/>. Generation and content lookups select
    /// tables by layer, so an unknown band silently selects nothing and the
    /// chunk generates empty rather than failing.
    /// </summary>
    [Fact]
    public void UnknownWorldLayerThrows()
    {
        byte[] payload = BuildPopulatedChunk().Serialize();
        payload[14] = 200;

        SaveFormatException e = Assert.Throws<SaveFormatException>(
            () => Chunk.ReadFrom(payload, "layer.chunk"));
        Assert.Contains("layer_primary", e.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Bytes past the entity section are refused. Extra trailing data means the
    /// writer knew a longer format than this build, so the sections that did
    /// parse cannot be assumed to mean what they appear to mean.
    /// </summary>
    [Fact]
    public void TrailingBytesAfterEntitySectionThrow()
    {
        byte[] payload = BuildPopulatedChunk().Serialize();
        byte[] padded = new byte[payload.Length + 4];
        payload.CopyTo(padded, 0);

        Assert.Throws<SaveFormatException>(() => Chunk.ReadFrom(padded, "trailing.chunk"));
    }

    /// <summary>
    /// The envelope records the same format_version as the payload it wraps.
    /// format_version is a keystream input, so a disagreement between the two
    /// would decrypt the body against a key that does not match the schema it
    /// actually holds — a corrupt-looking file at the first migration.
    /// </summary>
    [Fact]
    public void EnvelopeFormatVersionMatchesChunkHeader()
    {
        WithTempFile(path =>
        {
            Chunk chunk = BuildPopulatedChunk();
            SaveEnvelope envelope = chunk.Save(path, TestSeed);

            Assert.Equal(chunk.FormatVersion, envelope.FormatVersion);
            Assert.Equal(
                chunk.FormatVersion,
                BinaryPrimitives.ReadUInt16LittleEndian(chunk.Serialize().AsSpan(8)));
        });
    }
}
