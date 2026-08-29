using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Boot-time JSON loader: turns an <see cref="IContentSource"/> into a frozen
/// <see cref="Registry{T}"/> (VOID-006).
///
/// Deliberately engine-free — it depends only on <c>System.Text.Json</c> and
/// the <see cref="IContentSource"/> abstraction, so the whole parse/validate
/// path is unit-testable with no Godot engine initialised.
///
/// Each document may hold either a single definition object or an array of
/// them, so content can be split one-per-file or grouped, whichever reads
/// better. Adding an entry is therefore always a JSON-only change.
/// </summary>
public static class RegistryLoader
{
    /// <summary>
    /// Shared options. Snake_case JSON (<c>display_name</c>) maps onto PascalCase
    /// CLR properties (<c>DisplayName</c>); matching is also case-insensitive so
    /// hand-written data is forgiving. Comments and trailing commas are tolerated
    /// because content files are authored by hand; anything else malformed is an
    /// error naming the file.
    ///
    /// Enums are read and written as snake_case strings (<c>"platform"</c>), not
    /// integers: data files stay readable, and reordering an enum member can
    /// never silently repoint existing content at a different value.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
        },
    };

    /// <summary>
    /// Reads every document in <paramref name="source"/> and returns a frozen
    /// registry whose iteration order is ordinal-sorted by id.
    /// </summary>
    /// <exception cref="ContentLoadException">
    /// On malformed JSON, a missing or empty id, or a duplicate id. All three
    /// are fatal: partial content would make generation non-reproducible.
    /// </exception>
    public static Registry<T> Load<T>(IContentSource source)
        where T : IContentDefinition
    {
        ArgumentNullException.ThrowIfNull(source);

        RegistryBuilder<T> builder = new();
        LoadInto(builder, source);
        return builder.Build();
    }

    /// <summary>
    /// Merges a source into an existing builder, so several sources (base game
    /// then, later, mods or portal-world packs) can feed one registry.
    /// </summary>
    public static void LoadInto<T>(RegistryBuilder<T> builder, IContentSource source)
        where T : IContentDefinition
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);

        foreach (ContentDocument document in source.ReadAll())
        {
            foreach (T definition in Parse<T>(document))
            {
                builder.Add(definition, document.Name);
            }
        }
    }

    /// <summary>
    /// Parses one document into zero or more definitions. Exposed for tests and
    /// tooling that want to validate a single file.
    /// </summary>
    internal static List<T> Parse<T>(ContentDocument document)
        where T : IContentDefinition
    {
        List<T> result = new();

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(document.Json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            JsonElement root = parsed.RootElement;

            switch (root.ValueKind)
            {
                case JsonValueKind.Object:
                    result.Add(Deserialize<T>(root, document.Name));
                    break;

                case JsonValueKind.Array:
                    foreach (JsonElement element in root.EnumerateArray())
                    {
                        if (element.ValueKind != JsonValueKind.Object)
                        {
                            throw new ContentLoadException(
                                document.Name,
                                $"Array entries must be JSON objects, found {element.ValueKind}.");
                        }

                        result.Add(Deserialize<T>(element, document.Name));
                    }

                    break;

                default:
                    throw new ContentLoadException(
                        document.Name,
                        $"Root must be a JSON object or array of objects, found {root.ValueKind}.");
            }
        }
        catch (JsonException ex)
        {
            throw new ContentLoadException(document.Name, $"Malformed JSON: {ex.Message}", ex);
        }

        return result;
    }

    private static T Deserialize<T>(JsonElement element, string fileName)
        where T : IContentDefinition
    {
        T? value = element.Deserialize<T>(Options);

        if (value is null)
        {
            throw new ContentLoadException(fileName, $"Entry deserialised to null as {typeof(T).Name}.");
        }

        if (string.IsNullOrWhiteSpace(value.Id))
        {
            throw new ContentLoadException(
                fileName, $"A {typeof(T).Name} entry has a missing or empty 'id'.");
        }

        return value;
    }
}
