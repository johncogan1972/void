using System.Collections.Generic;

namespace Void;

/// <summary>
/// The tile palette a biome generates from (VOID-022), per
/// world-data-model-spec §6.
///
/// <para><b>String content ids, not numbers.</b> Spec §6 sketches these fields
/// as <c>uint16</c>, i.e. raw <c>block_id</c>/<c>wall_id</c> values. They are
/// deliberately string ids here (<c>"void:grass"</c>), resolved through the
/// block and wall registries at load time, matching spec §7's registry pattern,
/// biome-content-spec §8, and how every other content file already
/// cross-references. Numbers in hand-authored data are unreadable and silently
/// wrong when mistyped; a string id that does not resolve is a fatal load error
/// naming the biome. Do not "fix" this back to numbers — the numeric ids stay
/// an internal, save-format concern.</para>
///
/// <para>Every id here is validated by <see cref="BiomeRegistryLoader"/>
/// against the block and wall registries; a dangling ref is fatal.</para>
/// </summary>
public sealed class BiomePalette
{
    /// <summary>Top block of the surface column — grass, sand, snow. JSON key <c>surface_block</c>.</summary>
    public string SurfaceBlock { get; init; } = string.Empty;

    /// <summary>Block in the band just below the surface. JSON key <c>subsurface_block</c>.</summary>
    public string SubsurfaceBlock { get; init; } = string.Empty;

    /// <summary>Bulk fill for the layer beneath the subsurface band. JSON key <c>base_block</c>.</summary>
    public string BaseBlock { get; init; } = string.Empty;

    /// <summary>Background wall placed by default. JSON key <c>wall_default</c>.</summary>
    public string WallDefault { get; init; } = string.Empty;

    /// <summary>
    /// Wall variations sprinkled stochastically over <see cref="WallDefault"/>.
    /// May be empty, which means "use the default wall everywhere". JSON key
    /// <c>wall_ambient</c>.
    /// </summary>
    public IReadOnlyList<string> WallAmbient { get; init; } = [];
}
