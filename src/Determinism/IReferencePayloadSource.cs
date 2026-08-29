namespace Void.Determinism;

/// <summary>
/// Produces the canonical byte payload that the reference-seed CI test hashes
/// (VOID-008).
///
/// The payload source is deliberately an interface so it can be swapped as the
/// project grows without rewriting the harness or the CI wiring. Phase 0 hashes
/// a fixed draw sequence from the seeded RNG; Phase 2 is expected to replace it
/// with a source that serialises an actual generated world. Only
/// <see cref="ReferencePayload.Current"/> and the golden hash change — the
/// hashing, the test and the workflow stay as they are.
///
/// Implementations must be pure functions of the seed they are handed: no
/// clock, no environment, no hash-ordered iteration, no floating-point
/// formatting. See CLAUDE.md, "Determinism".
/// </summary>
public interface IReferencePayloadSource
{
    /// <summary>
    /// Stable identifier for this payload shape, written into the payload
    /// header so two different sources can never hash alike.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Payload format version. Bump when the byte layout changes for a reason
    /// other than a generator change, so the golden diff carries its reason.
    /// </summary>
    int Version { get; }

    /// <summary>Writes the payload for <paramref name="seed"/>.</summary>
    void Write(PayloadWriter writer, ulong seed);
}
