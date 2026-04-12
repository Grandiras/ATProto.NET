using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.Com.AtProto.Sync;

/// <summary>
/// Client for com.atproto.sync.* XRPC endpoints.
/// Handles repository sync operations and blob retrieval.
/// </summary>
public sealed class SyncClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal SyncClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    /// <summary>
    /// Get the latest commit CID and revision for a repository.
    /// </summary>
    /// <param name="did">The DID of the repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetLatestCommitResponse> GetLatestCommitAsync(
        string did, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?> { ["did"] = did };
        return _xrpc.QueryAsync<GetLatestCommitResponse>(
            "com.atproto.sync.getLatestCommit", parameters, cancellationToken);
    }

    /// <summary>
    /// Download a blob by DID and CID. Returns the raw byte stream.
    /// </summary>
    /// <param name="did">The DID of the repository containing the blob.</param>
    /// <param name="cid">The CID of the blob to download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Stream> GetBlobAsync(
        string did, string cid, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["did"] = did,
            ["cid"] = cid,
        };

        var result = await _xrpc.DownloadBlobAsync(
            "com.atproto.sync.getBlob", parameters, cancellationToken);
        return result.Stream;
    }

    /// <summary>
    /// Download an entire repository as a CAR file stream.
    /// </summary>
    /// <param name="did">The DID of the repository.</param>
    /// <param name="since">Optional cursor for incremental sync (rev of last seen commit).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Stream> GetRepoAsync(
        string did, string? since = null, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["did"] = did,
            ["since"] = since,
        };

        var result = await _xrpc.DownloadBlobAsync(
            "com.atproto.sync.getRepo", parameters, cancellationToken);
        return result.Stream;
    }

    /// <summary>
    /// List blob CIDs held by a repository.
    /// </summary>
    /// <param name="did">The DID of the repository.</param>
    /// <param name="since">Optional cursor for revision-based listing.</param>
    /// <param name="limit">Maximum number of results (default 500).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListBlobsResponse> ListBlobsAsync(
        string did,
        string? since = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["did"] = did,
            ["since"] = since,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<ListBlobsResponse>(
            "com.atproto.sync.listBlobs", parameters, cancellationToken);
    }

    /// <summary>
    /// Enumerate all repository DIDs hosted on a PDS.
    /// </summary>
    /// <param name="limit">Maximum number of results per page.</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListReposResponse> ListReposAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<ListReposResponse>(
            "com.atproto.sync.listRepos", parameters, cancellationToken);
    }

    /// <summary>
    /// Notify a relay/crawler that this PDS has new data.
    /// </summary>
    public async Task NotifyOfUpdateAsync(
        string hostname, CancellationToken cancellationToken = default)
    {
        var request = new NotifyOfUpdateRequest { Hostname = hostname };
        await _xrpc.ProcedureAsync<NotifyOfUpdateRequest, object>(
            "com.atproto.sync.notifyOfUpdate", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Request a crawl from a relay/crawler.
    /// </summary>
    public async Task RequestCrawlAsync(
        string hostname, CancellationToken cancellationToken = default)
    {
        var request = new RequestCrawlRequest { Hostname = hostname };
        await _xrpc.ProcedureAsync<RequestCrawlRequest, object>(
            "com.atproto.sync.requestCrawl", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the hosting status for a repository on this server.
    /// Expected to be implemented by PDS and Relay.
    /// </summary>
    /// <param name="did">The DID of the repo.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetRepoStatusResponse> GetRepoStatusAsync(
        string did, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?> { ["did"] = did };
        return _xrpc.QueryAsync<GetRepoStatusResponse>(
            "com.atproto.sync.getRepoStatus", parameters, cancellationToken);
    }

    /// <summary>
    /// Enumerate upstream hosts (PDS or relay instances) that this service consumes from.
    /// Implemented by relays.
    /// </summary>
    /// <param name="limit">Maximum number of results per page (default 200, max 1000).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListHostsResponse> ListHostsAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<ListHostsResponse>(
            "com.atproto.sync.listHosts", parameters, cancellationToken);
    }

    /// <summary>
    /// Get information about a specified upstream host. Implemented by relays.
    /// </summary>
    /// <param name="hostname">Hostname of the host (e.g., PDS or relay) being queried.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetHostStatusResponse> GetHostStatusAsync(
        string hostname, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?> { ["hostname"] = hostname };
        return _xrpc.QueryAsync<GetHostStatusResponse>(
            "com.atproto.sync.getHostStatus", parameters, cancellationToken);
    }

    /// <summary>
    /// Enumerate all DIDs which have records with the given collection NSID.
    /// Useful for efficient backfill of specific record types. New in Sync v1.1.
    /// </summary>
    /// <param name="collection">The collection NSID to filter by.</param>
    /// <param name="limit">Maximum number of results per page (default 500, max 2000).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListReposByCollectionResponse> ListReposByCollectionAsync(
        string collection,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["collection"] = collection,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<ListReposByCollectionResponse>(
            "com.atproto.sync.listReposByCollection", parameters, cancellationToken);
    }
}
