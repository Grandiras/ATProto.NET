using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Com.AtProto.Sync;

/// <summary>
/// Client for com.atproto.sync.* XRPC endpoints.
/// Handles repository sync operations and blob retrieval.
/// </summary>
public sealed class SyncClient
{
    private readonly XrpcClient _xrpc;

    internal SyncClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Get the latest commit CID and revision for a repository.
    /// </summary>
    /// <param name="did">The DID of the repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetLatestCommitResponse> GetLatestCommitAsync(
        Did did, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("did", did);
        return _xrpc.QueryAsync<GetLatestCommitResponse>(
            "com.atproto.sync.getLatestCommit", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Download a blob by DID and CID.
    /// </summary>
    /// <param name="did">The DID of the repository containing the blob.</param>
    /// <param name="cid">The CID of the blob to download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The blob's bytes and declared media type. Dispose it once read.</returns>
    public Task<XrpcStreamResponse> GetBlobAsync(
        Did did, Cid cid, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("did", did)
            .Add("cid", cid);

        return _xrpc.DownloadAsync(
            "com.atproto.sync.getBlob", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Download an entire repository as a CAR file.
    /// </summary>
    /// <param name="did">The DID of the repository.</param>
    /// <param name="since">Optional revision of the last seen commit, for an incremental export.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The CAR stream. Dispose it once read.</returns>
    public Task<XrpcStreamResponse> GetRepoAsync(
        Did did, Tid? since = null, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("did", did)
            .Add("since", since);

        return _xrpc.DownloadAsync(
            "com.atproto.sync.getRepo", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List one page of the blob CIDs held by a repository.
    /// </summary>
    /// <param name="did">The DID of the repository.</param>
    /// <param name="since">Optional revision: list only blobs added since it.</param>
    /// <param name="limit">Maximum number of results (1-1000, default 500).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListBlobsResponse> ListBlobsAsync(
        Did did,
        Tid? since = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("did", did)
            .Add("since", since)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<ListBlobsResponse>(
            "com.atproto.sync.listBlobs", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every blob CID held by a repository, fetching pages as needed.
    /// </summary>
    /// <param name="did">The DID of the repository.</param>
    /// <param name="since">Optional revision: list only blobs added since it.</param>
    /// <param name="pageSize">CIDs per request (1-1000); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<Cid> EnumerateBlobsAsync(
        Did did,
        Tid? since = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListBlobsResponse, Cid>(
            (cursor, ct) => ListBlobsAsync(did, since, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// List one page of the repositories hosted on a PDS.
    /// </summary>
    /// <param name="limit">Maximum number of results per page (1-1000, default 500).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListReposResponse> ListReposAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<ListReposResponse>(
            "com.atproto.sync.listRepos", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every repository hosted on a PDS, fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Repositories per request (1-1000); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<RepoInfo> EnumerateReposAsync(
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListReposResponse, RepoInfo>(
            (cursor, ct) => ListReposAsync(pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Notify a relay/crawler that this PDS has new data.
    /// </summary>
    [Obsolete("Deprecated upstream: use RequestCrawlAsync.")]
    public async Task NotifyOfUpdateAsync(
        string hostname, CancellationToken cancellationToken = default)
    {
        var request = new NotifyOfUpdateRequest { Hostname = hostname };
        await _xrpc.ProcedureAsync(
            "com.atproto.sync.notifyOfUpdate", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Request a crawl from a relay/crawler.
    /// </summary>
    public async Task RequestCrawlAsync(
        string hostname, CancellationToken cancellationToken = default)
    {
        var request = new RequestCrawlRequest { Hostname = hostname };
        await _xrpc.ProcedureAsync(
            "com.atproto.sync.requestCrawl", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the hosting status for a repository on this server.
    /// Expected to be implemented by PDS and Relay.
    /// </summary>
    /// <param name="did">The DID of the repo.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetRepoStatusResponse> GetRepoStatusAsync(
        Did did, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("did", did);
        return _xrpc.QueryAsync<GetRepoStatusResponse>(
            "com.atproto.sync.getRepoStatus", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List one page of the upstream hosts (PDS or relay instances) that this service consumes
    /// from. Implemented by relays.
    /// </summary>
    /// <param name="limit">Maximum number of results per page (default 200, max 1000).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListHostsResponse> ListHostsAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<ListHostsResponse>(
            "com.atproto.sync.listHosts", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every upstream host this service consumes from, fetching pages as needed.
    /// Implemented by relays.
    /// </summary>
    /// <param name="pageSize">Hosts per request (1-1000); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<HostInfo> EnumerateHostsAsync(
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListHostsResponse, HostInfo>(
            (cursor, ct) => ListHostsAsync(pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get information about a specified upstream host. Implemented by relays.
    /// </summary>
    /// <param name="hostname">Hostname of the host (e.g., PDS or relay) being queried.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetHostStatusResponse> GetHostStatusAsync(
        string hostname, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("hostname", hostname);
        return _xrpc.QueryAsync<GetHostStatusResponse>(
            "com.atproto.sync.getHostStatus", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List one page of the DIDs which have records in the given collection.
    /// Useful for efficient backfill of specific record types. New in Sync v1.1.
    /// </summary>
    /// <param name="collection">The collection NSID to filter by.</param>
    /// <param name="limit">Maximum number of results per page (default 500, max 2000).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListReposByCollectionResponse> ListReposByCollectionAsync(
        Nsid collection,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("collection", collection)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<ListReposByCollectionResponse>(
            "com.atproto.sync.listReposByCollection", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every DID which has records in the given collection, fetching pages as needed.
    /// </summary>
    /// <param name="collection">The collection NSID to filter by.</param>
    /// <param name="pageSize">Repositories per request (1-2000); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<CollectionRepoInfo> EnumerateReposByCollectionAsync(
        Nsid collection,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListReposByCollectionResponse, CollectionRepoInfo>(
            (cursor, ct) => ListReposByCollectionAsync(collection, pageSize, cursor, ct),
            cancellationToken);
}
