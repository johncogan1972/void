using System;
using System.Collections.Generic;

namespace Void;

/// <summary>
/// Phase 2 step 7: walks Perlin worms through the world and produces the
/// <see cref="CaveNetwork"/> their tunnels carve (VOID-065,
/// <c>cave-generation-spec</c> §3).
///
/// <para><b>Deterministic.</b> Each worm derives its own generator from
/// <see cref="GenKeys.Phase2Caves"/> by index, so a worm's path is a function of
/// the seed and its index alone — adding a worm to a layer cannot move the worms
/// that were already there, and no worm depends on how many draws the worms
/// before it happened to make. Headings are integer indices into
/// <see cref="WormDirections"/>, so no trigonometry runs on this path.</para>
///
/// <para><b>What this step does not do.</b> It produces paths, not tiles.
/// Rasterising happens per chunk inside <see cref="CaveNetwork.CarveInto"/>, so
/// materialisation stays a pure function of the chunk coordinate — see that type
/// for why the split exists.</para>
///
/// <para>Engine-free: pure arithmetic over content config, so the whole step is
/// testable under <c>dotnet test</c>.</para>
/// </summary>
public static class WormCarver
{
    /// <summary>
    /// Walks every worm the world type asks for, across all four layers.
    /// </summary>
    /// <param name="context">
    /// Supplies the world type's <see cref="CaveConfig"/>, the world extents and
    /// the cave sub-stream. The stream is derived here and nowhere else.
    /// </param>
    /// <param name="boundaries">
    /// Layer boundaries from Phase 1, so each layer's worms spawn inside their
    /// own band. Passed rather than recomputed so this step and the manifest
    /// cannot describe two different worlds.
    /// </param>
    /// <returns>
    /// The carved network, or <see cref="CaveNetwork.Empty"/> if the world type
    /// configures no caves — a solid world is a legitimate configuration, not a
    /// failure.
    /// </returns>
    public static CaveNetwork Generate(GenerationContext context, LayerBoundaries boundaries)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(boundaries);

        CaveConfig? caves = context.WorldType.Caves;
        if (caves is null)
        {
            return CaveNetwork.Empty;
        }

        int widthTiles = context.SizePreset.WidthTiles;
        int heightTiles = context.SizePreset.HeightTiles;

        // Phase 1 output. Caves run after it, so the surface is known here — which
        // is what lets a worm be spawned in rock rather than in sky.
        Heightmap heightmap = context.Heightmap;

        Rng stream = context.Stream(GenKeys.Phase2Caves);
        FbmNoise direction = new(
            stream.Derive("worm_direction"), caves.WormDirectionNoise.ToFbmParameters());

        List<double> x = new();
        List<double> y = new();
        List<double> radius = new();

        // Layers in fixed top-to-bottom order. The order does not change any
        // worm's path -- each derives its own stream by name -- but it fixes the
        // order discs land in the arrays, which keeps a network built twice
        // identical rather than merely equivalent.
        foreach (WorldLayer layer in
            (ReadOnlySpan<WorldLayer>)[
                WorldLayer.Outside, WorldLayer.Underground, WorldLayer.Deep, WorldLayer.Void])
        {
            if (caves.For(layer) is not WormConfig config || config.WormsPer1000Columns <= 0.0)
            {
                continue;
            }

            (int topRow, int bottomRow) = LayerRows(layer, boundaries, heightTiles);
            if (bottomRow <= topRow)
            {
                continue;
            }

            // Rounded rather than truncated so a density that works out to a
            // fraction of a worm still produces one on a wide enough world
            // instead of silently producing none.
            int wormCount = (int)Math.Round(config.WormsPer1000Columns * widthTiles / 1000.0);

            for (int i = 0; i < wormCount; i++)
            {
                // Keyed by layer and index, so the worms of one layer are
                // untouched by how many another layer spawned.
                Rng wormRng = stream.Derive($"{layer}.{i}");

                int startX = wormRng.NextInt(widthTiles);

                // Never spawn above the ground. The outside layer is mostly sky —
                // spawning uniformly in it would put most of its worms in open
                // air, where they carve nothing and the layer reads as having no
                // caves at all. Starting below the surface row is also what makes
                // spec §6.1's "surface-opening tunnel mouths" possible: a worm
                // that begins in rock can break out through the surface, which one
                // beginning in the sky never does.
                int spawnTop = Math.Max(topRow, heightmap[startX] + 1);
                if (spawnTop > bottomRow)
                {
                    continue;
                }

                Walk(
                    wormRng, direction, config, config.StepCount, config.Radius, depth: 0,
                    startX: startX,
                    startY: wormRng.NextInt(spawnTop, bottomRow + 1),
                    startHeading: wormRng.NextInt(WormDirections.Count),
                    topRow: topRow, bottomRow: bottomRow,
                    widthTiles: widthTiles, heightTiles: heightTiles,
                    x: x, y: y, radius: radius);
            }
        }

        return new CaveNetwork(
            x.ToArray(), y.ToArray(), radius.ToArray(), widthTiles, heightTiles);
    }

    /// <summary>
    /// Walks one worm, stamping a disc per step and recursing for branches.
    /// </summary>
    /// <remarks>
    /// <para>A worm stops when it runs out of steps or leaves its layer's rows,
    /// and is clamped to the world horizontally rather than terminated — a worm
    /// that wandered off the left edge and died there would leave the world's
    /// edges systematically less caved than its middle.</para>
    ///
    /// <para><b>Spec §3.2's "terminate on collision with an existing tunnel" is
    /// deliberately not implemented.</b> Knowing what has already been carved
    /// means holding a world-sized carved mask while worms walk, which is exactly
    /// the global state the path-only design exists to avoid. Worms therefore
    /// pass through each other; the visible result is that intersections open out
    /// rather than dead-end, which is no worse and arguably better.</para>
    /// </remarks>
    private static void Walk(
        Rng rng,
        FbmNoise direction,
        WormConfig config,
        int steps,
        double wormRadius,
        int depth,
        int startX,
        int startY,
        int startHeading,
        int topRow,
        int bottomRow,
        int widthTiles,
        int heightTiles,
        List<double> x,
        List<double> y,
        List<double> radius)
    {
        // A turn rate below one heading step would round to zero and give a worm
        // that can only travel dead straight, which is never what an author who
        // typed a small number meant.
        int maxTurn = Math.Max(1, (int)(config.TurnRate / WormDirections.RadiansPerStep));

        double px = startX;
        double py = startY;

        // Heading is accumulated as a fraction of a step and quantised only for
        // the table lookup. Rounding each turn to a whole heading step instead
        // would throw away every turn smaller than one: fBm output clusters near
        // zero, so with a turn rate of 0.18 rad -- under four heading steps --
        // almost every step would round to no turn at all, and worms would run
        // dead straight between occasional sharp corners. Accumulating lets small
        // turns add up into the graceful bend spec §3.1 asks for.
        double heading = startHeading;
        int branches = 0;

        for (int step = 0; step < steps; step++)
        {
            // Sampled at the worm's position, so worms crossing the same rock
            // bend the same way and the network reads as following the ground
            // rather than as unrelated squiggles.
            heading += direction.Sample(px, py) * maxTurn;

            // Floor, not truncate: truncation rounds toward zero, which would
            // bias every worm travelling in a negative heading direction.
            int index = (int)Math.Floor(heading) & WormDirections.Mask;

            px += WormDirections.X(index) * config.StepLength;
            py += WormDirections.Y(index) * config.StepLength;

            // Horizontally the world wraps nothing, so a worm that reaches an
            // edge is turned back rather than lost.
            if (px < 0.0 || px > widthTiles - 1)
            {
                px = Math.Clamp(px, 0.0, widthTiles - 1);
                heading += WormDirections.Count / 2;
            }

            // Leaving the layer ends the worm: layers are tuned separately and a
            // deep worm surfacing into the outside layer would carve it at deep
            // density.
            if (py < topRow || py > bottomRow || py > heightTiles - 1)
            {
                return;
            }

            x.Add(px);
            y.Add(py);
            radius.Add(wormRadius);

            if (depth < config.MaxBranchDepth
                && branches < config.MaxBranches
                && rng.NextDouble() < config.BranchChance)
            {
                branches++;

                // A child is shorter and thinner than its parent, so a branch
                // reads as a side passage rather than as a fork between equals.
                Walk(
                    rng.Derive($"branch.{depth}.{branches}"),
                    direction,
                    config,
                    Math.Max(1, (int)(steps * config.BranchScale)),
                    wormRadius * config.BranchScale,
                    depth + 1,
                    (int)px,
                    (int)py,
                    // A quarter turn off the parent, so the branch leaves at a
                    // visible angle instead of shadowing the tunnel it came from.
                    (int)Math.Floor(heading) + (WormDirections.Count / 4),
                    topRow, bottomRow, widthTiles, heightTiles,
                    x, y, radius);
            }
        }
    }

    /// <summary>
    /// The inclusive row range a layer occupies.
    /// </summary>
    /// <remarks>
    /// Read from the same <see cref="LayerBoundaries"/> the manifest records, so
    /// "underground" here means exactly the rows the rest of the game calls
    /// underground.
    /// </remarks>
    private static (int TopRow, int BottomRow) LayerRows(
        WorldLayer layer, LayerBoundaries boundaries, int heightTiles) => layer switch
        {
            WorldLayer.Outside => (0, boundaries.OutsideEnd - 1),
            WorldLayer.Underground => (boundaries.OutsideEnd, boundaries.UndergroundEnd - 1),
            WorldLayer.Deep => (boundaries.UndergroundEnd, boundaries.DeepEnd - 1),
            _ => (boundaries.DeepEnd, heightTiles - 1),
        };
}
