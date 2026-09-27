using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace ATProtoNet.Caching;

// A bounded map that evicts its least recently used entry to make room. Thread-safe. A lookup through
// TryGetValue and a store count as a use; TryPeek does not.
//
// Lookups take no lock, since some of these caches are read on every request: a hit moves its entry to
// the front only when the lock is free. So recency is exact when the cache is used from one thread at a
// time, and approximate under contention, where waiting would serialize every hit. Every write takes the
// lock. A node is never changed once published (a new value is a new node), so a lookup sees an old
// value or a new one, never a mix of the two.
internal sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly Lock _lock = new();
    private readonly ConcurrentDictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _recency = new(); // most recently used first

    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        _map = new(comparer);
    }

    public int Capacity { get; }

    public int Count => _map.Count;

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (!_map.TryGetValue(key, out var node))
        {
            value = default;
            return false;
        }

        value = node.Value.Value;
        if (!ReferenceEquals(_recency.First, node) && _lock.TryEnter())
        {
            try
            {
                // Still in the list: a write may have replaced or evicted it meanwhile.
                if (node.List is not null)
                    MoveToFront(node);
            }
            finally
            {
                _lock.Exit();
            }
        }

        return true;
    }

    public bool TryPeek(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var found = _map.TryGetValue(key, out var node);
        value = found ? node!.Value.Value : default;
        return found;
    }

    // Stores a value as the most recently used, replacing any the key held.
    //
    // Returns: The entry evicted to make room, if one was.
    public KeyValuePair<TKey, TValue>? Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                ReplaceLocked(node, value);
                return null;
            }

            return AddLocked(key, value);
        }
    }

    // Adds addValue for the key, or replaces the value it holds with update(value), as one step: no
    // other write lands between reading the value and storing the update. update runs under the
    // cache's lock, so it must be quick and must not call back into the cache.
    public TValue AddOrUpdate(TKey key, TValue addValue, Func<TValue, TValue> update)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                var updated = update(node.Value.Value);
                ReplaceLocked(node, updated);
                return updated;
            }

            AddLocked(key, addValue);
            return addValue;
        }
    }

    // Returns the value the key holds, as a use, or adds value for it.
    //
    // evicted: The entry evicted to make room, if one was.
    public TValue GetOrAdd(TKey key, TValue value, out KeyValuePair<TKey, TValue>? evicted)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                MoveToFront(node);
                evicted = null;
                return node.Value.Value;
            }

            evicted = AddLocked(key, value);
            return value;
        }
    }

    public bool Remove(TKey key) => Remove(key, out _);

    public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_lock)
        {
            if (!_map.TryRemove(key, out var node))
            {
                value = default;
                return false;
            }

            _recency.Remove(node);
            value = node.Value.Value;
            return true;
        }
    }

    // Removes the key only while it still holds value.
    public bool Remove(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(key, out var node) || !EqualityComparer<TValue>.Default.Equals(node.Value.Value, value))
                return false;

            _map.TryRemove(key, out _);
            _recency.Remove(node);
            return true;
        }
    }

    // Removes the least recently used entry whose value match accepts.
    public KeyValuePair<TKey, TValue>? RemoveLeastRecent(Func<TValue, bool> match)
    {
        lock (_lock)
        {
            for (var node = _recency.Last; node is not null; node = node.Previous)
            {
                if (match(node.Value.Value))
                {
                    _map.TryRemove(node.Value.Key, out _);
                    _recency.Remove(node);
                    return node.Value;
                }
            }

            return null;
        }
    }

    // Evicts before adding, so a lock-free Count never sees one entry over the capacity.
    private KeyValuePair<TKey, TValue>? AddLocked(TKey key, TValue value)
    {
        KeyValuePair<TKey, TValue>? evicted = null;
        if (_recency.Count >= Capacity)
        {
            var last = _recency.Last!;
            _recency.RemoveLast();
            _map.TryRemove(last.Value.Key, out _);
            evicted = last.Value;
        }

        _map[key] = _recency.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
        return evicted;
    }

    private void ReplaceLocked(LinkedListNode<KeyValuePair<TKey, TValue>> node, TValue value)
    {
        _recency.Remove(node);
        _map[node.Value.Key] = _recency.AddFirst(new KeyValuePair<TKey, TValue>(node.Value.Key, value));
    }

    private void MoveToFront(LinkedListNode<KeyValuePair<TKey, TValue>> node)
    {
        _recency.Remove(node);
        _recency.AddFirst(node);
    }
}
