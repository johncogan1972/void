using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Reads and writes <see cref="PlayerId"/> as a bare JSON string.
///
/// Without it, System.Text.Json would serialise the struct as
/// <c>{"value":"..."}</c>, which is both noisier in a save diff and a schema
/// change waiting to happen if the wrapper ever gains a second field. Attached
/// to the type by attribute, so it applies everywhere a player id appears —
/// including <c>PlayerId?</c>, which the framework wraps automatically.
/// </summary>
public sealed class PlayerIdJsonConverter : JsonConverter<PlayerId>
{
    /// <summary>
    /// Parses the UUID string. A malformed value is a hard failure, not
    /// <see cref="PlayerId.None"/>: silently unowning a player's anchor would
    /// look like a gameplay bug rather than a corrupt save.
    /// </summary>
    /// <exception cref="JsonException">If the token is not a UUID string.</exception>
    public override PlayerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a player id string, got {reader.TokenType}.");
        }

        string? text = reader.GetString();
        if (text is null || !Guid.TryParse(text, out Guid value))
        {
            throw new JsonException($"'{text}' is not a valid player id.");
        }

        return new PlayerId(value);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PlayerId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}
