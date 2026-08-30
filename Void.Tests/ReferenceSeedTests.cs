using System;
using Void.Determinism;

namespace Void.Tests;

/// <summary>
/// The reference-seed test (VOID-008). This is the CI tripwire on determinism.
///
/// PROVENANCE — read before touching the golden.
///
/// Unlike <see cref="GoldenVectorTests"/>, this hash has no external anchor and
/// is not meant to have one. It is a snapshot of this project's own output for
/// a frozen seed. Its only job is to detect *change*: whether the output is
/// correct is established by GoldenVectorTests, which pins the RNG against the
/// published reference vectors underneath.
///
/// HOW TO REGENERATE THE GOLDEN
///
/// Only when the generator changed on purpose. The failure message prints the
/// actual hash — paste it into <see cref="GoldenHash"/> in the same commit as
/// the generator change, and say in the commit message what changed and why.
///
///     dotnet test Void.Tests/Void.Tests.csproj --filter ReferenceSeedTests
///
/// Never update the golden to make a red build green. A moved hash means every
/// existing world generates differently; that is a decision, not a chore. If
/// you did not intend to change generation, the test has caught a real bug.
/// </summary>
public class ReferenceSeedTests
{
    /// <summary>
    /// SHA-256 of the canonical payload, uppercase hex. Frozen. See the class
    /// remarks before changing it.
    /// </summary>
    private const string GoldenHash =
        "1EC5B896BD9201270E657CBC078AD177352DB9D9639F1210C5F0666055F12765";

    /// <summary>
    /// The CI tripwire itself: the frozen seed still hashes to the frozen golden.
    ///
    /// A failure means generated output changed. Read the class remarks before
    /// touching GoldenHash — never update it to turn a build green.
    /// </summary>
    [Fact]
    public void ReferencePayloadHashMatchesGolden()
    {
        string actual = ReferencePayload.Hash();

        Assert.True(
            GoldenHash == actual,
            $"""
             Reference-seed payload changed.

               source:   {ReferencePayload.Current.Id} v{ReferencePayload.Current.Version}
               seed:     0x{ReferencePayload.ReferenceSeed:X16}
               expected: {GoldenHash}
               actual:   {actual}

             World generation is no longer reproducible against the committed
             golden. If this was NOT intentional, you have a determinism bug —
             find it, do not update the golden. If it WAS intentional, paste the
             actual hash into GoldenHash in ReferenceSeedTests.cs, in the same
             commit as the change, and explain the change in the commit message.
             """);
    }

    /// <summary>
    /// Determinism means repeatable, not merely stable across commits: the same
    /// seed must produce identical bytes twice in the same process.
    /// </summary>
    [Fact]
    public void ReferencePayloadIsRepeatable()
    {
        Assert.Equal(ReferencePayload.Build(), ReferencePayload.Build());
    }

    /// <summary>
    /// A payload that does not vary with the seed would hash stably while
    /// testing nothing.
    /// </summary>
    [Fact]
    public void ReferencePayloadVariesWithSeed()
    {
        string a = ReferencePayload.Hash(ReferencePayload.Current, ReferencePayload.ReferenceSeed);
        string b = ReferencePayload.Hash(ReferencePayload.Current, ReferencePayload.ReferenceSeed + 1UL);

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// The header pins the source identity, so swapping the payload source in a
    /// later phase cannot silently collide with the current golden.
    /// </summary>
    [Fact]
    public void PayloadHeaderDistinguishesSources()
    {
        string current = ReferencePayload.Hash();
        string other = ReferencePayload.Hash(
            new RenamedSource(ReferencePayload.Current), ReferencePayload.ReferenceSeed);

        Assert.NotEqual(current, other);
    }

    /// <summary>Same bytes, different identity — used to prove the header matters.</summary>
    private sealed class RenamedSource : IReferencePayloadSource
    {
        private readonly IReferencePayloadSource _inner;

        /// <summary>
        /// Wraps a source, changing only its reported identity.
        /// </summary>
        public RenamedSource(IReferencePayloadSource inner) => _inner = inner;

        /// <summary>
        /// A deliberately different id; everything else is passed through.
        /// </summary>
        public string Id => _inner.Id + "-renamed";

        /// <summary>
        /// Unchanged, so identity is the only variable in the comparison.
        /// </summary>
        public int Version => _inner.Version;

        /// <summary>
        /// Writes exactly the same bytes as the wrapped source.
        /// </summary>
        public void Write(PayloadWriter writer, ulong seed) => _inner.Write(writer, seed);
    }
}
