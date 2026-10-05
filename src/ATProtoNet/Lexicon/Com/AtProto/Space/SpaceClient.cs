using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Spaces;

namespace ATProtoNet.Lexicon.Com.AtProto.Space;

/// <summary>Client for <c>com.atproto.space.*</c> XRPC endpoints — the permissioned data protocol.</summary>
/// <remarks>
/// <para>Permissioned data is AT Protocol's second data protocol, alongside public broadcast.
/// It keeps the same shape — DID-based authority, per-user repos, Lexicon-typed records,
/// applications crawling hosts to build views — but adds an access perimeter: a
/// <see cref="SpaceUri">space</see>. It provides <b>access control, not confidentiality</b>;
/// data is not end-to-end encrypted, and every service that handles it can read it.</para>
/// <para>The methods here fall into three groups by who serves them:</para>
/// <list type="bullet">
/// <item><description><b>PDS methods</b> — <see cref="GetDelegationTokenAsync"/>,
/// <see cref="ListSpacesAsync"/>, and the record writes. Served by the authenticated user's own
/// PDS and authenticated with OAuth.</description></item>
/// <item><description><b>Repo methods</b> — <see cref="GetRecordAsync(SpaceUri, Did, Nsid, RecordKey, CancellationToken)"/>,
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

    internal SpaceClient(XrpcClient xrpc) => _xrpc = xrpc;

    // ── Credentials ──────────────────────────────────────────

    /// <summary>Mints a delegation token for a space, proving this application is acting on the user's behalf. Served by the user's own PDS.</summary>
    /// <param name="space">The space the token is for.</param>
    /// <remarks>
    /// The token asserts only the user-to-app delegation; it says nothing about whether the
    /// user is a member of the space, which is the authority's determination. It is single-use,
    /// short-lived, and addressed to the space authority — exchange it promptly via
    /// <see cref="GetSpaceCredentialAsync"/>. The session must hold a covering <c>space:</c>
    /// scope with a <c>read</c> grant; <c>read_self</c> alone does not confer this method.
    /// </remarks>
    public Task<GetDelegationTokenResponse> GetDelegationTokenAsync(
        SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        return _xrpc.QueryAsync<GetDelegationTokenResponse>(
            "com.atproto.space.getDelegationToken",
            new XrpcParams()
                .Add("space", space),
            cancellationToken: cancellationToken);
    }

    /// <summary>Exchanges a delegation token for a space credential. Called on the space authority.</summary>
    /// <param name="space">The space to read.</param>
    /// <param name="clientAttestation">
    /// The application's client attestation JWT, required only when the space gates on app identity.
    /// </param>
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
        SpaceUri space,
        string? clientAttestation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        return _xrpc.ProcedureAsync<GetSpaceCredentialResponse>(
            "com.atproto.space.getSpaceCredential",
            new GetSpaceCredentialRequest { Space = space, ClientAttestation = clientAttestation },
            cancellationToken: cancellationToken);
    }

    // ── Discovery ────────────────────────────────────────────

    /// <summary>Lists one page of the spaces the authenticated user holds a repo in.</summary>
    /// <param name="type">Filter to spaces of this type.</param>
    /// <param name="did">Filter to spaces under this authority DID.</param>
    /// <param name="limit">Maximum number of results per page (1–100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <remarks>
    /// This is <em>spaces the user has written data to</em>, not spaces the user is a member of.
    /// A PDS only tracks the former: membership is the authority's business, and for a space
    /// anchored elsewhere the user's PDS never sees it. An account migrating its data enumerates
    /// its permissioned repos through this method.
    /// </remarks>
    public Task<ListSpacesResponse> ListSpacesAsync(
        Nsid? type = null,
        Did? did = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<ListSpacesResponse>(
            "com.atproto.space.listSpaces",
            new XrpcParams()
                .Add("type", type)
                .Add("did", did)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);

    /// <summary>Lists one page of the repos that hold data in a space — the writer set. Served by the space host.</summary>
    /// <param name="limit">Maximum number of results per page (1–1000, default 100).</param>
    /// <param name="cursor">
    /// A <c>spaceRev</c> checkpoint, exclusive: the cursor of a previous page, or a <c>spaceRev</c>
    /// already processed. Sent as the plain string it is, so a cursor the server does not recognize
    /// (an old DID-based one) is its to refuse.
    /// </param>
    /// <remarks>
    /// <para>This is the sync boundary, not an access-control list: it enumerates accounts that
    /// have <em>written at least one record</em>, never the broader set allowed to write and
    /// never readers, which the protocol does not enumerate at all.</para>
    /// <para>It is also only what the authority claims, kept current by the write notifications
    /// it has received. A listed account's repo host is the source of truth. Treat the writer
    /// set as a starting point for discovery and confirm each repo by syncing it. Entries come
    /// in ascending <c>spaceRev</c> order and <paramref name="cursor"/> is an exclusive <c>spaceRev</c>
    /// checkpoint, so a syncer resumes from the last one it processed and re-syncs only what advanced
    /// (<see cref="ATProtoNet.Spaces.SpaceSyncer.ListChangedReposAsync"/>).</para>
    /// </remarks>
    public Task<ListSpaceReposResponse> ListReposAsync(
        SpaceUri space,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        return _xrpc.QueryAsync<ListSpaceReposResponse>(
            "com.atproto.space.listRepos",
            new XrpcParams()
                .Add("space", space)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);
    }

    /// <summary>Enumerates a space's whole writer set, fetching pages as needed.</summary>
    /// <param name="pageSize">Repos per request (1–1000); <see langword="null"/> for the server default.</param>
    public IAsyncEnumerable<SpaceRepoView> EnumerateReposAsync(
        SpaceUri space,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        return Pagination.EnumerateAsync<ListSpaceReposResponse, SpaceRepoView>(
            (cursor, ct) => ListReposAsync(space, pageSize, cursor, ct),
            cancellationToken);
    }

    // ── Reads ────────────────────────────────────────────────

    /// <summary>Gets a single record from a permissioned repo.</summary>
    /// <param name="repo">The DID of the account whose repo to read from.</param>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    public Task<GetSpaceRecordResponse> GetRecordAsync(
        SpaceUri space,
        Did repo,
        Nsid collection,
        RecordKey rkey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(rkey);

        return _xrpc.QueryAsync<GetSpaceRecordResponse>(
            "com.atproto.space.getRecord",
            new XrpcParams()
                .Add("space", space)
                .Add("repo", repo)
                .Add("collection", collection)
                .Add("rkey", rkey),
            cancellationToken: cancellationToken);
    }

    /// <summary>Gets the record a space record URI names.</summary>
    /// <param name="uri">The record's URI, which names its space, author, collection and key.</param>
    public Task<GetSpaceRecordResponse> GetRecordAsync(
        SpaceRecordUri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return GetRecordAsync(uri.Space, uri.Author, uri.Collection, uri.Rkey, cancellationToken);
    }

    /// <summary>Lists one page of the records in an account's repo within a space.</summary>
    /// <param name="repo">The DID of the account whose repo to list.</param>
    /// <param name="collection">Restrict to one collection. Lists across all collections when omitted.</param>
    /// <param name="reverse">Reverse the order of the returned records.</param>
    /// <param name="excludeValues">
    /// Return only metadata (collection, rkey, cid). Combined with <c>getLatestCommit</c> this
    /// is the cheap way to heal a copy that has diverged only slightly: diff the listing against
    /// what you hold and fetch just the differing records.
    /// </param>
    /// <param name="limit">Maximum number of results per page (1–1000, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<ListSpaceRecordsResponse> ListRecordsAsync(
        SpaceUri space,
        Did repo,
        Nsid? collection = null,
        bool? reverse = null,
        bool? excludeValues = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repo);

        return _xrpc.QueryAsync<ListSpaceRecordsResponse>(
            "com.atproto.space.listRecords",
            new XrpcParams()
                .Add("space", space)
                .Add("repo", repo)
                .Add("collection", collection)
                .Add("reverse", reverse)
                .Add("excludeValues", excludeValues)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);
    }

    /// <summary>Enumerates every record in an account's repo within a space, fetching pages as needed.</summary>
    /// <param name="repo">The DID of the account whose repo to list.</param>
    /// <param name="collection">Restrict to one collection.</param>
    /// <param name="reverse">Reverse the order of the returned records.</param>
    /// <param name="excludeValues">Return only metadata.</param>
    /// <param name="pageSize">Records per request (1–1000); <see langword="null"/> for the server default.</param>
    public IAsyncEnumerable<SpaceRecordView> EnumerateRecordsAsync(
        SpaceUri space,
        Did repo,
        Nsid? collection = null,
        bool? reverse = null,
        bool? excludeValues = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repo);

        return Pagination.EnumerateAsync<ListSpaceRecordsResponse, SpaceRecordView>(
            (cursor, ct) => ListRecordsAsync(space, repo, collection, reverse, excludeValues, pageSize, cursor, ct),
            cancellationToken);
    }

    /// <summary>Gets the current signed commit for an account's repo within a space.</summary>
    /// <param name="repo">The DID of the account.</param>
    /// <remarks>
    /// Verify it with <see cref="SpaceCommitVerifier"/> before trusting its digest — the commit
    /// arrives over the wire like anything else.
    /// </remarks>
    public Task<GetSpaceLatestCommitResponse> GetLatestCommitAsync(
        SpaceUri space, Did repo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repo);

        return _xrpc.QueryAsync<GetSpaceLatestCommitResponse>(
            "com.atproto.space.getLatestCommit",
            new XrpcParams()
                .Add("space", space)
                .Add("repo", repo),
            cancellationToken: cancellationToken);
    }

    /// <summary>Downloads an account's whole permissioned repo as a CAR file, for full-state recovery.</summary>
    /// <param name="repo">The DID of the account.</param>
    /// <param name="excludeValues">
    /// Return only the commit and index roots, with no record blocks. The index still
    /// authenticates against the commit.
    /// </param>
    /// <returns>The CAR stream, which the caller disposes. Verify it with <see cref="SpaceRepoCar.Verify"/>.</returns>
    public Task<XrpcStreamResponse> GetRepoAsync(
        SpaceUri space,
        Did repo,
        bool? excludeValues = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repo);

        return _xrpc.DownloadAsync(
            "com.atproto.space.getRepo",
            new XrpcParams()
                .Add("space", space)
                .Add("repo", repo)
                .Add("excludeValues", excludeValues),
            cancellationToken: cancellationToken);
    }

    /// <summary>Lists an account's operation log for a space, the primary incremental sync mechanism.</summary>
    /// <param name="repo">The DID of the account.</param>
    /// <param name="since">Return operations after this revision — the caller's own sync position.</param>
    /// <param name="excludeValues">Return operation metadata only, without inlined record values.</param>
    /// <param name="limit">Maximum number of operations per page (1–1000, default 100).</param>
    /// <param name="cursor">Opaque pagination cursor. Takes precedence over <paramref name="since"/>.</param>
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
        SpaceUri space,
        Did repo,
        Tid? since = null,
        bool? excludeValues = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repo);

        return _xrpc.QueryAsync<ListSpaceRepoOpsResponse>(
            "com.atproto.space.listRepoOps",
            new XrpcParams()
                .Add("space", space)
                .Add("repo", repo)
                .Add("since", since)
                .Add("excludeValues", excludeValues)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);
    }

    /// <summary>Downloads a blob referenced from a record in a permissioned space.</summary>
    /// <param name="repo">The DID of the account whose repo holds the blob.</param>
    /// <param name="cid">The blob's CID.</param>
    /// <remarks>
    /// Blobs are not uploaded through this namespace. A space record references a blob uploaded
    /// with <c>com.atproto.repo.uploadBlob</c>, so a client writing blob-bearing records into a
    /// space needs a <c>blob:</c> permission alongside its <c>space:</c> one.
    /// </remarks>
    /// <returns>The blob's bytes and declared media type, which the caller disposes.</returns>
    public Task<XrpcStreamResponse> GetBlobAsync(
        SpaceUri space, Did repo, Cid cid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(cid);

        return _xrpc.DownloadAsync(
            "com.atproto.space.getBlob",
            new XrpcParams()
                .Add("space", space)
                .Add("repo", repo)
                .Add("cid", cid),
            cancellationToken: cancellationToken);
    }

    /// <summary>Lists one page of the CIDs of blobs referenced by an account's records within a space.</summary>
    /// <param name="repo">The DID of the account.</param>
    /// <param name="since">Optional revision of the permissioned repo to list blobs since.</param>
    /// <param name="limit">Maximum number of results per page (1–1000, default 500).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <remarks>
    /// Scoped to one space. Blobs behind permissioned records are never enumerated by
    /// <c>com.atproto.sync.listBlobs</c>, which is unauthenticated.
    /// </remarks>
    public Task<ListSpaceBlobsResponse> ListBlobsAsync(
        SpaceUri space,
        Did repo,
        Tid? since = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repo);

        return _xrpc.QueryAsync<ListSpaceBlobsResponse>(
            "com.atproto.space.listBlobs",
            new XrpcParams()
                .Add("space", space)
                .Add("repo", repo)
                .Add("since", since)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);
    }

    // ── Writes ───────────────────────────────────────────────

    /// <summary>Creates a record in the caller's permissioned repo for a space.</summary>
    /// <param name="repo">The DID of the repo to write to (the authenticated member).</param>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="record">The record. Must carry a <c>$type</c>.</param>
    /// <param name="rkey">The record key. Generated by the host when omitted.</param>
    /// <param name="validate">Lexicon validation behaviour; <see langword="null"/> validates known Lexicons only.</param>
    /// <remarks>Writes accept only an OAuth credential — a write is attributed to the authoring user.</remarks>
    public Task<SpaceWriteResult> CreateRecordAsync(
        SpaceUri space,
        Did repo,
        Nsid collection,
        object record,
        RecordKey? rkey = null,
        bool? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(record);

        return _xrpc.ProcedureAsync<SpaceWriteResult>(
            "com.atproto.space.createRecord",
            new CreateSpaceRecordRequest(
                Space: space,
                Repo: repo,
                Collection: collection,
                Rkey: rkey,
                Validate: validate,
                Record: record),
            cancellationToken: cancellationToken);
    }

    /// <summary>Creates or updates a record in the caller's permissioned repo for a space.</summary>
    /// <param name="repo">The DID of the repo to write to (the authenticated member).</param>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="record">The record to write.</param>
    /// <param name="validate">Lexicon validation behaviour; <see langword="null"/> validates known Lexicons only.</param>
    public Task<SpaceWriteResult> PutRecordAsync(
        SpaceUri space,
        Did repo,
        Nsid collection,
        RecordKey rkey,
        object record,
        bool? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(rkey);
        ArgumentNullException.ThrowIfNull(record);

        return _xrpc.ProcedureAsync<SpaceWriteResult>(
            "com.atproto.space.putRecord",
            new PutSpaceRecordRequest(
                Space: space,
                Repo: repo,
                Collection: collection,
                Rkey: rkey,
                Validate: validate,
                Record: record),
            cancellationToken: cancellationToken);
    }

    /// <summary>Deletes a record from the caller's permissioned repo, or ensures it does not exist. Succeeds whether or not the record was present.</summary>
    /// <param name="repo">The DID of the repo to delete from (the authenticated member).</param>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    public async Task DeleteRecordAsync(
        SpaceUri space,
        Did repo,
        Nsid collection,
        RecordKey rkey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(rkey);

        await _xrpc.ProcedureAsync(
            "com.atproto.space.deleteRecord",
            new DeleteSpaceRecordRequest(Space: space, Repo: repo, Collection: collection, Rkey: rkey),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the record a space record URI names from the caller's permissioned repo, or ensures it does not exist.</summary>
    /// <param name="uri">The record's URI. Its author must be the authenticated member.</param>
    public Task DeleteRecordAsync(SpaceRecordUri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return DeleteRecordAsync(uri.Space, uri.Author, uri.Collection, uri.Rkey, cancellationToken);
    }

    /// <summary>Applies a batch of creates, updates, and deletes to one permissioned repo atomically.</summary>
    /// <param name="repo">The DID of the repo to write to (the authenticated member).</param>
    /// <param name="writes">The operations.</param>
    /// <param name="validate">Lexicon validation behaviour across all operations.</param>
    /// <remarks>
    /// The batch lands under a single revision, which is how a syncer recognises the operations
    /// as one atomic change: entries sharing a <c>rev</c> belong together.
    /// </remarks>
    public Task<ApplySpaceWritesResponse> ApplyWritesAsync(
        SpaceUri space,
        Did repo,
        IEnumerable<SpaceWriteOp> writes,
        bool? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(writes);

        return _xrpc.ProcedureAsync<ApplySpaceWritesResponse>(
            "com.atproto.space.applyWrites",
            new ApplySpaceWritesRequest(Space: space, Repo: repo, Validate: validate, Writes: [.. writes]),
            cancellationToken: cancellationToken);
    }

    // ── Write notifications ──────────────────────────────────

    /// <summary>Registers a service to be notified when repos in a space advance.</summary>
    /// <param name="service">
    /// The subscriber's service identifier: a DID with an optional service fragment naming the
    /// entry in its DID document to deliver to.
    /// </param>
    /// <remarks>
    /// <para>Called on the space host, this subscribes to writes for every repo in the space,
    /// which is what a syncer normally wants. Called on a particular repo host it subscribes to
    /// that host's repos.</para>
    /// <para>Notifications carry no record data — only that a given repo reached a new revision
    /// and hash — and are best-effort. A dropped notification is not a lost write: the repo is
    /// caught up by a later notification or by a catch-up over the writer set from a spaceRev checkpoint.</para>
    /// <para>Authenticated with a space credential. Re-registering replaces the existing
    /// registration and extends its expiry.</para>
    /// </remarks>
    public Task<RegisterNotifyResponse> RegisterNotifyAsync(
        SpaceUri space, string service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        return _xrpc.ProcedureAsync<RegisterNotifyResponse>(
            "com.atproto.space.registerNotify",
            new RegisterNotifyRequest { Space = space, Service = service },
            cancellationToken: cancellationToken);
    }

    /// <summary>Withdraws a write-notification registration. Idempotent.</summary>
    /// <param name="service">The subscriber's service identifier, as passed to <see cref="RegisterNotifyAsync"/>.</param>
    public async Task UnregisterNotifyAsync(
        SpaceUri space, string service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        await _xrpc.ProcedureAsync(
            "com.atproto.space.unregisterNotify",
            new UnregisterNotifyRequest { Space = space, Service = service },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Notifies that a repo in a space advanced to a new revision.</summary>
    /// <param name="repo">The DID of the account whose repo advanced.</param>
    /// <param name="repoRev">The repo's revision after the write.</param>
    /// <param name="hash">The repo's commit hash after the write.</param>
    /// <remarks>
    /// Sent by a repo host to the space host with service auth, which sequences it and forwards it
    /// (with a <c>spaceRev</c>) to the services registered for the space. Answers
    /// <see cref="SpaceErrors.SpaceNotFound"/> for an unknown space and
    /// <see cref="SpaceErrors.FutureRev"/> for a revision more than five minutes ahead.
    /// </remarks>
    public async Task NotifyWriteAsync(
        SpaceUri space,
        Did repo,
        Tid repoRev,
        byte[] hash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(repoRev);
        ArgumentNullException.ThrowIfNull(hash);

        await _xrpc.ProcedureAsync(
            "com.atproto.space.notifyWrite",
            new NotifyWriteRequest { Space = space, Repo = repo, RepoRev = repoRev, Hash = hash },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Notifies a syncing service that a space was deleted and its data should be dropped.</summary>
    /// <param name="space">The deleted space.</param>
    /// <remarks>
    /// Sent by the space authority to the services registered for the space, best-effort. A
    /// syncer that misses it learns on its next credential renewal, which answers
    /// <see cref="SpaceErrors.SpaceDeleted"/>. A renewal that fails for any other reason says
    /// nothing about the space, and the syncer keeps its copy.
    /// </remarks>
    public async Task NotifySpaceDeletedAsync(SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        await _xrpc.ProcedureAsync(
            "com.atproto.space.notifySpaceDeleted",
            new NotifySpaceDeletedRequest(Space: space),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
