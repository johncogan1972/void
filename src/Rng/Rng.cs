using System;
using System.Collections.Generic;

namespace Void;

/// <summary>
/// Deterministic random source for all generation code (VOID-005).
///
/// Backed by xoshiro256++ seeded through SplitMix64. Contains no
/// <c>System.Random</c>, clock, GUID or hardware entropy: a given seed always
/// produces the same sequence, on every machine and every run.
///
/// Sub-streams are derived by name via <see cref="Derive"/>. A sub-stream's
/// sequence depends only on (world seed, key) — never on how many other
/// sub-streams exist, when they were created, or how far they have been drawn.
/// </summary>
public sealed class Rng
{
    private const ulong FnvOffsetBasis = 0xCBF29CE484222325UL;
    private const ulong FnvPrime = 0x100000001B3UL;

    private readonly Xoshiro256PlusPlus _core;

    /// <summary>The 64-bit seed this generator was constructed from.</summary>
    public ulong Seed { get; }

    /// <summary>Creates a generator for the given seed.</summary>
    public Rng(ulong seed)
    {
        Seed = seed;
        _core = new Xoshiro256PlusPlus(seed);
    }

    /// <summary>
    /// FNV-1a over the UTF-8 bytes of <paramref name="key"/>, XORed into the
    /// parent seed and mixed once through SplitMix64. Pure function of its
    /// inputs, so sub-stream derivation is order-independent.
    /// </summary>
    internal static ulong DeriveSeed(ulong seed, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        unchecked
        {
            ulong hash = FnvOffsetBasis;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(key);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash = (hash ^ bytes[i]) * FnvPrime;
            }

            SplitMix64 mixer = new SplitMix64(seed ^ hash);
            return mixer.Next();
        }
    }

    /// <summary>
    /// Returns an independent generator for the named sub-stream. Calling this
    /// does not advance the parent, so derivation order is irrelevant.
    /// </summary>
    public Rng Derive(string key) => new Rng(DeriveSeed(Seed, key));

    /// <summary>Raw 64-bit draw.</summary>
    public ulong NextULong() => _core.Next();

    /// <summary>
    /// Uniform double in [0, 1). Uses the top 53 bits, the canonical
    /// conversion recommended alongside xoshiro.
    /// </summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform bool, taken from the top (highest-quality) bit.</summary>
    public bool NextBool() => (NextULong() >> 63) != 0UL;

    /// <summary>
    /// Uniform int in the half-open range [min, max). Free of modulo bias:
    /// draws are rejected unless they fall in the largest multiple of the
    /// range span that fits in 2^64 (classic rejection sampling).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If max &lt;= min.</exception>
    public int NextInt(int min, int max)
    {
        if (max <= min)
        {
            throw new ArgumentOutOfRangeException(
                nameof(max), $"max ({max}) must be greater than min ({min}).");
        }

        ulong span = (ulong)((long)max - min);

        unchecked
        {
            // Number of representable ulongs is 2^64, so 2^64 % span is
            // ((2^64 - 1) % span + 1) % span. Everything at or below `zone`
            // maps onto span uniformly.
            ulong zone = ulong.MaxValue - ((ulong.MaxValue % span) + 1UL) % span;

            ulong draw;
            do
            {
                draw = NextULong();
            }
            while (draw > zone);

            return (int)((long)min + (long)(draw % span));
        }
    }

    /// <summary>Uniform int in [0, max).</summary>
    public int NextInt(int max) => NextInt(0, max);

    /// <summary>
    /// Weighted choice. Candidates are sorted by their ordinal string key
    /// before the draw, so the result never depends on insertion order, hash
    /// order or registry load order (world-generation-spec §14).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// If the collection is empty, contains duplicate keys, has a negative or
    /// non-finite weight, or has a total weight of zero.
    /// </exception>
    public T ChooseWeighted<T>(
        IEnumerable<T> items,
        Func<T, string> keySelector,
        Func<T, double> weightSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(weightSelector);

        List<(string Key, T Item, double Weight)> candidates = new();
        foreach (T item in items)
        {
            string key = keySelector(item);
            double weight = weightSelector(item);

            if (double.IsNaN(weight) || double.IsInfinity(weight) || weight < 0.0)
            {
                throw new ArgumentException(
                    $"Weight for '{key}' must be finite and non-negative (was {weight}).",
                    nameof(items));
            }

            candidates.Add((key, item, weight));
        }

        if (candidates.Count == 0)
        {
            throw new ArgumentException("Cannot choose from an empty collection.", nameof(items));
        }

        candidates.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

        double total = 0.0;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (i > 0 && string.Equals(candidates[i].Key, candidates[i - 1].Key, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Duplicate weighted-choice key '{candidates[i].Key}'; keys must be unique to be sortable.",
                    nameof(items));
            }

            total += candidates[i].Weight;
        }

        if (total <= 0.0)
        {
            throw new ArgumentException("Total weight must be greater than zero.", nameof(items));
        }

        double target = NextDouble() * total;
        double cumulative = 0.0;
        for (int i = 0; i < candidates.Count; i++)
        {
            cumulative += candidates[i].Weight;
            if (target < cumulative)
            {
                return candidates[i].Item;
            }
        }

        // Only reachable through floating-point summation slack; the last
        // non-zero-weight entry is the correct fallback.
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            if (candidates[i].Weight > 0.0)
            {
                return candidates[i].Item;
            }
        }

        throw new ArgumentException("Total weight must be greater than zero.", nameof(items));
    }

    /// <summary>Weighted choice over a key-to-weight map. Keys are sorted ordinally.</summary>
    public string ChooseWeighted(IReadOnlyDictionary<string, double> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        return ChooseWeighted(weights, static kv => kv.Key, static kv => kv.Value).Key;
    }
}
