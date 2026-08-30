using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// JSON settings shared by every manifest payload (world-data-model-spec §4).
///
/// Manifests are plain JSON inside the save envelope: <see cref="SaveFile"/>
/// owns compression, obfuscation, integrity hashing and atomic writing, so
/// nothing here duplicates any of that. Keeping the payload readable is the
/// point — a debug-mode save (§14) can be opened in a text editor.
///
/// <b>Why not reuse <see cref="RegistryLoader.Options"/>:</b> content files are
/// authored by hand, so that loader forgives comments, trailing commas and
/// casing drift. Manifests are written only by this game, so the same slack
/// would only ever hide a bug — a manifest containing a comment did not come
/// from us. Naming (snake_case) and enum handling (snake_case strings, never
/// integers) match deliberately, so there is exactly one JSON dialect on disk.
/// </summary>
public static class ManifestJson
{
    /// <summary>
    /// Strict options for machine-written documents.
    /// </summary>
    /// <remarks>
    /// Nulls are written, never skipped: a null <c>picked_up_by</c> means "still
    /// in the world", which is different from a missing field, and the two must
    /// not collapse into each other on round-trip. Property order comes from the
    /// explicit <see cref="JsonPropertyOrderAttribute"/> on every manifest
    /// property, not from declaration order, so two saves diff line-for-line.
    ///
    /// Every manifest member is marked <see cref="JsonRequiredAttribute"/>,
    /// including the nullable ones. Top-level <c>required</c> alone is not
    /// enough: the nested types are positional records, and this serialiser
    /// fills a missing constructor parameter with <c>default</c> — so a
    /// half-written <c>main_boss_lair</c> would otherwise deserialise to row 0
    /// with a null prefab id and look like a real lair. Absent must fail;
    /// explicit null stays legal, which is what keeps "still in the world"
    /// distinguishable from "field dropped".
    /// </remarks>
    public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
            new UtcTimestampJsonConverter(),
        },
    };
}
