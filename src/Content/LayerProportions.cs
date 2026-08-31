using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// The four vertical layer proportions of a world type (world-generation-spec
/// §4), as fractions of total world height.
///
/// <para>Data, not code: the defaults are 30/25/30/15, but every world type —
/// home and each portal-world theme — may override them, which is how a
/// portal world gets, say, a token sky and an enormous void with no code
/// change.</para>
///
/// <para>The four values must sum to 1 and must each produce a layer at least
/// one row tall at every declared size preset. Both are checked by
/// <see cref="WorldTypeRegistryLoader"/> and are fatal there; nothing
/// downstream re-checks them, because a squashed layer produces a world that
/// generates and plays wrong rather than one that fails.</para>
/// </summary>
public sealed class LayerProportions
{
    /// <summary>The default 30/25/30/15 split of spec §4, used by the home world.</summary>
    public static LayerProportions Default { get; } = new LayerProportions
    {
        Outside = 0.30,
        Underground = 0.25,
        Deep = 0.30,
        VoidLayer = 0.15,
    };

    // The four fractions, in top-to-bottom world order. Each defaults to 0 so an
    // omitted field fails the sum-to-1 check loudly instead of quietly
    // inheriting a default that the data file never said.
    public double Outside { get; init; }

    public double Underground { get; init; }

    public double Deep { get; init; }

    /// <summary>
    /// Bottom layer's fraction. Named <c>VoidLayer</c> rather than <c>Void</c>
    /// because the assembly's root namespace is <c>Void</c>; the JSON key is
    /// still <c>void</c>, matching the spec's layer name.
    /// </summary>
    [JsonPropertyName("void")]
    public double VoidLayer { get; init; }

    /// <summary>
    /// The four fractions added top-to-bottom. Summed in world order rather than
    /// any other, so the floating-point result is identical on every machine and
    /// the sum check cannot pass on one and fail on another.
    /// </summary>
    [JsonIgnore]
    public double Sum => Outside + Underground + Deep + VoidLayer;
}
