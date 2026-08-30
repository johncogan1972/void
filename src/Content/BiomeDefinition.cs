using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Definition backing one biome (VOID-022), per world-data-model-spec §6 and
/// biome-content-spec §8.
///
/// A biome is a coordination point, not a bundle of assets: it names the blocks,
/// walls, prefabs and enemies that generation should draw on for a region, and
/// everything it names resolves through another registry (spec §7). Adding a
/// biome is therefore a JSON-only change.
///
/// <para><b>Load through <see cref="BiomeRegistryLoader"/>, never
/// <c>RegistryLoader.Load&lt;BiomeDefinition&gt;</c> directly.</b> Half of what
/// makes a biome correct is cross-registry — palette ids must resolve, and an
/// <see cref="UndergroundVariant"/> must name a real underground biome — and
/// those checks live in the loader, not in this POCO.</para>
///
/// <para>Unlike blocks and walls, biomes carry no numeric id: nothing in the
/// save format stores a biome by number, so there is no stable-forever
/// numbering to defend. The JSON key is <c>id</c>, as for every other content
/// type, rather than spec §6's <c>biome_id</c>.</para>
/// </summary>
public sealed class BiomeDefinition : ICrossRegistryValidated
{
    /// <summary>Stable unique string id, e.g. <c>void:meadow</c>. JSON key <c>id</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-facing name, for UI and debug overlays. JSON key <c>display_name</c>.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// World layer this biome may be placed in. Load-bearing for pairing: only a
    /// biome whose category is <see cref="LayerCategory.Underground"/> may be
    /// named as another biome's <see cref="UndergroundVariant"/>. JSON key
    /// <c>layer_category</c>.
    /// </summary>
    public LayerCategory LayerCategory { get; init; } = LayerCategory.Surface;

    /// <summary>Blocks and walls generation fills the region with.</summary>
    public BiomePalette Palette { get; init; } = new BiomePalette();

    /// <summary>Prefab scatter sets. All three lists may be empty.</summary>
    public BiomeVegetation Vegetation { get; init; } = new BiomeVegetation();

    /// <summary>
    /// Multipliers on the base ore distribution. Ordinal-sorted and immutable so
    /// generation can iterate it without going non-deterministic; unlisted ores
    /// default to <see cref="OreBiasTable.DefaultMultiplier"/>. JSON key
    /// <c>ore_biases</c>.
    /// </summary>
    public OreBiasTable OreBiases { get; init; } = OreBiasTable.Empty;

    /// <summary>
    /// Weighted enemy spawn pool. May be empty for a peaceful biome. Enemy ids
    /// are not resolved yet — see
    /// <see cref="BiomeRegistryLoader.ValidateDeferredReferences"/>.
    /// </summary>
    public IReadOnlyList<BiomeEnemySpawn> Enemies { get; init; } = [];

    /// <summary>
    /// Id of the underground biome placed directly beneath this one, column by
    /// column (spec §6), or null when this biome has no pairing — which is every
    /// non-surface biome. When set, the target must exist and must itself be
    /// <see cref="LayerCategory.Underground"/>; both are fatal load errors
    /// otherwise. JSON key <c>underground_variant</c>.
    /// </summary>
    [JsonPropertyName("underground_variant")]
    public string? UndergroundVariant { get; init; }

    /// <summary>
    /// Music, particles and light tint. Entirely post-MVP and entirely optional;
    /// never validated, never required by generation.
    /// </summary>
    public BiomeAmbient Ambient { get; init; } = new BiomeAmbient();

    /// <summary>
    /// Ambient hazards. Empty for all MVP home-world biomes; populated only by
    /// portal-world themes.
    /// </summary>
    public IReadOnlyList<BiomeHazard> Hazards { get; init; } = [];
}
