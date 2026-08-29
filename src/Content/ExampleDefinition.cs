namespace Void;

/// <summary>
/// Minimal worked example of a content definition (VOID-006).
///
/// Exists to exercise and document the registry mechanism end to end; the real
/// registries (blocks, biomes, items, loot tables) arrive in Phase 1 and are
/// deliberately not part of this ticket. Sample data lives in
/// <c>res://data/example/</c>.
/// </summary>
public sealed class ExampleDefinition : IContentDefinition
{
    /// <summary>Stable unique id, e.g. <c>void:example_stone</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-facing name. JSON key <c>display_name</c>.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Arbitrary numeric payload, to prove non-string fields round-trip.</summary>
    public int SortOrder { get; init; }
}
