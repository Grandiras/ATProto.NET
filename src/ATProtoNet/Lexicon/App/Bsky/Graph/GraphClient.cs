using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;

namespace ATProtoNet.Lexicon.App.Bsky.Graph;

/// <summary>Client for app.bsky.graph.* XRPC endpoints. Handles follows, blocks, mutes, and lists.</summary>
public sealed class GraphClient
{
    private readonly XrpcClient _xrpc;

    internal GraphClient(XrpcClient xrpc) => _xrpc = xrpc;

    // ── Follows ──────────────────────────────────────────────

    /// <summary>Get one page of the accounts following an actor.</summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="sort">The order: <c>latest</c> or <c>top</c>; the server's default when omitted.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetFollowersResponse> GetFollowersAsync(
        AtIdentifier actor, string? sort = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetFollowersResponse>(
            "app.bsky.graph.getFollowers",
            new XrpcParams()
                .Add("actor", actor)
                .Add("sort", sort)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);

    /// <summary>Enumerate the accounts following an actor, fetching pages as needed.</summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="sort">The order: <c>latest</c> or <c>top</c>; the server's default when omitted.</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    public IAsyncEnumerable<ProfileView> EnumerateFollowersAsync(
        AtIdentifier actor, string? sort = null, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetFollowersResponse, ProfileView>(
            (cursor, ct) => GetFollowersAsync(actor, sort, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>Get one page of the accounts an actor follows.</summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="sort">The order: <c>latest</c> or <c>top</c>; the server's default when omitted.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetFollowsResponse> GetFollowsAsync(
        AtIdentifier actor, string? sort = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetFollowsResponse>(
            "app.bsky.graph.getFollows",
            new XrpcParams()
                .Add("actor", actor)
                .Add("sort", sort)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);

    /// <summary>Enumerate the accounts an actor follows, fetching pages as needed.</summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="sort">The order: <c>latest</c> or <c>top</c>; the server's default when omitted.</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    public IAsyncEnumerable<ProfileView> EnumerateFollowsAsync(
        AtIdentifier actor, string? sort = null, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetFollowsResponse, ProfileView>(
            (cursor, ct) => GetFollowsAsync(actor, sort, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>Get suggested follows based on a given actor.</summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    public Task<GetSuggestedFollowsByActorResponse> GetSuggestedFollowsByActorAsync(
        AtIdentifier actor, CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetSuggestedFollowsByActorResponse>(
            "app.bsky.graph.getSuggestedFollowsByActor",
            new XrpcParams().Add("actor", actor), cancellationToken: cancellationToken);

    // ── Blocks ───────────────────────────────────────────────

    /// <summary>Get one page of the accounts the authenticated user blocks.</summary>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetBlocksResponse> GetBlocksAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetBlocksResponse>(
            "app.bsky.graph.getBlocks",
            new XrpcParams().Add("limit", limit).Add("cursor", cursor), cancellationToken: cancellationToken);

    // ── Mutes ────────────────────────────────────────────────

    /// <summary>Get one page of the accounts the authenticated user mutes.</summary>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetMutesResponse> GetMutesAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetMutesResponse>(
            "app.bsky.graph.getMutes",
            new XrpcParams().Add("limit", limit).Add("cursor", cursor), cancellationToken: cancellationToken);

    /// <summary>Mute an actor: fully, or only their reposts and/or quote posts.</summary>
    /// <remarks>
    /// With neither scope set the actor is fully muted; with either set, only that content is.
    /// A repeated call replaces the stored scope rather than adding to it.
    /// </remarks>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="onlyReposts">Mute only the actor's reposts.</param>
    /// <param name="onlyQuoteposts">Mute only the actor's quote posts.</param>
    public Task MuteActorAsync(
        AtIdentifier actor, bool? onlyReposts = null, bool? onlyQuoteposts = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "app.bsky.graph.muteActor",
            new MuteActorRequest(Actor: actor, OnlyReposts: onlyReposts, OnlyQuoteposts: onlyQuoteposts),
            cancellationToken: cancellationToken);

    /// <summary>Unmute an actor.</summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    public Task UnmuteActorAsync(
        AtIdentifier actor, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "app.bsky.graph.unmuteActor", new MuteActorRequest(Actor: actor), cancellationToken: cancellationToken);

    /// <summary>Mute all members of a list.</summary>
    /// <param name="list">The AT-URI of the list.</param>
    public Task MuteActorListAsync(
        AtUri list, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "app.bsky.graph.muteActorList", new MuteActorListRequest(List: list), cancellationToken: cancellationToken);

    /// <summary>Unmute a list.</summary>
    /// <param name="list">The AT-URI of the list.</param>
    public Task UnmuteActorListAsync(
        AtUri list, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "app.bsky.graph.unmuteActorList",
            new MuteActorListRequest(List: list), cancellationToken: cancellationToken);

    // ── Lists ────────────────────────────────────────────────

    /// <summary>Get one page of the lists an actor created.</summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="purposes">
    /// Only lists with these purposes, by short name: <c>modlist</c>, <c>curatelist</c>.
    /// <see langword="null"/> for every purpose the server supports.
    /// </param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetListsResponse> GetListsAsync(
        AtIdentifier actor, IEnumerable<string>? purposes = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetListsResponse>(
            "app.bsky.graph.getLists",
            new XrpcParams()
                .Add("actor", actor)
                .AddAll("purposes", purposes)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);

    /// <summary>Get a list and one page of its members.</summary>
    /// <param name="list">The AT-URI of the list.</param>
    /// <param name="limit">Max members per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetListResponse> GetListAsync(
        AtUri list, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetListResponse>(
            "app.bsky.graph.getList",
            new XrpcParams()
                .Add("list", list)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);

    /// <summary>Enumerate every member of a list, fetching pages as needed.</summary>
    /// <param name="list">The AT-URI of the list.</param>
    /// <param name="pageSize">Members per request (1-100); <see langword="null"/> for the server default.</param>
    public IAsyncEnumerable<ListItemView> EnumerateListMembersAsync(
        AtUri list, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetListResponse, ListItemView>(
            (cursor, ct) => GetListAsync(list, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>Get one page of the lists the authenticated user blocks.</summary>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetListBlocksResponse> GetListBlocksAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetListBlocksResponse>(
            "app.bsky.graph.getListBlocks",
            new XrpcParams().Add("limit", limit).Add("cursor", cursor), cancellationToken: cancellationToken);

    /// <summary>Get one page of the lists the authenticated user mutes.</summary>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetListMutesResponse> GetListMutesAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetListMutesResponse>(
            "app.bsky.graph.getListMutes",
            new XrpcParams().Add("limit", limit).Add("cursor", cursor), cancellationToken: cancellationToken);

    // ── Relationships ────────────────────────────────────────

    /// <summary>Get the relationships between an actor and other accounts.</summary>
    /// <param name="actor">Handle or DID of the account the relationships are relative to.</param>
    /// <param name="others">Handles or DIDs of the other accounts.</param>
    public Task<GetRelationshipsResponse> GetRelationshipsAsync(
        AtIdentifier actor, IEnumerable<AtIdentifier>? others = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetRelationshipsResponse>(
            "app.bsky.graph.getRelationships",
            new XrpcParams()
                .Add("actor", actor)
                .AddAll("others", others?.Select(other => other.Value)),
            cancellationToken: cancellationToken);

    /// <summary>Get one page of an actor's followers that the authenticated user also follows.</summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetKnownFollowersResponse> GetKnownFollowersAsync(
        AtIdentifier actor, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetKnownFollowersResponse>(
            "app.bsky.graph.getKnownFollowers",
            new XrpcParams()
                .Add("actor", actor)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);

    // ── Thread mutes ─────────────────────────────────────────

    /// <summary>Mute a thread (stop receiving notifications).</summary>
    /// <param name="root">The AT-URI of the thread's root post.</param>
    public Task MuteThreadAsync(
        AtUri root, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "app.bsky.graph.muteThread", new MuteThreadRequest(Root: root), cancellationToken: cancellationToken);

    /// <summary>Unmute a thread.</summary>
    /// <param name="root">The AT-URI of the thread's root post.</param>
    public Task UnmuteThreadAsync(
        AtUri root, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "app.bsky.graph.unmuteThread", new MuteThreadRequest(Root: root), cancellationToken: cancellationToken);

    // ── Starter packs ────────────────────────────────────────

    /// <summary>Get a starter pack by its AT-URI.</summary>
    /// <param name="starterPack">The AT-URI of the starter pack record.</param>
    public Task<GetStarterPackResponse> GetStarterPackAsync(
        AtUri starterPack, CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetStarterPackResponse>(
            "app.bsky.graph.getStarterPack",
            new XrpcParams().Add("starterPack", starterPack), cancellationToken: cancellationToken);

    /// <summary>Get multiple starter packs by their AT-URIs.</summary>
    /// <param name="uris">The AT-URIs of the starter pack records.</param>
    public Task<GetStarterPacksResponse> GetStarterPacksAsync(
        IEnumerable<AtUri> uris, CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetStarterPacksResponse>(
            "app.bsky.graph.getStarterPacks",
            new XrpcParams().AddAll("uris", uris.Select(uri => uri.Value)), cancellationToken: cancellationToken);

    /// <summary>Get one page of the starter packs an actor created.</summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetActorStarterPacksResponse> GetActorStarterPacksAsync(
        AtIdentifier actor, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetActorStarterPacksResponse>(
            "app.bsky.graph.getActorStarterPacks",
            new XrpcParams()
                .Add("actor", actor)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);

    /// <summary>Search for starter packs, one page at a time.</summary>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Max results per page (1-100, default 25).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<SearchStarterPacksResponse> SearchStarterPacksAsync(
        string query, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<SearchStarterPacksResponse>(
            "app.bsky.graph.searchStarterPacks",
            new XrpcParams()
                .Add("q", query)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);

    /// <summary>Search for starter packs, one page at a time, returning full starter pack views and an estimate of the hit count.</summary>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Max results per page (1-100, default 25).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<SearchStarterPacksV2Response> SearchStarterPacksV2Async(
        string query, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<SearchStarterPacksV2Response>(
            "app.bsky.graph.searchStarterPacksV2",
            new XrpcParams()
                .Add("q", query)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);

    // ── Membership ───────────────────────────────────────────

    /// <summary>Get one page of the authenticated account's curation and moderation lists, each with whether an actor is on it.</summary>
    /// <param name="actor">Handle or DID of the actor to check.</param>
    /// <param name="purposes">
    /// Only lists with these purposes, by short name: <c>modlist</c>, <c>curatelist</c>.
    /// <see langword="null"/> for both.
    /// </param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetListsWithMembershipResponse> GetListsWithMembershipAsync(
        AtIdentifier actor, IEnumerable<string>? purposes = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetListsWithMembershipResponse>(
            "app.bsky.graph.getListsWithMembership",
            new XrpcParams()
                .Add("actor", actor)
                .AddAll("purposes", purposes)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);

    /// <summary>Get one page of the authenticated account's starter packs, each with whether an actor is in it.</summary>
    /// <param name="actor">Handle or DID of the actor to check.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetStarterPacksWithMembershipResponse> GetStarterPacksWithMembershipAsync(
        AtIdentifier actor, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetStarterPacksWithMembershipResponse>(
            "app.bsky.graph.getStarterPacksWithMembership",
            new XrpcParams()
                .Add("actor", actor)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);
}
