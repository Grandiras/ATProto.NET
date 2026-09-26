using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;

namespace ATProtoNet.Lexicon.App.Bsky.Graph;

/// <summary>
/// Client for app.bsky.graph.* XRPC endpoints.
/// Handles follows, blocks, mutes, and lists.
/// </summary>
public sealed class GraphClient
{
    private readonly XrpcClient _xrpc;

    internal GraphClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    // ──────────────────────────────────────────────────────────
    //  Follows
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get one page of the accounts following an actor.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="sort">The order: <c>latest</c> or <c>top</c>; the server's default when omitted.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetFollowersResponse> GetFollowersAsync(
        AtIdentifier actor, string? sort = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("actor", actor)
            .Add("sort", sort)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetFollowersResponse>(
            "app.bsky.graph.getFollowers", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the accounts following an actor, fetching pages as needed.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="sort">The order: <c>latest</c> or <c>top</c>; the server's default when omitted.</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ProfileView> EnumerateFollowersAsync(
        AtIdentifier actor, string? sort = null, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetFollowersResponse, ProfileView>(
            (cursor, ct) => GetFollowersAsync(actor, sort, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get one page of the accounts an actor follows.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="sort">The order: <c>latest</c> or <c>top</c>; the server's default when omitted.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetFollowsResponse> GetFollowsAsync(
        AtIdentifier actor, string? sort = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("actor", actor)
            .Add("sort", sort)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetFollowsResponse>(
            "app.bsky.graph.getFollows", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the accounts an actor follows, fetching pages as needed.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="sort">The order: <c>latest</c> or <c>top</c>; the server's default when omitted.</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ProfileView> EnumerateFollowsAsync(
        AtIdentifier actor, string? sort = null, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetFollowsResponse, ProfileView>(
            (cursor, ct) => GetFollowsAsync(actor, sort, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get suggested follows based on a given actor.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetSuggestedFollowsByActorResponse> GetSuggestedFollowsByActorAsync(
        AtIdentifier actor, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("actor", actor);
        return _xrpc.QueryAsync<GetSuggestedFollowsByActorResponse>(
            "app.bsky.graph.getSuggestedFollowsByActor", parameters, cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Blocks
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get one page of the accounts the authenticated user blocks.
    /// </summary>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetBlocksResponse> GetBlocksAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetBlocksResponse>(
            "app.bsky.graph.getBlocks", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the accounts the authenticated user blocks, fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ProfileView> EnumerateBlocksAsync(
        int? pageSize = null, CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetBlocksResponse, ProfileView>(
            (cursor, ct) => GetBlocksAsync(pageSize, cursor, ct),
            cancellationToken);

    // ──────────────────────────────────────────────────────────
    //  Mutes
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get one page of the accounts the authenticated user mutes.
    /// </summary>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetMutesResponse> GetMutesAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetMutesResponse>(
            "app.bsky.graph.getMutes", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the accounts the authenticated user mutes, fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ProfileView> EnumerateMutesAsync(
        int? pageSize = null, CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetMutesResponse, ProfileView>(
            (cursor, ct) => GetMutesAsync(pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Mute an actor: fully, or only their reposts and/or quote posts.
    /// </summary>
    /// <remarks>
    /// With neither scope set the actor is fully muted; with either set, only that content is.
    /// A repeated call replaces the stored scope rather than adding to it.
    /// </remarks>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="onlyReposts">Mute only the actor's reposts.</param>
    /// <param name="onlyQuoteposts">Mute only the actor's quote posts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task MuteActorAsync(
        AtIdentifier actor, bool? onlyReposts = null, bool? onlyQuoteposts = null,
        CancellationToken cancellationToken = default)
    {
        var request = new MuteActorRequest
        {
            Actor = actor,
            OnlyReposts = onlyReposts,
            OnlyQuoteposts = onlyQuoteposts,
        };
        await _xrpc.ProcedureAsync(
            "app.bsky.graph.muteActor", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unmute an actor.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UnmuteActorAsync(
        AtIdentifier actor, CancellationToken cancellationToken = default)
    {
        var request = new MuteActorRequest { Actor = actor };
        await _xrpc.ProcedureAsync(
            "app.bsky.graph.unmuteActor", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Mute all members of a list.
    /// </summary>
    /// <param name="list">The AT-URI of the list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task MuteActorListAsync(
        AtUri list, CancellationToken cancellationToken = default)
    {
        var request = new MuteActorListRequest { List = list };
        await _xrpc.ProcedureAsync(
            "app.bsky.graph.muteActorList", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unmute a list.
    /// </summary>
    /// <param name="list">The AT-URI of the list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UnmuteActorListAsync(
        AtUri list, CancellationToken cancellationToken = default)
    {
        var request = new MuteActorListRequest { List = list };
        await _xrpc.ProcedureAsync(
            "app.bsky.graph.unmuteActorList", request, cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Lists
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get one page of the lists an actor created.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="purposes">
    /// Only lists with these purposes, by short name: <c>modlist</c>, <c>curatelist</c>.
    /// <see langword="null"/> for every purpose the server supports.
    /// </param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetListsResponse> GetListsAsync(
        AtIdentifier actor, IEnumerable<string>? purposes = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("actor", actor)
            .AddAll("purposes", purposes)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetListsResponse>(
            "app.bsky.graph.getLists", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the lists an actor created, fetching pages as needed.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="purposes">
    /// Only lists with these purposes, by short name: <c>modlist</c>, <c>curatelist</c>.
    /// <see langword="null"/> for every purpose the server supports.
    /// </param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ListView> EnumerateListsAsync(
        AtIdentifier actor, IEnumerable<string>? purposes = null, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetListsResponse, ListView>(
            (cursor, ct) => GetListsAsync(actor, purposes, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get a list and one page of its members.
    /// </summary>
    /// <param name="list">The AT-URI of the list.</param>
    /// <param name="limit">Max members per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetListResponse> GetListAsync(
        AtUri list, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("list", list)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetListResponse>(
            "app.bsky.graph.getList", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every member of a list, fetching pages as needed.
    /// </summary>
    /// <param name="list">The AT-URI of the list.</param>
    /// <param name="pageSize">Members per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ListItemView> EnumerateListMembersAsync(
        AtUri list, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetListResponse, ListItemView>(
            (cursor, ct) => GetListAsync(list, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get one page of the lists the authenticated user blocks.
    /// </summary>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetListBlocksResponse> GetListBlocksAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetListBlocksResponse>(
            "app.bsky.graph.getListBlocks", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the lists the authenticated user blocks, fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ListView> EnumerateListBlocksAsync(
        int? pageSize = null, CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetListBlocksResponse, ListView>(
            (cursor, ct) => GetListBlocksAsync(pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get one page of the lists the authenticated user mutes.
    /// </summary>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetListMutesResponse> GetListMutesAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetListMutesResponse>(
            "app.bsky.graph.getListMutes", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the lists the authenticated user mutes, fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ListView> EnumerateListMutesAsync(
        int? pageSize = null, CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetListMutesResponse, ListView>(
            (cursor, ct) => GetListMutesAsync(pageSize, cursor, ct),
            cancellationToken);

    // ──────────────────────────────────────────────────────────
    //  Relationships
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get the relationships between an actor and other accounts.
    /// </summary>
    /// <param name="actor">Handle or DID of the account the relationships are relative to.</param>
    /// <param name="others">Handles or DIDs of the other accounts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetRelationshipsResponse> GetRelationshipsAsync(
        AtIdentifier actor, IEnumerable<AtIdentifier>? others = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("actor", actor)
            .AddAll("others", others?.Select(other => other.Value));

        return _xrpc.QueryAsync<GetRelationshipsResponse>(
            "app.bsky.graph.getRelationships", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get one page of an actor's followers that the authenticated user also follows.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetKnownFollowersResponse> GetKnownFollowersAsync(
        AtIdentifier actor, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("actor", actor)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetKnownFollowersResponse>(
            "app.bsky.graph.getKnownFollowers", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate an actor's followers that the authenticated user also follows, fetching pages as
    /// needed.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ProfileView> EnumerateKnownFollowersAsync(
        AtIdentifier actor, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetKnownFollowersResponse, ProfileView>(
            (cursor, ct) => GetKnownFollowersAsync(actor, pageSize, cursor, ct),
            cancellationToken);

    // ──────────────────────────────────────────────────────────
    //  Thread mutes
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Mute a thread (stop receiving notifications).
    /// </summary>
    /// <param name="root">The AT-URI of the thread's root post.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task MuteThreadAsync(
        AtUri root, CancellationToken cancellationToken = default)
    {
        var request = new MuteThreadRequest { Root = root };
        await _xrpc.ProcedureAsync(
            "app.bsky.graph.muteThread", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unmute a thread.
    /// </summary>
    /// <param name="root">The AT-URI of the thread's root post.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UnmuteThreadAsync(
        AtUri root, CancellationToken cancellationToken = default)
    {
        var request = new MuteThreadRequest { Root = root };
        await _xrpc.ProcedureAsync(
            "app.bsky.graph.unmuteThread", request, cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Starter packs
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get a starter pack by its AT-URI.
    /// </summary>
    /// <param name="starterPack">The AT-URI of the starter pack record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetStarterPackResponse> GetStarterPackAsync(
        AtUri starterPack, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("starterPack", starterPack);
        return _xrpc.QueryAsync<GetStarterPackResponse>(
            "app.bsky.graph.getStarterPack", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get multiple starter packs by their AT-URIs.
    /// </summary>
    /// <param name="uris">The AT-URIs of the starter pack records.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetStarterPacksResponse> GetStarterPacksAsync(
        IEnumerable<AtUri> uris, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .AddAll("uris", uris.Select(uri => uri.Value));
        return _xrpc.QueryAsync<GetStarterPacksResponse>(
            "app.bsky.graph.getStarterPacks", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get one page of the starter packs an actor created.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetActorStarterPacksResponse> GetActorStarterPacksAsync(
        AtIdentifier actor, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("actor", actor)
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<GetActorStarterPacksResponse>(
            "app.bsky.graph.getActorStarterPacks", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the starter packs an actor created, fetching pages as needed.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<StarterPackViewBasic> EnumerateActorStarterPacksAsync(
        AtIdentifier actor, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetActorStarterPacksResponse, StarterPackViewBasic>(
            (cursor, ct) => GetActorStarterPacksAsync(actor, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Search for starter packs, one page at a time.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Max results per page (1-100, default 25).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<SearchStarterPacksResponse> SearchStarterPacksAsync(
        string query, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("q", query)
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<SearchStarterPacksResponse>(
            "app.bsky.graph.searchStarterPacks", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every starter pack matching a search, fetching pages as needed.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<StarterPackViewBasic> EnumerateSearchStarterPacksAsync(
        string query, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<SearchStarterPacksResponse, StarterPackViewBasic>(
            (cursor, ct) => SearchStarterPacksAsync(query, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Search for starter packs, one page at a time, returning full starter pack views and an
    /// estimate of the hit count.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Max results per page (1-100, default 25).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<SearchStarterPacksV2Response> SearchStarterPacksV2Async(
        string query, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("q", query)
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<SearchStarterPacksV2Response>(
            "app.bsky.graph.searchStarterPacksV2", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every starter pack matching a <see cref="SearchStarterPacksV2Async"/> search,
    /// fetching pages as needed.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<StarterPackView> EnumerateSearchStarterPacksV2Async(
        string query, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<SearchStarterPacksV2Response, StarterPackView>(
            (cursor, ct) => SearchStarterPacksV2Async(query, pageSize, cursor, ct),
            cancellationToken);

    // ──────────────────────────────────────────────────────────
    //  Membership
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get one page of the authenticated account's curation and moderation lists, each with
    /// whether an actor is on it.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor to check.</param>
    /// <param name="purposes">
    /// Only lists with these purposes, by short name: <c>modlist</c>, <c>curatelist</c>.
    /// <see langword="null"/> for both.
    /// </param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetListsWithMembershipResponse> GetListsWithMembershipAsync(
        AtIdentifier actor, IEnumerable<string>? purposes = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("actor", actor)
            .AddAll("purposes", purposes)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetListsWithMembershipResponse>(
            "app.bsky.graph.getListsWithMembership", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the authenticated account's curation and moderation lists, each with whether an
    /// actor is on it, fetching pages as needed.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor to check.</param>
    /// <param name="purposes">
    /// Only lists with these purposes, by short name: <c>modlist</c>, <c>curatelist</c>.
    /// <see langword="null"/> for both.
    /// </param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ListWithMembership> EnumerateListsWithMembershipAsync(
        AtIdentifier actor, IEnumerable<string>? purposes = null, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetListsWithMembershipResponse, ListWithMembership>(
            (cursor, ct) => GetListsWithMembershipAsync(actor, purposes, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get one page of the authenticated account's starter packs, each with whether an actor is
    /// in it.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor to check.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetStarterPacksWithMembershipResponse> GetStarterPacksWithMembershipAsync(
        AtIdentifier actor, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("actor", actor)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetStarterPacksWithMembershipResponse>(
            "app.bsky.graph.getStarterPacksWithMembership", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the authenticated account's starter packs, each with whether an actor is in it,
    /// fetching pages as needed.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor to check.</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<StarterPackWithMembership> EnumerateStarterPacksWithMembershipAsync(
        AtIdentifier actor, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetStarterPacksWithMembershipResponse, StarterPackWithMembership>(
            (cursor, ct) => GetStarterPacksWithMembershipAsync(actor, pageSize, cursor, ct),
            cancellationToken);
}
