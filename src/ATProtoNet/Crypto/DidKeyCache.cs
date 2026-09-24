using System.Security.Cryptography;

namespace ATProtoNet.Crypto;

/// <summary>
/// A bounded cache of parsed <c>did:key</c> public keys, for signature verification.
/// </summary>
/// <remarks>
/// <para>Parsing a did:key costs as much as verifying with it: base58, a modular square root
/// to decompress the point, and a platform key import whose first use is slower still. The
/// same few keys (a repo's signing key, a space's credential issuer) verify signature after
/// signature. A did:key is its own content, so an entry never goes stale; the bound only
/// limits memory, and the least recently used entry goes first.</para>
/// <para>.NET does not document <see cref="ECDsa"/> instances as safe for concurrent use, so
/// an entry keeps a small pool of imported keys instead of sharing one. A verification rents
/// a key for its duration, and concurrent verifications against the same did:key each get
/// their own.</para>
/// </remarks>
internal sealed class DidKeyCache
{
    internal const int DefaultCapacity = 1024;

    private readonly int _capacity;
    private readonly int _maxIdleKeysPerEntry;
    private readonly object _lock = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _recency = new(); // most recently used first

    internal DidKeyCache(int capacity, int maxIdleKeysPerEntry)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxIdleKeysPerEntry, 1);
        _capacity = capacity;
        _maxIdleKeysPerEntry = maxIdleKeysPerEntry;
    }

    /// <summary>The process-wide cache behind <see cref="AtProtoCrypto.VerifySignature"/>.</summary>
    internal static DidKeyCache Shared { get; } = new(DefaultCapacity, Environment.ProcessorCount);

    internal int Count
    {
        get
        {
            lock (_lock)
                return _entries.Count;
        }
    }

    /// <summary>
    /// Verifies <paramref name="signature"/> over <paramref name="message"/> with the key a
    /// did:key names, exactly as <see cref="AtProtoKey.Verify"/> would.
    /// </summary>
    /// <exception cref="FormatException">Thrown when the did:key is malformed; nothing is cached.</exception>
    internal bool Verify(string didKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        var entry = GetOrAdd(didKey);
        var key = entry.Rent();
        try
        {
            return key.Verify(message, signature);
        }
        finally
        {
            entry.Return(key);
        }
    }

    private Entry GetOrAdd(string didKey)
    {
        ArgumentNullException.ThrowIfNull(didKey);

        lock (_lock)
        {
            if (_entries.TryGetValue(didKey, out var node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                return node.Value;
            }
        }

        // Parse and import outside the lock: that is the expensive part, and a malformed
        // did:key (or a curve the platform lacks) throws here without touching the cache. Two
        // threads may race to parse the same key; the loser's entry is simply discarded.
        var parameters = AtProtoCrypto.ParseDidKey(didKey, out var curve);
        var created = new Entry(didKey, curve, parameters, _maxIdleKeysPerEntry,
            AtProtoCrypto.CreatePublicKey(parameters, curve));

        Entry result;
        Entry? evicted = null;
        lock (_lock)
        {
            if (_entries.TryGetValue(didKey, out var raced))
            {
                result = raced.Value;
            }
            else
            {
                _entries.Add(didKey, _recency.AddFirst(created));
                result = created;

                if (_entries.Count > _capacity)
                {
                    var last = _recency.Last!;
                    _recency.RemoveLast();
                    _entries.Remove(last.Value.DidKey);
                    evicted = last.Value;
                }
            }
        }

        if (!ReferenceEquals(result, created))
            created.Evict();
        evicted?.Evict();
        return result;
    }

    /// <summary>One did:key: its parsed point, and the imported keys not currently in use.</summary>
    private sealed class Entry
    {
        private readonly KeyCurve _curve;
        private readonly ECParameters _parameters;
        private readonly int _maxIdleKeys;
        private readonly Stack<AtProtoKey> _idle = new(); // guarded by itself
        private bool _evicted;

        public Entry(string didKey, KeyCurve curve, ECParameters parameters, int maxIdleKeys, AtProtoKey firstKey)
        {
            DidKey = didKey;
            _curve = curve;
            _parameters = parameters;
            _maxIdleKeys = maxIdleKeys;
            _idle.Push(firstKey);
        }

        public string DidKey { get; }

        public AtProtoKey Rent()
        {
            lock (_idle)
            {
                if (_idle.TryPop(out var key))
                    return key;
            }

            return AtProtoCrypto.CreatePublicKey(_parameters, _curve);
        }

        public void Return(AtProtoKey key)
        {
            lock (_idle)
            {
                if (!_evicted && _idle.Count < _maxIdleKeys)
                {
                    _idle.Push(key);
                    return;
                }
            }

            key.Dispose();
        }

        /// <summary>
        /// Disposes the idle keys. Keys rented at the time are disposed when they come back.
        /// </summary>
        public void Evict()
        {
            AtProtoKey[] idle;
            lock (_idle)
            {
                _evicted = true;
                idle = [.. _idle];
                _idle.Clear();
            }

            foreach (var key in idle)
                key.Dispose();
        }
    }
}
