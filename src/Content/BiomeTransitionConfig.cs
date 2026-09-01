using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// How wide the interleaved band between two surface biomes is (VOID-060),
/// optional per world type.
///
/// <para><b>The width is a range, not a number.</b> A single width would trade
/// one uniform artefact for another: every border in the world would be the same
/// border. Each boundary draws its own width from this range, so two Frostreach
/// coastlines in the same world do not look like the same coastline twice.</para>
///
/// <para>Omit the block for hard seams — the pre-VOID-060 behaviour, where a
/// biome changes completely between one column and the next.</para>
/// </summary>
public sealed class BiomeTransitionConfig
{
    /// <summary>
    /// Narrowest half-width, in columns, that a boundary's band may take. JSON
    /// key <c>min_columns</c>. 0 is legal and lets some boundaries stay hard,
    /// which is itself a kind of variety.
    /// </summary>
    [JsonPropertyName("min_columns")]
    public int MinColumns { get; init; }

    /// <summary>
    /// Widest half-width, in columns. JSON key <c>max_columns</c>. Must be at
    /// least <see cref="MinColumns"/>.
    ///
    /// <para>A band is clamped to half the length of the runs on either side of
    /// it, so a wide setting cannot make two nearby boundaries overlap and paint
    /// a third biome's worth of confusion between them. That clamp means this is
    /// an upper bound on intent rather than a promise about any one border.</para>
    /// </summary>
    [JsonPropertyName("max_columns")]
    public int MaxColumns { get; init; }
}
