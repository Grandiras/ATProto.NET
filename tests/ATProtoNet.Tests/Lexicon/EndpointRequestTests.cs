using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Notification;
using ATProtoNet.Lexicon.Chat.Bsky.Group;
using ATProtoNet.Lexicon.Chat.Bsky.Notification;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Queue;
using ATProtoNet.Lexicon.Tools.Ozone.Report;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>
/// One row per XRPC call whose interesting behavior is the request it sends: given every
/// argument, the exact HTTP method, NSID, query string or JSON body — and that a canned response
/// deserializes. Each row gets its own <see cref="XrpcTestClient"/>, so any route or proxy it sets
/// up is private to it.
/// </summary>
/// <remarks>
/// A call whose response needs more than a light check — a union, a wrapper, bespoke client
/// logic, pagination, an error mapping, or an unusual header or encoding — keeps its own test near
/// the client instead of a row here; see <see cref="ATProtoNet.Tests.Ozone.OzoneModerationToolingTests"/>
/// and <see cref="WireFidelityTests"/> for examples. The drift tests in
/// <c>Lexicon/Upstream/LexiconDriftTests</c> check every NSID, HTTP method, JSON name and query
/// key against the upstream Lexicons across the whole SDK; this theory is about the request/response
/// shape for one call at a time, not about re-deriving what drift already covers.
/// </remarks>
public sealed class EndpointRequestTests
{
    private const string ModDid = TestIds.ModDid;

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Endpoint_SendsExpectedRequest_ReadsExpectedResponse(string name, Func<Task> scenario)
    {
        _ = name; // the row name is for the theory's own display; nothing here reads it.
        await scenario();
    }

    public static TheoryData<string, Func<Task>> Rows()
    {
        var data = new TheoryData<string, Func<Task>>();

        // Each row builds and disposes its own fixture, so its routes and any proxy it sets are
        // private to it.
        void Add(string name, Func<XrpcTestClient, Task> body) => data.Add(name, async () =>
        {
            using var fixture = new XrpcTestClient();
            await body(fixture);
        });

        // ─── chat.bsky.actor ───

        Add("chat.bsky.actor.deleteAccount", async f =>
        {
            f.On("chat.bsky.actor.deleteAccount", "{}");
            await f.Client.Chat.Actor.DeleteAccountAsync();
            f.AssertPost("chat.bsky.actor.deleteAccount", proxy: ServiceProxy.BskyChatHeader);
        });

        Add("chat.bsky.actor.getStatus", async f =>
        {
            f.On("chat.bsky.actor.getStatus", """{"chatDisabled":false,"canCreateGroups":true,"groupMemberLimit":100}""");
            var status = await f.Client.Chat.Actor.GetStatusAsync();
            f.AssertGet("chat.bsky.actor.getStatus", proxy: ServiceProxy.BskyChatHeader);
            Assert.False(status.ChatDisabled);
            Assert.True(status.CanCreateGroups);
            Assert.Equal(100, status.GroupMemberLimit);
        });

        // ─── chat.bsky.notification ───

        Add("chat.bsky.notification.getPreferences", async f =>
        {
            f.On("chat.bsky.notification.getPreferences",
                """{"preferences":{"chat":{"include":"all","push":true},"chatRequest":{"include":"follows","push":false}}}""");
            var preferences = await f.Client.Chat.Notification.GetPreferencesAsync();
            f.AssertGet("chat.bsky.notification.getPreferences", proxy: ServiceProxy.BskyChatHeader);
            Assert.True(preferences.Chat.Push);
        });

        Add("chat.bsky.notification.putPreferences", async f =>
        {
            f.On(
                "chat.bsky.notification.putPreferences",
                """{"preferences":{"chat":{"include":"all","push":true},"chatRequest":{"include":"follows","push":false}}}""");
            var preferences = await f.Client.Chat.Notification.PutPreferencesAsync(
                chatRequest: new ATProtoNet.Lexicon.Chat.Bsky.Notification.ChatPreference
                {
                    Include = ChatPreferenceInclude.Follows,
                    Push = false,
                });
            f.AssertPost(
                "chat.bsky.notification.putPreferences",
                """{"chatRequest":{"include":"follows","push":false}}""",
                ServiceProxy.BskyChatHeader);
            Assert.True(preferences.Chat.Push);
        });

        // ─── chat.bsky.group: the ack-only endpoints ───

        Add("chat.bsky.group.withdrawJoinRequest", async f =>
        {
            f.On("chat.bsky.group.withdrawJoinRequest", "{}");
            await f.Client.Chat.Group.WithdrawJoinRequestAsync("convo-1");
            f.AssertPost("chat.bsky.group.withdrawJoinRequest", """{"convoId":"convo-1"}""", ServiceProxy.BskyChatHeader);
        });

        Add("chat.bsky.group.rejectJoinRequest", async f =>
        {
            f.On("chat.bsky.group.rejectJoinRequest", "{}");
            await f.Client.Chat.Group.RejectJoinRequestAsync("convo-1", Did.Parse(ModDid));
            f.AssertPost(
                "chat.bsky.group.rejectJoinRequest", $$"""{"convoId":"convo-1","member":"{{ModDid}}"}""", ServiceProxy.BskyChatHeader);
        });

        Add("chat.bsky.group.updateJoinRequestsRead", async f =>
        {
            f.On("chat.bsky.group.updateJoinRequestsRead", "{}");
            await f.Client.Chat.Group.UpdateJoinRequestsReadAsync("convo-1");
            f.AssertPost("chat.bsky.group.updateJoinRequestsRead", """{"convoId":"convo-1"}""", ServiceProxy.BskyChatHeader);
        });

        Add("chat.bsky.convo.updateAllRead", async f =>
        {
            f.On("chat.bsky.convo.updateAllRead", HttpStub.Json(new { updatedCount = 0 }));
            await f.Client.Chat.Convo.UpdateAllReadAsync();
            f.AssertPost("chat.bsky.convo.updateAllRead", proxy: ServiceProxy.BskyChatHeader);
        });

        Add("chat.bsky.moderation.updateActorAccess", async f =>
        {
            const string ozoneProxy = "did:plc:ozone#atproto_labeler";
            f.Client.SetProxy(ozoneProxy);
            f.On("chat.bsky.moderation.updateActorAccess", "");
            await f.Client.Chat.Moderation.UpdateActorAccessAsync(Did.Parse(ModDid), allowAccess: false, reference: "ozone-event-42");
            f.AssertPost(
                "chat.bsky.moderation.updateActorAccess",
                $$"""{"actor":"{{ModDid}}","allowAccess":false,"ref":"ozone-event-42"}""",
                ozoneProxy);
        });

        // ─── app.bsky.labeler ───

        Add("app.bsky.labeler.getServices (detailed)", async f =>
        {
            f.On("app.bsky.labeler.getServices", """{"views":[]}""");
            var result = await f.Client.Bsky.Labeler.GetServicesAsync(
                [Did.Parse("did:plc:labeler1"), Did.Parse("did:plc:labeler2")], detailed: true);
            f.AssertGet("app.bsky.labeler.getServices", "dids=did:plc:labeler1&dids=did:plc:labeler2&detailed=true");
            Assert.Empty(result.Views);
        });

        Add("app.bsky.labeler.getServices (no detailed)", async f =>
        {
            f.On("app.bsky.labeler.getServices", """{"views":[]}""");
            await f.Client.Bsky.Labeler.GetServicesAsync([Did.Parse("did:plc:labeler1")]);
            Assert.DoesNotContain("detailed", Assert.Single(f.Requests).Query);
        });

        // ─── com.atproto.admin / com.atproto.temp ───

        Add("com.atproto.admin.searchAccounts", async f =>
        {
            f.On("com.atproto.admin.searchAccounts", $$"""
                {"cursor":"c2","accounts":[{"did":"{{ModDid}}","handle":"alice.test","email":"a@example.com","indexedAt":"2026-01-01T00:00:00.000Z"}]}
                """);
            var page = await f.Client.Admin.SearchAccountsAsync(email: "a@example.com", limit: 10, cursor: "c1");
            f.AssertGet("com.atproto.admin.searchAccounts", "email=a@example.com&limit=10&cursor=c1");
            Assert.Equal("c2", page.Cursor);
            Assert.Equal(Did.Parse(ModDid), Assert.Single(page.Accounts).Did);
        });

        Add("com.atproto.admin.updateAccountSigningKey", async f =>
        {
            const string keyText = "did:key:zQ3shTbmthbWnaziXpJifLJvXvGwVyW3zeWetTatAXUtA1b2S";
            f.On("com.atproto.admin.updateAccountSigningKey", "{}");
            await f.Client.Admin.UpdateAccountSigningKeyAsync(Did.Parse(ModDid), Did.Parse(keyText));
            var body = f.AssertPost("com.atproto.admin.updateAccountSigningKey").JsonBody;
            Assert.Equal(ModDid, body.GetProperty("did").GetString());
            Assert.Equal(keyText, body.GetProperty("signingKey").GetString());
        });

        Add("com.atproto.temp.checkSignupQueue", async f =>
        {
            f.On("com.atproto.temp.checkSignupQueue", """{"activated":false,"placeInQueue":12,"estimatedTimeMs":3600000}""");
            var queue = await f.Client.Temp.CheckSignupQueueAsync();
            f.AssertGet("com.atproto.temp.checkSignupQueue");
            Assert.Equal(12, queue.PlaceInQueue);
        });

        Add("com.atproto.temp.dereferenceScope", async f =>
        {
            f.On("com.atproto.temp.dereferenceScope", """{"scope":"atproto repo:app.bsky.feed.post?action=create"}""");
            var scope = await f.Client.Temp.DereferenceScopeAsync("ref:abc123");
            f.AssertGet("com.atproto.temp.dereferenceScope", "scope=ref:abc123");
            Assert.Equal("atproto repo:app.bsky.feed.post?action=create", scope);
        });

        Add("com.atproto.temp.requestPhoneVerification", async f =>
        {
            f.On("com.atproto.temp.requestPhoneVerification", "{}");
            await f.Client.Temp.RequestPhoneVerificationAsync("+15555550123");
            f.AssertPost("com.atproto.temp.requestPhoneVerification", """{"phoneNumber":"+15555550123"}""");
        });

        Add("com.atproto.temp.revokeAccountCredentials", async f =>
        {
            f.On("com.atproto.temp.revokeAccountCredentials", "{}");
            await f.Client.Temp.RevokeAccountCredentialsAsync(Did.Parse(ModDid));
            f.AssertPost("com.atproto.temp.revokeAccountCredentials", $$"""{"account":"{{ModDid}}"}""");
        });

        // ─── tools.ozone.queue ───

        Add("tools.ozone.queue.unassignModerator", async f =>
        {
            f.On("tools.ozone.queue.unassignModerator", "{}");
            await f.Client.Ozone.Queue.UnassignModeratorAsync(4, Did.Parse(ModDid));
            f.AssertPost("tools.ozone.queue.unassignModerator", $$"""{"queueId":4,"did":"{{ModDid}}"}""");
        });

        Add("tools.ozone.queue.routeReports", async f =>
        {
            f.On("tools.ozone.queue.routeReports", """{"assigned":40,"unmatched":2}""");
            var result = await f.Client.Ozone.Queue.RouteReportsAsync(1000, 1999);
            f.AssertPost("tools.ozone.queue.routeReports", """{"startReportId":1000,"endReportId":1999}""");
            Assert.Equal(40, result.Assigned);
        });

        Add("tools.ozone.queue.deleteQueue", async f =>
        {
            f.On("tools.ozone.queue.deleteQueue", """{"deleted":true,"reportsMigrated":12}""");
            var result = await f.Client.Ozone.Queue.DeleteQueueAsync(4, migrateToQueueId: 5);
            f.AssertPost("tools.ozone.queue.deleteQueue", """{"queueId":4,"migrateToQueueId":5}""");
            Assert.True(result.Deleted);
        });

        // ─── tools.ozone.report ───

        Add("tools.ozone.report.unassignModerator", async f =>
        {
            f.On("tools.ozone.report.unassignModerator", $$"""{"id":11,"did":"{{ModDid}}","reportId":42,"startAt":"2026-09-01T00:00:00.000Z"}""");
            var assignment = await f.Client.Ozone.Report.UnassignModeratorAsync(42);
            f.AssertPost("tools.ozone.report.unassignModerator", """{"reportId":42}""");
            Assert.Equal(42L, assignment.ReportId);
        });

        Add("tools.ozone.report.refreshStats", async f =>
        {
            f.On("tools.ozone.report.refreshStats", "{}");
            await f.Client.Ozone.Report.RefreshStatsAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), [4, -1]);
            f.AssertPost("tools.ozone.report.refreshStats", """{"startDate":"2026-08-01","endDate":"2026-08-31","queueIds":[4,-1]}""");
        });

        // ─── tools.ozone.moderation: plain (non-union) lookups, continued ───

        Add("tools.ozone.moderation.getEvent", async f =>
        {
            f.On("tools.ozone.moderation.getEvent", $$"""
                {"id":42,"event":{"$type":"tools.ozone.moderation.defs#modEventComment","comment":"test"},
                 "subject":{"$type":"com.atproto.admin.defs#repoRef","did":"{{ModDid}}"},"createdBy":"{{ModDid}}","createdAt":"2024-01-01T00:00:00Z"}
                """);
            var result = await f.Client.Ozone.Moderation.GetEventAsync(42);
            f.AssertGet("tools.ozone.moderation.getEvent", "id=42");
            Assert.Equal(42L, result.Id);
        });

        Add("tools.ozone.moderation.getRepo", async f =>
        {
            f.On("tools.ozone.moderation.getRepo", HttpStub.Json(new
            {
                did = "did:plc:xyz",
                handle = "user.bsky.social",
                indexedAt = "2024-01-01T00:00:00Z",
                moderation = new { },
            }));
            var result = await f.Client.Ozone.Moderation.GetRepoAsync(Did.Parse("did:plc:xyz"));
            f.AssertGet("tools.ozone.moderation.getRepo", "did=did:plc:xyz");
            Assert.Equal("did:plc:xyz", result.Did);
        });

        Add("tools.ozone.moderation.queryEvents (plain)", async f =>
        {
            f.On("tools.ozone.moderation.queryEvents", HttpStub.Json(new { events = Array.Empty<object>() }));
            await f.Client.Ozone.Moderation.QueryEventsAsync(subject: "did:plc:abc", limit: 10, sortDirection: "desc");
            f.AssertGet("tools.ozone.moderation.queryEvents", "subject=did:plc:abc&sortDirection=desc&limit=10");
        });

        // ─── tools.ozone.communication ───

        Add("tools.ozone.communication.createTemplate", async f =>
        {
            f.On("tools.ozone.communication.createTemplate", HttpStub.Json(new
            {
                id = "tmpl-1",
                name = "Warning",
                contentMarkdown = "You violated...",
                disabled = false,
                lastUpdatedBy = ModDid,
                createdAt = "2024-01-01T00:00:00Z",
                updatedAt = "2024-01-01T00:00:00Z",
            }));
            var result = await f.Client.Ozone.Communication.CreateTemplateAsync(new ATProtoNet.Lexicon.Tools.Ozone.Communication.CreateTemplateRequest
            {
                Name = "Warning",
                ContentMarkdown = "You violated...",
                Subject = "Policy Warning",
            });
            f.AssertPost("tools.ozone.communication.createTemplate");
            Assert.Equal("tmpl-1", result.Id);
        });

        Add("tools.ozone.communication.listTemplates", async f =>
        {
            f.On("tools.ozone.communication.listTemplates", HttpStub.Json(new
            {
                communicationTemplates = new[]
                {
                    new
                    {
                        id = "t1",
                        name = "Template1",
                        contentMarkdown = "...",
                        disabled = false,
                        lastUpdatedBy = ModDid,
                        createdAt = "2024-01-01T00:00:00Z",
                        updatedAt = "2024-01-01T00:00:00Z",
                    },
                },
            }));
            var result = await f.Client.Ozone.Communication.ListTemplatesAsync();
            f.AssertGet("tools.ozone.communication.listTemplates");
            Assert.Single(result.CommunicationTemplates);
        });

        // ─── tools.ozone.team ───

        Add("tools.ozone.team.addMember", async f =>
        {
            f.On("tools.ozone.team.addMember", HttpStub.Json(new { did = "did:plc:newmod", role = "tools.ozone.team.defs#roleModerator" }));
            var result = await f.Client.Ozone.Team.AddMemberAsync(
                new ATProtoNet.Lexicon.Tools.Ozone.Team.AddMemberRequest
                {
                    Did = Did.Parse("did:plc:newmod"),
                    Role = ATProtoNet.Lexicon.Tools.Ozone.Team.TeamMemberRole.Moderator,
                });
            f.AssertPost("tools.ozone.team.addMember");
            Assert.Equal(ATProtoNet.Lexicon.Tools.Ozone.Team.TeamMemberRole.Moderator, result.Role);
        });

        Add("tools.ozone.team.listMembers", async f =>
        {
            f.On("tools.ozone.team.listMembers", HttpStub.Json(new
            {
                members = new[] { new { did = "did:plc:admin", role = "tools.ozone.team.defs#roleAdmin" } },
                cursor = "next-page",
            }));
            var result = await f.Client.Ozone.Team.ListMembersAsync(limit: 25);
            f.AssertGet("tools.ozone.team.listMembers", "limit=25");
            Assert.Equal("next-page", result.Cursor);
        });

        // ─── tools.ozone.set ───

        Add("tools.ozone.set.upsertSet", async f =>
        {
            f.On("tools.ozone.set.upsertSet", HttpStub.Json(new { name = "bad-words", setSize = 0, createdAt = "2024-01-01T00:00:00Z", updatedAt = "2024-01-01T00:00:00Z" }));
            var result = await f.Client.Ozone.Set.UpsertSetAsync(
                new ATProtoNet.Lexicon.Tools.Ozone.Set.UpsertSetRequest { Name = "bad-words", Description = "Known bad words" });
            f.AssertPost("tools.ozone.set.upsertSet");
            Assert.Equal("bad-words", result.Name);
        });

        Add("tools.ozone.set.getValues", async f =>
        {
            f.On("tools.ozone.set.getValues", HttpStub.Json(new
            {
                set = new { name = "bad-words", setSize = 2, createdAt = "2024-01-01T00:00:00Z", updatedAt = "2024-01-01T00:00:00Z" },
                values = new[] { "word1", "word2" },
            }));
            var result = await f.Client.Ozone.Set.GetValuesAsync("bad-words");
            f.AssertGet("tools.ozone.set.getValues", "name=bad-words");
            Assert.Equal(2, result.Values.Count);
        });

        // ─── tools.ozone.server ───

        Add("tools.ozone.server.getConfig", async f =>
        {
            f.On("tools.ozone.server.getConfig", HttpStub.Json(new
            {
                appview = new { url = "https://api.bsky.app" },
                pds = new { url = "https://pds.example.com" },
                viewer = new { role = "tools.ozone.team.defs#roleAdmin" },
            }));
            var result = await f.Client.Ozone.Server.GetConfigAsync();
            f.AssertGet("tools.ozone.server.getConfig");
            Assert.Equal("https://api.bsky.app", result.Appview?.Url);
        });

        // ─── tools.ozone.signature ───

        Add("tools.ozone.signature.findRelatedAccounts", async f =>
        {
            f.On("tools.ozone.signature.findRelatedAccounts", HttpStub.Json(new { accounts = Array.Empty<object>() }));
            await f.Client.Ozone.Signature.FindRelatedAccountsAsync(Did.Parse(ModDid), 10, "c");
            f.AssertGet("tools.ozone.signature.findRelatedAccounts", $"did={ModDid}&cursor=c&limit=10");
        });

        // ─── tools.ozone.setting / .signature / .safelink ───

        Add("tools.ozone.setting.removeOptions", async f =>
        {
            f.On("tools.ozone.setting.removeOptions", "{}");
            await f.Client.Ozone.Setting.RemoveOptionsAsync(
                [Nsid.Parse("tools.ozone.setting.client.queues")], ATProtoNet.Lexicon.Tools.Ozone.Setting.SettingScope.Personal);
            f.AssertPost(
                "tools.ozone.setting.removeOptions",
                """{"keys":["tools.ozone.setting.client.queues"],"scope":"personal"}""");
        });

        Add("tools.ozone.safelink.removeRule", async f =>
        {
            var eventJson = $$"""
                {"id":3,"eventType":"removeRule","url":"scam.example.com","pattern":"domain","action":"block","reason":"phishing",
                 "createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z"}
                """;
            f.On("tools.ozone.safelink.removeRule", eventJson);
            await f.Client.Ozone.Safelink.RemoveRuleAsync(
                "scam.example.com", ATProtoNet.Lexicon.Tools.Ozone.Safelink.SafelinkPatternType.Domain, "false positive");
            f.AssertPost(
                "tools.ozone.safelink.removeRule",
                """{"url":"scam.example.com","pattern":"domain","comment":"false positive"}""");
        });

        // ─── tools.ozone.moderation: plain (non-union) lookups ───

        Add("tools.ozone.moderation.getAccountTimeline", async f =>
        {
            f.On("tools.ozone.moderation.getAccountTimeline", """
                {"timeline":[{"day":"2026-09-01","summary":[
                  {"eventSubjectType":"account","eventType":"tools.ozone.moderation.defs#modEventTakedown","count":1}]}]}
                """);
            var result = await f.Client.Ozone.Moderation.GetAccountTimelineAsync(Did.Parse(ModDid));
            f.AssertGet("tools.ozone.moderation.getAccountTimeline", $"did={ModDid}");
            Assert.Equal("2026-09-01", Assert.Single(result.Timeline).Day);
        });

        Add("tools.ozone.moderation.getReporterStats", async f =>
        {
            f.On("tools.ozone.moderation.getReporterStats", $$"""
                {"stats":[{"did":"{{ModDid}}","accountReportCount":1,"recordReportCount":2,"reportedAccountCount":3,
                  "reportedRecordCount":4,"takendownAccountCount":5,"takendownRecordCount":6,
                  "labeledAccountCount":7,"labeledRecordCount":8}]}
                """);
            var result = await f.Client.Ozone.Moderation.GetReporterStatsAsync([Did.Parse(ModDid)]);
            f.AssertGet("tools.ozone.moderation.getReporterStats", $"dids={ModDid}");
            Assert.Equal(8, Assert.Single(result.Stats).LabeledRecordCount);
        });

        // ─── app.bsky.bookmark ───

        Add("app.bsky.bookmark.createBookmark", async f =>
        {
            const string postUri = $"at://{ModDid}/app.bsky.feed.post/3k2la";
            const string postCid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";
            f.On("app.bsky.bookmark.createBookmark", "{}");
            await f.Client.Bsky.Bookmark.CreateBookmarkAsync(AtUri.Parse(postUri), Cid.Parse(postCid));
            f.AssertPost("app.bsky.bookmark.createBookmark", $$"""{"uri":"{{postUri}}","cid":"{{postCid}}"}""");
        });

        Add("app.bsky.bookmark.deleteBookmark", async f =>
        {
            const string postUri = $"at://{ModDid}/app.bsky.feed.post/3k2la";
            f.On("app.bsky.bookmark.deleteBookmark", "{}");
            await f.Client.Bsky.Bookmark.DeleteBookmarkAsync(AtUri.Parse(postUri));
            f.AssertPost("app.bsky.bookmark.deleteBookmark", $$"""{"uri":"{{postUri}}"}""");
        });

        // ─── app.bsky.notification ───

        Add("app.bsky.notification.unregisterPush", async f =>
        {
            f.On("app.bsky.notification.unregisterPush", "{}");
            await f.Client.Bsky.Notification.UnregisterPushAsync(
                Did.Parse("did:web:api.bsky.app"), "device-token", PushPlatform.Ios, "xyz.blueskyweb.app");
            f.AssertPost(
                "app.bsky.notification.unregisterPush",
                """{"serviceDid":"did:web:api.bsky.app","token":"device-token","platform":"ios","appId":"xyz.blueskyweb.app"}""");
        });

        Add("app.bsky.notification.listActivitySubscriptions", async f =>
        {
            f.On(
                "app.bsky.notification.listActivitySubscriptions",
                $$"""{"cursor":"n","subscriptions":[{"did":"{{ModDid}}","handle":"mod.test"}]}""");
            var page = await f.Client.Bsky.Notification.ListActivitySubscriptionsAsync(limit: 5);
            f.AssertGet("app.bsky.notification.listActivitySubscriptions", "limit=5");
            Assert.Equal("n", page.Cursor);
            Assert.Equal(Did.Parse(ModDid), Assert.Single(page.Subscriptions).Did);
        });

        Add("app.bsky.notification.putActivitySubscription", async f =>
        {
            f.On(
                "app.bsky.notification.putActivitySubscription",
                $$$"""{"subject":"{{{ModDid}}}","activitySubscription":{"post":true,"reply":false}}""");
            var stored = await f.Client.Bsky.Notification.PutActivitySubscriptionAsync(Did.Parse(ModDid), post: true, reply: false);
            f.AssertPost(
                "app.bsky.notification.putActivitySubscription",
                $$$"""{"subject":"{{{ModDid}}}","activitySubscription":{"post":true,"reply":false}}""");
            Assert.True(stored.ActivitySubscription!.Post);
        });

        // ─── app.bsky.unspecced / app.bsky.embed: the "nothing resolved" defaults ───

        Add("app.bsky.unspecced.getPostThreadV2 (defaults)", async f =>
        {
            const string postUri = $"at://{ModDid}/app.bsky.feed.post/3lwinfmsd2k2a";
            f.On("app.bsky.unspecced.getPostThreadV2", """{"thread":[],"hasOtherReplies":false}""");
            await f.Client.Bsky.Unspecced.GetPostThreadV2Async(AtUri.Parse(postUri));
            f.AssertGet("app.bsky.unspecced.getPostThreadV2", $"anchor={postUri}");
        });

        Add("app.bsky.embed.getEmbedExternalView (nothing resolved)", async f =>
        {
            const string documentUri = $"at://{ModDid}/site.standard.document/3lwinfmsd2k2i";
            f.On("app.bsky.embed.getEmbedExternalView", "{}");
            var response = await f.Client.Bsky.Embed.GetEmbedExternalViewAsync("https://example.com", [AtUri.Parse(documentUri)]);
            f.AssertGet("app.bsky.embed.getEmbedExternalView", $"url=https://example.com&uris={documentUri}");
            Assert.Null(response.View);
        });

        // ─── app.bsky.notification.updateSeen / app.bsky.graph.muteActor ───

        Add("app.bsky.notification.updateSeen", async f =>
        {
            f.On("app.bsky.notification.updateSeen", "{}");
            await f.Client.Bsky.Notification.UpdateSeenAsync(AtDatetime.Parse("2024-05-01T12:00:00Z"));
            Assert.Equal("2024-05-01T12:00:00Z", f.AssertPost("app.bsky.notification.updateSeen").JsonBody.GetProperty("seenAt").GetString());
        });

        Add("app.bsky.graph.muteActor", async f =>
        {
            f.On("app.bsky.graph.muteActor", "{}");
            await f.Client.Bsky.Graph.MuteActorAsync(Handle.Parse("bob.test"));
            Assert.Equal("bob.test", f.AssertPost("app.bsky.graph.muteActor").JsonBody.GetProperty("actor").GetString());
        });

        // ─── app.bsky.draft ───

        Add("app.bsky.draft.updateDraft", async f =>
        {
            f.On("app.bsky.draft.updateDraft", "{}");
            await f.Client.Bsky.Draft.UpdateDraftAsync(
                Tid.Parse("3lwinfmsd2k2h"),
                new ATProtoNet.Lexicon.App.Bsky.Draft.Draft { Posts = [new ATProtoNet.Lexicon.App.Bsky.Draft.DraftPost { Text = "edited" }] });
            f.AssertPost(
                "app.bsky.draft.updateDraft",
                """{"draft":{"id":"3lwinfmsd2k2h","draft":{"posts":[{"text":"edited"}]}}}""");
        });

        Add("app.bsky.draft.deleteDraft", async f =>
        {
            f.On("app.bsky.draft.deleteDraft", "{}");
            await f.Client.Bsky.Draft.DeleteDraftAsync(Tid.Parse("3lwinfmsd2k2h"));
            f.AssertPost("app.bsky.draft.deleteDraft", """{"id":"3lwinfmsd2k2h"}""");
        });

        // ─── app.bsky.ageassurance.begin ───

        Add("app.bsky.ageassurance.begin", async f =>
        {
            f.On(
                "app.bsky.ageassurance.begin",
                """{"lastInitiatedAt":"2026-09-25T10:00:00.000Z","status":"pending","access":"unknown"}""");
            var state = await f.Client.Bsky.AgeAssurance.BeginAsync("alice@example.com", "en", "GB");
            f.AssertPost("app.bsky.ageassurance.begin", """{"email":"alice@example.com","language":"en","countryCode":"GB"}""");
            Assert.Equal(
                ATProtoNet.Lexicon.App.Bsky.AgeAssurance.AgeAssuranceStatus.Pending, state.Status);
        });

        // ─── site.standard: the generic repo-CRUD delete calls ───
        // (StandardSiteClient wraps com.atproto.repo.deleteRecord for each collection.)

        Add("site.standard.publication (delete)", async f =>
        {
            f.On("com.atproto.repo.deleteRecord", HttpStub.Json(new { commit = new { cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm", rev = "3jzfcijpj2z2a" } }));
            await f.Client.Site.DeletePublicationAsync(Did.Parse("did:plc:test"), RecordKey.Parse("abc"));
            Assert.Contains("site.standard.publication", f.Last.BodyText);
        });

        Add("site.standard.document (delete)", async f =>
        {
            f.On("com.atproto.repo.deleteRecord", HttpStub.Json(new { commit = new { cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm", rev = "3jzfcijpj2z2a" } }));
            await f.Client.Site.DeleteDocumentAsync(Did.Parse("did:plc:test"), RecordKey.Parse("doc1"));
            Assert.Contains("site.standard.document", f.Last.BodyText);
        });

        Add("site.standard.graph.subscription (delete)", async f =>
        {
            f.On("com.atproto.repo.deleteRecord", HttpStub.Json(new { commit = new { cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm", rev = "3jzfcijpj2z2a" } }));
            await f.Client.Site.DeleteSubscriptionAsync(Did.Parse("did:plc:sub"), RecordKey.Parse("s1"));
            Assert.Contains("site.standard.graph.subscription", f.Last.BodyText);
        });

        Add("site.standard.graph.recommend (delete)", async f =>
        {
            f.On("com.atproto.repo.deleteRecord", HttpStub.Json(new { commit = new { cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm", rev = "3jzfcijpj2z2a" } }));
            await f.Client.Site.DeleteRecommendationAsync(Did.Parse("did:plc:fan"), RecordKey.Parse("r1"));
            var body = f.Last.JsonBody;
            Assert.Equal("site.standard.graph.recommend", body.GetProperty("collection").GetString());
            Assert.Equal("r1", body.GetProperty("rkey").GetString());
        });

        return data;
    }
}
