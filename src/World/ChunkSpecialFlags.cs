using System;

namespace Void;

/// <summary>
/// Content hints for a chunk, stored as the metadata <c>special_flags</c> field
/// (VOID-020, world-data-model-spec §3).
///
/// Set by world generation and read by systems that need to find candidate
/// chunks without loading tile data — boss placement, portal siting, fishing and
/// liquid simulation warm-up.
///
/// These are <b>wire values</b> written to every chunk file and can never be
/// renumbered. The field is 32 bits, so bits 3-31 are free for later additions.
/// </summary>
[Flags]
public enum ChunkSpecialFlags : uint
{
    /// <summary>Nothing special about this chunk.</summary>
    None = 0,

    /// <summary>Holds a boss lair, or part of one.</summary>
    ContainsBossLair = 1 << 0,

    /// <summary>Viable site for a portal. A hint, not a reservation.</summary>
    ContainsPortalCandidate = 1 << 1,

    /// <summary>Holds a body of liquid large enough to matter to simulation.</summary>
    ContainsWaterBody = 1 << 2,
}
