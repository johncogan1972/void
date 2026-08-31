using System;

namespace Void;

/// <summary>
/// Everything a generation phase is allowed to read: the seed, the world's
/// resolved size, its world-type configuration and the loaded content registries
/// (VOID-046, world-generation-spec §6).
///
/// <para>Handed to every phase unchanged. Phases take their randomness from
/// <see cref="Stream"/>, never from a generator threaded in from the previous
/// phase — a threaded generator would make each phase's output depend on how
/// many draws every earlier phase happened to make, so adding one draw to the
/// heightmap would move every ore in the world.</para>
///
/// <para>Engine-free: no Godot types, so the whole pipeline is testable under
/// plain <c>dotnet test</c>.</para>
/// </summary>
public sealed class GenerationContext
{
    /// <summary>
    /// Builds a context, resolving the world type and size preset out of loaded
    /// content.
    /// </summary>
    /// <param name="content">Loaded, cross-validated registries; shared read-only.</param>
    /// <param name="worldTypeId">Id in <see cref="GameContent.WorldTypes"/>.</param>
    /// <param name="seed">
    /// World seed as stored in <see cref="WorldManifest.Seed"/>. Signed there
    /// and unsigned in <see cref="Rng"/>; reinterpreted with
    /// <c>unchecked((ulong)seed)</c>, matching
    /// <see cref="WorldManifest.SeedInput"/>, so the manifest, the save
    /// keystream and the generator all key off the same bits.
    /// </param>
    /// <param name="sizePresetId">
    /// Preset to generate at, or null for the world type's declared default.
    /// </param>
    /// <exception cref="ArgumentException">
    /// If the world type or size preset does not exist. Fatal rather than
    /// defaulted: silently generating a Medium world when the caller asked for
    /// Large would be discovered only after the world existed.
    /// </exception>
    public GenerationContext(
        GameContent content, string worldTypeId, long seed, string? sizePresetId = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(worldTypeId);

        if (!content.WorldTypes.TryGet(worldTypeId, out WorldTypeDefinition worldType))
        {
            throw new ArgumentException(
                $"'{worldTypeId}' is not a registered world type.", nameof(worldTypeId));
        }

        string presetId = sizePresetId ?? worldType.SizePreset;
        WorldSizePreset? preset = worldType.FindSizePreset(presetId);

        if (preset is null)
        {
            throw new ArgumentException(
                $"World type '{worldTypeId}' declares no size preset '{presetId}'.",
                nameof(sizePresetId));
        }

        Content = content;
        WorldType = worldType;
        SizePreset = preset;
        Seed = seed;
        Master = new Rng(unchecked((ulong)seed));
    }

    /// <summary>The loaded registries. Read-only; a phase must never mutate content.</summary>
    public GameContent Content { get; }

    /// <summary>Resolved world-type config: layer proportions and later phases' tuning.</summary>
    public WorldTypeDefinition WorldType { get; }

    /// <summary>The size this world is being generated at, already resolved to tile extents.</summary>
    public WorldSizePreset SizePreset { get; }

    /// <summary>The seed exactly as it will be written to the manifest.</summary>
    public long Seed { get; }

    /// <summary>
    /// Master generator. Present so <see cref="Stream"/> has a parent and so
    /// tests can inspect the seed; phases must not draw from it directly, or
    /// their output would depend on every other phase's draw count.
    /// </summary>
    public Rng Master { get; }

    /// <summary>
    /// The sub-stream for one <see cref="GenKeys"/> key. Returns a fresh
    /// generator each call — two calls with the same key give two generators
    /// that produce the same sequence, so a phase derives once and keeps it for
    /// the duration of that phase.
    /// </summary>
    public Rng Stream(string key) => Master.Derive(key);
}
