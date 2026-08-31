using System;
using System.Collections.Generic;

namespace Void;

/// <summary>
/// Boot-time loader for the world-type registry (VOID-046).
///
/// <para>Since VOID-048 a world type does name ids in another registry — every
/// biome classification rule names a surface biome — but that half is deferred
/// to <see cref="ValidateDeferredReferences"/>; everything <see cref="Load"/>
/// checks is self-contained. It is the only way in, because the half of
/// a world type that matters most is arithmetic that JSON parsing cannot check:
/// layer proportions that do not sum to 1, or that squash a layer to zero rows
/// at some size preset, parse perfectly and then generate a world that is
/// simply wrong. Every failure here is fatal at boot rather than a clamp,
/// because a silently squashed layer is invisible until someone digs to it.</para>
///
/// <para>Engine-free, like the rest of the content layer.</para>
/// </summary>
public static class WorldTypeRegistryLoader
{
    /// <summary>
    /// Tolerance on the layer proportions summing to 1. The values are authored
    /// as decimal fractions (0.3, 0.25) that have no exact binary
    /// representation, so an exact <c>== 1.0</c> test would reject correct data;
    /// this is wide enough to absorb the accumulated representation error of
    /// four such additions and far too narrow to let a genuine authoring slip
    /// (0.3 + 0.25 + 0.3 + 0.1) through.
    /// </summary>
    public const double ProportionSumTolerance = 1e-9;

    /// <summary>
    /// Parses every world-type document in <paramref name="source"/> and
    /// validates each entry's proportions and size presets.
    /// </summary>
    /// <exception cref="ContentLoadException">
    /// On malformed JSON, a duplicate id, proportions that do not sum to 1, a
    /// non-positive or duplicated size preset, a default <c>size_preset</c> that
    /// names no declared preset, any preset height at which a layer would be
    /// zero rows tall, an invalid heightmap octave stack or slope cap, or
    /// heightmap band fractions that leave no usable surface band at some
    /// preset, an invalid biome-classification octave stack or shape knob, or
    /// classification rules that leave any part of the climate square uncovered.
    /// </exception>
    public static Registry<WorldTypeDefinition> Load(IContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Registry<WorldTypeDefinition> worldTypes =
            RegistryLoader.LoadUnvalidated<WorldTypeDefinition>(source);

        // Ordinal-sorted registry order, so a data drop with several broken
        // entries blames the same one on every machine.
        foreach (WorldTypeDefinition worldType in worldTypes)
        {
            ValidateProportions(worldType);
            ValidateHeightmapOctaves(worldType);
            ValidateTerrain(worldType);
            ValidateSizePresets(worldType);

            // Classification last, for the same reason CheckSurfaceBandFits runs
            // in a second pass: it is the least fundamental of the checks, and a
            // world type with a broken layer split should send its author to the
            // split rather than to a biome table that was never the problem.
            ValidateBiomeClassification(worldType);
        }

        return worldTypes;
    }

    /// <summary>
    /// Checks the four fractions sum to 1 (within
    /// <see cref="ProportionSumTolerance"/>) and that none is negative. A
    /// negative fraction would still let a sum reach 1 while inverting a
    /// boundary, so it is checked separately rather than inferred from the sum.
    /// </summary>
    private static void ValidateProportions(WorldTypeDefinition worldType)
    {
        LayerProportions p = worldType.LayerProportions;

        CheckNonNegative(worldType, "outside", p.Outside);
        CheckNonNegative(worldType, "underground", p.Underground);
        CheckNonNegative(worldType, "deep", p.Deep);
        CheckNonNegative(worldType, "void", p.VoidLayer);

        if (Math.Abs(p.Sum - 1.0) > ProportionSumTolerance)
        {
            throw new ContentLoadException(
                $"World type '{worldType.Id}' layer_proportions sum to {p.Sum}, not 1 " +
                $"(tolerance {ProportionSumTolerance}). Layers are fractions of world height; " +
                "a sum other than 1 would leave rows in no layer or in two.");
        }
    }

    /// <summary>One proportion's sign check, named so the message points at the field.</summary>
    private static void CheckNonNegative(WorldTypeDefinition worldType, string field, double value)
    {
        if (value < 0.0)
        {
            throw new ContentLoadException(
                $"World type '{worldType.Id}' layer_proportions.{field} is {value}; " +
                "proportions are fractions of world height and cannot be negative.");
        }
    }

    /// <summary>
    /// Checks the heightmap's octave stack and slope cap — the parts that do not
    /// depend on world height. The octave check is delegated to
    /// <see cref="HeightmapConfig.ToFbmParameters"/> rather than re-listed here,
    /// so there is exactly one definition of a valid octave stack; the throw it
    /// produces is translated into a <see cref="ContentLoadException"/> that
    /// names the world type, because a stack trace out of a struct constructor
    /// does not tell an author which data file to open.
    /// </summary>
    private static void ValidateHeightmapOctaves(WorldTypeDefinition worldType)
    {
        HeightmapConfig heightmap = worldType.Heightmap;

        try
        {
            heightmap.ToFbmParameters();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ContentLoadException(
                $"World type '{worldType.Id}' has an invalid heightmap octave stack: {ex.Message}");
        }

        if (heightmap.MaxColumnDelta < 1)
        {
            throw new ContentLoadException(
                $"World type '{worldType.Id}' heightmap.max_column_delta is " +
                $"{heightmap.MaxColumnDelta}; it is the per-column elevation cap in rows and must " +
                "be at least 1, or generation would flatten the world to a single row.");
        }
    }

    /// <summary>
    /// Checks the terrain block's fallback subsurface depth. Zero is legal and
    /// means the surface block sits directly on the base block; negative is not,
    /// because materialisation would read it as "start the base fill above the
    /// surface" and invert the column.
    /// </summary>
    private static void ValidateTerrain(WorldTypeDefinition worldType)
    {
        int depth = worldType.Terrain.DefaultSubsurfaceDepth;

        if (depth < 0)
        {
            throw new ContentLoadException(
                $"World type '{worldType.Id}' terrain.default_subsurface_depth is {depth}; " +
                "it is a band thickness in rows and cannot be negative. Use 0 for no subsurface " +
                "band at all.");
        }
    }

    /// <summary>
    /// Resolves every biome classification rule's biome id, now that the biome
    /// registry exists.
    ///
    /// <para><b>Why deferred at all, when biomes load before world types.</b>
    /// Passing the biome registry into <see cref="Load"/> would work today and
    /// would quietly make world-type loading order-dependent on a registry it
    /// otherwise has nothing to do with; keeping the reference check in the same
    /// closing step as
    /// <see cref="BiomeRegistryLoader.ValidateDeferredReferences"/> keeps every
    /// cross-registry check in world generation's content in one place, and keeps
    /// <see cref="Load"/> callable — as the tests call it — with a hand-written
    /// world type and no other registry in hand.</para>
    ///
    /// <para>Called by <see cref="ContentLoader.LoadAll"/> as part of the last
    /// step of boot. It is not optional: a rule naming a biome that does not
    /// exist is a world type that cannot classify part of its own climate
    /// square, and generation would fail mid-world instead of at boot.</para>
    /// </summary>
    /// <param name="worldTypes">Registry returned by <see cref="Load"/>.</param>
    /// <param name="biomes">Fully loaded biome registry; needed whole, not as ids, because the layer category is checked too.</param>
    /// <exception cref="ContentLoadException">
    /// On the first rule naming an unregistered biome, or one whose
    /// <see cref="BiomeDefinition.LayerCategory"/> is not
    /// <see cref="LayerCategory.Surface"/>. World types are visited in the
    /// registry's ordinal-sorted order, so the reported failure is the same on
    /// every machine.
    /// </exception>
    public static void ValidateDeferredReferences(
        Registry<WorldTypeDefinition> worldTypes, Registry<BiomeDefinition> biomes)
    {
        ArgumentNullException.ThrowIfNull(worldTypes);
        ArgumentNullException.ThrowIfNull(biomes);

        foreach (WorldTypeDefinition worldType in worldTypes)
        {
            IReadOnlyList<BiomeClassificationRule> rules = worldType.BiomeClassification.Rules;

            for (int i = 0; i < rules.Count; i++)
            {
                if (!biomes.TryGet(rules[i].Biome, out BiomeDefinition biome))
                {
                    throw new ContentLoadException(
                        $"World type '{worldType.Id}' biome_classification.rules[{i}] names biome " +
                        $"'{rules[i].Biome}', which is not a registered biome.");
                }

                if (biome.LayerCategory != LayerCategory.Surface)
                {
                    throw new ContentLoadException(
                        $"World type '{worldType.Id}' biome_classification.rules[{i}] names biome " +
                        $"'{biome.Id}', whose layer_category is '{biome.LayerCategory}' and not " +
                        "'surface'. Classification assigns the surface column; the underground " +
                        "layer follows from that biome's underground_variant, so naming a " +
                        "non-surface biome here would leave the underground unresolvable.");
                }
            }
        }
    }

    /// <summary>
    /// Checks the classification block that <see cref="Load"/> can check on its
    /// own: both octave stacks, the two shape knobs, and — the important one —
    /// that the rule rectangles cover the whole climate square. Biome ids are not
    /// resolved here; see <see cref="ValidateDeferredReferences"/>.
    /// </summary>
    private static void ValidateBiomeClassification(WorldTypeDefinition worldType)
    {
        BiomeClassificationConfig config = worldType.BiomeClassification;

        CheckClimateField(worldType, "temperature", config.Temperature);
        CheckClimateField(worldType, "humidity", config.Humidity);

        if (config.BlendColumns < 0)
        {
            throw new ContentLoadException(
                $"World type '{worldType.Id}' biome_classification.blend_columns is " +
                $"{config.BlendColumns}; it is the half-width in columns of the seam jitter and " +
                "cannot be negative.");
        }

        if (config.MinRunColumns < 1)
        {
            throw new ContentLoadException(
                $"World type '{worldType.Id}' biome_classification.min_run_columns is " +
                $"{config.MinRunColumns}; it is the shortest surviving run of one biome and must " +
                "be at least 1, because a run of zero columns does not exist.");
        }

        CheckRuleRectangles(worldType, config.Rules);
        CheckRulesCoverTheSquare(worldType, config.Rules);
    }

    /// <summary>
    /// One climate field's octave stack, delegated to
    /// <see cref="NoiseFieldConfig.ToFbmParameters"/> for the same reason
    /// <see cref="ValidateHeightmapOctaves"/> delegates: one definition of a valid
    /// octave stack, translated here into a message that names the data file.
    /// </summary>
    private static void CheckClimateField(
        WorldTypeDefinition worldType, string field, NoiseFieldConfig config)
    {
        try
        {
            config.ToFbmParameters();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ContentLoadException(
                $"World type '{worldType.Id}' biome_classification.{field} has an invalid octave " +
                $"stack: {ex.Message}");
        }
    }

    /// <summary>
    /// Every rectangle must sit inside the unit square with a non-empty span on
    /// both axes. Checked before coverage, because an inverted or out-of-range
    /// rectangle would also make the coverage sweep's answer meaningless — and
    /// "this range is backwards" points at the mistake, where "the square is not
    /// covered" sends the author hunting for a missing rule.
    /// </summary>
    private static void CheckRuleRectangles(
        WorldTypeDefinition worldType, IReadOnlyList<BiomeClassificationRule> rules)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(rules[i].Biome))
            {
                throw new ContentLoadException(
                    $"World type '{worldType.Id}' biome_classification.rules[{i}] names no biome.");
            }

            CheckRange(worldType, i, "temperature", rules[i].Temperature);
            CheckRange(worldType, i, "humidity", rules[i].Humidity);
        }
    }

    /// <summary>One rule axis: finite, inside [0, 1], and strictly ascending.</summary>
    private static void CheckRange(
        WorldTypeDefinition worldType, int index, string axis, UnitRange range)
    {
        if (!double.IsFinite(range.Min) || !double.IsFinite(range.Max)
            || range.Min < 0.0 || range.Max > 1.0 || range.Min >= range.Max)
        {
            throw new ContentLoadException(
                $"World type '{worldType.Id}' biome_classification.rules[{index}].{axis} is " +
                $"{range}; a climate range must lie within [0, 1] with min strictly below max. " +
                "The axes are normalised noise, so a bound outside [0, 1] describes a climate " +
                "that cannot occur.");
        }
    }

    /// <summary>
    /// Proves the rules leave <b>no gap</b> in the climate square, exactly rather
    /// than by sampling.
    ///
    /// <para>Because every rule is an axis-aligned rectangle, the distinct edge
    /// coordinates on each axis (plus 0 and 1) cut the square into a grid whose
    /// every cell is either wholly inside a rule or wholly outside all of them.
    /// Testing one interior point per cell is therefore a complete proof, not a
    /// sample: the grid has no finer structure for a gap to hide in. Midpoints
    /// are used so the answer never depends on whether a rule's edge is treated
    /// as inclusive.</para>
    ///
    /// <para>Overlap between rules is deliberately <i>not</i> an error — first
    /// match in authored order wins, and a narrow rule shadowing a broad fallback
    /// is normal authoring. A gap is an error, because the column it corresponds
    /// to has no biome and generation would have to invent one.</para>
    /// </summary>
    private static void CheckRulesCoverTheSquare(
        WorldTypeDefinition worldType, IReadOnlyList<BiomeClassificationRule> rules)
    {
        if (rules.Count == 0)
        {
            throw new ContentLoadException(
                $"World type '{worldType.Id}' declares no biome_classification.rules, so no column " +
                "of its surface could be classified.");
        }

        double[] temperatureEdges = CollectEdges(rules, static rule => rule.Temperature);
        double[] humidityEdges = CollectEdges(rules, static rule => rule.Humidity);

        for (int t = 0; t < temperatureEdges.Length - 1; t++)
        {
            double temperature = (temperatureEdges[t] + temperatureEdges[t + 1]) * 0.5;

            for (int h = 0; h < humidityEdges.Length - 1; h++)
            {
                double humidity = (humidityEdges[h] + humidityEdges[h + 1]) * 0.5;

                if (Covers(rules, temperature, humidity))
                {
                    continue;
                }

                throw new ContentLoadException(
                    $"World type '{worldType.Id}' biome_classification.rules leave the climate " +
                    $"region temperature [{temperatureEdges[t]}, {temperatureEdges[t + 1]}] x " +
                    $"humidity [{humidityEdges[h]}, {humidityEdges[h + 1]}] uncovered. The rules " +
                    "must tile the whole unit square: any column whose climate lands in a gap " +
                    "would have no biome, and generation fails rather than inventing one.");
            }
        }
    }

    /// <summary>
    /// The sorted, de-duplicated cut coordinates of one axis, always including
    /// both ends of the unit square so a rule set that stops short of 0 or 1
    /// leaves a cell rather than shrinking the square it is checked against.
    /// </summary>
    private static double[] CollectEdges(
        IReadOnlyList<BiomeClassificationRule> rules, Func<BiomeClassificationRule, UnitRange> axis)
    {
        SortedSet<double> edges = new() { 0.0, 1.0 };

        for (int i = 0; i < rules.Count; i++)
        {
            UnitRange range = axis(rules[i]);

            // Edges outside the square cannot cut it, and CheckRuleRectangles has
            // already rejected them; clamping here only keeps this pure geometry.
            edges.Add(Math.Clamp(range.Min, 0.0, 1.0));
            edges.Add(Math.Clamp(range.Max, 0.0, 1.0));
        }

        double[] sorted = new double[edges.Count];
        edges.CopyTo(sorted);
        return sorted;
    }

    /// <summary>Whether any rule claims one climate point; order is irrelevant to coverage.</summary>
    private static bool Covers(
        IReadOnlyList<BiomeClassificationRule> rules, double temperature, double humidity)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            if (rules[i].Matches(temperature, humidity))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Runs the real surface-band resolution for one preset. Uses
    /// <see cref="SurfaceBand.Compute"/> for the same reason
    /// <see cref="CheckNoZeroHeightLayer"/> uses the real boundary calculator:
    /// band fractions that leave no sky, or too few rows to shape terrain in,
    /// depend on the preset's height, and boot must check exactly the rule
    /// generation applies.
    /// </summary>
    private static void CheckSurfaceBandFits(WorldTypeDefinition worldType, WorldSizePreset preset)
    {
        LayerBoundaries b = LayerBoundaryCalculator.Compute(preset.HeightTiles, worldType.LayerProportions);

        try
        {
            SurfaceBand.Compute(b.OutsideEnd, worldType.Heightmap);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ContentLoadException(
                $"World type '{worldType.Id}' heightmap band at size preset '{preset.Id}' " +
                $"(outside layer {b.OutsideEnd} rows) is unusable: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks the presets themselves and, for each, that the proportions produce
    /// four layers of at least one row. The zero-height check runs per preset
    /// because it depends on height: a split that is fine at Large can vanish a
    /// layer at Small, and the failure must surface at boot rather than the
    /// first time someone generates a small world.
    ///
    /// <para><b>The surface-band check is a second pass, deliberately.</b> Both
    /// checks derive from the same layer proportions, so bad proportions trip
    /// both — and "this split vanishes a layer" points an author at the actual
    /// mistake, where "the heightmap band is unusable" sends them to tune a
    /// heightmap that was never the problem. Running every preset's geometry
    /// check before any preset's band check keeps the more fundamental error the
    /// one that gets reported.</para>
    /// </summary>
    private static void ValidateSizePresets(WorldTypeDefinition worldType)
    {
        if (worldType.SizePresets.Count == 0)
        {
            throw new ContentLoadException(
                $"World type '{worldType.Id}' declares no size_presets, so it can never be generated.");
        }

        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (WorldSizePreset preset in worldType.SizePresets)
        {
            if (string.IsNullOrWhiteSpace(preset.Id))
            {
                throw new ContentLoadException(
                    $"World type '{worldType.Id}' has a size preset with a missing or empty 'id'.");
            }

            if (!seen.Add(preset.Id))
            {
                throw new ContentLoadException(
                    $"World type '{worldType.Id}' declares size preset '{preset.Id}' twice.");
            }

            if (preset.WidthTiles <= 0 || preset.HeightTiles <= 0)
            {
                throw new ContentLoadException(
                    $"World type '{worldType.Id}' size preset '{preset.Id}' is " +
                    $"{preset.WidthTiles}x{preset.HeightTiles}; both extents must be positive.");
            }

            CheckNoZeroHeightLayer(worldType, preset);
        }

        foreach (WorldSizePreset preset in worldType.SizePresets)
        {
            CheckSurfaceBandFits(worldType, preset);
        }

        if (worldType.FindSizePreset(worldType.SizePreset) is null)
        {
            throw new ContentLoadException(
                $"World type '{worldType.Id}' names default size_preset '{worldType.SizePreset}', " +
                "which is not one of its declared size_presets.");
        }
    }

    /// <summary>
    /// Runs the real boundary computation for one preset and rejects any layer
    /// of zero rows. Uses <see cref="LayerBoundaryCalculator"/> rather than a
    /// separate arithmetic check, so validation cannot disagree with what
    /// generation will actually produce.
    /// </summary>
    private static void CheckNoZeroHeightLayer(WorldTypeDefinition worldType, WorldSizePreset preset)
    {
        LayerBoundaries b = LayerBoundaryCalculator.Compute(preset.HeightTiles, worldType.LayerProportions);

        // Strictly increasing, and the last boundary strictly above the world
        // floor: that is exactly "all four layers are at least one row tall".
        if (b.OutsideEnd > 0
            && b.UndergroundEnd > b.OutsideEnd
            && b.DeepEnd > b.UndergroundEnd
            && preset.HeightTiles > b.DeepEnd)
        {
            return;
        }

        throw new ContentLoadException(
            $"World type '{worldType.Id}' layer_proportions at size preset '{preset.Id}' " +
            $"(height {preset.HeightTiles}) produce boundaries {b.OutsideEnd}/{b.UndergroundEnd}/" +
            $"{b.DeepEnd}, which leaves at least one layer zero rows tall. Every layer must be " +
            "at least one row: generation places content per layer and would silently skip it.");
    }
}
