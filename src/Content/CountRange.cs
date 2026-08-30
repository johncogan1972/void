using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Inclusive minimum/maximum stack count for a loot entry (VOID-023), per
/// loot-table-spec §4.
///
/// <para><b>Inclusive at both ends.</b> <c>[1, 3]</c> can roll 1, 2 or 3, and
/// <c>[2, 2]</c> always rolls exactly 2. The roll itself is Phase 5; the
/// interpretation of the pair is fixed here so authored data means one thing.</para>
///
/// <para><b>Serialised as the spec's two-element array</b> (<c>[1, 3]</c>), not
/// an object, so content files match the spec verbatim.</para>
///
/// <para><b>Both bounds are validated on construction</b> — non-negative, and
/// <see cref="Min"/> no greater than <see cref="Max"/>. An inverted range is
/// fatal rather than clamped because it would otherwise silently drop nothing
/// forever, which reads in play as "this item never drops" and never as an
/// error.</para>
/// </summary>
[JsonConverter(typeof(CountRange.ArrayConverter))]
public readonly struct CountRange : IEquatable<CountRange>
{
    /// <summary>Builds a validated range. Prefer this over the default value.</summary>
    /// <exception cref="ContentLoadException">
    /// If either bound is negative or <paramref name="min"/> exceeds
    /// <paramref name="max"/>. Fatal by design; see the type summary.
    /// </exception>
    public CountRange(int min, int max)
    {
        if (min < 0 || max < 0)
        {
            throw new ContentLoadException(
                $"count_range [{min}, {max}] has a negative bound; counts cannot be negative.");
        }

        if (min > max)
        {
            throw new ContentLoadException(
                $"count_range [{min}, {max}] is inverted (min > max), which would drop nothing forever.");
        }

        Min = min;
        Max = max;
    }

    /// <summary>Lowest count this entry can yield, inclusive.</summary>
    public int Min { get; }

    /// <summary>Highest count this entry can yield, inclusive.</summary>
    public int Max { get; }

    /// <summary>Ranges compare by value, so definitions round-trip comparably in tests.</summary>
    public bool Equals(CountRange other) => Min == other.Min && Max == other.Max;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CountRange other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Min, Max);

    /// <summary>Renders as the authored form, so failure messages read like the data file.</summary>
    public override string ToString() => $"[{Min}, {Max}]";

    /// <summary>Value equality operator, paired with <see cref="Equals(CountRange)"/>.</summary>
    public static bool operator ==(CountRange left, CountRange right) => left.Equals(right);

    /// <summary>Value inequality operator, paired with <see cref="Equals(CountRange)"/>.</summary>
    public static bool operator !=(CountRange left, CountRange right) => !left.Equals(right);

    /// <summary>
    /// Reads and writes the spec's <c>[min, max]</c> array form. Public only
    /// because <c>JsonConverterAttribute</c> must name the type.
    /// </summary>
    public sealed class ArrayConverter : JsonConverter<CountRange>
    {
        /// <summary>
        /// Parses exactly two integers. Any other shape or length is fatal: a
        /// one- or three-element array has no agreed meaning, and guessing one
        /// would ship whatever the author actually meant as a silent loot bug.
        /// </summary>
        public override CountRange Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException(
                    $"count_range must be a two-element array [min, max], found {reader.TokenType}.");
            }

            int min = ReadBound(ref reader, "min");
            int max = ReadBound(ref reader, "max");

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException("count_range must hold exactly two elements, [min, max].");
            }

            return new CountRange(min, max);
        }

        /// <summary>Writes the two-element array form, matching the spec.</summary>
        public override void Write(Utf8JsonWriter writer, CountRange value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStartArray();
            writer.WriteNumberValue(value.Min);
            writer.WriteNumberValue(value.Max);
            writer.WriteEndArray();
        }

        /// <summary>Pulls one integer bound, naming which one is malformed.</summary>
        private static int ReadBound(ref Utf8JsonReader reader, string which)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out int value))
            {
                throw new JsonException($"count_range {which} must be an integer.");
            }

            return value;
        }
    }
}
