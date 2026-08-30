using System.Collections.Generic;

namespace Void;

/// <summary>
/// The prefab sets a biome scatters during decoration (VOID-022), per
/// world-data-model-spec §6.
///
/// Every list may legitimately be empty — barren biomes exist — so absence is
/// never an error. Lists are authored in file order and consumed by weighted
/// draw; generation must therefore seed that draw rather than depend on index.
/// </summary>
public sealed class BiomeVegetation
{
    /// <summary>Tree prefabs placed on the surface block.</summary>
    public IReadOnlyList<PrefabRef> Trees { get; init; } = [];

    /// <summary>Small ground plants — flowers, grass tufts, mushrooms.</summary>
    public IReadOnlyList<PrefabRef> Plants { get; init; } = [];

    /// <summary>Non-plant scatter: rocks, bones, debris.</summary>
    public IReadOnlyList<PrefabRef> Decorations { get; init; } = [];
}
