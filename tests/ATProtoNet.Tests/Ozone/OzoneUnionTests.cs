using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Hosting;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Report;
using ATProtoNet.Serialization;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Ozone;

/// <summary>
/// The tools.ozone unions, one representative call per union, read from a real response (and
/// written, where the client sends one). The plain request/response calls are rows in
/// <see cref="ATProtoNet.Tests.Lexicon.EndpointRequestTests"/>.
/// </summary>
public sealed class OzoneUnionTests : IDisposable
{
    private const string ModDid = TestIds.ModDid;
    private const string UserDid = "did:plc:z72i7hdynmk6r22z27h6tvur";
    private const string CidText = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";

    private const string Repo =
        $$$"""{"did":"{{{UserDid}}}","handle":"user.example.com","relatedRecords":[],"indexedAt":"2026-09-01T00:00:00.000Z","moderation":{}}""";

    private readonly XrpcTestClient _fixture = new(instanceUrl: "https://ozone.example.com");

    private AtProtoClient Client => _fixture.Client;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task QueryEventsAsync_ParsesTheTypedEventView()
    {
        _fixture.On("tools.ozone.moderation.queryEvents", $$"""
            {"events":[{
              "id":7,
              "event":{"$type":"tools.ozone.moderation.defs#modEventComment","comment":"hi"},
              "subject":{"$type":"com.atproto.repo.strongRef","uri":"at://{{ModDid}}/app.bsky.feed.post/3k2la","cid":"{{CidText}}"},
              "subjectBlobCids":["{{CidText}}"],
              "createdBy":"{{ModDid}}",
              "createdAt":"2024-01-01T00:00:00Z",
              "creatorHandle":"mod.example.com"
            }]}
            """);

        var page = await Client.Ozone.Moderation.QueryEventsAsync();

        var view = Assert.Single(page.Events);
        Assert.Equal("hi", Assert.IsType<ModEventComment>(view.Event).Comment);
        var subject = Assert.IsType<RecordSubject>(view.Subject);
        Assert.Equal(RecordKey.Parse("3k2la"), subject.Uri.RecordKey);
        Assert.Equal(Cid.Parse(CidText), Assert.Single(view.SubjectBlobCids!));
        Assert.Equal(Did.Parse(ModDid), view.CreatedBy);
        Assert.Equal("2024-01-01T00:00:00Z", view.CreatedAt.ToString());
        Assert.Equal(Handle.Parse("mod.example.com"), view.CreatorHandle);
    }

    [Fact]
    public async Task GetReposAsync_ReadsDetailAndNotFound()
    {
        _fixture.On("tools.ozone.moderation.getRepos", $$$"""
            {"repos":[
              {"$type":"tools.ozone.moderation.defs#repoViewDetail",
               "did":"{{{UserDid}}}","handle":"user.example.com","relatedRecords":[],"indexedAt":"2026-09-01T00:00:00.000Z","moderation":{}},
              {"$type":"tools.ozone.moderation.defs#repoViewNotFound","did":"{{{ModDid}}}"}
            ]}
            """);

        var result = await Client.Ozone.Moderation.GetReposAsync([Did.Parse(UserDid), Did.Parse(ModDid)]);

        _fixture.AssertGet("tools.ozone.moderation.getRepos", $"dids={UserDid}&dids={ModDid}");
        Assert.Equal(Handle.Parse("user.example.com"), Assert.IsType<RepoViewDetail>(result.Repos[0]).Handle);
        Assert.Equal(Did.Parse(ModDid), Assert.IsType<RepoViewNotFound>(result.Repos[1]).Did);
    }

    [Fact]
    public void RepoViewDetail_ReadDirectly_NeedsNoTypeDiscriminator()
    {
        // getRepo returns the view itself, without the $type it carries inside getRepos.
        var repo = JsonSerializer.Deserialize<RepoViewDetail>(Repo, AtProtoJsonDefaults.Options)!;

        Assert.Equal(Did.Parse(UserDid), repo.Did);
        Assert.Null(repo.ExtensionData);
    }

    public static TheoryData<Type, string> EventVariants() => new()
    {
        { typeof(ModEventResolveAppeal), """{"$type":"tools.ozone.moderation.defs#modEventResolveAppeal","comment":"upheld"}""" },
        { typeof(ModEventPriorityScore), """{"$type":"tools.ozone.moderation.defs#modEventPriorityScore","comment":"hot","score":90}""" },
        { typeof(AccountEvent), """{"$type":"tools.ozone.moderation.defs#accountEvent","active":false,"status":"takendown","timestamp":"2026-09-01T00:00:00.000Z"}""" },
        { typeof(IdentityEvent), """{"$type":"tools.ozone.moderation.defs#identityEvent","handle":"new.example.com","pdsHost":"https://pds.example.com","tombstone":false,"timestamp":"2026-09-01T00:00:00.000Z"}""" },
        { typeof(RecordEvent), $$"""{"$type":"tools.ozone.moderation.defs#recordEvent","op":"update","cid":"{{CidText}}","timestamp":"2026-09-01T00:00:00.000Z"}""" },
        { typeof(RecordEvent), """{"$type":"tools.ozone.moderation.defs#recordEvent","op":"delete","timestamp":"2026-09-01T00:00:00.000Z"}""" },
        { typeof(AgeAssuranceEvent), """{"$type":"tools.ozone.moderation.defs#ageAssuranceEvent","createdAt":"2026-09-01T00:00:00.000Z","attemptId":"4f7c","status":"assured","access":"full","countryCode":"GB","regionCode":"GB-ENG","initIp":"192.0.2.1","initUa":"ua","completeIp":"192.0.2.2","completeUa":"ua2"}""" },
        { typeof(AgeAssuranceOverrideEvent), """{"$type":"tools.ozone.moderation.defs#ageAssuranceOverrideEvent","status":"reset","access":"none","comment":"support ticket"}""" },
        { typeof(AgeAssurancePurgeEvent), """{"$type":"tools.ozone.moderation.defs#ageAssurancePurgeEvent","comment":"data request"}""" },
        { typeof(RevokeAccountCredentialsEvent), """{"$type":"tools.ozone.moderation.defs#revokeAccountCredentialsEvent","comment":"compromised"}""" },
        { typeof(ScheduleTakedownEvent), """{"$type":"tools.ozone.moderation.defs#scheduleTakedownEvent","comment":"wave","executeAfter":"2026-10-01T00:00:00.000Z","executeUntil":"2026-10-02T00:00:00.000Z"}""" },
        { typeof(CancelScheduledTakedownEvent), """{"$type":"tools.ozone.moderation.defs#cancelScheduledTakedownEvent","comment":"appeal"}""" },
        // Regression: durationInHours is optional upstream; a permanent reporter mute omits it.
        { typeof(ModEventMuteReporter), """{"$type":"tools.ozone.moderation.defs#modEventMuteReporter","comment":"report spam"}""" },
    };

    [Theory]
    [MemberData(nameof(EventVariants))]
    public void ModEventType_Variant_ReadsTypedAndWritesBackTheSameFields(Type variant, string json)
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
    public void ModEventReport_WithoutReportType_IsRejected()
    {
        // reportType is required upstream.
        const string Json = """{"$type":"tools.ozone.moderation.defs#modEventReport","comment":"x"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ModEventType>(Json, AtProtoJsonDefaults.Options));
    }

    [Fact]
    public async Task CreateActivityAsync_WritesAndReadsTheTypedActivity()
    {
        _fixture.On("tools.ozone.report.createActivity", $$$"""
            {"activity":{"id":5,"reportId":42,"activity":{"$type":"tools.ozone.report.defs#closeActivity","previousStatus":"assigned"},
             "internalNote":"dupe","meta":{"assignmentId":11},"isAutomated":false,"createdBy":"{{{ModDid}}}","createdAt":"2026-09-01T00:00:00.000Z"}}
            """);

        var result = await Client.Ozone.Report.CreateActivityAsync(42, new CloseActivity(), internalNote: "dupe", publicNote: "thanks");

        _fixture.AssertPost(
            "tools.ozone.report.createActivity",
            """{"reportId":42,"activity":{"$type":"tools.ozone.report.defs#closeActivity"},"internalNote":"dupe","publicNote":"thanks"}""");
        var activity = result.Activity;
        Assert.Equal(ReportStatus.Assigned, Assert.IsType<CloseActivity>(activity.Activity).PreviousStatus);
        Assert.Equal(11, activity.Meta!.Value.GetProperty("assignmentId").GetInt32());
    }

    [Fact]
    public async Task GetAccountHistoryAsync_ReadsTypedDetails()
    {
        _fixture.On("tools.ozone.hosting.getAccountHistory", """
            {"cursor":"c1","events":[
              {"details":{"$type":"tools.ozone.hosting.getAccountHistory#accountCreated","email":"a@example.com","handle":"user.example.com"},
               "createdBy":"user","createdAt":"2026-09-01T00:00:00.000Z"},
              {"details":{"$type":"tools.ozone.hosting.getAccountHistory#handleUpdated","handle":"new.example.com"},
               "createdBy":"user","createdAt":"2026-09-02T00:00:00.000Z"},
              {"details":{"$type":"tools.ozone.hosting.getAccountHistory#passwordUpdated"},
               "createdBy":"admin","createdAt":"2026-09-03T00:00:00.000Z"}
            ]}
            """);

        var page = await Client.Ozone.Hosting.GetAccountHistoryAsync(
            Did.Parse(UserDid), [AccountHistoryEventType.AccountCreated, AccountHistoryEventType.HandleUpdated], limit: 10, cursor: "c0");

        _fixture.AssertGet(
            "tools.ozone.hosting.getAccountHistory",
            $"did={UserDid}&events=accountCreated&events=handleUpdated&cursor=c0&limit=10");
        Assert.Equal("a@example.com", Assert.IsType<AccountCreated>(page.Events[0].Details).Email);
        Assert.Equal(Handle.Parse("new.example.com"), Assert.IsType<HandleUpdated>(page.Events[1].Details).Handle);
        Assert.IsType<PasswordUpdated>(page.Events[2].Details);
    }
}
