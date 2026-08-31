namespace Void;

/// <summary>
/// One rectangle of the temperature x humidity square, and the surface biome it
/// selects (VOID-048, world-generation-spec §6, Phase 1 step 4).
///
/// <para><b>Order is meaning.</b> Rules are evaluated in the order they appear in
/// the world type's <c>rules</c> array and the <i>first</i> whose rectangle
/// contains a column's climate sample wins. Overlap is therefore legal and is a
/// normal authoring tool — a narrow special case listed first shadows a broad
/// fallback listed after it. Reordering the array regenerates every existing
/// seed's biome layout, so it is a content change of the same weight as
/// retuning the noise.</para>
///
/// <para>Gaps, unlike overlaps, are not legal: the rules must tile the whole
/// unit square, checked exactly at load by
/// <see cref="WorldTypeRegistryLoader"/>. That is what makes "no column can be
/// unclassifiable" a boot-time guarantee rather than a runtime hope.</para>
/// </summary>
public sealed class BiomeClassificationRule
{
    /// <summary>
    /// Id of the biome this rectangle selects. Must resolve to a registered
    /// biome whose <see cref="BiomeDefinition.LayerCategory"/> is
    /// <see cref="LayerCategory.Surface"/> — checked in the deferred pass, since
    /// world types load after biomes.
    /// </summary>
    public string Biome { get; init; } = string.Empty;

    /// <summary>
    /// Temperature span, normalised 0 (coldest) to 1 (hottest). Defaults to the
    /// whole axis so a rule may state only the axis it cares about.
    /// </summary>
    public UnitRange Temperature { get; init; } = UnitRange.Full;

    /// <summary>Humidity span, normalised 0 (driest) to 1 (wettest); same defaulting as <see cref="Temperature"/>.</summary>
    public UnitRange Humidity { get; init; } = UnitRange.Full;

    /// <summary>
    /// Whether this rule claims a climate sample. Inclusive on every edge —
    /// see <see cref="UnitRange.Contains"/> for why that is safe given first-match
    /// ordering.
    /// </summary>
    public bool Matches(double temperature, double humidity) =>
        Temperature.Contains(temperature) && Humidity.Contains(humidity);
}
