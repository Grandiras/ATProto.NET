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

    /// <summary>
    /// Get an account's private app preferences. Needs moderator or admin auth.
    /// </summary>
    /// <param name="did">The account's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetAccountPreferencesResponse> GetAccountPreferencesAsync(
        Did did,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("did", did);
        return _xrpc.QueryAsync<GetAccountPreferencesResponse>(
            "tools.ozone.moderation.getAccountPreferences", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get several accounts with moderation context at once.
    /// </summary>
    /// <param name="dids">The accounts' DIDs (at most 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One entry per DID: a <see cref="RepoViewDetail"/>, or a <see cref="RepoViewNotFound"/>.
    /// </returns>
    public Task<GetReposResponse> GetReposAsync(
        IEnumerable<Did> dids,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().AddAll("dids", dids.Select(did => did.Value));
        return _xrpc.QueryAsync<GetReposResponse>(
            "tools.ozone.moderation.getRepos", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get several records with moderation context at once.
    /// </summary>
    /// <param name="uris">The records' AT URIs (at most 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One entry per URI: a <see cref="RecordViewDetail"/>, or a <see cref="RecordViewNotFound"/>.
    /// </returns>
    public Task<GetRecordsResponse> GetRecordsAsync(
        IEnumerable<AtUri> uris,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().AddAll("uris", uris.Select(uri => uri.Value));
        return _xrpc.QueryAsync<GetRecordsResponse>(
            "tools.ozone.moderation.getRecords", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get everything Ozone knows about several subjects: status, account, profile and record.
    /// </summary>
    /// <param name="subjects">The subjects (at most 100): account DIDs or record AT URIs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetSubjectsResponse> GetSubjectsAsync(
        IEnumerable<string> subjects,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().AddAll("subjects", subjects);
        return _xrpc.QueryAsync<GetSubjectsResponse>(
            "tools.ozone.moderation.getSubjects", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get an account's history, day by day: moderation events, account changes and PLC operations.
    /// </summary>
    /// <param name="did">The account's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException"><c>RepoNotFound</c> when Ozone does not know the account.</exception>
    public Task<GetAccountTimelineResponse> GetAccountTimelineAsync(
        Did did,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("did", did);
        return _xrpc.QueryAsync<GetAccountTimelineResponse>(
            "tools.ozone.moderation.getAccountTimeline", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get how several accounts' reports turned out: how many they filed, and how many led to a
    /// takedown or a label.
    /// </summary>
    /// <param name="dids">The reporters' DIDs (at most 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetReporterStatsResponse> GetReporterStatsAsync(
        IEnumerable<Did> dids,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().AddAll("dids", dids.Select(did => did.Value));
        return _xrpc.QueryAsync<GetReporterStatsResponse>(
            "tools.ozone.moderation.getReporterStats", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Schedule a moderation action to run later on several accounts.
    /// </summary>
    /// <param name="subjects">The accounts (at most 100).</param>
    /// <param name="action">The action, such as a <see cref="ScheduledTakedown"/>.</param>
    /// <param name="scheduling">When it runs: an exact time, or a random time in a window.</param>
    /// <param name="createdBy">The moderator scheduling it.</param>
    /// <param name="modTool">The tool scheduling it; passed on to the event the action emits.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The accounts the action was scheduled for, and those it failed for.</returns>
    public Task<ScheduledActionResults> ScheduleActionAsync(
        IEnumerable<Did> subjects,
        ScheduledAction action,
        SchedulingConfig scheduling,
        Did createdBy,
        ModTool? modTool = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ScheduleActionRequest
        {
            Action = action,
            Subjects = [.. subjects],
            CreatedBy = createdBy,
            Scheduling = scheduling,
            ModTool = modTool,
        };
        return _xrpc.ProcedureAsync<ScheduledActionResults>(
            "tools.ozone.moderation.scheduleAction", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List one page of scheduled moderation actions.
    /// </summary>
    /// <param name="statuses">Only actions in these statuses (see <see cref="ScheduledActionStatus"/>).</param>
    /// <param name="subjects">Only actions for these accounts (at most 100).</param>
    /// <param name="startsAfter">Only actions scheduled to run after this time.</param>
    /// <param name="endsBefore">Only actions scheduled to run before this time.</param>
    /// <param name="limit">Maximum number of actions (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListScheduledActionsResponse> ListScheduledActionsAsync(
        IEnumerable<string> statuses,
        IEnumerable<Did>? subjects = null,
        AtDatetime? startsAfter = null,
        AtDatetime? endsBefore = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ListScheduledActionsRequest
        {
            Statuses = [.. statuses],
            Subjects = subjects is null ? null : [.. subjects],
            StartsAfter = startsAfter,
            EndsBefore = endsBefore,
            Limit = limit,
            Cursor = cursor,
        };
        return _xrpc.ProcedureAsync<ListScheduledActionsResponse>(
            "tools.ozone.moderation.listScheduledActions", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every scheduled moderation action matching the filters, fetching pages as needed.
    /// </summary>
    /// <param name="statuses">Only actions in these statuses (see <see cref="ScheduledActionStatus"/>).</param>
    /// <param name="subjects">Only actions for these accounts (at most 100).</param>
    /// <param name="startsAfter">Only actions scheduled to run after this time.</param>
    /// <param name="endsBefore">Only actions scheduled to run before this time.</param>
    /// <param name="pageSize">Actions per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ScheduledActionView> EnumerateScheduledActionsAsync(
        IEnumerable<string> statuses,
        IEnumerable<Did>? subjects = null,
        AtDatetime? startsAfter = null,
        AtDatetime? endsBefore = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListScheduledActionsResponse, ScheduledActionView>(
            (cursor, ct) => ListScheduledActionsAsync(statuses, subjects, startsAfter, endsBefore, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Cancel every pending scheduled action on several accounts.
    /// </summary>
    /// <param name="subjects">The accounts (at most 100).</param>
    /// <param name="comment">Why the actions are cancelled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The accounts whose actions were cancelled, and those it failed for.</returns>
    public Task<CancellationResults> CancelScheduledActionsAsync(
        IEnumerable<Did> subjects,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CancelScheduledActionsRequest { Subjects = [.. subjects], Comment = comment };
        return _xrpc.ProcedureAsync<CancellationResults>(
            "tools.ozone.moderation.cancelScheduledActions", request, cancellationToken: cancellationToken);
    }
}
