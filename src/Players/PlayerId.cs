using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Identity of one player, stable for the life of a character.
///
/// Multiplayer-ready by construction (CLAUDE.md): there is never a "the player"
/// singleton, so anything a player can own — a picked-up side anchor, a placed
/// block's placer, a party spawn — is keyed by this value even while the game
/// is single-player. Code that would otherwise say "the player" says "the
/// player with this id" instead, and never needs revisiting when a second
/// player arrives.
///
/// A struct wrapping a <see cref="Guid"/> rather than a bare <c>Guid</c> so the
/// compiler can tell a player id from a world id or a campaign id; they are all
/// UUIDs and mixing them up would otherwise compile cleanly.
///
/// Serialises as a plain JSON string via <see cref="PlayerIdJsonConverter"/>,
/// not as an object with a <c>value</c> member, so manifests stay readable.
/// </summary>
[JsonConverter(typeof(PlayerIdJsonConverter))]
public readonly struct PlayerId : IEquatable<PlayerId>
{
    /// <summary>Wraps an existing UUID — use when rehydrating from disk or the network.</summary>
    public PlayerId(Guid value) => Value = value;

    /// <summary>
    /// The underlying UUID. Exposed because save and network code has to write
    /// the raw 16 bytes; gameplay code should pass the <see cref="PlayerId"/>.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// The default value, which no real player ever has. Treat it as "no player"
    /// — it is what a struct field left unset holds.
    /// </summary>
    public static PlayerId None => default;

    /// <summary>True for <see cref="None"/>; a cheap "this slot is unowned" test.</summary>
    public bool IsNone => Value == Guid.Empty;

    /// <summary>
    /// Mints a brand new identity.
    /// </summary>
    /// <remarks>
    /// <see cref="Guid.NewGuid"/> is deliberate and allowed here: a player id is
    /// runtime identity, not generated world content, so CLAUDE.md's determinism
    /// rule (which covers world generation only) does not apply. Never call this
    /// from a world-generation pass.
    /// </remarks>
    public static PlayerId New() => new PlayerId(Guid.NewGuid());

    /// <summary>Parses the canonical form written by <see cref="ToString"/>.</summary>
    /// <exception cref="FormatException">If the text is not a UUID.</exception>
    public static PlayerId Parse(string text) => new PlayerId(Guid.Parse(text));

    /// <summary>Canonical dashed lower-case form, invariant of the user's locale.</summary>
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>Identity is the UUID and nothing else.</summary>
    public bool Equals(PlayerId other) => Value.Equals(other.Value);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PlayerId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Value equality, so ids can be compared without unwrapping them.</summary>
    public static bool operator ==(PlayerId left, PlayerId right) => left.Equals(right);

    /// <summary>Value inequality; the negation of <see cref="op_Equality"/>.</summary>
    public static bool operator !=(PlayerId left, PlayerId right) => !left.Equals(right);
}
