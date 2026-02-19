using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Models;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.Com.AtProto.Repo;

/// <summary>
/// Client for com.atproto.repo.* XRPC endpoints.
/// Handles CRUD operations on repository records.
/// </summary>
public sealed class RepoClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal RepoClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    /// <summary>
    /// Create a new record in a repository collection.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="collection">The NSID of the collection (e.g., "app.bsky.feed.post").</param>
    /// <param name="record">The record data object. Must include $type field.</param>
    /// <param name="rkey">Optional record key. Server will generate one (TID) if not provided.</param>
    /// <param name="validate">Whether to validate against the Lexicon schema.</param>
    /// <param name="swapCommit">Optional CAS commit rev for optimistic concurrency.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CreateRecordResponse> CreateRecordAsync(
        string repo,
        string collection,
        object record,
        string? rkey = null,
        bool? validate = null,
        string? swapCommit = null,
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

        return _xrpc.ProcedureAsync<CreateRecordRequest, CreateRecordResponse>(
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
        string repo,
        string collection,
        string rkey,
        string? cid = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["repo"] = repo,
            ["collection"] = collection,
            ["rkey"] = rkey,
            ["cid"] = cid,
        };

        return _xrpc.QueryAsync<GetRecordResponse>(
            "com.atproto.repo.getRecord", parameters, cancellationToken);
    }

    /// <summary>
    /// Get a single record and deserialize the value to a typed object.
    /// </summary>
    public async Task<GetRecordResponse<T>> GetRecordAsync<T>(
        string repo,
        string collection,
        string rkey,
        string? cid = null,
        CancellationToken cancellationToken = default)
    {
        var response = await GetRecordAsync(repo, collection, rkey, cid, cancellationToken);
        var typedValue = response.Value.Deserialize<T>(AtProtoJsonDefaults.Options)
            ?? throw new InvalidOperationException($"Failed to deserialize record value to {typeof(T).Name}");

        return new GetRecordResponse<T>
        {
            Uri = response.Uri,
            Cid = response.Cid,
            Value = typedValue,
        };
    }

    /// <summary>
    /// Write a record to a repository, creating or updating as needed.
    /// </summary>
    public Task<PutRecordResponse> PutRecordAsync(
        string repo,
        string collection,
        string rkey,
        object record,
        bool? validate = null,
        string? swapRecord = null,
        string? swapCommit = null,
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

        return _xrpc.ProcedureAsync<PutRecordRequest, PutRecordResponse>(
            "com.atproto.repo.putRecord", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete a record from a repository.
    /// </summary>
    public Task<DeleteRecordResponse> DeleteRecordAsync(
        string repo,
        string collection,
        string rkey,
        string? swapRecord = null,
        string? swapCommit = null,
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

        return _xrpc.ProcedureAsync<DeleteRecordRequest, DeleteRecordResponse>(
            "com.atproto.repo.deleteRecord", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List records in a collection, with optional pagination.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="collection">The NSID of the collection.</param>
    /// <param name="limit">Max number of records per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="reverse">Reverse the order of results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListRecordsResponse> ListRecordsAsync(
        string repo,
        string collection,
        int? limit = null,
        string? cursor = null,
        bool? reverse = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["repo"] = repo,
            ["collection"] = collection,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
            ["reverse"] = reverse?.ToString()?.ToLowerInvariant(),
        };

        return _xrpc.QueryAsync<ListRecordsResponse>(
            "com.atproto.repo.listRecords", parameters, cancellationToken);
    }

    /// <summary>
    /// Enumerate all records in a collection using automatic pagination.
    /// </summary>
    public async IAsyncEnumerable<RecordEntry> ListAllRecordsAsync(
        string repo,
        string collection,
        int pageSize = 100,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var response = await ListRecordsAsync(repo, collection, pageSize, cursor, cancellationToken: cancellationToken);
            foreach (var record in response.Records)
                yield return record;

            cursor = response.Cursor;
        } while (cursor is not null);
    }

    /// <summary>
    /// Get information about a repository.
    /// </summary>
    public Task<DescribeRepoResponse> DescribeRepoAsync(
        string repo, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?> { ["repo"] = repo };
        return _xrpc.QueryAsync<DescribeRepoResponse>(
            "com.atproto.repo.describeRepo", parameters, cancellationToken);
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
        var response = await _xrpc.UploadBlobAsync<UploadBlobResponse>(
            "com.atproto.repo.uploadBlob", data, mimeType, cancellationToken);
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
    public Task<ApplyWritesResponse> ApplyWritesAsync(
        string repo,
        List<ApplyWriteOperation> writes,
        bool? validate = null,
        string? swapCommit = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ApplyWritesRequest
        {
            Repo = repo,
            Writes = writes,
            Validate = validate,
            SwapCommit = swapCommit,
        };

        return _xrpc.ProcedureAsync<ApplyWritesRequest, ApplyWritesResponse>(
            "com.atproto.repo.applyWrites", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List blobs that are missing from the repository.
    /// </summary>
    public Task<ListMissingBlobsResponse> ListMissingBlobsAsync(
        string? cursor = null, int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["cursor"] = cursor,
            ["limit"] = limit?.ToString(),
        };

        return _xrpc.QueryAsync<ListMissingBlobsResponse>(
            "com.atproto.repo.listMissingBlobs", parameters, cancellationToken);
    }
}
