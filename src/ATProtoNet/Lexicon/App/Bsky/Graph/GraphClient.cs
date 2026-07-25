using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.App.Bsky.Graph;

/// <summary>
/// Client for app.bsky.graph.* XRPC endpoints.
/// Handles follows, blocks, mutes, and lists.
/// </summary>
public sealed class GraphClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal GraphClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────
    //  Follows
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get followers of an actor.
    /// </summary>
    public Task<GetFollowersResponse> GetFollowersAsync(
        string actor, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["actor"] = actor,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetFollowersResponse>(
            "app.bsky.graph.getFollowers", parameters, cancellationToken);
    }

    /// <summary>
    /// Get accounts that an actor follows.
    /// </summary>
    public Task<GetFollowsResponse> GetFollowsAsync(
        string actor, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["actor"] = actor,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetFollowsResponse>(
            "app.bsky.graph.getFollows", parameters, cancellationToken);
    }

    /// <summary>
    /// Get suggested follows based on a given actor.
    /// </summary>
    public Task<GetSuggestedFollowsByActorResponse> GetSuggestedFollowsByActorAsync(
        string actor, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?> { ["actor"] = actor };
        return _xrpc.QueryAsync<GetSuggestedFollowsByActorResponse>(
            "app.bsky.graph.getSuggestedFollowsByActor", parameters, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Blocks
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get the authenticated user's blocked accounts.
    /// </summary>
    public Task<GetBlocksResponse> GetBlocksAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetBlocksResponse>(
            "app.bsky.graph.getBlocks", parameters, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Mutes
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get the authenticated user's muted accounts.
    /// </summary>
    public Task<GetMutesResponse> GetMutesAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetMutesResponse>(
            "app.bsky.graph.getMutes", parameters, cancellationToken);
    }

    /// <summary>
    /// Mute an actor.
    /// </summary>
    public async Task MuteActorAsync(
        string actor, CancellationToken cancellationToken = default)
    {
        var request = new MuteActorRequest { Actor = actor };
        await _xrpc.ProcedureAsync<MuteActorRequest>(
            "app.bsky.graph.muteActor", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unmute an actor.
    /// </summary>
    public async Task UnmuteActorAsync(
        string actor, CancellationToken cancellationToken = default)
    {
        var request = new MuteActorRequest { Actor = actor };
        await _xrpc.ProcedureAsync<MuteActorRequest>(
            "app.bsky.graph.unmuteActor", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Mute all members of a list.
    /// </summary>
    public async Task MuteActorListAsync(
        string list, CancellationToken cancellationToken = default)
    {
        var request = new MuteActorListRequest { List = list };
        await _xrpc.ProcedureAsync<MuteActorListRequest>(
            "app.bsky.graph.muteActorList", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unmute a list.
    /// </summary>
    public async Task UnmuteActorListAsync(
        string list, CancellationToken cancellationToken = default)
    {
        var request = new MuteActorListRequest { List = list };
        await _xrpc.ProcedureAsync<MuteActorListRequest>(
            "app.bsky.graph.unmuteActorList", request, cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Lists
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get lists created by an actor.
    /// </summary>
    public Task<GetListsResponse> GetListsAsync(
        string actor, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["actor"] = actor,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetListsResponse>(
            "app.bsky.graph.getLists", parameters, cancellationToken);
    }

    /// <summary>
    /// Get a list and its items.
    /// </summary>
    public Task<GetListResponse> GetListAsync(
        string list, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["list"] = list,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetListResponse>(
            "app.bsky.graph.getList", parameters, cancellationToken);
    }

    /// <summary>
    /// Get lists that the authenticated user has blocked.
    /// </summary>
    public Task<GetListBlocksResponse> GetListBlocksAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetListBlocksResponse>(
            "app.bsky.graph.getListBlocks", parameters, cancellationToken);
    }

    /// <summary>
    /// Get lists that the authenticated user has muted.
    /// </summary>
    public Task<GetListMutesResponse> GetListMutesAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetListMutesResponse>(
            "app.bsky.graph.getListMutes", parameters, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Relationships
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get relationships between the authenticated user and other actors.
    /// </summary>
    public Task<GetRelationshipsResponse> GetRelationshipsAsync(
        string actor, List<string>? others = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["actor"] = actor,
        };
        // others is an array parameter, pass as comma-separated
        if (others is { Count: > 0 })
            parameters["others"] = string.Join(",", others);

        return _xrpc.QueryAsync<GetRelationshipsResponse>(
            "app.bsky.graph.getRelationships", parameters, cancellationToken);
    }

    /// <summary>
    /// Get followers of an actor that are known (followed by) the authenticated user.
    /// </summary>
    public Task<GetKnownFollowersResponse> GetKnownFollowersAsync(
        string actor, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["actor"] = actor,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetKnownFollowersResponse>(
            "app.bsky.graph.getKnownFollowers", parameters, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Thread mutes
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Mute a thread (stop receiving notifications).
    /// </summary>
    public async Task MuteThreadAsync(
        string root, CancellationToken cancellationToken = default)
    {
        var request = new MuteThreadRequest { Root = root };
        await _xrpc.ProcedureAsync<MuteThreadRequest>(
            "app.bsky.graph.muteThread", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unmute a thread.
    /// </summary>
    public async Task UnmuteThreadAsync(
        string root, CancellationToken cancellationToken = default)
    {
        var request = new MuteThreadRequest { Root = root };
        await _xrpc.ProcedureAsync<MuteThreadRequest>(
            "app.bsky.graph.unmuteThread", request, cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Starter packs
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get a starter pack by its AT-URI.
    /// </summary>
    public Task<GetStarterPackResponse> GetStarterPackAsync(
        string starterPack, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?> { ["starterPack"] = starterPack };
        return _xrpc.QueryAsync<GetStarterPackResponse>(
            "app.bsky.graph.getStarterPack", parameters, cancellationToken);
    }

    /// <summary>
    /// Get multiple starter packs by their AT-URIs.
    /// </summary>
    public Task<GetStarterPacksResponse> GetStarterPacksAsync(
        List<string> uris, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["uris"] = string.Join(",", uris),
        };
        return _xrpc.QueryAsync<GetStarterPacksResponse>(
            "app.bsky.graph.getStarterPacks", parameters, cancellationToken);
    }

    /// <summary>
    /// Get starter packs created by an actor.
    /// </summary>
    public Task<GetActorStarterPacksResponse> GetActorStarterPacksAsync(
        string actor, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["actor"] = actor,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };
        return _xrpc.QueryAsync<GetActorStarterPacksResponse>(
            "app.bsky.graph.getActorStarterPacks", parameters, cancellationToken);
    }

    /// <summary>
    /// Search for starter packs.
    /// </summary>
    public Task<SearchStarterPacksResponse> SearchStarterPacksAsync(
        string query, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["q"] = query,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };
        return _xrpc.QueryAsync<SearchStarterPacksResponse>(
            "app.bsky.graph.searchStarterPacks", parameters, cancellationToken);
    }
}
