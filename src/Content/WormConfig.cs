using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Worm tuning for one world layer (VOID-065, <c>cave-generation-spec</c> §3.3
/// and §6).
///
/// <para>All of it is data because the four layers want genuinely different
/// caves — spec §6 asks for sparse tunnel mouths outside, a mining feel
/// underground, dense overlapping space deep, and long thin connections between
/// cathedral chambers in the void. Those are terrain design decisions, and a
/// world type that wants different ones should not need a code change.</para>
/// </summary>
public sealed class WormConfig
{
    /// <summary>
    /// How many worms to spawn per 1000 columns of world width. JSON key
    /// <c>worms_per_1000_columns</c>.
    ///
    /// <para>A density rather than a count so the same numbers mean the same
    /// caves at every size preset: spec §6 expresses these as "one worm per 40
    /// columns", which is 25 here. 0 disables the layer entirely, which is what
    /// the outside layer's "very low" becomes if a world type wants no surface
    /// caves at all.</para>
    /// </summary>
    [JsonPropertyName("worms_per_1000_columns")]
    public double WormsPer1000Columns { get; init; }

    /// <summary>Steps in a worm's walk; spec §3.3's <c>step_count</c>, typically 100-500.</summary>
    [JsonPropertyName("step_count")]
    public int StepCount { get; init; } = 200;

    /// <summary>
    /// Tiles moved per step; spec's <c>step_length</c>, typically 0.5-2.0.
    /// Should stay at or below <see cref="Radius"/>: carving stamps a disc at
    /// each step, so a step longer than the radius leaves gaps between the discs
    /// and the tunnel comes out as a dotted line.
    /// </summary>
    [JsonPropertyName("step_length")]
    public double StepLength { get; init; } = 1.0;

    /// <summary>
    /// Narrowest tunnel half-width in tiles. JSON key <c>radius_min</c>.
    ///
    /// <para>A range rather than a single value because spec §3.3 lists
    /// <c>radius</c> under "per worm": one value per layer makes every root worm
    /// in that layer exactly the same width, so a tunnel network reads as
    /// machine-cut pipe. Each worm draws its own base width from
    /// [<see cref="RadiusMin"/>, <see cref="RadiusMax"/>].</para>
    /// </summary>
    [JsonPropertyName("radius_min")]
    public double RadiusMin { get; init; } = 1.6;

    /// <summary>
    /// Widest tunnel half-width in tiles. JSON key <c>radius_max</c>. Must be at
    /// least <see cref="RadiusMin"/>; equal to it means every worm in the layer
    /// is the same width, which is legal and occasionally wanted.
    /// </summary>
    [JsonPropertyName("radius_max")]
    public double RadiusMax { get; init; } = 2.8;

    /// <summary>
    /// How much a tunnel pinches and widens <b>along its own length</b>, as a
    /// fraction of the worm's base width. JSON key <c>radius_variation</c>;
    /// 0 gives a tunnel of constant bore.
    ///
    /// <para>This is the variation that actually reads as rock. Per-worm width
    /// alone still gives every individual tunnel a dead constant bore from end to
    /// end — the eye reads that as extruded, not excavated. 0.35 lets a tunnel
    /// swell to roughly half again its width and squeeze to about two thirds.</para>
    /// </summary>
    [JsonPropertyName("radius_variation")]
    public double RadiusVariation { get; init; } = 0.35;

    /// <summary>
    /// Wavelength of that pinching, in worm steps. JSON key
    /// <c>radius_wavelength</c>. Short values make a lumpy tube; long ones make
    /// a tunnel that opens out and closes down over its length.
    /// </summary>
    [JsonPropertyName("radius_wavelength")]
    public double RadiusWavelength { get; init; } = 28.0;

    /// <summary>
    /// Maximum heading change per step, in radians; spec's <c>turn_rate</c>,
    /// typically 0.05-0.30. Converted to a whole number of
    /// <see cref="WormDirections"/> steps, so values below one step's worth round
    /// up to one rather than to a worm that can never turn.
    /// </summary>
    [JsonPropertyName("turn_rate")]
    public double TurnRate { get; init; } = 0.15;

    /// <summary>
    /// Per-step probability of spawning a child worm; spec's
    /// <c>branch_chance</c>, typically 0.0-0.05.
    /// </summary>
    [JsonPropertyName("branch_chance")]
    public double BranchChance { get; init; }

    /// <summary>
    /// Hard cap on children one worm may spawn. JSON key <c>max_branches</c>.
    ///
    /// <para>Not in the spec, and load-bearing. Branch chance alone is
    /// unbounded: at spec §3.3's upper end of 0.05 over a 300-step worm, a root
    /// worm spawns about fifteen children, each of which spawns fifteen more.
    /// A cap makes the work a worm can create finite and predictable, which
    /// matters because these paths are held for the whole of generation.</para>
    /// </summary>
    [JsonPropertyName("max_branches")]
    public int MaxBranches { get; init; } = 2;

    /// <summary>
    /// How many generations of children may spawn. JSON key
    /// <c>max_branch_depth</c>; 0 means a root worm may not branch at all.
    /// The second half of the bound described on <see cref="MaxBranches"/>.
    /// </summary>
    [JsonPropertyName("max_branch_depth")]
    public int MaxBranchDepth { get; init; } = 2;

    /// <summary>
    /// Fraction of <see cref="StepCount"/> a child worm walks, and of
    /// <see cref="Radius"/> it carves. JSON key <c>branch_scale</c>.
    ///
    /// <para>Children are shorter and thinner than their parent so a branch
    /// reads as a side passage rather than as a fork between two equals, which
    /// is spec §3.2's "inherits parent parameters with slight variation" made
    /// concrete.</para>
    /// </summary>
    [JsonPropertyName("branch_scale")]
    public double BranchScale { get; init; } = 0.6;
}
