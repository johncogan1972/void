using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Void;

/// <summary>
/// One special tile inside a prefab that the placement engine acts on
/// (VOID-024), per world-data-model-spec §5.
///
/// A marker is a coordinate plus an intent: the tile arrays say what the
/// structure is built from, markers say what still has to be filled in — the
/// boss, the chest, the entrance the reachability check aims at.
///
/// <para>Coordinates are <b>tile-local</b> to the prefab, so a marker is
/// meaningful before the prefab has a world position.
/// <see cref="PrefabRegistryLoader"/> proves they fall inside the declared
/// dimensions at load; nothing downstream re-checks.</para>
/// </summary>
public sealed class PrefabMarker
{
    /// <summary>
    /// Backing store for <see cref="Metadata"/>. Held separately so the
    /// <c>init</c> accessor can copy and freeze whatever it is given, and so an
    /// omitted <c>metadata</c> key still yields an empty map rather than null.
    /// </summary>
    private readonly IReadOnlyDictionary<string, JsonElement> _metadata =
        ReadOnlyDictionary<string, JsonElement>.Empty;

    /// <summary>What the placement engine should do here. JSON key <c>type</c>.</summary>
    public PrefabMarkerType Type { get; init; }

    /// <summary>
    /// Tile-local coordinates, origin at the prefab's top-left. Valid range is
    /// <c>[0, width)</c> / <c>[0, height)</c> — enforced at load, fatally,
    /// because an out-of-range marker would otherwise place a boss or a chest
    /// outside the structure that was meant to contain it.
    /// </summary>
    public int X { get; init; }

    /// <inheritdoc cref="X"/>
    public int Y { get; init; }

    /// <summary>
    /// Free-form, per-marker payload: a chest's loot table id, a spawner's
    /// enemy and rate, a boss's type. JSON key <c>metadata</c>; absent means an
    /// empty map, never null, so callers never null-check.
    ///
    /// <para><b>Deliberately unvalidated, and deliberately untyped.</b> What a
    /// key means depends on <see cref="Type"/>, and the code that gives those
    /// keys meaning is the Phase 2 placement engine, which does not exist yet.
    /// Inventing a typed shape here would either be guesswork frozen into the
    /// schema, or a validator that rejects data the real consumer would have
    /// accepted. The values are kept as raw <see cref="JsonElement"/> so any
    /// JSON shape survives a load/save round trip untouched.</para>
    ///
    /// <para>Values are cloned on assignment, and the map is copied. This is
    /// defensive, not load-bearing: the deserialiser hands over elements that
    /// already own their backing document, so they outlive the loader's
    /// <c>JsonDocument</c> on their own. The clone exists for the other caller
    /// — code or a test that builds a marker from a map it still holds a
    /// reference to, or from elements of a document it is about to dispose.</para>
    ///
    /// <para>Keys compare with <see cref="StringComparer.Ordinal"/>, like every
    /// other id map in the content layer. <b>Enumeration order is insertion
    /// order and carries no guarantee</b> — never iterate this map to produce
    /// generated output or to drive a seeded draw. Look keys up by name.</para>
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Metadata
    {
        get => _metadata;
        init => _metadata = Freeze(value);
    }

    /// <summary>Copies and detaches a metadata map; see <see cref="Metadata"/>.</summary>
    private static IReadOnlyDictionary<string, JsonElement> Freeze(
        IReadOnlyDictionary<string, JsonElement>? source)
    {
        if (source is null || source.Count == 0)
        {
            return ReadOnlyDictionary<string, JsonElement>.Empty;
        }

        Dictionary<string, JsonElement> copy = new(source.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonElement> entry in source)
        {
            copy[entry.Key] = entry.Value.Clone();
        }

        return new ReadOnlyDictionary<string, JsonElement>(copy);
    }
}
