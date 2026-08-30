using System;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// A tile coordinate qualified by the world it is in
/// (world-data-model-spec §4).
///
/// Distinct from <see cref="TilePosition"/> on purpose: anything that can move
/// between worlds — a placed side anchor, the party's active hearth — is
/// meaningless without its world id, and the campaign spans many worlds. Using
/// the wrong one of the two types then fails to compile rather than teleporting
/// a player into the wrong world.
/// </summary>
/// <param name="WorldId">
/// The world this point lives in. Resolvable through
/// <see cref="CampaignManifest.Worlds"/>; a world id with no campaign entry is a
/// broken save, not an empty result.
/// </param>
/// <param name="X">Tile column within that world.</param>
/// <param name="Y">Tile row within that world.</param>
public sealed record WorldPosition(
    [property: JsonPropertyOrder(0), JsonRequired] Guid WorldId,
    [property: JsonPropertyOrder(1), JsonRequired] int X,
    [property: JsonPropertyOrder(2), JsonRequired] int Y);
