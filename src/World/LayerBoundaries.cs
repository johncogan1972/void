using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Row indices where each vertical band ends (world-data-model-spec §4).
///
/// Stored per world rather than hardcoded because layer proportions are
/// configurable per size preset and per portal-world theme. Each value is the
/// first row *below* that band, so a band is the half-open range
/// <c>[previous_end, this_end)</c> and row 0 is the top of the outside layer.
///
/// <b>The void has no explicit end</b>: it runs from <see cref="DeepEnd"/> to
/// <c>dimensions.height_tiles</c>. Anything looking for a fourth boundary field
/// is looking for something that does not exist by design — the world's height
/// is the end of the void.
///
/// Values must be strictly increasing and within the world's height; a world
/// whose boundaries overlap would assign tiles to two layers at once.
/// </summary>
/// <param name="OutsideEnd">First row of the underground layer.</param>
/// <param name="UndergroundEnd">First row of the deep layer.</param>
/// <param name="DeepEnd">First row of the void layer.</param>
public sealed record LayerBoundaries(
    [property: JsonPropertyOrder(0), JsonRequired] int OutsideEnd,
    [property: JsonPropertyOrder(1), JsonRequired] int UndergroundEnd,
    [property: JsonPropertyOrder(2), JsonRequired] int DeepEnd);
