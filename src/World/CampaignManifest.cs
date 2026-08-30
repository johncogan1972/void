using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Campaign-level state: the <c>campaign.manifest</c> file that save/load starts
/// from (VOID-021, world-data-model-spec §4, save-format-spec §3).
///
/// It is deliberately thin — a campaign id, when it was created, and the list of
/// worlds discovered so far. Loading walks campaign manifest → world manifest →
/// chunks, so this file is the only path into a save that does not require
/// knowing a UUID in advance.
///
/// Characters are <b>not</b> listed here: they live in <c>saves/characters/</c>
/// and travel between campaigns (save-format-spec §3).
///
/// Serialisation rules are the same as <see cref="WorldManifest"/>: plain JSON
/// through <see cref="ManifestJson"/>, wrapped by <see cref="SaveFile"/>, with
/// property order pinned to the spec's field order.
/// </summary>
public sealed class CampaignManifest
{
    /// <summary>
    /// Schema version carried in the envelope header. Non-zero from day one;
    /// see <see cref="WorldManifest.CurrentFormatVersion"/> for why an unknown
    /// value is a hard failure rather than a best-effort parse.
    /// </summary>
    public const ushort CurrentFormatVersion = 1;

    /// <summary>File name of a campaign manifest. Fixed by save-format-spec §3.</summary>
    public const string ManifestFileName = "campaign.manifest";

    /// <summary>
    /// Version this instance claims. Defaults to
    /// <see cref="CurrentFormatVersion"/>; settable only for migration code and
    /// format tests. Not serialised into the payload — the envelope header owns it.
    /// </summary>
    [JsonIgnore]
    public ushort FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>
    /// Unique per campaign, and the name of the campaign's save directory
    /// (save-format-spec §3), so it is fixed at creation.
    /// </summary>
    [JsonPropertyOrder(0)]
    public required Guid CampaignId { get; set; }

    /// <summary>
    /// When the campaign was created.
    /// </summary>
    /// <remarks>
    /// Supplied by the caller, never read from the clock in a constructor or
    /// during serialisation: a save must be reproducible from its inputs, and a
    /// hidden clock read makes tests flaky and diffs noisy. Stored ISO-8601 UTC.
    /// </remarks>
    [JsonPropertyOrder(1)]
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Every world the party has entered, home world included.
    /// </summary>
    /// <remarks>
    /// Order is discovery order and is preserved verbatim. An empty list is a
    /// legal state — a campaign created but not yet entered — and must survive
    /// the round-trip as an empty list, not as an absent field.
    /// </remarks>
    [JsonPropertyOrder(2)]
    public IList<CampaignWorldEntry> Worlds { get; set; } = new List<CampaignWorldEntry>();

    /// <summary>
    /// Envelope keystream input for the campaign file (save-format-spec §7).
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="CampaignId"/> because a campaign has no seed of
    /// its own, and the keystream input must be reproducible from the file's own
    /// contents — it is stored in the header, so this only has to be stable, not
    /// secret. Obfuscation is a deterrent, not encryption (§7).
    /// </remarks>
    [JsonIgnore]
    public ulong SeedInput => SeedInputFor(CampaignId);

    /// <summary>Keystream input for a campaign id: its first eight bytes, little-endian.</summary>
    public static ulong SeedInputFor(Guid campaignId)
    {
        Span<byte> bytes = stackalloc byte[16];
        campaignId.TryWriteBytes(bytes);
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    /// <summary>Appends a world entry with the standard manifest path and returns it.</summary>
    public CampaignWorldEntry AddWorld(Guid worldId, string worldType, DateTimeOffset discoveredAt)
    {
        CampaignWorldEntry entry = CampaignWorldEntry.Create(worldId, worldType, discoveredAt);
        Worlds.Add(entry);
        return entry;
    }

    /// <summary>Serialises the manifest to its JSON payload bytes.</summary>
    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, ManifestJson.Options);

    /// <summary>
    /// Parses a manifest payload.
    /// </summary>
    /// <exception cref="SaveFormatException">
    /// If the JSON is malformed, missing a required field, or null. Fatal: this
    /// file is the entry point to the save, so a partial parse would present the
    /// player with a campaign missing worlds they have played.
    /// </exception>
    public static CampaignManifest Deserialize(ReadOnlySpan<byte> payload, string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        try
        {
            return JsonSerializer.Deserialize<CampaignManifest>(payload, ManifestJson.Options)
                ?? throw new SaveFormatException(fileName, "Campaign manifest payload is JSON null.");
        }
        catch (JsonException e)
        {
            throw new SaveFormatException(fileName, $"Campaign manifest JSON is invalid: {e.Message}", e);
        }
    }

    /// <summary>
    /// Writes the manifest through the save envelope; see
    /// <see cref="WorldManifest.Save"/> for why the instance's
    /// <see cref="FormatVersion"/> is used rather than the constant.
    /// </summary>
    public SaveEnvelope Save(string path, bool debug = false, uint? fileSalt = null) =>
        SaveFile.Save(path, SaveFileKind.CampaignManifest, FormatVersion, SeedInput, Serialize(), debug, fileSalt);

    /// <summary>
    /// Reads a campaign manifest written by <see cref="Save"/>.
    /// </summary>
    /// <exception cref="SaveFormatException">
    /// If the envelope carries another payload kind, declares an unsupported
    /// format version, or the JSON does not parse.
    /// </exception>
    /// <exception cref="SaveIntegrityException">If the payload hash does not match.</exception>
    public static CampaignManifest Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        SaveLoadResult result = SaveFile.Load(path);
        if (result.Envelope.FileKind != SaveFileKind.CampaignManifest)
        {
            throw new SaveFormatException(
                path, $"Expected a campaign manifest file, header says {result.Envelope.FileKind}.");
        }

        if (result.Envelope.FormatVersion != CurrentFormatVersion)
        {
            throw new SaveFormatException(
                path,
                $"Campaign manifest format_version {result.Envelope.FormatVersion} is not supported "
                + $"by this build (expected {CurrentFormatVersion}).");
        }

        CampaignManifest manifest = Deserialize(result.Payload, path);
        manifest.FormatVersion = result.Envelope.FormatVersion;
        return manifest;
    }
}
