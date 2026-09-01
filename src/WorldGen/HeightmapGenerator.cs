using System;
using System.Collections.Generic;

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
/// <para>The surface is two fields, not one: a low-frequency base shape mapped
/// onto the band, plus an optional <see cref="SurfaceDetailConfig"/> displacement
/// in rows on top (VOID-061). They are separate because fBm trades hill amplitude
/// for roughness, so rolling hills and tile-scale texture cannot both be asked of
/// one octave stack. Each draws from its own <see cref="GenKeys"/> stream.</para>
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

		// Roughness is per biome, which is why this step runs after classification
		// (VOID-061). One resolved field per biome id, not per column: a world is
		// thousands of columns over a handful of biomes, so resolving inside the
		// loop would be thousands of registry lookups to answer three questions.
		BiomeMap biomeMap = context.BiomeMap;
		Registry<BiomeDefinition> biomes = context.Content.Biomes;
		Rng detailRoot = context.Stream(GenKeys.Phase1HeightmapDetail);
		Dictionary<string, DetailField?> detailFields = new(StringComparer.Ordinal);

		int[] surfaceY = new int[context.SizePreset.WidthTiles];

		for (int x = 0; x < surfaceY.Length; x++)
		{
			DetailField? field = ResolveDetail(
				detailFields, biomeMap[x], biomes, config, detailRoot);

			double detailRows = Displace(field, x);

			// Inside a transition band, crossfade the two biomes' roughness rather
			// than dithering it (VOID-060). Roughness is a continuous quantity and the
			// surface is a single row per column, so picking one biome's displacement
			// or the other's at random would read as noise where a gradient is wanted
			// -- and would put a one-row cliff wherever the choice flipped. The weight
			// runs 0 -> 0.5 -> 0 across the band while the column's own biome changes
			// at its centre, which makes the crossfade continuous through the seam.
			if (biomeMap.BlendBiomeAt(x) is string blendId)
			{
				double weight = biomeMap.BlendWeightAt(x);
				DetailField? blendField =
					ResolveDetail(detailFields, blendId, biomes, config, detailRoot);

				detailRows = ((1.0 - weight) * detailRows) + (weight * Displace(blendField, x));
			}

			surfaceY[x] = MapIntoBand(noise.SampleUnit(x), detailRows, band);
		}

		LimitSlope(surfaceY, config.MaxColumnDelta, band);
		return new Heightmap(surfaceY, band);
	}

	/// <summary>
	/// One biome's roughness displacement at a column, in rows, or 0 where that
	/// biome is smooth.
	/// </summary>
	/// <remarks>
	/// <see cref="FbmNoise.SampleUnit(double)"/> is [0, 1]; it is re-centred to
	/// [-1, 1] so the field displaces the surface both ways rather than only
	/// downwards, which would raise the whole world as a side effect of roughening
	/// it.
	/// </remarks>
	private static double Displace(DetailField? field, int x) =>
		field is null ? 0.0 : ((field.Noise.SampleUnit(x) * 2.0) - 1.0) * field.AmplitudeRows;

	/// <summary>
	/// One biome's resolved roughness: the field to sample and how many rows it
	/// may displace the surface by. Resolved once per biome and reused for every
	/// column that biome owns.
	/// </summary>
	/// <param name="Noise">The biome's own detail field.</param>
	/// <param name="AmplitudeRows">Peak displacement in rows; always above zero here.</param>
	private sealed record DetailField(FbmNoise Noise, double AmplitudeRows);

	/// <summary>
	/// The roughness field for one biome, or null when that biome is smooth.
	/// </summary>
	/// <remarks>
	/// <para>A biome's own <see cref="BiomeDefinition.SurfaceDetail"/> wins; absent
	/// that, the world type's <see cref="HeightmapConfig.Detail"/> applies. So a
	/// world type sets the house style and a biome overrides it only where it
	/// genuinely differs.</para>
	///
	/// <para><b>Each biome samples its own decorrelated field.</b> The stream is
	/// derived per biome id, so Frostreach's drifts are not Meadow's bumps at a
	/// different amplitude — which is what they would be if every biome shared one
	/// field. <see cref="Rng.Derive"/> is a pure function of seed and key and does
	/// not advance its parent, so the order biomes are first encountered in cannot
	/// affect any of them.</para>
	///
	/// <para>An unknown biome id resolves to the world-type default rather than
	/// throwing: the biome map is built from this same registry a step earlier, so
	/// it cannot contain one, and a throw here would be unreachable code guarding
	/// an invariant that is already held.</para>
	/// </remarks>
	private static DetailField? ResolveDetail(
		Dictionary<string, DetailField?> cache,
		string biomeId,
		Registry<BiomeDefinition> biomes,
		HeightmapConfig config,
		Rng detailRoot)
	{
		if (cache.TryGetValue(biomeId, out DetailField? cached))
		{
			return cached;
		}

		SurfaceDetailConfig? detail = biomes.TryGet(biomeId, out BiomeDefinition biome)
			? biome.SurfaceDetail ?? config.Detail
			: config.Detail;

		// Absent, or zero rows, both mean "smooth" -- the term is additive, so
		// either way this biome generates exactly the pre-VOID-061 surface.
		DetailField? field = detail is null || detail.AmplitudeRows <= 0.0
			? null
			: new DetailField(
				new FbmNoise(detailRoot.Derive(biomeId), detail.ToFbmParameters()),
				detail.AmplitudeRows);

		cache[biomeId] = field;
		return field;
	}

	/// <summary>
	/// Maps one [0, 1] noise sample, plus any detail displacement, onto an
	/// inclusive row range. The multiply is by <see cref="SurfaceBand.RowCount"/>
	/// with the top of the range clamped off, which gives every row an equal
	/// share of the input interval; scaling by <c>RowCount - 1</c> instead would
	/// make the two extreme rows half as likely as the rest.
	/// </summary>
	/// <param name="detailRows">
	/// Signed row displacement from <see cref="SurfaceDetailConfig"/>, or 0 when
	/// no detail is configured. Added <b>before</b> the floor and the clamp, so
	/// it can move a column across a row boundary — which is the entire point:
	/// the staircase is where a smooth ramp crosses those boundaries at even
	/// intervals.
	/// </param>
	private static int MapIntoBand(double unitSample, double detailRows, SurfaceBand band)
	{
		int offset = (int)Math.Floor((unitSample * band.RowCount) + detailRows);
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
