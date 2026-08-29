using System;

namespace Void;

/// <summary>
/// xoshiro256++ 1.0 (Blackman &amp; Vigna), the project's only source of
/// randomness in generation code. Pure state, no engine or platform entropy:
/// every sequence is a function of its seed alone.
///
/// Reference: http://prng.di.unimi.it/xoshiro256plusplus.c
/// </summary>
internal sealed class Xoshiro256PlusPlus
{
    private readonly ulong[] _state = new ulong[4];

    internal Xoshiro256PlusPlus(ulong seed)
    {
        SplitMix64.ExpandState(seed, _state);
    }

    private static ulong Rotl(ulong x, int k)
    {
        unchecked
        {
            return (x << k) | (x >> (64 - k));
        }
    }

    /// <summary>Returns the next 64-bit output and advances the state.</summary>
    internal ulong Next()
    {
        unchecked
        {
            ulong result = Rotl(_state[0] + _state[3], 23) + _state[0];

            ulong t = _state[1] << 17;
            _state[2] ^= _state[0];
            _state[3] ^= _state[1];
            _state[1] ^= _state[2];
            _state[0] ^= _state[3];
            _state[2] ^= t;
            _state[3] = Rotl(_state[3], 45);

            return result;
        }
    }
}
