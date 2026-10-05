using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Repo;

namespace ATProtoNet.Lexicon.Com.AtProto.Sync;

/// <summary>Client for com.atproto.sync.* XRPC endpoints. Handles repository sync operations and blob retrieval.</summary>
public sealed class SyncClient
{
    // The largest record proof GetVerifiedRecordAsync reads. A proof is one record and a few tree nodes,
    // far below this; the ceiling only stops a hostile server from making the client buffer without end.
    private const int MaxRecordProofBytes = 16 * 1024 * 1024;

    private readonly XrpcClient _xrpc;

    internal SyncClient(XrpcClient xrpc) => _xrpc = xrpc;

    /// <summary>Get the latest commit CID and revision for a repository.</summary>
    /// <param name="did">The DID of the repository.</param>
    public Task<GetLatestCommitResponse> GetLatestCommitAsync(
        Did did, CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetLatestCommitResponse>(
            "com.atproto.sync.getLatestCommit", new XrpcParams().Add("did", did), cancellationToken: cancellationToken);

    /// <summary>Download a blob by DID and CID.</summary>
    /// <param name="did">The DID of the repository containing the blob.</param>
    /// <param name="cid">The CID of the blob to download.</param>
    /// <returns>The blob's bytes and declared media type. Dispose it once read.</returns>
    public Task<XrpcStreamResponse> GetBlobAsync(
        Did did, Cid cid, CancellationToken cancellationToken = default) =>
        _xrpc.DownloadAsync(
            "com.atproto.sync.getBlob",
            new XrpcParams()
                .Add("did", did)
                .Add("cid", cid),
            cancellationToken: cancellationToken);

    /// <summary>Download the blocks that prove a record's presence or absence in the current version of a repository, as a CAR file: the signed commit, the tree nodes on the path to the record, and the record itself when it exists.</summary>
    /// <remarks>
    /// <see cref="GetVerifiedRecordAsync"/> downloads and verifies the proof in one call; this
    /// returns the raw CAR, for example to pass on or verify later with
    /// <see cref="RecordProof.Verify"/>.
    /// </remarks>
    /// <param name="did">The DID of the repository.</param>
    /// <param name="collection">The record's collection.</param>
    /// <param name="rkey">The record key.</param>
    /// <returns>The CAR stream. Dispose it once read.</returns>
    public Task<XrpcStreamResponse> GetRecordAsync(
        Did did, Nsid collection, RecordKey rkey, CancellationToken cancellationToken = default) =>
        _xrpc.DownloadAsync(
            "com.atproto.sync.getRecord",
            new XrpcParams()
                .Add("did", did)
                .Add("collection", collection)
                .Add("rkey", rkey),
            cancellationToken: cancellationToken);

    /// <summary>Download a record together with its proof, and verify the proof against the repository's signed commit: the record is then known to be what the account committed, whichever server delivered it.</summary>
    /// <remarks>
    /// The signing key must come from a source you trust for the account — its DID document,
    /// resolved from the PLC directory or the <c>did:web</c> host — rather than from the server
    /// being checked. The proof is read into memory, up to 16 MiB.
    /// </remarks>
    /// <param name="did">The DID of the repository.</param>
    /// <param name="collection">The record's collection.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="signingKey">
    /// The repository's signing key as a <c>did:key</c>: the <c>#atproto</c> verification method
    /// of the account's DID document.
    /// </param>
    /// <returns>
    /// The verified record. <see cref="VerifiedRecord.Exists"/> is <see langword="false"/> when the
    /// proof shows the repository holds no such record.
    /// </returns>
    /// <exception cref="RepoVerificationException">
    /// The proof does not verify, is incomplete, or is larger than 16 MiB.
    /// </exception>
    /// <exception cref="FormatException"><paramref name="signingKey"/> is not a valid <c>did:key</c>.</exception>
    public async Task<VerifiedRecord> GetVerifiedRecordAsync(
        Did did,
        Nsid collection,
        RecordKey rkey,
        string signingKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKey);

        ReadOnlyMemory<byte> car;
        #pragma warning disable CA2007 // The resource keeps the default context for disposal: ConfigureAwait on it would change its declared type.
        await using (var response = await GetRecordAsync(did, collection, rkey, cancellationToken).ConfigureAwait(false))
        #pragma warning restore CA2007
        {
            car = await response.Content.ReadBoundedAsync(MaxRecordProofBytes, response.ContentLength, cancellationToken).ConfigureAwait(false)
                ?? throw new RepoVerificationException(
                    $"The record proof for {did}/{collection}/{rkey} is larger than {MaxRecordProofBytes} bytes.");
        }

        return RecordProof.Verify(car.Span, did, collection, rkey, signingKey);
    }

    /// <summary>Download blocks from a repository by CID — records or MST nodes — as a CAR file.</summary>
    /// <remarks>
    /// Read the result with <see cref="CarReader.FromStreamAsync"/>, and call
    /// <see cref="CarReader.VerifyAllBlockCids"/> before trusting the blocks.
    /// </remarks>
    /// <param name="did">The DID of the repository.</param>
    /// <param name="cids">The CIDs of the blocks to fetch.</param>
    /// <returns>The CAR stream. Dispose it once read.</returns>
    public Task<XrpcStreamResponse> GetBlocksAsync(
        Did did, IEnumerable<Cid> cids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cids);

        return _xrpc.DownloadAsync(
            "com.atproto.sync.getBlocks",
            new XrpcParams()
                .Add("did", did)
                .AddAll("cids", cids.Select(cid => cid.Value)),
            cancellationToken: cancellationToken);
    }

    /// <summary>Download an entire repository as a CAR file.</summary>
    /// <param name="did">The DID of the repository.</param>
    /// <param name="since">Optional revision of the last seen commit, for an incremental export.</param>
    /// <returns>The CAR stream. Dispose it once read.</returns>
    public Task<XrpcStreamResponse> GetRepoAsync(
        Did did, Tid? since = null, CancellationToken cancellationToken = default) =>
        _xrpc.DownloadAsync(
            "com.atproto.sync.getRepo",
            new XrpcParams()
                .Add("did", did)
                .Add("since", since),
            cancellationToken: cancellationToken);

    /// <summary>List one page of the blob CIDs held by a repository.</summary>
    /// <param name="did">The DID of the repository.</param>
    /// <param name="since">Optional revision: list only blobs added since it.</param>
    /// <param name="limit">Maximum number of results (1-1000, default 500).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<ListBlobsResponse> ListBlobsAsync(
        Did did,
        Tid? since = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<ListBlobsResponse>(
            "com.atproto.sync.listBlobs",
            new XrpcParams()
                .Add("did", did)
                .Add("since", since)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);

    /// <summary>List one page of the repositories hosted on a PDS.</summary>
    /// <param name="limit">Maximum number of results per page (1-1000, default 500).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<ListReposResponse> ListReposAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<ListReposResponse>(
            "com.atproto.sync.listRepos",
            new XrpcParams().Add("limit", limit).Add("cursor", cursor), cancellationToken: cancellationToken);

    /// <summary>Notify a relay/crawler that this PDS has new data.</summary>
    [Obsolete("Deprecated upstream: use RequestCrawlAsync.")]
    public Task NotifyOfUpdateAsync(
        string hostname, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "com.atproto.sync.notifyOfUpdate", new HostnameRequest(hostname), cancellationToken: cancellationToken);

    /// <summary>Request a crawl from a relay/crawler.</summary>
    public Task RequestCrawlAsync(
        string hostname, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "com.atproto.sync.requestCrawl", new HostnameRequest(hostname), cancellationToken: cancellationToken);

    /// <summary>Get the hosting status for a repository on this server. Expected to be implemented by PDS and Relay.</summary>
    /// <param name="did">The DID of the repo.</param>
    public Task<GetRepoStatusResponse> GetRepoStatusAsync(
        Did did, CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetRepoStatusResponse>(
            "com.atproto.sync.getRepoStatus", new XrpcParams().Add("did", did), cancellationToken: cancellationToken);

    /// <summary>List one page of the upstream hosts (PDS or relay instances) that this service consumes from. Implemented by relays.</summary>
    /// <param name="limit">Maximum number of results per page (default 200, max 1000).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<ListHostsResponse> ListHostsAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<ListHostsResponse>(
            "com.atproto.sync.listHosts",
            new XrpcParams().Add("limit", limit).Add("cursor", cursor), cancellationToken: cancellationToken);

    /// <summary>Get information about a specified upstream host. Implemented by relays.</summary>
    /// <param name="hostname">Hostname of the host (e.g., PDS or relay) being queried.</param>
    public Task<GetHostStatusResponse> GetHostStatusAsync(
        string hostname, CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetHostStatusResponse>(
            "com.atproto.sync.getHostStatus",
            new XrpcParams().Add("hostname", hostname), cancellationToken: cancellationToken);

    /// <summary>List one page of the DIDs which have records in the given collection. Useful for efficient backfill of specific record types. New in Sync v1.1.</summary>
    /// <param name="collection">The collection NSID to filter by.</param>
    /// <param name="limit">Maximum number of results per page (default 500, max 2000).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<ListReposByCollectionResponse> ListReposByCollectionAsync(
        Nsid collection,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<ListReposByCollectionResponse>(
            "com.atproto.sync.listReposByCollection",
            new XrpcParams()
                .Add("collection", collection)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);
}
