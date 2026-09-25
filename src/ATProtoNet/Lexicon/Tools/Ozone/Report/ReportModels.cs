using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Queue;
using ATProtoNet.Lexicon.Tools.Ozone.Team;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Report;

// ─── Reports ───

/// <summary>
/// One report: an individual instance of a subject being reported, as opposed to the subject's
/// status, which aggregates its reports (<c>tools.ozone.report.defs#reportView</c>).
/// </summary>
public sealed class ReportView : LexObject
{
    /// <summary>The report's identifier.</summary>
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    /// <summary>The moderation event that created the report.</summary>
    [JsonPropertyName("eventId")]
    public required long EventId { get; init; }

    /// <summary>The report's status (see <see cref="ReportStatus"/>).</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>The reported subject, with its details.</summary>
    [JsonPropertyName("subject")]
    public required SubjectView Subject { get; init; }

    /// <summary>The report's reason type (see <c>ReportReasons</c>).</summary>
    [JsonPropertyName("reportType")]
    public required string ReportType { get; init; }

    /// <summary>The DID of the account that filed the report.</summary>
    [JsonPropertyName("reportedBy")]
    public required Did ReportedBy { get; init; }

    /// <summary>The reporter's account, with its details.</summary>
    [JsonPropertyName("reporter")]
    public required SubjectView Reporter { get; init; }

    /// <summary>The reporter's comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>When the report was filed.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>When the report last changed.</summary>
    [JsonPropertyName("updatedAt")]
    public AtDatetime? UpdatedAt { get; init; }

    /// <summary>When the report entered its current queue.</summary>
    [JsonPropertyName("queuedAt")]
    public AtDatetime? QueuedAt { get; init; }

    /// <summary>The moderation events that actioned the report, most recent first.</summary>
    [JsonPropertyName("actionEventIds")]
    public IReadOnlyList<long>? ActionEventIds { get; init; }

    /// <summary>Those events in full, when the server expands them.</summary>
    [JsonPropertyName("actions")]
    public IReadOnlyList<ModEventView>? Actions { get; init; }

    /// <summary>The note sent to the reporter when the report was actioned.</summary>
    [JsonPropertyName("actionNote")]
    public string? ActionNote { get; init; }

    /// <summary>The reported subject's current moderation status.</summary>
    [JsonPropertyName("subjectStatus")]
    public SubjectStatusView? SubjectStatus { get; init; }

    /// <summary>The number of other pending reports on the same subject.</summary>
    [JsonPropertyName("relatedReportCount")]
    public int? RelatedReportCount { get; init; }

    /// <summary>The moderator assigned to the report, if any.</summary>
    [JsonPropertyName("assignment")]
    public ReportAssignment? Assignment { get; init; }

    /// <summary>The queue the report is in, if any.</summary>
    [JsonPropertyName("queue")]
    public QueueView? Queue { get; init; }

    /// <summary>
    /// Whether the report is muted: its reporter or its subject was muted when it was filed.
    /// </summary>
    [JsonPropertyName("isMuted")]
    public bool? IsMuted { get; init; }

    /// <summary>Whether automated tooling filed the report.</summary>
    [JsonPropertyName("isAutomated")]
    public bool? IsAutomated { get; init; }
}

/// <summary>
/// The statuses of a report (<see cref="ReportView.Status"/>), for
/// <see cref="ReportClient.QueryReportsAsync"/>.
/// </summary>
public static class ReportStatus
{
    /// <summary>Waiting for review.</summary>
    public const string Open = "open";

    /// <summary>Closed, with or without action.</summary>
    public const string Closed = "closed";

    /// <summary>Escalated for further review.</summary>
    public const string Escalated = "escalated";

    /// <summary>Routed to a queue.</summary>
    public const string Queued = "queued";

    /// <summary>Assigned to a moderator.</summary>
    public const string Assigned = "assigned";
}

/// <summary>
/// The kinds of subject a report or queue is about, for <see cref="ReportFilter.SubjectType"/> and
/// the queues' <c>SubjectTypes</c>.
/// </summary>
public static class ReportSubjectType
{
    /// <summary>An account.</summary>
    public const string Account = "account";

    /// <summary>A record.</summary>
    public const string Record = "record";

    /// <summary>A chat message.</summary>
    public const string Message = "message";

    /// <summary>A chat conversation.</summary>
    public const string Conversation = "conversation";
}

/// <summary>
/// The moderator currently assigned to a report (<c>tools.ozone.report.defs#reportAssignment</c>).
/// </summary>
public sealed class ReportAssignment : LexObject
{
    /// <summary>The DID of the assigned moderator.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The assigned moderator's team membership.</summary>
    [JsonPropertyName("moderator")]
    public TeamMember? Moderator { get; init; }

    /// <summary>When the report was assigned.</summary>
    [JsonPropertyName("assignedAt")]
    public required AtDatetime AssignedAt { get; init; }
}

/// <summary>
/// A moderator's assignment to a report (<c>tools.ozone.report.defs#assignmentView</c>).
/// </summary>
public sealed class AssignmentView : LexObject
{
    /// <summary>The assignment's identifier.</summary>
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    /// <summary>The DID of the assigned moderator.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The assigned moderator's team membership.</summary>
    [JsonPropertyName("moderator")]
    public TeamMember? Moderator { get; init; }

    /// <summary>The queue the assignment was made on, if any.</summary>
    [JsonPropertyName("queue")]
    public QueueView? Queue { get; init; }

    /// <summary>The assigned report.</summary>
    [JsonPropertyName("reportId")]
    public required long ReportId { get; init; }

    /// <summary>When the assignment began.</summary>
    [JsonPropertyName("startAt")]
    public required AtDatetime StartAt { get; init; }

    /// <summary>When the assignment ends; <see langword="null"/> for a permanent one.</summary>
    [JsonPropertyName("endAt")]
    public AtDatetime? EndAt { get; init; }
}

// ─── Activities ───

/// <summary>
/// What happened to a report (the open <c>tools.ozone.report.defs#reportActivityView.activity</c>
/// union). Record one with <see cref="ReportClient.CreateActivityAsync"/>; an activity type that
/// changes the report's status moves it to that status. An activity this SDK does not model reads
/// as <see cref="UnknownReportActivity"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownReportActivity))]
[JsonDerivedType(typeof(QueueActivity), "tools.ozone.report.defs#queueActivity")]
[JsonDerivedType(typeof(AssignmentActivity), "tools.ozone.report.defs#assignmentActivity")]
[JsonDerivedType(typeof(EscalationActivity), "tools.ozone.report.defs#escalationActivity")]
[JsonDerivedType(typeof(CloseActivity), "tools.ozone.report.defs#closeActivity")]
[JsonDerivedType(typeof(ReopenActivity), "tools.ozone.report.defs#reopenActivity")]
[JsonDerivedType(typeof(NoteActivity), "tools.ozone.report.defs#noteActivity")]
public abstract class ReportActivity : LexObject;

/// <summary>
/// A report activity whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownReportActivity : ReportActivity, IUnknownUnionVariant
{
    /// <summary>Creates an unknown report activity from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownReportActivity(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        Type = type;
        Raw = UnknownUnionVariant.RequireObject(raw);
    }

    /// <inheritdoc/>
    public string Type { get; }

    /// <inheritdoc/>
    public JsonElement Raw { get; }
}

/// <summary>The report was routed to a queue.</summary>
public sealed class QueueActivity : ReportActivity
{
    /// <summary>
    /// The report's status before the activity (see <see cref="ReportStatus"/>). The server fills
    /// it in; leave it unset when recording an activity.
    /// </summary>
    [JsonPropertyName("previousStatus")]
    public string? PreviousStatus { get; init; }
}

/// <summary>A moderator was assigned to the report.</summary>
public sealed class AssignmentActivity : ReportActivity
{
    /// <summary>
    /// The report's status before the activity (see <see cref="ReportStatus"/>). The server fills
    /// it in; leave it unset when recording an activity.
    /// </summary>
    [JsonPropertyName("previousStatus")]
    public string? PreviousStatus { get; init; }
}

/// <summary>The report was escalated.</summary>
public sealed class EscalationActivity : ReportActivity
{
    /// <summary>
    /// The report's status before the activity (see <see cref="ReportStatus"/>). The server fills
    /// it in; leave it unset when recording an activity.
    /// </summary>
    [JsonPropertyName("previousStatus")]
    public string? PreviousStatus { get; init; }
}

/// <summary>The report was closed.</summary>
public sealed class CloseActivity : ReportActivity
{
    /// <summary>
    /// The report's status before the activity (see <see cref="ReportStatus"/>). The server fills
    /// it in; leave it unset when recording an activity.
    /// </summary>
    [JsonPropertyName("previousStatus")]
    public string? PreviousStatus { get; init; }
}

/// <summary>A closed report was reopened; valid only on a closed report.</summary>
public sealed class ReopenActivity : ReportActivity
{
    /// <summary>
    /// The report's status before the activity (see <see cref="ReportStatus"/>). The server fills
    /// it in; leave it unset when recording an activity.
    /// </summary>
    [JsonPropertyName("previousStatus")]
    public string? PreviousStatus { get; init; }
}

/// <summary>
/// A note on the report: the activity's internal note for moderators, its public note for the
/// reporter, or both.
/// </summary>
public sealed class NoteActivity : ReportActivity;

/// <summary>
/// One activity on a report (<c>tools.ozone.report.defs#reportActivityView</c>).
/// </summary>
public sealed class ReportActivityView : LexObject
{
    /// <summary>The activity's identifier.</summary>
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    /// <summary>The report the activity belongs to.</summary>
    [JsonPropertyName("reportId")]
    public required long ReportId { get; init; }

    /// <summary>What happened.</summary>
    [JsonPropertyName("activity")]
    public required ReportActivity Activity { get; init; }

    /// <summary>A note for moderators only.</summary>
    [JsonPropertyName("internalNote")]
    public string? InternalNote { get; init; }

    /// <summary>A note the reporter may see.</summary>
    [JsonPropertyName("publicNote")]
    public string? PublicNote { get; init; }

    /// <summary>Loose, activity-specific metadata, such as an assignment's identifier.</summary>
    [JsonPropertyName("meta")]
    public JsonElement? Meta { get; init; }

    /// <summary>Whether an automated process (such as the queue router) recorded the activity.</summary>
    [JsonPropertyName("isAutomated")]
    public required bool IsAutomated { get; init; }

    /// <summary>
    /// The DID of the moderator who recorded the activity, or the service's DID for an automated one.
    /// </summary>
    [JsonPropertyName("createdBy")]
    public required Did CreatedBy { get; init; }

    /// <summary>The team membership of the moderator who recorded the activity.</summary>
    [JsonPropertyName("moderator")]
    public TeamMember? Moderator { get; init; }

    /// <summary>The report the activity belongs to, in full.</summary>
    [JsonPropertyName("report")]
    public ReportView? Report { get; init; }

    /// <summary>When the activity was recorded.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

// ─── Statistics ───

/// <summary>
/// Report statistics for the current day (<c>tools.ozone.report.defs#liveStats</c>).
/// </summary>
public sealed class LiveStats : LexObject
{
    /// <summary>The reports not closed yet.</summary>
    [JsonPropertyName("pendingCount")]
    public int? PendingCount { get; init; }

    /// <summary>The reports closed today.</summary>
    [JsonPropertyName("actionedCount")]
    public int? ActionedCount { get; init; }

    /// <summary>The reports escalated today.</summary>
    [JsonPropertyName("escalatedCount")]
    public int? EscalatedCount { get; init; }

    /// <summary>The reports received today.</summary>
    [JsonPropertyName("inboundCount")]
    public int? InboundCount { get; init; }

    /// <summary>The percentage of received reports that were actioned, rounded.</summary>
    [JsonPropertyName("actionRate")]
    public int? ActionRate { get; init; }

    /// <summary>
    /// The average time in seconds from a report's creation (or assignment) to its close.
    /// </summary>
    [JsonPropertyName("avgHandlingTimeSec")]
    public int? AvgHandlingTimeSec { get; init; }

    /// <summary>When the statistics were computed.</summary>
    [JsonPropertyName("lastUpdated")]
    public AtDatetime? LastUpdated { get; init; }
}

/// <summary>
/// Report statistics for one past day (<c>tools.ozone.report.defs#historicalStats</c>).
/// </summary>
public sealed class HistoricalStats : LexObject
{
    /// <summary>The day, as <c>YYYY-MM-DD</c>.</summary>
    [JsonPropertyName("date")]
    public required string Date { get; init; }

    /// <summary>When the statistics were computed.</summary>
    [JsonPropertyName("computedAt")]
    public AtDatetime? ComputedAt { get; init; }

    /// <summary>The reports not closed when the statistics were computed.</summary>
    [JsonPropertyName("pendingCount")]
    public int? PendingCount { get; init; }

    /// <summary>The reports closed that day.</summary>
    [JsonPropertyName("actionedCount")]
    public int? ActionedCount { get; init; }

    /// <summary>The reports escalated that day.</summary>
    [JsonPropertyName("escalatedCount")]
    public int? EscalatedCount { get; init; }

    /// <summary>The reports received that day.</summary>
    [JsonPropertyName("inboundCount")]
    public int? InboundCount { get; init; }

    /// <summary>The percentage of received reports that were actioned, rounded.</summary>
    [JsonPropertyName("actionRate")]
    public int? ActionRate { get; init; }

    /// <summary>
    /// The average time in seconds from a report's creation (or assignment) to its close.
    /// </summary>
    [JsonPropertyName("avgHandlingTimeSec")]
    public int? AvgHandlingTimeSec { get; init; }
}

// ─── Filters ───

/// <summary>
/// Which reports <see cref="ReportClient.QueryReportsAsync"/> returns besides their status, and in
/// what order. Every filter is optional; set only the ones you need.
/// </summary>
public sealed class ReportFilter
{
    internal static ReportFilter None { get; } = new();

    /// <summary>Only reports in this queue; <c>-1</c> for reports in no queue.</summary>
    public long? QueueId { get; init; }

    /// <summary>Only reports of these reason types (see <c>ReportReasons</c>).</summary>
    public IReadOnlyList<string>? ReportTypes { get; init; }

    /// <summary>Only reports on this subject: an account's DID, or a record's AT URI.</summary>
    public string? Subject { get; init; }

    /// <summary>
    /// Only reports on this account or on any of its records. Unlike <see cref="Subject"/>, which
    /// matches one account or one record, this covers both.
    /// </summary>
    public Did? Did { get; init; }

    /// <summary>Only reports on this kind of subject (see <see cref="ReportSubjectType"/>).</summary>
    public string? SubjectType { get; init; }

    /// <summary>
    /// Only reports on records in these collections (at most 20); ignored when
    /// <see cref="SubjectType"/> is <c>account</c>.
    /// </summary>
    public IReadOnlyList<Nsid>? Collections { get; init; }

    /// <summary>Only reports filed after this time.</summary>
    public AtDatetime? ReportedAfter { get; init; }

    /// <summary>Only reports filed before this time.</summary>
    public AtDatetime? ReportedBefore { get; init; }

    /// <summary>
    /// <see langword="true"/> for only muted reports, <see langword="false"/> (the server default)
    /// for only unmuted ones.
    /// </summary>
    public bool? IsMuted { get; init; }

    /// <summary>Only reports permanently assigned to this moderator.</summary>
    public Did? AssignedTo { get; init; }

    /// <summary>The field to sort by: <c>createdAt</c> (the default) or <c>updatedAt</c>.</summary>
    public string? SortField { get; init; }

    /// <summary>The sort direction: <c>asc</c> or <c>desc</c> (the default).</summary>
    public string? SortDirection { get; init; }

    internal XrpcParams ToParams(string status) => new XrpcParams()
        .Add("queueId", QueueId)
        .AddAll("reportTypes", ReportTypes)
        .Add("status", status)
        .Add("subject", Subject)
        .Add("did", Did)
        .Add("subjectType", SubjectType)
        .AddAll("collections", Collections?.Select(collection => collection.Value))
        .Add("reportedAfter", ReportedAfter?.ToString())
        .Add("reportedBefore", ReportedBefore?.ToString())
        .Add("isMuted", IsMuted)
        .Add("assignedTo", AssignedTo)
        .Add("sortField", SortField)
        .Add("sortDirection", SortDirection);
}

// ─── Request / Response Models ───

/// <summary>
/// Request body for tools.ozone.report.assignModerator.
/// </summary>
internal sealed class AssignModeratorRequest
{
    /// <summary>The report to assign.</summary>
    [JsonPropertyName("reportId")]
    public required long ReportId { get; init; }

    /// <summary>The queue to make the assignment on.</summary>
    [JsonPropertyName("queueId")]
    public long? QueueId { get; init; }

    /// <summary>The moderator to assign; the caller when unset.</summary>
    [JsonPropertyName("did")]
    public Did? Did { get; init; }

    /// <summary>Whether the assignment never expires.</summary>
    [JsonPropertyName("isPermanent")]
    public bool? IsPermanent { get; init; }
}

/// <summary>
/// Request body for tools.ozone.report.unassignModerator.
/// </summary>
internal sealed class UnassignModeratorRequest
{
    /// <summary>The report to unassign.</summary>
    [JsonPropertyName("reportId")]
    public required long ReportId { get; init; }
}

/// <summary>
/// Request body for tools.ozone.report.closeReports.
/// </summary>
internal sealed class CloseReportsRequest
{
    /// <summary>The subject whose reports to close: an account's DID, or a record's AT URI.</summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>Only reports of these reason types.</summary>
    [JsonPropertyName("reportTypes")]
    public IReadOnlyList<string>? ReportTypes { get; init; }

    /// <summary>A note for moderators, recorded on each close activity.</summary>
    [JsonPropertyName("internalNote")]
    public string? InternalNote { get; init; }

    /// <summary>Whether an automated process is closing the reports.</summary>
    [JsonPropertyName("isAutomated")]
    public bool? IsAutomated { get; init; }
}

/// <summary>
/// Response from tools.ozone.report.closeReports.
/// </summary>
public sealed class CloseReportsResponse
{
    /// <summary>The number of reports closed.</summary>
    [JsonPropertyName("closedCount")]
    public required int ClosedCount { get; init; }

    /// <summary>The reports closed.</summary>
    [JsonPropertyName("reportIds")]
    public required IReadOnlyList<long> ReportIds { get; init; }
}

/// <summary>
/// Request body for tools.ozone.report.createActivity.
/// </summary>
internal sealed class CreateActivityRequest
{
    /// <summary>The report to record the activity on.</summary>
    [JsonPropertyName("reportId")]
    public long? ReportId { get; init; }

    /// <summary>The moderation event whose report to record the activity on.</summary>
    [JsonPropertyName("eventId")]
    public long? EventId { get; init; }

    /// <summary>What happened.</summary>
    [JsonPropertyName("activity")]
    public required ReportActivity Activity { get; init; }

    /// <summary>A note for moderators only.</summary>
    [JsonPropertyName("internalNote")]
    public string? InternalNote { get; init; }

    /// <summary>A note the reporter may see.</summary>
    [JsonPropertyName("publicNote")]
    public string? PublicNote { get; init; }

    /// <summary>Whether an automated process is recording the activity.</summary>
    [JsonPropertyName("isAutomated")]
    public bool? IsAutomated { get; init; }
}

/// <summary>
/// Response from tools.ozone.report.createActivity.
/// </summary>
public sealed class CreateActivityResponse
{
    /// <summary>The recorded activity.</summary>
    [JsonPropertyName("activity")]
    public required ReportActivityView Activity { get; init; }
}

/// <summary>
/// Response from tools.ozone.report.getAssignments.
/// </summary>
public sealed class GetAssignmentsResponse : ICursorPage<AssignmentView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The assignments.</summary>
    [JsonPropertyName("assignments")]
    public required IReadOnlyList<AssignmentView> Assignments { get; init; }

    IReadOnlyList<AssignmentView> ICursorPage<AssignmentView>.Items => Assignments;
}

/// <summary>
/// Response from tools.ozone.report.getHistoricalStats.
/// </summary>
public sealed class GetHistoricalStatsResponse : ICursorPage<HistoricalStats>
{
    /// <summary>The daily statistics, newest first.</summary>
    [JsonPropertyName("stats")]
    public required IReadOnlyList<HistoricalStats> Stats { get; init; }

    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    IReadOnlyList<HistoricalStats> ICursorPage<HistoricalStats>.Items => Stats;
}

/// <summary>
/// Response from tools.ozone.report.getLatestReport.
/// </summary>
public sealed class GetLatestReportResponse
{
    /// <summary>The most recent report.</summary>
    [JsonPropertyName("report")]
    public required ReportView Report { get; init; }
}

/// <summary>
/// Response from tools.ozone.report.getLiveStats.
/// </summary>
public sealed class GetLiveStatsResponse
{
    /// <summary>The statistics.</summary>
    [JsonPropertyName("stats")]
    public required LiveStats Stats { get; init; }
}

/// <summary>
/// Response from tools.ozone.report.listActivities.
/// </summary>
public sealed class ListActivitiesResponse : ICursorPage<ReportActivityView>
{
    /// <summary>The report's activities, most recent first.</summary>
    [JsonPropertyName("activities")]
    public required IReadOnlyList<ReportActivityView> Activities { get; init; }

    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    IReadOnlyList<ReportActivityView> ICursorPage<ReportActivityView>.Items => Activities;
}

/// <summary>
/// Response from tools.ozone.report.queryActivities.
/// </summary>
public sealed class QueryActivitiesResponse : ICursorPage<ReportActivityView>
{
    /// <summary>The activities.</summary>
    [JsonPropertyName("activities")]
    public required IReadOnlyList<ReportActivityView> Activities { get; init; }

    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    IReadOnlyList<ReportActivityView> ICursorPage<ReportActivityView>.Items => Activities;
}

/// <summary>
/// Response from tools.ozone.report.queryReports.
/// </summary>
public sealed class QueryReportsResponse : ICursorPage<ReportView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The reports.</summary>
    [JsonPropertyName("reports")]
    public required IReadOnlyList<ReportView> Reports { get; init; }

    IReadOnlyList<ReportView> ICursorPage<ReportView>.Items => Reports;
}

/// <summary>
/// Request body for tools.ozone.report.reassignQueue.
/// </summary>
internal sealed class ReassignQueueRequest
{
    /// <summary>The report to move.</summary>
    [JsonPropertyName("reportId")]
    public required long ReportId { get; init; }

    /// <summary>The queue to move it to; <c>-1</c> for none.</summary>
    [JsonPropertyName("queueId")]
    public required long QueueId { get; init; }

    /// <summary>A note for moderators, recorded on the queue activity.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

/// <summary>
/// Response from tools.ozone.report.reassignQueue.
/// </summary>
public sealed class ReassignQueueResponse
{
    /// <summary>The report, in its new queue.</summary>
    [JsonPropertyName("report")]
    public required ReportView Report { get; init; }
}

/// <summary>
/// Request body for tools.ozone.report.refreshStats.
/// </summary>
internal sealed class RefreshStatsRequest
{
    /// <summary>The first day to recompute.</summary>
    [JsonPropertyName("startDate")]
    public required DateOnly StartDate { get; init; }

    /// <summary>The last day to recompute.</summary>
    [JsonPropertyName("endDate")]
    public required DateOnly EndDate { get; init; }

    /// <summary>Only these queues' statistics.</summary>
    [JsonPropertyName("queueIds")]
    public IReadOnlyList<long>? QueueIds { get; init; }
}
