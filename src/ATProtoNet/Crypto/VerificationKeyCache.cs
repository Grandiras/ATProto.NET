namespace ATProtoNet.Crypto;

/// <summary>A bounded least-recently-used cache of parsed verification keys, shared by whoever is given it.</summary>
/// <remarks>
/// A <c>did:key</c> is its own content, so an entry never goes stale; the bound only limits memory. Keys that
/// verify signature after signature (a repo's signing key, a credential issuer) keep their platform key imported.
/// There is no process-wide instance: create one and pass it to the components that should share it.
/// </remarks>
public sealed class VerificationKeyCache
{
    /// <summary>The capacity of a cache created without one.</summary>
    public const int DefaultCapacity = 1024;

    private readonly int _capacity;
    private readonly Dictionary<DidKey, LinkedListNode<VerificationKey>> _entries = [];
    private readonly LinkedList<VerificationKey> _recency = [];

    /// <summary>Creates an empty cache.</summary>
    /// <param name="capacity">The most keys kept, at least 1.</param>
    public VerificationKeyCache(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>The number of keys cached.</summary>
    public int Count
    {
        get
        {
            lock (_entries) return _entries.Count;
        }
    }

    /// <summary>The verification key of <paramref name="key"/>, from the cache or newly cached.</summary>
    /// <param name="key">The <c>did:key</c>.</param>
    /// <returns>The key.</returns>
    public VerificationKey Get(DidKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_entries)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                return node.Value;
            }
            if (_entries.Count == _capacity)
            {
                _entries.Remove(_recency.Last!.Value.DidKey);
                _recency.RemoveLast();
            }
            var created = key.ToVerificationKey();
            _entries.Add(key, _recency.AddFirst(created));
            return created;
        }
    }

    /// <summary>Verifies a signature with the cached key of <paramref name="key"/>, as <see cref="VerificationKey.Verify"/> does.</summary>
    /// <param name="key">The signer's <c>did:key</c>.</param>
    /// <param name="message">The signed bytes.</param>
    /// <param name="signature">The 64-byte <c>r || s</c>.</param>
    /// <param name="policy">Whether a high-S signature may verify.</param>
    /// <returns>Whether the signature is valid.</returns>
    public bool Verify(DidKey key, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, SignaturePolicy policy = SignaturePolicy.LowSOnly) =>
        Get(key).Verify(message, signature, policy);
}
