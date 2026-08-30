using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// A biome's subtle lighting shift, as linear RGBA (VOID-022), per
/// world-data-model-spec §6.
///
/// Serialised as a four-element JSON array — <c>[1.0, 0.98, 0.92, 1.0]</c> —
/// matching biome-content-spec §8, rather than an object with r/g/b/a keys.
/// A struct with a converter rather than <c>float[]</c> so the four-component
/// shape is enforced at load time instead of blowing up in the renderer, and
/// so the value cannot be mutated after the registry is frozen.
///
/// <para>Post-MVP: nothing in the MVP renderer reads this, and it is always
/// nullable on <see cref="BiomeAmbient"/>.</para>
/// </summary>
[JsonConverter(typeof(BiomeLightTint.ArrayConverter))]
public readonly record struct BiomeLightTint(float R, float G, float B, float A)
{
    /// <summary>Number of components in the JSON array form. Exactly four; alpha is not optional.</summary>
    public const int ComponentCount = 4;

    /// <summary>
    /// Reads and writes <see cref="BiomeLightTint"/> as a four-element array.
    /// Public only because <c>JsonConverterAttribute</c> needs to name the type;
    /// no caller should construct it directly.
    /// </summary>
    public sealed class ArrayConverter : JsonConverter<BiomeLightTint>
    {
        /// <summary>
        /// Parses <c>[r, g, b, a]</c>. Any other shape or length is a
        /// <see cref="JsonException"/>, which the registry loader reports as a
        /// fatal <see cref="ContentLoadException"/> naming the file.
        /// </summary>
        public override BiomeLightTint Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException(
                    $"light_tint must be an array of {ComponentCount} numbers, found {reader.TokenType}.");
            }

            Span<float> components = stackalloc float[ComponentCount];
            int count = 0;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.Number)
                {
                    throw new JsonException(
                        $"light_tint components must be numbers, found {reader.TokenType}.");
                }

                if (count == ComponentCount)
                {
                    throw new JsonException(
                        $"light_tint must have exactly {ComponentCount} components (r, g, b, a).");
                }

                components[count++] = reader.GetSingle();
            }

            if (count != ComponentCount)
            {
                throw new JsonException(
                    $"light_tint must have exactly {ComponentCount} components (r, g, b, a), found {count}.");
            }

            return new BiomeLightTint(components[0], components[1], components[2], components[3]);
        }

        /// <summary>Writes the array form, so a round-trip reproduces the authored file.</summary>
        public override void Write(
            Utf8JsonWriter writer, BiomeLightTint value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStartArray();
            writer.WriteNumberValue(value.R);
            writer.WriteNumberValue(value.G);
            writer.WriteNumberValue(value.B);
            writer.WriteNumberValue(value.A);
            writer.WriteEndArray();
        }
    }
}
