using System.Security.Cryptography;
using ATProtoNet.Caching;

namespace ATProtoNet.Crypto;

// A bounded cache of parsed did:key public keys, for signature verification.
//
// Parsing a did:key costs as much as verifying with it: base58, a modular square root to decompress the
// point, and a platform key import whose first use is slower still. The same few keys (a repo's signing
// key, a space's credential issuer) verify signature after signature. A did:key is its own content, so
// an entry never goes stale; the bound only limits memory, and the least recently used entry goes first.
//
// .NET does not document ECDsa instances as safe for concurrent use, so an entry keeps a small pool of
// imported keys instead of sharing one. A verification rents a key for its duration, and concurrent
// verifications against the same did:key each get their own.
internal sealed class DidKeyCache
{
    internal const int DefaultCapacity = 1024;

    private readonly int _maxIdleKeysPerEntry;
    private readonly LruCache<string, Entry> _entries;

    internal DidKeyCache(int capacity, int maxIdleKeysPerEntry)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxIdleKeysPerEntry, 1);
        _entries = new LruCache<string, Entry>(capacity, StringComparer.Ordinal);
        _maxIdleKeysPerEntry = maxIdleKeysPerEntry;
    }

    // The process-wide cache behind AtProtoCrypto.VerifySignature.
    internal static DidKeyCache Shared { get; } = new(DefaultCapacity, Environment.ProcessorCount);

    internal int Count => _entries.Count;

    // Verifies signature over message with the key a did:key names, exactly as AtProtoKey.Verify would.
    //
    // Throws FormatException: Thrown when the did:key is malformed; nothing is cached.
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

    // Verifies a JWS signature with the key a did:key names: algorithm must be the one the key's curve
    // signs with, and a high-S signature is accepted.
    //
    // Throws FormatException: Thrown when the did:key is malformed; nothing is cached.
    internal bool VerifyJws(string didKey, string algorithm, ReadOnlySpan<byte> signingInput, ReadOnlySpan<byte> signature)
    {
        var entry = GetOrAdd(didKey);
        if (!string.Equals(entry.Curve.JwsAlgorithm(), algorithm, StringComparison.Ordinal) ||
            !AtProtoCrypto.HasSignatureLength(signature) ||
            !AtProtoCrypto.HasScalarsInRange(signature, entry.Curve))
        {
            return false;
        }

        var normalized = AtProtoCrypto.NormalizeLowSSignature(signature.ToArray(), entry.Curve);
        var key = entry.Rent();
        try
        {
            return key.Verify(signingInput, normalized);
        }
        finally
        {
            entry.Return(key);
        }
    }

    private Entry GetOrAdd(string didKey)
    {
        ArgumentNullException.ThrowIfNull(didKey);

        if (_entries.TryGetValue(didKey, out var cached))
            return cached;

        // Parse and import outside the lock: that is the expensive part, and a malformed
        // did:key (or a curve the platform lacks) throws here without touching the cache. Two
        // threads may race to parse the same key; the loser's entry is simply discarded.
        var parameters = AtProtoCrypto.ParseDidKey(didKey, out var curve);
        var created = new Entry(curve, parameters, _maxIdleKeysPerEntry, AtProtoCrypto.CreatePublicKey(parameters, curve));

        var result = _entries.GetOrAdd(didKey, created, out var evicted);
        if (!ReferenceEquals(result, created))
            created.Evict();
        evicted?.Value.Evict();
        return result;
    }

    // One did:key: its parsed point, and the imported keys not currently in use.
    private sealed class Entry
    {
        private readonly KeyCurve _curve;
        private readonly ECParameters _parameters;
        private readonly int _maxIdleKeys;
        private readonly Stack<AtProtoKey> _idle = new(); // guarded by itself
        private bool _evicted;

        public Entry(KeyCurve curve, ECParameters parameters, int maxIdleKeys, AtProtoKey firstKey)
        {
            _curve = curve;
            _parameters = parameters;
            _maxIdleKeys = maxIdleKeys;
            _idle.Push(firstKey);
        }

        public KeyCurve Curve => _curve;

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

        // Disposes the idle keys. Keys rented at the time are disposed when they come back.
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
