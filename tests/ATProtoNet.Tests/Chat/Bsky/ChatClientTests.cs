using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Lexicon.Chat.Bsky.Actor;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
using ATProtoNet.Lexicon.Chat.Bsky.Embed;
using ATProtoNet.Lexicon.Chat.Bsky.Group;
using ATProtoNet.Lexicon.Chat.Bsky.Moderation;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Chat.Bsky;

/// <summary>
/// The chat clients' proxy header, and their unions read from (or written to) a real exchange.
/// The chat service is not open source, so the fixtures follow the vendored <c>chat.bsky.*</c>
/// Lexicons field by field. The plain request/response calls are rows in
/// <see cref="ATProtoNet.Tests.Lexicon.EndpointRequestTests"/>.
/// </summary>
public class ChatClientTests : IDisposable
{
    private readonly XrpcTestClient _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task ProxyHeader_DoesNotAffectOtherXrpcCalls()
    {
        // The chat proxy header is per request and doesn't leak to other calls.
        _fixture.On("chat.bsky.convo.listConvos", """{"convos":[]}""");
        _fixture.On("app.bsky.feed.getTimeline", "{}");

        await _fixture.Client.Chat.Convo.ListConvosAsync();
        Assert.Equal("did:web:api.bsky.chat#bsky_chat", Assert.Single(_fixture.To("chat.bsky.convo.listConvos")).Proxy);

        await _fixture.Client.QueryAsync<object>(Nsid.Parse("app.bsky.feed.getTimeline"));
        Assert.Null(Assert.Single(_fixture.To("app.bsky.feed.getTimeline")).Proxy);
    }

    [Fact]
    public async Task ModerationCalls_WithoutAClientWideProxy_SendNoProxyHeader()
    {
        // chat.bsky.moderation calls carry no fixed proxy: they reach whatever the client-wide default names.
        _fixture.On("chat.bsky.moderation.getMessageContext", """{"messages":[]}""");

        await _fixture.Client.Chat.Moderation.GetMessageContextAsync("msg-2");

        Assert.Null(Assert.Single(_fixture.To("chat.bsky.moderation.getMessageContext")).Proxy);
    }

    [Fact]
    public async Task ListConvoRequestsAsync_MixedRequests_ReadsConvosAndJoinRequests()
    {
        _fixture.On("chat.bsky.convo.listConvoRequests",
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

        var page = await _fixture.Client.Chat.Convo.ListConvoRequestsAsync(limit: 3, cursor: "abc");

        _fixture.AssertGet("chat.bsky.convo.listConvoRequests", "limit=3&cursor=abc", ServiceProxy.BskyChatHeader);
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
        _fixture.On("chat.bsky.convo.getConvoMembers",
            """
            {"members":[
              {"did":"did:plc:owner","handle":"owner.bsky.social","kind":{"$type":"chat.bsky.actor.defs#groupConvoMember","role":"owner"}},
              {"did":"did:plc:user1","handle":"alice.bsky.social","createdAt":"2024-01-01T00:00:00.000Z",
               "kind":{"$type":"chat.bsky.actor.defs#groupConvoMember","role":"standard",
                       "addedBy":{"did":"did:plc:owner","handle":"owner.bsky.social"}}},
              {"did":"did:plc:user2","handle":"bob.bsky.social","kind":{"$type":"chat.bsky.actor.defs#pastGroupConvoMember"}}]}
            """);

        var page = await _fixture.Client.Chat.Convo.GetConvoMembersAsync("convo-1", limit: 3);

        _fixture.AssertGet("chat.bsky.convo.getConvoMembers", "convoId=convo-1&limit=3", ServiceProxy.BskyChatHeader);
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
    public async Task SendMessageAsync_ReplyEmbedAndFacets_WritesTheLexiconShape()
    {
        _fixture.On("chat.bsky.convo.sendMessage",
            """
            {"id":"msg-2","rev":"rev-2","text":"join us @alice","sender":{"did":"did:plc:owner"},"sentAt":"2026-06-01T12:00:00.000Z",
             "replyTo":{"$type":"chat.bsky.convo.defs#messageView","id":"msg-1","rev":"rev-1","text":"hi",
                        "sender":{"did":"did:plc:user1"},"sentAt":"2026-06-01T11:59:00.000Z"}}
            """);

        var sent = await _fixture.Client.Chat.Convo.SendMessageAsync("convo-1", new MessageInput
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

        _fixture.AssertPost(
            "chat.bsky.convo.sendMessage",
            """
            {"convoId":"convo-1","message":{"text":"join us @alice",
              "facets":[{"index":{"byteStart":8,"byteEnd":14},"features":[{"$type":"app.bsky.richtext.facet#mention","did":"did:plc:user1"}]}],
              "embed":{"$type":"chat.bsky.embed.joinLink","code":"abc123"},
              "replyTo":{"messageId":"msg-1"}}}
            """,
            ServiceProxy.BskyChatHeader);
        Assert.Equal("msg-1", Assert.IsType<MessageView>(sent.ReplyTo).Id);
    }

    [Fact]
    public async Task GetJoinLinkPreviewsAsync_ReadsEachPreviewKind()
    {
        _fixture.On("chat.bsky.group.getJoinLinkPreviews",
            """
            {"joinLinkPreviews":[
              {"$type":"chat.bsky.group.defs#joinLinkPreviewView","convoId":"convo-1","code":"abc123","name":"Book club",
               "owner":{"did":"did:plc:owner","handle":"owner.bsky.social"},"memberCount":12,"memberLimit":100,
               "requireApproval":false,"joinRule":"anyone"},
              {"$type":"chat.bsky.group.defs#disabledJoinLinkPreviewView","code":"off456"},
              {"$type":"chat.bsky.group.defs#invalidJoinLinkPreviewView","code":"nope"}]}
            """);

        var result = await _fixture.Client.Chat.Group.GetJoinLinkPreviewsAsync(["abc123", "off456", "nope"]);

        _fixture.AssertGet("chat.bsky.group.getJoinLinkPreviews", "codes=abc123&codes=off456&codes=nope", ServiceProxy.BskyChatHeader);
        Assert.Collection(result.JoinLinkPreviews,
            p => Assert.Equal(12, Assert.IsType<JoinLinkPreviewView>(p).MemberCount),
            p => Assert.Equal("off456", Assert.IsType<DisabledJoinLinkPreviewView>(p).Code),
            p => Assert.Equal("nope", Assert.IsType<InvalidJoinLinkPreviewView>(p).Code));
    }

    [Fact]
    public async Task ModerationGetConvosAsync_ReadsEachConvoKind()
    {
        _fixture.On("chat.bsky.moderation.getConvos",
            """
            {"convos":[
              {"id":"convo-1","rev":"r",
               "kind":{"$type":"chat.bsky.moderation.defs#groupConvo","name":"Book club","memberCount":12,"memberLimit":100,
                       "joinRequestCount":4,"lockStatus":"locked","createdAt":"2026-06-01T12:00:00.000Z",
                       "joinLink":{"code":"abc123","enabledStatus":"disabled","requireApproval":false,"joinRule":"anyone",
                                   "createdAt":"2026-06-01T12:00:10.000Z"}}},
              {"id":"convo-2","rev":"r","kind":{"$type":"chat.bsky.moderation.defs#directConvo"}},
              {"id":"convo-3","rev":"r","kind":{"$type":"chat.bsky.moderation.defs#channelConvo","topic":"x"}}]}
            """);

        var result = await _fixture.Client.Chat.Moderation.GetConvosAsync(["convo-1", "convo-2", "convo-3"]);

        _fixture.AssertGet("chat.bsky.moderation.getConvos", "convoIds=convo-1&convoIds=convo-2&convoIds=convo-3");
        Assert.Collection(result.Convos,
            c =>
            {
                var group = Assert.IsType<ModerationGroupConvo>(c.Kind);
                Assert.Equal(ConvoLockStatus.Locked, group.LockStatus);
                Assert.Equal("abc123", group.JoinLink!.Code);
            },
            c => Assert.IsType<ModerationDirectConvo>(c.Kind),
            c => Assert.Equal("chat.bsky.moderation.defs#channelConvo", Assert.IsType<UnknownModerationConvoKind>(c.Kind).Type));
    }
}
