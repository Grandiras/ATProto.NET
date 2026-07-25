using System.Text.Json;

namespace ATProtoNet.Pds;

/// <summary>
/// Represents a single record in a repository collection.
/// </summary>
public sealed class RepoRecord
{
    /// <summary>The DID of the repository owner.</summary>
    public required string Did { get; init; }

    /// <summary>The collection NSID (e.g. "app.bsky.feed.post").</summary>
    public required string Collection { get; init; }

    /// <summary>The record key within the collection.</summary>
    public required string Rkey { get; init; }

    /// <summary>The record value as JSON.</summary>
    public required JsonElement Value { get; set; }

    /// <summary>The CID of this version of the record.</summary>
    public required string Cid { get; set; }

    /// <summary>When the record was created or last written.</summary>
    public DateTimeOffset IndexedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents a blob stored in a repository.
/// </summary>
public sealed class RepoBlob
{
    /// <summary>The DID of the repository owner.</summary>
    public required string Did { get; init; }

    /// <summary>The CID of the blob.</summary>
    public required string Cid { get; init; }

    /// <summary>MIME type of the blob.</summary>
    public required string MimeType { get; init; }

    /// <summary>Size in bytes.</summary>
    public required long Size { get; init; }

    /// <summary>The blob data.</summary>
    public required byte[] Data { get; init; }
}

/// <summary>
/// A paged list result for records.
/// </summary>
public sealed class RecordPage
{
    /// <summary>The records in this page.</summary>
    public required IReadOnlyList<RepoRecord> Records { get; init; }

    /// <summary>Cursor for the next page, or null if no more results.</summary>
    public string? Cursor { get; init; }
}

/// <summary>
/// Persistent store for repository records and blobs.
/// </summary>
public interface IRepoStore
{
    // ── Records ──

    /// <summary>Create or overwrite a record.</summary>
    Task PutRecordAsync(RepoRecord record, CancellationToken cancellationToken = default);

    /// <summary>Get a specific record.</summary>
    Task<RepoRecord?> GetRecordAsync(string did, string collection, string rkey,
        CancellationToken cancellationToken = default);

    /// <summary>List records in a collection with pagination.</summary>
    Task<RecordPage> ListRecordsAsync(string did, string collection,
        int limit = 50, string? cursor = null, bool reverse = false,
        CancellationToken cancellationToken = default);

    /// <summary>Delete a specific record. Returns true if it existed.</summary>
    Task<bool> DeleteRecordAsync(string did, string collection, string rkey,
        CancellationToken cancellationToken = default);

    /// <summary>Delete all records belonging to a DID (for account deletion).</summary>
    Task DeleteAllAsync(string did, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates every record in a repository, across all collections, in MST key order
    /// (<c>collection/rkey</c>, ordinal).
    /// </summary>
    /// <remarks>
    /// Required for federation: the Merkle Search Tree and the signed commit are derived from
    /// the complete record set, so <see cref="PdsRepoManager"/> re-reads it on every commit.
    /// The default implementation throws — a store written before federation support keeps
    /// working for plain repo CRUD, and only federation surfaces the gap, with a message that
    /// says exactly what to implement.
    /// </remarks>
    Task<IReadOnlyList<RepoRecord>> ListAllRecordsAsync(
        string did, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"{GetType().Name} does not implement IRepoStore.ListAllRecordsAsync, which is " +
            "required to build the Merkle Search Tree for a federating repository. Implement it " +
            "to return every record for a DID across all collections.");

    /// <summary>
    /// Enumerates the CIDs of every blob held for a DID. Backs <c>com.atproto.sync.listBlobs</c>.
    /// </summary>
    /// <remarks>
    /// As with <see cref="ListAllRecordsAsync"/>, the default implementation throws so that
    /// pre-existing stores keep compiling and only fail on the federation surface they don't support.
    /// </remarks>
    Task<IReadOnlyList<string>> ListBlobCidsAsync(
        string did, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"{GetType().Name} does not implement IRepoStore.ListBlobCidsAsync, which is " +
            "required by com.atproto.sync.listBlobs.");

    // ── Blobs ──

    /// <summary>Store a blob.</summary>
    Task PutBlobAsync(RepoBlob blob, CancellationToken cancellationToken = default);

    /// <summary>Get a blob by DID and CID.</summary>
    Task<RepoBlob?> GetBlobAsync(string did, string cid,
        CancellationToken cancellationToken = default);

    /// <summary>Delete a blob.</summary>
    Task<bool> DeleteBlobAsync(string did, string cid,
        CancellationToken cancellationToken = default);
}
