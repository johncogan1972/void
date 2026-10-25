using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Void;

/// <summary>
/// The player's graphics preferences (VOID-062) — the in-memory form of
/// <c>settings/graphics.json</c>.
///
/// Engine-free on purpose: the file is read and normalised before any Godot
/// window exists, and the whole validation path has to be reachable from the
/// xunit suite. <see cref="DisplayBoot"/> is the only thing that turns these
/// values into engine calls.
///
/// Mutable-by-copy (a record) rather than settable in place, so a caller cannot
/// hand an unvalidated instance to the boot path: every construction route of
/// interest goes through <see cref="Normalised"/>.
/// </summary>
public sealed record GraphicsSettings
{
    /// <summary>How the window is presented. Windowed is the default because it cannot fail to display.</summary>
    public WindowMode WindowMode { get; init; } = WindowMode.Windowed;

    /// <summary>
    /// Requested window width in screen pixels. Only meaningful for
    /// <see cref="WindowMode.Windowed"/> — the other two modes take the
    /// display's own size — but it is still persisted so that leaving
    /// fullscreen restores the player's window.
    /// </summary>
    public int ResolutionWidth { get; init; } = DisplayConfig.BaseViewportWidth;

    /// <summary>Requested window height; see <see cref="ResolutionWidth"/>.</summary>
    public int ResolutionHeight { get; init; } = DisplayConfig.BaseViewportHeight;

    /// <summary>
    /// Camera magnification, 1-3 (GDD §9.4). Whole numbers only, and never
    /// below 1: see <see cref="DisplayConfig.MinZoomLevel"/> for why sub-1x is
    /// refused rather than clamped silently at load.
    /// </summary>
    public int ZoomLevel { get; init; } = DisplayConfig.DefaultZoomLevel;

    /// <summary>
    /// What a fresh install uses, and the fallback whenever the file on disk is
    /// unreadable. Deliberately the Steam Deck baseline: it is the one
    /// configuration guaranteed to be displayable on every target.
    /// </summary>
    public static GraphicsSettings Default { get; } = new GraphicsSettings();

    /// <summary>
    /// Returns an instance with every field pulled into its legal range, plus
    /// one human-readable warning per field that had to move.
    /// </summary>
    /// <remarks>
    /// Never throws, by design. The file is hand-editable (save-format-spec §3),
    /// so a player's typo must cost them that one setting and nothing else — a
    /// throw here would brick boot on a stray character. Callers log the
    /// warnings; the game continues either way.
    /// </remarks>
    public GraphicsSettingsValidation Normalised()
    {
        List<string> warnings = new();
        GraphicsSettings result = this;

        if (!Enum.IsDefined(this.WindowMode))
        {
            warnings.Add($"window_mode {(int)this.WindowMode} is not a known mode; using windowed.");
            result = result with { WindowMode = Void.WindowMode.Windowed };
        }

        if (result.ResolutionWidth < DisplayConfig.BaseViewportWidth)
        {
            warnings.Add($"resolution_width {result.ResolutionWidth} is below the {DisplayConfig.BaseViewportWidth} minimum; raised to it.");
            result = result with { ResolutionWidth = DisplayConfig.BaseViewportWidth };
        }

        if (result.ResolutionHeight < DisplayConfig.BaseViewportHeight)
        {
            warnings.Add($"resolution_height {result.ResolutionHeight} is below the {DisplayConfig.BaseViewportHeight} minimum; raised to it.");
            result = result with { ResolutionHeight = DisplayConfig.BaseViewportHeight };
        }

        int zoom = Math.Clamp(result.ZoomLevel, DisplayConfig.MinZoomLevel, DisplayConfig.MaxZoomLevel);
        if (zoom != result.ZoomLevel)
        {
            warnings.Add($"zoom_level {result.ZoomLevel} is outside the supported {DisplayConfig.MinZoomLevel}-{DisplayConfig.MaxZoomLevel} range; using {zoom}.");
            result = result with { ZoomLevel = zoom };
        }

        return new GraphicsSettingsValidation(result, new ReadOnlyCollection<string>(warnings));
    }
}

/// <summary>
/// A normalised <see cref="GraphicsSettings"/> paired with what had to be
/// corrected to get there (VOID-062).
/// </summary>
/// <param name="Settings">Always safe to apply; never null.</param>
/// <param name="Warnings">
/// Empty when the input was already legal. Each entry names the offending field
/// and the value used instead, so a player who mistyped the file can find out
/// why the game ignored them from the log alone.
/// </param>
public readonly record struct GraphicsSettingsValidation(
    GraphicsSettings Settings,
    IReadOnlyList<string> Warnings);
