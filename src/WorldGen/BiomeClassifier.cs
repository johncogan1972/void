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
/// <para><b>Seams are ragged, not blended.</b> The output is exactly one biome
/// per column, so there is nothing to interpolate; instead the sample position
/// is offset by a low-frequency jitter bounded by
/// <see cref="BiomeClassificationConfig.BlendColumns"/>, which bends the boundary
/// left and right as it runs down the world. Dithering between two biomes at a
/// boundary was rejected for a concrete reason: it manufactures exactly the
/// single-column islands the run-length rule below then has to destroy, so the
/// two features would be fighting each other.</para>
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
        return new BiomeMap(biomeIds);
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
