using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Report;
using ATProtoNet.Lexicon.Tools.Ozone.Team;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Ozone;

/// <summary>
/// tools.ozone.report: one test per endpoint, checking the method, what goes on the wire and that
/// the typed response reads back.
/// </summary>
public sealed class OzoneReportClientTests : IDisposable
{
    private const string ModDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
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

    private readonly OzoneTestClient _ozone = new();

    private ReportClient Reports => _ozone.Client.Ozone.Report;

    public void Dispose() => _ozone.Dispose();

    [Fact]
    public async Task GetReportAsync_SendsTheId_ReadsTheReport()
    {
        _ozone.Respond(Report);

        var report = await Reports.GetReportAsync(42);

        Assert.Equal("?id=42", _ozone.Sent.IsQuery("tools.ozone.report.getReport").Query);
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
        _ozone.Respond($$"""{"report":{{Report}}}""");

        var result = await Reports.GetLatestReportAsync();

        Assert.Equal("", _ozone.Sent.IsQuery("tools.ozone.report.getLatestReport").Query);
        Assert.Equal(7L, result.Report.EventId);
    }

    [Fact]
    public async Task QueryReportsAsync_StatusAndFilter_GoOutAsQueryParameters()
    {
        _ozone.Respond($$"""{"cursor":"c1","reports":[{{Report}}]}""");

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

        Assert.Equal(
            "?queueId=-1&reportTypes=tools.ozone.report.defs#reasonMisleadingSpam&reportTypes=com.atproto.moderation.defs#reasonSpam" +
            $"&status=open&did={UserDid}&subjectType=record&collections=app.bsky.feed.post&reportedAfter=2026-09-01T00:00:00.000Z" +
            $"&isMuted=true&assignedTo={ModDid}&sortField=updatedAt&sortDirection=asc&limit=25&cursor=c0",
            _ozone.Sent.IsQuery("tools.ozone.report.queryReports").Query);
        Assert.Equal("c1", page.Cursor);
        Assert.Equal(42L, Assert.Single(page.Reports).Id);
    }

    [Fact]
    public async Task QueryReportsAsync_NoFilter_SendsOnlyTheStatus()
    {
        _ozone.Respond("""{"reports":[]}""");

        await Reports.QueryReportsAsync(ReportStatus.Escalated);

        Assert.Equal("?status=escalated", _ozone.Sent.Query);
    }

    [Fact]
    public async Task CloseReportsAsync_PostsTheSubjectAndTypes_ReadsTheClosedIds()
    {
        _ozone.Respond("""{"closedCount":2,"reportIds":[42,43]}""");

        var result = await Reports.CloseReportsAsync(
            PostUri, [ReportReasons.MisleadingSpam], internalNote: "resolved upstream", isAutomated: true);

        var body = _ozone.Sent.IsProcedure("tools.ozone.report.closeReports").Json;
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
        _ozone.Respond($$"""{"report":{{Report}}}""");

        var result = await Reports.ReassignQueueAsync(42, -1, "wrong queue");

        var body = _ozone.Sent.IsProcedure("tools.ozone.report.reassignQueue").Json;
        Assert.Equal(42, body.GetProperty("reportId").GetInt64());
        Assert.Equal(-1, body.GetProperty("queueId").GetInt64());
        Assert.Equal("wrong queue", body.GetProperty("comment").GetString());
        Assert.Equal(42L, result.Report.Id);
    }

    [Fact]
    public async Task AssignModeratorAsync_PostsTheAssignment_ReadsTheAssignmentView()
    {
        _ozone.Respond(Assignment);

        var assignment = await Reports.AssignModeratorAsync(42, Did.Parse(ModDid), queueId: 4, isPermanent: true);

        var body = _ozone.Sent.IsProcedure("tools.ozone.report.assignModerator").Json;
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
        _ozone.Respond(Assignment);

        await Reports.AssignModeratorAsync(42);

        Assert.Equal("""{"reportId":42}""", _ozone.Sent.Body);
    }

    [Fact]
    public async Task UnassignModeratorAsync_PostsTheReport()
    {
        _ozone.Respond(Assignment);

        var assignment = await Reports.UnassignModeratorAsync(42);

        Assert.Equal("""{"reportId":42}""", _ozone.Sent.IsProcedure("tools.ozone.report.unassignModerator").Body);
        Assert.Equal(42L, assignment.ReportId);
    }

    [Fact]
    public async Task GetAssignmentsAsync_SendsEachIdAndDid()
    {
        _ozone.Respond($$"""{"cursor":"c1","assignments":[{{Assignment}}]}""");

        var page = await Reports.GetAssignmentsAsync([42, 43], [Did.Parse(ModDid)], onlyActive: false, limit: 5);

        Assert.Equal(
            $"?onlyActive=false&reportIds=42&reportIds=43&dids={ModDid}&limit=5",
            _ozone.Sent.IsQuery("tools.ozone.report.getAssignments").Query);
        Assert.Equal(Did.Parse(ModDid), Assert.Single(page.Assignments).Did);
    }

    [Fact]
    public async Task CreateActivityAsync_PostsTheTypedActivityForTheReport()
    {
        _ozone.Respond($$"""{"activity":{{Activity}}}""");

        var result = await Reports.CreateActivityAsync(42, new CloseActivity(), internalNote: "dupe", publicNote: "thanks");

        var body = _ozone.Sent.IsProcedure("tools.ozone.report.createActivity").Json;
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
        _ozone.Respond($$"""{"activity":{{Activity}}}""");

        await Reports.CreateActivityForEventAsync(7, new NoteActivity(), internalNote: "seen");

        var body = _ozone.Sent.IsProcedure("tools.ozone.report.createActivity").Json;
        Assert.Equal(7, body.GetProperty("eventId").GetInt64());
        Assert.False(body.TryGetProperty("reportId", out _));
        Assert.Equal("tools.ozone.report.defs#noteActivity", body.GetProperty("activity").GetProperty("$type").GetString());
    }

    [Fact]
    public async Task ListActivitiesAsync_SendsTheReport_ReadsTheActivities()
    {
        _ozone.Respond($$"""{"cursor":"c1","activities":[{{Activity}}]}""");

        var page = await Reports.ListActivitiesAsync(42, limit: 10, cursor: "c0");

        Assert.Equal("?reportId=42&limit=10&cursor=c0", _ozone.Sent.IsQuery("tools.ozone.report.listActivities").Query);
        Assert.IsType<CloseActivity>(Assert.Single(page.Activities).Activity);
    }

    [Fact]
    public async Task QueryActivitiesAsync_SendsTheFilters()
    {
        _ozone.Respond($$"""{"activities":[{{Activity}}]}""");

        await Reports.QueryActivitiesAsync(
            ["closeActivity", "escalationActivity"],
            createdAfter: AtDatetime.Parse("2026-09-01T00:00:00.000Z"),
            sortDirection: "asc",
            limit: 50);

        Assert.Equal(
            "?activityTypes=closeActivity&activityTypes=escalationActivity&createdAfter=2026-09-01T00:00:00.000Z&sortDirection=asc&limit=50",
            _ozone.Sent.IsQuery("tools.ozone.report.queryActivities").Query);
    }

    [Fact]
    public async Task GetLiveStatsAsync_SendsTheFilters_ReadsTheStats()
    {
        _ozone.Respond("""
            {"stats":{"pendingCount":12,"actionedCount":30,"escalatedCount":2,"inboundCount":40,"actionRate":75,
                      "avgHandlingTimeSec":300,"lastUpdated":"2026-09-01T12:00:00.000Z"}}
            """);

        var result = await Reports.GetLiveStatsAsync(4, Did.Parse(ModDid), [ReportReasons.MisleadingSpam]);

        Assert.Equal(
            $"?queueId=4&moderatorDid={ModDid}&reportTypes=tools.ozone.report.defs#reasonMisleadingSpam",
            _ozone.Sent.IsQuery("tools.ozone.report.getLiveStats").Query);
        Assert.Equal(75, result.Stats.ActionRate);
        Assert.Equal("2026-09-01T12:00:00.000Z", result.Stats.LastUpdated.ToString());
    }

    [Fact]
    public async Task GetHistoricalStatsAsync_SendsTheRange_ReadsTheDays()
    {
        _ozone.Respond("""
            {"stats":[{"date":"2026-09-01","computedAt":"2026-09-02T00:00:00.000Z","inboundCount":40}],"cursor":"c1"}
            """);

        var page = await Reports.GetHistoricalStatsAsync(
            startDate: AtDatetime.Parse("2026-08-01T00:00:00.000Z"),
            endDate: AtDatetime.Parse("2026-09-01T00:00:00.000Z"),
            limit: 30);

        Assert.Equal(
            "?startDate=2026-08-01T00:00:00.000Z&endDate=2026-09-01T00:00:00.000Z&limit=30",
            _ozone.Sent.IsQuery("tools.ozone.report.getHistoricalStats").Query);
        var day = Assert.Single(page.Stats);
        Assert.Equal("2026-09-01", day.Date);
        Assert.Equal(40, day.InboundCount);
        Assert.Null(day.ActionRate);
    }

    [Fact]
    public async Task RefreshStatsAsync_PostsCalendarDates()
    {
        _ozone.Respond("{}");

        await Reports.RefreshStatsAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), [4, -1]);

        Assert.Equal(
            """{"startDate":"2026-08-01","endDate":"2026-08-31","queueIds":[4,-1]}""",
            _ozone.Sent.IsProcedure("tools.ozone.report.refreshStats").Body);
    }

    [Fact]
    public void ReportActivity_UnknownType_ReadsAsUnknownActivity()
    {
        var view = JsonSerializer.Deserialize<ReportActivityView>(
            Activity.Replace("#closeActivity", "#futureActivity", StringComparison.Ordinal),
            AtProtoJsonDefaults.Options)!;

        Assert.Equal("tools.ozone.report.defs#futureActivity", Assert.IsType<UnknownReportActivity>(view.Activity).Type);
    }

    public static TheoryData<string> Enumerators => ["reports", "assignments", "activities", "reportActivities", "historicalStats"];

    [Theory]
    [MemberData(nameof(Enumerators))]
    public async Task Enumerate_WalksPagesWithPageSize(string listing)
    {
        var (nsid, list, item, enumerate) = listing switch
        {
            "reports" => ("tools.ozone.report.queryReports", "reports", Report,
                Count(Reports.EnumerateReportsAsync(ReportStatus.Open, pageSize: 2))),
            "assignments" => ("tools.ozone.report.getAssignments", "assignments", Assignment,
                Count(Reports.EnumerateAssignmentsAsync(pageSize: 2))),
            "activities" => ("tools.ozone.report.queryActivities", "activities", Activity,
                Count(Reports.EnumerateActivitiesAsync(pageSize: 2))),
            "reportActivities" => ("tools.ozone.report.listActivities", "activities", Activity,
                Count(Reports.EnumerateReportActivitiesAsync(42, pageSize: 2))),
            "historicalStats" => ("tools.ozone.report.getHistoricalStats", "stats", """{"date":"2026-09-01"}""",
                Count(Reports.EnumerateHistoricalStatsAsync(pageSize: 2))),
            _ => throw new ArgumentOutOfRangeException(nameof(listing)),
        };
        _ozone.Respond($$"""{"cursor":"c1","{{list}}":[{{item}}]}""");
        _ozone.Respond($$"""{"{{list}}":[{{item}}]}""");

        Assert.Equal(2, await enumerate());

        Assert.All(_ozone.Requests, r => Assert.Equal(nsid, r.Nsid));
        Assert.All(_ozone.Requests, r => Assert.Contains("limit=2", r.Query));
        Assert.DoesNotContain("cursor=", _ozone.Requests[0].Query);
        Assert.Contains("cursor=c1", _ozone.Requests[1].Query);
    }

    private static Func<Task<int>> Count<T>(IAsyncEnumerable<T> items) => async () =>
    {
        var count = 0;
        await foreach (var _ in items)
            count++;
        return count;
    };
}
