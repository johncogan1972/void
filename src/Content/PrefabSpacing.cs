namespace Void;

/// <summary>
/// Minimum tile distance a prefab must keep from already-placed prefabs
/// (VOID-024), per world-data-model-spec §5's <c>constraints.min_spacing</c>.
///
/// Both fields default to <c>0</c>, which means "no spacing requirement" — a
/// missing <c>min_spacing</c> block must never be read as "cannot be placed".
/// Units are tiles, not chunks.
/// </summary>
public sealed class PrefabSpacing
{
    /// <summary>
    /// Distance kept from other prefabs sharing this one's
    /// <see cref="PrefabDefinition.Category"/>. Usually the larger of the two:
    /// three shrines in a row read worse than a shrine next to a ruin.
    /// JSON key <c>same_category</c>.
    /// </summary>
    public int SameCategory { get; init; }

    /// <summary>Distance kept from any prefab at all. JSON key <c>any_category</c>.</summary>
    public int AnyCategory { get; init; }
}
