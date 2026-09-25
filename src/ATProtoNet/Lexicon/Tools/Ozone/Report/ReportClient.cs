using System.Globalization;
using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Tools.Ozone.Report;

/// <summary>
/// Client for tools.ozone.report.* endpoints: the report-centric workflow, where each report is
/// reviewed on its own (queued, assigned, escalated, closed) rather than only through its
/// subject's status.
/// </summary>
public sealed class ReportClient
{
    private readonly XrpcClient _xrpc;

    internal ReportClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Get one report.
    /// </summary>
    /// <param name="id">The report's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException"><c>NotFound</c> when there is no such report.</exception>
    public Task<ReportView> GetReportAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("id", id);
        return _xrpc.QueryAsync<ReportView>(
            "tools.ozone.report.getReport", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the most recent report.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException"><c>NotFound</c> when there are no reports.</exception>
    public Task<GetLatestReportResponse> GetLatestReportAsync(
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetLatestReportResponse>(
            "tools.ozone.report.getLatestReport", cancellationToken: cancellationToken);

    /// <summary>
    /// Query one page of reports.
    /// </summary>
    /// <param name="status">Only reports in this status (see <see cref="ReportStatus"/>).</param>
    /// <param name="filter">The other filters and the order; <see langword="null"/> for the defaults.</param>
    /// <param name="limit">Maximum number of reports (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<QueryReportsResponse> QueryReportsAsync(
        string status,
        ReportFilter? filter = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = (filter ?? ReportFilter.None).ToParams(status)
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<QueryReportsResponse>(
            "tools.ozone.report.queryReports", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every report a filter matches, fetching pages as needed.
    /// </summary>
    /// <param name="status">Only reports in this status (see <see cref="ReportStatus"/>).</param>
    /// <param name="filter">The other filters and the order; <see langword="null"/> for the defaults.</param>
    /// <param name="pageSize">Reports per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ReportView> EnumerateReportsAsync(
        string status,
        ReportFilter? filter = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<QueryReportsResponse, ReportView>(
            (cursor, ct) => QueryReportsAsync(status, filter, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Close every open report on a subject, without acting on the subject: for automated flows
    /// that resolve reports. Reports whose status cannot move to closed are skipped.
    /// </summary>
    /// <param name="subject">The subject: an account's DID (account reports) or a record's AT URI.</param>
    /// <param name="reportTypes">Only reports of these reason types; <see langword="null"/> for all.</param>
    /// <param name="internalNote">A note for moderators, recorded on each close activity.</param>
    /// <param name="isAutomated">Whether an automated process is closing the reports.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CloseReportsResponse> CloseReportsAsync(
        string subject,
        IEnumerable<string>? reportTypes = null,
        string? internalNote = null,
        bool? isAutomated = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CloseReportsRequest
        {
            Subject = subject,
            ReportTypes = reportTypes is null ? null : [.. reportTypes],
            InternalNote = internalNote,
            IsAutomated = isAutomated,
        };
        return _xrpc.ProcedureAsync<CloseReportsResponse>(
            "tools.ozone.report.closeReports", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Move a report to another queue, or out of every queue, recording a queue activity.
    /// </summary>
    /// <param name="reportId">The report.</param>
    /// <param name="queueId">The queue to move it to; <c>-1</c> for none.</param>
    /// <param name="comment">A note for moderators, recorded on the queue activity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException">
    /// <c>ReportNotFound</c>, <c>ReportClosed</c>, <c>AlreadyInTargetQueue</c>, <c>QueueNotFound</c>
    /// or <c>QueueDisabled</c>.
    /// </exception>
    public Task<ReassignQueueResponse> ReassignQueueAsync(
        long reportId,
        long queueId,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ReassignQueueRequest { ReportId = reportId, QueueId = queueId, Comment = comment };
        return _xrpc.ProcedureAsync<ReassignQueueResponse>(
            "tools.ozone.report.reassignQueue", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Assign a report to a moderator: the caller by default; admins may assign anyone.
    /// </summary>
    /// <param name="reportId">The report.</param>
    /// <param name="did">The moderator; <see langword="null"/> for the caller.</param>
    /// <param name="queueId">
    /// The queue to make the assignment on; <see langword="null"/> keeps the queue of an earlier
    /// assignment.
    /// </param>
    /// <param name="isPermanent">Whether the assignment never expires.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException">
    /// <c>AlreadyAssigned</c> when another moderator holds a permanent assignment, or
    /// <c>InvalidAssignment</c>.
    /// </exception>
    public Task<AssignmentView> AssignModeratorAsync(
        long reportId,
        Did? did = null,
        long? queueId = null,
        bool? isPermanent = null,
        CancellationToken cancellationToken = default)
    {
        var request = new AssignModeratorRequest
        {
            ReportId = reportId,
            Did = did,
            QueueId = queueId,
            IsPermanent = isPermanent,
        };
        return _xrpc.ProcedureAsync<AssignmentView>(
            "tools.ozone.report.assignModerator", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Remove a report's assignment.
    /// </summary>
    /// <param name="reportId">The report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ended assignment.</returns>
    /// <exception cref="XrpcException"><c>InvalidAssignment</c>.</exception>
    public Task<AssignmentView> UnassignModeratorAsync(
        long reportId,
        CancellationToken cancellationToken = default)
    {
        var request = new UnassignModeratorRequest { ReportId = reportId };
        return _xrpc.ProcedureAsync<AssignmentView>(
            "tools.ozone.report.unassignModerator", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get one page of report assignments.
    /// </summary>
    /// <param name="reportIds">Only assignments of these reports (at most 50).</param>
    /// <param name="dids">Only assignments of these moderators (at most 50).</param>
    /// <param name="onlyActive">Only active assignments; the server default is <see langword="true"/>.</param>
    /// <param name="limit">Maximum number of assignments (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetAssignmentsResponse> GetAssignmentsAsync(
        IEnumerable<long>? reportIds = null,
        IEnumerable<Did>? dids = null,
        bool? onlyActive = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("onlyActive", onlyActive)
            .AddAll("reportIds", reportIds?.Select(FormatId))
            .AddAll("dids", dids?.Select(did => did.Value))
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<GetAssignmentsResponse>(
            "tools.ozone.report.getAssignments", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every report assignment matching the filters, fetching pages as needed.
    /// </summary>
    /// <param name="reportIds">Only assignments of these reports (at most 50).</param>
    /// <param name="dids">Only assignments of these moderators (at most 50).</param>
    /// <param name="onlyActive">Only active assignments; the server default is <see langword="true"/>.</param>
    /// <param name="pageSize">Assignments per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<AssignmentView> EnumerateAssignmentsAsync(
        IEnumerable<long>? reportIds = null,
        IEnumerable<Did>? dids = null,
        bool? onlyActive = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetAssignmentsResponse, AssignmentView>(
            (cursor, ct) => GetAssignmentsAsync(reportIds, dids, onlyActive, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Record an activity on a report. An activity that changes the report's status (such as a
    /// <see cref="CloseActivity"/>) checks the transition and moves the report in the same step.
    /// </summary>
    /// <param name="reportId">The report.</param>
    /// <param name="activity">What happened, such as a <see cref="NoteActivity"/>.</param>
    /// <param name="internalNote">A note for moderators only.</param>
    /// <param name="publicNote">A note the reporter may see.</param>
    /// <param name="isAutomated">Whether an automated process is recording the activity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException">
    /// <c>ReportNotFound</c>, <c>InvalidStateTransition</c> or <c>AlreadyInTargetState</c>.
    /// </exception>
    public Task<CreateActivityResponse> CreateActivityAsync(
        long reportId,
        ReportActivity activity,
        string? internalNote = null,
        string? publicNote = null,
        bool? isAutomated = null,
        CancellationToken cancellationToken = default) =>
        SendActivityAsync(
            new CreateActivityRequest
            {
                ReportId = reportId,
                Activity = activity,
                InternalNote = internalNote,
                PublicNote = publicNote,
                IsAutomated = isAutomated,
            },
            cancellationToken);

    /// <summary>
    /// Record an activity on the report a moderation event created, when you have the event's
    /// identifier rather than the report's. Otherwise the same as
    /// <see cref="CreateActivityAsync(long, ReportActivity, string, string, bool?, CancellationToken)"/>.
    /// </summary>
    /// <param name="eventId">The moderation event that created the report.</param>
    /// <param name="activity">What happened, such as a <see cref="NoteActivity"/>.</param>
    /// <param name="internalNote">A note for moderators only.</param>
    /// <param name="publicNote">A note the reporter may see.</param>
    /// <param name="isAutomated">Whether an automated process is recording the activity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException">
    /// <c>ReportNotFound</c>, <c>InvalidStateTransition</c> or <c>AlreadyInTargetState</c>.
    /// </exception>
    public Task<CreateActivityResponse> CreateActivityForEventAsync(
        long eventId,
        ReportActivity activity,
        string? internalNote = null,
        string? publicNote = null,
        bool? isAutomated = null,
        CancellationToken cancellationToken = default) =>
        SendActivityAsync(
            new CreateActivityRequest
            {
                EventId = eventId,
                Activity = activity,
                InternalNote = internalNote,
                PublicNote = publicNote,
                IsAutomated = isAutomated,
            },
            cancellationToken);

    private Task<CreateActivityResponse> SendActivityAsync(
        CreateActivityRequest request, CancellationToken cancellationToken) =>
        _xrpc.ProcedureAsync<CreateActivityResponse>(
            "tools.ozone.report.createActivity", request, cancellationToken: cancellationToken);

    /// <summary>
    /// List one page of a report's activities, most recent first.
    /// </summary>
    /// <param name="reportId">The report.</param>
    /// <param name="limit">Maximum number of activities (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListActivitiesResponse> ListActivitiesAsync(
        long reportId,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("reportId", reportId)
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<ListActivitiesResponse>(
            "tools.ozone.report.listActivities", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every activity on a report, most recent first, fetching pages as needed.
    /// </summary>
    /// <param name="reportId">The report.</param>
    /// <param name="pageSize">Activities per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ReportActivityView> EnumerateReportActivitiesAsync(
        long reportId,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListActivitiesResponse, ReportActivityView>(
            (cursor, ct) => ListActivitiesAsync(reportId, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Query one page of activities across all reports, ordered by creation time: for pollers
    /// that follow report activity. For one report's history use <see cref="ListActivitiesAsync"/>.
    /// </summary>
    /// <param name="activityTypes">
    /// Only activities of these types, such as <c>closeActivity</c> or <c>escalationActivity</c>.
    /// </param>
    /// <param name="createdAfter">Only activities created at or after this time.</param>
    /// <param name="createdBefore">Only activities created at or before this time.</param>
    /// <param name="sortDirection">The sort direction: <c>asc</c> or <c>desc</c> (the default).</param>
    /// <param name="limit">Maximum number of activities (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<QueryActivitiesResponse> QueryActivitiesAsync(
        IEnumerable<string>? activityTypes = null,
        AtDatetime? createdAfter = null,
        AtDatetime? createdBefore = null,
        string? sortDirection = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .AddAll("activityTypes", activityTypes)
            .Add("createdAfter", createdAfter?.ToString())
            .Add("createdBefore", createdBefore?.ToString())
            .Add("sortDirection", sortDirection)
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<QueryActivitiesResponse>(
            "tools.ozone.report.queryActivities", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every activity across all reports that matches the filters, fetching pages as
    /// needed.
    /// </summary>
    /// <param name="activityTypes">
    /// Only activities of these types, such as <c>closeActivity</c> or <c>escalationActivity</c>.
    /// </param>
    /// <param name="createdAfter">Only activities created at or after this time.</param>
    /// <param name="createdBefore">Only activities created at or before this time.</param>
    /// <param name="sortDirection">The sort direction: <c>asc</c> or <c>desc</c> (the default).</param>
    /// <param name="pageSize">Activities per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ReportActivityView> EnumerateActivitiesAsync(
        IEnumerable<string>? activityTypes = null,
        AtDatetime? createdAfter = null,
        AtDatetime? createdBefore = null,
        string? sortDirection = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<QueryActivitiesResponse, ReportActivityView>(
            (cursor, ct) => QueryActivitiesAsync(activityTypes, createdAfter, createdBefore, sortDirection, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get report statistics for the current day. Leave every filter out for the totals.
    /// </summary>
    /// <param name="queueId">Only reports in this queue; <c>-1</c> for reports in no queue.</param>
    /// <param name="moderatorDid">Only reports handled by this moderator.</param>
    /// <param name="reportTypes">Only reports of these reason types.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetLiveStatsResponse> GetLiveStatsAsync(
        long? queueId = null,
        Did? moderatorDid = null,
        IEnumerable<string>? reportTypes = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("queueId", queueId)
            .Add("moderatorDid", moderatorDid)
            .AddAll("reportTypes", reportTypes);
        return _xrpc.QueryAsync<GetLiveStatsResponse>(
            "tools.ozone.report.getLiveStats", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get one page of daily report statistics, newest first.
    /// </summary>
    /// <param name="queueId">Only reports in this queue; <c>-1</c> for reports in no queue.</param>
    /// <param name="moderatorDid">Only reports handled by this moderator.</param>
    /// <param name="reportTypes">Only reports of these reason types.</param>
    /// <param name="startDate">The earliest day to include.</param>
    /// <param name="endDate">The latest day to include.</param>
    /// <param name="limit">Maximum number of days (1-100, default 30).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetHistoricalStatsResponse> GetHistoricalStatsAsync(
        long? queueId = null,
        Did? moderatorDid = null,
        IEnumerable<string>? reportTypes = null,
        AtDatetime? startDate = null,
        AtDatetime? endDate = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("queueId", queueId)
            .Add("moderatorDid", moderatorDid)
            .AddAll("reportTypes", reportTypes)
            .Add("startDate", startDate?.ToString())
            .Add("endDate", endDate?.ToString())
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<GetHistoricalStatsResponse>(
            "tools.ozone.report.getHistoricalStats", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the daily report statistics, newest first, fetching pages as needed.
    /// </summary>
    /// <param name="queueId">Only reports in this queue; <c>-1</c> for reports in no queue.</param>
    /// <param name="moderatorDid">Only reports handled by this moderator.</param>
    /// <param name="reportTypes">Only reports of these reason types.</param>
    /// <param name="startDate">The earliest day to include.</param>
    /// <param name="endDate">The latest day to include.</param>
    /// <param name="pageSize">Days per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<HistoricalStats> EnumerateHistoricalStatsAsync(
        long? queueId = null,
        Did? moderatorDid = null,
        IEnumerable<string>? reportTypes = null,
        AtDatetime? startDate = null,
        AtDatetime? endDate = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetHistoricalStatsResponse, HistoricalStats>(
            (cursor, ct) => GetHistoricalStatsAsync(queueId, moderatorDid, reportTypes, startDate, endDate, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Recompute the daily report statistics for a range of days, to backfill after a failure or a
    /// data correction.
    /// </summary>
    /// <param name="startDate">The first day to recompute.</param>
    /// <param name="endDate">The last day to recompute.</param>
    /// <param name="queueIds">Only these queues' statistics; <see langword="null"/> for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RefreshStatsAsync(
        DateOnly startDate,
        DateOnly endDate,
        IEnumerable<long>? queueIds = null,
        CancellationToken cancellationToken = default)
    {
        var request = new RefreshStatsRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            QueueIds = queueIds is null ? null : [.. queueIds],
        };
        return _xrpc.ProcedureAsync(
            "tools.ozone.report.refreshStats", request, cancellationToken: cancellationToken);
    }

    private static string FormatId(long id) => id.ToString(CultureInfo.InvariantCulture);
}
