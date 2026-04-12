using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.Tools.Ozone.Moderation;

/// <summary>
/// Client for tools.ozone.moderation.* endpoints.
/// </summary>
public sealed class ModerationClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal ModerationClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    /// <summary>
    /// Emit a moderation event (takedown, label, acknowledge, escalate, etc.).
    /// </summary>
    public Task<ModEventView> EmitEventAsync(
        EmitEventRequest request,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<EmitEventRequest, ModEventView>(
            "tools.ozone.moderation.emitEvent", request, cancellationToken: cancellationToken);

    /// <summary>
    /// Get a specific moderation event by ID.
    /// </summary>
    public Task<ModEventViewDetail> GetEventAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?> { ["id"] = id.ToString() };
        return _xrpc.QueryAsync<ModEventViewDetail>(
            "tools.ozone.moderation.getEvent", parameters, cancellationToken);
    }

    /// <summary>
    /// Get a record with moderation context.
    /// </summary>
    public Task<RecordViewDetail> GetRecordAsync(
        string uri,
        string? cid = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["uri"] = uri,
            ["cid"] = cid,
        };
        return _xrpc.QueryAsync<RecordViewDetail>(
            "tools.ozone.moderation.getRecord", parameters, cancellationToken);
    }

    /// <summary>
    /// Get a repo/account with moderation context.
    /// </summary>
    public Task<RepoViewDetail> GetRepoAsync(
        string did,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?> { ["did"] = did };
        return _xrpc.QueryAsync<RepoViewDetail>(
            "tools.ozone.moderation.getRepo", parameters, cancellationToken);
    }

    /// <summary>
    /// Search/filter moderation events.
    /// </summary>
    public Task<QueryEventsResponse> QueryEventsAsync(
        string? subject = null,
        string? createdBy = null,
        string? sortDirection = null,
        string? createdAfter = null,
        string? createdBefore = null,
        int? limit = null,
        string? cursor = null,
        bool? hasComment = null,
        string? comment = null,
        List<string>? addedLabels = null,
        List<string>? removedLabels = null,
        List<string>? addedTags = null,
        List<string>? removedTags = null,
        List<string>? reportTypes = null,
        List<string>? types = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["subject"] = subject,
            ["createdBy"] = createdBy,
            ["sortDirection"] = sortDirection,
            ["createdAfter"] = createdAfter,
            ["createdBefore"] = createdBefore,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
            ["hasComment"] = hasComment?.ToString()?.ToLowerInvariant(),
            ["comment"] = comment,
        };

        AddListParams(parameters, "addedLabels", addedLabels);
        AddListParams(parameters, "removedLabels", removedLabels);
        AddListParams(parameters, "addedTags", addedTags);
        AddListParams(parameters, "removedTags", removedTags);
        AddListParams(parameters, "reportTypes", reportTypes);
        AddListParams(parameters, "types", types);

        return _xrpc.QueryAsync<QueryEventsResponse>(
            "tools.ozone.moderation.queryEvents", parameters, cancellationToken);
    }

    /// <summary>
    /// Search/filter moderation subjects (moderation queue view).
    /// </summary>
    public Task<QuerySubjectsResponse> QuerySubjectsAsync(
        string? subject = null,
        string? reviewState = null,
        string? sortDirection = null,
        string? sortField = null,
        string? takendown = null,
        string? appealed = null,
        int? limit = null,
        string? cursor = null,
        string? lastReviewedBy = null,
        List<string>? tags = null,
        List<string>? excludeTags = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["subject"] = subject,
            ["reviewState"] = reviewState,
            ["sortDirection"] = sortDirection,
            ["sortField"] = sortField,
            ["takendown"] = takendown,
            ["appealed"] = appealed,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
            ["lastReviewedBy"] = lastReviewedBy,
        };

        AddListParams(parameters, "tags", tags);
        AddListParams(parameters, "excludeTags", excludeTags);

        return _xrpc.QueryAsync<QuerySubjectsResponse>(
            "tools.ozone.moderation.querySubjects", parameters, cancellationToken);
    }

    /// <summary>
    /// Search repos with moderation context.
    /// </summary>
    public Task<SearchReposResponse> SearchReposAsync(
        string? q = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["q"] = q,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };
        return _xrpc.QueryAsync<SearchReposResponse>(
            "tools.ozone.moderation.searchRepos", parameters, cancellationToken);
    }

    private static void AddListParams(
        Dictionary<string, string?> parameters,
        string key,
        List<string>? values)
    {
        if (values is not { Count: > 0 }) return;
        // XRPC array params use repeated keys: key=val1&key=val2
        // Most implementations serialize as comma-separated for simplicity
        parameters[key] = string.Join(",", values);
    }
}
