using System;

namespace Void;

/// <summary>
/// Per-tile bit flags (VOID-019, world-data-model-spec §2).
///
/// These are <b>wire values</b> stored in the packed <see cref="Tile"/> and
/// written to save files. A bit's meaning is fixed forever: reusing a retired
/// bit would silently reinterpret every existing world. Add new meanings in the
/// reserved range instead.
///
/// The field is 16 bits wide, so bits 5–15 are still free.
/// </summary>
[Flags]
public enum TileFlags : ushort
{
    /// <summary>No flags set. The default state of a generated tile.</summary>
    None = 0,

    /// <summary>
    /// Placed by a player rather than by generation.
    /// </summary>
    /// <remarks>
    /// Load-bearing beyond bookkeeping: player-placed tiles must survive any
    /// regeneration or corrective pass, and several rules (NPC housing validity,
    /// anti-cheese checks) treat built structures differently from natural ones.
    /// </remarks>
    PlayerPlaced = 1 << 0,

    /// <summary>
    /// Scratch bit owned by world generation for temporary state.
    /// </summary>
    /// <remarks>
    /// Must be cleared before a chunk is serialised — a generation temp bit that
    /// reaches disk is a bug, not a state. Nothing at runtime may read it.
    /// </remarks>
    GenerationTemp = 1 << 1,

    /// <summary>Carries a wire (post-MVP tech).</summary>
    Wire = 1 << 2,

    /// <summary>
    /// Part of a placed prefab, and must not be overwritten by later generation
    /// passes. This is how structures survive ore veins and cave carving that
    /// run after placement.
    /// </summary>
    PartOfPrefab = 1 << 3,

    /// <summary>
    /// Structural — supports adjacent tiles (post-MVP structural integrity).
    /// </summary>
    Structural = 1 << 4,
}
