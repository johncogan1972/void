using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// World-level state: one <c>world.manifest</c> file per world, home or portal
/// (VOID-021, world-data-model-spec §4, save-format-spec §3).
///
/// The manifest is the source of truth for everything that spans chunks —
/// generation parameters, spawn, the main boss, portal candidates, side
/// anchors, the active hearth — while chunks hold tile-level state only. If a
/// fact matters to more than one chunk, it belongs here.
///
/// <b>Serialisation</b>: the payload is plain JSON (<see cref="ManifestJson"/>)
/// handed to <see cref="SaveFile"/>, which owns zstd, obfuscation, the integrity
/// hash and the atomic write. Property order is pinned with
/// <see cref="JsonPropertyOrderAttribute"/> to the spec's field order so two
/// saves diff readably; declaration order is not a serialisation contract.
///
/// Nothing here touches the Godot API, so the whole save/load path is testable
/// under plain <c>dotnet test</c>.
/// </summary>
public sealed class WorldManifest
{
    /// <summary>
    /// Schema version carried in the envelope header.
    /// </summary>
    /// <remarks>
    /// Non-zero from day one, mirroring <see cref="Chunk.CurrentFormatVersion"/>:
    /// 0 means "nobody set this". <see cref="Load"/> rejects any other value
    /// outright rather than parsing optimistically, because a manifest that
    /// half-parses yields a world with plausible-looking but wrong boundaries.
    /// Bumping this is a save migration, not an edit.
    /// </remarks>
    public const ushort CurrentFormatVersion = 1;

    /// <summary>
    /// Version this instance claims. Defaults to
    /// <see cref="CurrentFormatVersion"/> and is written to the envelope
    /// verbatim; settable only so migration code and format tests can build
    /// other versions.
    /// </summary>
    /// <remarks>
    /// Not serialised into the JSON payload: the envelope header is the single
    /// place the version lives, and two copies could disagree. It is also a
    /// keystream input, so the value passed to <see cref="SaveFile.Save"/> must
    /// be the value <see cref="Load"/> will read back.
    /// </remarks>
    [JsonIgnore]
    public ushort FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>
    /// Unique per world instance, and the key <see cref="CampaignManifest"/> and
    /// <see cref="WorldPosition"/> both point at. Also names the world's save
    /// directory (save-format-spec §3), so it is fixed at creation.
    /// </summary>
    [JsonPropertyOrder(0)]
    public required Guid WorldId { get; set; }

    /// <summary>
    /// Which world template generated this: <c>"home"</c>,
    /// <c>"portal_scorched"</c>, and so on. A string rather than an enum because
    /// world types are data-driven content, and an unknown value from a newer
    /// content build must survive a round-trip rather than fail to parse.
    /// </summary>
    [JsonPropertyOrder(1)]
    public required string WorldType { get; set; }

    /// <summary>
    /// The generation seed. Every world-gen random stream derives from it
    /// (CLAUDE.md determinism), so it is the one value that must never change
    /// after generation — the world would no longer reproduce.
    /// </summary>
    [JsonPropertyOrder(2)]
    public required long Seed { get; set; }

    /// <summary>
    /// Semver of the generator that produced this world, recorded so a future
    /// build can tell whether regenerating from <see cref="Seed"/> would
    /// reproduce the same world or a different one.
    /// </summary>
    [JsonPropertyOrder(3)]
    public required string GenVersion { get; set; }

    /// <summary>Size preset used at generation: <c>"small"</c>, <c>"medium"</c>, <c>"large"</c>.</summary>
    [JsonPropertyOrder(4)]
    public required string SizePreset { get; set; }

    /// <summary>Tile and chunk extents. Streaming bounds-checks against these.</summary>
    [JsonPropertyOrder(5)]
    public required WorldDimensions Dimensions { get; set; }

    /// <summary>Row indices dividing outside / underground / deep / void.</summary>
    [JsonPropertyOrder(6)]
    public required LayerBoundaries LayerBoundaries { get; set; }

    /// <summary>Where players enter this world. Generation output; not the respawn point.</summary>
    [JsonPropertyOrder(7)]
    public required TilePosition PlayerSpawn { get; set; }

    /// <summary>Location and prefab of the main boss arena.</summary>
    [JsonPropertyOrder(8)]
    public required BossLair MainBossLair { get; set; }

    /// <summary>
    /// Every site generation phase 4 chose for a side anchor.
    /// </summary>
    /// <remarks>
    /// Order is generation output and is preserved verbatim; keep it sorted at
    /// the point of generation so the same seed produces the same manifest
    /// bytes. An empty list is legal (a world with no candidates) and must not
    /// be confused with an absent field.
    /// </remarks>
    [JsonPropertyOrder(9)]
    public IList<PortalCandidate> PortalCandidates { get; set; } = new List<PortalCandidate>();

    /// <summary>Runtime: true once this world's main boss has been killed.</summary>
    [JsonPropertyOrder(10)]
    public bool MainBossKilled { get; set; }

    /// <summary>
    /// Runtime: anchors generated in this world, whatever world they now sit in.
    /// Keyed back to <see cref="PortalCandidates"/> by
    /// <see cref="SideAnchor.CandidateId"/>.
    /// </summary>
    [JsonPropertyOrder(11)]
    public IList<SideAnchor> SideAnchors { get; set; } = new List<SideAnchor>();

    /// <summary>
    /// Runtime: the party's current spawn point (GDD §4.7), or null when no
    /// hearth is active and players spawn at <see cref="PlayerSpawn"/>. It
    /// carries a world id because a hearth can be placed in another world.
    /// </summary>
    [JsonPropertyOrder(12)]
    public WorldPosition? ActiveHearth { get; set; }

    /// <summary>
    /// Every chunk file belonging to this world, with its modified flag.
    /// </summary>
    /// <remarks>
    /// Paths are relative to the campaign directory. Add entries with
    /// <see cref="AddChunkIndexEntry"/> so the naming rule stays in one place.
    /// </remarks>
    [JsonPropertyOrder(13)]
    public IList<ChunkIndexEntry> ChunkIndex { get; set; } = new List<ChunkIndexEntry>();

    /// <summary>
    /// Envelope keystream input for this world's files (save-format-spec §7).
    /// The world seed, reinterpreted unsigned — the same value chunk saves use,
    /// so one world's files share one keystream input.
    /// </summary>
    [JsonIgnore]
    public ulong SeedInput => unchecked((ulong)Seed);

    /// <summary>Relative path this manifest's chunk at (x, y) would occupy.</summary>
    public string ChunkPath(int chunkX, int chunkY) =>
        ChunkIndexEntry.ChunkPath(WorldId, chunkX, chunkY);

    /// <summary>
    /// Appends an index entry for a chunk of this world and returns it.
    /// </summary>
    /// <remarks>
    /// Does not check for duplicates: generation writes each chunk once, and a
    /// silent de-duplication here would hide a pass that ran twice.
    /// </remarks>
    public ChunkIndexEntry AddChunkIndexEntry(int chunkX, int chunkY, bool modified = false)
    {
        ChunkIndexEntry entry = ChunkIndexEntry.Create(WorldId, chunkX, chunkY, modified);
        ChunkIndex.Add(entry);
        return entry;
    }

    /// <summary>Serialises the manifest to its JSON payload bytes.</summary>
    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, ManifestJson.Options);

    /// <summary>
    /// Parses a manifest payload.
    /// </summary>
    /// <param name="payload">Exactly the bytes produced by <see cref="Serialize"/>.</param>
    /// <param name="fileName">Name used in exception messages.</param>
    /// <exception cref="SaveFormatException">
    /// If the JSON is malformed, missing a required field, or empty. All are
    /// fatal: a partially populated manifest would send players to spawn
    /// coordinates that were never written.
    /// </exception>
    public static WorldManifest Deserialize(ReadOnlySpan<byte> payload, string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        try
        {
            return JsonSerializer.Deserialize<WorldManifest>(payload, ManifestJson.Options)
                ?? throw new SaveFormatException(fileName, "World manifest payload is JSON null.");
        }
        catch (JsonException e)
        {
            throw new SaveFormatException(fileName, $"World manifest JSON is invalid: {e.Message}", e);
        }
    }

    /// <summary>
    /// Writes the manifest through the save envelope — compression,
    /// obfuscation, integrity hash and atomic write all belong to
    /// <see cref="SaveFile"/>. Returns the header written.
    /// </summary>
    /// <remarks>
    /// The envelope gets this instance's <see cref="FormatVersion"/>, not
    /// <see cref="CurrentFormatVersion"/>: format_version is a keystream input,
    /// so an envelope disagreeing with the payload it wraps would decrypt
    /// against the wrong key at the first migration.
    /// </remarks>
    public SaveEnvelope Save(string path, bool debug = false, uint? fileSalt = null) =>
        SaveFile.Save(path, SaveFileKind.WorldManifest, FormatVersion, SeedInput, Serialize(), debug, fileSalt);

    /// <summary>
    /// Reads a world manifest written by <see cref="Save"/>.
    /// </summary>
    /// <exception cref="SaveFormatException">
    /// If the envelope carries another payload kind, declares a format version
    /// this build has no parser for, or the JSON does not parse.
    /// </exception>
    /// <exception cref="SaveIntegrityException">If the payload hash does not match.</exception>
    public static WorldManifest Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        SaveLoadResult result = SaveFile.Load(path);
        if (result.Envelope.FileKind != SaveFileKind.WorldManifest)
        {
            throw new SaveFormatException(
                path, $"Expected a world manifest file, header says {result.Envelope.FileKind}.");
        }

        if (result.Envelope.FormatVersion != CurrentFormatVersion)
        {
            throw new SaveFormatException(
                path,
                $"World manifest format_version {result.Envelope.FormatVersion} is not supported by "
                + $"this build (expected {CurrentFormatVersion}).");
        }

        WorldManifest manifest = Deserialize(result.Payload, path);
        manifest.FormatVersion = result.Envelope.FormatVersion;
        return manifest;
    }
}
