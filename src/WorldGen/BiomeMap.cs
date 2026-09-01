using System;

namespace Void;

/// <summary>
/// Phase 1's surface biome assignment: one biome id per world column (VOID-048,
/// world-generation-spec §6, Phase 1 step 4).
///
/// <para>A plain per-column array, mirroring <see cref="Heightmap"/>, for the
/// same reason: later phases read it column by column — palette selection,
/// vegetation, enemy pools and structure placement all ask "what biome is this
/// column".</para>
///
/// <para><b>Ids are stored as strings, not indices.</b> An index would be into
/// the biome registry, whose ordering is stable within a build but is not a
/// save-format guarantee; a string cannot silently mean a different biome after
/// a content drop reorders the registry. Width-scale arrays of references cost
/// tens of kilobytes on the largest preset, which is nothing next to the chunk
/// data, and this map is regenerated from the seed rather than serialised.</para>
///
/// <para><b>There is no second map for the underground layer.</b> Spec §6 pairs
/// the underground with the surface column directly above it, so underground
/// biome is a <i>function</i> of this map — see
/// <see cref="UndergroundBiomeAt"/>. Storing a parallel array would create a
/// second source of truth that could drift out of step with this one.</para>
///
/// <para><b>A column can be part-way between two biomes</b> (VOID-060). The
/// column's own id is still exactly one biome — everything that asks "what biome
/// is this column" gets a single answer, and the run-length rule still holds —
/// but near a boundary the column also carries the biome on the other side and a
/// weight, and <see cref="BiomeAt(int, int)"/> picks between them <i>per
/// tile</i>. That is what turns a seam from a line into an interleaved band, and
/// it is why the choice lives here rather than in the materialiser: the
/// randomness belongs to this phase's stream, so materialisation stays a pure
/// function of the chunk coordinate with no stream of its own.</para>
///
/// <para>Immutable once built. A phase that wants to change biome assignment
/// produces a new instance from the old one.</para>
/// </summary>
public sealed class BiomeMap
{
    /// <summary>
    /// Multiplier applied to the wander field before it is clamped into the
    /// band. fBm's output clusters around the middle of its range, so an
    /// unscaled field would sweep only the innermost part of a band and leave its
    /// outer columns permanently unblended — the failure that made an earlier
    /// attempt collapse back to a nearly hard seam.
    /// </summary>
    private const double WanderGain = 2.2;

    /// <summary>
    /// Biome id per column, indexed by x. Copied in on construction so the
    /// caller's buffer cannot alias — otherwise "immutable" would depend on the
    /// caller's manners.
    /// </summary>
    private readonly string[] _biomeIds;

    /// <summary>
    /// The biome on the other side of the nearest boundary, per column, or null
    /// where the column is not inside a transition band. Parallel to
    /// <see cref="_biomeIds"/>.
    /// </summary>
    private readonly string?[] _blendIds;

    /// <summary>
    /// Where the column sits in its band, as a signed fraction: -1 at the left
    /// edge, 0 on the boundary, +1 at the right edge. 0 outside any band.
    ///
    /// <para>Signed rather than a distance because the sign is what says which
    /// side of the nominal boundary the column is on, and the whole per-tile
    /// decision is a comparison against that.</para>
    /// </summary>
    private readonly double[] _blendOffsets;

    /// <summary>
    /// The column of the boundary this column's band belongs to. Used to sample
    /// the wander field, so every boundary reads a different slice of it and two
    /// borders in one world do not wander in step.
    /// </summary>
    private readonly int[] _boundaryColumns;

    /// <summary>
    /// The field that displaces the boundary from row to row, or null when no
    /// column is blended.
    ///
    /// <para><b>The boundary wanders; tiles are not chosen independently.</b> Two
    /// mechanisms were tried and rejected before this one. Hashing each
    /// coordinate gives every tile its own answer, which is white noise: the band
    /// comes out as salt-and-pepper speckle and reads as a rendering fault.
    /// Thresholding a coherent field against a per-column probability clumps
    /// correctly but collapses the band — fBm concentrates near its midpoint, so
    /// the low probabilities near a band's edge are almost never met and the
    /// outer band never blends at all.</para>
    ///
    /// <para>Displacing the boundary instead makes every row have exactly one
    /// clean edge, in a different place. The edge sweeps the full width of the
    /// band because the displacement is scaled to it, and the two biomes
    /// interlock as fingers with no islands and no speckle — which is what a
    /// border between two places actually looks like.</para>
    /// </summary>
    private readonly FbmNoise? _wanderField;

    /// <summary>
    /// Wraps a fully populated column array.
    /// </summary>
    /// <param name="biomeIds">
    /// One biome id per column, length equal to the world width. Every entry must
    /// be a non-empty id; an empty or null entry is the signature of a column
    /// that no classification rule claimed, which must never reach a later phase
    /// as a silent blank.
    /// </param>
    /// <exception cref="ArgumentException">If the array is empty or any column holds a blank id.</exception>
    public BiomeMap(string[] biomeIds)
        : this(
            biomeIds,
            new string?[biomeIds?.Length ?? 0],
            new double[biomeIds?.Length ?? 0],
            new int[biomeIds?.Length ?? 0],
            null)
    {
    }

    /// <summary>
    /// Wraps a column array together with the transition band around each
    /// boundary (VOID-060).
    /// </summary>
    /// <param name="biomeIds">One biome id per column; see the other constructor.</param>
    /// <param name="blendIds">
    /// Per column, the biome on the other side of the nearest boundary, or null
    /// outside a band. Same length as <paramref name="biomeIds"/>.
    /// </param>
    /// <param name="blendOffsets">
    /// Per column, where it sits in its band as a signed fraction in [-1, 1].
    /// 0 outside a band.
    /// </param>
    /// <param name="boundaryColumns">
    /// Per column, the boundary its band belongs to. Ignored outside a band.
    /// </param>
    /// <param name="wanderField">
    /// The field that displaces the boundary per row, seeded from the
    /// classifier's stream so two worlds with the same seed interlock identically
    /// and two different seeds do not. Null means nothing is blended.
    /// </param>
    /// <exception cref="ArgumentException">
    /// If the arrays disagree in length, or a weight is outside [0, 1]. Both are
    /// caller bugs that would otherwise show up as terrain rather than as an
    /// error.
    /// </exception>
    public BiomeMap(
        string[] biomeIds,
        string?[] blendIds,
        double[] blendOffsets,
        int[] boundaryColumns,
        FbmNoise? wanderField)
    {
        ArgumentNullException.ThrowIfNull(biomeIds);
        ArgumentNullException.ThrowIfNull(blendIds);
        ArgumentNullException.ThrowIfNull(blendOffsets);
        ArgumentNullException.ThrowIfNull(boundaryColumns);

        if (biomeIds.Length == 0)
        {
            throw new ArgumentException("A biome map needs at least one column.", nameof(biomeIds));
        }

        for (int x = 0; x < biomeIds.Length; x++)
        {
            if (string.IsNullOrWhiteSpace(biomeIds[x]))
            {
                throw new ArgumentException(
                    $"Column {x} has no biome id. Every column must be classified; an unclassified "
                    + "column means the world type's rules do not cover the climate square.",
                    nameof(biomeIds));
            }
        }

        if (blendIds.Length != biomeIds.Length
            || blendOffsets.Length != biomeIds.Length
            || boundaryColumns.Length != biomeIds.Length)
        {
            throw new ArgumentException(
                "The blend arrays must have one entry per column, like the biome ids.",
                nameof(blendIds));
        }

        for (int x = 0; x < blendOffsets.Length; x++)
        {
            if (!double.IsFinite(blendOffsets[x]) || blendOffsets[x] < -1.0 || blendOffsets[x] > 1.0)
            {
                throw new ArgumentException(
                    $"Column {x} has blend offset {blendOffsets[x]}; it is a signed fraction of "
                    + "the band and must be in [-1, 1].",
                    nameof(blendOffsets));
            }
        }

        _biomeIds = (string[])biomeIds.Clone();
        _blendIds = (string?[])blendIds.Clone();
        _blendOffsets = (double[])blendOffsets.Clone();
        _boundaryColumns = (int[])boundaryColumns.Clone();
        _wanderField = wanderField;
    }

    /// <summary>Number of columns, always the world's width in tiles.</summary>
    public int Count => _biomeIds.Length;

    /// <summary>
    /// Surface biome id of one column.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If <paramref name="x"/> is outside [0, <see cref="Count"/>). Loud rather
    /// than wrapped or clamped, because an off-by-one at the world edge would
    /// otherwise return a plausible biome and hide the bug.
    /// </exception>
    public string this[int x]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(x);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, _biomeIds.Length);
            return _biomeIds[x];
        }
    }

    /// <summary>
    /// The underground biome for a column: the
    /// <see cref="BiomeDefinition.UndergroundVariant"/> of the surface biome
    /// directly above it (spec §6).
    ///
    /// <para>Resolved on demand from this map rather than stored, so the surface
    /// map stays the single source of truth for what a column is. The registry is
    /// a parameter rather than a field because this type holds no content
    /// references; callers already have <c>GenerationContext.Content.Biomes</c>
    /// in hand.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="x"/> is outside the world.</exception>
    /// <exception cref="InvalidOperationException">
    /// If the surface biome is not registered, or declares no underground
    /// variant. Both are load-time invariants — the classifier only writes ids
    /// that resolved at boot — so reaching either here means the registry the
    /// caller passed is not the one the map was generated against, and filling
    /// the underground with a guess would bury the mistake.
    /// </exception>
    public string UndergroundBiomeAt(int x, Registry<BiomeDefinition> biomes)
    {
        ArgumentNullException.ThrowIfNull(biomes);

        string surfaceId = this[x];

        if (!biomes.TryGet(surfaceId, out BiomeDefinition surface))
        {
            throw new InvalidOperationException(
                $"Column {x} is biome '{surfaceId}', which is not in the biome registry passed here.");
        }

        return surface.UndergroundVariant ?? throw new InvalidOperationException(
            $"Surface biome '{surfaceId}' declares no underground_variant, so the underground layer "
            + $"beneath column {x} cannot be resolved.");
    }

    /// <summary>
    /// The biome on the other side of the nearest boundary, or null if this
    /// column is not inside a transition band.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="x"/> is outside the world.</exception>
    public string? BlendBiomeAt(int x)
    {
        ThrowIfOutside(x);
        return _blendIds[x];
    }

    /// <summary>
    /// How strongly this column is mixed with <see cref="BlendBiomeAt"/>: the
    /// probability one of its tiles takes the other biome. 0 outside a band,
    /// rising to 0.5 on the boundary itself.
    /// </summary>
    /// <remarks>
    /// Also read as a plain interpolation factor by
    /// <see cref="HeightmapGenerator"/>, which crossfades the two biomes'
    /// surface roughness across the band rather than dithering it — roughness is
    /// a continuous quantity, so mixing it stochastically would produce noise
    /// where a gradient is wanted.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="x"/> is outside the world.</exception>
    public double BlendWeightAt(int x)
    {
        ThrowIfOutside(x);
        return _blendIds[x] is null ? 0.0 : 0.5 * (1.0 - Math.Abs(_blendOffsets[x]));
    }

    /// <summary>
    /// Whether the tile at (<paramref name="x"/>, <paramref name="y"/>) takes the
    /// blend biome rather than the column's own.
    /// </summary>
    /// <remarks>
    /// <para>Deterministic and stateless: a sample of a seeded field at the
    /// tile's coordinate, not a draw from a generator. That is what lets
    /// materialisation stay a pure function of the chunk coordinate — a chunk
    /// re-materialised an hour later, or on another machine, interleaves
    /// identically without anyone having to replay the same sequence of
    /// draws.</para>
    ///
    /// <para><b>The field is sampled in two dimensions on purpose.</b> Were it
    /// sampled by column alone, every tile in a column would make the same choice
    /// and the band would be a set of full-height stripes — a seam made of wider
    /// seams. Sampling by row as well makes the boundary ragged vertically too,
    /// which is what stops it reading as a straight cut through the world.</para>
    ///
    /// <para>The threshold is the weight itself, so a column mixed at 0.5 takes
    /// the blend wherever the field falls below its midpoint — about half its
    /// tiles, in coherent patches — and a column at the band's edge takes it only
    /// in the field's deepest pockets, which is what feathers the band out
    /// instead of ending it on a line.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="x"/> is outside the world.</exception>
    public bool TakesBlendAt(int x, int y)
    {
        ThrowIfOutside(x);

        if (_wanderField is null || _blendIds[x] is null)
        {
            return false;
        }

        // Where this row's boundary actually falls, as the same signed fraction
        // of the band the column's own offset is measured in. Sampled statelessly
        // at (boundary, row), so the answer depends on nothing but position --
        // which is what lets a chunk be re-materialised at any time, in any
        // order, and come back identical.
        //
        // The gain is there because fBm concentrates near the middle of its
        // range: without it the edge would wander over the innermost third of the
        // band and leave the rest permanently unblended. Clamped after, so the
        // edge stays inside the band it was given.
        double displacement = Math.Clamp(
            _wanderField.Sample(_boundaryColumns[x], y) * WanderGain, -1.0, 1.0);

        // The tile takes the other biome exactly when this row's edge has moved
        // past it -- when the side it is on now differs from the side the column
        // nominally sits on.
        return (_blendOffsets[x] < displacement) != (_blendOffsets[x] < 0.0);
    }

    /// <summary>
    /// The surface biome of one <b>tile</b>, which inside a transition band may
    /// differ from the column's own biome.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="x"/> is outside the world.</exception>
    public string BiomeAt(int x, int y) =>
        TakesBlendAt(x, y) ? _blendIds[x]! : _biomeIds[x];

    /// <summary>
    /// The underground variant paired with a given surface biome id.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="UndergroundBiomeAt"/> so the same pairing rule can
    /// be applied to a column's blend partner, which has no column of its own to
    /// be looked up by.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// If the biome is not registered or declares no underground variant; both
    /// are load-time invariants, so either means the registry passed here is not
    /// the one this map was generated against.
    /// </exception>
    public static string UndergroundVariantOf(string surfaceId, Registry<BiomeDefinition> biomes)
    {
        ArgumentNullException.ThrowIfNull(surfaceId);
        ArgumentNullException.ThrowIfNull(biomes);

        if (!biomes.TryGet(surfaceId, out BiomeDefinition surface))
        {
            throw new InvalidOperationException(
                $"Biome '{surfaceId}' is not in the biome registry passed here.");
        }

        return surface.UndergroundVariant ?? throw new InvalidOperationException(
            $"Surface biome '{surfaceId}' declares no underground_variant, so the underground "
            + "layer beneath it cannot be resolved.");
    }

    /// <summary>Bounds check shared by every column accessor.</summary>
    private void ThrowIfOutside(int x)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, _biomeIds.Length);
    }

    /// <summary>
    /// A copy of the whole column array, for phases that scan it and for tests.
    /// A copy rather than the buffer itself: handing out the array would make
    /// this type mutable through the back door.
    /// </summary>
    public string[] ToArray() => (string[])_biomeIds.Clone();
}
