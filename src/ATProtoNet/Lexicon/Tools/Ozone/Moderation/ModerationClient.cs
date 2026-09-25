using ATProtoNet.Http;
using ATProtoNet.Identity;

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
    /// <param name="request">The event, its subject and the moderator emitting it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ModEventView> EmitEventAsync(
        EmitEventRequest request,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<ModEventView>(
            "tools.ozone.moderation.emitEvent", request, cancellationToken: cancellationToken);

    /// <summary>
    /// Get a specific moderation event by ID.
    /// </summary>
    /// <param name="id">The event's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ModEventViewDetail> GetEventAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("id", id.ToString());
        return _xrpc.QueryAsync<ModEventViewDetail>(
            "tools.ozone.moderation.getEvent", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get a record with moderation context.
    /// </summary>
    /// <param name="uri">The record's AT URI.</param>
    /// <param name="cid">Optional specific version CID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<RecordViewDetail> GetRecordAsync(
        AtUri uri,
        Cid? cid = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("uri", uri)
            .Add("cid", cid);
        return _xrpc.QueryAsync<RecordViewDetail>(
            "tools.ozone.moderation.getRecord", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get a repo/account with moderation context.
    /// </summary>
    /// <param name="did">The account's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<RepoViewDetail> GetRepoAsync(
        Did did,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("did", did);
        return _xrpc.QueryAsync<RepoViewDetail>(
            "tools.ozone.moderation.getRepo", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Search/filter one page of moderation events.
    /// </summary>
    /// <param name="subject">
    /// Only events on this subject: an account's DID, or a record's AT URI.
    /// </param>
    /// <param name="createdBy">Only events created by this moderator.</param>
    /// <param name="sortDirection">Sort by creation time, <c>asc</c> or <c>desc</c> (the default).</param>
    /// <param name="createdAfter">Only events created after this time.</param>
    /// <param name="createdBefore">Only events created before this time.</param>
    /// <param name="hasComment"><see langword="true"/> for only events with a comment.</param>
    /// <param name="comment">Only events whose comment contains this keyword; <c>||</c> separates alternatives.</param>
    /// <param name="addedLabels">Only events that added all of these labels.</param>
    /// <param name="removedLabels">Only events that removed all of these labels.</param>
    /// <param name="addedTags">Only events that added all of these tags.</param>
    /// <param name="removedTags">Only events that removed all of these tags.</param>
    /// <param name="reportTypes">Only report events of these reason types.</param>
    /// <param name="types">Only events of these types (<c>tools.ozone.moderation.defs#modEvent…</c>).</param>
    /// <param name="collections">
    /// Only events on records in these collections; applies when <paramref name="subject"/> is
    /// an account or <paramref name="includeAllUserRecords"/> is set.
    /// </param>
    /// <param name="subjectType">Only events on this kind of subject: <c>account</c>, <c>record</c> or <c>conversation</c>.</param>
    /// <param name="includeAllUserRecords">
    /// With an account <paramref name="subject"/>, also events on the account's records.
    /// </param>
    /// <param name="policies">Only events enforcing one of these policies.</param>
    /// <param name="modTool">Only events emitted with one of these tools.</param>
    /// <param name="batchId">Only events of this batch.</param>
    /// <param name="ageAssuranceState">Only age-assurance events that set this state.</param>
    /// <param name="withStrike"><see langword="true"/> for only events that gave strikes.</param>
    /// <param name="limit">Maximum number of events (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<QueryEventsResponse> QueryEventsAsync(
        string? subject = null,
        Did? createdBy = null,
        string? sortDirection = null,
        AtDatetime? createdAfter = null,
        AtDatetime? createdBefore = null,
        bool? hasComment = null,
        string? comment = null,
        IEnumerable<string>? addedLabels = null,
        IEnumerable<string>? removedLabels = null,
        IEnumerable<string>? addedTags = null,
        IEnumerable<string>? removedTags = null,
        IEnumerable<string>? reportTypes = null,
        IEnumerable<string>? types = null,
        IEnumerable<Nsid>? collections = null,
        string? subjectType = null,
        bool? includeAllUserRecords = null,
        IEnumerable<string>? policies = null,
        IEnumerable<string>? modTool = null,
        string? batchId = null,
        string? ageAssuranceState = null,
        bool? withStrike = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("subject", subject)
            .Add("createdBy", createdBy)
            .Add("sortDirection", sortDirection)
            .Add("createdAfter", createdAfter?.ToString())
            .Add("createdBefore", createdBefore?.ToString())
            .Add("hasComment", hasComment)
            .Add("comment", comment)
            .AddAll("addedLabels", addedLabels)
            .AddAll("removedLabels", removedLabels)
            .AddAll("addedTags", addedTags)
            .AddAll("removedTags", removedTags)
            .AddAll("reportTypes", reportTypes)
            .AddAll("types", types)
            .AddAll("collections", collections?.Select(collection => collection.Value))
            .Add("subjectType", subjectType)
            .Add("includeAllUserRecords", includeAllUserRecords)
            .AddAll("policies", policies)
            .AddAll("modTool", modTool)
            .Add("batchId", batchId)
            .Add("ageAssuranceState", ageAssuranceState)
            .Add("withStrike", withStrike)
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<QueryEventsResponse>(
            "tools.ozone.moderation.queryEvents", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every moderation event matching the filters, fetching pages as needed.
    /// </summary>
    /// <param name="subject">
    /// Only events on this subject: an account's DID, or a record's AT URI.
    /// </param>
    /// <param name="createdBy">Only events created by this moderator.</param>
    /// <param name="sortDirection">Sort by creation time, <c>asc</c> or <c>desc</c> (the default).</param>
    /// <param name="createdAfter">Only events created after this time.</param>
    /// <param name="createdBefore">Only events created before this time.</param>
    /// <param name="hasComment"><see langword="true"/> for only events with a comment.</param>
    /// <param name="comment">Only events whose comment contains this keyword; <c>||</c> separates alternatives.</param>
    /// <param name="addedLabels">Only events that added all of these labels.</param>
    /// <param name="removedLabels">Only events that removed all of these labels.</param>
    /// <param name="addedTags">Only events that added all of these tags.</param>
    /// <param name="removedTags">Only events that removed all of these tags.</param>
    /// <param name="reportTypes">Only report events of these reason types.</param>
    /// <param name="types">Only events of these types (<c>tools.ozone.moderation.defs#modEvent…</c>).</param>
    /// <param name="collections">
    /// Only events on records in these collections; applies when <paramref name="subject"/> is
    /// an account or <paramref name="includeAllUserRecords"/> is set.
    /// </param>
    /// <param name="subjectType">Only events on this kind of subject: <c>account</c>, <c>record</c> or <c>conversation</c>.</param>
    /// <param name="includeAllUserRecords">
    /// With an account <paramref name="subject"/>, also events on the account's records.
    /// </param>
    /// <param name="policies">Only events enforcing one of these policies.</param>
    /// <param name="modTool">Only events emitted with one of these tools.</param>
    /// <param name="batchId">Only events of this batch.</param>
    /// <param name="ageAssuranceState">Only age-assurance events that set this state.</param>
    /// <param name="withStrike"><see langword="true"/> for only events that gave strikes.</param>
    /// <param name="pageSize">Events per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ModEventView> EnumerateEventsAsync(
        string? subject = null,
        Did? createdBy = null,
        string? sortDirection = null,
        AtDatetime? createdAfter = null,
        AtDatetime? createdBefore = null,
        bool? hasComment = null,
        string? comment = null,
        IEnumerable<string>? addedLabels = null,
        IEnumerable<string>? removedLabels = null,
        IEnumerable<string>? addedTags = null,
        IEnumerable<string>? removedTags = null,
        IEnumerable<string>? reportTypes = null,
        IEnumerable<string>? types = null,
        IEnumerable<Nsid>? collections = null,
        string? subjectType = null,
        bool? includeAllUserRecords = null,
        IEnumerable<string>? policies = null,
        IEnumerable<string>? modTool = null,
        string? batchId = null,
        string? ageAssuranceState = null,
        bool? withStrike = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<QueryEventsResponse, ModEventView>(
            (cursor, ct) => QueryEventsAsync(
                subject, createdBy, sortDirection, createdAfter, createdBefore, hasComment, comment,
                addedLabels, removedLabels, addedTags, removedTags, reportTypes, types,
                collections, subjectType, includeAllUserRecords, policies, modTool, batchId,
                ageAssuranceState, withStrike, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get one page of the subjects' moderation statuses: the review queue.
    /// </summary>
    /// <param name="filter">Which subjects to return and in what order; <see langword="null"/> for the defaults.</param>
    /// <param name="limit">Maximum number of statuses (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<QueryStatusesResponse> QueryStatusesAsync(
        SubjectStatusFilter? filter = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = (filter ?? SubjectStatusFilter.None).ToParams()
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<QueryStatusesResponse>(
            "tools.ozone.moderation.queryStatuses", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every subject status a filter matches, fetching pages as needed.
    /// </summary>
    /// <param name="filter">Which subjects to return and in what order; <see langword="null"/> for the defaults.</param>
    /// <param name="pageSize">Statuses per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<SubjectStatusView> EnumerateStatusesAsync(
        SubjectStatusFilter? filter = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<QueryStatusesResponse, SubjectStatusView>(
            (cursor, ct) => QueryStatusesAsync(filter, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Search one page of repos with moderation context.
    /// </summary>
    /// <param name="q">The search term; <see langword="null"/> matches every repo.</param>
    /// <param name="limit">Maximum number of repos (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
            "tools.ozone.moderation.searchRepos", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every repo a search matches, fetching pages as needed.
    /// </summary>
    /// <param name="q">The search term; <see langword="null"/> matches every repo.</param>
    /// <param name="pageSize">Repos per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<RepoView> EnumerateReposAsync(
        string? q = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<SearchReposResponse, RepoView>(
            (cursor, ct) => SearchReposAsync(q, pageSize, cursor, ct),
            cancellationToken);
}
