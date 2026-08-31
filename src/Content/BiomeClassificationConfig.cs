using System.Collections.Generic;

namespace Void;

/// <summary>
/// JSON-facing tuning for Phase 1's surface biome map (VOID-048,
/// world-generation-spec §6, Phase 1 step 4).
///
/// <para>Classification is a 2D climate lookup: two independent low-frequency
/// noise fields give every column a temperature and a humidity in [0, 1], and
/// the first <see cref="BiomeClassificationRule"/> whose rectangle contains that
/// point names the column's surface biome. Which biomes a world can grow, and
/// where, is therefore entirely a data decision — adding a biome to a world type
/// is a JSON edit, not a code branch.</para>
///
/// <para>The two shape knobs are not cosmetic. <see cref="BlendColumns"/> makes
/// biome seams ragged instead of straight, and <see cref="MinRunColumns"/>
/// forbids the single-column islands that any noise threshold produces at a
/// boundary; both are enforced by <see cref="BiomeClassifier"/>, in code, so they
/// hold for whatever numbers a world type ships.</para>
/// </summary>
public sealed class BiomeClassificationConfig
{
    /// <summary>
    /// The empty default, present only so a world type written before this block
    /// existed still parses. It classifies nothing: a world type that reaches
    /// generation with no rules fails the coverage check at load, which is the
    /// intended outcome — biome layout is design and belongs in the data file.
    /// </summary>
    public static BiomeClassificationConfig Default { get; } = new BiomeClassificationConfig();

    /// <summary>
    /// Octave stack for the temperature field. Frequencies here are far lower
    /// than the heightmap's: a biome is a region tens of chunks across, not a
    /// hill.
    /// </summary>
    public NoiseFieldConfig Temperature { get; init; } = new NoiseFieldConfig();

    /// <summary>
    /// Octave stack for the humidity field. Authored at a different frequency
    /// from <see cref="Temperature"/> on purpose — matched frequencies would make
    /// the two fields visibly correlated and collapse the square to a diagonal,
    /// so most of the authored rectangles would never be reached.
    /// </summary>
    public NoiseFieldConfig Humidity { get; init; } = new NoiseFieldConfig();

    /// <summary>
    /// Half-width, in columns, of the jitter applied to the climate sample
    /// position. Zero gives dead-straight vertical seams; 24 makes a boundary
    /// wander about a chunk either way. Must be at least 0.
    /// </summary>
    public int BlendColumns { get; init; }

    /// <summary>
    /// Shortest run of one biome, in columns, that may survive. Runs shorter than
    /// this are absorbed into their left-hand neighbour by
    /// <see cref="BiomeClassifier"/>. Must be at least 1 — a value of 0 has no
    /// meaning, and a run of zero columns does not exist.
    /// </summary>
    public int MinRunColumns { get; init; } = 1;

    /// <summary>
    /// The climate rectangles, <b>in evaluation order</b>: first match wins, so
    /// the array order is part of the world's identity. They must together cover
    /// the entire unit square; <see cref="WorldTypeRegistryLoader"/> proves it at
    /// load, so an unclassifiable column is impossible rather than caught late.
    /// </summary>
    public IReadOnlyList<BiomeClassificationRule> Rules { get; init; } = [];
}
