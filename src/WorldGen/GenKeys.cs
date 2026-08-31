using System.Collections.Generic;

namespace Void;

/// <summary>
/// Every RNG sub-stream key used by world generation, in one place
/// (world-generation-spec §6, Phase 1 step 1).
///
/// <para>Keys follow <c>phase&lt;n&gt;.&lt;subsystem&gt;</c> and are fed to
/// <see cref="Rng.Derive"/>, which is pure in (world seed, key): deriving a
/// stream never advances the parent, so phases may derive their streams in any
/// order, at any time, without changing what any other stream produces.</para>
///
/// <para><b>Never spell a key inline.</b> A typo does not fail — it derives a
/// perfectly valid stream and produces a different-but-plausible world, and
/// nothing downstream can tell that apart from correct output. Constants here
/// are also, once a key has shipped, effectively part of the save format:
/// changing a key's text regenerates that subsystem differently for every
/// existing seed.</para>
/// </summary>
public static class GenKeys
{
    // Phase 1 — structural (spec §6, steps 2 and 4).
    public const string Phase1Heightmap = "phase1.heightmap";
    public const string Phase1BiomeMap = "phase1.biome_map";

    // Phase 2 — terrain shaping (steps 6-8). Step 5, terrain materialisation,
    // has no key on purpose: it is a pure function of the heightmap, the biome
    // map and the biome palettes, so there is nothing for a stream to decide.
    // See TerrainMaterializer.
    public const string Phase2MacroFeatures = "phase2.macro_features";
    public const string Phase2Caves = "phase2.caves";
    public const string Phase2Water = "phase2.water";

    // Phase 3 — composition (steps 9-11).
    public const string Phase3Ores = "phase3.ores";
    public const string Phase3Vegetation = "phase3.vegetation";
    public const string Phase3Structures = "phase3.structures";

    // Phase 4 — reservations and metadata (steps 12-15).
    public const string Phase4PlayerSpawn = "phase4.player_spawn";
    public const string Phase4BossLair = "phase4.boss_lair";
    public const string Phase4PortalCandidates = "phase4.portal_candidates";

    // Phase 5 — validation and polish (steps 16-17).
    public const string Phase5PostProcess = "phase5.post_process";

    /// <summary>
    /// Every key above, in phase order. Exists so tests can assert the whole set
    /// at once — that no two keys collide, and that deriving them in reverse
    /// yields the same streams. Reading order is documentation only; nothing in
    /// generation may depend on it.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Phase1Heightmap,
        Phase1BiomeMap,
        Phase2MacroFeatures,
        Phase2Caves,
        Phase2Water,
        Phase3Ores,
        Phase3Vegetation,
        Phase3Structures,
        Phase4PlayerSpawn,
        Phase4BossLair,
        Phase4PortalCandidates,
        Phase5PostProcess,
    ];
}
