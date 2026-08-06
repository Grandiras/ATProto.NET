using ATProtoNet.Http;

namespace ATProtoNet.Lexicon.Tools.Ozone.Moderation;

/// <summary>
/// Client for tools.ozone.moderation.* endpoints.
/// </summary>
public sealed class ModerationClient
{
    private readonly XrpcClient _xrpc;

    internal ModerationClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
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
        var parameters = new XrpcParams().Add("id", id.ToString());
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
        var parameters = new XrpcParams()
            .Add("uri", uri)
            .Add("cid", cid);
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
        var parameters = new XrpcParams().Add("did", did);
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
        var parameters = new XrpcParams()
            .Add("subject", subject)
            .Add("createdBy", createdBy)
            .Add("sortDirection", sortDirection)
            .Add("createdAfter", createdAfter)
            .Add("createdBefore", createdBefore)
            .Add("limit", limit)
            .Add("cursor", cursor)
            .Add("hasComment", hasComment)
            .Add("comment", comment)
            .AddAll("addedLabels", addedLabels)
            .AddAll("removedLabels", removedLabels)
            .AddAll("addedTags", addedTags)
            .AddAll("removedTags", removedTags)
            .AddAll("reportTypes", reportTypes)
            .AddAll("types", types);
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
        var parameters = new XrpcParams()
            .Add("subject", subject)
            .Add("reviewState", reviewState)
            .Add("sortDirection", sortDirection)
            .Add("sortField", sortField)
            .Add("takendown", takendown)
            .Add("appealed", appealed)
            .Add("limit", limit)
            .Add("cursor", cursor)
            .Add("lastReviewedBy", lastReviewedBy)
            .AddAll("tags", tags)
            .AddAll("excludeTags", excludeTags);
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
        var parameters = new XrpcParams()
            .Add("q", q)
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<SearchReposResponse>(
            "tools.ozone.moderation.searchRepos", parameters, cancellationToken);
    }
}
