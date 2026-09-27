using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Report;
using ATProtoNet.Lexicon.Tools.Ozone.Team;
using ATProtoNet.Serialization;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Ozone;

/// <summary>
/// tools.ozone.report: one test per endpoint, checking the method, what goes on the wire and that
/// the typed response reads back.
/// </summary>
public sealed class OzoneReportClientTests : IDisposable
{
    private const string ModDid = TestIds.ModDid;
    private const string UserDid = "did:plc:z72i7hdynmk6r22z27h6tvur";
    private const string PostUri = $"at://{UserDid}/app.bsky.feed.post/3k2la";

    private const string Queue =
        $$$"""{"id":4,"name":"Spam","createdBy":"{{{ModDid}}}","createdAt":"2026-09-01T00:00:00.000Z","updatedAt":"2026-09-01T00:00:00.000Z","enabled":true,"stats":{}}""";

    private const string Assignment =
        $$$"""{"id":11,"did":"{{{ModDid}}}","reportId":42,"startAt":"2026-09-01T00:00:00.000Z","moderator":{"did":"{{{ModDid}}}","role":"tools.ozone.team.defs#roleModerator"}}""";

    private static readonly string Report = $$"""
        {"id":42,"eventId":7,"status":"open",
         "subject":{"type":"record","subject":"{{PostUri}}"},
         "reportType":"tools.ozone.report.defs#reasonMisleadingSpam","reportedBy":"{{UserDid}}",
         "reporter":{"type":"account","subject":"{{UserDid}}"},
         "comment":"spam","createdAt":"2026-09-01T00:00:00.000Z","actionEventIds":[9,8],
         "assignment":{"did":"{{ModDid}}","assignedAt":"2026-09-01T00:00:00.000Z"},
         "queue":{{Queue}},"isMuted":false}
        """;

    private static readonly string Activity = $$"""
        {"id":5,"reportId":42,"activity":{"$type":"tools.ozone.report.defs#closeActivity","previousStatus":"assigned"},
         "internalNote":"dupe","meta":{"assignmentId":11},"isAutomated":false,"createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z"}
        """;

    private readonly XrpcTestClient _fixture = new(instanceUrl: "https://ozone.example.com");

    private ReportClient Reports => _fixture.Client.Ozone.Report;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task GetReportAsync_SendsTheId_ReadsTheReport()
    {
        _fixture.On("tools.ozone.report.getReport", Report);

        var report = await Reports.GetReportAsync(42);

        _fixture.AssertGet("tools.ozone.report.getReport", "id=42");
        Assert.Equal(42L, report.Id);
        Assert.Equal(ReportStatus.Open, report.Status);
        Assert.Equal(ReportReasons.MisleadingSpam, report.ReportType);
        Assert.Equal(PostUri, report.Subject.Subject);
        Assert.Equal(Did.Parse(UserDid), report.ReportedBy);
        Assert.Equal([9L, 8L], report.ActionEventIds);
        Assert.Equal(Did.Parse(ModDid), report.Assignment!.Did);
        Assert.Equal("Spam", report.Queue!.Name);
    }

    [Fact]
    public async Task GetLatestReportAsync_SendsNoParameters_ReadsTheReport()
    {
        _fixture.On("tools.ozone.report.getLatestReport", $$"""{"report":{{Report}}}""");

        var result = await Reports.GetLatestReportAsync();

        _fixture.AssertGet("tools.ozone.report.getLatestReport");
        Assert.Equal(7L, result.Report.EventId);
    }

    [Fact]
    public async Task QueryReportsAsync_StatusAndFilter_GoOutAsQueryParameters()
    {
        _fixture.On("tools.ozone.report.queryReports", $$"""{"cursor":"c1","reports":[{{Report}}]}""");

        var page = await Reports.QueryReportsAsync(
            ReportStatus.Open,
            new ReportFilter
            {
                QueueId = -1,
                ReportTypes = [ReportReasons.MisleadingSpam, ReportReasons.Spam],
                Did = Did.Parse(UserDid),
                SubjectType = ReportSubjectType.Record,
                Collections = [Nsid.Parse("app.bsky.feed.post")],
                ReportedAfter = AtDatetime.Parse("2026-09-01T00:00:00.000Z"),
                IsMuted = true,
                AssignedTo = Did.Parse(ModDid),
                SortField = "updatedAt",
                SortDirection = "asc",
            },
            limit: 25,
            cursor: "c0");

        _fixture.AssertGet(
            "tools.ozone.report.queryReports",
            "queueId=-1&reportTypes=tools.ozone.report.defs#reasonMisleadingSpam&reportTypes=com.atproto.moderation.defs#reasonSpam" +
            $"&status=open&did={UserDid}&subjectType=record&collections=app.bsky.feed.post&reportedAfter=2026-09-01T00:00:00.000Z" +
            $"&isMuted=true&assignedTo={ModDid}&sortField=updatedAt&sortDirection=asc&limit=25&cursor=c0");
        Assert.Equal("c1", page.Cursor);
        Assert.Equal(42L, Assert.Single(page.Reports).Id);
    }

    [Fact]
    public async Task QueryReportsAsync_NoFilter_SendsOnlyTheStatus()
    {
        _fixture.On("tools.ozone.report.queryReports", """{"reports":[]}""");

        await Reports.QueryReportsAsync(ReportStatus.Escalated);

        _fixture.AssertGet("tools.ozone.report.queryReports", "status=escalated");
    }

    [Fact]
    public async Task CloseReportsAsync_PostsTheSubjectAndTypes_ReadsTheClosedIds()
    {
        _fixture.On("tools.ozone.report.closeReports", """{"closedCount":2,"reportIds":[42,43]}""");

        var result = await Reports.CloseReportsAsync(
            PostUri, [ReportReasons.MisleadingSpam], internalNote: "resolved upstream", isAutomated: true);

        var body = _fixture.AssertPost("tools.ozone.report.closeReports").JsonBody;
        Assert.Equal(PostUri, body.GetProperty("subject").GetString());
        Assert.Equal(ReportReasons.MisleadingSpam, body.GetProperty("reportTypes")[0].GetString());
        Assert.Equal("resolved upstream", body.GetProperty("internalNote").GetString());
        Assert.True(body.GetProperty("isAutomated").GetBoolean());
        Assert.Equal(2, result.ClosedCount);
        Assert.Equal([42L, 43L], result.ReportIds);
    }

    [Fact]
    public async Task ReassignQueueAsync_PostsReportQueueAndComment()
    {
        _fixture.On("tools.ozone.report.reassignQueue", $$"""{"report":{{Report}}}""");

        var result = await Reports.ReassignQueueAsync(42, -1, "wrong queue");

        var body = _fixture.AssertPost("tools.ozone.report.reassignQueue").JsonBody;
        Assert.Equal(42, body.GetProperty("reportId").GetInt64());
        Assert.Equal(-1, body.GetProperty("queueId").GetInt64());
        Assert.Equal("wrong queue", body.GetProperty("comment").GetString());
        Assert.Equal(42L, result.Report.Id);
    }

    [Fact]
    public async Task AssignModeratorAsync_PostsTheAssignment_ReadsTheAssignmentView()
    {
        _fixture.On("tools.ozone.report.assignModerator", Assignment);

        var assignment = await Reports.AssignModeratorAsync(42, Did.Parse(ModDid), queueId: 4, isPermanent: true);

        var body = _fixture.AssertPost("tools.ozone.report.assignModerator").JsonBody;
        Assert.Equal(42, body.GetProperty("reportId").GetInt64());
        Assert.Equal(ModDid, body.GetProperty("did").GetString());
        Assert.Equal(4, body.GetProperty("queueId").GetInt64());
        Assert.True(body.GetProperty("isPermanent").GetBoolean());
        Assert.Equal(11L, assignment.Id);
        Assert.Equal(TeamMemberRole.Moderator, assignment.Moderator!.Role);
        Assert.Null(assignment.EndAt);
    }

    [Fact]
    public async Task AssignModeratorAsync_OnlyTheReport_LeavesTheRestToTheServer()
    {
        _fixture.On("tools.ozone.report.assignModerator", Assignment);

        await Reports.AssignModeratorAsync(42);

        _fixture.AssertPost("tools.ozone.report.assignModerator", """{"reportId":42}""");
    }

    // unassignModerator is covered by ATProtoNet.Tests.Lexicon.EndpointRequestTests.

    [Fact]
    public async Task GetAssignmentsAsync_SendsEachIdAndDid()
    {
        _fixture.On("tools.ozone.report.getAssignments", $$"""{"cursor":"c1","assignments":[{{Assignment}}]}""");

        var page = await Reports.GetAssignmentsAsync([42, 43], [Did.Parse(ModDid)], onlyActive: false, limit: 5);

        _fixture.AssertGet("tools.ozone.report.getAssignments", $"onlyActive=false&reportIds=42&reportIds=43&dids={ModDid}&limit=5");
        Assert.Equal(Did.Parse(ModDid), Assert.Single(page.Assignments).Did);
    }

    [Fact]
    public async Task CreateActivityAsync_PostsTheTypedActivityForTheReport()
    {
        _fixture.On("tools.ozone.report.createActivity", $$"""{"activity":{{Activity}}}""");

        var result = await Reports.CreateActivityAsync(42, new CloseActivity(), internalNote: "dupe", publicNote: "thanks");

        var body = _fixture.AssertPost("tools.ozone.report.createActivity").JsonBody;
        Assert.Equal(42, body.GetProperty("reportId").GetInt64());
        Assert.False(body.TryGetProperty("eventId", out _));
        Assert.Equal("tools.ozone.report.defs#closeActivity", body.GetProperty("activity").GetProperty("$type").GetString());
        Assert.Equal("thanks", body.GetProperty("publicNote").GetString());

        var activity = result.Activity;
        Assert.Equal(ReportStatus.Assigned, Assert.IsType<CloseActivity>(activity.Activity).PreviousStatus);
        Assert.Equal(11, activity.Meta!.Value.GetProperty("assignmentId").GetInt32());
        Assert.Equal(Did.Parse(ModDid), activity.CreatedBy);
    }

    [Fact]
    public async Task CreateActivityForEventAsync_SendsTheEventInsteadOfTheReport()
    {
        _fixture.On("tools.ozone.report.createActivity", $$"""{"activity":{{Activity}}}""");

        await Reports.CreateActivityForEventAsync(7, new NoteActivity(), internalNote: "seen");

        var body = _fixture.AssertPost("tools.ozone.report.createActivity").JsonBody;
        Assert.Equal(7, body.GetProperty("eventId").GetInt64());
        Assert.False(body.TryGetProperty("reportId", out _));
        Assert.Equal("tools.ozone.report.defs#noteActivity", body.GetProperty("activity").GetProperty("$type").GetString());
    }

    [Fact]
    public async Task ListActivitiesAsync_SendsTheReport_ReadsTheActivities()
    {
        _fixture.On("tools.ozone.report.listActivities", $$"""{"cursor":"c1","activities":[{{Activity}}]}""");

        var page = await Reports.ListActivitiesAsync(42, limit: 10, cursor: "c0");

        _fixture.AssertGet("tools.ozone.report.listActivities", "reportId=42&limit=10&cursor=c0");
        Assert.IsType<CloseActivity>(Assert.Single(page.Activities).Activity);
    }

    [Fact]
    public async Task QueryActivitiesAsync_SendsTheFilters()
    {
        _fixture.On("tools.ozone.report.queryActivities", $$"""{"activities":[{{Activity}}]}""");

        await Reports.QueryActivitiesAsync(
            ["closeActivity", "escalationActivity"],
            createdAfter: AtDatetime.Parse("2026-09-01T00:00:00.000Z"),
            sortDirection: "asc",
            limit: 50);

        _fixture.AssertGet(
            "tools.ozone.report.queryActivities",
            "activityTypes=closeActivity&activityTypes=escalationActivity&createdAfter=2026-09-01T00:00:00.000Z&sortDirection=asc&limit=50");
    }

    [Fact]
    public async Task GetLiveStatsAsync_SendsTheFilters_ReadsTheStats()
    {
        _fixture.On("tools.ozone.report.getLiveStats", """
            {"stats":{"pendingCount":12,"actionedCount":30,"escalatedCount":2,"inboundCount":40,"actionRate":75,
                      "avgHandlingTimeSec":300,"lastUpdated":"2026-09-01T12:00:00.000Z"}}
            """);

        var result = await Reports.GetLiveStatsAsync(4, Did.Parse(ModDid), [ReportReasons.MisleadingSpam]);

        _fixture.AssertGet(
            "tools.ozone.report.getLiveStats",
            $"queueId=4&moderatorDid={ModDid}&reportTypes=tools.ozone.report.defs#reasonMisleadingSpam");
        Assert.Equal(75, result.Stats.ActionRate);
        Assert.Equal("2026-09-01T12:00:00.000Z", result.Stats.LastUpdated.ToString());
    }

    [Fact]
    public async Task GetHistoricalStatsAsync_SendsTheRange_ReadsTheDays()
    {
        _fixture.On("tools.ozone.report.getHistoricalStats", """
            {"stats":[{"date":"2026-09-01","computedAt":"2026-09-02T00:00:00.000Z","inboundCount":40}],"cursor":"c1"}
            """);

        var page = await Reports.GetHistoricalStatsAsync(
            startDate: AtDatetime.Parse("2026-08-01T00:00:00.000Z"),
            endDate: AtDatetime.Parse("2026-09-01T00:00:00.000Z"),
            limit: 30);

        _fixture.AssertGet(
            "tools.ozone.report.getHistoricalStats",
            "startDate=2026-08-01T00:00:00.000Z&endDate=2026-09-01T00:00:00.000Z&limit=30");
        var day = Assert.Single(page.Stats);
        Assert.Equal("2026-09-01", day.Date);
        Assert.Equal(40, day.InboundCount);
        Assert.Null(day.ActionRate);
    }

    // refreshStats is covered by ATProtoNet.Tests.Lexicon.EndpointRequestTests.

    [Fact]
    public void ReportActivity_UnknownType_ReadsAsUnknownActivity()
    {
        var view = JsonSerializer.Deserialize<ReportActivityView>(
            Activity.Replace("#closeActivity", "#futureActivity", StringComparison.Ordinal),
            AtProtoJsonDefaults.Options)!;

        Assert.Equal("tools.ozone.report.defs#futureActivity", Assert.IsType<UnknownReportActivity>(view.Activity).Type);
    }
}
