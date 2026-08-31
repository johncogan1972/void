namespace Void;

/// <summary>
/// JSON-facing tuning for Phase 2 step 5, terrain materialisation (VOID-056,
/// world-generation-spec §6).
///
/// <para>Materialisation turns Phase 1's per-column arrays into actual tiles,
/// and the only thing about that it cannot read off the heightmap or the biome
/// palette is how thick the subsurface band should be. That number is terrain
/// design, so it lives in data — this class is where the world type states it,
/// exactly as <see cref="HeightmapConfig"/> states the octave stack.</para>
///
/// <para><b>This is the fallback, not the whole story.</b> Depth is authored
/// per biome on <see cref="BiomeDefinition.SubsurfaceDepth"/>, because a band
/// of snow over ice reads nothing like a band of dirt under grass. A biome that
/// declares no depth of its own uses the value here, so a new biome generates
/// sanely with no world-type edit.</para>
/// </summary>
public sealed class TerrainConfig
{
    /// <summary>
    /// Used when a world type omits the <c>terrain</c> block entirely, so an
    /// entry written before this existed still generates. A real entry states
    /// the value, because band thickness is terrain design and belongs in the
    /// data file rather than in this default.
    /// </summary>
    public static TerrainConfig Default { get; } = new TerrainConfig();

    /// <summary>
    /// Rows of <c>subsurface_block</c> placed beneath the surface block for
    /// biomes that declare no depth of their own. JSON key
    /// <c>default_subsurface_depth</c>.
    ///
    /// <para>Four rows is roughly a player's height of topsoil: thick enough to
    /// read as a distinct band once a cave clips the surface, thin enough that
    /// early digging reaches stone quickly. Zero is legal and means the surface
    /// block sits directly on the base block.</para>
    ///
    /// <para><b>Changing this regenerates the near-surface of every existing
    /// seed</b>, with no code change — which is the point of it being data, and
    /// the reason not to nudge it casually.</para>
    /// </summary>
    public int DefaultSubsurfaceDepth { get; init; } = 4;
}
