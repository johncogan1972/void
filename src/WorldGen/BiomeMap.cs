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
/// <para>Immutable once built. A phase that wants to change biome assignment
/// produces a new instance from the old one.</para>
/// </summary>
public sealed class BiomeMap
{
    /// <summary>
    /// Biome id per column, indexed by x. Copied in on construction so the
    /// caller's buffer cannot alias — otherwise "immutable" would depend on the
    /// caller's manners.
    /// </summary>
    private readonly string[] _biomeIds;

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
    {
        ArgumentNullException.ThrowIfNull(biomeIds);

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

        _biomeIds = (string[])biomeIds.Clone();
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
    /// A copy of the whole column array, for phases that scan it and for tests.
    /// A copy rather than the buffer itself: handing out the array would make
    /// this type mutable through the back door.
    /// </summary>
    public string[] ToArray() => (string[])_biomeIds.Clone();
}
