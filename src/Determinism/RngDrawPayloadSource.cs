using System.Collections.Generic;

namespace Void.Determinism;

/// <summary>
/// Phase 0 reference payload: a fixed draw sequence across the whole public
/// surface of <see cref="Rng"/> (VOID-008).
///
/// There is no world generator yet, so this stands in for one. It is not a
/// sample of the generator's output — it is a tripwire on the layer every
/// generator will sit on. Any change to xoshiro256++, to the SplitMix64 seed
/// expansion, to sub-stream derivation, to the ulong-to-double conversion, to
/// the rejection sampling in <c>NextInt</c>, or to the sort order inside
/// <c>ChooseWeighted</c> moves this payload's hash.
///
/// Deliberately excluded: anything routed through the save envelope. Its
/// compressed bytes depend on the zstd library version, so a dependency bump
/// would fail this test without any generator change — a false alarm that
/// would train people to update the golden without reading it. The save
/// round-trip is covered by its own tests (VOID-007).
/// </summary>
public sealed class RngDrawPayloadSource : IReferencePayloadSource
{
    /// <summary>
    /// Sub-streams drawn from, in a fixed ordinal order. Sub-stream derivation
    /// is order-independent by design, so this list pins the payload layout,
    /// not the values.
    /// </summary>
    private static readonly string[] SubStreams =
    {
        "biomes", "caves", "loot", "ores", "structures", "terrain",
    };

    /// <summary>Fixed weights for the <c>ChooseWeighted</c> section.</summary>
    private static readonly Dictionary<string, double> Weights = new()
    {
        ["common"] = 60.0,
        ["uncommon"] = 25.0,
        ["rare"] = 10.0,
        ["epic"] = 4.5,
        ["legendary"] = 0.5,
    };

    /// <summary>
    /// Stable identifier recorded in the payload header. Changing this string
    /// changes the hash, so treat it as part of the format, not a label.
    /// </summary>
    public string Id => "rng-draws";

    /// <summary>
    /// Payload schema version. Bump only alongside a deliberate golden-hash
    /// regeneration — see the determinism rules in CLAUDE.md.
    /// </summary>
    public int Version => 1;

    /// <summary>
    /// Writes the fixed draw sequence the golden hash is taken over.
    ///
    /// Every line here is load-bearing: reordering a draw, changing a count, or
    /// touching the weight table changes the hash and therefore claims that every
    /// existing world generates differently. Do not edit to fix a red build.
    /// </summary>
    /// <param name="writer">Destination for the canonical byte layout.</param>
    /// <param name="seed">World seed the draws derive from.</param>
    public void Write(PayloadWriter writer, ulong seed)
    {
        Rng root = new Rng(seed);

        // Raw 64-bit draws.
        writer.WriteInt32(32);
        for (int i = 0; i < 32; i++)
        {
            writer.WriteUInt64(root.NextULong());
        }

        // Doubles, as raw bit patterns.
        writer.WriteInt32(16);
        for (int i = 0; i < 16; i++)
        {
            writer.WriteDouble(root.NextDouble());
        }

        // Bounded ints — exercises the rejection-sampling loop.
        writer.WriteInt32(16);
        for (int i = 0; i < 16; i++)
        {
            writer.WriteInt32(root.NextInt(-1000, 1000));
        }

        // Bools, one byte each.
        writer.WriteInt32(64);
        for (int i = 0; i < 64; i++)
        {
            writer.WriteByte(root.NextBool() ? (byte)1 : (byte)0);
        }

        // Named sub-streams: derived seed plus a short sequence from each.
        writer.WriteInt32(SubStreams.Length);
        foreach (string key in SubStreams)
        {
            writer.WriteString(key);

            Rng stream = root.Derive(key);
            writer.WriteUInt64(stream.Seed);

            writer.WriteInt32(8);
            for (int i = 0; i < 8; i++)
            {
                writer.WriteUInt64(stream.NextULong());
            }
        }

        // Weighted choice — pins the ordinal sort that makes it order-independent.
        Rng picks = root.Derive("rarity");
        writer.WriteInt32(32);
        for (int i = 0; i < 32; i++)
        {
            writer.WriteString(picks.ChooseWeighted(Weights));
        }
    }
}
