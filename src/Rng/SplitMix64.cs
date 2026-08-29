using System;

namespace Void;

/// <summary>
/// SplitMix64 (Vigna). Used only to expand a 64-bit seed into xoshiro's
/// 256-bit state and to mix sub-stream keys — never as the game RNG itself.
///
/// Reference: http://prng.di.unimi.it/splitmix64.c
/// Anchored by the published test vector for seed 0:
/// E220A8397B1DCDAF, 6E789E6AA1B965F4, 06C45D188009454F, F88BB8A8724C81EC.
/// </summary>
internal struct SplitMix64
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    private ulong _state;

    /// <summary>
    /// Seeds the generator. Every seed is valid, including zero — that is the
    /// reason this is used to expand seeds for xoshiro, which cannot take an
    /// all-zero state.
    /// </summary>
    internal SplitMix64(ulong seed)
    {
        _state = seed;
    }

    /// <summary>Advances the state and returns the next 64-bit output.</summary>
    internal ulong Next()
    {
        unchecked
        {
            _state += GoldenGamma;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>
    /// Expands <paramref name="seed"/> into four state words for xoshiro256++.
    /// The all-zero state is invalid for xoshiro; if it ever occurred the
    /// fallback below substitutes a fixed non-zero state (deterministically).
    /// </summary>
    internal static void ExpandState(ulong seed, Span<ulong> state)
    {
        SplitMix64 mixer = new SplitMix64(seed);
        for (int i = 0; i < 4; i++)
        {
            state[i] = mixer.Next();
        }

        if ((state[0] | state[1] | state[2] | state[3]) == 0UL)
        {
            state[0] = GoldenGamma;
        }
    }
}
