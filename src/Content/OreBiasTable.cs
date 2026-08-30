using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Per-biome multipliers applied to the base ore distribution (VOID-022), per
/// world-data-model-spec §6.
///
/// <para><b>Why not a <c>Dictionary&lt;string, float&gt;</c>.</b> Ore biases
/// feed world generation directly, and CLAUDE.md forbids hash-ordered iteration
/// from reaching generated output: <c>Dictionary</c> enumeration order depends
/// on insertion history and runtime internals, so a generator that walked one
/// would be reproducible only by luck. This type sorts its keys once, with
/// <see cref="StringComparer.Ordinal"/> (culture-independent, so it is identical
/// on every machine), and is immutable thereafter. Iterating it is always
/// safe.</para>
///
/// <para><b>Absent ore ids default to a multiplier of 1.0</b> — the table lists
/// only the deviations from the base distribution, so "not mentioned" means
/// "unbiased", never "never generates". Use <see cref="Multiplier"/> rather than
/// probing the entries.</para>
/// </summary>
[JsonConverter(typeof(OreBiasTable.MapConverter))]
public sealed class OreBiasTable : IReadOnlyList<KeyValuePair<string, float>>
{
    /// <summary>Multiplier assumed for any ore id the table does not mention.</summary>
    public const float DefaultMultiplier = 1.0f;

    private readonly KeyValuePair<string, float>[] _sorted;
    private readonly Dictionary<string, float> _byId;

    /// <summary>A table with no biases — every ore generates at its base rate.</summary>
    public static OreBiasTable Empty { get; } = new OreBiasTable(new Dictionary<string, float>(StringComparer.Ordinal));

    /// <summary>
    /// Freezes <paramref name="biases"/> into ordinal-sorted order. The caller's
    /// collection is copied, so later mutation of it cannot reach generation.
    /// </summary>
    /// <exception cref="ContentLoadException">If an ore id is missing or blank.</exception>
    public OreBiasTable(IEnumerable<KeyValuePair<string, float>> biases)
    {
        ArgumentNullException.ThrowIfNull(biases);

        _byId = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, float> bias in biases)
        {
            if (string.IsNullOrWhiteSpace(bias.Key))
            {
                throw new ContentLoadException("An ore_biases entry has a missing or empty ore id.");
            }

            if (!_byId.TryAdd(bias.Key, bias.Value))
            {
                throw new ContentLoadException(
                    $"Duplicate ore_biases entry for ore '{bias.Key}'.");
            }
        }

        _sorted = new KeyValuePair<string, float>[_byId.Count];
        int i = 0;
        foreach (KeyValuePair<string, float> entry in _byId)
        {
            _sorted[i++] = entry;
        }

        Array.Sort(_sorted, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
    }

    /// <summary>Number of ores carrying an explicit bias.</summary>
    public int Count => _sorted.Length;

    /// <summary>Entry at <paramref name="index"/> in ordinal-sorted key order.</summary>
    public KeyValuePair<string, float> this[int index] => _sorted[index];

    /// <summary>
    /// Bias for <paramref name="oreId"/>, or <see cref="DefaultMultiplier"/> if
    /// the biome does not mention that ore. This is the only lookup generation
    /// should use.
    /// </summary>
    public float Multiplier(string oreId)
    {
        ArgumentNullException.ThrowIfNull(oreId);
        return _byId.TryGetValue(oreId, out float value) ? value : DefaultMultiplier;
    }

    /// <summary>True if an explicit bias was authored for <paramref name="oreId"/>.</summary>
    public bool Contains(string oreId)
    {
        ArgumentNullException.ThrowIfNull(oreId);
        return _byId.ContainsKey(oreId);
    }

    /// <summary>Enumerates biases in ordinal-sorted key order, never hash order.</summary>
    public IEnumerator<KeyValuePair<string, float>> GetEnumerator() =>
        ((IEnumerable<KeyValuePair<string, float>>)_sorted).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _sorted.GetEnumerator();

    /// <summary>
    /// Reads and writes the table as a plain JSON object (<c>{ "copper": 1.2 }</c>).
    /// Writing emits ordinal-sorted keys, so a load/save round-trip normalises
    /// however the file happened to be authored. Public only because
    /// <c>JsonConverterAttribute</c> must name the type.
    /// </summary>
    public sealed class MapConverter : JsonConverter<OreBiasTable>
    {
        /// <summary>Parses the object form; any other JSON shape is fatal.</summary>
        public override OreBiasTable Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException(
                    $"ore_biases must be a JSON object of ore id to multiplier, found {reader.TokenType}.");
            }

            Dictionary<string, float> biases = new(StringComparer.Ordinal);

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string key = reader.GetString() ?? string.Empty;

                if (!reader.Read() || reader.TokenType != JsonTokenType.Number)
                {
                    throw new JsonException($"ore_biases entry '{key}' must be a number.");
                }

                if (!biases.TryAdd(key, reader.GetSingle()))
                {
                    throw new JsonException($"Duplicate ore_biases entry for ore '{key}'.");
                }
            }

            return new OreBiasTable(biases);
        }

        /// <summary>Writes the object form with keys in ordinal-sorted order.</summary>
        public override void Write(Utf8JsonWriter writer, OreBiasTable value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);

            writer.WriteStartObject();
            foreach (KeyValuePair<string, float> bias in value)
            {
                writer.WriteNumber(bias.Key, bias.Value);
            }

            writer.WriteEndObject();
        }
    }
}
