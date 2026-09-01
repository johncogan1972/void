using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// A world type's cave generation settings (VOID-065,
/// <c>cave-generation-spec</c>).
///
/// <para>Null on a world type means no caves at all, which keeps the world solid
/// exactly as it was before carving existed. That is a real configuration, not
/// just a migration convenience: a portal world can legitimately be a sealed
/// slab.</para>
///
/// <para>Only Perlin worms are configured here. Cellular-automata caverns (spec
/// §4) are a separate subsystem and will add their own block rather than
/// overloading these fields.</para>
/// </summary>
public sealed class CaveConfig
{
    /// <summary>
    /// The field that bends worms as they walk; spec §3.2's "dedicated
    /// worm-direction noise field". JSON key <c>worm_direction_noise</c>.
    ///
    /// <para>One field shared by every worm, deliberately: worms walking through
    /// the same region then bend the same way, so a tunnel network reads as
    /// following the rock rather than as a set of unrelated squiggles. Its
    /// frequency is spec §3.3's <c>noise_scale</c>, and is the field's most
    /// load-bearing number — too high and worms jitter, too low and they run
    /// straight.</para>
    /// </summary>
    [JsonPropertyName("worm_direction_noise")]
    public NoiseFieldConfig WormDirectionNoise { get; init; } = new NoiseFieldConfig();

    /// <summary>Worm tuning for the outside layer, or null to carve none there.</summary>
    public WormConfig? Outside { get; init; }

    /// <summary>Worm tuning for the underground layer.</summary>
    public WormConfig? Underground { get; init; }

    /// <summary>Worm tuning for the deep layer.</summary>
    public WormConfig? Deep { get; init; }

    /// <summary>
    /// Worm tuning for the void layer. JSON key <c>void</c>, spelled out because
    /// <c>void</c> is a C# keyword — same treatment as
    /// <see cref="LayerProportions.VoidLayer"/>.
    /// </summary>
    [JsonPropertyName("void")]
    public WormConfig? VoidLayer { get; init; }

    /// <summary>
    /// The config for one layer, or null if that layer carves no worms.
    /// </summary>
    /// <remarks>
    /// A switch rather than a dictionary so the four layers stay compile-time
    /// names: a dictionary keyed by string would let a typo in a data file
    /// silently disable a whole layer's caves, which generates a plausible world
    /// rather than an error.
    /// </remarks>
    public WormConfig? For(WorldLayer layer) => layer switch
    {
        WorldLayer.Outside => Outside,
        WorldLayer.Underground => Underground,
        WorldLayer.Deep => Deep,
        WorldLayer.Void => VoidLayer,
        _ => null,
    };
}
