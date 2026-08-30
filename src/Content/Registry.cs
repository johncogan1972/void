using System;
using System.Collections;
using System.Collections.Generic;

namespace Void;

/// <summary>
/// Immutable, id-keyed table of content definitions (VOID-006).
///
/// Two properties are load-bearing:
/// <list type="bullet">
/// <item>Iteration is <b>explicitly sorted by id</b> using
/// <see cref="StringComparer.Ordinal"/> — never hash order, never file load
/// order, never filesystem enumeration order. Registry order feeds world
/// generation, so it must be identical on every machine and every run.</item>
/// <item>There is no mutating API. A registry is produced by
/// <see cref="RegistryBuilder{T}.Build"/> and frozen from that point on.</item>
/// </list>
/// </summary>
/// <typeparam name="T">Definition type; see <see cref="IContentDefinition"/>.</typeparam>
public sealed class Registry<T> : IReadOnlyCollection<T>
    where T : IContentDefinition
{
    private readonly Dictionary<string, T> _byId;
    private readonly T[] _sorted;
    private readonly string[] _sortedIds;

    // Populated only when T carries a numeric key (INumericContentDefinition).
    // Null for plain definition types, which is what makes the numeric lookups
    // throw a clear "this registry has no numeric ids" instead of quietly
    // returning nothing. Lookup-only, so hash order never escapes: iteration
    // still goes through _sorted.
    private readonly Dictionary<ushort, T>? _byNumericId;

    private static readonly bool HasNumericIds =
        typeof(INumericContentDefinition).IsAssignableFrom(typeof(T));

    /// <summary>An empty registry of this type.</summary>
    public static Registry<T> Empty { get; } = new Registry<T>(new List<T>());

    /// <summary>
    /// Freezes <paramref name="entries"/> into sorted, immutable form. Internal by
    /// design: registries are produced through <see cref="RegistryBuilder{T}"/>, so
    /// the duplicate-id checks cannot be bypassed on the normal path.
    ///
    /// Sorting is ordinal on the string id and happens here, once — iteration order
    /// feeds world generation, so it must not depend on insertion or file order.
    /// </summary>
    internal Registry(IReadOnlyList<T> entries)
    {
        T[] sorted = new T[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            sorted[i] = entries[i];
        }

        Array.Sort(sorted, static (a, b) => string.CompareOrdinal(a.Id, b.Id));

        _sorted = sorted;
        _sortedIds = new string[sorted.Length];
        _byId = new Dictionary<string, T>(sorted.Length, StringComparer.Ordinal);

        _byNumericId = HasNumericIds
            ? new Dictionary<ushort, T>(sorted.Length)
            : null;

        for (int i = 0; i < sorted.Length; i++)
        {
            _sortedIds[i] = sorted[i].Id;
            _byId.Add(sorted[i].Id, sorted[i]);

            if (_byNumericId is not null)
            {
                // RegistryBuilder already rejected numeric collisions with a
                // message naming both files; this indexer form is a safety net
                // for registries built by hand in tests or tooling.
                ushort numeric = ((INumericContentDefinition)sorted[i]!).NumericId;
                if (!_byNumericId.TryAdd(numeric, sorted[i]))
                {
                    throw new ContentLoadException(
                        $"Duplicate {typeof(T).Name} numeric id {numeric}: claimed by both " +
                        $"'{_byNumericId[numeric].Id}' and '{sorted[i].Id}'.");
                }
            }
        }
    }

    /// <summary>Number of definitions held.</summary>
    public int Count => _sorted.Length;

    /// <summary>All ids, in ordinal-sorted order.</summary>
    public IReadOnlyList<string> Ids => _sortedIds;

    /// <summary>All definitions, in ordinal-sorted id order.</summary>
    public IReadOnlyList<T> Entries => _sorted;

    /// <summary>True if <paramref name="id"/> is present.</summary>
    public bool Contains(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.ContainsKey(id);
    }

    /// <summary>
    /// Looks up a definition, throwing if absent. Unknown ids are a bug in data
    /// or code, so the default is to fail loudly with the id in the message.
    /// </summary>
    /// <exception cref="ContentLoadException">If no such id is registered.</exception>
    public T Get(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!_byId.TryGetValue(id, out T? value))
        {
            throw new ContentLoadException(
                $"Unknown {typeof(T).Name} id '{id}'. Registry holds {Count} entries.");
        }

        return value;
    }

    /// <summary>Probing lookup for callers that treat absence as normal.</summary>
    public bool TryGet(string id, out T value)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (_byId.TryGetValue(id, out T? found))
        {
            value = found;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Looks up a definition by its stable numeric key, throwing if absent.
    /// This is the hot path for tile rendering and mining, which hold
    /// <c>block_id</c> / <c>wall_id</c> numbers rather than strings.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// If <typeparamref name="T"/> does not implement
    /// <see cref="INumericContentDefinition"/> and so has no numeric keys.
    /// </exception>
    /// <exception cref="ContentLoadException">If no such numeric id is registered.</exception>
    public T GetByNumericId(ushort numericId)
    {
        Dictionary<ushort, T> map = RequireNumericMap();

        if (!map.TryGetValue(numericId, out T? value))
        {
            throw new ContentLoadException(
                $"Unknown {typeof(T).Name} numeric id {numericId}. Registry holds {Count} entries.");
        }

        return value;
    }

    /// <summary>Probing form of <see cref="GetByNumericId"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// If <typeparamref name="T"/> has no numeric keys.
    /// </exception>
    public bool TryGetByNumericId(ushort numericId, out T value)
    {
        Dictionary<ushort, T> map = RequireNumericMap();

        if (map.TryGetValue(numericId, out T? found))
        {
            value = found;
            return true;
        }

        value = default!;
        return false;
    }

    private Dictionary<ushort, T> RequireNumericMap()
    {
        if (_byNumericId is null)
        {
            throw new InvalidOperationException(
                $"{typeof(T).Name} does not implement {nameof(INumericContentDefinition)}, " +
                "so this registry has no numeric ids. Use Get(string) instead.");
        }

        return _byNumericId;
    }

    /// <summary>Indexer form of <see cref="Get"/>.</summary>
    public T this[string id] => Get(id);

    /// <summary>Enumerates definitions in ordinal-sorted id order.</summary>
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_sorted).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _sorted.GetEnumerator();
}

/// <summary>
/// Accumulates definitions and produces a frozen <see cref="Registry{T}"/>
/// (VOID-006).
///
/// Duplicate ids are a hard error naming the id and <b>both</b> contributing
/// files: last-write-wins would make the world depend on enumeration order.
/// </summary>
/// <typeparam name="T">Definition type.</typeparam>
public sealed class RegistryBuilder<T>
    where T : IContentDefinition
{
    private readonly List<T> _entries = new();
    private readonly Dictionary<string, string> _originById = new(StringComparer.Ordinal);
    private readonly Dictionary<ushort, string> _originByNumericId = new();

    /// <summary>Number of definitions accumulated so far.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Adds a definition sourced from <paramref name="fileName"/>.
    /// </summary>
    /// <exception cref="ContentLoadException">
    /// If the id is missing/blank, or already claimed by another file.
    /// </exception>
    public RegistryBuilder<T> Add(T definition, string fileName)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string? id = definition.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ContentLoadException(
                fileName, $"A {typeof(T).Name} entry has a missing or empty 'id'.");
        }

        if (_originById.TryGetValue(id, out string? firstFile))
        {
            throw new ContentLoadException(
                $"Duplicate {typeof(T).Name} id '{id}': defined in both " +
                $"'{firstFile}' and '{fileName}'. Ids must be unique.");
        }

        // Types carrying a numeric key (block_id, wall_id) get the same
        // duplicate treatment as string ids: saved tiles store the raw number,
        // so two definitions claiming one number is a silent world corruption,
        // not a warning. Checked at runtime rather than via a second generic
        // constraint so RegistryBuilder stays one type for all content.
        if (definition is INumericContentDefinition numeric)
        {
            if (_originByNumericId.TryGetValue(numeric.NumericId, out string? firstNumericFile))
            {
                throw new ContentLoadException(
                    $"Duplicate {typeof(T).Name} numeric id {numeric.NumericId} (on '{id}'): " +
                    $"defined in both '{firstNumericFile}' and '{fileName}'. " +
                    "Numeric ids must be unique and are stable forever.");
            }

            _originByNumericId.Add(numeric.NumericId, fileName);
        }

        _originById.Add(id, fileName);
        _entries.Add(definition);
        return this;
    }

    /// <summary>Freezes the accumulated definitions into an immutable registry.</summary>
    public Registry<T> Build() => new Registry<T>(_entries);
}
