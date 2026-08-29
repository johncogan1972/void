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

    /// <summary>
    /// Seeds the four state words directly from 32 bytes, read little-endian.
    /// Used by the save-file keystream (save-format-spec §7), which derives its
    /// state from SHA-256 output rather than from a 64-bit seed.
    ///
    /// The all-zero state is xoshiro's single invalid state (it is a fixed
    /// point and emits zeroes forever). If the supplied bytes are all zero the
    /// constructor falls back to <see cref="SplitMix64.ExpandState"/> of seed 0,
    /// which is deterministic and matches the fallback used elsewhere. It is
    /// not rejected because the caller's bytes are hash output, so the case is
    /// unreachable in practice and a throw would be untestable dead weight.
    /// </summary>
    /// <exception cref="ArgumentException">If the span is not exactly 32 bytes.</exception>
    internal Xoshiro256PlusPlus(ReadOnlySpan<byte> state32)
    {
        if (state32.Length != 32)
        {
            throw new ArgumentException(
                $"State must be exactly 32 bytes (was {state32.Length}).", nameof(state32));
        }

        for (int i = 0; i < 4; i++)
        {
            _state[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                state32.Slice(i * 8, 8));
        }

        if ((_state[0] | _state[1] | _state[2] | _state[3]) == 0UL)
        {
            SplitMix64.ExpandState(0UL, _state);
        }
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
