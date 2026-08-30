using System.Collections.Generic;

namespace Void;

/// <summary>
/// Where a prefab is allowed to be placed (VOID-024), per
/// world-data-model-spec §5.
///
/// Every field defaults to the permissive value — empty list, false, zero — so
/// a prefab that declares no <c>constraints</c> block at all is placeable
/// anywhere rather than nowhere. No property is ever null; the placement engine
/// reads these on its hot loop and must not null-check.
///
/// <para>Nothing here is cross-registry validated at load. Biome ids are
/// checked against the biome registry when the placement engine is wired up
/// (Phase 2); the prefab loader's job is the tile data, which is what a typo
/// corrupts silently.</para>
/// </summary>
public sealed class PrefabConstraints
{
    /// <summary>
    /// Biome ids this prefab may appear in. <b>Empty means any biome</b>, not
    /// "no biome" — an empty allow-list is how an unrestricted prefab is
    /// authored. JSON key <c>allowed_biomes</c>.
    /// </summary>
    public IReadOnlyList<string> AllowedBiomes { get; init; } = [];

    /// <summary>
    /// World layers this prefab may appear in; empty means any. Reuses
    /// <see cref="LayerCategory"/> rather than raw strings so a misspelled layer
    /// is a parse failure instead of a prefab that never places. JSON key
    /// <c>allowed_layers</c>.
    /// </summary>
    public IReadOnlyList<LayerCategory> AllowedLayers { get; init; } = [];

    /// <summary>Must sit on solid tiles — surface buildings, not floating ones. JSON key <c>requires_ground</c>.</summary>
    public bool RequiresGround { get; init; }

    /// <summary>Must sit inside already-carved-out space. JSON key <c>requires_cavern</c>.</summary>
    public bool RequiresCavern { get; init; }

    /// <summary>Minimum tile distance from other prefabs. JSON key <c>min_spacing</c>.</summary>
    public PrefabSpacing MinSpacing { get; init; } = new PrefabSpacing();

    /// <summary>
    /// Empty tiles required directly above the footprint, in tiles. Zero means
    /// none. Large lairs use it so a cathedral does not get a ceiling one tile
    /// over its spire. JSON key <c>clearance_above</c>.
    /// </summary>
    public int ClearanceAbove { get; init; }
}
