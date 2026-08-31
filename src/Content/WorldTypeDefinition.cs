using System.Collections.Generic;

namespace Void;

/// <summary>
/// Definition of one world template — the home world, or a portal-world theme —
/// holding the generation parameters that are decided before any phase runs
/// (VOID-046, world-generation-spec §4-§6).
///
/// <para>This is the seam that keeps world shape data-driven: layer proportions
/// and the available size presets are per world type, so a portal world with a
/// token sky and a vast void is a JSON entry, not a code branch. Later phases'
/// tuning (cave density, ore rates) belongs here too as those phases land.</para>
///
/// <para><b>Load through <see cref="WorldTypeRegistryLoader"/>, never
/// <c>RegistryLoader.Load&lt;WorldTypeDefinition&gt;</c>.</b> The generic path
/// only proves the JSON parsed; proportions that do not sum to 1, or that
/// squash a layer to zero rows at some preset, parse perfectly and generate a
/// broken world. It deliberately does <i>not</i> implement
/// <see cref="ICrossRegistryValidated"/>, because it names no ids in other
/// registries — its validation is entirely self-contained.</para>
/// </summary>
public sealed class WorldTypeDefinition : IContentDefinition
{
    /// <summary>
    /// Stable id, e.g. <c>void:home</c>. Written verbatim into
    /// <see cref="WorldManifest.WorldType"/>, so a saved world names it forever:
    /// retire ids, never repurpose them.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-facing name, for world-creation UI and debug overlays.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Vertical split of the world's height. Defaults to the spec §4 30/25/30/15
    /// only so a partially written entry has something to fail against — a real
    /// entry always states all four, and omitting one fails the sum check.
    /// </summary>
    public LayerProportions LayerProportions { get; init; } = LayerProportions.Default;

    /// <summary>
    /// Surface-elevation tuning for Phase 1 step 2: the octave stack and the
    /// slice of the Outside layer the surface may occupy. Defaults to
    /// <see cref="HeightmapConfig.Default"/> so an entry written before this
    /// existed still generates, but terrain shape is design and belongs in the
    /// data file, not in that default.
    /// </summary>
    public HeightmapConfig Heightmap { get; init; } = HeightmapConfig.Default;

    /// <summary>
    /// Every size this world type may be generated at. Order is authoring order
    /// and carries no meaning; lookups go through
    /// <see cref="FindSizePreset"/> by id.
    /// </summary>
    public IReadOnlyList<WorldSizePreset> SizePresets { get; init; } = [];

    /// <summary>
    /// Id within <see cref="SizePresets"/> used when the caller does not name
    /// one. Must resolve; the loader fails otherwise, because a world type whose
    /// default size does not exist cannot generate at all.
    /// </summary>
    public string SizePreset { get; init; } = string.Empty;

    /// <summary>
    /// Resolves a preset id, or null when this world type does not offer it.
    /// Ordinal comparison, like every other id comparison in the content layer,
    /// so the answer never depends on the machine's culture.
    /// </summary>
    public WorldSizePreset? FindSizePreset(string presetId)
    {
        for (int i = 0; i < SizePresets.Count; i++)
        {
            if (string.Equals(SizePresets[i].Id, presetId, System.StringComparison.Ordinal))
            {
                return SizePresets[i];
            }
        }

        return null;
    }
}
