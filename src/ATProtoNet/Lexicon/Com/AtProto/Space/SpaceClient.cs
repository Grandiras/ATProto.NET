using ATProtoNet.Http;
using ATProtoNet.Spaces;

namespace ATProtoNet.Lexicon.Com.AtProto.Space;

/// <summary>
/// Client for <c>com.atproto.space.*</c> XRPC endpoints — the permissioned data protocol.
/// </summary>
/// <remarks>
/// <para>Permissioned data is AT Protocol's second data protocol, alongside public broadcast.
/// It keeps the same shape — DID-based authority, per-user repos, Lexicon-typed records,
/// applications crawling hosts to build views — but adds an access perimeter: a
/// <see cref="SpaceUri">space</see>. It provides <b>access control, not confidentiality</b>;
/// data is not end-to-end encrypted, and every service that handles it can read it.</para>
/// <para>The methods here fall into three groups by who serves them:</para>
/// <list type="bullet">
/// <item><description><b>PDS methods</b> — <see cref="GetDelegationTokenAsync(string, CancellationToken)"/>,
/// <see cref="ListSpacesAsync"/>, and the record writes. Served by the authenticated user's own
/// PDS and authenticated with OAuth.</description></item>
/// <item><description><b>Repo methods</b> — <see cref="GetRecordAsync"/>,
/// <see cref="ListRecordsAsync"/>, <see cref="GetRepoAsync"/>, <see cref="ListRepoOpsAsync"/>,
/// and the rest of the read/sync surface. Served by whichever host holds the repo, and accept
/// either OAuth (for the caller's own repo) or a space credential (for a syncer).</description></item>
/// <item><description><b>Host methods</b> — <see cref="GetSpaceCredentialAsync"/>,
/// <see cref="ListReposAsync"/>, and the notification registrations. Served by the space
/// authority.</description></item>
/// </list>
/// <para>Reading <em>another member's</em> repo needs a space credential, which is DPoP-bound
/// and cannot be presented as a bearer token. <see cref="SpaceCredentialProvider"/> runs that
/// exchange and <see cref="SpaceSyncer"/> drives sync on top of it; this client is the raw
/// endpoint surface underneath both.</para>
/// </remarks>
public sealed class SpaceClient
{
    private readonly XrpcClient _xrpc;

    internal SpaceClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    // ──────────────────────────────────────────────────────────
    //  Credentials
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Mints a delegation token for a space, proving this application is acting on the user's
    /// behalf. Served by the user's own PDS.
    /// </summary>
    /// <param name="space">The space the token is for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The token asserts only the user-to-app delegation; it says nothing about whether the
    /// user is a member of the space, which is the authority's determination. It is single-use,
    /// short-lived, and addressed to the space authority — exchange it promptly via
    /// <see cref="GetSpaceCredentialAsync"/>. The session must hold a covering <c>space:</c>
    /// scope with a <c>read</c> grant; <c>read_self</c> alone does not confer this method.
    /// </remarks>
    public Task<GetDelegationTokenResponse> GetDelegationTokenAsync(
        string space, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);

        var parameters = new XrpcParams().Add("space", space);
        return _xrpc.QueryAsync<GetDelegationTokenResponse>(
            "com.atproto.space.getDelegationToken", parameters, cancellationToken);
    }

    /// <inheritdoc cref="GetDelegationTokenAsync(string, CancellationToken)"/>
    /// <param name="space">The space the token is for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetDelegationTokenResponse> GetDelegationTokenAsync(
        SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        return GetDelegationTokenAsync(space.Value, cancellationToken);
    }

    /// <summary>
    /// Exchanges a delegation token for a space credential. Called on the space authority.
    /// </summary>
    /// <param name="space">The space to read.</param>
    /// <param name="clientAttestation">
    /// The application's client attestation JWT, required only when the space gates on app identity.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <para>This request must carry the delegation token as its authorization and a DPoP proof
    /// signed by the key the resulting credential is to be bound to. Neither is applied here —
    /// this method is the raw endpoint. Use <see cref="SpaceCredentialProvider"/> for the whole
    /// exchange, including the DPoP binding and credential caching.</para>
    /// <para>Whether the space requires a client attestation is not advertised: an application
    /// either learns it out of band or discovers it by asking without one and seeing whether an
    /// <see cref="SpaceErrors.AppNotAuthorized"/> comes back.</para>
    /// </remarks>
    public Task<GetSpaceCredentialResponse> GetSpaceCredentialAsync(
        string space,
        string? clientAttestation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);

        var request = new GetSpaceCredentialRequest { Space = space, ClientAttestation = clientAttestation };
        return _xrpc.ProcedureAsync<GetSpaceCredentialRequest, GetSpaceCredentialResponse>(
            "com.atproto.space.getSpaceCredential", request, cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Discovery
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Lists the spaces the authenticated user holds a repo in.
    /// </summary>
    /// <param name="type">Filter to spaces of this type (an NSID).</param>
    /// <param name="did">Filter to spaces under this authority DID.</param>
    /// <param name="limit">Maximum number of results per page (1–100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// This is <em>spaces the user has written data to</em>, not spaces the user is a member of.
    /// A PDS only tracks the former: membership is the authority's business, and for a space
    /// anchored elsewhere the user's PDS never sees it. An account migrating its data enumerates
    /// its permissioned repos through this method.
    /// </remarks>
    public Task<ListSpacesResponse> ListSpacesAsync(
        string? type = null,
        string? did = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("type", type)
            .Add("did", did)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<ListSpacesResponse>(
            "com.atproto.space.listSpaces", parameters, cancellationToken);
    }

    /// <summary>
    /// Lists the repos that hold data in a space — the writer set. Served by the space host.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="limit">Maximum number of results per page (1–1000, default 100).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <para>This is the sync boundary, not an access-control list: it enumerates accounts that
    /// have <em>written at least one record</em>, never the broader set allowed to write and
    /// never readers, which the protocol does not enumerate at all.</para>
    /// <para>It is also only what the authority claims, kept current by the write notifications
    /// it has received. A listed account's repo host is the source of truth. Treat the writer
    /// set as a starting point for discovery and confirm each repo by syncing it. Because each
    /// entry carries a <c>rev</c>, a syncer can sweep the whole space by comparing revisions and
    /// re-syncing only what advanced.</para>
    /// </remarks>
    public Task<ListSpaceReposResponse> ListReposAsync(
        string space,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);

        var parameters = new XrpcParams()
            .Add("space", space)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<ListSpaceReposResponse>(
            "com.atproto.space.listRepos", parameters, cancellationToken);
    }

    /// <summary>
    /// Enumerates a space's whole writer set, following pagination.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="pageSize">Results per request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<SpaceRepoView> EnumerateReposAsync(
        string space,
        int? pageSize = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var page = await ListReposAsync(space, pageSize, cursor, cancellationToken);
            foreach (var repo in page.Repos)
                yield return repo;

            cursor = page.Repos.Count == 0 ? null : page.Cursor;
        }
        while (!string.IsNullOrEmpty(cursor));
    }

    // ──────────────────────────────────────────────────────────
    //  Reads
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Gets a single record from a permissioned repo.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the account whose repo to read from.</param>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetSpaceRecordResponse> GetRecordAsync(
        string space,
        string repo,
        string collection,
        string rkey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(rkey);

        var parameters = new XrpcParams()
            .Add("space", space)
            .Add("repo", repo)
            .Add("collection", collection)
            .Add("rkey", rkey);

        return _xrpc.QueryAsync<GetSpaceRecordResponse>(
            "com.atproto.space.getRecord", parameters, cancellationToken);
    }

    /// <summary>
    /// Lists the records in an account's repo within a space.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the account whose repo to list.</param>
    /// <param name="collection">Restrict to one collection. Lists across all collections when omitted.</param>
    /// <param name="limit">Maximum number of results per page (1–1000, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="reverse">Reverse the order of the returned records.</param>
    /// <param name="excludeValues">
    /// Return only metadata (collection, rkey, cid). Combined with <c>getLatestCommit</c> this
    /// is the cheap way to heal a copy that has diverged only slightly: diff the listing against
    /// what you hold and fetch just the differing records.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListSpaceRecordsResponse> ListRecordsAsync(
        string space,
        string repo,
        string? collection = null,
        int? limit = null,
        string? cursor = null,
        bool? reverse = null,
        bool? excludeValues = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var parameters = new XrpcParams()
            .Add("space", space)
            .Add("repo", repo)
            .Add("collection", collection)
            .Add("limit", limit)
            .Add("cursor", cursor)
            .Add("reverse", reverse)
            .Add("excludeValues", excludeValues);

        return _xrpc.QueryAsync<ListSpaceRecordsResponse>(
            "com.atproto.space.listRecords", parameters, cancellationToken);
    }

    /// <summary>
    /// Enumerates every record in an account's repo within a space, following pagination.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the account whose repo to list.</param>
    /// <param name="collection">Restrict to one collection.</param>
    /// <param name="pageSize">Results per request.</param>
    /// <param name="excludeValues">Return only metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<SpaceRecordView> EnumerateRecordsAsync(
        string space,
        string repo,
        string? collection = null,
        int? pageSize = null,
        bool? excludeValues = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var page = await ListRecordsAsync(
                space, repo, collection, pageSize, cursor, reverse: null, excludeValues, cancellationToken);

            foreach (var record in page.Records)
                yield return record;

            cursor = page.Records.Count == 0 ? null : page.Cursor;
        }
        while (!string.IsNullOrEmpty(cursor));
    }

    /// <summary>
    /// Gets the current signed commit for an account's repo within a space.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Verify it with <see cref="SpaceCommitVerifier"/> before trusting its digest — the commit
    /// arrives over the wire like anything else.
    /// </remarks>
    public Task<GetSpaceLatestCommitResponse> GetLatestCommitAsync(
        string space, string repo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var parameters = new XrpcParams()
            .Add("space", space)
            .Add("repo", repo);

        return _xrpc.QueryAsync<GetSpaceLatestCommitResponse>(
            "com.atproto.space.getLatestCommit", parameters, cancellationToken);
    }

    /// <summary>
    /// Downloads an account's whole permissioned repo as a CAR file, for full-state recovery.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the account.</param>
    /// <param name="excludeValues">
    /// Return only the commit and index roots, with no record blocks. The index still
    /// authenticates against the commit.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The CAR stream. Verify it with <see cref="SpaceRepoCar.Verify"/>.</returns>
    public async Task<Stream> GetRepoAsync(
        string space,
        string repo,
        bool? excludeValues = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var parameters = new XrpcParams()
            .Add("space", space)
            .Add("repo", repo)
            .Add("excludeValues", excludeValues);

        var result = await _xrpc.DownloadBlobAsync(
            "com.atproto.space.getRepo", parameters, cancellationToken);
        return result.Stream;
    }

    /// <summary>
    /// Lists an account's operation log for a space, the primary incremental sync mechanism.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the account.</param>
    /// <param name="since">Return operations after this revision — the caller's own sync position.</param>
    /// <param name="limit">Maximum number of operations per page (1–1000, default 100).</param>
    /// <param name="cursor">Opaque pagination cursor. Takes precedence over <paramref name="since"/>.</param>
    /// <param name="excludeValues">Return operation metadata only, without inlined record values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <para>The oplog is a transport optimization, not a committed data structure. A host may
    /// compact or drop it, and it does not survive account migration, so omitting
    /// <paramref name="since"/> returns whatever window is retained rather than the repo's full
    /// history. A syncer whose <paramref name="since"/> is no longer available falls back to
    /// <see cref="GetRepoAsync"/>.</para>
    /// <para>When the response reaches the head of the log it also carries the repo's current
    /// signed commit, which is what a syncer compares its own running set hash against.</para>
    /// </remarks>
    public Task<ListSpaceRepoOpsResponse> ListRepoOpsAsync(
        string space,
        string repo,
        string? since = null,
        int? limit = null,
        string? cursor = null,
        bool? excludeValues = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var parameters = new XrpcParams()
            .Add("space", space)
            .Add("repo", repo)
            .Add("since", since)
            .Add("limit", limit)
            .Add("cursor", cursor)
            .Add("excludeValues", excludeValues);

        return _xrpc.QueryAsync<ListSpaceRepoOpsResponse>(
            "com.atproto.space.listRepoOps", parameters, cancellationToken);
    }

    /// <summary>
    /// Downloads a blob referenced from a record in a permissioned space.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the account whose repo holds the blob.</param>
    /// <param name="cid">The blob's CID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Blobs are not uploaded through this namespace. A space record references a blob uploaded
    /// with <c>com.atproto.repo.uploadBlob</c>, so a client writing blob-bearing records into a
    /// space needs a <c>blob:</c> permission alongside its <c>space:</c> one.
    /// </remarks>
    public async Task<Stream> GetBlobAsync(
        string space, string repo, string cid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(cid);

        var parameters = new XrpcParams()
            .Add("space", space)
            .Add("repo", repo)
            .Add("cid", cid);

        var result = await _xrpc.DownloadBlobAsync(
            "com.atproto.space.getBlob", parameters, cancellationToken);
        return result.Stream;
    }

    /// <summary>
    /// Lists the CIDs of blobs referenced by an account's records within a space.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the account.</param>
    /// <param name="since">Optional revision of the permissioned repo to list blobs since.</param>
    /// <param name="limit">Maximum number of results per page (1–1000, default 500).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Scoped to one space. Blobs behind permissioned records are never enumerated by
    /// <c>com.atproto.sync.listBlobs</c>, which is unauthenticated.
    /// </remarks>
    public Task<ListSpaceBlobsResponse> ListBlobsAsync(
        string space,
        string repo,
        string? since = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var parameters = new XrpcParams()
            .Add("space", space)
            .Add("repo", repo)
            .Add("since", since)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<ListSpaceBlobsResponse>(
            "com.atproto.space.listBlobs", parameters, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Writes
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a record in the caller's permissioned repo for a space.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the repo to write to (the authenticated member).</param>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="record">The record. Must carry a <c>$type</c>.</param>
    /// <param name="rkey">The record key. Generated by the host when omitted.</param>
    /// <param name="validate">Lexicon validation behaviour; <see langword="null"/> validates known Lexicons only.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>Writes accept only an OAuth credential — a write is attributed to the authoring user.</remarks>
    public Task<SpaceWriteResult> CreateRecordAsync(
        string space,
        string repo,
        string collection,
        object record,
        string? rkey = null,
        bool? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(record);

        var request = new CreateSpaceRecordRequest
        {
            Space = space,
            Repo = repo,
            Collection = collection,
            Rkey = rkey,
            Validate = validate,
            Record = record,
        };

        return _xrpc.ProcedureAsync<CreateSpaceRecordRequest, SpaceWriteResult>(
            "com.atproto.space.createRecord", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Creates or updates a record in the caller's permissioned repo for a space.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the repo to write to (the authenticated member).</param>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="record">The record to write.</param>
    /// <param name="validate">Lexicon validation behaviour; <see langword="null"/> validates known Lexicons only.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<SpaceWriteResult> PutRecordAsync(
        string space,
        string repo,
        string collection,
        string rkey,
        object record,
        bool? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(rkey);
        ArgumentNullException.ThrowIfNull(record);

        var request = new PutSpaceRecordRequest
        {
            Space = space,
            Repo = repo,
            Collection = collection,
            Rkey = rkey,
            Validate = validate,
            Record = record,
        };

        return _xrpc.ProcedureAsync<PutSpaceRecordRequest, SpaceWriteResult>(
            "com.atproto.space.putRecord", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Deletes a record from the caller's permissioned repo, or ensures it does not exist.
    /// Succeeds whether or not the record was present.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the repo to delete from (the authenticated member).</param>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeleteRecordAsync(
        string space,
        string repo,
        string collection,
        string rkey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(rkey);

        var request = new DeleteSpaceRecordRequest
        {
            Space = space,
            Repo = repo,
            Collection = collection,
            Rkey = rkey,
        };

        await _xrpc.ProcedureAsync<DeleteSpaceRecordRequest>(
            "com.atproto.space.deleteRecord", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Applies a batch of creates, updates, and deletes to one permissioned repo atomically.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the repo to write to (the authenticated member).</param>
    /// <param name="writes">The operations.</param>
    /// <param name="validate">Lexicon validation behaviour across all operations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The batch lands under a single revision, which is how a syncer recognises the operations
    /// as one atomic change: entries sharing a <c>rev</c> belong together.
    /// </remarks>
    public Task<ApplySpaceWritesResponse> ApplyWritesAsync(
        string space,
        string repo,
        IEnumerable<SpaceWriteOp> writes,
        bool? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(writes);

        var request = new ApplySpaceWritesRequest
        {
            Space = space,
            Repo = repo,
            Validate = validate,
            Writes = [.. writes],
        };

        return _xrpc.ProcedureAsync<ApplySpaceWritesRequest, ApplySpaceWritesResponse>(
            "com.atproto.space.applyWrites", request, cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Write notifications
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a service to be notified when repos in a space advance.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="service">
    /// The subscriber's service identifier: a DID with an optional service fragment naming the
    /// entry in its DID document to deliver to.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <para>Called on the space host, this subscribes to writes for every repo in the space,
    /// which is what a syncer normally wants. Called on a particular repo host it subscribes to
    /// that host's repos.</para>
    /// <para>Notifications carry no record data — only that a given repo reached a new revision
    /// and hash — and are best-effort. A dropped notification is not a lost write: the repo is
    /// caught up by a later notification or by a periodic sweep over the writer set.</para>
    /// <para>Authenticated with a space credential. Re-registering replaces the existing
    /// registration and extends its expiry.</para>
    /// </remarks>
    public Task<RegisterNotifyResponse> RegisterNotifyAsync(
        string space, string service, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var request = new RegisterNotifyRequest { Space = space, Service = service };
        return _xrpc.ProcedureAsync<RegisterNotifyRequest, RegisterNotifyResponse>(
            "com.atproto.space.registerNotify", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Withdraws a write-notification registration. Idempotent.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="service">The subscriber's service identifier, as passed to <see cref="RegisterNotifyAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UnregisterNotifyAsync(
        string space, string service, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var request = new UnregisterNotifyRequest { Space = space, Service = service };
        await _xrpc.ProcedureAsync<UnregisterNotifyRequest>(
            "com.atproto.space.unregisterNotify", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Notifies that a repo in a space advanced to a new revision.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the account whose repo advanced.</param>
    /// <param name="rev">The revision of the write.</param>
    /// <param name="hash">The repo's commit hash after the write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Sent by a repo host to the space host, and forwarded by the space host to the services
    /// registered for the space. Authenticated with service auth.
    /// </remarks>
    public async Task NotifyWriteAsync(
        string space,
        string repo,
        string rev,
        byte[] hash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(rev);
        ArgumentNullException.ThrowIfNull(hash);

        var request = new NotifyWriteRequest { Space = space, Repo = repo, Rev = rev, Hash = hash };
        await _xrpc.ProcedureAsync<NotifyWriteRequest>(
            "com.atproto.space.notifyWrite", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Notifies a syncing service that a space was deleted and its data should be dropped.
    /// </summary>
    /// <param name="space">The deleted space.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Sent by the space authority to the services registered for the space, best-effort. A
    /// syncer that misses it learns on its next credential renewal, which answers
    /// <see cref="SpaceErrors.SpaceDeleted"/>. A renewal that fails for any other reason says
    /// nothing about the space, and the syncer keeps its copy.
    /// </remarks>
    public async Task NotifySpaceDeletedAsync(string space, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);

        var request = new NotifySpaceDeletedRequest { Space = space };
        await _xrpc.ProcedureAsync<NotifySpaceDeletedRequest>(
            "com.atproto.space.notifySpaceDeleted", request, cancellationToken: cancellationToken);
    }
}
