using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Queue;
using ATProtoNet.Lexicon.Tools.Ozone.Report;

namespace ATProtoNet.Tests.Ozone;

/// <summary>
/// tools.ozone.queue: one test per endpoint, checking the method, what goes on the wire and that
/// the typed response reads back.
/// </summary>
public sealed class OzoneQueueClientTests : IDisposable
{
    private const string ModDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";

    private const string QueueJson = $$$"""
        {"id":4,"name":"Post spam","subjectTypes":["record"],"collection":"app.bsky.feed.post",
         "reportTypes":["tools.ozone.report.defs#reasonMisleadingSpam"],"description":"Spammy posts",
         "recommendedPolicies":["spam"],"createdBy":"{{{ModDid}}}","createdAt":"2026-09-01T00:00:00.000Z",
         "updatedAt":"2026-09-02T00:00:00.000Z","enabled":true,
         "stats":{"pendingCount":3,"actionedCount":10,"inboundCount":13,"actionRate":77,"lastUpdated":"2026-09-02T00:00:00.000Z"}}
        """;

    private const string AssignmentJson =
        $$"""{"id":8,"did":"{{ModDid}}","queue":{{QueueJson}},"startAt":"2026-09-01T00:00:00.000Z"}""";

    private readonly OzoneTestClient _ozone = new();

    private QueueClient Queues => _ozone.Client.Ozone.Queue;

    public void Dispose() => _ozone.Dispose();

    [Fact]
    public async Task CreateQueueAsync_PostsTheCriteria_ReadsTheQueue()
    {
        _ozone.Respond($$"""{"queue":{{QueueJson}}}""");

        var result = await Queues.CreateQueueAsync(
            "Post spam",
            [ReportSubjectType.Record],
            Nsid.Parse("app.bsky.feed.post"),
            [ReportReasons.MisleadingSpam],
            "Spammy posts",
            ["spam"]);

        var body = _ozone.Sent.IsProcedure("tools.ozone.queue.createQueue").Json;
        Assert.Equal("Post spam", body.GetProperty("name").GetString());
        Assert.Equal("record", body.GetProperty("subjectTypes")[0].GetString());
        Assert.Equal("app.bsky.feed.post", body.GetProperty("collection").GetString());
        Assert.Equal(ReportReasons.MisleadingSpam, body.GetProperty("reportTypes")[0].GetString());
        Assert.Equal("spam", body.GetProperty("recommendedPolicies")[0].GetString());

        var queue = result.Queue;
        Assert.Equal(4L, queue.Id);
        Assert.Equal(Nsid.Parse("app.bsky.feed.post"), queue.Collection);
        Assert.Equal(Did.Parse(ModDid), queue.CreatedBy);
        Assert.Equal(77, queue.Stats.ActionRate);
        Assert.Null(queue.Stats.EscalatedCount);
    }

    [Fact]
    public async Task CreateQueueAsync_OnlyAName_SendsOnlyTheName()
    {
        _ozone.Respond($$"""{"queue":{{QueueJson}}}""");

        await Queues.CreateQueueAsync("Manual");

        Assert.Equal("""{"name":"Manual"}""", _ozone.Sent.Body);
    }

    [Fact]
    public async Task UpdateQueueAsync_PostsTheChanges()
    {
        _ozone.Respond($$"""{"queue":{{QueueJson}}}""");

        var result = await Queues.UpdateQueueAsync(4, enabled: false, description: "Paused");

        Assert.Equal(
            """{"queueId":4,"enabled":false,"description":"Paused"}""",
            _ozone.Sent.IsProcedure("tools.ozone.queue.updateQueue").Body);
        Assert.True(result.Queue.Enabled);
    }

    [Fact]
    public async Task DeleteQueueAsync_PostsTheMigrationTarget()
    {
        _ozone.Respond("""{"deleted":true,"reportsMigrated":12}""");

        var result = await Queues.DeleteQueueAsync(4, migrateToQueueId: 5);

        Assert.Equal(
            """{"queueId":4,"migrateToQueueId":5}""",
            _ozone.Sent.IsProcedure("tools.ozone.queue.deleteQueue").Body);
        Assert.True(result.Deleted);
        Assert.Equal(12, result.ReportsMigrated);
    }

    [Fact]
    public async Task ListQueuesAsync_SendsTheFilters_ReadsTheQueues()
    {
        _ozone.Respond($$"""{"cursor":"c1","queues":[{{QueueJson}}]}""");

        var page = await Queues.ListQueuesAsync(
            enabled: true,
            subjectType: ReportSubjectType.Record,
            collection: Nsid.Parse("app.bsky.feed.post"),
            reportTypes: [ReportReasons.MisleadingSpam],
            limit: 10);

        Assert.Equal(
            "?enabled=true&subjectType=record&collection=app.bsky.feed.post" +
            "&reportTypes=tools.ozone.report.defs#reasonMisleadingSpam&limit=10",
            _ozone.Sent.IsQuery("tools.ozone.queue.listQueues").Query);
        Assert.Equal("c1", page.Cursor);
        Assert.Equal("Post spam", Assert.Single(page.Queues).Name);
    }

    [Fact]
    public async Task RouteReportsAsync_PostsTheRange_ReadsTheCounts()
    {
        _ozone.Respond("""{"assigned":40,"unmatched":2}""");

        var result = await Queues.RouteReportsAsync(1000, 1999);

        Assert.Equal(
            """{"startReportId":1000,"endReportId":1999}""",
            _ozone.Sent.IsProcedure("tools.ozone.queue.routeReports").Body);
        Assert.Equal(40, result.Assigned);
        Assert.Equal(2, result.Unmatched);
    }

    [Fact]
    public async Task AssignModeratorAsync_PostsQueueAndDid_ReadsTheAssignment()
    {
        _ozone.Respond(AssignmentJson);

        var assignment = await Queues.AssignModeratorAsync(4, Did.Parse(ModDid));

        Assert.Equal(
            $$"""{"queueId":4,"did":"{{ModDid}}"}""",
            _ozone.Sent.IsProcedure("tools.ozone.queue.assignModerator").Body);
        Assert.Equal(8L, assignment.Id);
        Assert.Equal(4L, assignment.Queue.Id);
    }

    [Fact]
    public async Task UnassignModeratorAsync_PostsQueueAndDid()
    {
        _ozone.Respond("{}");

        await Queues.UnassignModeratorAsync(4, Did.Parse(ModDid));

        Assert.Equal(
            $$"""{"queueId":4,"did":"{{ModDid}}"}""",
            _ozone.Sent.IsProcedure("tools.ozone.queue.unassignModerator").Body);
    }

    [Fact]
    public async Task GetAssignmentsAsync_SendsEachQueueAndDid()
    {
        _ozone.Respond($$"""{"assignments":[{{AssignmentJson}}]}""");

        var page = await Queues.GetAssignmentsAsync([4, 5], [Did.Parse(ModDid)], onlyActive: true, limit: 20, cursor: "c0");

        Assert.Equal(
            $"?onlyActive=true&queueIds=4&queueIds=5&dids={ModDid}&limit=20&cursor=c0",
            _ozone.Sent.IsQuery("tools.ozone.queue.getAssignments").Query);
        Assert.Equal(Did.Parse(ModDid), Assert.Single(page.Assignments).Did);
    }

    [Theory]
    [InlineData("queues")]
    [InlineData("assignments")]
    public async Task Enumerate_WalksPagesWithPageSize(string listing)
    {
        var (nsid, item) = listing == "queues"
            ? ("tools.ozone.queue.listQueues", QueueJson)
            : ("tools.ozone.queue.getAssignments", AssignmentJson);
        _ozone.Respond($$"""{"cursor":"c1","{{listing}}":[{{item}}]}""");
        _ozone.Respond($$"""{"{{listing}}":[{{item}}]}""");

        var count = 0;
        if (listing == "queues")
        {
            await foreach (var _ in Queues.EnumerateQueuesAsync(pageSize: 2))
                count++;
        }
        else
        {
            await foreach (var _ in Queues.EnumerateAssignmentsAsync(pageSize: 2))
                count++;
        }

        Assert.Equal(2, count);
        Assert.All(_ozone.Requests, r => Assert.Equal(nsid, r.Nsid));
        Assert.All(_ozone.Requests, r => Assert.Contains("limit=2", r.Query));
        Assert.Contains("cursor=c1", _ozone.Requests[1].Query);
    }
}
