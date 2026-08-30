using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// VOID-021 acceptance tests: the campaign manifest schema and its file format
/// (world-data-model-spec §4, save-format-spec §3).
///
/// This file is the entry point to a save — it resolves world ids to world
/// manifests to chunks. A red test here means a campaign either fails to open
/// or opens missing worlds the party has already played, which reads to the
/// player as lost progress rather than as a bug.
/// </summary>
public sealed class CampaignManifestTests
{
    /// <summary>Ids and timestamps are fixed in source; tests never read the clock.</summary>
    private static readonly Guid TestCampaignId = new Guid("0f0f0f0f-1e1e-2d2d-3c3c-4b4b4b4b4b4b");
    private static readonly Guid HomeWorldId = new Guid("11111111-2222-3333-4444-555555555555");
    private static readonly Guid PortalWorldId = new Guid("99999999-8888-7777-6666-555555555555");
    private static readonly DateTimeOffset CreatedAt =
        new DateTimeOffset(2026, 8, 30, 11, 22, 33, 444, TimeSpan.Zero);

    /// <summary>Builds a campaign with two worlds discovered at distinct, fixed times.</summary>
    private static CampaignManifest BuildPopulated()
    {
        CampaignManifest manifest = new CampaignManifest
        {
            CampaignId = TestCampaignId,
            CreatedAt = CreatedAt,
        };

        manifest.AddWorld(HomeWorldId, "home", CreatedAt);
        manifest.AddWorld(PortalWorldId, "portal_scorched", CreatedAt.AddHours(3));
        return manifest;
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
    /// A campaign written to disk comes back identical, world list included.
    /// If this fails, reopening a save loses or corrupts the list of worlds.
    /// </summary>
    [Fact]
    public void RoundTripsThroughSaveFile()
    {
        WithTempFile(path =>
        {
            CampaignManifest original = BuildPopulated();
            SaveEnvelope envelope = original.Save(path);

            Assert.Equal(SaveFileKind.CampaignManifest, envelope.FileKind);
            Assert.Equal(CampaignManifest.CurrentFormatVersion, envelope.FormatVersion);

            CampaignManifest loaded = CampaignManifest.Load(path);
            Assert.Equal(original.CampaignId, loaded.CampaignId);
            Assert.Equal(original.CreatedAt, loaded.CreatedAt);
            Assert.Equal(original.Worlds, loaded.Worlds);

            // Spot-check the nested entry rather than trusting record equality alone.
            CampaignWorldEntry portal = loaded.Worlds[1];
            Assert.Equal(PortalWorldId, portal.WorldId);
            Assert.Equal("portal_scorched", portal.WorldType);
            Assert.Equal($"worlds/{PortalWorldId:D}/world.manifest", portal.ManifestPath);
            Assert.Equal(CreatedAt.AddHours(3), portal.DiscoveredAt);
        });
    }

    /// <summary>
    /// A campaign created but not yet entered has an empty world list, and that
    /// must come back empty rather than null — a null list would throw on the
    /// first load screen instead of showing "no worlds yet".
    /// </summary>
    [Fact]
    public void EmptyWorldListSurvivesRoundTrip()
    {
        WithTempFile(path =>
        {
            CampaignManifest original = new CampaignManifest
            {
                CampaignId = TestCampaignId,
                CreatedAt = CreatedAt,
            };

            original.Save(path);
            CampaignManifest loaded = CampaignManifest.Load(path);

            Assert.NotNull(loaded.Worlds);
            Assert.Empty(loaded.Worlds);
            Assert.Contains(
                "\"worlds\":[]",
                Encoding.UTF8.GetString(original.Serialize()),
                StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Timestamps are stored as ISO-8601 UTC regardless of the caller's offset,
    /// so the same instant saved in two time zones produces the same bytes and
    /// a save diff stays readable.
    /// </summary>
    [Fact]
    public void TimestampsAreStoredIsoUtc()
    {
        CampaignManifest manifest = new CampaignManifest
        {
            CampaignId = TestCampaignId,
            CreatedAt = new DateTimeOffset(2026, 8, 30, 13, 22, 33, 444, TimeSpan.FromHours(2)),
        };

        string json = Encoding.UTF8.GetString(manifest.Serialize());
        Assert.Contains("\"created_at\":\"2026-08-30T11:22:33.4440000Z\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// A campaign file from a future build fails loudly and names the version.
    /// Opening it optimistically would show the player a campaign whose contents
    /// this build cannot represent.
    /// </summary>
    [Fact]
    public void UnknownFormatVersionThrowsNamingTheVersion()
    {
        WithTempFile(path =>
        {
            CampaignManifest manifest = BuildPopulated();
            manifest.FormatVersion = 7;
            manifest.Save(path);

            SaveFormatException error =
                Assert.Throws<SaveFormatException>(() => CampaignManifest.Load(path));
            Assert.Contains("7", error.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A world manifest handed to the campaign loader is rejected on its header.
    /// Both are JSON, so without the kind check it would very nearly parse.
    /// </summary>
    [Fact]
    public void WrongFileKindThrows()
    {
        WithTempFile(path =>
        {
            new WorldManifest
            {
                WorldId = HomeWorldId,
                WorldType = "home",
                Seed = 1,
                GenVersion = "0.1.0",
                SizePreset = "small",
                Dimensions = new WorldDimensions(1, 1, 1, 1),
                LayerBoundaries = new LayerBoundaries(1, 2, 3),
                PlayerSpawn = new TilePosition(0, 0),
                MainBossLair = new BossLair(0, 0, "none"),
            }.Save(path);

            SaveFormatException error =
                Assert.Throws<SaveFormatException>(() => CampaignManifest.Load(path));
            Assert.Contains("WorldManifest", error.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Field order matches the spec, at both levels. A stable order is what
    /// makes two saves diffable when a campaign goes wrong.
    /// </summary>
    [Fact]
    public void JsonPropertyOrderMatchesSpec()
    {
        WithTempFile(path =>
        {
            BuildPopulated().Save(path, debug: true);

            // Debug mode (§14) stores the payload verbatim, so this is the JSON.
            string json = Encoding.UTF8.GetString(SaveFile.Load(path).Payload.ToArray());

            int campaign = json.IndexOf("\"campaign_id\"", StringComparison.Ordinal);
            int created = json.IndexOf("\"created_at\"", StringComparison.Ordinal);
            int worlds = json.IndexOf("\"worlds\"", StringComparison.Ordinal);
            Assert.True(campaign >= 0 && campaign < created && created < worlds, json);

            Assert.Contains(
                $"{{\"world_id\":\"{HomeWorldId:D}\",\"world_type\":\"home\","
                + $"\"manifest_path\":\"worlds/{HomeWorldId:D}/world.manifest\","
                + "\"discovered_at\":\"2026-08-30T11:22:33.4440000Z\"}",
                json,
                StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A campaign entry resolves to the world manifest actually on disk. This is
    /// the first hop of the load path: campaign → world → chunks. If the path
    /// shape drifts from save-format-spec §3, a save opens to nothing.
    /// </summary>
    [Fact]
    public void WorldEntryPathRoundTripsIntoWorldManifestLoad()
    {
        string campaignDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            CampaignManifest campaign = BuildPopulated();
            CampaignWorldEntry entry = campaign.Worlds[0];

            WorldManifest world = new WorldManifest
            {
                WorldId = HomeWorldId,
                WorldType = "home",
                Seed = 42,
                GenVersion = "0.1.0",
                SizePreset = "small",
                Dimensions = new WorldDimensions(640, 640, 10, 10),
                LayerBoundaries = new LayerBoundaries(64, 256, 512),
                PlayerSpawn = new TilePosition(320, 60),
                MainBossLair = new BossLair(100, 300, "lair_home_01"),
            };

            string worldPath = entry.ResolvePath(campaignDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(worldPath)!);
            world.Save(worldPath);

            WorldManifest loaded = WorldManifest.Load(worldPath);
            Assert.Equal(entry.WorldId, loaded.WorldId);
            Assert.Equal(42, loaded.Seed);
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
    /// A player id survives a string round-trip and compares by value. Anchor
    /// ownership is keyed on it, so a broken equality would let one player pick
    /// up another's anchor.
    /// </summary>
    [Fact]
    public void PlayerIdRoundTripsAndComparesByValue()
    {
        PlayerId id = PlayerId.New();
        Assert.Equal(id, PlayerId.Parse(id.ToString()));
        Assert.NotEqual(id, PlayerId.New());
        Assert.True(PlayerId.None.IsNone);
        Assert.False(id.IsNone);
    }

    /// <summary>
    /// The keystream input is derived purely from the campaign id, so the same
    /// id always yields the same value and a save written on one machine is
    /// readable on another. The golden number is pinned: changing the derivation
    /// makes every existing campaign file undecodable.
    /// </summary>
    [Fact]
    public void SeedInputIsDeterministicForACampaignId()
    {
        ulong expected = 3_255_291_220_109_233_935UL;
        Assert.Equal(expected, CampaignManifest.SeedInputFor(TestCampaignId));
        Assert.Equal(
            CampaignManifest.SeedInputFor(TestCampaignId),
            CampaignManifest.SeedInputFor(TestCampaignId));
        Assert.Equal(expected, BuildPopulated().SeedInput);

        // Different ids must not collide, or two campaigns share a keystream.
        Assert.NotEqual(
            CampaignManifest.SeedInputFor(HomeWorldId),
            CampaignManifest.SeedInputFor(PortalWorldId));
    }

    /// <summary>
    /// The value the file was written with is the value it is read back with.
    /// A mismatch would not corrupt the header — it is stored there — but it
    /// would mean the campaign id no longer describes its own file, so a
    /// recovery tool rebuilding a header from the id would produce garbage.
    /// </summary>
    [Fact]
    public void SavedSeedInputMatchesTheIdItWasDerivedFrom()
    {
        WithTempFile(path =>
        {
            CampaignManifest manifest = BuildPopulated();
            SaveEnvelope envelope = manifest.Save(path);

            Assert.Equal(CampaignManifest.SeedInputFor(TestCampaignId), envelope.SeedInput);
            Assert.Equal(
                CampaignManifest.SeedInputFor(TestCampaignId),
                SaveFile.Load(path).Envelope.SeedInput);
            Assert.Equal(TestCampaignId, CampaignManifest.Load(path).CampaignId);
        });
    }

    /// <summary>
    /// The schema version stays in the envelope header and never appears in the
    /// payload, so there is only one copy of it to migrate.
    /// </summary>
    [Fact]
    public void FormatVersionIsNotWrittenIntoThePayload()
    {
        string json = Encoding.UTF8.GetString(BuildPopulated().Serialize());
        Assert.DoesNotContain("format_version", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deleting a field from a valid payload is rejected as a save format
    /// error, with the framework exception wrapped rather than escaping to the
    /// caller — load screens catch <see cref="SaveFormatException"/> only.
    /// </summary>
    [Fact]
    public void DeletingAFieldFromAValidPayloadThrowsSaveFormatException()
    {
        string json = Encoding.UTF8.GetString(BuildPopulated().Serialize());
        string broken = json.Replace(
            "\"created_at\":\"2026-08-30T11:22:33.4440000Z\",", string.Empty, StringComparison.Ordinal);

        // Guard the guard: a shape change must not turn this into a no-op edit.
        Assert.NotEqual(json, broken);

        SaveFormatException error = Assert.Throws<SaveFormatException>(
            () => CampaignManifest.Deserialize(Encoding.UTF8.GetBytes(broken), CampaignManifest.ManifestFileName));
        Assert.IsType<JsonException>(error.InnerException);
    }

    /// <summary>
    /// A timestamp that is not ISO-8601 is corrupt, not merely odd. Defaulting
    /// it would silently reorder the campaign list by creation date.
    /// </summary>
    [Fact]
    public void MalformedTimestampThrowsSaveFormatException()
    {
        string json = Encoding.UTF8.GetString(BuildPopulated().Serialize());
        string broken = json.Replace(
            "\"created_at\":\"2026-08-30T11:22:33.4440000Z\"",
            "\"created_at\":\"last tuesday\"",
            StringComparison.Ordinal);
        Assert.NotEqual(json, broken);

        Assert.Throws<SaveFormatException>(
            () => CampaignManifest.Deserialize(Encoding.UTF8.GetBytes(broken), CampaignManifest.ManifestFileName));
    }
}
