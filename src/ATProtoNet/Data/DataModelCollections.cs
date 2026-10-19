using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace ATProtoNet.Data;

/// <summary>A data-model array.</summary>
public sealed record DataModelArray : DataModelValue, IReadOnlyList<DataModelValue>
{
    private readonly DataModelValue[] _items;

    /// <summary>Creates the array.</summary>
    /// <param name="items">The items, in order.</param>
    public DataModelArray(IEnumerable<DataModelValue> items)
    {
        _items = [.. items];
        foreach (var item in _items) ArgumentNullException.ThrowIfNull(item, nameof(items));
    }

    internal DataModelArray(DataModelValue[] items, bool owned) => _items = owned ? items : [.. items];

    /// <inheritdoc />
    public int Count => _items.Length;

    /// <inheritdoc />
    public DataModelValue this[int index] => _items[index];

    /// <inheritdoc />
    public IEnumerator<DataModelValue> GetEnumerator() => ((IEnumerable<DataModelValue>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Equals(DataModelArray? other) => other is not null && _items.AsSpan().SequenceEqual(other._items);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items) hash.Add(item);
        return hash.ToHashCode();
    }
}

/// <summary>A data-model map: string keys, kept in the order they were read or given.</summary>
/// <remarks>Equality ignores the order, as the data model does; DRISL writing sorts the keys canonically.</remarks>
public sealed record DataModelMap : DataModelValue, IReadOnlyDictionary<string, DataModelValue>
{
    private const int IndexedFrom = 9;

    private readonly KeyValuePair<string, DataModelValue>[] _entries;
    private readonly Dictionary<string, int>? _index;

    /// <summary>Creates the map.</summary>
    /// <param name="entries">The entries, in the order to keep.</param>
    /// <exception cref="ArgumentException">A key occurs twice.</exception>
    public DataModelMap(IEnumerable<KeyValuePair<string, DataModelValue>> entries)
    {
        _entries = [.. entries];
        foreach (var (key, value) in _entries)
        {
            ArgumentNullException.ThrowIfNull(key, nameof(entries));
            ArgumentNullException.ThrowIfNull(value, nameof(entries));
        }
        if (!TryIndex(_entries, out _index)) throw new ArgumentException("A map key occurs twice.", nameof(entries));
    }

    private DataModelMap(KeyValuePair<string, DataModelValue>[] entries, Dictionary<string, int>? index) => (_entries, _index) = (entries, index);

    /// <summary>The entries, in the order they were read or given.</summary>
    public IReadOnlyList<KeyValuePair<string, DataModelValue>> Entries => _entries;

    /// <inheritdoc />
    public int Count => _entries.Length;

    /// <inheritdoc />
    public IEnumerable<string> Keys => _entries.Select(e => e.Key);

    /// <inheritdoc />
    public IEnumerable<DataModelValue> Values => _entries.Select(e => e.Value);

    /// <inheritdoc />
    public DataModelValue this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException($"The map has no key \"{key}\".");

    /// <inheritdoc />
    public bool ContainsKey(string key) => Find(key) >= 0;

    /// <inheritdoc />
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out DataModelValue value)
    {
        var at = Find(key);
        value = at >= 0 ? _entries[at].Value : null;
        return at >= 0;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, DataModelValue>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, DataModelValue>>)_entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Equals(DataModelMap? other)
    {
        if (other is null || other.Count != Count) return false;
        foreach (var (key, value) in _entries)
            if (!other.TryGetValue(key, out var theirs) || !value.Equals(theirs)) return false;
        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var (key, value) in _entries) hash ^= HashCode.Combine(key, value);
        return hash;
    }

    // From a source that has not checked uniqueness (JSON).
    internal static DataModelMap FromParsedEntries(List<KeyValuePair<string, DataModelValue>> entries)
    {
        var array = entries.ToArray();
        return TryIndex(array, out var index) ? new(array, index) : throw new FormatException("A map key occurs twice.");
    }

    // Small maps are searched, larger ones indexed; either way a repeated key is found.
    private static bool TryIndex(KeyValuePair<string, DataModelValue>[] entries, out Dictionary<string, int>? index)
    {
        index = null;
        if (entries.Length < IndexedFrom)
        {
            for (var i = 1; i < entries.Length; i++)
                for (var j = 0; j < i; j++)
                    if (entries[i].Key == entries[j].Key) return false;
            return true;
        }
        index = new Dictionary<string, int>(entries.Length, StringComparer.Ordinal);
        for (var i = 0; i < entries.Length; i++)
            if (!index.TryAdd(entries[i].Key, i)) return false;
        return true;
    }

    private int Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_index is not null) return _index.GetValueOrDefault(key, -1);
        for (var i = 0; i < _entries.Length; i++)
            if (_entries[i].Key == key) return i;
        return -1;
    }
}
