using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// One site chosen during generation phase 4 where a side anchor may sit
/// (world-data-model-spec §4).
///
/// The list of candidates is generation output and fixed for the world's life;
/// which of them are occupied and activated is runtime state held in
/// <see cref="SideAnchor"/>, keyed back here by <see cref="Id"/>.
/// </summary>
/// <param name="Id">
/// Candidate slot id, unique within one world. A <c>ushort</c> to match the
/// spec's <c>uint16</c>; it is the join key <see cref="SideAnchor.CandidateId"/>
/// points at, so it must never be reused within a world.
/// </param>
/// <param name="X">Tile column of the candidate site.</param>
/// <param name="Y">Tile row of the candidate site.</param>
public sealed record PortalCandidate(
    [property: JsonPropertyOrder(0), JsonRequired] ushort Id,
    [property: JsonPropertyOrder(1), JsonRequired] int X,
    [property: JsonPropertyOrder(2), JsonRequired] int Y);
