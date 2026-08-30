using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Definition backing one hand-authored structure (VOID-024), per
/// world-data-model-spec §5 — the <c>PrefabRegistry</c> of §7.
///
/// A prefab is a fixed rectangle of tile data plus the constraints that say
/// where it may be stamped and the markers that say what still has to be filled
/// in. Authored in Tiled, exported to this JSON shape; adding one is a data-only
/// change.
///
/// <para><b>Load through <see cref="PrefabRegistryLoader"/>, never
/// <c>RegistryLoader.Load&lt;PrefabDefinition&gt;</c>.</b> The type is
/// <see cref="ICrossRegistryValidated"/> and the generic loader refuses it: the
/// tile arrays hold raw numeric <c>block_id</c>/<c>wall_id</c> values, which
/// parse fine no matter what they are and resolve to nothing if wrong. Only the
/// dedicated loader, holding the block and wall registries, can prove
/// otherwise.</para>
///
/// <para>Unlike blocks and walls, a prefab has no numeric id: nothing in the
/// save format stores a prefab by number, so there is no stable-forever
/// numbering to defend.</para>
/// </summary>
public sealed class PrefabDefinition : ICrossRegistryValidated
{
    /// <summary>
    /// Stable unique string id, e.g. <c>void:ruin_stone_small</c>. JSON key
    /// <c>prefab_id</c> as spec §5 spells it, mapped onto the registry's
    /// <c>Id</c> the same way <see cref="BlockDefinition.NumericId"/> maps
    /// <c>block_id</c>.
    /// </summary>
    [JsonPropertyName("prefab_id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Grouping the placement engine filters and spaces by — <c>"ruin"</c>,
    /// <c>"shrine"</c>, <c>"boss_lair"</c>. Free-form on purpose: categories are
    /// content decisions, and <see cref="PrefabSpacing.SameCategory"/> only ever
    /// compares them for ordinal equality.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Tile footprint. Both dimensions must be &gt; 0; enforced at load.</summary>
    public PrefabDimensions Dimensions { get; init; } = new PrefabDimensions();

    /// <summary>Placement rules. Never null; an absent block means "no restrictions".</summary>
    public PrefabConstraints Constraints { get; init; } = new PrefabConstraints();

    /// <summary>
    /// Foreground and background tile data, row-major, one entry per tile.
    /// JSON keys <c>block_ids</c> / <c>wall_ids</c>.
    ///
    /// <para><b>The stride is this prefab's own <see cref="Width"/> — not the
    /// 64-wide chunk stride.</b> A prefab is an arbitrary rectangle that has
    /// nothing to do with chunk geometry, so indexing one of these arrays with a
    /// chunk stride produces a plausible-looking but scrambled structure and no
    /// error anywhere. Always go through <see cref="TileIndex"/> rather than
    /// writing the multiply out.</para>
    ///
    /// <para>Values are raw numeric block/wall ids; both arrays must be exactly
    /// <c>Width * Height</c> long and every entry must resolve in the
    /// corresponding registry. <see cref="PrefabRegistryLoader"/> proves both,
    /// fatally.</para>
    /// </summary>
    public IReadOnlyList<ushort> BlockIds { get; init; } = [];

    /// <inheritdoc cref="BlockIds"/>
    public IReadOnlyList<ushort> WallIds { get; init; } = [];

    /// <summary>
    /// Special tiles for the placement engine. May be empty — a plain decorative
    /// structure needs none. Every marker's coordinates are proven in-bounds at
    /// load.
    /// </summary>
    public IReadOnlyList<PrefabMarker> Markers { get; init; } = [];

    /// <summary>
    /// Relative probability when the generator picks between variants of the
    /// same thing. Relative, not a probability: weights need not sum to 1.
    /// <c>0</c> disables an entry without deleting it; negative is a fatal load
    /// error, since it has no meaning in a weighted draw and would corrupt the
    /// running total.
    /// </summary>
    public float Weight { get; init; }

    /// <summary>Tile width, and the row stride of the tile arrays.</summary>
    [JsonIgnore]
    public int Width => Dimensions.Width;

    /// <summary>Tile height.</summary>
    [JsonIgnore]
    public int Height => Dimensions.Height;

    /// <summary>Number of entries each tile array must hold.</summary>
    [JsonIgnore]
    public int TileCount => Dimensions.Width * Dimensions.Height;

    /// <summary>
    /// Row-major index of tile-local <paramref name="x"/>,<paramref name="y"/>
    /// into <see cref="BlockIds"/> and <see cref="WallIds"/>.
    ///
    /// <para>The one place the <c>y * Width + x</c> stride is written. Callers
    /// that hand-roll it eventually reach for the chunk's 64, which silently
    /// reads the wrong tile; the bounds check here turns that class of mistake
    /// into an exception instead.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If the coordinates fall outside the prefab. Out of range is always a bug
    /// in the caller, never legitimate data, so it throws rather than clamping.
    /// </exception>
    public int TileIndex(int x, int y)
    {
        if ((uint)x >= (uint)Dimensions.Width)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x), x, $"Prefab '{Id}' is {Dimensions.Width} tiles wide.");
        }

        if ((uint)y >= (uint)Dimensions.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(y), y, $"Prefab '{Id}' is {Dimensions.Height} tiles tall.");
        }

        return (y * Dimensions.Width) + x;
    }
}
