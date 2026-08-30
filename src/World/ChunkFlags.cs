using System;

namespace Void;

/// <summary>
/// Chunk-level bit flags stored in the chunk header (VOID-020,
/// world-data-model-spec §3).
///
/// These are <b>wire values</b>: bit 0 will mean "modified" for the lifetime of
/// the save format. Renumbering one would silently reinterpret every chunk file
/// already on disk. New meanings go in the reserved range (bits 3-15).
/// </summary>
[Flags]
public enum ChunkFlags : ushort
{
    /// <summary>No flags set. A freshly generated, untouched chunk.</summary>
    None = 0,

    /// <summary>
    /// Modified since generation — dirty, and must be re-serialised on eviction.
    /// </summary>
    /// <remarks>
    /// The streaming eviction path saves only chunks carrying this bit; an
    /// untouched chunk was written once at generation time and is never
    /// rewritten (§3, "Save on eviction"). Failing to set it on a mutation
    /// silently loses the player's edits.
    /// </remarks>
    Modified = 1 << 0,

    /// <summary>
    /// Contains player-placed structures. Read by passes that must not disturb
    /// player builds, and by NPC housing validation.
    /// </summary>
    ContainsPlayerStructures = 1 << 1,

    /// <summary>
    /// Currently resident in the streaming set.
    /// </summary>
    /// <remarks>
    /// <b>Transient — this bit must never reach disk.</b> It describes the
    /// running process, not the world, so a chunk file carrying it would come
    /// back claiming to be loaded before anything loaded it.
    /// <see cref="Chunk.WriteTo"/> masks it off when writing the header; the
    /// in-memory chunk keeps it. Do not add a code path that serialises
    /// <see cref="Chunk.Flags"/> without that mask.
    /// </remarks>
    CurrentlyLoaded = 1 << 2,
}
