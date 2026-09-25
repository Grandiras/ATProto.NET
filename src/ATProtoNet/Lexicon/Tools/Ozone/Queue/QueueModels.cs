using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Tools.Ozone.Team;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Tools.Ozone.Queue;

/// <summary>
/// A moderation queue: a named bucket that Ozone's queue router fills with the reports matching
/// its criteria (<c>tools.ozone.queue.defs#queueView</c>).
/// </summary>
public sealed class QueueView : LexObject
{
    /// <summary>The queue's identifier.</summary>
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    /// <summary>The queue's display name, unique among queues.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The kinds of subject the queue takes (see <see cref="Report.ReportSubjectType"/>).
    /// </summary>
    [JsonPropertyName("subjectTypes")]
    public IReadOnlyList<string>? SubjectTypes { get; init; }

    /// <summary>The collection the queue takes record subjects from, such as <c>app.bsky.feed.post</c>.</summary>
    [JsonPropertyName("collection")]
    public Nsid? Collection { get; init; }

    /// <summary>The report reason types the queue takes (see <c>ReportReasons</c>).</summary>
    [JsonPropertyName("reportTypes")]
    public IReadOnlyList<string>? ReportTypes { get; init; }

    /// <summary>A description of the queue.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The policies to recommend when actioning the queue's reports.</summary>
    [JsonPropertyName("recommendedPolicies")]
    public IReadOnlyList<string>? RecommendedPolicies { get; init; }

    /// <summary>The DID of the moderator who created the queue.</summary>
    [JsonPropertyName("createdBy")]
    public required Did CreatedBy { get; init; }

    /// <summary>When the queue was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>When the queue last changed.</summary>
    [JsonPropertyName("updatedAt")]
    public required AtDatetime UpdatedAt { get; init; }

    /// <summary>Whether the queue is active.</summary>
    [JsonPropertyName("enabled")]
    public required bool Enabled { get; init; }

    /// <summary>When the queue was deleted, if it was.</summary>
    [JsonPropertyName("deletedAt")]
    public AtDatetime? DeletedAt { get; init; }

    /// <summary>Statistics about the queue's reports.</summary>
    [JsonPropertyName("stats")]
    public required QueueStats Stats { get; init; }
}

/// <summary>
/// Statistics about a queue's reports (<c>tools.ozone.queue.defs#queueStats</c>).
/// </summary>
public sealed class QueueStats : LexObject
{
    /// <summary>The reports in <c>open</c> status.</summary>
    [JsonPropertyName("pendingCount")]
    public int? PendingCount { get; init; }

    /// <summary>The reports in <c>closed</c> status.</summary>
    [JsonPropertyName("actionedCount")]
    public int? ActionedCount { get; init; }

    /// <summary>The reports in <c>escalated</c> status.</summary>
    [JsonPropertyName("escalatedCount")]
    public int? EscalatedCount { get; init; }

    /// <summary>The reports the queue received in the last 24 hours.</summary>
    [JsonPropertyName("inboundCount")]
    public int? InboundCount { get; init; }

    /// <summary>
    /// The percentage of received reports that were actioned, rounded; absent when none were
    /// received.
    /// </summary>
    [JsonPropertyName("actionRate")]
    public int? ActionRate { get; init; }

    /// <summary>The average time in seconds from a report's creation to its close.</summary>
    [JsonPropertyName("avgHandlingTimeSec")]
    public int? AvgHandlingTimeSec { get; init; }

    /// <summary>When the statistics were computed.</summary>
    [JsonPropertyName("lastUpdated")]
    public AtDatetime? LastUpdated { get; init; }
}

/// <summary>
/// A moderator's assignment to a queue (<c>tools.ozone.queue.defs#assignmentView</c>).
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

    /// <summary>The queue.</summary>
    [JsonPropertyName("queue")]
    public required QueueView Queue { get; init; }

    /// <summary>When the assignment began.</summary>
    [JsonPropertyName("startAt")]
    public required AtDatetime StartAt { get; init; }

    /// <summary>When the assignment ends, if it does.</summary>
    [JsonPropertyName("endAt")]
    public AtDatetime? EndAt { get; init; }
}

// ─── Request / Response Models ───

/// <summary>
/// Request body for tools.ozone.queue.createQueue.
/// </summary>
internal sealed class CreateQueueRequest
{
    /// <summary>The queue's display name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The kinds of subject the queue takes.</summary>
    [JsonPropertyName("subjectTypes")]
    public IReadOnlyList<string>? SubjectTypes { get; init; }

    /// <summary>The collection the queue takes record subjects from.</summary>
    [JsonPropertyName("collection")]
    public Nsid? Collection { get; init; }

    /// <summary>The report reason types the queue takes.</summary>
    [JsonPropertyName("reportTypes")]
    public IReadOnlyList<string>? ReportTypes { get; init; }

    /// <summary>A description of the queue.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The policies to recommend when actioning the queue's reports.</summary>
    [JsonPropertyName("recommendedPolicies")]
    public IReadOnlyList<string>? RecommendedPolicies { get; init; }
}

/// <summary>
/// Response from tools.ozone.queue.createQueue.
/// </summary>
public sealed class CreateQueueResponse
{
    /// <summary>The new queue.</summary>
    [JsonPropertyName("queue")]
    public required QueueView Queue { get; init; }
}

/// <summary>
/// Request body for tools.ozone.queue.updateQueue.
/// </summary>
internal sealed class UpdateQueueRequest
{
    /// <summary>The queue to update.</summary>
    [JsonPropertyName("queueId")]
    public required long QueueId { get; init; }

    /// <summary>The new display name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Whether the queue is active.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>The new description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The policies to recommend when actioning the queue's reports.</summary>
    [JsonPropertyName("recommendedPolicies")]
    public IReadOnlyList<string>? RecommendedPolicies { get; init; }
}

/// <summary>
/// Response from tools.ozone.queue.updateQueue.
/// </summary>
public sealed class UpdateQueueResponse
{
    /// <summary>The updated queue.</summary>
    [JsonPropertyName("queue")]
    public required QueueView Queue { get; init; }
}

/// <summary>
/// Request body for tools.ozone.queue.deleteQueue.
/// </summary>
internal sealed class DeleteQueueRequest
{
    /// <summary>The queue to delete.</summary>
    [JsonPropertyName("queueId")]
    public required long QueueId { get; init; }

    /// <summary>The queue to move its reports to.</summary>
    [JsonPropertyName("migrateToQueueId")]
    public long? MigrateToQueueId { get; init; }
}

/// <summary>
/// Response from tools.ozone.queue.deleteQueue.
/// </summary>
public sealed class DeleteQueueResponse
{
    /// <summary>Whether the queue was deleted.</summary>
    [JsonPropertyName("deleted")]
    public required bool Deleted { get; init; }

    /// <summary>The number of reports moved to another queue.</summary>
    [JsonPropertyName("reportsMigrated")]
    public int? ReportsMigrated { get; init; }
}

/// <summary>
/// Response from tools.ozone.queue.listQueues.
/// </summary>
public sealed class ListQueuesResponse : ICursorPage<QueueView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The queues.</summary>
    [JsonPropertyName("queues")]
    public required IReadOnlyList<QueueView> Queues { get; init; }

    IReadOnlyList<QueueView> ICursorPage<QueueView>.Items => Queues;
}

/// <summary>
/// Request body for tools.ozone.queue.assignModerator.
/// </summary>
internal sealed class AssignModeratorRequest
{
    /// <summary>The queue.</summary>
    [JsonPropertyName("queueId")]
    public required long QueueId { get; init; }

    /// <summary>The moderator to assign.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }
}

/// <summary>
/// Request body for tools.ozone.queue.unassignModerator.
/// </summary>
internal sealed class UnassignModeratorRequest
{
    /// <summary>The queue.</summary>
    [JsonPropertyName("queueId")]
    public required long QueueId { get; init; }

    /// <summary>The moderator to unassign.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }
}

/// <summary>
/// Response from tools.ozone.queue.getAssignments.
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
/// Request body for tools.ozone.queue.routeReports.
/// </summary>
internal sealed class RouteReportsRequest
{
    /// <summary>The first report to route.</summary>
    [JsonPropertyName("startReportId")]
    public required long StartReportId { get; init; }

    /// <summary>The last report to route.</summary>
    [JsonPropertyName("endReportId")]
    public required long EndReportId { get; init; }
}

/// <summary>
/// Response from tools.ozone.queue.routeReports.
/// </summary>
public sealed class RouteReportsResponse
{
    /// <summary>The number of reports routed to a queue.</summary>
    [JsonPropertyName("assigned")]
    public required int Assigned { get; init; }

    /// <summary>The number of reports no queue matched.</summary>
    [JsonPropertyName("unmatched")]
    public required int Unmatched { get; init; }
}
