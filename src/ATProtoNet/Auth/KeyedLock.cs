namespace ATProtoNet.Auth;

// An asynchronous lock per key, held only while it is in use: an entry exists from the first waiter to
// the last release, so the keys of a large population (one per account) do not accumulate.
//
// TKey: The key type.
internal sealed class KeyedLock<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> _entries;

    public KeyedLock(IEqualityComparer<TKey>? comparer = null) => _entries = new Dictionary<TKey, Entry>(comparer);

    // How many keys are held or waited for: zero once every lease is released.
    public int Count
    {
        get
        {
            lock (_entries)
                return _entries.Count;
        }
    }

    // Waits for the lock of key; disposing the lease releases it.
    //
    // Throws OperationCanceledException: The wait was cancelled; nothing is held.
    public async ValueTask<Lease> AcquireAsync(TKey key, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
                _entries[key] = entry = new Entry();

            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Forget(key, entry);
            throw;
        }

        return new Lease(this, key, entry);
    }

    private void Forget(TKey key, Entry entry)
    {
        lock (_entries)
        {
            if (--entry.References == 0)
                _entries.Remove(key);
        }
    }

    internal sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        // Guarded by the dictionary's lock.
        public int References { get; set; }
    }

    // The held lock of one key; released once, however often it is disposed.
    public sealed class Lease : IDisposable, IAsyncDisposable
    {
        private readonly KeyedLock<TKey> _owner;
        private readonly TKey _key;
        private readonly Entry _entry;
        private int _released;

        internal Lease(KeyedLock<TKey> owner, TKey key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            _entry.Semaphore.Release();
            _owner.Forget(_key, _entry);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
