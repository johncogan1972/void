namespace Void;

/// <summary>
/// An ambient environmental hazard attached to a biome (VOID-022), per
/// world-data-model-spec §6.
///
/// Portal-world biomes only in practice; the list is empty for every MVP
/// home-world biome, and an empty list is the normal case rather than missing
/// data.
/// </summary>
public sealed class BiomeHazard
{
    /// <summary>
    /// Hazard kind, e.g. <c>void_aura</c>, <c>poison_gas</c>, <c>lava</c>.
    /// A free string until a hazard registry exists; nothing resolves it yet.
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Strength multiplier for the hazard's effect. Unitless and hazard-specific
    /// — 1.0 is the hazard's own baseline, not a normalised 0..1 fraction.
    /// </summary>
    public float Intensity { get; init; }
}
