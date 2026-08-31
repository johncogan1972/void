using System;

namespace Void;

/// <summary>
/// Owns the ordering of the five generation phases and produces the world's
/// <see cref="WorldManifest"/> (VOID-046, world-generation-spec §6).
///
/// <para>Only Phase 1's structural metadata exists today — layer boundaries and
/// dimensions. The phase ordering and the sub-stream convention are the point of
/// this scaffold: every later phase derives its own stream from
/// <see cref="GenerationContext.Stream"/> using a <see cref="GenKeys"/> constant,
/// so phases can be written, reordered or run in isolation without changing what
/// any other phase generates.</para>
///
/// <para><b>Deterministic.</b> Given the same content, seed, world type and size
/// preset this produces byte-identical output — no clock, no
/// <c>System.Random</c>, no <c>Guid.NewGuid</c>. World identity is therefore an
/// <i>input</i> (see <see cref="Generate"/>), not something generation invents.</para>
///
/// <para>Engine-free: no Godot types anywhere on this path.</para>
/// </summary>
public static class WorldGenerator
{
    /// <summary>
    /// Recorded into <see cref="WorldManifest.GenVersion"/>. Bump it whenever a
    /// change makes an existing seed generate a different world, so a save can
    /// be told apart from one this build would reproduce.
    /// </summary>
    public const string GenVersion = "0.1.0";

    /// <summary>
    /// Stand-in prefab id for manifest fields that phases 2-5 will fill. Not a
    /// registered prefab on purpose: anything that tries to resolve it fails
    /// loudly instead of quietly placing the wrong structure.
    /// </summary>
    public const string UnassignedPrefabId = "void:unassigned";

    /// <summary>
    /// Runs the pipeline and returns the world's manifest.
    /// </summary>
    /// <param name="context">Seed, dimensions, world-type config and registries.</param>
    /// <param name="worldId">
    /// Identity of the world being created. An input rather than a fresh
    /// <c>Guid</c>: generation must be reproducible, and the campaign that owns
    /// the world is what decides its id anyway.
    /// </param>
    public static WorldManifest Generate(GenerationContext context, Guid worldId)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Phase 1 — structural. Steps 1 and 3 of spec §6 are all that exists:
        // the master stream lives on the context, and the layer boundaries are
        // computed here. Steps 2 and 4 (heightmap, biome map) and phases 2-5
        // will each derive their own stream from the context by GenKeys key;
        // none of them may thread a generator in from the phase before.
        WorldDimensions dimensions = ComputeDimensions(context.SizePreset);
        LayerBoundaries boundaries =
            LayerBoundaryCalculator.Compute(dimensions.HeightTiles, context.WorldType.LayerProportions);

        return new WorldManifest
        {
            WorldId = worldId,
            WorldType = context.WorldType.Id,
            Seed = context.Seed,
            GenVersion = GenVersion,
            SizePreset = context.SizePreset.Id,
            Dimensions = dimensions,
            LayerBoundaries = boundaries,

            // Populated by phase 4 (spec §6, steps 11 and 12); placeholders
            // until then. They are required members of the manifest, so they
            // must hold *something* — these values are deliberately not
            // plausible spawn output: row 0 is the top of the sky, and the lair
            // prefab id resolves to nothing.
            PlayerSpawn = new TilePosition(0, 0),
            MainBossLair = new BossLair(0, 0, UnassignedPrefabId),
        };
    }

    /// <summary>
    /// Tile extents straight from the preset, with chunk counts rounded
    /// <b>up</b>: a world height that is not a whole number of chunks still
    /// needs the last, partly-used chunk row to exist. Medium (6400x1800) gives
    /// 100 x 29 chunks, the edge-padded count of spec §5.
    /// </summary>
    private static WorldDimensions ComputeDimensions(WorldSizePreset preset) =>
        new WorldDimensions(
            preset.WidthTiles,
            preset.HeightTiles,
            CeilDiv(preset.WidthTiles, Chunk.Width),
            CeilDiv(preset.HeightTiles, Chunk.Height));

    /// <summary>Integer division rounding up. Both arguments are positive here.</summary>
    private static int CeilDiv(int value, int divisor) => (value + divisor - 1) / divisor;
}
