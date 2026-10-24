using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace ATProtoNet.Identity;

// The tiers behind CachingDidResolver: documents and remembered failures in memory, one fetch per DID, and an optional shared tier.
internal sealed class DidCache(Func<Did, DidCache.Mode, Task<DidDocument>> resolve, DidCacheOptions options, IDistributedCache? shared, TimeProvider time, Action<string, Did, Exception> sharedFailed)
{
    private readonly Lock _lock = new();
    private readonly LruCache<Did, Entry> _documents = new(options.Capacity);
    private readonly LruCache<Did, Entry> _failures = new(options.FailureCapacity);
    private readonly Dictionary<Did, Fetch> _inflight = [];
    private readonly Dictionary<Did, PendingWrite> _writes = [];

    // How a fetch reaches the document: the shared tier first, or the inner resolver's matching method.
    public enum Mode { Shared, Resolve, Refresh, Fresh }

    public Task<DidDocument> GetAsync(Did did, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return Touch(did) is { } entry && TryServe(did, entry, now) is { } served ? served : Start(did, shared is null ? Mode.Resolve : Mode.Shared).WaitAsync(cancellationToken);
    }

    // At most one fetch per MinRefreshInterval, counted from the last attempt; within it, what that attempt left.
    public Task<DidDocument> RefreshAsync(Did did, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (Touch(did) is not { } entry || now - entry.LastAttemptAt >= options.MinRefreshInterval) return Start(did, Mode.Refresh).WaitAsync(cancellationToken);
        if (entry.Error is { } error) return Task.FromException<DidDocument>(Copy(error));
        return now - entry.FetchedAt < options.ExpireAfter ? entry.Served! : Start(did, Mode.Refresh).WaitAsync(cancellationToken);
    }

    public Task<DidDocument> FetchFreshAsync(Did did, CancellationToken cancellationToken)
    {
        Detach(did);
        return Start(did, Mode.Fresh).WaitAsync(cancellationToken);
    }

    public async ValueTask InvalidateAsync(Did did, CancellationToken cancellationToken)
    {
        Detach(did);
        if (shared is not null) await RemoveSharedAsync(did, cancellationToken).ConfigureAwait(false);
    }

    private void Detach(Did did)
    {
        lock (_lock)
        {
            _documents.Remove(did);
            _failures.Remove(did);
            if (_inflight.Remove(did, out var fetch)) fetch.Detached = true;
            if (_writes.TryGetValue(did, out var write)) write.Invalidated = true;
        }
    }

    // The cached document as a completed task, a remembered failure as a faulted one, or null when the entry does not answer.
    private Task<DidDocument>? TryServe(Did did, Entry entry, DateTimeOffset now)
    {
        var age = now - entry.FetchedAt;
        if (entry.Error is { } error) return age < options.FailureTtl ? Task.FromException<DidDocument>(Copy(error)) : null;
        if (age >= options.ExpireAfter) return null;
        if (age >= options.StaleAfter && now >= entry.RetryAfter) _ = Start(did, Mode.Resolve);
        return entry.Served;
    }

    private Task<DidDocument> Start(Did did, Mode mode)
    {
        Fetch fetch;
        lock (_lock)
        {
            if (_inflight.TryGetValue(did, out var existing)) return existing.Completion.Task;
            _inflight.Add(did, fetch = new());
        }
        // Observed once here, so a fetch nobody awaits never surfaces as an unobserved exception.
        _ = fetch.Completion.Task.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        // On the thread pool: a fetch resuming on the first caller's context would stall every caller that joined it.
        _ = Task.Run(() => RunAsync(did, fetch, mode));
        return fetch.Completion.Task;
    }

    private async Task RunAsync(Did did, Fetch fetch, Mode mode)
    {
        DidDocument document;
        var fetchedAt = time.GetUtcNow();
        try
        {
            // Read inside the single flight, so an invalidation or a fresh fetch meanwhile detaches it like any other fetch.
            if (mode == Mode.Shared && await ReadSharedAsync(did).ConfigureAwait(false) is { } stored && fetchedAt - stored.FetchedAt < options.ExpireAfter)
                (document, fetchedAt) = stored;
            else
            {
                document = await resolve(did, mode == Mode.Shared ? Mode.Resolve : mode).ConfigureAwait(false);
                fetchedAt = time.GetUtcNow();
                mode = Mode.Resolve;
            }
        }
        catch (IdentityResolutionException ex)
        {
            var now = time.GetUtcNow();
            lock (_lock)
                if (Complete(did, fetch))
                {
                    // A document inside its lifetime outlives a failed refresh; the next attempt waits out the failure TTL.
                    if (_documents.TryPeek(did, out var current) && now - current.FetchedAt < options.ExpireAfter)
                        _documents.Set(did, current with { RetryAfter = now + options.FailureTtl, LastAttemptAt = now });
                    else
                    {
                        _documents.Remove(did);
                        _failures.Set(did, new(null, ex, now, default, now));
                    }
                }
            fetch.Completion.SetException(ex);
            return;
        }
        catch (Exception ex)
        {
            // Not a resolution outcome (a bug): nothing is remembered.
            lock (_lock) Complete(did, fetch);
            fetch.Completion.SetException(ex);
            return;
        }

        PendingWrite? write = null;
        lock (_lock)
            if (Complete(did, fetch))
            {
                _failures.Remove(did);
                _documents.Set(did, new(document, null, fetchedAt, default, fetchedAt));
                // Registered with the store, so an invalidation that sees the document gone also sees the write to undo.
                if (shared is not null && mode != Mode.Shared)
                {
                    if (!_writes.TryGetValue(did, out write) || write.Invalidated) _writes[did] = write = new();
                    write.Count++;
                }
            }
        fetch.Completion.SetResult(document);
        if (mode == Mode.Shared && time.GetUtcNow() - fetchedAt >= options.StaleAfter) _ = Start(did, Mode.Resolve);
        if (write is not null) await WriteSharedAsync(did, document, fetchedAt, write).ConfigureAwait(false);
    }

    private Entry? Touch(Did did)
    {
        lock (_lock)
            return _documents.TryGetValue(did, out var document) ? document : _failures.TryGetValue(did, out var failure) ? failure : null;
    }

    private bool Complete(Did did, Fetch fetch)
    {
        if (_inflight.TryGetValue(did, out var current) && ReferenceEquals(current, fetch)) _inflight.Remove(did);
        return !fetch.Detached;
    }

    // A fresh exception for each throw: one instance thrown on several threads would have its stack trace rewritten under each.
    private static IdentityResolutionException Copy(IdentityResolutionException cached) => new(cached.Error, cached.Message, cached.Identifier, cached.InnerException);

    private async Task<(DidDocument Document, DateTimeOffset FetchedAt)?> ReadSharedAsync(Did did)
    {
        try
        {
            if (await shared!.GetAsync(Key(did)).ConfigureAwait(false) is not { } bytes) return null;
            using var stored = JsonDocument.Parse(bytes);
            var document = DidDocument.Parse(JsonMarshal.GetRawUtf8Value(stored.RootElement.GetProperty("document")));
            // A document stored under another DID's key is not this DID's document.
            return document.Id.Value == did.Value ? (document, stored.RootElement.GetProperty("fetchedAt").GetDateTimeOffset()) : null;
        }
        catch (Exception ex)
        {
            sharedFailed("read", did, ex);
            return null;
        }
    }

    private async Task WriteSharedAsync(Did did, DidDocument document, DateTimeOffset fetchedAt, PendingWrite write)
    {
        try
        {
            await shared!.SetAsync(Key(did), Serialize(document, fetchedAt), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = options.ExpireAfter }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            sharedFailed("write", did, ex);
        }
        bool undo;
        lock (_lock)
        {
            undo = write.Invalidated;
            if (--write.Count == 0 && _writes.TryGetValue(did, out var current) && current == write) _writes.Remove(did);
        }
        // Invalidated while in flight: the write may have landed after the removal and put the old document back.
        if (undo) await RemoveSharedAsync(did, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task RemoveSharedAsync(Did did, CancellationToken cancellationToken)
    {
        try
        {
            await shared!.RemoveAsync(Key(did), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sharedFailed("remove", did, ex);
        }
    }

    private string Key(Did did) => options.DistributedCacheKeyPrefix + did.Value;

    private static byte[] Serialize(DidDocument document, DateTimeOffset fetchedAt)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("fetchedAt", fetchedAt);
            writer.WritePropertyName("document");
            writer.WriteRawValue(document.Json.Span);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private sealed record Entry(DidDocument? Document, IdentityResolutionException? Error, DateTimeOffset FetchedAt, DateTimeOffset RetryAfter, DateTimeOffset LastAttemptAt)
    {
        // Allocated once: a hit is served on every request.
        public Task<DidDocument>? Served { get; } = Document is null ? null : Task.FromResult(Document);
    }

    private sealed class Fetch
    {
        public TaskCompletionSource<DidDocument> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Detached { get; set; }
    }

    private sealed class PendingWrite
    {
        public int Count { get; set; }

        public bool Invalidated { get; set; }
    }
}
