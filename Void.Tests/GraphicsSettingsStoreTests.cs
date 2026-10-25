using System;
using System.IO;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// Load/save and recovery coverage for <c>settings/graphics.json</c>
/// (VOID-062). Each test writes into its own temp directory, which is what the
/// engine-free path exists for: the real game passes a globalised
/// <c>user://</c> path, and nothing here needs an engine.
/// </summary>
public sealed class GraphicsSettingsStoreTests : IDisposable
{
    private readonly string _root;

    /// <summary>Throwaway settings root per test; never a real user path.</summary>
    public GraphicsSettingsStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "void-settings-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    /// <summary>Removes the temp root; leftovers in the OS temp dir are harmless.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Nothing to do; the OS will reclaim it.
        }
    }

    private string Path0 => Path.Combine(_root, "settings", "graphics.json");

    /// <summary>
    /// Everything the player chose must survive a restart. A silent round-trip
    /// failure would look like the settings screen not saving at all.
    /// </summary>
    [Fact]
    public void RoundTripsEverySetting()
    {
        GraphicsSettings written = new GraphicsSettings
        {
            WindowMode = WindowMode.Borderless,
            ResolutionWidth = 2560,
            ResolutionHeight = 1440,
            ZoomLevel = 3,
        };

        GraphicsSettingsStore.Save(Path0, written);
        GraphicsSettingsValidation read = GraphicsSettingsStore.Load(Path0);

        Assert.Equal(written, read.Settings);
        Assert.Empty(read.Warnings);
    }

    /// <summary>
    /// The written file is meant to be opened and edited by the player
    /// (save-format-spec §3), so it must be indented and use readable names
    /// rather than enum ordinals.
    /// </summary>
    [Fact]
    public void WritesReadableSnakeCaseJson()
    {
        GraphicsSettingsStore.Save(Path0, GraphicsSettings.Default with { WindowMode = WindowMode.Fullscreen });

        string text = File.ReadAllText(Path0);

        Assert.Contains("\n", text, StringComparison.Ordinal);
        Assert.Contains("\"window_mode\"", text, StringComparison.Ordinal);
        Assert.Contains("\"fullscreen\"", text, StringComparison.Ordinal);
        Assert.Contains("\"zoom_level\"", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// First run: no file yet. Must be defaults and must not be reported as a
    /// problem, or every fresh install logs a spurious warning.
    /// </summary>
    [Fact]
    public void AbsentFileYieldsSilentDefaults()
    {
        GraphicsSettingsValidation read = GraphicsSettingsStore.Load(Path0);

        Assert.Equal(GraphicsSettings.Default, read.Settings);
        Assert.Empty(read.Warnings);
    }

    /// <summary>
    /// A player typo must cost one setting, not the game. Boot has to survive
    /// unparseable JSON with defaults and a warning rather than throwing.
    /// </summary>
    [Fact]
    public void MalformedJsonFallsBackToDefaultsWithWarning()
    {
        Write("{ \"window_mode\": \"borderless\" ");

        GraphicsSettingsValidation read = GraphicsSettingsStore.Load(Path0);

        Assert.Equal(GraphicsSettings.Default, read.Settings);
        Assert.Single(read.Warnings);
    }

    /// <summary>
    /// A root that is not an object (an array, a bare number) is the other
    /// shape of hand-edit accident; same contract as malformed JSON.
    /// </summary>
    [Fact]
    public void NonObjectRootFallsBackToDefaults()
    {
        Write("[1, 2, 3]");

        GraphicsSettingsValidation read = GraphicsSettingsStore.Load(Path0);

        Assert.Equal(GraphicsSettings.Default, read.Settings);
        Assert.Single(read.Warnings);
    }

    /// <summary>
    /// A partial file is legal: the player may want to set one thing. Fields
    /// present must be honoured, absent ones defaulted, and nothing warned —
    /// otherwise an edited file nags on every boot.
    /// </summary>
    [Fact]
    public void PartialFileKeepsPresentFieldsAndDefaultsTheRest()
    {
        Write("{ \"zoom_level\": 2 }");

        GraphicsSettingsValidation read = GraphicsSettingsStore.Load(Path0);

        Assert.Equal(2, read.Settings.ZoomLevel);
        Assert.Equal(GraphicsSettings.Default.WindowMode, read.Settings.WindowMode);
        Assert.Equal(GraphicsSettings.Default.ResolutionWidth, read.Settings.ResolutionWidth);
        Assert.Empty(read.Warnings);
    }

    /// <summary>
    /// Hand-editing produces comments, trailing commas and casing drift. This
    /// file forgives all three deliberately — unlike <c>ManifestJson</c>, whose
    /// documents only ever come from the game.
    /// </summary>
    [Fact]
    public void ToleratesCommentsTrailingCommasAndCasingDrift()
    {
        Write("""
        {
            // my preferred setup
            "Window_Mode": "Fullscreen",
            "zoom_level": 2,
        }
        """);

        GraphicsSettingsValidation read = GraphicsSettingsStore.Load(Path0);

        Assert.Equal(WindowMode.Fullscreen, read.Settings.WindowMode);
        Assert.Equal(2, read.Settings.ZoomLevel);
        Assert.Empty(read.Warnings);
    }

    /// <summary>
    /// One bad field must not drag the valid ones down with it: the broken
    /// value defaults and warns, the good value stands.
    /// </summary>
    [Fact]
    public void BadFieldFallsBackAloneAndWarns()
    {
        Write("{ \"window_mode\": \"potato\", \"zoom_level\": 3 }");

        GraphicsSettingsValidation read = GraphicsSettingsStore.Load(Path0);

        Assert.Equal(GraphicsSettings.Default.WindowMode, read.Settings.WindowMode);
        Assert.Equal(3, read.Settings.ZoomLevel);
        Assert.Single(read.Warnings);
    }

    /// <summary>
    /// Enum ordinals are refused on read. The <see cref="WindowMode"/> members
    /// are safe to reorder only because no ordinal is ever persisted or
    /// accepted; accepting <c>2</c> here would quietly create that coupling.
    /// </summary>
    [Fact]
    public void NumericWindowModeIsRefused()
    {
        Write("{ \"window_mode\": 2 }");

        GraphicsSettingsValidation read = GraphicsSettingsStore.Load(Path0);

        Assert.Equal(GraphicsSettings.Default.WindowMode, read.Settings.WindowMode);
        Assert.Single(read.Warnings);
    }

    /// <summary>
    /// GDD §9.4 supports 1x-3x and explicitly does not support zooming below
    /// 1x. Sub-1x would break pixel-perfect rendering and widen the awareness
    /// gap the aspect cap bounds, so it must clamp up rather than be honoured.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(4, 3)]
    [InlineData(99, 3)]
    public void OutOfRangeZoomClampsWithWarning(int written, int expected)
    {
        Write($"{{ \"zoom_level\": {written} }}");

        GraphicsSettingsValidation read = GraphicsSettingsStore.Load(Path0);

        Assert.Equal(expected, read.Settings.ZoomLevel);
        Assert.Single(read.Warnings);
    }

    /// <summary>
    /// A resolution below the Steam Deck baseline (1280x800) would put the
    /// player under the viewport the game is balanced for, so it is raised
    /// rather than honoured.
    /// </summary>
    [Fact]
    public void UndersizedResolutionRaisesToBaseWithWarnings()
    {
        Write("{ \"resolution_width\": 640, \"resolution_height\": 480 }");

        GraphicsSettingsValidation read = GraphicsSettingsStore.Load(Path0);

        Assert.Equal(DisplayConfig.BaseViewportWidth, read.Settings.ResolutionWidth);
        Assert.Equal(DisplayConfig.BaseViewportHeight, read.Settings.ResolutionHeight);
        Assert.Equal(2, read.Warnings.Count);
    }

    /// <summary>
    /// Out-of-range values must never reach disk from inside the game, so that
    /// a save by a future settings screen cannot produce a file that warns on
    /// the next load.
    /// </summary>
    [Fact]
    public void SaveNormalisesBeforeWriting()
    {
        GraphicsSettingsStore.Save(Path0, GraphicsSettings.Default with { ZoomLevel = 11 });

        GraphicsSettingsValidation read = GraphicsSettingsStore.Load(Path0);

        Assert.Equal(DisplayConfig.MaxZoomLevel, read.Settings.ZoomLevel);
        Assert.Empty(read.Warnings);
    }

    /// <summary>
    /// A broken file is left on disk untouched, so the player can see and fix
    /// their own typo instead of having it silently overwritten.
    /// </summary>
    [Fact]
    public void LoadDoesNotRewriteABrokenFile()
    {
        const string broken = "{ not json at all";
        Write(broken);

        GraphicsSettingsStore.Load(Path0);

        Assert.Equal(broken, File.ReadAllText(Path0));
    }

    private void Write(string text)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path0)!);
        File.WriteAllText(Path0, text);
    }
}
