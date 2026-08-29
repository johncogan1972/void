namespace Void.Tests;

/// <summary>
/// Minimal string-keyed content definition used as a test fixture.
///
/// Originally <c>src/Content/ExampleDefinition.cs</c>, shipped with VOID-006 to
/// prove the registry mechanism end to end. VOID-018 landed the first real
/// registries (blocks and walls), so the example no longer needs to ship — but
/// the generic <see cref="Registry{T}"/> tests still need a definition type that
/// is <b>not</b> an <see cref="INumericContentDefinition"/>, to cover the plain
/// string-keyed path and the numeric-lookup rejection. It lives here now,
/// compiled only into the test assembly.
/// </summary>
public sealed class SampleDefinition : IContentDefinition
{
    /// <summary>Stable unique id, e.g. <c>void:sample_stone</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-facing name. JSON key <c>display_name</c>.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Arbitrary numeric payload, to prove non-string fields round-trip.</summary>
    public int SortOrder { get; init; }
}
