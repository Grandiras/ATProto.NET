using System.Globalization;
using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Tools.Ozone.Queue;

/// <summary>
/// Client for tools.ozone.queue.* endpoints: custom moderation queues, the router that fills them
/// with reports, and the moderators assigned to them.
/// </summary>
public sealed class QueueClient
{
    private readonly XrpcClient _xrpc;

    internal QueueClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Create a moderation queue. The queue router fills a queue with the reports that match its
    /// criteria; a queue without criteria only gets reports sent to it by hand, through
    /// <c>modTool.meta.queueId</c> on an emitted event or with
    /// <see cref="Report.ReportClient.ReassignQueueAsync"/>.
    /// </summary>
    /// <param name="name">The queue's display name, unique among queues.</param>
    /// <param name="subjectTypes">
    /// The kinds of subject the queue takes (see <see cref="Report.ReportSubjectType"/>).
    /// </param>
    /// <param name="collection">
    /// The collection the queue takes record subjects from; required when
    /// <paramref name="subjectTypes"/> includes <c>record</c>.
    /// </param>
    /// <param name="reportTypes">The report reason types the queue takes (at most 25).</param>
    /// <param name="description">A description of the queue.</param>
    /// <param name="recommendedPolicies">The policies to recommend when actioning the queue's reports.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException"><c>InvalidRecommendedPolicies</c> or <c>ConflictingQueue</c>.</exception>
    public Task<CreateQueueResponse> CreateQueueAsync(
        string name,
        IEnumerable<string>? subjectTypes = null,
        Nsid? collection = null,
        IEnumerable<string>? reportTypes = null,
        string? description = null,
        IEnumerable<string>? recommendedPolicies = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateQueueRequest
        {
            Name = name,
            SubjectTypes = subjectTypes is null ? null : [.. subjectTypes],
            Collection = collection,
            ReportTypes = reportTypes is null ? null : [.. reportTypes],
            Description = description,
            RecommendedPolicies = recommendedPolicies is null ? null : [.. recommendedPolicies],
        };
        return _xrpc.ProcedureAsync<CreateQueueResponse>(
            "tools.ozone.queue.createQueue", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Change a queue's name, description, recommended policies or whether it is active.
    /// </summary>
    /// <param name="queueId">The queue.</param>
    /// <param name="name">The new display name.</param>
    /// <param name="enabled">Whether the queue is active.</param>
    /// <param name="description">The new description.</param>
    /// <param name="recommendedPolicies">The policies to recommend when actioning the queue's reports.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException"><c>InvalidRecommendedPolicies</c>.</exception>
    public Task<UpdateQueueResponse> UpdateQueueAsync(
        long queueId,
        string? name = null,
        bool? enabled = null,
        string? description = null,
        IEnumerable<string>? recommendedPolicies = null,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateQueueRequest
        {
            QueueId = queueId,
            Name = name,
            Enabled = enabled,
            Description = description,
            RecommendedPolicies = recommendedPolicies is null ? null : [.. recommendedPolicies],
        };
        return _xrpc.ProcedureAsync<UpdateQueueResponse>(
            "tools.ozone.queue.updateQueue", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete a queue, moving its reports to another queue or to none.
    /// </summary>
    /// <param name="queueId">The queue.</param>
    /// <param name="migrateToQueueId">
    /// The queue to move its reports to; <see langword="null"/> leaves them in no queue.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<DeleteQueueResponse> DeleteQueueAsync(
        long queueId,
        long? migrateToQueueId = null,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteQueueRequest { QueueId = queueId, MigrateToQueueId = migrateToQueueId };
        return _xrpc.ProcedureAsync<DeleteQueueResponse>(
            "tools.ozone.queue.deleteQueue", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List one page of the queues, with their statistics.
    /// </summary>
    /// <param name="enabled">Only active (or only inactive) queues; <see langword="null"/> for all.</param>
    /// <param name="subjectType">Only queues that take this kind of subject (see <see cref="Report.ReportSubjectType"/>).</param>
    /// <param name="collection">Only queues for this collection.</param>
    /// <param name="reportTypes">Only queues that take any of these report reason types (at most 10).</param>
    /// <param name="limit">Maximum number of queues (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListQueuesResponse> ListQueuesAsync(
        bool? enabled = null,
        string? subjectType = null,
        Nsid? collection = null,
        IEnumerable<string>? reportTypes = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("enabled", enabled)
            .Add("subjectType", subjectType)
            .Add("collection", collection)
            .AddAll("reportTypes", reportTypes)
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<ListQueuesResponse>(
            "tools.ozone.queue.listQueues", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every queue matching the filters, fetching pages as needed.
    /// </summary>
    /// <param name="enabled">Only active (or only inactive) queues; <see langword="null"/> for all.</param>
    /// <param name="subjectType">Only queues that take this kind of subject (see <see cref="Report.ReportSubjectType"/>).</param>
    /// <param name="collection">Only queues for this collection.</param>
    /// <param name="reportTypes">Only queues that take any of these report reason types (at most 10).</param>
    /// <param name="pageSize">Queues per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<QueueView> EnumerateQueuesAsync(
        bool? enabled = null,
        string? subjectType = null,
        Nsid? collection = null,
        IEnumerable<string>? reportTypes = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListQueuesResponse, QueueView>(
            (cursor, ct) => ListQueuesAsync(enabled, subjectType, collection, reportTypes, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Route the reports in a range of identifiers to the queues that match them.
    /// </summary>
    /// <param name="startReportId">The first report to route.</param>
    /// <param name="endReportId">The last report to route; the range must span fewer than 5,000 reports.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException"><c>OutOfRange</c> when the range is too wide.</exception>
    public Task<RouteReportsResponse> RouteReportsAsync(
        long startReportId,
        long endReportId,
        CancellationToken cancellationToken = default)
    {
        var request = new RouteReportsRequest { StartReportId = startReportId, EndReportId = endReportId };
        return _xrpc.ProcedureAsync<RouteReportsResponse>(
            "tools.ozone.queue.routeReports", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Assign a moderator to a queue.
    /// </summary>
    /// <param name="queueId">The queue.</param>
    /// <param name="did">The moderator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException"><c>InvalidAssignment</c>.</exception>
    public Task<AssignmentView> AssignModeratorAsync(
        long queueId,
        Did did,
        CancellationToken cancellationToken = default)
    {
        var request = new AssignModeratorRequest { QueueId = queueId, Did = did };
        return _xrpc.ProcedureAsync<AssignmentView>(
            "tools.ozone.queue.assignModerator", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Remove a moderator's assignment to a queue.
    /// </summary>
    /// <param name="queueId">The queue.</param>
    /// <param name="did">The moderator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException"><c>InvalidAssignment</c>.</exception>
    public Task UnassignModeratorAsync(
        long queueId,
        Did did,
        CancellationToken cancellationToken = default)
    {
        var request = new UnassignModeratorRequest { QueueId = queueId, Did = did };
        return _xrpc.ProcedureAsync(
            "tools.ozone.queue.unassignModerator", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get one page of moderators' queue assignments.
    /// </summary>
    /// <param name="queueIds">Only assignments to these queues.</param>
    /// <param name="dids">Only assignments of these moderators.</param>
    /// <param name="onlyActive">Only active assignments; the server default is <see langword="true"/>.</param>
    /// <param name="limit">Maximum number of assignments (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetAssignmentsResponse> GetAssignmentsAsync(
        IEnumerable<long>? queueIds = null,
        IEnumerable<Did>? dids = null,
        bool? onlyActive = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("onlyActive", onlyActive)
            .AddAll("queueIds", queueIds?.Select(FormatId))
            .AddAll("dids", dids?.Select(did => did.Value))
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<GetAssignmentsResponse>(
            "tools.ozone.queue.getAssignments", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every queue assignment matching the filters, fetching pages as needed.
    /// </summary>
    /// <param name="queueIds">Only assignments to these queues.</param>
    /// <param name="dids">Only assignments of these moderators.</param>
    /// <param name="onlyActive">Only active assignments; the server default is <see langword="true"/>.</param>
    /// <param name="pageSize">Assignments per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<AssignmentView> EnumerateAssignmentsAsync(
        IEnumerable<long>? queueIds = null,
        IEnumerable<Did>? dids = null,
        bool? onlyActive = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetAssignmentsResponse, AssignmentView>(
            (cursor, ct) => GetAssignmentsAsync(queueIds, dids, onlyActive, pageSize, cursor, ct),
            cancellationToken);

    private static string FormatId(long id) => id.ToString(CultureInfo.InvariantCulture);
}
