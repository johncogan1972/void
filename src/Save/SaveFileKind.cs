namespace Void;

/// <summary>
/// Which kind of payload a save file carries (save-format-spec §4).
/// Values are wire values: they are written to the envelope header as a
/// single byte and must never be renumbered.
/// </summary>
public enum SaveFileKind : byte
{
    /// <summary>A single character file (<c>&lt;uuid&gt;.character</c>).</summary>
    Character = 1,

    /// <summary>Campaign-level state (<c>campaign.manifest</c>).</summary>
    CampaignManifest = 2,

    /// <summary>World-level state (<c>world.manifest</c>).</summary>
    WorldManifest = 3,

    /// <summary>One streamed chunk (<c>&lt;x&gt;_&lt;y&gt;.chunk</c>).</summary>
    Chunk = 4,

    /// <summary>One persistent entity (<c>&lt;uuid&gt;.entity</c>).</summary>
    Entity = 5,
}
