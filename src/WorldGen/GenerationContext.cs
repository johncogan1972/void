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
/// <para>It also carries <i>phase output</i> that later phases read — the
/// heightmap (VOID-047) and the surface biome map (VOID-048). Those members are the only mutable state here,
/// and they follow one rule: set once, by the phase that owns them, before any
/// later phase runs, and reading one that is unset throws.</para>
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
    /// Phase output, once <see cref="SetHeightmap"/> has run. Null only in the
    /// window before Phase 1 step 2; <see cref="Heightmap"/> is the accessor
    /// every consumer uses, so nothing outside this type ever sees the null.
    /// </summary>
    private Heightmap? _heightmap;

    /// <summary>
    /// Phase 1's surface elevation per column, read by macro features, biome
    /// classification and structure placement.
    ///
    /// <para><b>The rule for phase output on this context:</b> written once, by
    /// the phase that owns it, before any later phase runs; read-only
    /// thereafter. Reading it before it is set throws rather than returning null
    /// or an empty map, because a phase that quietly generated against a flat
    /// world-of-zeros would produce a world that looks generated and is
    /// wrong.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">If the heightmap phase has not run yet.</exception>
    public Heightmap Heightmap =>
        _heightmap ?? throw new InvalidOperationException(
            "The heightmap has not been generated yet. Phase 1 step 2 must run before any phase "
            + "that reads the surface; see world-generation-spec §6 for the phase order.");

    /// <summary>
    /// Records the generated heightmap. Called by
    /// <see cref="HeightmapGenerator"/>'s phase step and by nothing else.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// If a heightmap is already set. Fatal rather than an overwrite: two phases
    /// each believing they own the surface is a pipeline-ordering bug, and the
    /// second write would silently discard whatever the first produced.
    /// </exception>
    public void SetHeightmap(Heightmap heightmap)
    {
        ArgumentNullException.ThrowIfNull(heightmap);

        if (_heightmap is not null)
        {
            throw new InvalidOperationException(
                "The heightmap is already set. It is written once, by the phase that owns it.");
        }

        _heightmap = heightmap;
    }

    /// <summary>
    /// Phase output, once <see cref="SetBiomeMap"/> has run. Null only in the
    /// window before Phase 1 step 4; <see cref="BiomeMap"/> is the accessor every
    /// consumer uses, so nothing outside this type ever sees the null.
    /// </summary>
    private BiomeMap? _biomeMap;

    /// <summary>
    /// Phase 1's surface biome per column, read by terrain composition,
    /// vegetation, spawn pools and — through
    /// <see cref="Void.BiomeMap.UndergroundBiomeAt"/> — the underground layer.
    ///
    /// <para>Same rule as <see cref="Heightmap"/>: written once, by the phase
    /// that owns it, before any later phase runs; read-only thereafter. Reading
    /// it early throws rather than returning null, because a phase that quietly
    /// composed a world with no biomes would produce a world that looks
    /// generated and is wrong.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">If the biome map phase has not run yet.</exception>
    public BiomeMap BiomeMap =>
        _biomeMap ?? throw new InvalidOperationException(
            "The biome map has not been generated yet. Phase 1 step 4 must run before any phase "
            + "that reads biomes; see world-generation-spec §6 for the phase order.");

    /// <summary>
    /// Records the generated biome map. Called by <see cref="BiomeClassifier"/>'s
    /// phase step and by nothing else.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// If a biome map is already set. Fatal rather than an overwrite: two phases
    /// each believing they own biome assignment is a pipeline-ordering bug, and
    /// the second write would silently discard the first.
    /// </exception>
    public void SetBiomeMap(BiomeMap biomeMap)
    {
        ArgumentNullException.ThrowIfNull(biomeMap);

        if (_biomeMap is not null)
        {
            throw new InvalidOperationException(
                "The biome map is already set. It is written once, by the phase that owns it.");
        }

        _biomeMap = biomeMap;
    }

    /// <summary>
    /// The sub-stream for one <see cref="GenKeys"/> key. Returns a fresh
    /// generator each call — two calls with the same key give two generators
    /// that produce the same sequence, so a phase derives once and keeps it for
    /// the duration of that phase.
    /// </summary>
    public Rng Stream(string key) => Master.Derive(key);
}
