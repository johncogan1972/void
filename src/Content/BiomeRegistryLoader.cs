using System;
using System.Collections.Generic;

namespace Void;

/// <summary>
/// Boot-time loader for the biome registry (VOID-022).
///
/// Biome loading is deliberately two-step, and this type is the only way to
/// perform it. Half of what makes a biome valid is cross-registry — palette ids
/// must resolve against the block and wall registries, and a surface biome's
/// underground variant must name a real underground biome — so parsing alone
/// proves nothing. Callers therefore never invoke
/// <c>RegistryLoader.Load&lt;BiomeDefinition&gt;</c> directly: doing so would
/// yield a registry that looks loaded but was never checked. Every path here
/// parses and then validates before returning.
///
/// <para>Engine-free, like the rest of the content layer, so the whole
/// validation path is unit-testable with no Godot engine initialised.</para>
/// </summary>
public static class BiomeRegistryLoader
{
    /// <summary>
    /// Parses every biome document in <paramref name="source"/> and validates it
    /// against the already-loaded block and wall registries. Load blocks and
    /// walls first; biomes cannot be validated without them.
    /// </summary>
    /// <exception cref="ContentLoadException">
    /// On malformed JSON, a duplicate id, an unresolvable palette block or wall,
    /// an <c>underground_variant</c> naming a biome that does not exist, or one
    /// naming a biome that is not in the underground layer. All are fatal:
    /// generation reading a half-valid biome would produce a broken world
    /// rather than an error.
    /// </exception>
    public static Registry<BiomeDefinition> Load(
        IContentSource source,
        Registry<BlockDefinition> blocks,
        Registry<WallDefinition> walls)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(walls);

        Registry<BiomeDefinition> biomes = RegistryLoader.LoadUnvalidated<BiomeDefinition>(source);
        Validate(biomes, blocks, walls);
        return biomes;
    }

    /// <summary>
    /// Resolves the two reference kinds <see cref="Load"/> cannot: vegetation
    /// prefab ids and enemy pool ids. They close the one cycle in the content
    /// graph — biomes load before prefabs, because prefabs need only blocks and
    /// walls — so they are checked once everything is loaded.
    ///
    /// <para>Called by <see cref="ContentLoader.LoadAll"/> as the last step of
    /// boot (VOID-025). It is not optional and there is no warning-only mode: a
    /// dangling ref here is a biome that silently grows nothing or spawns
    /// nothing, which no later stage reports.</para>
    /// </summary>
    /// <param name="biomes">Registry returned by <see cref="Load"/>.</param>
    /// <param name="knownPrefabIds">Every registered prefab id. Lookup only, so its order is irrelevant.</param>
    /// <param name="knownEnemyIds">Every registered enemy id. Lookup only, so its order is irrelevant.</param>
    /// <exception cref="ContentLoadException">
    /// On the first unresolvable prefab or enemy id, naming the biome, the list
    /// and the missing id. Biomes are visited in the registry's ordinal-sorted
    /// order, so the *reported* failure is the same on every machine.
    /// </exception>
    public static void ValidateDeferredReferences(
        Registry<BiomeDefinition> biomes,
        IReadOnlyCollection<string> knownPrefabIds,
        IReadOnlyCollection<string> knownEnemyIds)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(knownPrefabIds);
        ArgumentNullException.ThrowIfNull(knownEnemyIds);

        HashSet<string> prefabs = new(knownPrefabIds, StringComparer.Ordinal);
        HashSet<string> enemies = new(knownEnemyIds, StringComparer.Ordinal);

        foreach (BiomeDefinition biome in biomes)
        {
            CheckPrefabs(biome, "vegetation.trees", biome.Vegetation.Trees, prefabs);
            CheckPrefabs(biome, "vegetation.plants", biome.Vegetation.Plants, prefabs);
            CheckPrefabs(biome, "vegetation.decorations", biome.Vegetation.Decorations, prefabs);

            foreach (BiomeEnemySpawn spawn in biome.Enemies)
            {
                if (!enemies.Contains(spawn.EnemyId))
                {
                    throw new ContentLoadException(
                        $"Biome '{biome.Id}' spawn pool names enemy '{spawn.EnemyId}', " +
                        "which is not a registered enemy.");
                }
            }
        }
    }

    /// <summary>
    /// Cross-registry checks run on every load. Private because a biome registry
    /// must never exist in an unvalidated state.
    /// </summary>
    private static void Validate(
        Registry<BiomeDefinition> biomes,
        Registry<BlockDefinition> blocks,
        Registry<WallDefinition> walls)
    {
        // Ordinal-sorted registry order, so the biome that gets blamed for a
        // multi-error data drop is the same one on every machine.
        foreach (BiomeDefinition biome in biomes)
        {
            CheckBlock(biome, "palette.surface_block", biome.Palette.SurfaceBlock, blocks);
            CheckBlock(biome, "palette.subsurface_block", biome.Palette.SubsurfaceBlock, blocks);
            CheckBlock(biome, "palette.base_block", biome.Palette.BaseBlock, blocks);
            CheckWall(biome, "palette.wall_default", biome.Palette.WallDefault, walls);

            for (int i = 0; i < biome.Palette.WallAmbient.Count; i++)
            {
                CheckWall(biome, $"palette.wall_ambient[{i}]", biome.Palette.WallAmbient[i], walls);
            }

            CheckUndergroundVariant(biome, biomes);
        }
    }

    /// <summary>
    /// Enforces the surface/underground pairing of spec §6: the named variant
    /// must exist, and must itself sit in the underground layer. Applied to any
    /// biome that declares a variant, not just surface ones — a deep or
    /// underground biome pointing at a surface biome is the same mistake.
    /// </summary>
    private static void CheckUndergroundVariant(BiomeDefinition biome, Registry<BiomeDefinition> biomes)
    {
        if (biome.UndergroundVariant is null)
        {
            return;
        }

        if (!biomes.TryGet(biome.UndergroundVariant, out BiomeDefinition variant))
        {
            throw new ContentLoadException(
                $"Biome '{biome.Id}' names underground_variant '{biome.UndergroundVariant}', " +
                "which is not a registered biome.");
        }

        if (variant.LayerCategory != LayerCategory.Underground)
        {
            throw new ContentLoadException(
                $"Biome '{biome.Id}' names underground_variant '{variant.Id}', whose " +
                $"layer_category is '{variant.LayerCategory}' and not 'underground'. " +
                "The underground layer generator places the variant directly beneath the " +
                "surface column, so it must be an underground biome.");
        }
    }

    /// <summary>Resolves one palette block id, or fails naming biome, field and id.</summary>
    private static void CheckBlock(
        BiomeDefinition biome, string field, string blockId, Registry<BlockDefinition> blocks)
    {
        if (!blocks.Contains(blockId))
        {
            throw new ContentLoadException(
                $"Biome '{biome.Id}' field '{field}' names block '{blockId}', " +
                "which is not a registered block.");
        }
    }

    /// <summary>Resolves one palette wall id, or fails naming biome, field and id.</summary>
    private static void CheckWall(
        BiomeDefinition biome, string field, string wallId, Registry<WallDefinition> walls)
    {
        if (!walls.Contains(wallId))
        {
            throw new ContentLoadException(
                $"Biome '{biome.Id}' field '{field}' names wall '{wallId}', " +
                "which is not a registered wall.");
        }
    }

    /// <summary>Resolves one vegetation list against the prefab id set.</summary>
    private static void CheckPrefabs(
        BiomeDefinition biome, string field, IReadOnlyList<PrefabRef> refs, HashSet<string> prefabs)
    {
        for (int i = 0; i < refs.Count; i++)
        {
            if (!prefabs.Contains(refs[i].Prefab))
            {
                throw new ContentLoadException(
                    $"Biome '{biome.Id}' field '{field}[{i}]' names prefab '{refs[i].Prefab}', " +
                    "which is not a registered prefab.");
            }
        }
    }
}
