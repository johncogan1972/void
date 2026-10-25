using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Reads and writes <c>settings/graphics.json</c> (VOID-062).
///
/// Engine-free <c>System.IO</c> so the whole parse-and-recover path is
/// unit-testable against a temp directory; <see cref="DisplayBoot"/> supplies
/// the real <c>user://</c> path.
///
/// <para><b>JSON dialect, and why it is not <see cref="ManifestJson"/>'s.</b>
/// Manifests are machine-written, so that file is strict on purpose: slack
/// there could only ever hide a bug of ours. This file is the opposite case —
/// save-format-spec §3 specifies settings as plain text, outside the save
/// envelope, <i>user-editable by design</i>. A human is the second author, so
/// reading forgives what humans actually do: comments, trailing commas and
/// casing drift. It is written indented for the same reason.</para>
///
/// <para><b>Nothing here throws on bad input.</b> A player typo must not brick
/// boot. Fields are read one at a time out of a loose DOM, so a single broken
/// value falls back alone and leaves the rest intact; only a document that will
/// not parse at all falls back wholesale. Every substitution is reported as a
/// warning for the caller to log.</para>
/// </summary>
public static class GraphicsSettingsStore
{
    /// <summary>Path of the settings file relative to the user data root (save-format-spec §3).</summary>
    public const string RelativePath = "settings/graphics.json";

    /// <summary>
    /// Options for writing. Snake_case and snake_case enum strings match the
    /// one JSON dialect used everywhere else on disk, and indentation is
    /// deliberate: the player is expected to open this file.
    /// </summary>
    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    /// <summary>
    /// Options for reading. Comments and trailing commas are skipped rather
    /// than rejected because a hand-edited file will contain them sooner or
    /// later.
    /// </summary>
    private static readonly JsonDocumentOptions ReadOptions = new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads settings from <paramref name="path"/>, substituting defaults for
    /// anything missing, unreadable or out of range.
    /// </summary>
    /// <remarks>
    /// An absent file is not a warning — it is a first run, and writing one is
    /// the caller's choice, not this method's. Any other failure (unparseable
    /// document, unreadable file, wrong root type) yields
    /// <see cref="GraphicsSettings.Default"/> plus a warning naming the cause;
    /// the existing file is left untouched so the player can still fix their
    /// own typo rather than having it silently overwritten.
    /// </remarks>
    public static GraphicsSettingsValidation Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            return GraphicsSettings.Default.Normalised();
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fallback($"could not read {RelativePath} ({e.Message}); using defaults.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, ReadOptions);
        }
        catch (JsonException e)
        {
            return Fallback($"{RelativePath} is not valid JSON ({e.Message}); using defaults.");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fallback($"{RelativePath} must contain a JSON object; using defaults.");
            }

            List<string> warnings = new();
            GraphicsSettings loaded = new GraphicsSettings
            {
                WindowMode = ReadEnum(root, "window_mode", GraphicsSettings.Default.WindowMode, warnings),
                ResolutionWidth = ReadInt(root, "resolution_width", GraphicsSettings.Default.ResolutionWidth, warnings),
                ResolutionHeight = ReadInt(root, "resolution_height", GraphicsSettings.Default.ResolutionHeight, warnings),
                ZoomLevel = ReadInt(root, "zoom_level", GraphicsSettings.Default.ZoomLevel, warnings),
            };

            // Range checks live on the settings type, so the same clamping
            // applies whether values came from this file or from a future
            // settings screen.
            GraphicsSettingsValidation validated = loaded.Normalised();
            warnings.AddRange(validated.Warnings);
            return new GraphicsSettingsValidation(validated.Settings, new ReadOnlyCollection<string>(warnings));
        }
    }

    /// <summary>
    /// Writes <paramref name="settings"/> to <paramref name="path"/>, creating
    /// parent directories as needed.
    /// </summary>
    /// <remarks>
    /// Goes through <see cref="AtomicWriter"/> even though this file is small
    /// and plain: losing graphics settings to a crash mid-write would leave the
    /// player booting at defaults, which on a mis-detected display can mean
    /// booting into something they cannot see to fix. Values are normalised
    /// first, so an out-of-range value can never reach disk from inside the
    /// game.
    /// </remarks>
    public static void Save(string path, GraphicsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(settings);

        byte[] bytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(settings.Normalised().Settings, WriteOptions));
        AtomicWriter.Write(path, bytes);
    }

    /// <summary>Default settings plus a single warning explaining the whole-file fallback.</summary>
    private static GraphicsSettingsValidation Fallback(string warning)
    {
        GraphicsSettingsValidation defaults = GraphicsSettings.Default.Normalised();
        return new GraphicsSettingsValidation(
            defaults.Settings,
            new ReadOnlyCollection<string>(new List<string> { warning }));
    }

    /// <summary>
    /// Finds a property by name, ignoring case and underscores, so a
    /// hand-edited <c>"Window_Mode"</c> or <c>"windowmode"</c> still lands on
    /// <c>window_mode</c>. The exact match is tried first and short-circuits;
    /// the scan only runs for drifted names, and the first match wins — a file
    /// containing two spellings of the same key is already ambiguous, and
    /// picking one beats refusing to boot.
    /// </summary>
    private static bool TryFindProperty(JsonElement root, string name, out JsonElement value)
    {
        if (root.TryGetProperty(name, out value))
        {
            return true;
        }

        string wanted = Normalise(name);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (Normalise(property.Name) == wanted)
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Casing- and underscore-insensitive key form used only for matching, never for writing.</summary>
    private static string Normalise(string name) =>
        name.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    /// <summary>
    /// Reads one integer property, falling back per-field. A missing property
    /// is silent (a partial file is legal — it just means "default the rest");
    /// a present but non-numeric one warns, because that is a typo the player
    /// wants to hear about.
    /// </summary>
    private static int ReadInt(JsonElement root, string name, int fallback, List<string> warnings)
    {
        if (!TryFindProperty(root, name, out JsonElement value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed))
        {
            return parsed;
        }

        warnings.Add($"{name} in {RelativePath} is not a whole number; using {fallback}.");
        return fallback;
    }

    /// <summary>
    /// Reads one enum property by name, case-insensitively and ignoring
    /// underscores, so <c>"borderless"</c>, <c>"Borderless"</c> and
    /// <c>"BORDERLESS"</c> all work. Numbers are refused rather than accepted
    /// as ordinals: the enum's members are reorderable precisely because no
    /// ordinal is ever persisted.
    /// </summary>
    private static WindowMode ReadEnum(
        JsonElement root, string name, WindowMode fallback, List<string> warnings)
    {
        if (!TryFindProperty(root, name, out JsonElement value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            if (text is not null
                && Enum.TryParse(text.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out WindowMode parsed)
                && Enum.IsDefined(parsed))
            {
                return parsed;
            }
        }

        warnings.Add($"{name} in {RelativePath} is not one of windowed/borderless/fullscreen; using {fallback}.");
        return fallback;
    }
}
