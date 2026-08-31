using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// A half-open-authored, inclusively-tested <c>[min, max]</c> interval on one
/// normalised climate axis (VOID-048, world-generation-spec §6, Phase 1 step 4).
///
/// <para>Authored as the spec's two-element array — <c>"temperature": [0.30,
/// 1.00]</c> — which is why it carries its own converter rather than being an
/// object with two keys.</para>
///
/// <para><b>This type does not validate its bounds.</b> A range outside [0, 1],
/// or with <c>min &gt;= max</c>, is a content error that must be reported
/// against the world type that authored it, so the check lives in
/// <see cref="WorldTypeRegistryLoader"/> where that name is in scope. The
/// converter enforces only the <i>shape</i>, because a one- or three-element
/// array has no meaning to guess at.</para>
/// </summary>
/// <param name="Min">Lower bound, inclusive.</param>
/// <param name="Max">Upper bound, inclusive — see <see cref="Contains"/>.</param>
[JsonConverter(typeof(ArrayConverter))]
public readonly record struct UnitRange(double Min, double Max)
{
    /// <summary>The whole axis: the range a rule uses when it does not care about that axis.</summary>
    public static UnitRange Full { get; } = new UnitRange(0.0, 1.0);

    /// <summary>
    /// Inclusive at <b>both</b> ends, deliberately. Classification rules tile the
    /// unit square edge to edge, so half-open testing would leave the top edge of
    /// the square (temperature or humidity of exactly 1.0) matching no rule at
    /// all. Inclusive ends make adjacent rules overlap on their shared edge
    /// instead, which is harmless: rules are evaluated in authored order and the
    /// first match wins.
    /// </summary>
    public bool Contains(double value) => value >= Min && value <= Max;

    /// <summary>Renders as the authored form, so failure messages read like the data file.</summary>
    public override string ToString() => $"[{Min}, {Max}]";

    /// <summary>
    /// Reads and writes the <c>[min, max]</c> array form. Public only because
    /// <c>JsonConverterAttribute</c> must name the type.
    /// </summary>
    public sealed class ArrayConverter : JsonConverter<UnitRange>
    {
        /// <summary>Parses exactly two numbers; any other shape is fatal.</summary>
        public override UnitRange Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException(
                    $"A climate range must be a two-element array [min, max], found {reader.TokenType}.");
            }

            double min = ReadBound(ref reader, "min");
            double max = ReadBound(ref reader, "max");

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException("A climate range must hold exactly two elements, [min, max].");
            }

            return new UnitRange(min, max);
        }

        /// <summary>Writes the two-element array form, matching the authored shape.</summary>
        public override void Write(Utf8JsonWriter writer, UnitRange value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStartArray();
            writer.WriteNumberValue(value.Min);
            writer.WriteNumberValue(value.Max);
            writer.WriteEndArray();
        }

        /// <summary>Pulls one bound, naming which one is malformed.</summary>
        private static double ReadBound(ref Utf8JsonReader reader, string which)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.Number
                || !reader.TryGetDouble(out double value))
            {
                throw new JsonException($"A climate range's {which} bound must be a number.");
            }

            return value;
        }
    }
}
