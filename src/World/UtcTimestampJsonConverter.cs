using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Serialises every manifest timestamp as ISO-8601 in UTC with a trailing
/// <c>Z</c> (<c>2026-08-30T11:22:33.4440000Z</c>).
///
/// The framework default would preserve whatever offset the caller happened to
/// hold, so the same instant saved on two machines would produce different text
/// and a pointless save diff. Normalising on write also means a save copied
/// between time zones sorts correctly as a string.
///
/// Reads accept any ISO-8601 offset and convert; the resulting
/// <see cref="DateTimeOffset"/> compares equal to the original instant.
/// </summary>
public sealed class UtcTimestampJsonConverter : JsonConverter<DateTimeOffset>
{
    /// <summary>Round-trippable UTC form. Fixed by the file format; do not localise.</summary>
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    /// <summary>
    /// Parses the stored timestamp and normalises it to UTC.
    /// </summary>
    /// <exception cref="JsonException">
    /// If the token is not an ISO-8601 string. A manifest with an unreadable
    /// creation time is corrupt, not merely undated.
    /// </exception>
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected an ISO-8601 timestamp string, got {reader.TokenType}.");
        }

        string? text = reader.GetString();
        if (text is null
            || !DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset value))
        {
            throw new JsonException($"'{text}' is not an ISO-8601 timestamp.");
        }

        return value;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
    }
}
