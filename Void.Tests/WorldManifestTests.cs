using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// VOID-021 acceptance tests: the world manifest schema and its file format
/// (world-data-model-spec §4, save-format-spec §3).
///
/// The manifest is world-level truth. A red test here means either a save
/// migration happened by accident, or a loaded world differs from the saved one
/// in a way that shows up in game as a moved spawn, a resurrected boss, a lost
/// side anchor or chunks the streamer cannot find — never as a clean crash.
/// </summary>
public sealed class WorldManifestTests
{
    /// <summary>A world id fixed in source; tests must never mint a random one.</summary>
    private static readonly Guid TestWorldId = new Guid("11111111-2222-3333-4444-555555555555");

    /// <summary>The world an anchor was carried into, to prove cross-world references survive.</summary>
    private static readonly Guid OtherWorldId = new Guid("99999999-8888-7777-6666-555555555555");

    /// <summary>A player id fixed in source, so the round-trip assertion is reproducible.</summary>
    private static readonly PlayerId Carrier = new PlayerId(new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

    /// <summary>
    /// Builds a manifest with every field non-default, both nullable fields
    /// populated and both left null, so a round-trip that drops, reorders or
    /// defaults anything is visible.
    /// </summary>
    private static WorldManifest BuildPopulated()
    {
        WorldManifest manifest = new WorldManifest
        {
            WorldId = TestWorldId,
            WorldType = "portal_scorched",
            Seed = -6_148_914_691_236_517_206L,
            GenVersion = "0.3.1",
            SizePreset = "medium",
            Dimensions = new WorldDimensions(4200, 1200, 66, 19),
            LayerBoundaries = new LayerBoundaries(240, 600, 1000),
            PlayerSpawn = new TilePosition(2100, 231),
            MainBossLair = new BossLair(1712, 884, "lair_scorched_01"),
            PortalCandidates =
            {
                new PortalCandidate(1, 300, 640),
                new PortalCandidate(2, 3900, 705),
                new PortalCandidate(65535, 12, 1199),
            },
            MainBossKilled = true,
            SideAnchors =
            {
                // Still lying in the world: unowned and unplaced.
                new SideAnchor(1, false, null, null),

                // Carried by a player, not yet installed.
                new SideAnchor(2, true, Carrier, null),

                // Installed in a different world entirely.
                new SideAnchor(65535, true, null, new WorldPosition(OtherWorldId, 40, 41)),
            },
            ActiveHearth = new WorldPosition(TestWorldId, 2101, 230),
        };

        manifest.AddChunkIndexEntry(0, 0);
        manifest.AddChunkIndexEntry(-12, 34, modified: true);
        return manifest;
    }

    /// <summary>Asserts field-for-field equality, nested values included.</summary>
    private static void AssertSame(WorldManifest expected, WorldManifest actual)
    {
        Assert.Equal(expected.WorldId, actual.WorldId);
        Assert.Equal(expected.WorldType, actual.WorldType);
        Assert.Equal(expected.Seed, actual.Seed);
        Assert.Equal(expected.GenVersion, actual.GenVersion);
        Assert.Equal(expected.SizePreset, actual.SizePreset);
        Assert.Equal(expected.Dimensions, actual.Dimensions);
        Assert.Equal(expected.LayerBoundaries, actual.LayerBoundaries);
        Assert.Equal(expected.PlayerSpawn, actual.PlayerSpawn);
        Assert.Equal(expected.MainBossLair, actual.MainBossLair);
        Assert.Equal(expected.PortalCandidates, actual.PortalCandidates);
        Assert.Equal(expected.MainBossKilled, actual.MainBossKilled);
        Assert.Equal(expected.SideAnchors, actual.SideAnchors);
        Assert.Equal(expected.ActiveHearth, actual.ActiveHearth);
        Assert.Equal(expected.ChunkIndex, actual.ChunkIndex);
    }

    /// <summary>Runs a body against a unique temp path and always deletes it.</summary>
    private static void WithTempFile(Action<string> body)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".manifest");
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
    /// The whole point of the ticket: a manifest written to disk comes back
    /// identical. If this fails, saving and reloading a world silently changes
    /// world-level state — spawn, boss progress, anchor ownership.
    /// </summary>
    [Fact]
    public void RoundTripsThroughSaveFile()
    {
        WithTempFile(path =>
        {
            WorldManifest original = BuildPopulated();
            SaveEnvelope envelope = original.Save(path);

            Assert.Equal(SaveFileKind.WorldManifest, envelope.FileKind);
            Assert.Equal(WorldManifest.CurrentFormatVersion, envelope.FormatVersion);

            AssertSame(original, WorldManifest.Load(path));
        });
    }

    /// <summary>
    /// Null and empty must survive as null and empty. "No anchor holder" and
    /// "no hearth set" are real states; if they came back as a default value or
    /// an absent field, a fresh world would look like one already in progress.
    /// </summary>
    [Fact]
    public void NullsAndEmptyCollectionsSurviveRoundTrip()
    {
        WithTempFile(path =>
        {
            WorldManifest original = new WorldManifest
            {
                WorldId = TestWorldId,
                WorldType = "home",
                Seed = 0,
                GenVersion = "0.1.0",
                SizePreset = "small",
                Dimensions = new WorldDimensions(1, 2, 3, 4),
                LayerBoundaries = new LayerBoundaries(1, 2, 3),
                PlayerSpawn = new TilePosition(0, 0),
                MainBossLair = new BossLair(0, 0, "none"),
                SideAnchors = { new SideAnchor(7, false, null, null) },
                ActiveHearth = null,
            };

            original.Save(path);
            WorldManifest loaded = WorldManifest.Load(path);

            Assert.Null(loaded.ActiveHearth);
            Assert.Empty(loaded.PortalCandidates);
            Assert.Empty(loaded.ChunkIndex);
            SideAnchor anchor = Assert.Single(loaded.SideAnchors);
            Assert.Null(anchor.PickedUpBy);
            Assert.Null(anchor.PlacedAt);

            // Absence is not the same as null: the writer emits both explicitly.
            string json = Encoding.UTF8.GetString(original.Serialize());
            Assert.Contains("\"active_hearth\":null", json, StringComparison.Ordinal);
            Assert.Contains("\"picked_up_by\":null", json, StringComparison.Ordinal);
            Assert.Contains("\"portal_candidates\":[]", json, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A manifest from a future build must fail loudly and name the version it
    /// found. Parsing it optimistically would hand generation a world with
    /// fields it does not understand and no way to notice.
    /// </summary>
    [Fact]
    public void UnknownFormatVersionThrowsNamingTheVersion()
    {
        WithTempFile(path =>
        {
            WorldManifest manifest = BuildPopulated();
            manifest.FormatVersion = 999;
            manifest.Save(path);

            SaveFormatException error =
                Assert.Throws<SaveFormatException>(() => WorldManifest.Load(path));
            Assert.Contains("999", error.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A file of the wrong kind is rejected on its header, before any JSON is
    /// parsed. Without this a chunk handed to the manifest loader would surface
    /// as a confusing parse error rather than "that is not a world manifest".
    /// </summary>
    [Fact]
    public void WrongFileKindThrows()
    {
        WithTempFile(path =>
        {
            new Chunk(0, 0).Save(path, 1234UL);

            SaveFormatException error =
                Assert.Throws<SaveFormatException>(() => WorldManifest.Load(path));
            Assert.Contains("Chunk", error.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Serialised field order matches the spec's field order. Not cosmetic: a
    /// stable order is what makes two saves diffable when debugging a world that
    /// went wrong, and declaration order alone is not a contract.
    /// </summary>
    [Fact]
    public void JsonPropertyOrderMatchesSpec()
    {
        WithTempFile(path =>
        {
            BuildPopulated().Save(path, debug: true);

            // Debug mode (§14) stores the payload verbatim, so this is the JSON.
            SaveLoadResult result = SaveFile.Load(path);
            string json = Encoding.UTF8.GetString(result.Payload.ToArray());

            string[] expectedOrder =
            {
                "\"world_id\"", "\"world_type\"", "\"seed\"", "\"gen_version\"", "\"size_preset\"",
                "\"dimensions\"", "\"layer_boundaries\"", "\"player_spawn\"", "\"main_boss_lair\"",
                "\"portal_candidates\"", "\"main_boss_killed\"", "\"side_anchors\"",
                "\"active_hearth\"", "\"chunk_index\"",
            };

            int previous = -1;
            foreach (string key in expectedOrder)
            {
                int index = json.IndexOf(key, StringComparison.Ordinal);
                Assert.True(index > previous, $"{key} is out of spec order in: {json}");
                previous = index;
            }
        });
    }

    /// <summary>
    /// Nested objects keep the spec's field order too, so the whole document
    /// diffs cleanly rather than just its top level.
    /// </summary>
    [Fact]
    public void NestedJsonPropertyOrderMatchesSpec()
    {
        string json = Encoding.UTF8.GetString(BuildPopulated().Serialize());

        Assert.Contains(
            "\"dimensions\":{\"width_tiles\":4200,\"height_tiles\":1200,\"chunks_x\":66,\"chunks_y\":19}",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"layer_boundaries\":{\"outside_end\":240,\"underground_end\":600,\"deep_end\":1000}",
            json,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A player id serialises as a bare string, not an object. Changing that
    /// shape would break every existing save that has an anchor in a pocket.
    /// </summary>
    [Fact]
    public void PlayerIdSerialisesAsPlainString()
    {
        string json = Encoding.UTF8.GetString(BuildPopulated().Serialize());
        Assert.Contains(
            $"\"picked_up_by\":\"{Carrier}\"",
            json,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The index builds paths the chunk loader actually accepts. This is the
    /// join between two file formats: if the manifest's file name drifts from
    /// <see cref="Chunk.ChunkFileName"/>, streaming finds nothing on disk and a
    /// saved world loads as empty air.
    /// </summary>
    [Fact]
    public void ChunkIndexPathRoundTripsIntoChunkLoad()
    {
        string campaignDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            WorldManifest manifest = BuildPopulated();
            manifest.ChunkIndex.Clear();
            ChunkIndexEntry entry = manifest.AddChunkIndexEntry(-12, 34, modified: true);

            Assert.Equal(
                $"worlds/{TestWorldId:D}/chunks/{Chunk.ChunkFileName(-12, 34)}",
                entry.File);

            string chunkPath = entry.ResolvePath(campaignDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(chunkPath)!);

            Chunk written = new Chunk(-12, 34) { BiomePrimary = 0xBEEF };
            written.Save(chunkPath, manifest.SeedInput);

            Chunk read = Chunk.Load(chunkPath);
            Assert.Equal(entry.ChunkX, read.ChunkX);
            Assert.Equal(entry.ChunkY, read.ChunkY);
            Assert.Equal(0xBEEF, read.BiomePrimary);
        }
        finally
        {
            if (Directory.Exists(campaignDirectory))
            {
                Directory.Delete(campaignDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Stored paths use forward slashes on every platform. A save folder copied
    /// from Windows to a Steam Deck must still resolve; a backslash is a legal
    /// Linux filename character, so getting this wrong would not even error.
    /// </summary>
    [Fact]
    public void StoredPathsUseForwardSlashes()
    {
        WorldManifest manifest = BuildPopulated();
        foreach (ChunkIndexEntry entry in manifest.ChunkIndex)
        {
            Assert.DoesNotContain('\\', entry.File);
        }
    }

    /// <summary>
    /// A manifest missing a required field is rejected rather than defaulted.
    /// A world with a zeroed spawn would drop players into the corner of the map
    /// with no indication anything went wrong.
    /// </summary>
    [Fact]
    public void MissingRequiredFieldThrows()
    {
        byte[] payload = Encoding.UTF8.GetBytes("{\"world_id\":\"11111111-2222-3333-4444-555555555555\"}");
        Assert.Throws<SaveFormatException>(() => WorldManifest.Deserialize(payload, "world.manifest"));
    }

    /// <summary>
    /// The schema version lives in the envelope header and nowhere else. If it
    /// ever leaked into the payload the two copies could disagree, and a
    /// migration would then trust whichever one it happened to read first.
    /// </summary>
    [Fact]
    public void FormatVersionIsNotWrittenIntoThePayload()
    {
        WithTempFile(path =>
        {
            WorldManifest manifest = BuildPopulated();
            manifest.Save(path, debug: true);

            string json = Encoding.UTF8.GetString(SaveFile.Load(path).Payload.ToArray());
            Assert.DoesNotContain("format_version", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"999\"", json, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Deleting one field from an otherwise valid payload is rejected, and the
    /// framework's <see cref="JsonException"/> is wrapped rather than escaping.
    /// Callers catch <see cref="SaveFormatException"/>; a raw JsonException
    /// would sail past the load screen's error handling as an unhandled crash.
    /// </summary>
    [Fact]
    public void DeletingAFieldFromAValidPayloadThrowsSaveFormatException()
    {
        string json = Encoding.UTF8.GetString(BuildPopulated().Serialize());
        string broken = json.Replace(
            "\"player_spawn\":{\"x\":2100,\"y\":231},", string.Empty, StringComparison.Ordinal);

        // Guard the guard: if the shape changed, the replace silently did nothing.
        Assert.NotEqual(json, broken);

        SaveFormatException error = Assert.Throws<SaveFormatException>(
            () => WorldManifest.Deserialize(Encoding.UTF8.GetBytes(broken), "world.manifest"));
        Assert.IsType<JsonException>(error.InnerException);
    }

    /// <summary>
    /// Truncated JSON is a format error, not an unhandled parse crash. Half a
    /// manifest on disk is what a power cut mid-write looks like.
    /// </summary>
    [Fact]
    public void TruncatedPayloadThrowsSaveFormatException()
    {
        byte[] json = BuildPopulated().Serialize();
        Assert.Throws<SaveFormatException>(
            () => WorldManifest.Deserialize(json.AsSpan(0, json.Length / 2), "world.manifest"));
    }

    /// <summary>
    /// The envelope's keystream input is the world seed reinterpreted unsigned,
    /// and it is what the chunk files of the same world are written with. If the
    /// two ever diverged, a world's manifest and its chunks would need different
    /// keys and one of them would fail to deobfuscate.
    /// </summary>
    [Fact]
    public void SeedInputIsTheUnsignedSeedAndReachesTheHeader()
    {
        WithTempFile(path =>
        {
            WorldManifest manifest = BuildPopulated();
            Assert.Equal(0xAAAA_AAAA_AAAA_AAAAUL, manifest.SeedInput);

            SaveEnvelope envelope = manifest.Save(path);
            Assert.Equal(manifest.SeedInput, envelope.SeedInput);
            Assert.Equal(manifest.SeedInput, SaveFile.Load(path).Envelope.SeedInput);
        });
    }

    /// <summary>
    /// A nested object missing one of its members is refused, not filled in with
    /// defaults. The nested types are positional records, so without an explicit
    /// required marker a half-written <c>main_boss_lair</c> would deserialise to
    /// row 0 with a null prefab id — a lair the game would happily fly the player
    /// to, in the corner of the world, backed by no prefab.
    /// </summary>
    [Fact]
    public void NestedObjectMissingAMemberThrowsRatherThanDefaulting()
    {
        string json = Encoding.UTF8.GetString(BuildPopulated().Serialize());
        int lair = json.IndexOf("\"main_boss_lair\":{", StringComparison.Ordinal);
        Assert.NotEqual(-1, lair);

        int open = json.IndexOf('{', lair);
        int close = json.IndexOf('}', open);
        string broken = json[..(open + 1)] + "\"x\":1" + json[close..];

        SaveFormatException error = Assert.Throws<SaveFormatException>(
            () => WorldManifest.Deserialize(Encoding.UTF8.GetBytes(broken), "world.manifest"));
        Assert.IsType<JsonException>(error.InnerException);
    }

    /// <summary>
    /// A chunk index entry missing its <c>file</c> is refused at load. Letting it
    /// through puts a null path in the index that only fails later, at
    /// ResolvePath, as a NullReferenceException from inside streaming — far from
    /// the corrupt file that caused it.
    /// </summary>
    [Fact]
    public void ChunkIndexEntryMissingItsPathThrows()
    {
        string json = Encoding.UTF8.GetString(BuildPopulated().Serialize());
        int entry = json.IndexOf("\"chunk_index\":[{", StringComparison.Ordinal);
        Assert.NotEqual(-1, entry);

        int open = json.IndexOf('{', entry);
        int close = json.IndexOf('}', open);
        string broken = json[..(open + 1)] + "\"chunk_x\":0,\"chunk_y\":0,\"modified\":false" + json[close..];

        Assert.Throws<SaveFormatException>(
            () => WorldManifest.Deserialize(Encoding.UTF8.GetBytes(broken), "world.manifest"));
    }

    /// <summary>
    /// An explicit null still loads. This is the other half of the required
    /// rule: absent must fail, but null is a real value — a side anchor with a
    /// null <c>picked_up_by</c> is one still lying in the world, and refusing it
    /// would make every unclaimed anchor unloadable.
    /// </summary>
    [Fact]
    public void ExplicitNullInANestedObjectStillLoads()
    {
        WorldManifest manifest = BuildPopulated();
        byte[] json = manifest.Serialize();

        Assert.Contains("\"picked_up_by\":null", Encoding.UTF8.GetString(json), StringComparison.Ordinal);
        WorldManifest loaded = WorldManifest.Deserialize(json, "world.manifest");
        Assert.Null(loaded.SideAnchors[0].PickedUpBy);
    }
}
