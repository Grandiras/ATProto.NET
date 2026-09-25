using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Ozone;

/// <summary>
/// The tools.ozone.moderation lookups, reporter statistics, scheduled actions and the event types
/// Ozone records on its own (account, identity, record, age assurance, scheduling).
/// </summary>
public sealed class OzoneModerationToolingTests : IDisposable
{
    private const string ModDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string UserDid = "did:plc:z72i7hdynmk6r22z27h6tvur";
    private const string CidText = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";
    private const string PostUri = $"at://{UserDid}/app.bsky.feed.post/3k2la";

    private const string Repo =
        $$$"""{"did":"{{{UserDid}}}","handle":"user.example.com","relatedRecords":[],"indexedAt":"2026-09-01T00:00:00.000Z","moderation":{}}""";

    private readonly OzoneTestClient _ozone = new();

    private ModerationClient Moderation => _ozone.Client.Ozone.Moderation;

    public void Dispose() => _ozone.Dispose();

    [Fact]
    public async Task GetAccountPreferencesAsync_SendsTheDid_ReadsTypedPreferences()
    {
        _ozone.Respond("""
            {"preferences":[
              {"$type":"app.bsky.actor.defs#adultContentPref","enabled":true},
              {"$type":"app.bsky.actor.defs#futurePref","x":1}
            ]}
            """);

        var result = await Moderation.GetAccountPreferencesAsync(Did.Parse(UserDid));

        Assert.Equal($"?did={UserDid}", _ozone.Sent.IsQuery("tools.ozone.moderation.getAccountPreferences").Query);
        Assert.True(Assert.IsType<AdultContentPreference>(result.Preferences[0]).Enabled);
        Assert.IsType<UnknownPreference>(result.Preferences[1]);
    }

    [Fact]
    public async Task GetReposAsync_OneDidParameterEach_ReadsDetailAndNotFound()
    {
        _ozone.Respond($$$"""
            {"repos":[
              {"$type":"tools.ozone.moderation.defs#repoViewDetail",
               "did":"{{{UserDid}}}","handle":"user.example.com","relatedRecords":[],"indexedAt":"2026-09-01T00:00:00.000Z","moderation":{}},
              {"$type":"tools.ozone.moderation.defs#repoViewNotFound","did":"{{{ModDid}}}"}
            ]}
            """);

        var result = await Moderation.GetReposAsync([Did.Parse(UserDid), Did.Parse(ModDid)]);

        Assert.Equal(
            $"?dids={UserDid}&dids={ModDid}",
            _ozone.Sent.IsQuery("tools.ozone.moderation.getRepos").Query);
        Assert.Equal(Handle.Parse("user.example.com"), Assert.IsType<RepoViewDetail>(result.Repos[0]).Handle);
        Assert.Equal(Did.Parse(ModDid), Assert.IsType<RepoViewNotFound>(result.Repos[1]).Did);
    }

    [Fact]
    public async Task GetRecordsAsync_OneUriParameterEach_ReadsDetailAndNotFound()
    {
        const string MissingUri = $"at://{UserDid}/app.bsky.feed.post/3k2lb";
        _ozone.Respond($$"""
            {"records":[
              {"$type":"tools.ozone.moderation.defs#recordViewDetail",
               "uri":"{{PostUri}}","cid":"{{CidText}}","value":{"text":"hi"},"blobs":[],
               "indexedAt":"2026-09-01T00:00:00.000Z","moderation":{},"repo":{{Repo}}},
              {"$type":"tools.ozone.moderation.defs#recordViewNotFound","uri":"{{MissingUri}}"}
            ]}
            """);

        var result = await Moderation.GetRecordsAsync([AtUri.Parse(PostUri), AtUri.Parse(MissingUri)]);

        Assert.Equal(
            $"?uris={PostUri}&uris={MissingUri}",
            _ozone.Sent.IsQuery("tools.ozone.moderation.getRecords").Query);
        Assert.Equal(Cid.Parse(CidText), Assert.IsType<RecordViewDetail>(result.Records[0]).Cid);
        Assert.Equal(AtUri.Parse(MissingUri), Assert.IsType<RecordViewNotFound>(result.Records[1]).Uri);
    }

    [Fact]
    public async Task GetSubjectsAsync_SendsEachSubject_ReadsTheSubjectViews()
    {
        _ozone.Respond($$"""
            {"subjects":[{
              "type":"account","subject":"{{UserDid}}",
              "status":{"id":3,"subject":{"$type":"com.atproto.admin.defs#repoRef","did":"{{UserDid}}"},
                        "createdAt":"2026-09-01T00:00:00.000Z","updatedAt":"2026-09-01T00:00:00.000Z",
                        "reviewState":"tools.ozone.moderation.defs#reviewOpen"},
              "repo":{{Repo}},
              "profile":{"$type":"app.bsky.actor.defs#profileViewDetailed","did":"{{UserDid}}","handle":"user.example.com"}
            }]}
            """);

        var result = await Moderation.GetSubjectsAsync([UserDid, PostUri]);

        Assert.Equal(
            $"?subjects={UserDid}&subjects={PostUri}",
            _ozone.Sent.IsQuery("tools.ozone.moderation.getSubjects").Query);
        var subject = Assert.Single(result.Subjects);
        Assert.Equal("account", subject.Type);
        Assert.Equal(SubjectReviewState.Open, subject.Status!.ReviewState);
        Assert.Equal(Did.Parse(UserDid), subject.Repo!.Did);
        Assert.Equal("user.example.com", subject.Profile!.Value.GetProperty("handle").GetString());
        Assert.Null(subject.Record);
    }

    [Fact]
    public async Task GetAccountTimelineAsync_SendsTheDid_ReadsTheDays()
    {
        _ozone.Respond("""
            {"timeline":[{"day":"2026-09-01","summary":[
              {"eventSubjectType":"account","eventType":"tools.ozone.moderation.defs#modEventTakedown","count":1},
              {"eventSubjectType":"account","eventType":"tools.ozone.hosting.getAccountHistory#handleUpdated","count":2}
            ]}]}
            """);

        var result = await Moderation.GetAccountTimelineAsync(Did.Parse(UserDid));

        Assert.Equal($"?did={UserDid}", _ozone.Sent.IsQuery("tools.ozone.moderation.getAccountTimeline").Query);
        var day = Assert.Single(result.Timeline);
        Assert.Equal("2026-09-01", day.Day);
        Assert.Equal(2, day.Summary[1].Count);
        Assert.Equal("tools.ozone.hosting.getAccountHistory#handleUpdated", day.Summary[1].EventType);
    }

    [Fact]
    public async Task GetReporterStatsAsync_SendsEachDid_ReadsTheCounts()
    {
        _ozone.Respond($$"""
            {"stats":[{"did":"{{UserDid}}","accountReportCount":1,"recordReportCount":2,"reportedAccountCount":3,
              "reportedRecordCount":4,"takendownAccountCount":5,"takendownRecordCount":6,
              "labeledAccountCount":7,"labeledRecordCount":8}]}
            """);

        var result = await Moderation.GetReporterStatsAsync([Did.Parse(UserDid), Did.Parse(ModDid)]);

        Assert.Equal(
            $"?dids={UserDid}&dids={ModDid}",
            _ozone.Sent.IsQuery("tools.ozone.moderation.getReporterStats").Query);
        var stats = Assert.Single(result.Stats);
        Assert.Equal(Did.Parse(UserDid), stats.Did);
        Assert.Equal(4, stats.ReportedRecordCount);
        Assert.Equal(8, stats.LabeledRecordCount);
    }

    [Fact]
    public async Task ScheduleActionAsync_PostsTheTypedTakedownAndItsWindow()
    {
        _ozone.Respond($$"""
            {"succeeded":["{{UserDid}}"],"failed":[{"subject":"{{ModDid}}","error":"Already scheduled","errorCode":"Conflict"}]}
            """);

        var result = await Moderation.ScheduleActionAsync(
            [Did.Parse(UserDid), Did.Parse(ModDid)],
            new ScheduledTakedown { Comment = "ban wave", StrikeCount = 1, EmailSubject = "Your account" },
            new SchedulingConfig
            {
                ExecuteAfter = AtDatetime.Parse("2026-10-01T00:00:00.000Z"),
                ExecuteUntil = AtDatetime.Parse("2026-10-02T00:00:00.000Z"),
            },
            Did.Parse(ModDid),
            new ModTool { Name = "ozone/workspace" });

        var body = _ozone.Sent.IsProcedure("tools.ozone.moderation.scheduleAction").Json;
        var action = body.GetProperty("action");
        Assert.Equal("tools.ozone.moderation.scheduleAction#takedown", action.GetProperty("$type").GetString());
        Assert.Equal("ban wave", action.GetProperty("comment").GetString());
        Assert.Equal("Your account", action.GetProperty("emailSubject").GetString());
        Assert.Equal(UserDid, body.GetProperty("subjects")[0].GetString());
        Assert.Equal(ModDid, body.GetProperty("createdBy").GetString());
        Assert.Equal("2026-10-01T00:00:00.000Z", body.GetProperty("scheduling").GetProperty("executeAfter").GetString());
        Assert.False(body.GetProperty("scheduling").TryGetProperty("executeAt", out _));
        Assert.Equal("ozone/workspace", body.GetProperty("modTool").GetProperty("name").GetString());

        Assert.Equal(Did.Parse(UserDid), Assert.Single(result.Succeeded));
        var failed = Assert.Single(result.Failed);
        Assert.Equal(Did.Parse(ModDid), failed.Subject);
        Assert.Equal("Conflict", failed.ErrorCode);
    }

    [Fact]
    public async Task ListScheduledActionsAsync_PostsTheFiltersInTheBody_ReadsTheActions()
    {
        _ozone.Respond($$"""
            {"cursor":"c1","actions":[{
              "id":9,"action":"takedown","eventData":{"comment":"ban wave"},"did":"{{UserDid}}",
              "executeAt":"2026-10-01T00:00:00.000Z","createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z",
              "status":"executed","lastExecutedAt":"2026-10-01T00:00:01.000Z","executionEventId":77}]}
            """);

        var page = await Moderation.ListScheduledActionsAsync(
            [ScheduledActionStatus.Pending, ScheduledActionStatus.Executed],
            subjects: [Did.Parse(UserDid)],
            startsAfter: AtDatetime.Parse("2026-09-01T00:00:00.000Z"),
            limit: 10,
            cursor: "c0");

        var body = _ozone.Sent.IsProcedure("tools.ozone.moderation.listScheduledActions").Json;
        Assert.Equal("pending", body.GetProperty("statuses")[0].GetString());
        Assert.Equal("executed", body.GetProperty("statuses")[1].GetString());
        Assert.Equal(UserDid, body.GetProperty("subjects")[0].GetString());
        Assert.Equal("2026-09-01T00:00:00.000Z", body.GetProperty("startsAfter").GetString());
        Assert.False(body.TryGetProperty("endsBefore", out _));
        Assert.Equal(10, body.GetProperty("limit").GetInt32());
        Assert.Equal("c0", body.GetProperty("cursor").GetString());

        Assert.Equal("c1", page.Cursor);
        var action = Assert.Single(page.Actions);
        Assert.Equal(ScheduledActionStatus.Executed, action.Status);
        Assert.Equal(77L, action.ExecutionEventId);
        Assert.Equal("ban wave", action.EventData!.Value.GetProperty("comment").GetString());
    }

    [Fact]
    public async Task CancelScheduledActionsAsync_PostsTheSubjectsAndComment()
    {
        _ozone.Respond($$"""
            {"succeeded":["{{UserDid}}"],"failed":[{"did":"{{ModDid}}","error":"Nothing pending"}]}
            """);

        var result = await Moderation.CancelScheduledActionsAsync([Did.Parse(UserDid), Did.Parse(ModDid)], "appeal granted");

        var body = _ozone.Sent.IsProcedure("tools.ozone.moderation.cancelScheduledActions").Json;
        Assert.Equal(ModDid, body.GetProperty("subjects")[1].GetString());
        Assert.Equal("appeal granted", body.GetProperty("comment").GetString());
        Assert.Equal(Did.Parse(UserDid), Assert.Single(result.Succeeded));
        Assert.Equal(Did.Parse(ModDid), Assert.Single(result.Failed).Did);
    }

    [Fact]
    public async Task EnumerateScheduledActionsAsync_SendsEachCursorInTheBody()
    {
        var action = $$"""{"id":1,"action":"takedown","did":"{{UserDid}}","createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z","status":"pending"}""";
        _ozone.Respond($$"""{"cursor":"c1","actions":[{{action}}]}""");
        _ozone.Respond($$"""{"actions":[{{action}}]}""");

        var count = 0;
        await foreach (var _ in Moderation.EnumerateScheduledActionsAsync([ScheduledActionStatus.Pending], pageSize: 1))
            count++;

        Assert.Equal(2, count);
        Assert.False(_ozone.Requests[0].Json.TryGetProperty("cursor", out _));
        Assert.Equal("c1", _ozone.Requests[1].Json.GetProperty("cursor").GetString());
        Assert.All(_ozone.Requests, r => Assert.Equal(1, r.Json.GetProperty("limit").GetInt32()));
    }

    // ─── The eleven event types added to the union ───

    public static TheoryData<Type, string> EventVariants() => new()
    {
        { typeof(ModEventResolveAppeal), """{"$type":"tools.ozone.moderation.defs#modEventResolveAppeal","comment":"upheld"}""" },
        { typeof(ModEventPriorityScore), """{"$type":"tools.ozone.moderation.defs#modEventPriorityScore","comment":"hot","score":90}""" },
        { typeof(AccountEvent), """{"$type":"tools.ozone.moderation.defs#accountEvent","active":false,"status":"takendown","timestamp":"2026-09-01T00:00:00.000Z"}""" },
        { typeof(IdentityEvent), """{"$type":"tools.ozone.moderation.defs#identityEvent","handle":"new.example.com","pdsHost":"https://pds.example.com","tombstone":false,"timestamp":"2026-09-01T00:00:00.000Z"}""" },
        { typeof(RecordEvent), $$"""{"$type":"tools.ozone.moderation.defs#recordEvent","op":"update","cid":"{{CidText}}","timestamp":"2026-09-01T00:00:00.000Z"}""" },
        { typeof(AgeAssuranceEvent), """{"$type":"tools.ozone.moderation.defs#ageAssuranceEvent","createdAt":"2026-09-01T00:00:00.000Z","attemptId":"4f7c","status":"assured","access":"full","countryCode":"GB","regionCode":"GB-ENG","initIp":"192.0.2.1","initUa":"ua","completeIp":"192.0.2.2","completeUa":"ua2"}""" },
        { typeof(AgeAssuranceOverrideEvent), """{"$type":"tools.ozone.moderation.defs#ageAssuranceOverrideEvent","status":"reset","access":"none","comment":"support ticket"}""" },
        { typeof(AgeAssurancePurgeEvent), """{"$type":"tools.ozone.moderation.defs#ageAssurancePurgeEvent","comment":"data request"}""" },
        { typeof(RevokeAccountCredentialsEvent), """{"$type":"tools.ozone.moderation.defs#revokeAccountCredentialsEvent","comment":"compromised"}""" },
        { typeof(ScheduleTakedownEvent), """{"$type":"tools.ozone.moderation.defs#scheduleTakedownEvent","comment":"wave","executeAfter":"2026-10-01T00:00:00.000Z","executeUntil":"2026-10-02T00:00:00.000Z"}""" },
        { typeof(CancelScheduledTakedownEvent), """{"$type":"tools.ozone.moderation.defs#cancelScheduledTakedownEvent","comment":"appeal"}""" },
    };

    [Theory]
    [MemberData(nameof(EventVariants))]
    public void ModEventType_NewVariant_ReadsTypedAndWritesBackTheSameFields(Type variant, string json)
    {
        var value = JsonSerializer.Deserialize<ModEventType>(json, AtProtoJsonDefaults.Options)!;

        Assert.IsType(variant, value);
        Assert.Null(value.ExtensionData);
        var written = JsonSerializer.SerializeToElement(value, AtProtoJsonDefaults.Options);
        var original = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(
            original.EnumerateObject().Select(p => (p.Name, p.Value.ToString())).OrderBy(p => p.Name),
            written.EnumerateObject().Select(p => (p.Name, p.Value.ToString())).OrderBy(p => p.Name));
    }

    [Fact]
    public async Task QueryEventsAsync_EventsOzoneRecordsItself_ReadTyped()
    {
        _ozone.Respond($$"""
            {"events":[
              {"id":1,"event":{"$type":"tools.ozone.moderation.defs#accountEvent","active":false,"status":"deactivated","timestamp":"2026-09-01T00:00:00.000Z"},
               "subject":{"$type":"com.atproto.admin.defs#repoRef","did":"{{UserDid}}"},"subjectBlobCids":[],"createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z"},
              {"id":2,"event":{"$type":"tools.ozone.moderation.defs#identityEvent","handle":"new.example.com","timestamp":"2026-09-01T00:00:00.000Z"},
               "subject":{"$type":"com.atproto.admin.defs#repoRef","did":"{{UserDid}}"},"subjectBlobCids":[],"createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z"},
              {"id":3,"event":{"$type":"tools.ozone.moderation.defs#recordEvent","op":"delete","timestamp":"2026-09-01T00:00:00.000Z"},
               "subject":{"$type":"com.atproto.repo.strongRef","uri":"{{PostUri}}","cid":"{{CidText}}"},"subjectBlobCids":[],"createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z"}
            ]}
            """);

        var page = await Moderation.QueryEventsAsync();

        var account = Assert.IsType<AccountEvent>(page.Events[0].Event);
        Assert.False(account.Active);
        Assert.Equal("deactivated", account.Status);
        Assert.Equal(Handle.Parse("new.example.com"), Assert.IsType<IdentityEvent>(page.Events[1].Event).Handle);
        var record = Assert.IsType<RecordEvent>(page.Events[2].Event);
        Assert.Equal("delete", record.Op);
        Assert.Null(record.Cid);
    }

    [Fact]
    public async Task QueryEventsAsync_MuteReporterWithoutDuration_ReadsAsAPermanentMute()
    {
        // durationInHours is optional upstream; a permanent reporter mute omits it.
        _ozone.Respond($$"""
            {"events":[{"id":4,"event":{"$type":"tools.ozone.moderation.defs#modEventMuteReporter","comment":"report spam"},
             "subject":{"$type":"com.atproto.admin.defs#repoRef","did":"{{UserDid}}"},"subjectBlobCids":[],
             "createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z"}]}
            """);

        var page = await Moderation.QueryEventsAsync();

        var mute = Assert.IsType<ModEventMuteReporter>(Assert.Single(page.Events).Event);
        Assert.Null(mute.DurationInHours);
        Assert.Equal("report spam", mute.Comment);
    }

    [Fact]
    public void ModEventReport_WithoutReportType_IsRejected()
    {
        // reportType is required upstream.
        const string Json = """{"$type":"tools.ozone.moderation.defs#modEventReport","comment":"x"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ModEventType>(Json, AtProtoJsonDefaults.Options));
    }

    [Fact]
    public void RepoViewDetail_ReadDirectly_NeedsNoTypeDiscriminator()
    {
        // getRepo returns the view itself, without the $type it carries inside getRepos.
        var repo = JsonSerializer.Deserialize<RepoViewDetail>(Repo, AtProtoJsonDefaults.Options)!;

        Assert.Equal(Did.Parse(UserDid), repo.Did);
        Assert.Null(repo.ExtensionData);
    }
}
