using System;
using System.Collections.Generic;

namespace Void;

/// <summary>
/// Boot-time loader for the world-type registry (VOID-046).
///
/// <para>World types name no ids in other registries, so this is not a
/// cross-registry loader — but it is still the only way in, because the half of
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
    /// preset.
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
            ValidateSizePresets(worldType);
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
