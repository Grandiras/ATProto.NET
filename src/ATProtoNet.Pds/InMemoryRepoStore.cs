using System.Collections.Concurrent;

namespace ATProtoNet.Pds;

/// <summary>
/// In-memory repository store for development and testing.
/// Not suitable for production use — all data is lost on restart.
/// </summary>
public sealed class InMemoryRepoStore : IRepoStore
{
    // Key: "{did}/{collection}/{rkey}"
    private readonly ConcurrentDictionary<string, RepoRecord> _records = new();
    // Key: "{did}/{cid}"
    private readonly ConcurrentDictionary<string, RepoBlob> _blobs = new();

    private static string RecordKey(string did, string collection, string rkey) =>
        $"{did}/{collection}/{rkey}";

    private static string BlobKey(string did, string cid) =>
        $"{did}/{cid}";

    // ── Records ──

    /// <inheritdoc />
    public Task PutRecordAsync(RepoRecord record, CancellationToken cancellationToken = default)
    {
        var key = RecordKey(record.Did, record.Collection, record.Rkey);
        _records[key] = record;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RepoRecord?> GetRecordAsync(string did, string collection, string rkey,
        CancellationToken cancellationToken = default)
    {
        _records.TryGetValue(RecordKey(did, collection, rkey), out var record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<RecordPage> ListRecordsAsync(string did, string collection,
        int limit = 50, string? cursor = null, bool reverse = false,
        CancellationToken cancellationToken = default)
    {
        var prefix = $"{did}/{collection}/";
        var matching = _records
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .OrderBy(r => r.Rkey, StringComparer.Ordinal);

        IEnumerable<RepoRecord> ordered = reverse ? matching.Reverse() : matching;

        if (cursor is not null)
            ordered = ordered.SkipWhile(r => r.Rkey != cursor).Skip(1);

        var page = ordered.Take(limit).ToList();
        var nextCursor = page.Count == limit ? page[^1].Rkey : null;

        return Task.FromResult(new RecordPage
        {
            Records = page,
            Cursor = nextCursor,
        });
    }

    /// <inheritdoc />
    public Task<bool> DeleteRecordAsync(string did, string collection, string rkey,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_records.TryRemove(RecordKey(did, collection, rkey), out _));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RepoRecord>> ListAllRecordsAsync(
        string did, CancellationToken cancellationToken = default)
    {
        var prefix = $"{did}/";
        IReadOnlyList<RepoRecord> all = _records
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .OrderBy(r => $"{r.Collection}/{r.Rkey}", StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(all);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListBlobCidsAsync(
        string did, CancellationToken cancellationToken = default)
    {
        var prefix = $"{did}/";
        IReadOnlyList<string> cids = _blobs
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(kv => kv.Value.Cid)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(cids);
    }

    /// <inheritdoc />
    public Task DeleteAllAsync(string did, CancellationToken cancellationToken = default)
    {
        var keysToRemove = _records.Keys.Where(k => k.StartsWith($"{did}/", StringComparison.Ordinal)).ToList();
        foreach (var key in keysToRemove)
            _records.TryRemove(key, out _);

        var blobKeysToRemove = _blobs.Keys.Where(k => k.StartsWith($"{did}/", StringComparison.Ordinal)).ToList();
        foreach (var key in blobKeysToRemove)
            _blobs.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    // ── Blobs ──

    /// <inheritdoc />
    public Task PutBlobAsync(RepoBlob blob, CancellationToken cancellationToken = default)
    {
        _blobs[BlobKey(blob.Did, blob.Cid)] = blob;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RepoBlob?> GetBlobAsync(string did, string cid,
        CancellationToken cancellationToken = default)
    {
        _blobs.TryGetValue(BlobKey(did, cid), out var blob);
        return Task.FromResult(blob);
    }

    /// <inheritdoc />
    public Task<bool> DeleteBlobAsync(string did, string cid,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_blobs.TryRemove(BlobKey(did, cid), out _));
    }
}
