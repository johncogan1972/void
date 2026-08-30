using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Presentation-only trim for a biome (VOID-022), per world-data-model-spec §6.
///
/// <para><b>Every field here is post-MVP and every field is nullable.</b>
/// Nothing in generation or simulation may require any of them: a biome with an
/// entirely empty ambient block is valid, shipping content, not a data gap. The
/// loader deliberately performs no validation on this type.</para>
/// </summary>
public sealed class BiomeAmbient
{
    /// <summary>Music track id, or null for "no biome-specific music". JSON key <c>music_theme</c>.</summary>
    [JsonPropertyName("music_theme")]
    public string? MusicTheme { get; init; }

    /// <summary>
    /// Ambient particle effect id (motes, snow, dust), or null for none.
    /// JSON key <c>particle_effect</c>.
    /// </summary>
    [JsonPropertyName("particle_effect")]
    public string? ParticleEffect { get; init; }

    /// <summary>
    /// Subtle lighting shift applied inside the biome, or null to leave lighting
    /// untouched. JSON key <c>light_tint</c>, written as <c>[r, g, b, a]</c>.
    /// </summary>
    [JsonPropertyName("light_tint")]
    public BiomeLightTint? LightTint { get; init; }
}
