using System;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// One world known to a campaign (world-data-model-spec §4).
///
/// The campaign manifest holds one of these per world the party has entered —
/// the home world plus every portal world — and they are how a world id
/// resolves to a file on disk.
/// </summary>
/// <param name="WorldId">Matches <see cref="WorldManifest.WorldId"/>.</param>
/// <param name="WorldType">
/// Copied from the world manifest so the load screen can list worlds without
/// opening and decompressing each one.
/// </param>
/// <param name="ManifestPath">
/// Path to that world's <c>world.manifest</c>, relative to the campaign
/// directory and using <see cref="ChunkIndexEntry.Separator"/> — same portability
/// rule as chunk paths. Build it with <see cref="ManifestPathFor"/>.
/// </param>
/// <param name="DiscoveredAt">
/// When the party first entered this world. Supplied by the caller and never
/// read from the clock inside serialisation, so saves are reproducible in tests.
/// </param>
public sealed record CampaignWorldEntry(
    [property: JsonPropertyOrder(0), JsonRequired] Guid WorldId,
    [property: JsonPropertyOrder(1), JsonRequired] string WorldType,
    [property: JsonPropertyOrder(2), JsonRequired] string ManifestPath,
    [property: JsonPropertyOrder(3), JsonRequired] DateTimeOffset DiscoveredAt)
{
    /// <summary>File name of a world manifest. Fixed by save-format-spec §3.</summary>
    public const string ManifestFileName = "world.manifest";

    /// <summary>Relative path of one world's manifest within a campaign directory.</summary>
    /// <remarks>
    /// Built from the same <c>worlds/&lt;world_uuid&gt;/</c> shape as
    /// <see cref="ChunkIndexEntry.ChunkDirectory"/>, so a world's manifest and
    /// its chunks can never disagree about where the world lives.
    /// </remarks>
    public static string ManifestPathFor(Guid worldId) =>
        $"worlds{ChunkIndexEntry.Separator}{worldId:D}{ChunkIndexEntry.Separator}{ManifestFileName}";

    /// <summary>Builds an entry whose manifest path follows the directory layout.</summary>
    public static CampaignWorldEntry Create(
        Guid worldId, string worldType, DateTimeOffset discoveredAt) =>
        new CampaignWorldEntry(worldId, worldType, ManifestPathFor(worldId), discoveredAt);

    /// <summary>
    /// Turns the stored relative path into a platform path under
    /// <paramref name="campaignDirectory"/>, ready for
    /// <see cref="WorldManifest.Load"/>.
    /// </summary>
    public string ResolvePath(string campaignDirectory)
    {
        ArgumentNullException.ThrowIfNull(campaignDirectory);
        return System.IO.Path.Combine(
            campaignDirectory,
            ManifestPath.Replace(ChunkIndexEntry.Separator, System.IO.Path.DirectorySeparatorChar));
    }
}
