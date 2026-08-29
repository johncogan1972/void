using System;
using Void;

namespace Void.Tests;

/// <summary>
/// Frozen output vectors. These are the contract that keeps world generation
/// reproducible forever: if any of these change, every existing world changes.
///
/// PROVENANCE — read before touching anything here.
///
/// These values were NOT produced by running the implementation under test.
/// They come from a separate transcription of the published reference C
/// sources into Python, written from the algorithm text:
///   splitmix64.c          http://prng.di.unimi.it/splitmix64.c
///   xoshiro256plusplus.c  http://prng.di.unimi.it/xoshiro256plusplus.c
///
/// That transcription is anchored to the widely published SplitMix64 test
/// vector for seed 0 (see <see cref="SplitMix64SeedZeroMatchesPublishedVector"/>),
/// which pins the seed-expansion half against an external source. The
/// xoshiro256++ outputs below are the reference algorithm applied to that
/// verified state. They were then cross-checked against a second, separately
/// written transcription of the same reference C: all 8 SplitMix64 seed-0
/// values, all 8 xoshiro256++ seed-0 values and all 8 seed-1 values matched
/// exactly. Two independent transcriptions agreeing is not the same as an
/// upstream-quoted vector for a SplitMix64-expanded seed, which does not
/// exist publicly — re-derive from a third-party implementation if you need
/// assurance beyond that.
///
/// The composed values (sub-stream seeds, doubles) additionally depend on this
/// project's own derivation scheme (FNV-1a 64 over UTF-8, XOR into the parent
/// seed, one SplitMix64 mix) and so are project-defined by construction.
/// </summary>
public class GoldenVectorTests
{
    /// <summary>
    /// External anchor: this sequence is the published SplitMix64 output for
    /// seed 0 and appears in the reference distribution and in numerous
    /// independent ports.
    /// </summary>
    [Fact]
    public void SplitMix64SeedZeroMatchesPublishedVector()
    {
        ulong[] expected =
        {
            0xE220A8397B1DCDAFUL,
            0x6E789E6AA1B965F4UL,
            0x06C45D188009454FUL,
            0xF88BB8A8724C81ECUL,
            0x1B39896A51A8749BUL,
            0x53CB9F0C747EA2EAUL,
            0x2C829ABE1F4532E1UL,
            0xC584133AC916AB3CUL,
        };

        SplitMix64 mixer = new SplitMix64(0UL);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], mixer.Next());
        }
    }

    /// <summary>
    /// The four state words xoshiro is seeded with are exactly the first four
    /// SplitMix64 outputs, so the seed-0 state is externally pinned too.
    /// </summary>
    [Fact]
    public void SeedExpansionUsesSplitMix64Outputs()
    {
        ulong[] state = new ulong[4];
        SplitMix64.ExpandState(0UL, state);

        Assert.Equal(
            new ulong[]
            {
                0xE220A8397B1DCDAFUL,
                0x6E789E6AA1B965F4UL,
                0x06C45D188009454FUL,
                0xF88BB8A8724C81ECUL,
            },
            state);
    }

    [Fact]
    public void Xoshiro256PlusPlusSeedZeroGolden()
    {
        ulong[] expected =
        {
            0x53175D61490B23DFUL,
            0x61DA6F3DC380D507UL,
            0x5C0FDF91EC9A7BFCUL,
            0x02EEBF8C3BBE5E1AUL,
            0x7ECA04EBAF4A5EEAUL,
            0x0543C37757F08D9AUL,
            0xDB7490C75AB5026EUL,
            0xD87343E6464BC959UL,
        };

        Rng rng = new Rng(0UL);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], rng.NextULong());
        }
    }

    [Fact]
    public void Xoshiro256PlusPlusSeedOneGolden()
    {
        ulong[] expected =
        {
            0xCFC5D07F6F03C29BUL,
            0xBF424132963FE08DUL,
            0x19A37D5757AAF520UL,
            0xBF08119F05CD56D6UL,
            0x2F47184B86186FA4UL,
            0x97299FCAE7202345UL,
            0xFCA3C79508F41507UL,
            0x85FEA5C90363F221UL,
        };

        Rng rng = new Rng(1UL);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], rng.NextULong());
        }
    }

    [Fact]
    public void Xoshiro256PlusPlusWorldSeedGolden()
    {
        ulong[] expected =
        {
            0xB2F2A310E96BD1C5UL,
            0xB54062465B950493UL,
            0x87ACA4A9668814B0UL,
            0xF13D2E2448A9CFFBUL,
            0xB7AFDB427F6B86A2UL,
            0xC3A68C4E4F50D0C7UL,
            0x5BDE00C2B40585AEUL,
            0xB27E2DD974F18E8AUL,
        };

        Rng rng = new Rng(0x0123456789ABCDEFUL);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], rng.NextULong());
        }
    }

    /// <summary>
    /// Sub-stream seeds are project-defined: FNV-1a 64 of the UTF-8 key XORed
    /// into the parent seed, mixed once through SplitMix64. The FNV values are
    /// checkable against any FNV-1a 64 implementation.
    /// </summary>
    [Theory]
    [InlineData("terrain", 0x6490C5564A80B9E8UL)]
    [InlineData("caves", 0xE9DB1268B7C194B1UL)]
    public void SubStreamSeedGolden(string key, ulong expected)
    {
        Assert.Equal(expected, Rng.DeriveSeed(0x0123456789ABCDEFUL, key));
    }

    [Fact]
    public void SubStreamSequenceGolden()
    {
        ulong[] expectedTerrain =
        {
            0xEFA449C4C5416748UL,
            0xD3438C0D51CD7AC9UL,
            0x425B6D26C045088EUL,
            0x1F0C0A84498EB13FUL,
        };
        ulong[] expectedCaves =
        {
            0xB2083224BF112B9CUL,
            0x7223120F8E9F643DUL,
            0x6C5040868774ACD8UL,
            0x681FD92FB7425C98UL,
        };

        Rng root = new Rng(0x0123456789ABCDEFUL);
        Rng terrain = root.Derive("terrain");
        Rng caves = root.Derive("caves");

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(expectedTerrain[i], terrain.NextULong());
            Assert.Equal(expectedCaves[i], caves.NextULong());
        }
    }

    /// <summary>
    /// Pins the ulong-to-double conversion (top 53 bits scaled by 2^-53) on
    /// top of the sub-stream vectors above.
    /// </summary>
    [Fact]
    public void NextDoubleGolden()
    {
        double[] expected = { 0.9361005883595275, 0.8252494366186535, 0.2592075557334007 };

        Rng terrain = new Rng(0x0123456789ABCDEFUL).Derive("terrain");
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], terrain.NextDouble(), 15);
        }
    }
}
