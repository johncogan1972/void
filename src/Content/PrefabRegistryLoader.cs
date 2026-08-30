using System;
using System.Collections.Generic;

namespace Void;

/// <summary>
/// Boot-time loader for the prefab registry (VOID-024).
///
/// A prefab's tile arrays hold raw numeric block and wall ids, so a document
/// that parses cleanly can still describe a structure made of tiles that do not
/// exist. This type is the only way to build the registry —
/// <c>RegistryLoader.Load&lt;PrefabDefinition&gt;</c> refuses the type outright
/// — so an unchecked prefab cannot reach world generation.
///
/// <para>Load blocks and walls first (VOID-018). Engine-free, like the rest of
/// the content layer, so the whole validation path is unit-testable with no
/// Godot engine initialised.</para>
/// </summary>
public static class PrefabRegistryLoader
{
    /// <summary>
    /// Parses every prefab document in <paramref name="source"/> and validates
    /// it against the already-loaded block and wall registries.
    /// </summary>
    /// <exception cref="ContentLoadException">
    /// On malformed JSON (which includes an unrecognised marker <c>type</c>), a
    /// duplicate id, a non-positive dimension, a tile array whose length is not
    /// <c>width * height</c>, a marker outside the footprint, a block or wall id
    /// that does not resolve, or a negative weight.
    ///
    /// <para>Every one of these is fatal, never a warning and never a skipped
    /// prefab: a structure stamped into the world from half-valid data is a
    /// corrupted world that looks like a design mistake, and a silently dropped
    /// prefab makes generation depend on which files happened to be clean.</para>
    /// </exception>
    public static Registry<PrefabDefinition> Load(
        IContentSource source,
        Registry<BlockDefinition> blocks,
        Registry<WallDefinition> walls)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(walls);

        Registry<PrefabDefinition> prefabs = RegistryLoader.LoadUnvalidated<PrefabDefinition>(source);
        Validate(prefabs, blocks, walls);
        return prefabs;
    }

    /// <summary>
    /// Cross-registry and internal-consistency checks, run on every load.
    /// Private because a prefab registry must never exist in an unvalidated
    /// state.
    /// </summary>
    private static void Validate(
        Registry<PrefabDefinition> prefabs,
        Registry<BlockDefinition> blocks,
        Registry<WallDefinition> walls)
    {
        // Ordinal-sorted registry order, so the prefab blamed for a multi-error
        // content drop is the same one on every machine.
        foreach (PrefabDefinition prefab in prefabs)
        {
            CheckDimensions(prefab);
            CheckTileArrayLength(prefab, "block_ids", prefab.BlockIds.Count);
            CheckTileArrayLength(prefab, "wall_ids", prefab.WallIds.Count);
            CheckMarkers(prefab);
            CheckTileIds(prefab, "block_id", prefab.BlockIds, id => blocks.TryGetByNumericId(id, out _));
            CheckTileIds(prefab, "wall_id", prefab.WallIds, id => walls.TryGetByNumericId(id, out _));
            CheckWeight(prefab);
        }
    }

    /// <summary>
    /// Rejects a zero or negative footprint. Checked before the array lengths
    /// because a 0-wide prefab has <c>width * height == 0</c> and would let an
    /// empty tile array pass the length check unchallenged.
    /// </summary>
    private static void CheckDimensions(PrefabDefinition prefab)
    {
        if (prefab.Width <= 0 || prefab.Height <= 0)
        {
            throw new ContentLoadException(
                $"Prefab '{prefab.Id}' has dimensions {prefab.Width}x{prefab.Height}; " +
                "width and height must both be greater than zero.");
        }
    }

    /// <summary>
    /// Proves a tile array is exactly <c>width * height</c> long. A short or
    /// long array is the schema's most dangerous error: nothing else notices
    /// until the structure is stamped, sheared by the wrong row stride.
    /// </summary>
    private static void CheckTileArrayLength(PrefabDefinition prefab, string field, int actual)
    {
        if (actual != prefab.TileCount)
        {
            throw new ContentLoadException(
                $"Prefab '{prefab.Id}' field '{field}' holds {actual} entries, but its " +
                $"dimensions {prefab.Width}x{prefab.Height} require exactly {prefab.TileCount}.");
        }
    }

    /// <summary>
    /// Proves every marker sits inside the footprint, naming the marker's index
    /// in the authored list so a large prefab is still diagnosable.
    /// </summary>
    private static void CheckMarkers(PrefabDefinition prefab)
    {
        for (int i = 0; i < prefab.Markers.Count; i++)
        {
            PrefabMarker marker = prefab.Markers[i];

            if ((uint)marker.X >= (uint)prefab.Width || (uint)marker.Y >= (uint)prefab.Height)
            {
                throw new ContentLoadException(
                    $"Prefab '{prefab.Id}' marker[{i}] ('{marker.Type}') is at " +
                    $"({marker.X},{marker.Y}), outside the prefab bounds " +
                    $"x [0,{prefab.Width}) y [0,{prefab.Height}).");
            }
        }
    }

    /// <summary>
    /// Resolves every entry of one tile array against its registry. Reports the
    /// flat index and the (x,y) it decodes to, because an author reading a
    /// row-major array cannot count to 900 in their head.
    /// </summary>
    /// <param name="resolves">
    /// Numeric-id probe for the owning registry. A delegate rather than two
    /// near-identical copies of this loop; it runs at load only, never in a hot
    /// path.
    /// </param>
    private static void CheckTileIds(
        PrefabDefinition prefab, string field, IReadOnlyList<ushort> ids, Func<ushort, bool> resolves)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (!resolves(ids[i]))
            {
                throw new ContentLoadException(
                    $"Prefab '{prefab.Id}' {field} at index {i} " +
                    $"(x={i % prefab.Width}, y={i / prefab.Width}) is {ids[i]}, " +
                    "which is not a registered numeric id.");
            }
        }
    }

    /// <summary>
    /// Rejects any weight that is not a finite, non-negative number.
    /// </summary>
    /// <remarks>
    /// Weighted selection sums these, so one bad entry corrupts every other
    /// variant's odds rather than just its own: a negative weight silently
    /// distorts the running total, and a single <c>NaN</c> or infinity makes the
    /// total non-finite so that <i>no</i> prefab is ever selected. The
    /// non-finite case needs an explicit test because the comparison does not
    /// catch it — <c>float.NaN &lt; 0f</c> is <c>false</c>.
    /// </remarks>
    private static void CheckWeight(PrefabDefinition prefab)
    {
        if (!float.IsFinite(prefab.Weight) || prefab.Weight < 0f)
        {
            throw new ContentLoadException(
                $"Prefab '{prefab.Id}' has weight {prefab.Weight}; weights are relative " +
                "and must be a finite value that is zero (disabled) or positive.");
        }
    }
}
