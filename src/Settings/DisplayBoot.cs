using System;
using Godot;

namespace Void;

/// <summary>
/// Autoload that loads <c>settings/graphics.json</c> and applies it to the
/// window at startup, then keeps the aspect cap enforced as the window is
/// resized (VOID-062).
///
/// <para>Deliberately thin, and the only file in the display layer allowed to
/// touch Godot. Every decision — legal zoom range, integer scaling, the 2.0
/// aspect cap, what to do with a malformed file — lives in
/// <see cref="DisplayConfig"/> and <see cref="GraphicsSettingsStore"/>, which
/// are engine-free so the xunit suite can reach them. This node only translates
/// their output into engine calls. The xunit suite must never load this type:
/// it needs an initialised engine.</para>
///
/// <para>Registered in <c>project.godot</c> as the <c>DisplayBoot</c> autoload.
/// Unlike <see cref="ContentBoot"/> a failure here is not fatal: a window that
/// came up at the wrong size is still playable and still lets the player reach
/// the settings file, whereas quitting over it would be unfixable from inside
/// the game.</para>
///
/// <para>Scene change required: none. This is autoload-only; a settings screen
/// that writes the file back is a later ticket, and would call
/// <see cref="Apply"/> after <see cref="GraphicsSettingsStore.Save"/>.</para>
/// </summary>
public partial class DisplayBoot : Node
{
    /// <summary>
    /// Godot path of the settings file. <c>user://</c> resolves per-platform, so
    /// this is the one place that knows where settings actually live; the
    /// engine-free layer takes a plain filesystem path and therefore stays
    /// testable against a temp directory.
    /// </summary>
    public const string SettingsGodotPath = "user://" + GraphicsSettingsStore.RelativePath;

    /// <summary>The settings in force. Normalised, so it is always safe to apply.</summary>
    public GraphicsSettings Settings { get; private set; } = GraphicsSettings.Default;

    /// <summary>
    /// Absolute filesystem path behind <see cref="SettingsGodotPath"/>, for
    /// handing to the engine-free store.
    /// </summary>
    public static string SettingsFilePath => ProjectSettings.GlobalizePath(SettingsGodotPath);

    /// <summary>
    /// Loads settings and applies them before the main scene's nodes
    /// initialise, so nothing reads a window size that is about to change.
    /// </summary>
    /// <remarks>
    /// In <c>_EnterTree</c> for the same reason as <see cref="ContentBoot"/>:
    /// autoload <c>_Ready</c> happens to run first today, but the rest of the
    /// game should not have to rely on that.
    /// </remarks>
    public override void _EnterTree()
    {
        GraphicsSettingsValidation loaded;
        try
        {
            loaded = GraphicsSettingsStore.Load(SettingsFilePath);
        }
        catch (Exception ex)
        {
            // The store is written not to throw; if it ever does, defaults are
            // still a playable window, so this must not take boot down with it.
            GD.PushWarning($"Graphics settings could not be loaded ({ex.Message}); using defaults.");
            loaded = GraphicsSettings.Default.Normalised();
        }

        foreach (string warning in loaded.Warnings)
        {
            GD.PushWarning($"Graphics settings: {warning}");
        }

        Settings = loaded.Settings;
    }

    /// <summary>
    /// Applies the loaded settings once the window exists and subscribes to
    /// later resizes.
    /// </summary>
    /// <remarks>
    /// The window is only reliably reachable from <c>_Ready</c>, hence the
    /// split from <see cref="_EnterTree"/>. Resizes are handled by signal
    /// rather than polled in <c>_Process</c>: the cap only has to be recomputed
    /// when the window size actually changes, and a per-frame recompute would
    /// fight the user's drag.
    /// </remarks>
    public override void _Ready()
    {
        Window window = GetWindow();
        ApplyWindowMode(window);
        window.SizeChanged += OnWindowSizeChanged;
        Apply();
    }

    /// <summary>
    /// Recomputes and applies the content scale for the window's current size.
    /// Call after writing new settings; idempotent, so calling it spuriously
    /// costs nothing.
    /// </summary>
    public void Apply()
    {
        Window window = GetWindow();
        Vector2I size = window.Size;
        DisplayPlan plan = DisplayConfig.Plan(size.X, size.Y);

        // canvas_items + integer scaling is what makes rendering pixel-perfect
        // (GDD §9.4); it pairs with nearest-neighbour texture filtering, which
        // project.godot sets globally via
        // rendering/textures/canvas_textures/default_texture_filter=0. Both are
        // required — integer scaling alone still filters within a sprite.
        window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        window.ContentScaleStretch = Window.ContentScaleStretchEnum.Integer;

        // Expand normally: leftover window space becomes revealed world. Once
        // the window is wider than DisplayConfig.MaxAspectRatio, switch to Keep
        // against the capped content size instead, which pillarboxes the excess
        // rather than revealing more world to an ultrawide player.
        window.ContentScaleAspect = plan.AspectCapped
            ? Window.ContentScaleAspectEnum.Keep
            : Window.ContentScaleAspectEnum.Expand;
        window.ContentScaleSize = new Vector2I(plan.ContentScaleWidth, plan.ContentScaleHeight);
    }

    /// <summary>Keeps the cap honest while the player drags the window edge.</summary>
    private void OnWindowSizeChanged() => Apply();

    /// <summary>
    /// Puts the window into the configured mode, and in windowed mode also
    /// restores the saved size. Fullscreen and borderless take the display's
    /// own size, so the saved resolution is left alone for when the player
    /// comes back to windowed.
    /// </summary>
    private void ApplyWindowMode(Window window)
    {
        switch (Settings.WindowMode)
        {
            case WindowMode.Fullscreen:
                window.Mode = Window.ModeEnum.ExclusiveFullscreen;
                break;

            case WindowMode.Borderless:
                window.Mode = Window.ModeEnum.Fullscreen;
                break;

            case WindowMode.Windowed:
            default:
                window.Mode = Window.ModeEnum.Windowed;
                window.Size = new Vector2I(Settings.ResolutionWidth, Settings.ResolutionHeight);
                break;
        }
    }
}
