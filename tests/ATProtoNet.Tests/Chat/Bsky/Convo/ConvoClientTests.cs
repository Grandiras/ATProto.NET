using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Lexicon.Chat.Bsky.Actor;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
using ATProtoNet.Lexicon.Chat.Bsky.Embed;
using ATProtoNet.Lexicon.Chat.Bsky.Group;
using ATProtoNet.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Chat.Bsky.Convo;

public class ConvoClientTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;
    private readonly ConvoClient _convo;

    public ConvoClientTests()
    {
        _handler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://pds.example.com/")
        };
        _xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
        _convo = new ConvoClient(_xrpc);
    }

    [Fact]
    public async Task ListConvos_SendsCorrectRequest()
    {
        string? capturedUrl = null;
        string? capturedProxy = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            capturedProxy = request.Headers.TryGetValues("atproto-proxy", out var v)
                ? v.FirstOrDefault() : null;
            return JsonResponse(new { convos = Array.Empty<object>() });
        };

        var result = await _convo.ListConvosAsync(limit: 10, cursor: "abc");

        Assert.Contains("/xrpc/chat.bsky.convo.listConvos", capturedUrl);
        Assert.Contains("limit=10", capturedUrl!);
        Assert.Contains("cursor=abc", capturedUrl!);
        Assert.Equal(ServiceProxy.BskyChatHeader, capturedProxy);
        Assert.NotNull(result);
        Assert.Empty(result.Convos);
    }

    [Fact]
    public async Task GetConvo_SendsCorrectRequest()
    {
        string? capturedUrl = null;
        string? capturedProxy = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            capturedProxy = request.Headers.TryGetValues("atproto-proxy", out var v)
                ? v.FirstOrDefault() : null;
            return JsonResponse(new
            {
                convo = new
                {
                    id = "convo-1",
                    rev = "rev-1",
                    members = Array.Empty<object>(),
                    muted = false,
                    unreadCount = 0,
                }
            });
        };

        var result = await _convo.GetConvoAsync("convo-1");

        Assert.Contains("/xrpc/chat.bsky.convo.getConvo", capturedUrl);
        Assert.Contains("convoId=convo-1", capturedUrl!);
        Assert.Equal(ServiceProxy.BskyChatHeader, capturedProxy);
        Assert.Equal("convo-1", result.Convo.Id);
    }

    [Fact]
    public async Task SendMessage_PostsWithProxy()
    {
        string? capturedMethod = null;
        string? capturedProxy = null;
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedMethod = request.Method.Method;
            capturedProxy = request.Headers.TryGetValues("atproto-proxy", out var v)
                ? v.FirstOrDefault() : null;
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new
            {
                id = "msg-1",
                rev = "rev-1",
                text = "Hello!",
                sender = new { did = "did:plc:user1" },
                sentAt = "2024-01-01T00:00:00Z",
            });
        };

        var result = await _convo.SendMessageAsync(
            "convo-1",
            new MessageInput { Text = "Hello!" });

        Assert.Equal("POST", capturedMethod);
        Assert.Equal(ServiceProxy.BskyChatHeader, capturedProxy);
        Assert.Contains("convo-1", capturedBody!);
        Assert.Contains("Hello!", capturedBody!);
        Assert.Equal("msg-1", result.Id);
        Assert.Equal("Hello!", result.Text);
    }

    [Fact]
    public async Task MuteConvo_PostsWithProxy()
    {
        string? capturedProxy = null;
        _handler.ResponseFactory = request =>
        {
            capturedProxy = request.Headers.TryGetValues("atproto-proxy", out var v)
                ? v.FirstOrDefault() : null;
            return JsonResponse(new
            {
                convo = new
                {
                    id = "convo-1", rev = "rev-2",
                    members = Array.Empty<object>(),
                    muted = true, unreadCount = 0,
                },
            });
        };

        var result = await _convo.MuteConvoAsync("convo-1");

        Assert.Equal(ServiceProxy.BskyChatHeader, capturedProxy);
        Assert.True(result.Muted);
    }

    [Fact]
    public async Task UpdateAllRead_PostsWithProxy()
    {
        string? capturedUrl = null;
        string? capturedProxy = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            capturedProxy = request.Headers.TryGetValues("atproto-proxy", out var v)
                ? v.FirstOrDefault() : null;
            return JsonResponse(new { updatedCount = 0 });
        };

        await _convo.UpdateAllReadAsync();

        Assert.Contains("chat.bsky.convo.updateAllRead", capturedUrl);
        Assert.Equal(ServiceProxy.BskyChatHeader, capturedProxy);
    }

    [Fact]
    public async Task GetMessages_SendsCorrectRequest()
    {
        string? capturedUrl = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            return JsonResponse(new { messages = Array.Empty<object>() });
        };

        var result = await _convo.GetMessagesAsync("convo-1", limit: 25);

        Assert.Contains("convoId=convo-1", capturedUrl);
        Assert.Contains("limit=25", capturedUrl!);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetLog_SendsCorrectRequest()
    {
        string? capturedUrl = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            return JsonResponse(new { logs = Array.Empty<object>() });
        };

        var result = await _convo.GetLogAsync(cursor: "cur123");

        Assert.Contains("/xrpc/chat.bsky.convo.getLog", capturedUrl);
        Assert.Contains("cursor=cur123", capturedUrl!);
        Assert.Empty(result.Logs);
    }

    [Fact]
    public async Task AddReaction_PostsWithProxy()
    {
        string? capturedBody = null;
        string? capturedProxy = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedProxy = request.Headers.TryGetValues("atproto-proxy", out var v)
                ? v.FirstOrDefault() : null;
            return JsonResponse(new
            {
                message = new
                {
                    id = "msg-1", rev = "rev-1", text = "hi",
                    sender = new { did = "did:plc:user1" },
                    sentAt = "2024-01-01T00:00:00Z",
                },
            });
        };

        await _convo.AddReactionAsync("convo-1", "msg-1", "\u2764\uFE0F");

        Assert.Equal(ServiceProxy.BskyChatHeader, capturedProxy);
        Assert.Contains("convo-1", capturedBody!);
        Assert.Contains("msg-1", capturedBody!);
    }

    [Fact]
    public async Task ProxyHeader_DoesNotAffectOtherXrpcCalls()
    {
        // Verify that chat proxy header is per-request and doesn't leak to other calls
        string? capturedProxy = null;
        _handler.ResponseFactory = request =>
        {
            capturedProxy = request.Headers.TryGetValues("atproto-proxy", out var v)
                ? v.FirstOrDefault() : null;
            return JsonResponse(new { convos = Array.Empty<object>() });
        };

        // Chat call should have proxy
        await _convo.ListConvosAsync();
        Assert.Equal(ServiceProxy.BskyChatHeader, capturedProxy);

        // Non-chat call should NOT have proxy (no global proxy set)
        capturedProxy = "not-cleared";
        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");
        Assert.Null(capturedProxy);
    }

    [Fact]
    public async Task ListConvosAsync_FiltersThenPaging_SendsEveryParameter()
    {
        string? capturedQuery = null;
        _handler.ResponseFactory = request =>
        {
            capturedQuery = Uri.UnescapeDataString(request.RequestUri!.Query);
            return JsonResponse(new { convos = Array.Empty<object>() });
        };

        await _convo.ListConvosAsync(
            ConvoReadState.Unread, ConvoStatus.Accepted, ConvoKinds.Group, ConvoLockStatus.LockedPermanently, 10, "abc");

        Assert.Equal(
            "?readState=unread&status=accepted&kind=group&lockStatus=locked-permanently&limit=10&cursor=abc",
            capturedQuery);
    }

    [Fact]
    public async Task GetConvoForMembersAsync_TypedDids_SendsOneParameterEach()
    {
        string? capturedQuery = null;
        _handler.ResponseFactory = request =>
        {
            capturedQuery = Uri.UnescapeDataString(request.RequestUri!.Query);
            return JsonResponse(new
            {
                convo = new
                {
                    id = "convo-1",
                    rev = "rev-1",
                    members = new[] { new { did = "did:plc:user1", handle = "alice.bsky.social" } },
                    muted = false,
                    unreadCount = 0,
                },
            });
        };

        var result = await _convo.GetConvoForMembersAsync(
            [Did.Parse("did:plc:user1"), Did.Parse("did:plc:user2")]);

        Assert.Equal("?members=did:plc:user1&members=did:plc:user2", capturedQuery);
        Assert.Equal(Did.Parse("did:plc:user1"), result.Convo.Members[0].Did);
        Assert.Equal(Handle.Parse("alice.bsky.social"), result.Convo.Members[0].Handle);
    }

    [Fact]
    public async Task SendMessageBatchAsync_AnySequence_SendsEveryItem()
    {
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new { items = Array.Empty<object>() });
        };

        var items = Enumerable.Range(1, 3).Select(i => new BatchMessageItem
        {
            ConvoId = $"convo-{i}",
            Message = new MessageInput { Text = $"#{i}" },
        });
        await _convo.SendMessageBatchAsync(items);

        using var body = JsonDocument.Parse(capturedBody!);
        Assert.Equal(3, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task EnumerateConvosAsync_WalksPagesWithTheFiltersAndPageSize()
    {
        var queries = new List<string>();
        _handler.ResponseFactory = request =>
        {
            queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            return queries.Count == 1
                ? JsonResponse(new { cursor = "page-2", convos = new[] { Convo("convo-1") } })
                : JsonResponse(new { convos = new[] { Convo("convo-2") } });
        };

        var convos = await _convo.EnumerateConvosAsync(status: "request", pageSize: 1).ToListAsync();

        Assert.Equal(["convo-1", "convo-2"], convos.Select(c => c.Id));
        Assert.Equal(["?status=request&limit=1", "?status=request&limit=1&cursor=page-2"], queries);
    }

    [Fact]
    public async Task EnumerateMessagesAsync_RepeatedCursor_StopsInsteadOfLooping()
    {
        var requests = 0;
        _handler.ResponseFactory = _ =>
        {
            requests++;
            return JsonResponse(
                $$"""
                {"cursor":"same","messages":[{"$type":"chat.bsky.convo.defs#deletedMessageView",
                  "id":"msg-{{requests}}","rev":"rev-1","sender":{"did":"did:plc:user1"},"sentAt":"2026-06-01T12:00:00.000Z"}]}
                """);
        };

        var messages = await _convo.EnumerateMessagesAsync("convo-1").ToListAsync();

        Assert.Equal(2, requests);
        Assert.Equal(["msg-1", "msg-2"], messages.Select(m => Assert.IsType<DeletedMessageView>(m).Id));
    }

    [Fact]
    public async Task EnumerateLogAsync_StopsWhenTheLogHasNoNewerEntries()
    {
        var queries = new List<string>();
        _handler.ResponseFactory = request =>
        {
            queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            return queries.Count == 1
                ? JsonResponse("""{"cursor":"rev-5","logs":[{"$type":"chat.bsky.convo.defs#logBeginConvo","rev":"rev-5","convoId":"convo-1"}]}""")
                : JsonResponse(new { cursor = "rev-5", logs = Array.Empty<object>() });
        };

        var entries = await _convo.EnumerateLogAsync().ToListAsync();

        var entry = Assert.IsType<LogBeginConvo>(Assert.Single(entries));
        Assert.Equal(("rev-5", "convo-1"), (entry.Rev, entry.ConvoId));
        Assert.Equal(["", "?cursor=rev-5"], queries);
    }

    // ──────────────────────────────────────────────────────────
    //  Group-era endpoints
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUnreadCountsAsync_ExcludingGroups_SendsTheFlagAndReadsBothCounts()
    {
        string? capturedUrl = null;
        string? capturedProxy = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
            capturedProxy = request.Headers.TryGetValues("atproto-proxy", out var v) ? v.FirstOrDefault() : null;
            return JsonResponse("""{"unreadAcceptedConvos":100,"unreadRequestConvos":3}""");
        };

        var counts = await _convo.GetUnreadCountsAsync(includeGroupChats: false);

        Assert.Equal("/xrpc/chat.bsky.convo.getUnreadCounts?includeGroupChats=false", capturedUrl);
        Assert.Equal(ServiceProxy.BskyChatHeader, capturedProxy);
        Assert.Equal((100, 3), (counts.UnreadAcceptedConvos, counts.UnreadRequestConvos));
    }

    [Fact]
    public async Task ListConvoRequestsAsync_MixedRequests_ReadsConvosAndJoinRequests()
    {
        string? capturedUrl = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
            return JsonResponse(
                """
                {"cursor":"next","requests":[
                  {"$type":"chat.bsky.convo.defs#convoView","id":"convo-1","rev":"rev-1","muted":false,"unreadCount":1,"status":"request",
                   "members":[{"did":"did:plc:user1","handle":"alice.bsky.social"}],
                   "kind":{"$type":"chat.bsky.convo.defs#directConvo"}},
                  {"$type":"chat.bsky.group.defs#joinRequestConvoView","convoId":"convo-2","name":"Book club",
                   "owner":{"did":"did:plc:owner","handle":"owner.bsky.social"},"memberCount":12,"memberLimit":100,
                   "viewer":{"requestedAt":"2026-06-01T12:00:00.000Z"}},
                  {"$type":"chat.bsky.group.defs#futureRequestView","convoId":"convo-3"}]}
                """);
        };

        var page = await _convo.ListConvoRequestsAsync(limit: 3, cursor: "abc");

        Assert.Equal("/xrpc/chat.bsky.convo.listConvoRequests?limit=3&cursor=abc", capturedUrl);
        Assert.Equal("next", page.Cursor);
        Assert.Collection(page.Requests,
            r => Assert.IsType<DirectConvo>(Assert.IsType<ConvoView>(r).Kind),
            r =>
            {
                var join = Assert.IsType<JoinRequestConvoView>(r);
                Assert.Equal(("convo-2", "Book club", 12, 100), (join.ConvoId, join.Name, join.MemberCount, join.MemberLimit));
                Assert.Equal(AtDatetime.Parse("2026-06-01T12:00:00.000Z"), join.Viewer.RequestedAt);
            },
            r => Assert.Equal("chat.bsky.group.defs#futureRequestView", Assert.IsType<UnknownConvoRequestView>(r).Type));
    }

    [Fact]
    public async Task EnumerateConvoRequestsAsync_WalksPages()
    {
        var queries = new List<string>();
        _handler.ResponseFactory = request =>
        {
            queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            return queries.Count == 1
                ? JsonResponse("""{"cursor":"page-2","requests":[{"$type":"chat.bsky.convo.defs#convoView","id":"convo-1","rev":"r","members":[],"muted":false,"unreadCount":0}]}""")
                : JsonResponse("""{"requests":[{"$type":"chat.bsky.convo.defs#convoView","id":"convo-2","rev":"r","members":[],"muted":false,"unreadCount":0}]}""");
        };

        var requests = await _convo.EnumerateConvoRequestsAsync(pageSize: 1).ToListAsync();

        Assert.Equal(["convo-1", "convo-2"], requests.Select(r => Assert.IsType<ConvoView>(r).Id));
        Assert.Equal(["?limit=1", "?limit=1&cursor=page-2"], queries);
    }

    [Fact]
    public async Task GetConvoMembersAsync_GroupMembers_ReadsTheirKinds()
    {
        string? capturedUrl = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
            return JsonResponse(
                """
                {"members":[
                  {"did":"did:plc:owner","handle":"owner.bsky.social","kind":{"$type":"chat.bsky.actor.defs#groupConvoMember","role":"owner"}},
                  {"did":"did:plc:user1","handle":"alice.bsky.social","createdAt":"2024-01-01T00:00:00.000Z",
                   "kind":{"$type":"chat.bsky.actor.defs#groupConvoMember","role":"standard",
                           "addedBy":{"did":"did:plc:owner","handle":"owner.bsky.social"}}},
                  {"did":"did:plc:user2","handle":"bob.bsky.social","kind":{"$type":"chat.bsky.actor.defs#pastGroupConvoMember"}}]}
                """);
        };

        var page = await _convo.GetConvoMembersAsync("convo-1", limit: 3);

        Assert.Equal("/xrpc/chat.bsky.convo.getConvoMembers?convoId=convo-1&limit=3", capturedUrl);
        Assert.Collection(page.Members,
            m => Assert.Equal(ChatMemberRole.Owner, Assert.IsType<GroupConvoMember>(m.Kind).Role),
            m =>
            {
                var member = Assert.IsType<GroupConvoMember>(m.Kind);
                Assert.Equal(ChatMemberRole.Standard, member.Role);
                Assert.Equal(Did.Parse("did:plc:owner"), member.AddedBy!.Did);
            },
            m => Assert.IsType<PastGroupConvoMember>(m.Kind));
    }

    [Fact]
    public async Task EnumerateConvoMembersAsync_WalksPagesForTheConvo()
    {
        var queries = new List<string>();
        _handler.ResponseFactory = request =>
        {
            queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            return queries.Count == 1
                ? JsonResponse("""{"cursor":"page-2","members":[{"did":"did:plc:user1","handle":"alice.bsky.social"}]}""")
                : JsonResponse("""{"members":[{"did":"did:plc:user2","handle":"bob.bsky.social"}]}""");
        };

        var members = await _convo.EnumerateConvoMembersAsync("convo-1", pageSize: 1).ToListAsync();

        Assert.Equal(["did:plc:user1", "did:plc:user2"], members.Select(m => m.Did.ToString()));
        Assert.Equal(["?convoId=convo-1&limit=1", "?convoId=convo-1&limit=1&cursor=page-2"], queries);
    }

    [Theory]
    [InlineData("lockConvo", "locked")]
    [InlineData("unlockConvo", "unlocked")]
    public async Task LockAndUnlockConvoAsync_PostTheConvoIdAndUnwrapTheGroup(string method, string lockStatus)
    {
        string? capturedPath = null;
        string? capturedBody = null;
        string? capturedProxy = null;
        _handler.ResponseFactory = request =>
        {
            capturedPath = request.RequestUri!.AbsolutePath;
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedProxy = request.Headers.TryGetValues("atproto-proxy", out var v) ? v.FirstOrDefault() : null;
            return JsonResponse(
                """
                {"convo":{"id":"convo-1","rev":"rev-2","members":[],"muted":false,"unreadCount":0,
                  "kind":{"$type":"chat.bsky.convo.defs#groupConvo","name":"Book club","memberCount":3,"memberLimit":100,
                          "lockStatus":"LOCK_STATUS","lockStatusModerationOverride":false,"createdAt":"2026-06-01T12:00:00.000Z"}}}
                """.Replace("LOCK_STATUS", lockStatus));
        };

        var convo = method == "lockConvo"
            ? await _convo.LockConvoAsync("convo-1")
            : await _convo.UnlockConvoAsync("convo-1");

        Assert.Equal($"/xrpc/chat.bsky.convo.{method}", capturedPath);
        Assert.Equal("""{"convoId":"convo-1"}""", capturedBody);
        Assert.Equal(ServiceProxy.BskyChatHeader, capturedProxy);
        Assert.Equal(lockStatus, Assert.IsType<GroupConvo>(convo.Kind).LockStatus);
    }

    [Fact]
    public async Task SendMessageAsync_ReplyEmbedAndFacets_WritesTheLexiconShape()
    {
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(
                """
                {"id":"msg-2","rev":"rev-2","text":"join us @alice","sender":{"did":"did:plc:owner"},"sentAt":"2026-06-01T12:00:00.000Z",
                 "replyTo":{"$type":"chat.bsky.convo.defs#messageView","id":"msg-1","rev":"rev-1","text":"hi",
                            "sender":{"did":"did:plc:user1"},"sentAt":"2026-06-01T11:59:00.000Z"}}
                """);
        };

        var sent = await _convo.SendMessageAsync("convo-1", new MessageInput
        {
            Text = "join us @alice",
            Facets =
            [
                new Facet
                {
                    Index = new FacetIndex { ByteStart = 8, ByteEnd = 14 },
                    Features = [new MentionFeature { Did = Did.Parse("did:plc:user1") }],
                },
            ],
            Embed = new JoinLinkEmbed { Code = "abc123" },
            ReplyTo = new MessageReplyRef { MessageId = "msg-1" },
        });

        const string expected =
            """
            {"convoId":"convo-1","message":{"text":"join us @alice",
              "facets":[{"index":{"byteStart":8,"byteEnd":14},"features":[{"$type":"app.bsky.richtext.facet#mention","did":"did:plc:user1"}]}],
              "embed":{"$type":"chat.bsky.embed.joinLink","code":"abc123"},
              "replyTo":{"messageId":"msg-1"}}}
            """;
        Assert.True(
            JsonElement.DeepEquals(JsonDocument.Parse(expected).RootElement, JsonDocument.Parse(capturedBody!).RootElement),
            capturedBody);
        Assert.Equal("msg-1", Assert.IsType<MessageView>(sent.ReplyTo).Id);
    }

    [Fact]
    public async Task SendMessageAsync_QuotedPost_WritesARecordEmbed()
    {
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""{"id":"msg-1","rev":"rev-1","text":"look","sender":{"did":"did:plc:owner"},"sentAt":"2026-06-01T12:00:00.000Z"}""");
        };

        await _convo.SendMessageAsync("convo-1", new MessageInput
        {
            Text = "look",
            Embed = new MessageRecordEmbed
            {
                Record = new StrongRef
                {
                    Uri = AtUri.Parse("at://did:plc:user1/app.bsky.feed.post/3lq5a2kxzqc2a"),
                    Cid = Cid.Parse("bafyreigdwdwrrkpjhp5vwmrl5gvm4wasvk7lkeo3m4v37qi5wicrp2aoky"),
                },
            },
        });

        using var body = JsonDocument.Parse(capturedBody!);
        var embed = body.RootElement.GetProperty("message").GetProperty("embed");
        Assert.Equal("app.bsky.embed.record", embed.GetProperty("$type").GetString());
        Assert.Equal("at://did:plc:user1/app.bsky.feed.post/3lq5a2kxzqc2a", embed.GetProperty("record").GetProperty("uri").GetString());
    }

    private static object Convo(string id) => new
    {
        id,
        rev = "rev-1",
        members = Array.Empty<object>(),
        muted = false,
        unreadCount = 0,
    };

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static HttpResponseMessage JsonResponse(object body) => JsonResponse(JsonSerializer.Serialize(body));

    private static HttpResponseMessage JsonResponse(string json) => new()
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } =
            _ => new HttpResponseMessage { Content = new StringContent("{}") };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(ResponseFactory(request));
    }
}
