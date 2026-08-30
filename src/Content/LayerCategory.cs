namespace Void;

/// <summary>
/// Which world layer a biome belongs to (VOID-022), per world-data-model-spec
/// §6 and world-generation-spec §4.
///
/// Serialised as a snake_case string (<c>"surface"</c>, <c>"underground"</c>,
/// <c>"deep"</c>, <c>"void"</c>) like <see cref="BlockCollision"/>, so data
/// files stay readable and reordering members can never silently repoint
/// existing content at a different layer.
/// </summary>
public enum LayerCategory
{
    /// <summary>Open-air layer: the surface biomes players spawn into.</summary>
    Surface = 0,

    /// <summary>Layer directly beneath the surface; paired to a surface biome.</summary>
    Underground = 1,

    /// <summary>Standalone deep layer — no surface pairing (biome-content-spec §5).</summary>
    Deep = 2,

    /// <summary>The lowest layer, and portal-world void themes.</summary>
    Void = 3,
}
