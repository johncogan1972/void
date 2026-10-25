namespace Void;

/// <summary>
/// The three window presentations the game offers (VOID-062).
///
/// Named rather than numeric on disk: <c>settings/graphics.json</c> is plain
/// text and hand-editable by design (save-format-spec §3), so a player reading
/// the file must be able to tell what the value means. Members are therefore
/// also safe to reorder — nothing persists the ordinal.
/// </summary>
public enum WindowMode
{
    /// <summary>Decorated, resizable window. The default, and the safest fallback.</summary>
    Windowed,

    /// <summary>Undecorated window sized to the display; no mode switch, so alt-tab stays instant.</summary>
    Borderless,

    /// <summary>Exclusive fullscreen; may change the display mode.</summary>
    Fullscreen,
}
