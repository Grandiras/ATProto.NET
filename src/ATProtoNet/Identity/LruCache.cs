using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace ATProtoNet.Identity;

// A bounded, thread-safe map that evicts its least recently used entry. Lookups take no lock (caches are read on every request): a
// hit moves to the front only when the lock is free, so recency is approximate under contention. Writes take the lock.
internal sealed class LruCache<TKey, TValue>(int capacity) where TKey : notnull
{
    private readonly Lock _lock = new();
    private readonly ConcurrentDictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map = new();
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _recency = new(); // most recent first

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
            try
            {
                // Still listed: a write may have replaced or evicted it meanwhile.
                if (node.List is not null)
                {
                    _recency.Remove(node);
                    _recency.AddFirst(node);
                }
            }
            finally
            {
                _lock.Exit();
            }
        return true;
    }

    public bool TryPeek(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var found = _map.TryGetValue(key, out var node);
        value = found ? node!.Value.Value : default;
        return found;
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryRemove(key, out var old)) _recency.Remove(old);
            else if (_recency.Count >= capacity)
            {
                _map.TryRemove(_recency.Last!.Value.Key, out _);
                _recency.RemoveLast();
            }
            _map[key] = _recency.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
        }
    }

    public void Remove(TKey key)
    {
        lock (_lock)
            if (_map.TryRemove(key, out var node)) _recency.Remove(node);
    }
}
