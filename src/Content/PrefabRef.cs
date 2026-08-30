namespace Void;

/// <summary>
/// A weighted reference to a prefab from a biome's vegetation lists
/// (VOID-022), per world-data-model-spec §6.
/// </summary>
public sealed class PrefabRef
{
    /// <summary>
    /// Prefab content id, e.g. <c>void:oak_small</c>. JSON key <c>prefab</c>.
    /// Not resolved at biome load time. The prefab registry exists
    /// (<see cref="PrefabRegistryLoader"/>, VOID-024), but the boot sequence
    /// that would hand it to the biome loader is VOID-025, so these refs still
    /// dangle until then; see
    /// <c>BiomeRegistryLoader.ValidateDeferredReferences</c>.
    /// </summary>
    public string Prefab { get; init; } = string.Empty;

    /// <summary>
    /// Relative selection weight within its list. Weights are relative, not
    /// probabilities: they are not required to sum to 1, and a list is chosen
    /// from by weighted draw. Must be positive to have any effect; 0 disables
    /// the entry without deleting it.
    /// </summary>
    public float Weight { get; init; }
}
