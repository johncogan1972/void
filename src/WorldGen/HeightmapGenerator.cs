using System;

namespace Void;

/// <summary>
/// Phase 1 step 2: multi-octave 1D noise across the world width, mapped into the
/// Outside layer's surface band (VOID-047, world-generation-spec §6).
///
/// <para><b>Deterministic.</b> The field comes from
/// <see cref="GenKeys.Phase1Heightmap"/>'s sub-stream and is sampled statelessly
/// — no draw happens per column, so column order cannot affect the result, and
/// the map is identical on every machine. All arithmetic is <c>double</c> and
/// avoids the transcendental functions banned by spec §14.1.</para>
///
/// <para>Engine-free: pure arithmetic over content config, so the whole step is
/// testable under <c>dotnet test</c>.</para>
/// </summary>
public static class HeightmapGenerator
{
    /// <summary>
    /// Generates the surface for a whole world.
    /// </summary>
    /// <param name="context">
    /// Supplies the world type's <see cref="HeightmapConfig"/>, the width, and
    /// the heightmap sub-stream. The stream is derived here and nowhere else, so
    /// this step's output cannot depend on how many draws any other phase made.
    /// </param>
    /// <param name="boundaries">
    /// Already-computed layer boundaries; only <see cref="LayerBoundaries.OutsideEnd"/>
    /// is read. Passed in rather than recomputed so this step and the manifest
    /// cannot end up describing two different worlds.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If the world type's heightmap config is unusable — bad octaves, bad band
    /// fractions, or a non-positive <c>max_column_delta</c>. Content load
    /// normally catches these first; reaching here means a config was built in
    /// code, and failing is still better than generating a broken surface.
    /// </exception>
    public static Heightmap Generate(GenerationContext context, LayerBoundaries boundaries)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(boundaries);

        HeightmapConfig config = context.WorldType.Heightmap;
        SurfaceBand band = SurfaceBand.Compute(boundaries.OutsideEnd, config);

        if (config.MaxColumnDelta < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(context), config.MaxColumnDelta,
                "max_column_delta must be at least 1; a cap of 0 would flatten the world to one row.");
        }

        FbmNoise noise = new(context.Stream(GenKeys.Phase1Heightmap), config.ToFbmParameters());
        int[] surfaceY = new int[context.SizePreset.WidthTiles];

        for (int x = 0; x < surfaceY.Length; x++)
        {
            surfaceY[x] = MapIntoBand(noise.SampleUnit(x), band);
        }

        LimitSlope(surfaceY, config.MaxColumnDelta, band);
        return new Heightmap(surfaceY, band);
    }

    /// <summary>
    /// Maps one [0, 1] noise sample onto an inclusive row range. The multiply is
    /// by <see cref="SurfaceBand.RowCount"/> with the top of the range clamped
    /// off, which gives every row an equal share of the input interval; scaling
    /// by <c>RowCount - 1</c> instead would make the two extreme rows half as
    /// likely as the rest.
    /// </summary>
    private static int MapIntoBand(double unitSample, SurfaceBand band)
    {
        int offset = (int)Math.Floor(unitSample * band.RowCount);
        return band.MinRow + Math.Clamp(offset, 0, band.RowCount - 1);
    }

    /// <summary>
    /// Clamps every column to within <paramref name="maxDelta"/> rows of its
    /// left-hand neighbour, in a <b>single left-to-right pass</b>.
    ///
    /// <para>Why a limiter rather than trusting the noise: the octave stack is
    /// authored in JSON, so nothing stops a world type from asking for a
    /// frequency that puts a full-band swing between adjacent columns. Relying
    /// on the field being smooth would make the bound a property of the data
    /// file, testable only for the values that happen to ship. The limiter makes
    /// it a property of the code, true for any config that loads.</para>
    ///
    /// <para><b>Direction is load-bearing.</b> Left to right, column 0 taken as
    /// authoritative: each column is clamped against the already-final value to
    /// its left, so one pass is enough to guarantee the bound everywhere. A
    /// right-to-left pass would guarantee the same bound and produce different
    /// terrain, so the direction is part of the world's identity — reversing it
    /// regenerates every seed.</para>
    /// </summary>
    private static void LimitSlope(int[] surfaceY, int maxDelta, SurfaceBand band)
    {
        for (int x = 1; x < surfaceY.Length; x++)
        {
            int previous = surfaceY[x - 1];

            // The clamp window is intersected with the band, which is redundant
            // today (both ends are already inside it) but keeps the invariant
            // local: this loop can never be the thing that pushes a column out.
            int low = Math.Max(band.MinRow, previous - maxDelta);
            int high = Math.Min(band.MaxRow, previous + maxDelta);

            surfaceY[x] = Math.Clamp(surfaceY[x], low, high);
        }
    }
}
