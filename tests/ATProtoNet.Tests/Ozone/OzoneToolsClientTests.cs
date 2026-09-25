using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Tools.Ozone.Hosting;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Safelink;
using ATProtoNet.Lexicon.Tools.Ozone.Setting;
using ATProtoNet.Lexicon.Tools.Ozone.Team;
using ATProtoNet.Lexicon.Tools.Ozone.Verification;

namespace ATProtoNet.Tests.Ozone;

/// <summary>
/// tools.ozone.safelink, .setting, .verification and .hosting: one test per endpoint, checking the
/// method, what goes on the wire and that the typed response reads back.
/// </summary>
public sealed class OzoneToolsClientTests : IDisposable
{
    private const string ModDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string UserDid = "did:plc:z72i7hdynmk6r22z27h6tvur";
    private const string VerificationUri = $"at://{ModDid}/app.bsky.graph.verification/3lndpmyzszx2b";

    private const string SafelinkEventJson = $$"""
        {"id":3,"eventType":"addRule","url":"scam.example.com","pattern":"domain","action":"block","reason":"phishing",
         "createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z","comment":"reported"}
        """;

    private const string OptionJson = $$"""
        {"key":"tools.ozone.setting.client.queues","did":"{{ModDid}}","value":{"order":["spam"]},
         "managerRole":"tools.ozone.team.defs#roleAdmin","scope":"instance","createdBy":"{{ModDid}}","lastUpdatedBy":"{{ModDid}}"}
        """;

    private const string VerificationJson = $$$"""
        {"issuer":"{{{ModDid}}}","uri":"{{{VerificationUri}}}","subject":"{{{UserDid}}}","handle":"user.example.com",
         "displayName":"User","createdAt":"2026-09-01T00:00:00.000Z",
         "subjectRepo":{"$type":"tools.ozone.moderation.defs#repoViewNotFound","did":"{{{UserDid}}}"}}
        """;

    private readonly OzoneTestClient _ozone = new();

    public void Dispose() => _ozone.Dispose();

    // ─── Safelink ───

    [Fact]
    public async Task AddRuleAsync_PostsTheRule_ReadsTheAuditEvent()
    {
        _ozone.Respond(SafelinkEventJson);

        var result = await _ozone.Client.Ozone.Safelink.AddRuleAsync(
            "scam.example.com", SafelinkPatternType.Domain, SafelinkActionType.Block, SafelinkReasonType.Phishing,
            comment: "reported");

        Assert.Equal(
            """{"url":"scam.example.com","pattern":"domain","action":"block","reason":"phishing","comment":"reported"}""",
            _ozone.Sent.IsProcedure("tools.ozone.safelink.addRule").Body);
        Assert.Equal(SafelinkEventType.AddRule, result.EventType);
        Assert.Equal(Did.Parse(ModDid), result.CreatedBy);
    }

    [Fact]
    public async Task UpdateRuleAsync_PostsTheRule()
    {
        _ozone.Respond(SafelinkEventJson.Replace("addRule", "updateRule", StringComparison.Ordinal));

        var result = await _ozone.Client.Ozone.Safelink.UpdateRuleAsync(
            "https://scam.example.com/x", SafelinkPatternType.Url, SafelinkActionType.Warn, SafelinkReasonType.Spam,
            createdBy: Did.Parse(ModDid));

        Assert.Equal(
            $$"""{"url":"https://scam.example.com/x","pattern":"url","action":"warn","reason":"spam","createdBy":"{{ModDid}}"}""",
            _ozone.Sent.IsProcedure("tools.ozone.safelink.updateRule").Body);
        Assert.Equal(SafelinkEventType.UpdateRule, result.EventType);
    }

    [Fact]
    public async Task RemoveRuleAsync_PostsUrlAndPattern()
    {
        _ozone.Respond(SafelinkEventJson.Replace("addRule", "removeRule", StringComparison.Ordinal));

        await _ozone.Client.Ozone.Safelink.RemoveRuleAsync("scam.example.com", SafelinkPatternType.Domain, "false positive");

        Assert.Equal(
            """{"url":"scam.example.com","pattern":"domain","comment":"false positive"}""",
            _ozone.Sent.IsProcedure("tools.ozone.safelink.removeRule").Body);
    }

    [Fact]
    public async Task QueryRulesAsync_PostsTheFilters_ReadsTheRules()
    {
        _ozone.Respond($$"""
            {"cursor":"c1","rules":[{"url":"scam.example.com","pattern":"domain","action":"block","reason":"phishing",
              "createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z","updatedAt":"2026-09-02T00:00:00.000Z"}]}
            """);

        var page = await _ozone.Client.Ozone.Safelink.QueryRulesAsync(
            urls: ["scam.example.com"],
            actions: [SafelinkActionType.Block, SafelinkActionType.Warn],
            createdBy: Did.Parse(ModDid),
            limit: 10,
            cursor: "c0");

        var body = _ozone.Sent.IsProcedure("tools.ozone.safelink.queryRules").Json;
        Assert.Equal("scam.example.com", body.GetProperty("urls")[0].GetString());
        Assert.Equal("warn", body.GetProperty("actions")[1].GetString());
        Assert.Equal(ModDid, body.GetProperty("createdBy").GetString());
        Assert.Equal("c0", body.GetProperty("cursor").GetString());
        Assert.False(body.TryGetProperty("reason", out _));
        var rule = Assert.Single(page.Rules);
        Assert.Equal(SafelinkReasonType.Phishing, rule.Reason);
        Assert.Equal("2026-09-02T00:00:00.000Z", rule.UpdatedAt.ToString());
    }

    [Fact]
    public async Task QueryEventsAsync_PostsTheFilters_ReadsTheEvents()
    {
        _ozone.Respond($$"""{"events":[{{SafelinkEventJson}}]}""");

        var page = await _ozone.Client.Ozone.Safelink.QueryEventsAsync(
            patternType: SafelinkPatternType.Domain, sortDirection: "asc", limit: 5);

        Assert.Equal(
            """{"limit":5,"patternType":"domain","sortDirection":"asc"}""",
            _ozone.Sent.IsProcedure("tools.ozone.safelink.queryEvents").Body);
        Assert.Equal(3L, Assert.Single(page.Events).Id);
    }

    [Theory]
    [InlineData("rules")]
    [InlineData("events")]
    public async Task SafelinkEnumerate_SendsEachCursorInTheBody(string listing)
    {
        var item = listing == "events"
            ? SafelinkEventJson
            : $$"""{"url":"u","pattern":"url","action":"warn","reason":"none","createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z","updatedAt":"2026-09-01T00:00:00.000Z"}""";
        _ozone.Respond($$"""{"cursor":"c1","{{listing}}":[{{item}}]}""");
        _ozone.Respond($$"""{"{{listing}}":[{{item}}]}""");

        var count = 0;
        if (listing == "events")
        {
            await foreach (var _ in _ozone.Client.Ozone.Safelink.EnumerateEventsAsync(pageSize: 2))
                count++;
        }
        else
        {
            await foreach (var _ in _ozone.Client.Ozone.Safelink.EnumerateRulesAsync(pageSize: 2))
                count++;
        }

        Assert.Equal(2, count);
        Assert.False(_ozone.Requests[0].Json.TryGetProperty("cursor", out _));
        Assert.Equal("c1", _ozone.Requests[1].Json.GetProperty("cursor").GetString());
        Assert.All(_ozone.Requests, r => Assert.Equal(2, r.Json.GetProperty("limit").GetInt32()));
    }

    // ─── Setting ───

    [Fact]
    public async Task ListOptionsAsync_SendsTheFilters_ReadsTheOptions()
    {
        _ozone.Respond($$"""{"cursor":"c1","options":[{{OptionJson}}]}""");

        var page = await _ozone.Client.Ozone.Setting.ListOptionsAsync(
            SettingScope.Personal,
            keys: [Nsid.Parse("tools.ozone.setting.client.queues"), Nsid.Parse("tools.ozone.setting.client.tags")],
            limit: 10);

        Assert.Equal(
            "?limit=10&scope=personal&keys=tools.ozone.setting.client.queues&keys=tools.ozone.setting.client.tags",
            _ozone.Sent.IsQuery("tools.ozone.setting.listOptions").Query);
        var option = Assert.Single(page.Options);
        Assert.Equal(Nsid.Parse("tools.ozone.setting.client.queues"), option.Key);
        Assert.Equal("spam", option.Value.GetProperty("order")[0].GetString());
        Assert.Equal(TeamMemberRole.Admin, option.ManagerRole);
        Assert.Equal(SettingScope.Instance, option.Scope);
    }

    [Fact]
    public async Task UpsertOptionAsync_PostsTheValueAsIs()
    {
        _ozone.Respond($$"""{"option":{{OptionJson}}}""");

        var result = await _ozone.Client.Ozone.Setting.UpsertOptionAsync(
            Nsid.Parse("tools.ozone.setting.client.queues"),
            SettingScope.Instance,
            JsonSerializer.SerializeToElement(new { order = new[] { "spam" } }),
            description: "Queue order",
            managerRole: TeamMemberRole.Admin);

        Assert.Equal(
            """{"key":"tools.ozone.setting.client.queues","scope":"instance","value":{"order":["spam"]},"description":"Queue order","managerRole":"tools.ozone.team.defs#roleAdmin"}""",
            _ozone.Sent.IsProcedure("tools.ozone.setting.upsertOption").Body);
        Assert.Equal(Did.Parse(ModDid), result.Option.LastUpdatedBy);
    }

    [Fact]
    public async Task RemoveOptionsAsync_PostsKeysAndScope()
    {
        _ozone.Respond("{}");

        await _ozone.Client.Ozone.Setting.RemoveOptionsAsync(
            [Nsid.Parse("tools.ozone.setting.client.queues")], SettingScope.Personal);

        Assert.Equal(
            """{"keys":["tools.ozone.setting.client.queues"],"scope":"personal"}""",
            _ozone.Sent.IsProcedure("tools.ozone.setting.removeOptions").Body);
    }

    [Fact]
    public async Task EnumerateOptionsAsync_WalksPagesWithPageSize()
    {
        _ozone.Respond($$"""{"cursor":"c1","options":[{{OptionJson}}]}""");
        _ozone.Respond($$"""{"options":[{{OptionJson}}]}""");

        var count = 0;
        await foreach (var _ in _ozone.Client.Ozone.Setting.EnumerateOptionsAsync(prefix: "tools.ozone.setting.client", pageSize: 2))
            count++;

        Assert.Equal(2, count);
        Assert.All(_ozone.Requests, r => Assert.Contains("prefix=tools.ozone.setting.client", r.Query));
        Assert.Contains("cursor=c1", _ozone.Requests[1].Query);
    }

    // ─── Verification ───

    [Fact]
    public async Task GrantVerificationsAsync_PostsTheInputs_ReadsCreatedAndFailed()
    {
        _ozone.Respond($$"""
            {"verifications":[{{VerificationJson}}],"failedVerifications":[{"error":"Handle mismatch","subject":"{{ModDid}}"}]}
            """);

        var result = await _ozone.Client.Ozone.Verification.GrantVerificationsAsync(
        [
            new VerificationInput { Subject = Did.Parse(UserDid), Handle = Handle.Parse("user.example.com"), DisplayName = "User" },
            new VerificationInput
            {
                Subject = Did.Parse(ModDid),
                Handle = Handle.Parse("mod.example.com"),
                DisplayName = "Mod",
                CreatedAt = AtDatetime.Parse("2026-09-01T00:00:00.000Z"),
            },
        ]);

        var body = _ozone.Sent.IsProcedure("tools.ozone.verification.grantVerifications").Json;
        var first = body.GetProperty("verifications")[0];
        Assert.Equal(UserDid, first.GetProperty("subject").GetString());
        Assert.Equal("user.example.com", first.GetProperty("handle").GetString());
        Assert.False(first.TryGetProperty("createdAt", out _));
        Assert.Equal("2026-09-01T00:00:00.000Z", body.GetProperty("verifications")[1].GetProperty("createdAt").GetString());

        var verification = Assert.Single(result.Verifications);
        Assert.Equal(AtUri.Parse(VerificationUri), verification.Uri);
        Assert.Equal(Did.Parse(UserDid), Assert.IsType<RepoViewNotFound>(verification.SubjectRepo).Did);
        Assert.Null(verification.IssuerRepo);
        Assert.Equal("Handle mismatch", Assert.Single(result.FailedVerifications).Error);
    }

    [Fact]
    public async Task RevokeVerificationsAsync_PostsUrisAndReason()
    {
        _ozone.Respond($$"""{"revokedVerifications":["{{VerificationUri}}"],"failedRevocations":[]}""");

        var result = await _ozone.Client.Ozone.Verification.RevokeVerificationsAsync(
            [AtUri.Parse(VerificationUri)], "handle changed");

        Assert.Equal(
            $$"""{"uris":["{{VerificationUri}}"],"revokeReason":"handle changed"}""",
            _ozone.Sent.IsProcedure("tools.ozone.verification.revokeVerifications").Body);
        Assert.Equal(AtUri.Parse(VerificationUri), Assert.Single(result.RevokedVerifications));
        Assert.Empty(result.FailedRevocations);
    }

    [Fact]
    public async Task ListVerificationsAsync_SendsTheFilters_ReadsTheVerifications()
    {
        _ozone.Respond($$"""
            {"verifications":[{"issuer":"{{ModDid}}","uri":"{{VerificationUri}}","subject":"{{UserDid}}",
              "handle":"user.example.com","displayName":"User","createdAt":"2026-09-01T00:00:00.000Z",
              "revokeReason":"x","revokedAt":"2026-09-02T00:00:00.000Z","revokedBy":"{{ModDid}}"}]}
            """);

        var page = await _ozone.Client.Ozone.Verification.ListVerificationsAsync(
            subjects: [Did.Parse(UserDid)],
            issuers: [Did.Parse(ModDid)],
            createdAfter: AtDatetime.Parse("2026-08-01T00:00:00.000Z"),
            isRevoked: true,
            limit: 10);

        Assert.Equal(
            $"?limit=10&createdAfter=2026-08-01T00:00:00.000Z&issuers={ModDid}&subjects={UserDid}&isRevoked=true",
            _ozone.Sent.IsQuery("tools.ozone.verification.listVerifications").Query);
        var verification = Assert.Single(page.Verifications);
        Assert.Equal(Did.Parse(ModDid), verification.RevokedBy);
        Assert.Equal("x", verification.RevokeReason);
    }

    [Fact]
    public async Task EnumerateVerificationsAsync_WalksPagesWithPageSize()
    {
        _ozone.Respond($$"""{"cursor":"c1","verifications":[{{VerificationJson}}]}""");
        _ozone.Respond($$"""{"verifications":[{{VerificationJson}}]}""");

        var count = 0;
        await foreach (var _ in _ozone.Client.Ozone.Verification.EnumerateVerificationsAsync(pageSize: 2))
            count++;

        Assert.Equal(2, count);
        Assert.All(_ozone.Requests, r => Assert.Contains("limit=2", r.Query));
        Assert.Contains("cursor=c1", _ozone.Requests[1].Query);
    }

    // ─── Hosting ───

    [Fact]
    public async Task GetAccountHistoryAsync_SendsDidAndEventKinds_ReadsTypedDetails()
    {
        _ozone.Respond("""
            {"cursor":"c1","events":[
              {"details":{"$type":"tools.ozone.hosting.getAccountHistory#accountCreated","email":"a@example.com","handle":"user.example.com"},
               "createdBy":"user","createdAt":"2026-09-01T00:00:00.000Z"},
              {"details":{"$type":"tools.ozone.hosting.getAccountHistory#handleUpdated","handle":"new.example.com"},
               "createdBy":"user","createdAt":"2026-09-02T00:00:00.000Z"},
              {"details":{"$type":"tools.ozone.hosting.getAccountHistory#passwordUpdated"},
               "createdBy":"admin","createdAt":"2026-09-03T00:00:00.000Z"},
              {"details":{"$type":"tools.ozone.hosting.getAccountHistory#futureChange","x":1},
               "createdBy":"admin","createdAt":"2026-09-04T00:00:00.000Z"}
            ]}
            """);

        var page = await _ozone.Client.Ozone.Hosting.GetAccountHistoryAsync(
            Did.Parse(UserDid),
            [AccountHistoryEventType.AccountCreated, AccountHistoryEventType.HandleUpdated],
            limit: 10,
            cursor: "c0");

        Assert.Equal(
            $"?did={UserDid}&events=accountCreated&events=handleUpdated&cursor=c0&limit=10",
            _ozone.Sent.IsQuery("tools.ozone.hosting.getAccountHistory").Query);
        Assert.Equal("a@example.com", Assert.IsType<AccountCreated>(page.Events[0].Details).Email);
        Assert.Equal(Handle.Parse("new.example.com"), Assert.IsType<HandleUpdated>(page.Events[1].Details).Handle);
        Assert.IsType<PasswordUpdated>(page.Events[2].Details);
        Assert.IsType<UnknownAccountHistoryDetails>(page.Events[3].Details);
        Assert.Equal("admin", page.Events[3].CreatedBy);
    }

    [Fact]
    public async Task EnumerateAccountHistoryAsync_WalksPagesWithPageSize()
    {
        const string Event = """{"details":{"$type":"tools.ozone.hosting.getAccountHistory#passwordUpdated"},"createdBy":"user","createdAt":"2026-09-01T00:00:00.000Z"}""";
        _ozone.Respond($$"""{"cursor":"c1","events":[{{Event}}]}""");
        _ozone.Respond($$"""{"events":[{{Event}}]}""");

        var count = 0;
        await foreach (var _ in _ozone.Client.Ozone.Hosting.EnumerateAccountHistoryAsync(Did.Parse(UserDid), pageSize: 2))
            count++;

        Assert.Equal(2, count);
        Assert.All(_ozone.Requests, r => Assert.Contains($"did={UserDid}", r.Query));
        Assert.All(_ozone.Requests, r => Assert.Contains("limit=2", r.Query));
        Assert.Contains("cursor=c1", _ozone.Requests[1].Query);
    }
}
