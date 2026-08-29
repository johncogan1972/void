using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Void;

/// <summary>
/// Save-file obfuscation keystream (save-format-spec §7).
///
/// Purpose is deterrence, not security: it stops a hex editor and a grep, and
/// nothing more. The derivation is a pure function of
/// (seed_input, file_salt, format_version, magic), all of which live in the
/// envelope header, so any reader can reproduce it.
///
/// <c>key_material = SHA-256(seed_input LE64 || file_salt LE32 ||
/// format_version LE16 || magic LE32)</c>, whose 32 bytes become xoshiro256++'s
/// four state words directly (little-endian), not a SplitMix64 expansion.
/// </summary>
internal static class Keystream
{
    /// <summary>Bytes hashed to produce the key material: 8 + 4 + 2 + 4.</summary>
    internal const int KeyMaterialInputSize = 18;

    /// <summary>Derives the 32 bytes of key material for one file.</summary>
    internal static byte[] DeriveKeyMaterial(ulong seedInput, uint fileSalt, ushort formatVersion)
    {
        Span<byte> input = stackalloc byte[KeyMaterialInputSize];
        BinaryPrimitives.WriteUInt64LittleEndian(input[..8], seedInput);
        BinaryPrimitives.WriteUInt32LittleEndian(input.Slice(8, 4), fileSalt);
        BinaryPrimitives.WriteUInt16LittleEndian(input.Slice(12, 2), formatVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(input.Slice(14, 4), SaveEnvelope.Magic);

        byte[] material = new byte[SaveEnvelope.HashSize];
        SHA256.HashData(input, material);
        return material;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with keystream bytes, emitted as
    /// little-endian 64-bit draws from the seeded generator. A partial trailing
    /// word contributes only its low bytes.
    /// </summary>
    internal static void Fill(ulong seedInput, uint fileSalt, ushort formatVersion, Span<byte> destination)
    {
        byte[] material = DeriveKeyMaterial(seedInput, fileSalt, formatVersion);
        Xoshiro256PlusPlus rng = new Xoshiro256PlusPlus(material);

        Span<byte> word = stackalloc byte[8];
        int offset = 0;
        while (offset < destination.Length)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(word, rng.Next());
            int take = Math.Min(8, destination.Length - offset);
            word[..take].CopyTo(destination.Slice(offset, take));
            offset += take;
        }
    }

    /// <summary>XORs the keystream into <paramref name="buffer"/> in place (§7).</summary>
    internal static void ApplyXor(ulong seedInput, uint fileSalt, ushort formatVersion, Span<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        byte[] keystream = new byte[buffer.Length];
        Fill(seedInput, fileSalt, formatVersion, keystream);
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] ^= keystream[i];
        }
    }
}
