using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Queue;
using ATProtoNet.Lexicon.Tools.Ozone.Report;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Ozone;

/// <summary>
/// tools.ozone.queue: one test per endpoint, checking the method, what goes on the wire and that
/// the typed response reads back.
/// </summary>
public sealed class OzoneQueueClientTests : IDisposable
{
    private const string ModDid = TestIds.ModDid;

    private const string QueueJson = $$$"""
        {"id":4,"name":"Post spam","subjectTypes":["record"],"collection":"app.bsky.feed.post",
         "reportTypes":["tools.ozone.report.defs#reasonMisleadingSpam"],"description":"Spammy posts",
         "recommendedPolicies":["spam"],"createdBy":"{{{ModDid}}}","createdAt":"2026-09-01T00:00:00.000Z",
         "updatedAt":"2026-09-02T00:00:00.000Z","enabled":true,
         "stats":{"pendingCount":3,"actionedCount":10,"inboundCount":13,"actionRate":77,"lastUpdated":"2026-09-02T00:00:00.000Z"}}
        """;

    private const string AssignmentJson =
        $$"""{"id":8,"did":"{{ModDid}}","queue":{{QueueJson}},"startAt":"2026-09-01T00:00:00.000Z"}""";

    private readonly XrpcTestClient _fixture = new(instanceUrl: "https://ozone.example.com");

    private QueueClient Queues => _fixture.Client.Ozone.Queue;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task CreateQueueAsync_PostsTheCriteria_ReadsTheQueue()
    {
        _fixture.On("tools.ozone.queue.createQueue", $$"""{"queue":{{QueueJson}}}""");

        var result = await Queues.CreateQueueAsync(
            "Post spam",
            [ReportSubjectType.Record],
            Nsid.Parse("app.bsky.feed.post"),
            [ReportReasons.MisleadingSpam],
            "Spammy posts",
            ["spam"]);

        var body = _fixture.AssertPost("tools.ozone.queue.createQueue").JsonBody;
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
        _fixture.On("tools.ozone.queue.createQueue", $$"""{"queue":{{QueueJson}}}""");

        await Queues.CreateQueueAsync("Manual");

        _fixture.AssertPost("tools.ozone.queue.createQueue", """{"name":"Manual"}""");
    }

    [Fact]
    public async Task UpdateQueueAsync_PostsTheChanges()
    {
        _fixture.On("tools.ozone.queue.updateQueue", $$"""{"queue":{{QueueJson}}}""");

        var result = await Queues.UpdateQueueAsync(4, enabled: false, description: "Paused");

        _fixture.AssertPost("tools.ozone.queue.updateQueue", """{"queueId":4,"enabled":false,"description":"Paused"}""");
        Assert.True(result.Queue.Enabled);
    }

    // deleteQueue (its response check too) is covered by ATProtoNet.Tests.Lexicon.EndpointRequestTests.

    [Fact]
    public async Task ListQueuesAsync_SendsTheFilters_ReadsTheQueues()
    {
        _fixture.On("tools.ozone.queue.listQueues", $$"""{"cursor":"c1","queues":[{{QueueJson}}]}""");

        var page = await Queues.ListQueuesAsync(
            enabled: true,
            subjectType: ReportSubjectType.Record,
            collection: Nsid.Parse("app.bsky.feed.post"),
            reportTypes: [ReportReasons.MisleadingSpam],
            limit: 10);

        _fixture.AssertGet(
            "tools.ozone.queue.listQueues",
            "enabled=true&subjectType=record&collection=app.bsky.feed.post" +
            "&reportTypes=tools.ozone.report.defs#reasonMisleadingSpam&limit=10");
        Assert.Equal("c1", page.Cursor);
        Assert.Equal("Post spam", Assert.Single(page.Queues).Name);
    }

    // routeReports and unassignModerator (ack-only) are covered by
    // ATProtoNet.Tests.Lexicon.EndpointRequestTests.

    [Fact]
    public async Task AssignModeratorAsync_PostsQueueAndDid_ReadsTheAssignment()
    {
        _fixture.On("tools.ozone.queue.assignModerator", AssignmentJson);

        var assignment = await Queues.AssignModeratorAsync(4, Did.Parse(ModDid));

        _fixture.AssertPost("tools.ozone.queue.assignModerator", $$"""{"queueId":4,"did":"{{ModDid}}"}""");
        Assert.Equal(8L, assignment.Id);
        Assert.Equal(4L, assignment.Queue.Id);
    }

    [Fact]
    public async Task GetAssignmentsAsync_SendsEachQueueAndDid()
    {
        _fixture.On("tools.ozone.queue.getAssignments", $$"""{"assignments":[{{AssignmentJson}}]}""");

        var page = await Queues.GetAssignmentsAsync([4, 5], [Did.Parse(ModDid)], onlyActive: true, limit: 20, cursor: "c0");

        _fixture.AssertGet("tools.ozone.queue.getAssignments", $"onlyActive=true&queueIds=4&queueIds=5&dids={ModDid}&limit=20&cursor=c0");
        Assert.Equal(Did.Parse(ModDid), Assert.Single(page.Assignments).Did);
    }
}
