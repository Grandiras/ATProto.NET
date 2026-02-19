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
        await _xrpc.ProcedureAsync<MuteActorRequest, object>(
            "app.bsky.graph.muteActor", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unmute an actor.
    /// </summary>
    public async Task UnmuteActorAsync(
        string actor, CancellationToken cancellationToken = default)
    {
        var request = new MuteActorRequest { Actor = actor };
        await _xrpc.ProcedureAsync<MuteActorRequest, object>(
            "app.bsky.graph.unmuteActor", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Mute all members of a list.
    /// </summary>
    public async Task MuteActorListAsync(
        string list, CancellationToken cancellationToken = default)
    {
        var request = new MuteActorListRequest { List = list };
        await _xrpc.ProcedureAsync<MuteActorListRequest, object>(
            "app.bsky.graph.muteActorList", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unmute a list.
    /// </summary>
    public async Task UnmuteActorListAsync(
        string list, CancellationToken cancellationToken = default)
    {
        var request = new MuteActorListRequest { List = list };
        await _xrpc.ProcedureAsync<MuteActorListRequest, object>(
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
}
