using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Lexicon.Chat.Bsky.Actor;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
using ATProtoNet.Lexicon.Chat.Bsky.Embed;
using ATProtoNet.Lexicon.Chat.Bsky.Group;
using ATProtoNet.Models;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Chat.Bsky.Convo;

public class ConvoClientTests : IDisposable
{
    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;
    private readonly ConvoClient _convo;

    public ConvoClientTests()
    {
        _httpClient = new HttpClient(_stub) { BaseAddress = new Uri("https://pds.example.com/") };
        _xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
        _convo = new ConvoClient(_xrpc);
    }

    [Fact]
    public async Task ListConvos_SendsCorrectRequest()
    {
        _stub.On("chat.bsky.convo.listConvos", JsonBody(new { convos = Array.Empty<object>() }));

        var result = await _convo.ListConvosAsync(limit: 10, cursor: "abc");

        var request = Assert.Single(_stub.To("chat.bsky.convo.listConvos"));
        Assert.Contains("limit=10", request.Query);
        Assert.Contains("cursor=abc", request.Query);
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
        Assert.NotNull(result);
        Assert.Empty(result.Convos);
    }

    [Fact]
    public async Task GetConvo_SendsCorrectRequest()
    {
        _stub.On("chat.bsky.convo.getConvo", JsonBody(new
        {
            convo = new { id = "convo-1", rev = "rev-1", members = Array.Empty<object>(), muted = false, unreadCount = 0 },
        }));

        var result = await _convo.GetConvoAsync("convo-1");

        var request = Assert.Single(_stub.To("chat.bsky.convo.getConvo"));
        Assert.Contains("convoId=convo-1", request.Query);
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
        Assert.Equal("convo-1", result.Convo.Id);
    }

    [Fact]
    public async Task SendMessage_PostsWithProxy()
    {
        _stub.On("chat.bsky.convo.sendMessage", JsonBody(new
        {
            id = "msg-1",
            rev = "rev-1",
            text = "Hello!",
            sender = new { did = "did:plc:user1" },
            sentAt = "2024-01-01T00:00:00Z",
        }));

        var result = await _convo.SendMessageAsync("convo-1", new MessageInput { Text = "Hello!" });

        var request = Assert.Single(_stub.To("chat.bsky.convo.sendMessage"));
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
        Assert.Contains("convo-1", request.BodyText);
        Assert.Contains("Hello!", request.BodyText);
        Assert.Equal("msg-1", result.Id);
        Assert.Equal("Hello!", result.Text);
    }

    [Fact]
    public async Task MuteConvo_PostsWithProxy()
    {
        _stub.On("chat.bsky.convo.muteConvo", JsonBody(new
        {
            convo = new { id = "convo-1", rev = "rev-2", members = Array.Empty<object>(), muted = true, unreadCount = 0 },
        }));

        var result = await _convo.MuteConvoAsync("convo-1");

        var request = Assert.Single(_stub.To("chat.bsky.convo.muteConvo"));
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
        Assert.True(result.Muted);
    }

    [Fact]
    public async Task UpdateAllRead_PostsWithProxy()
    {
        _stub.On("chat.bsky.convo.updateAllRead", JsonBody(new { updatedCount = 0 }));

        await _convo.UpdateAllReadAsync();

        var request = Assert.Single(_stub.To("chat.bsky.convo.updateAllRead"));
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
    }

    [Fact]
    public async Task GetMessages_SendsCorrectRequest()
    {
        _stub.On("chat.bsky.convo.getMessages", JsonBody(new { messages = Array.Empty<object>() }));

        var result = await _convo.GetMessagesAsync("convo-1", limit: 25);

        var request = Assert.Single(_stub.To("chat.bsky.convo.getMessages"));
        Assert.Contains("convoId=convo-1", request.Query);
        Assert.Contains("limit=25", request.Query);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetLog_SendsCorrectRequest()
    {
        _stub.On("chat.bsky.convo.getLog", JsonBody(new { logs = Array.Empty<object>() }));

        var result = await _convo.GetLogAsync(cursor: "cur123");

        var request = Assert.Single(_stub.To("chat.bsky.convo.getLog"));
        Assert.Contains("cursor=cur123", request.Query);
        Assert.Empty(result.Logs);
    }

    [Fact]
    public async Task AddReaction_PostsWithProxy()
    {
        _stub.On("chat.bsky.convo.addReaction", JsonBody(new
        {
            message = new
            {
                id = "msg-1", rev = "rev-1", text = "hi",
                sender = new { did = "did:plc:user1" },
                sentAt = "2024-01-01T00:00:00Z",
            },
        }));

        await _convo.AddReactionAsync("convo-1", "msg-1", "❤️");

        var request = Assert.Single(_stub.To("chat.bsky.convo.addReaction"));
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
        Assert.Contains("convo-1", request.BodyText);
        Assert.Contains("msg-1", request.BodyText);
    }

    [Fact]
    public async Task ProxyHeader_DoesNotAffectOtherXrpcCalls()
    {
        // Verify that the chat proxy header is per-request and doesn't leak to other calls.
        _stub.On("chat.bsky.convo.listConvos", JsonBody(new { convos = Array.Empty<object>() }));
        _stub.On("app.bsky.feed.getTimeline", "{}");

        await _convo.ListConvosAsync();
        Assert.Equal(ServiceProxy.BskyChatHeader, Assert.Single(_stub.To("chat.bsky.convo.listConvos")).Proxy);

        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");
        Assert.Null(Assert.Single(_stub.To("app.bsky.feed.getTimeline")).Proxy);
    }

    [Fact]
    public async Task ListConvosAsync_FiltersThenPaging_SendsEveryParameter()
    {
        _stub.On("chat.bsky.convo.listConvos", JsonBody(new { convos = Array.Empty<object>() }));

        await _convo.ListConvosAsync(
            ConvoReadState.Unread, ConvoStatus.Accepted, ConvoKinds.Group, ConvoLockStatus.LockedPermanently, 10, "abc");

        var request = Assert.Single(_stub.To("chat.bsky.convo.listConvos"));
        Assert.Equal(
            "readState=unread&status=accepted&kind=group&lockStatus=locked-permanently&limit=10&cursor=abc",
            Uri.UnescapeDataString(request.Query));
    }

    [Fact]
    public async Task GetConvoForMembersAsync_TypedDids_SendsOneParameterEach()
    {
        _stub.On("chat.bsky.convo.getConvoForMembers", JsonBody(new
        {
            convo = new
            {
                id = "convo-1",
                rev = "rev-1",
                members = new[] { new { did = "did:plc:user1", handle = "alice.bsky.social" } },
                muted = false,
                unreadCount = 0,
            },
        }));

        var result = await _convo.GetConvoForMembersAsync(
            [Did.Parse("did:plc:user1"), Did.Parse("did:plc:user2")]);

        var request = Assert.Single(_stub.To("chat.bsky.convo.getConvoForMembers"));
        Assert.Equal("members=did:plc:user1&members=did:plc:user2", Uri.UnescapeDataString(request.Query));
        Assert.Equal(Did.Parse("did:plc:user1"), result.Convo.Members[0].Did);
        Assert.Equal(Handle.Parse("alice.bsky.social"), result.Convo.Members[0].Handle);
    }

    [Fact]
    public async Task SendMessageBatchAsync_AnySequence_SendsEveryItem()
    {
        _stub.On("chat.bsky.convo.sendMessageBatch", JsonBody(new { items = Array.Empty<object>() }));

        var items = Enumerable.Range(1, 3).Select(i => new BatchMessageItem
        {
            ConvoId = $"convo-{i}",
            Message = new MessageInput { Text = $"#{i}" },
        });
        await _convo.SendMessageBatchAsync(items);

        var request = Assert.Single(_stub.To("chat.bsky.convo.sendMessageBatch"));
        using var body = JsonDocument.Parse(request.BodyText);
        Assert.Equal(3, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task EnumerateMessagesAsync_RepeatedCursor_StopsInsteadOfLooping()
    {
        var requests = 0;
        _stub.On("chat.bsky.convo.getMessages", _ =>
        {
            requests++;
            return HttpStub.JsonResponse(
                $$"""
                {"cursor":"same","messages":[{"$type":"chat.bsky.convo.defs#deletedMessageView",
                  "id":"msg-{{requests}}","rev":"rev-1","sender":{"did":"did:plc:user1"},"sentAt":"2026-06-01T12:00:00.000Z"}]}
                """);
        });

        var messages = await _convo.EnumerateMessagesAsync("convo-1").ToListAsync();

        Assert.Equal(2, requests);
        Assert.Equal(["msg-1", "msg-2"], messages.Select(m => Assert.IsType<DeletedMessageView>(m).Id));
    }


    // ──────────────────────────────────────────────────────────
    //  Group-era endpoints
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUnreadCountsAsync_ExcludingGroups_SendsTheFlagAndReadsBothCounts()
    {
        _stub.On("chat.bsky.convo.getUnreadCounts", """{"unreadAcceptedConvos":100,"unreadRequestConvos":3}""");

        var counts = await _convo.GetUnreadCountsAsync(includeGroupChats: false);

        var request = Assert.Single(_stub.To("chat.bsky.convo.getUnreadCounts"));
        Assert.Equal("includeGroupChats=false", Uri.UnescapeDataString(request.Query));
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
        Assert.Equal((100, 3), (counts.UnreadAcceptedConvos, counts.UnreadRequestConvos));
    }

    [Fact]
    public async Task ListConvoRequestsAsync_MixedRequests_ReadsConvosAndJoinRequests()
    {
        _stub.On("chat.bsky.convo.listConvoRequests",
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

        var page = await _convo.ListConvoRequestsAsync(limit: 3, cursor: "abc");

        var request = Assert.Single(_stub.To("chat.bsky.convo.listConvoRequests"));
        Assert.Equal("limit=3&cursor=abc", Uri.UnescapeDataString(request.Query));
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
    public async Task GetConvoMembersAsync_GroupMembers_ReadsTheirKinds()
    {
        _stub.On("chat.bsky.convo.getConvoMembers",
            """
            {"members":[
              {"did":"did:plc:owner","handle":"owner.bsky.social","kind":{"$type":"chat.bsky.actor.defs#groupConvoMember","role":"owner"}},
              {"did":"did:plc:user1","handle":"alice.bsky.social","createdAt":"2024-01-01T00:00:00.000Z",
               "kind":{"$type":"chat.bsky.actor.defs#groupConvoMember","role":"standard",
                       "addedBy":{"did":"did:plc:owner","handle":"owner.bsky.social"}}},
              {"did":"did:plc:user2","handle":"bob.bsky.social","kind":{"$type":"chat.bsky.actor.defs#pastGroupConvoMember"}}]}
            """);

        var page = await _convo.GetConvoMembersAsync("convo-1", limit: 3);

        var request = Assert.Single(_stub.To("chat.bsky.convo.getConvoMembers"));
        Assert.Equal("convoId=convo-1&limit=3", Uri.UnescapeDataString(request.Query));
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


    [Theory]
    [InlineData("lockConvo", "locked")]
    [InlineData("unlockConvo", "unlocked")]
    public async Task LockAndUnlockConvoAsync_PostTheConvoIdAndUnwrapTheGroup(string method, string lockStatus)
    {
        var nsid = $"chat.bsky.convo.{method}";
        _stub.On(nsid,
            """
            {"convo":{"id":"convo-1","rev":"rev-2","members":[],"muted":false,"unreadCount":0,
              "kind":{"$type":"chat.bsky.convo.defs#groupConvo","name":"Book club","memberCount":3,"memberLimit":100,
                      "lockStatus":"LOCK_STATUS","lockStatusModerationOverride":false,"createdAt":"2026-06-01T12:00:00.000Z"}}}
            """.Replace("LOCK_STATUS", lockStatus));

        var convo = method == "lockConvo"
            ? await _convo.LockConvoAsync("convo-1")
            : await _convo.UnlockConvoAsync("convo-1");

        var request = Assert.Single(_stub.To(nsid));
        Assert.Equal("""{"convoId":"convo-1"}""", request.BodyText);
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
        Assert.Equal(lockStatus, Assert.IsType<GroupConvo>(convo.Kind).LockStatus);
    }

    [Fact]
    public async Task SendMessageAsync_ReplyEmbedAndFacets_WritesTheLexiconShape()
    {
        _stub.On("chat.bsky.convo.sendMessage",
            """
            {"id":"msg-2","rev":"rev-2","text":"join us @alice","sender":{"did":"did:plc:owner"},"sentAt":"2026-06-01T12:00:00.000Z",
             "replyTo":{"$type":"chat.bsky.convo.defs#messageView","id":"msg-1","rev":"rev-1","text":"hi",
                        "sender":{"did":"did:plc:user1"},"sentAt":"2026-06-01T11:59:00.000Z"}}
            """);

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
        var request = Assert.Single(_stub.To("chat.bsky.convo.sendMessage"));
        Assert.True(
            JsonElement.DeepEquals(JsonDocument.Parse(expected).RootElement, JsonDocument.Parse(request.BodyText).RootElement),
            request.BodyText);
        Assert.Equal("msg-1", Assert.IsType<MessageView>(sent.ReplyTo).Id);
    }

    [Fact]
    public async Task SendMessageAsync_QuotedPost_WritesARecordEmbed()
    {
        _stub.On("chat.bsky.convo.sendMessage",
            """{"id":"msg-1","rev":"rev-1","text":"look","sender":{"did":"did:plc:owner"},"sentAt":"2026-06-01T12:00:00.000Z"}""");

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

        var request = Assert.Single(_stub.To("chat.bsky.convo.sendMessage"));
        using var body = JsonDocument.Parse(request.BodyText);
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

    private static string JsonBody(object body) => JsonSerializer.Serialize(body);

    public void Dispose() => _httpClient.Dispose();
}
