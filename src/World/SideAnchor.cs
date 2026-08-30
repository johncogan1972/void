using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Runtime state of one side anchor: the portal component a party finds in a
/// world, clears a gate for, carries, and eventually places
/// (world-data-model-spec §4, GDD §4).
///
/// Lives in the manifest of the world the anchor was *generated* in, not the
/// world it currently sits in, so the generating world always knows which of its
/// candidate slots have been consumed. Where it physically is now is
/// <see cref="PlacedAt"/>, which may name a different world entirely.
/// </summary>
/// <param name="CandidateId">
/// Which <see cref="PortalCandidate.Id"/> slot this anchor occupies. The join
/// back to generation output; must match a candidate in the same manifest.
/// </param>
/// <param name="Activated">True once the gate or mini boss guarding it is cleared.</param>
/// <param name="PickedUpBy">
/// The player carrying it, or null while it is still in the world.
/// <b>Null and absent are not the same thing</b> — see
/// <see cref="ManifestJson.Options"/>; null is written explicitly.
/// </param>
/// <param name="PlacedAt">
/// Where the anchor was installed, or null if it has never been placed.
/// Non-null with a non-null <see cref="PickedUpBy"/> would mean an anchor both
/// carried and installed, which is not a reachable state.
/// </param>
public sealed record SideAnchor(
    [property: JsonPropertyOrder(0), JsonRequired] ushort CandidateId,
    [property: JsonPropertyOrder(1), JsonRequired] bool Activated,
    [property: JsonPropertyOrder(2), JsonRequired] PlayerId? PickedUpBy,
    [property: JsonPropertyOrder(3), JsonRequired] WorldPosition? PlacedAt);
