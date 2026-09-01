using System;
using System.Collections.Generic;

namespace Void;

/// <summary>
/// Phase 1 step 4: assigns a surface biome to every world column from two
/// low-frequency climate fields (VOID-048, world-generation-spec §6).
///
/// <para><b>Deterministic.</b> Both fields come from sub-streams of
/// <see cref="GenKeys.Phase1BiomeMap"/> and are sampled statelessly — no draw
/// happens per column, so column order cannot affect the result. Rules are
/// visited in the authored array order, never registry or hash order.</para>
///
/// <para><b>Boundaries move, and they also blend.</b> Two separate mechanisms,
/// often confused:</para>
/// <list type="bullet">
/// <item><description><see cref="BiomeClassificationConfig.BlendColumns"/> offsets
/// the sample position by a low-frequency jitter, which <i>moves</i> a boundary.
/// It cannot soften one: the jitter field is far lower frequency than a boundary
/// is wide, so over the few columns around a seam it is effectively constant and
/// simply translates it.</description></item>
/// <item><description><see cref="BiomeClassificationConfig.Transition"/> gives each
/// boundary a band in which columns carry a second biome and a weight, and
/// <see cref="BiomeMap.BiomeAt(int, int)"/> chooses between them <b>per tile</b>
/// (VOID-060). That is what makes a seam an interleaved band rather than a
/// line.</description></item>
/// </list>
///
/// <para><b>Why the blend is per tile and not per column.</b> Dithering the
/// column <i>classification</i> was rejected, and correctly: it manufactures
/// exactly the single-column islands <see cref="EnforceMinimumRuns"/> then has to
/// destroy, so the two features fight. Dithering the palette a tile at a time
/// sidesteps that entirely — every column still belongs to exactly one biome, the
/// run-length rule is untouched, and the interleave lives below the resolution
/// the rule operates at.</para>
///
/// <para><b>Each boundary gets its own width.</b> A single configured width
/// would trade one uniform artefact for another; the width is drawn per boundary
/// from a range, so no two borders in a world look like the same border.</para>
///
/// <para>Engine-free: pure arithmetic over content config, so the whole step is
/// testable under <c>dotnet test</c>.</para>
/// </summary>
public static class BiomeClassifier
{
    // Sub-stream keys within phase 1's biome-map stream. Spelled here as
    // constants for the reason GenKeys gives: a typo derives a perfectly valid
    // stream and produces a different-but-plausible world, which nothing
    // downstream can detect. Once shipped, these strings are effectively part of
    // the save format — changing one reclassifies every existing seed.
    private const string TemperatureKey = "temperature";
    private const string HumidityKey = "humidity";
    private const string JitterKey = "seam_jitter";

    /// <summary>Picks each boundary's band width; see <see cref="ApplyTransitions"/>.</summary>
    private const string TransitionWidthKey = "transition_width";

    /// <summary>Seeds the field that displaces a boundary from row to row.</summary>
    private const string TransitionWanderKey = "transition_dither";

    /// <summary>
    /// Frequency of the seam-jitter field, in lattice cells per column. Fixed
    /// rather than authored: it controls how *often* a seam wanders, while
    /// <see cref="BiomeClassificationConfig.BlendColumns"/> controls how far,
    /// and one knob for a shape this narrow is enough. 1/256 puts a wobble at
    /// roughly four chunks, which reads as a coastline rather than as noise.
    /// </summary>
    private const double JitterFrequency = 1.0 / 256.0;

    /// <summary>Octaves in the jitter field. One: this is a wobble, not terrain.</summary>
    private const int JitterOctaves = 2;

    /// <summary>
    /// Frequency of the band-width field, in lattice cells per column. Much
    /// higher than <see cref="JitterFrequency"/> on purpose: this is sampled once
    /// per boundary, and boundaries are hundreds of columns apart, so a low
    /// frequency would hand every boundary in a region the same width and undo
    /// the whole point of drawing it.
    /// </summary>
    private const double TransitionWidthFrequency = 1.0 / 64.0;

    /// <summary>
    /// Frequency of the boundary-wander field, in lattice cells per row. This
    /// sets how tall the fingers two biomes interlock as are: 1/24 makes the edge
    /// hold a direction for a couple of dozen rows before turning, which reads as
    /// an interlocking border. Much higher and it frays into noise; much lower
    /// and the boundary is a straight line that happens to lean.
    /// </summary>
    private const double TransitionWanderFrequency = 1.0 / 24.0;

    /// <summary>Octaves in that field. Two, so the fingers are irregular rather than sinusoidal.</summary>
    private const int TransitionWanderOctaves = 2;

    /// <summary>
    /// Classifies every column of a world.
    /// </summary>
    /// <param name="context">
    /// Supplies the world type's <see cref="BiomeClassificationConfig"/>, the
    /// width, and the biome-map sub-stream. The stream is derived here and
    /// nowhere else, so this step's output cannot depend on how many draws any
    /// other phase made.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If the config is unusable — bad octaves, negative <c>blend_columns</c> or
    /// a <c>min_run_columns</c> below 1. Content load normally catches these
    /// first; reaching here means a config was built in code.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// If some column's climate matches no rule. The loader proves the rules tile
    /// the unit square, so this is unreachable from loaded content — it stays as
    /// a loud failure rather than a fallback biome, because a world quietly
    /// filled with a default biome looks generated and is wrong.
    /// </exception>
    public static BiomeMap Generate(GenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        BiomeClassificationConfig config = context.WorldType.BiomeClassification;

        if (config.BlendColumns < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(context), config.BlendColumns,
                "blend_columns is a jitter half-width in columns and cannot be negative.");
        }

        if (config.MinRunColumns < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(context), config.MinRunColumns,
                "min_run_columns must be at least 1; a run of zero columns does not exist.");
        }

        Rng stream = context.Stream(GenKeys.Phase1BiomeMap);
        FbmNoise temperature = new(stream.Derive(TemperatureKey), config.Temperature.ToFbmParameters());
        FbmNoise humidity = new(stream.Derive(HumidityKey), config.Humidity.ToFbmParameters());
        FbmNoise jitter = new(
            stream.Derive(JitterKey),
            new FbmParameters(JitterOctaves, JitterFrequency));

        string[] biomeIds = new string[context.SizePreset.WidthTiles];

        for (int x = 0; x < biomeIds.Length; x++)
        {
            // One jitter value shifts *both* fields together, so the pair of
            // samples still describes a single place. Sampling them at two
            // different offsets would mix the climate of two columns and could
            // produce a combination that exists nowhere in the world.
            double sampleX = x + (jitter.Sample(x) * config.BlendColumns);

            biomeIds[x] = Classify(
                config, temperature.SampleUnit(sampleX), humidity.SampleUnit(sampleX), x);
        }

        EnforceMinimumRuns(biomeIds, config.MinRunColumns);

        // Bands are computed after the run-length pass, never before: that pass
        // moves boundaries, and a band built around a boundary that then moves
        // would sit in the wrong place with nothing to say it had.
        string?[] blendIds = new string?[biomeIds.Length];
        double[] blendOffsets = new double[biomeIds.Length];
        int[] boundaryColumns = new int[biomeIds.Length];
        FbmNoise? wanderField = null;

        if (config.Transition is BiomeTransitionConfig transition && transition.MaxColumns > 0)
        {
            ApplyTransitions(
                biomeIds,
                blendIds,
                blendOffsets,
                boundaryColumns,
                transition,
                new FbmNoise(
                    stream.Derive(TransitionWidthKey),
                    new FbmParameters(1, TransitionWidthFrequency)));

            wanderField = new FbmNoise(
                stream.Derive(TransitionWanderKey),
                new FbmParameters(TransitionWanderOctaves, TransitionWanderFrequency));
        }

        return new BiomeMap(biomeIds, blendIds, blendOffsets, boundaryColumns, wanderField);
    }

    /// <summary>
    /// Gives every internal boundary a band of columns that carry the biome on
    /// the other side plus a weight, so materialisation can interleave the two
    /// (VOID-060).
    /// </summary>
    /// <remarks>
    /// <para><b>What a band stores is geometry, not a probability.</b> Each
    /// column records where it sits between the two biomes and which boundary it
    /// belongs to; <see cref="BiomeMap.TakesBlendAt"/> then moves the boundary
    /// from row to row and asks which side of it each tile fell on. The derived
    /// weight — half and half on the boundary, nothing at the edges — exists for
    /// the roughness crossfade, which needs a continuous quantity rather than a
    /// side.</para>
    ///
    /// <para><b>Each band is clamped to half the run on either side of it</b>, so
    /// two nearby boundaries cannot overlap and paint a column with a third
    /// biome's worth of confusion. That makes the configured maximum an
    /// expression of intent rather than a promise about any one border — a
    /// boundary between two short runs gets a narrow band whatever the config
    /// asks for.</para>
    ///
    /// <para>Boundaries are visited left to right and a later band overwrites an
    /// earlier one where they meet. The order is fixed and explicit so the
    /// output is reproducible; the clamp above makes the overlap rare enough that
    /// which rule wins is close to moot.</para>
    /// </remarks>
    private static void ApplyTransitions(
        string[] biomeIds,
        string?[] blendIds,
        double[] blendOffsets,
        int[] boundaryColumns,
        BiomeTransitionConfig transition,
        FbmNoise widthField)
    {
        int minWidth = Math.Max(0, transition.MinColumns);
        int maxWidth = Math.Max(minWidth, transition.MaxColumns);

        int runStart = 0;

        for (int boundary = 1; boundary <= biomeIds.Length; boundary++)
        {
            bool endOfRun = boundary == biomeIds.Length
                || !string.Equals(biomeIds[boundary], biomeIds[boundary - 1], StringComparison.Ordinal);

            if (!endOfRun)
            {
                continue;
            }

            // The final run ends at the world edge rather than at a boundary, so
            // there is nothing on the other side of it to blend towards.
            if (boundary == biomeIds.Length)
            {
                break;
            }

            int leftRunLength = boundary - runStart;
            int rightRunLength = RunLengthFrom(biomeIds, boundary);

            // SampleUnit is [0, 1], so this lands anywhere in the configured
            // range; sampled at the boundary column, so the width is a property
            // of where the border is rather than of how many came before it.
            double unit = widthField.SampleUnit(boundary);
            int width = minWidth + (int)(unit * (maxWidth - minWidth + 1));
            width = Math.Min(width, maxWidth);
            width = Math.Min(width, leftRunLength / 2);
            width = Math.Min(width, rightRunLength / 2);

            if (width > 0)
            {
                string leftBiome = biomeIds[boundary - 1];
                string rightBiome = biomeIds[boundary];

                for (int offset = -width; offset <= width; offset++)
                {
                    int x = boundary + offset;
                    if (x < 0 || x >= biomeIds.Length)
                    {
                        continue;
                    }

                    // Signed position within the band: -1 at its left edge, 0 on
                    // the boundary, +1 at its right. The +1 in the divisor keeps
                    // the outermost column just inside the range rather than
                    // exactly on it, so it still blends.
                    blendIds[x] = offset < 0 ? rightBiome : leftBiome;
                    blendOffsets[x] = offset / (double)(width + 1);
                    boundaryColumns[x] = boundary;
                }
            }

            runStart = boundary;
        }
    }

    /// <summary>Length of the run of identical ids beginning at <paramref name="start"/>.</summary>
    private static int RunLengthFrom(string[] biomeIds, int start)
    {
        int x = start + 1;
        while (x < biomeIds.Length
            && string.Equals(biomeIds[x], biomeIds[start], StringComparison.Ordinal))
        {
            x++;
        }

        return x - start;
    }

    /// <summary>
    /// First rule in <b>authored array order</b> whose rectangle contains the
    /// climate point. Array order, never a sorted or hashed traversal: overlap
    /// between rules is legal and the author's ordering is what resolves it.
    /// </summary>
    private static string Classify(
        BiomeClassificationConfig config, double temperature, double humidity, int column)
    {
        IReadOnlyList<BiomeClassificationRule> rules = config.Rules;

        for (int i = 0; i < rules.Count; i++)
        {
            if (rules[i].Matches(temperature, humidity))
            {
                return rules[i].Biome;
            }
        }

        throw new InvalidOperationException(
            $"Column {column} has climate (temperature {temperature}, humidity {humidity}), which "
            + "no biome_classification rule covers. The rules must tile the whole unit square; "
            + "content load checks exactly that, so this world type was not loaded through "
            + "WorldTypeRegistryLoader.");
    }

    /// <summary>
    /// Absorbs every run of fewer than <paramref name="minRun"/> columns into the
    /// run on its left, in a <b>single left-to-right pass</b>.
    ///
    /// <para>Why a pass rather than trusting the noise: the climate fields and
    /// the rule rectangles are authored in JSON, so nothing stops a world type
    /// from producing a boundary that flickers back and forth for a few columns.
    /// Relying on the fields being smooth would make "no single-column islands" a
    /// property of the data file, true only for the values that happen to ship.
    /// This pass makes it a property of the code, true for any config that
    /// loads.</para>
    ///
    /// <para><b>Direction is load-bearing.</b> Left to right, column 0 taken as
    /// authoritative: a short run is given the id of the already-final run to its
    /// left, and if that merge is still short — only possible when the leading run
    /// itself was short — it merges again. A right-to-left pass would satisfy the
    /// same rule and produce different biome layout, so the direction is part of
    /// the world's identity; reversing it regenerates every seed.</para>
    ///
    /// <para>The one run that may end up shorter than <paramref name="minRun"/> is
    /// the run starting at column 0, which has no left-hand neighbour to join. A
    /// world narrower than <paramref name="minRun"/> is entirely that case.</para>
    /// </summary>
    private static void EnforceMinimumRuns(string[] biomeIds, int minRun)
    {
        // Start column of each surviving run, in order; the last entry is the run
        // still being accumulated. A stack rather than a rescan so a merge does
        // not have to walk back over the run it just joined.
        List<int> runStarts = [0];

        for (int x = 1; x <= biomeIds.Length; x++)
        {
            bool endOfRun = x == biomeIds.Length
                || !string.Equals(biomeIds[x], biomeIds[x - 1], StringComparison.Ordinal);

            if (!endOfRun)
            {
                continue;
            }

            // Merge repeatedly: absorbing a short run can leave a run that is
            // still short, and that one is subject to the same rule.
            while (runStarts.Count > 1 && x - runStarts[^1] < minRun)
            {
                int start = runStarts[^1];
                runStarts.RemoveAt(runStarts.Count - 1);
                string left = biomeIds[runStarts[^1]];

                for (int i = start; i < x; i++)
                {
                    biomeIds[i] = left;
                }
            }

            if (x < biomeIds.Length)
            {
                runStarts.Add(x);
            }
        }
    }
}
