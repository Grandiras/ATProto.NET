using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.Com.AtProto.Repo;

/// <summary>
/// Client for com.atproto.repo.* XRPC endpoints.
/// Handles CRUD operations on repository records.
/// </summary>
public sealed class RepoClient
{
    private readonly XrpcClient _xrpc;

    internal RepoClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Create a new record in a repository collection.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="collection">The NSID of the collection (e.g., "app.bsky.feed.post").</param>
    /// <param name="record">The record data object. Must include $type field.</param>
    /// <param name="rkey">Optional record key. Server will generate one (TID) if not provided.</param>
    /// <param name="validate">Whether to validate against the Lexicon schema.</param>
    /// <param name="swapCommit">Optional compare-and-swap guard: the commit CID the repository must be at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CreateRecordResponse> CreateRecordAsync(
        AtIdentifier repo,
        Nsid collection,
        object record,
        RecordKey? rkey = null,
        bool? validate = null,
        Cid? swapCommit = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateRecordRequest
        {
            Repo = repo,
            Collection = collection,
            Record = record,
            Rkey = rkey,
            Validate = validate,
            SwapCommit = swapCommit,
        };

        return _xrpc.ProcedureAsync<CreateRecordResponse>(
            "com.atproto.repo.createRecord", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get a single record from a repository.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="collection">The NSID of the collection.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">Optional specific version CID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetRecordResponse> GetRecordAsync(
        AtIdentifier repo,
        Nsid collection,
        RecordKey rkey,
        Cid? cid = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("repo", repo)
            .Add("collection", collection)
            .Add("rkey", rkey)
            .Add("cid", cid);

        return _xrpc.QueryAsync<GetRecordResponse>(
            "com.atproto.repo.getRecord", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the record an AT URI names.
    /// </summary>
    /// <param name="uri">The record's AT URI: it must name a collection and a record key.</param>
    /// <param name="cid">Optional specific version CID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="uri"/> does not name a record.</exception>
    public Task<GetRecordResponse> GetRecordAsync(
        AtUri uri,
        Cid? cid = null,
        CancellationToken cancellationToken = default)
    {
        var (collection, rkey) = RecordPath(uri);
        return GetRecordAsync(uri.Repo, collection, rkey, cid, cancellationToken);
    }

    /// <summary>
    /// Get a single record and deserialize the value to a typed object.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="collection">The NSID of the collection.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">Optional specific version CID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetRecordResponse<T>> GetRecordAsync<T>(
        AtIdentifier repo,
        Nsid collection,
        RecordKey rkey,
        Cid? cid = null,
        CancellationToken cancellationToken = default)
    {
        const string nsid = "com.atproto.repo.getRecord";

        var response = await GetRecordAsync(repo, collection, rkey, cid, cancellationToken);

        T typedValue;
        try
        {
            typedValue = response.Value.Deserialize<T>(AtProtoJsonDefaults.Options)
                ?? throw new XrpcResponseFormatException(nsid, $"Record {response.Uri} is null.");
        }
        catch (JsonException ex)
        {
            throw new XrpcResponseFormatException(
                nsid, $"Record {response.Uri} is not a valid {typeof(T).Name}: {ex.Message}", ex);
        }

        return new GetRecordResponse<T>
        {
            Uri = response.Uri,
            Cid = response.Cid,
            Value = typedValue,
        };
    }

    /// <summary>
    /// Get the record an AT URI names and deserialize the value to a typed object.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="uri">The record's AT URI: it must name a collection and a record key.</param>
    /// <param name="cid">Optional specific version CID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="uri"/> does not name a record.</exception>
    public Task<GetRecordResponse<T>> GetRecordAsync<T>(
        AtUri uri,
        Cid? cid = null,
        CancellationToken cancellationToken = default)
    {
        var (collection, rkey) = RecordPath(uri);
        return GetRecordAsync<T>(uri.Repo, collection, rkey, cid, cancellationToken);
    }

    /// <summary>
    /// Write a record to a repository, creating or updating as needed.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="collection">The NSID of the collection.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="record">The record data object.</param>
    /// <param name="validate">Whether to validate against the Lexicon schema.</param>
    /// <param name="swapRecord">Optional compare-and-swap guard: the CID the record must be at.</param>
    /// <param name="swapCommit">Optional compare-and-swap guard: the commit CID the repository must be at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<PutRecordResponse> PutRecordAsync(
        AtIdentifier repo,
        Nsid collection,
        RecordKey rkey,
        object record,
        bool? validate = null,
        Cid? swapRecord = null,
        Cid? swapCommit = null,
        CancellationToken cancellationToken = default)
    {
        var request = new PutRecordRequest
        {
            Repo = repo,
            Collection = collection,
            Rkey = rkey,
            Record = record,
            Validate = validate,
            SwapRecord = swapRecord,
            SwapCommit = swapCommit,
        };

        return _xrpc.ProcedureAsync<PutRecordResponse>(
            "com.atproto.repo.putRecord", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete a record from a repository.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="collection">The NSID of the collection.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="swapRecord">Optional compare-and-swap guard: the CID the record must be at.</param>
    /// <param name="swapCommit">Optional compare-and-swap guard: the commit CID the repository must be at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<DeleteRecordResponse> DeleteRecordAsync(
        AtIdentifier repo,
        Nsid collection,
        RecordKey rkey,
        Cid? swapRecord = null,
        Cid? swapCommit = null,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteRecordRequest
        {
            Repo = repo,
            Collection = collection,
            Rkey = rkey,
            SwapRecord = swapRecord,
            SwapCommit = swapCommit,
        };

        return _xrpc.ProcedureAsync<DeleteRecordResponse>(
            "com.atproto.repo.deleteRecord", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete the record an AT URI names.
    /// </summary>
    /// <param name="uri">The record's AT URI: it must name a collection and a record key.</param>
    /// <param name="swapRecord">Optional compare-and-swap guard: the CID the record must be at.</param>
    /// <param name="swapCommit">Optional compare-and-swap guard: the commit CID the repository must be at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="uri"/> does not name a record.</exception>
    public Task<DeleteRecordResponse> DeleteRecordAsync(
        AtUri uri,
        Cid? swapRecord = null,
        Cid? swapCommit = null,
        CancellationToken cancellationToken = default)
    {
        var (collection, rkey) = RecordPath(uri);
        return DeleteRecordAsync(uri.Repo, collection, rkey, swapRecord, swapCommit, cancellationToken);
    }

    /// <summary>
    /// List one page of records in a collection.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="collection">The NSID of the collection.</param>
    /// <param name="reverse">Reverse the order of results.</param>
    /// <param name="limit">Max number of records per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListRecordsResponse> ListRecordsAsync(
        AtIdentifier repo,
        Nsid collection,
        bool? reverse = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("repo", repo)
            .Add("collection", collection)
            .Add("limit", limit)
            .Add("cursor", cursor)
            .Add("reverse", reverse);

        return _xrpc.QueryAsync<ListRecordsResponse>(
            "com.atproto.repo.listRecords", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every record in a collection, fetching pages as needed.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="collection">The NSID of the collection.</param>
    /// <param name="reverse">Reverse the order of results.</param>
    /// <param name="pageSize">Records per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<RecordEntry> EnumerateRecordsAsync(
        AtIdentifier repo,
        Nsid collection,
        bool? reverse = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListRecordsResponse, RecordEntry>(
            (cursor, ct) => ListRecordsAsync(repo, collection, reverse, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get information about a repository.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<DescribeRepoResponse> DescribeRepoAsync(
        AtIdentifier repo, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("repo", repo);
        return _xrpc.QueryAsync<DescribeRepoResponse>(
            "com.atproto.repo.describeRepo", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Upload a blob (binary data) to the server.
    /// Returns a BlobRef that can be included in record data.
    /// </summary>
    /// <param name="data">The blob data stream.</param>
    /// <param name="mimeType">The MIME type (e.g., "image/png", "video/mp4").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<BlobRef> UploadBlobAsync(
        Stream data,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        var response = await _xrpc.UploadAsync<UploadBlobResponse>(
            "com.atproto.repo.uploadBlob", data, mimeType, cancellationToken: cancellationToken);
        return response.Blob;
    }

    /// <summary>
    /// Upload a blob from a file path.
    /// </summary>
    public async Task<BlobRef> UploadBlobAsync(
        string filePath,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        return await UploadBlobAsync(stream, mimeType, cancellationToken);
    }

    /// <summary>
    /// Upload a blob from a byte array.
    /// </summary>
    public async Task<BlobRef> UploadBlobAsync(
        byte[] data,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(data);
        return await UploadBlobAsync(stream, mimeType, cancellationToken);
    }

    /// <summary>
    /// Apply a batch of record writes in a single transaction.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="writes">The creates, updates and deletes to apply, in order.</param>
    /// <param name="validate">Whether to validate against the Lexicon schemas.</param>
    /// <param name="swapCommit">Optional compare-and-swap guard: the commit CID the repository must be at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ApplyWritesResponse> ApplyWritesAsync(
        AtIdentifier repo,
        IEnumerable<ApplyWriteOperation> writes,
        bool? validate = null,
        Cid? swapCommit = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ApplyWritesRequest
        {
            Repo = repo,
            Writes = [.. writes],
            Validate = validate,
            SwapCommit = swapCommit,
        };

        return _xrpc.ProcedureAsync<ApplyWritesResponse>(
            "com.atproto.repo.applyWrites", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List one page of the blobs the account's records reference but that were never uploaded,
    /// for example after a repository import.
    /// </summary>
    /// <param name="limit">Maximum number of results (1-1000, default 500).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListMissingBlobsResponse> ListMissingBlobsAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<ListMissingBlobsResponse>(
            "com.atproto.repo.listMissingBlobs", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every missing blob, fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Blobs per request (1-1000); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<MissingBlob> EnumerateMissingBlobsAsync(
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListMissingBlobsResponse, MissingBlob>(
            (cursor, ct) => ListMissingBlobsAsync(pageSize, cursor, ct),
            cancellationToken);

    private static (Nsid Collection, RecordKey Rkey) RecordPath(AtUri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri is { Collection: { } collection, RecordKey: { } rkey }
            ? (collection, rkey)
            : throw new ArgumentException(
                $"'{uri}' does not name a record: it needs a collection and a record key.", nameof(uri));
    }
}
