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
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<QueryEventsResponse, ModEventView>(
            (cursor, ct) => QueryEventsAsync(
                subject, createdBy, sortDirection, createdAfter, createdBefore, hasComment, comment,
                addedLabels, removedLabels, addedTags, removedTags, reportTypes, types,
                pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Search/filter one page of moderation subjects (moderation queue view).
    /// </summary>
    /// <param name="subject">
    /// Only this subject: an account's DID, or a record's AT URI.
    /// </param>
    /// <param name="reviewState">Only subjects in this review state (see <see cref="SubjectReviewState"/>).</param>
    /// <param name="sortDirection">Sort direction, <c>asc</c> or <c>desc</c> (the default).</param>
    /// <param name="sortField">The field to sort by.</param>
    /// <param name="takendown">Only subjects that were taken down.</param>
    /// <param name="appealed">Only subjects with an unresolved appeal.</param>
    /// <param name="lastReviewedBy">Only subjects last reviewed by this moderator.</param>
    /// <param name="tags">Only subjects with these tags.</param>
    /// <param name="excludeTags">Leave out subjects with any of these tags.</param>
    /// <param name="limit">Maximum number of subjects (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<QuerySubjectsResponse> QuerySubjectsAsync(
        string? subject = null,
        string? reviewState = null,
        string? sortDirection = null,
        string? sortField = null,
        string? takendown = null,
        string? appealed = null,
        Did? lastReviewedBy = null,
        IEnumerable<string>? tags = null,
        IEnumerable<string>? excludeTags = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("subject", subject)
            .Add("reviewState", reviewState)
            .Add("sortDirection", sortDirection)
            .Add("sortField", sortField)
            .Add("takendown", takendown)
            .Add("appealed", appealed)
            .Add("lastReviewedBy", lastReviewedBy)
            .AddAll("tags", tags)
            .AddAll("excludeTags", excludeTags)
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<QuerySubjectsResponse>(
            "tools.ozone.moderation.querySubjects", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every moderation subject matching the filters, fetching pages as needed.
    /// </summary>
    /// <param name="subject">
    /// Only this subject: an account's DID, or a record's AT URI.
    /// </param>
    /// <param name="reviewState">Only subjects in this review state (see <see cref="SubjectReviewState"/>).</param>
    /// <param name="sortDirection">Sort direction, <c>asc</c> or <c>desc</c> (the default).</param>
    /// <param name="sortField">The field to sort by.</param>
    /// <param name="takendown">Only subjects that were taken down.</param>
    /// <param name="appealed">Only subjects with an unresolved appeal.</param>
    /// <param name="lastReviewedBy">Only subjects last reviewed by this moderator.</param>
    /// <param name="tags">Only subjects with these tags.</param>
    /// <param name="excludeTags">Leave out subjects with any of these tags.</param>
    /// <param name="pageSize">Subjects per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<SubjectStatusView> EnumerateSubjectsAsync(
        string? subject = null,
        string? reviewState = null,
        string? sortDirection = null,
        string? sortField = null,
        string? takendown = null,
        string? appealed = null,
        Did? lastReviewedBy = null,
        IEnumerable<string>? tags = null,
        IEnumerable<string>? excludeTags = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<QuerySubjectsResponse, SubjectStatusView>(
            (cursor, ct) => QuerySubjectsAsync(
                subject, reviewState, sortDirection, sortField, takendown, appealed, lastReviewedBy,
                tags, excludeTags, pageSize, cursor, ct),
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
