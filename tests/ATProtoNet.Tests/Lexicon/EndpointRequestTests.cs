using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Notification;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
using ATProtoNet.Lexicon.Chat.Bsky.Embed;
using ATProtoNet.Lexicon.Chat.Bsky.Group;
using ATProtoNet.Lexicon.Chat.Bsky.Notification;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Report;
using ATProtoNet.Lexicon.Tools.Ozone.Safelink;
using ATProtoNet.Lexicon.Tools.Ozone.Setting;
using ATProtoNet.Lexicon.Tools.Ozone.Team;
using ATProtoNet.Lexicon.Tools.Ozone.Verification;
using ATProtoNet.Tests.TestSupport;
using static ATProtoNet.Tests.Lexicon.App.Bsky.BskyFixtures;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>
/// One row per XRPC call whose interesting behavior is the request it sends: given every
/// argument, the exact HTTP method, NSID, query string or JSON body, and that a realistic response
/// (every required field present) deserializes. Each row gets its own <see cref="XrpcTestClient"/>,
/// so any route or proxy it sets up is private to it.
/// </summary>
/// <remarks>
/// A call whose response needs more than that (a union, bespoke client logic, an error mapping, or
/// an unusual header or encoding) keeps its own test near the client. The drift tests in
/// <c>Lexicon/Upstream/LexiconDriftTests</c> check every NSID, HTTP method, JSON name and query key
/// against the upstream Lexicons across the whole SDK, so a row does not re-check response fields
/// one by one.
/// </remarks>
public sealed class EndpointRequestTests
{
    private const string ModDid = TestIds.ModDid;
    private const string Chat = ServiceProxy.BskyChatHeader;

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

        void Add(string name, Func<XrpcTestClient, Task> body) => data.Add(name, async () =>
        {
            using var fixture = new XrpcTestClient();
            await body(fixture);
        });

        // A query: the exact (unescaped) query string, and the proxy header when one is given.
        void Get(string nsid, string response, Func<AtProtoClient, Task> call, string query = "", string? proxy = null, string? name = null) =>
            Add(name ?? nsid, async f =>
            {
                f.On(nsid, response);
                await call(f.Client);
                f.AssertGet(nsid, query, proxy);
            });

        // A procedure: the JSON body when one is given, and the proxy header when one is given.
        void Post(string nsid, string response, Func<AtProtoClient, Task> call, string? body = null, string? proxy = null, string? name = null) =>
            Add(name ?? nsid, async f =>
            {
                f.On(nsid, response);
                await call(f.Client);
                f.AssertPost(nsid, body, proxy);
            });

        // ─── chat.bsky.actor / chat.bsky.notification ───

        Post("chat.bsky.actor.deleteAccount", "{}", c => c.Chat.Actor.DeleteAccountAsync(), proxy: Chat);
        Get("chat.bsky.actor.getStatus", """{"chatDisabled":false,"canCreateGroups":true,"groupMemberLimit":100}""",
            c => c.Chat.Actor.GetStatusAsync(), proxy: Chat);

        const string chatPreferences =
            """{"preferences":{"chat":{"include":"all","push":true},"chatRequest":{"include":"follows","push":false}}}""";
        Get("chat.bsky.notification.getPreferences", chatPreferences, c => c.Chat.Notification.GetPreferencesAsync(), proxy: Chat);
        Post("chat.bsky.notification.putPreferences", chatPreferences,
            c => c.Chat.Notification.PutPreferencesAsync(
                chatRequest: new ATProtoNet.Lexicon.Chat.Bsky.Notification.ChatPreference { Include = ChatPreferenceInclude.Follows, Push = false }),
            """{"chatRequest":{"include":"follows","push":false}}""", Chat);

        // ─── chat.bsky.convo ───

        const string convo = """{"id":"convo-1","rev":"rev-1","members":[{"did":"did:plc:user1","handle":"alice.bsky.social"}],"muted":false,"unreadCount":0}""";
        const string message = """{"$type":"chat.bsky.convo.defs#messageView","id":"msg-1","rev":"rev-1","text":"hi","sender":{"did":"did:plc:user1"},"sentAt":"2024-01-01T00:00:00Z"}""";

        Get("chat.bsky.convo.listConvos", $$"""{"convos":[{{convo}}]}""",
            c => c.Chat.Convo.ListConvosAsync(ConvoReadState.Unread, ConvoStatus.Accepted, ConvoKinds.Group, ConvoLockStatus.LockedPermanently, 10, "abc"),
            "readState=unread&status=accepted&kind=group&lockStatus=locked-permanently&limit=10&cursor=abc", Chat);
        Get("chat.bsky.convo.getConvo", $$"""{"convo":{{convo}}}""", c => c.Chat.Convo.GetConvoAsync("convo-1"), "convoId=convo-1", Chat);
        Get("chat.bsky.convo.getConvoForMembers", $$"""{"convo":{{convo}}}""",
            c => c.Chat.Convo.GetConvoForMembersAsync([Did.Parse("did:plc:user1"), Did.Parse("did:plc:user2")]),
            "members=did:plc:user1&members=did:plc:user2", Chat);
        Get("chat.bsky.convo.getUnreadCounts", """{"unreadAcceptedConvos":100,"unreadRequestConvos":3}""",
            c => c.Chat.Convo.GetUnreadCountsAsync(includeGroupChats: false), "includeGroupChats=false", Chat);
        Get("chat.bsky.convo.getMessages", """{"messages":[]}""", c => c.Chat.Convo.GetMessagesAsync("convo-1", limit: 25),
            "convoId=convo-1&limit=25", Chat);
        Get("chat.bsky.convo.getLog", """{"logs":[]}""", c => c.Chat.Convo.GetLogAsync(cursor: "cur123"), "cursor=cur123", Chat);
        Post("chat.bsky.convo.muteConvo", $$"""{"convo":{{convo}}}""", c => c.Chat.Convo.MuteConvoAsync("convo-1"), """{"convoId":"convo-1"}""", Chat);
        Post("chat.bsky.convo.lockConvo", $$"""{"convo":{{convo}}}""", c => c.Chat.Convo.LockConvoAsync("convo-1"), """{"convoId":"convo-1"}""", Chat);
        Post("chat.bsky.convo.unlockConvo", $$"""{"convo":{{convo}}}""", c => c.Chat.Convo.UnlockConvoAsync("convo-1"), """{"convoId":"convo-1"}""", Chat);
        Post("chat.bsky.convo.updateAllRead", """{"updatedCount":0}""", c => c.Chat.Convo.UpdateAllReadAsync(), proxy: Chat);
        Post("chat.bsky.convo.addReaction", $$"""{"message":{{message}}}""", c => c.Chat.Convo.AddReactionAsync("convo-1", "msg-1", "❤️"),
            """{"convoId":"convo-1","messageId":"msg-1","value":"❤️"}""", Chat);
        Post("chat.bsky.convo.sendMessage", message,
            c => c.Chat.Convo.SendMessageAsync("convo-1", new MessageInput
            {
                Text = "look",
                Embed = new MessageRecordEmbed
                {
                    Record = new ATProtoNet.Models.StrongRef { Uri = AtUri.Parse(PostUri), Cid = Cid.Parse(PostCid) },
                },
            }),
            $$$"""{"convoId":"convo-1","message":{"text":"look","embed":{"$type":"app.bsky.embed.record","record":{"uri":"{{{PostUri}}}","cid":"{{{PostCid}}}"}} }}""",
            Chat, name: "chat.bsky.convo.sendMessage (quoted post)");
        Post("chat.bsky.convo.sendMessageBatch", """{"items":[]}""",
            c => c.Chat.Convo.SendMessageBatchAsync(Enumerable.Range(1, 2).Select(i => new BatchMessageItem
            {
                ConvoId = $"convo-{i}",
                Message = new MessageInput { Text = $"#{i}" },
            })),
            """{"items":[{"convoId":"convo-1","message":{"text":"#1"}},{"convoId":"convo-2","message":{"text":"#2"}}]}""", Chat);

        // ─── chat.bsky.group ───

        const string group = """
            {"id":"convo-1","rev":"2222222222225","members":[],"muted":false,"unreadCount":0,"status":"accepted",
             "kind":{"$type":"chat.bsky.convo.defs#groupConvo","name":"Book club","memberCount":1,"memberLimit":100,
                     "lockStatus":"unlocked","lockStatusModerationOverride":false,"createdAt":"2026-06-01T12:00:00.000Z"}}
            """;
        const string joinLink =
            """{"joinLink":{"code":"abc123","enabledStatus":"enabled","requireApproval":true,"joinRule":"followedByOwner","createdAt":"2026-06-01T12:00:10.000Z"}}""";
        var alice = Did.Parse("did:plc:alice");

        Post("chat.bsky.group.createGroup", $$"""{"convo":{{group}}}""", c => c.Chat.Group.CreateGroupAsync("Book club", [alice, Did.Parse("did:plc:bob")]),
            """{"members":["did:plc:alice","did:plc:bob"],"name":"Book club"}""", Chat);
        Post("chat.bsky.group.editGroup", $$"""{"convo":{{group}}}""", c => c.Chat.Group.EditGroupAsync("convo-1", "Book club"),
            """{"convoId":"convo-1","name":"Book club"}""", Chat);
        Post("chat.bsky.group.addMembers", $$"""{"convo":{{group}},"addedMembers":[{"did":"did:plc:alice","handle":"alice.bsky.social"}]}""",
            c => c.Chat.Group.AddMembersAsync("convo-1", [alice]), """{"convoId":"convo-1","members":["did:plc:alice"]}""", Chat);
        Post("chat.bsky.group.removeMembers", $$"""{"convo":{{group}}}""", c => c.Chat.Group.RemoveMembersAsync("convo-1", [alice]),
            """{"convoId":"convo-1","members":["did:plc:alice"]}""", Chat);
        Get("chat.bsky.group.listMutualGroups", $$"""{"cursor":"next","convos":[{{group}}]}""",
            c => c.Chat.Group.ListMutualGroupsAsync(alice, limit: 10, cursor: "abc"), "subject=did:plc:alice&limit=10&cursor=abc", Chat);
        Post("chat.bsky.group.createJoinLink", joinLink, c => c.Chat.Group.CreateJoinLinkAsync("convo-1", JoinRule.FollowedByOwner, requireApproval: true),
            """{"convoId":"convo-1","requireApproval":true,"joinRule":"followedByOwner"}""", Chat);
        Post("chat.bsky.group.createJoinLink", joinLink, c => c.Chat.Group.CreateJoinLinkAsync("convo-1", JoinRule.Anyone),
            """{"convoId":"convo-1","joinRule":"anyone"}""", Chat, name: "chat.bsky.group.createJoinLink (approval left to the server)");
        Post("chat.bsky.group.editJoinLink", joinLink, c => c.Chat.Group.EditJoinLinkAsync("convo-1", requireApproval: false),
            """{"convoId":"convo-1","requireApproval":false}""", Chat);
        Post("chat.bsky.group.enableJoinLink", joinLink, c => c.Chat.Group.EnableJoinLinkAsync("convo-1"), """{"convoId":"convo-1"}""", Chat);
        Post("chat.bsky.group.disableJoinLink", joinLink, c => c.Chat.Group.DisableJoinLinkAsync("convo-1"), """{"convoId":"convo-1"}""", Chat);
        Post("chat.bsky.group.requestJoin", $$"""{"status":"joined","convo":{{group}}}""", c => c.Chat.Group.RequestJoinAsync("abc123"),
            """{"code":"abc123"}""", Chat);
        Post("chat.bsky.group.requestJoin", """{"status":"pending"}""", c => c.Chat.Group.RequestJoinAsync("abc123"),
            """{"code":"abc123"}""", Chat, name: "chat.bsky.group.requestJoin (pending: no convo yet)");
        Post("chat.bsky.group.withdrawJoinRequest", "{}", c => c.Chat.Group.WithdrawJoinRequestAsync("convo-1"),
            """{"convoId":"convo-1"}""", Chat);
        Get("chat.bsky.group.listJoinRequests", """
            {"cursor":"next","requests":[{"convoId":"convo-1","requestedAt":"2026-06-01T12:05:00.000Z",
              "requestedBy":{"did":"did:plc:alice","handle":"alice.bsky.social"}}]}
            """,
            c => c.Chat.Group.ListJoinRequestsAsync("convo-1", limit: 5, cursor: "abc"), "convoId=convo-1&limit=5&cursor=abc", Chat);
        Post("chat.bsky.group.approveJoinRequest", $$"""{"convo":{{group}}}""", c => c.Chat.Group.ApproveJoinRequestAsync("convo-1", alice),
            """{"convoId":"convo-1","member":"did:plc:alice"}""", Chat);
        Post("chat.bsky.group.rejectJoinRequest", "{}", c => c.Chat.Group.RejectJoinRequestAsync("convo-1", Did.Parse(ModDid)),
            $$"""{"convoId":"convo-1","member":"{{ModDid}}"}""", Chat);
        Post("chat.bsky.group.updateJoinRequestsRead", "{}", c => c.Chat.Group.UpdateJoinRequestsReadAsync("convo-1"),
            """{"convoId":"convo-1"}""", Chat);

        // ─── chat.bsky.moderation: no fixed proxy, so the client-wide one applies ───

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
        Get("chat.bsky.moderation.getActorMetadata", """
            {"day":{"messagesSent":3,"messagesReceived":4,"convos":1,"convosStarted":1},
             "month":{"messagesSent":30,"messagesReceived":40,"convos":5,"convosStarted":2},
             "all":{"messagesSent":300,"messagesReceived":400,"convos":9,"convosStarted":6}}
            """,
            c => c.Chat.Moderation.GetActorMetadataAsync(alice), "actor=did:plc:alice");
        Get("chat.bsky.moderation.getMessageContext", $$"""{"messages":[{{message}}]}""",
            c => c.Chat.Moderation.GetMessageContextAsync("msg-2", convoId: "convo-1", before: 3, after: 0, maxInterleavedSystemMessages: 2),
            "convoId=convo-1&messageId=msg-2&before=3&after=0&maxInterleavedSystemMessages=2");
        Get("chat.bsky.moderation.getMessageContext", """{"messages":[]}""", c => c.Chat.Moderation.GetMessageContextAsync("msg-2"),
            "messageId=msg-2", name: "chat.bsky.moderation.getMessageContext (message only)");
        Get("chat.bsky.moderation.getConvo", """{"convo":{"id":"convo-1","rev":"r","kind":{"$type":"chat.bsky.moderation.defs#directConvo"}}}""",
            c => c.Chat.Moderation.GetConvoAsync("convo-1"), "convoId=convo-1");
        Get("chat.bsky.moderation.getConvoMembers", """
            {"cursor":"next","members":[{"did":"did:plc:alice","handle":"alice.bsky.social",
              "kind":{"$type":"chat.bsky.actor.defs#pastGroupConvoMember"}}]}
            """,
            c => c.Chat.Moderation.GetConvoMembersAsync("convo-1", limit: 10, cursor: "abc"), "convoId=convo-1&limit=10&cursor=abc");

        // ─── app.bsky.feed ───

        var alicePost = AtUri.Parse(PostUri);
        var bobPost = AtUri.Parse(OtherPostUri);

        Get("app.bsky.feed.getAuthorFeed", $$"""{"feed":[{"post":{{PostViewJson}}}]}""",
            c => c.Bsky.Feed.GetAuthorFeedAsync(Did.Parse(AliceDid), filter: "posts_no_replies", includePins: false, limit: 5, cursor: "c"),
            $"actor={AliceDid}&filter=posts_no_replies&includePins=false&limit=5&cursor=c");
        Get("app.bsky.feed.getPosts", $$"""{"posts":[{{PostViewJson}}]}""", c => c.Bsky.Feed.GetPostsAsync([alicePost, bobPost]),
            $"uris={PostUri}&uris={OtherPostUri}");
        Get("app.bsky.feed.searchPosts", """{"posts":[]}""",
            c => c.Bsky.Feed.SearchPostsAsync("hello", since: "2024-01-01", mentions: Handle.Parse("bob.test"), author: Did.Parse(AliceDid)),
            $"q=hello&since=2024-01-01&mentions=bob.test&author={AliceDid}");
        Get("app.bsky.feed.searchPostsV2", """{"posts":[]}""",
            c => c.Bsky.Feed.SearchPostsV2Async(filters: new ATProtoNet.Lexicon.App.Bsky.Feed.PostSearchFilters { Hashtags = ["atproto"] }),
            "hashtags=atproto", name: "app.bsky.feed.searchPostsV2 (filters only, no query)");
        Post("app.bsky.feed.sendInteractions", "{}",
            c => c.Bsky.Feed.SendInteractionsAsync(
                [
                    new ATProtoNet.Lexicon.App.Bsky.Feed.Interaction
                    {
                        Item = alicePost,
                        Event = ATProtoNet.Lexicon.App.Bsky.Feed.InteractionEvent.RequestLess,
                        FeedContext = "ctx-1",
                        ReqId = "req-1",
                    },
                    new ATProtoNet.Lexicon.App.Bsky.Feed.Interaction { Item = bobPost, Event = ATProtoNet.Lexicon.App.Bsky.Feed.InteractionEvent.Seen },
                ],
                feed: AtUri.Parse($"at://{AliceDid}/app.bsky.feed.generator/discover")),
            $$"""
            {"feed":"at://{{AliceDid}}/app.bsky.feed.generator/discover","interactions":[
              {"item":"{{PostUri}}","event":"app.bsky.feed.defs#requestLess","feedContext":"ctx-1","reqId":"req-1"},
              {"item":"{{OtherPostUri}}","event":"app.bsky.feed.defs#interactionSeen"}]}
            """);

        // ─── app.bsky.graph / app.bsky.labeler / app.bsky.bookmark ───

        Post("app.bsky.graph.muteActor", "{}", c => c.Bsky.Graph.MuteActorAsync(Handle.Parse("bob.test")), """{"actor":"bob.test"}""");
        Get("app.bsky.graph.getListsWithMembership",
            $$"""{"cursor":"n","listsWithMembership":[{"list":{{ListViewJson}},"listItem":{{ListItemViewJson}}},{"list":{{ListViewJson}}}]}""",
            c => c.Bsky.Graph.GetListsWithMembershipAsync(Handle.Parse("bob.test"), ["curatelist", "modlist"], limit: 20, cursor: "c"),
            "actor=bob.test&purposes=curatelist&purposes=modlist&limit=20&cursor=c");
        Get("app.bsky.graph.getStarterPacksWithMembership",
            $$"""{"starterPacksWithMembership":[{"starterPack":{{StarterPackViewJson}},"listItem":{{ListItemViewJson}}}]}""",
            c => c.Bsky.Graph.GetStarterPacksWithMembershipAsync(Did.Parse(BobDid), limit: 10), $"actor={BobDid}&limit=10");
        Get("app.bsky.graph.searchStarterPacksV2", $$"""{"cursor":"25","hitsTotal":40,"starterPacks":[{{StarterPackViewJson}}]}""",
            c => c.Bsky.Graph.SearchStarterPacksV2Async("science", limit: 25), "q=science&limit=25");

        Get("app.bsky.labeler.getServices", """{"views":[]}""",
            c => c.Bsky.Labeler.GetServicesAsync([Did.Parse("did:plc:labeler1"), Did.Parse("did:plc:labeler2")], detailed: true),
            "dids=did:plc:labeler1&dids=did:plc:labeler2&detailed=true");
        Get("app.bsky.labeler.getServices", """{"views":[]}""",
            c => c.Bsky.Labeler.GetServicesAsync([Did.Parse("did:plc:labeler1")]), "dids=did:plc:labeler1",
            name: "app.bsky.labeler.getServices (no detailed)");

        Post("app.bsky.bookmark.createBookmark", "{}", c => c.Bsky.Bookmark.CreateBookmarkAsync(alicePost, Cid.Parse(PostCid)),
            $$"""{"uri":"{{PostUri}}","cid":"{{PostCid}}"}""");
        Post("app.bsky.bookmark.deleteBookmark", "{}", c => c.Bsky.Bookmark.DeleteBookmarkAsync(alicePost), $$"""{"uri":"{{PostUri}}"}""");

        // ─── app.bsky.notification ───

        // Regression: seenAt is deliberately not a parameter; upstream answers it with an error since 2026-09-21.
        Get("app.bsky.notification.listNotifications", $$"""
            {"notifications":[{"uri":"at://{{AliceDid}}/app.bsky.feed.like/3k2lb","cid":"{{PostCid}}","author":{{AliceBasicJson}},
              "reason":"like","reasonSubject":"{{PostUri}}","record":{},"isRead":false,"indexedAt":"2024-05-01T12:00:00.000Z"}],
             "seenAt":"2024-05-01T12:00:00+02:00"}
            """,
            c => c.Bsky.Notification.ListNotificationsAsync(reasons: [NotificationReasons.Like, NotificationReasons.LikeViaRepost], limit: 10),
            "reasons=like&reasons=like-via-repost&limit=10");
        Post("app.bsky.notification.updateSeen", "{}", c => c.Bsky.Notification.UpdateSeenAsync(AtDatetime.Parse("2024-05-01T12:00:00Z")),
            """{"seenAt":"2024-05-01T12:00:00Z"}""");
        Post("app.bsky.notification.unregisterPush", "{}",
            c => c.Bsky.Notification.UnregisterPushAsync(Did.Parse("did:web:api.bsky.app"), "device-token", PushPlatform.Ios, "xyz.blueskyweb.app"),
            """{"serviceDid":"did:web:api.bsky.app","token":"device-token","platform":"ios","appId":"xyz.blueskyweb.app"}""");

        const string preferences = """
            {"preferences":{
              "chat":{"include":"all","push":true},"follow":{"include":"all","list":true,"push":true},
              "like":{"include":"follows","list":true,"push":false},"likeViaRepost":{"include":"all","list":true,"push":true},
              "mention":{"include":"all","list":true,"push":true},"quote":{"include":"all","list":true,"push":true},
              "reply":{"include":"all","list":true,"push":true},"repost":{"include":"all","list":true,"push":true},
              "repostViaRepost":{"include":"all","list":false,"push":false},"starterpackJoined":{"list":true,"push":true},
              "subscribedPost":{"list":true,"push":true},"unverified":{"list":true,"push":true},"verified":{"list":true,"push":false}}}
            """;
        Get("app.bsky.notification.getPreferences", preferences, c => c.Bsky.Notification.GetPreferencesAsync());
        Post("app.bsky.notification.putPreferencesV2", preferences,
            c => c.Bsky.Notification.PutPreferencesV2Async(new PutPreferencesV2Request
            {
                Like = new FilterablePreference { Include = NotificationInclude.Follows, List = true, Push = false },
                Verified = new NotificationPreference { List = true, Push = false },
            }),
            """{"like":{"include":"follows","list":true,"push":false},"verified":{"list":true,"push":false}}""");
        Get("app.bsky.notification.listActivitySubscriptions",
            $$"""{"cursor":"n","subscriptions":[{"did":"{{ModDid}}","handle":"mod.test"}]}""",
            c => c.Bsky.Notification.ListActivitySubscriptionsAsync(limit: 5), "limit=5");
        Post("app.bsky.notification.putActivitySubscription",
            $$$"""{"subject":"{{{ModDid}}}","activitySubscription":{"post":true,"reply":false}}""",
            c => c.Bsky.Notification.PutActivitySubscriptionAsync(Did.Parse(ModDid), post: true, reply: false),
            $$$"""{"subject":"{{{ModDid}}}","activitySubscription":{"post":true,"reply":false}}""");
        Post("app.bsky.notification.putActivitySubscription", $$"""{"subject":"{{BobDid}}"}""",
            c => c.Bsky.Notification.PutActivitySubscriptionAsync(Did.Parse(BobDid), post: false, reply: false),
            $$$"""{"subject":"{{{BobDid}}}","activitySubscription":{"post":false,"reply":false}}""",
            name: "app.bsky.notification.putActivitySubscription (removed: none comes back)");

        // ─── app.bsky.unspecced / app.bsky.embed ───

        Get("app.bsky.unspecced.getPostThreadV2", """{"thread":[],"hasOtherReplies":false}""",
            c => c.Bsky.Unspecced.GetPostThreadV2Async(alicePost), $"anchor={PostUri}",
            name: "app.bsky.unspecced.getPostThreadV2 (defaults)");
        Get("app.bsky.unspecced.getPostThreadOtherV2", $$$"""
            {"thread":[{"uri":"{{{PostUri}}}","depth":1,"value":{"$type":"app.bsky.unspecced.defs#threadItemPost","post":{{{PostViewJson}}},
              "moreParents":false,"moreReplies":2,"opThread":false,"hiddenByThreadgate":true,"mutedByViewer":false}}]}
            """,
            c => c.Bsky.Unspecced.GetPostThreadOtherV2Async(bobPost), $"anchor={OtherPostUri}");
        const string documentUri = $"at://{AliceDid}/site.standard.document/3lwinfmsd2k2i";
        const string publicationUri = $"at://{AliceDid}/site.standard.publication/3lwinfmsd2k2j";
        Get("app.bsky.embed.getEmbedExternalView", $$$"""
            {"view":{"$type":"app.bsky.embed.external#view","external":{"uri":"https://alice.example.com/post","title":"A post",
               "description":"About things","createdAt":"2026-09-01T00:00:00.000Z","readingTime":4,
               "source":{"uri":"https://alice.example.com","title":"Alice's blog","theme":{"accentRGB":{"r":10,"g":20,"b":30} } },
               "associatedRefs":[{"uri":"{{{documentUri}}}","cid":"{{{PostCid}}}"}],"associatedProfiles":[{{{AliceBasicJson}}}]}},
             "associatedRefs":[{"uri":"{{{documentUri}}}","cid":"{{{PostCid}}}"},{"uri":"{{{publicationUri}}}","cid":"{{{OtherCid}}}"}],
             "associatedRecords":[{"$type":"site.standard.document","title":"A post"},{"$type":"site.standard.publication","name":"Alice's blog"}]}
            """,
            c => c.Bsky.Embed.GetEmbedExternalViewAsync("https://alice.example.com/post", [AtUri.Parse(documentUri), AtUri.Parse(publicationUri)]),
            $"url=https://alice.example.com/post&uris={documentUri}&uris={publicationUri}");
        Get("app.bsky.embed.getEmbedExternalView", "{}",
            c => c.Bsky.Embed.GetEmbedExternalViewAsync("https://example.com", [alicePost]),
            $"url=https://example.com&uris={PostUri}", name: "app.bsky.embed.getEmbedExternalView (nothing resolved)");

        // ─── app.bsky.draft / app.bsky.ageassurance / app.bsky.video ───

        Get("app.bsky.draft.getDrafts", """
            {"cursor":"n","drafts":[{"id":"3lwinfmsd2k2h","createdAt":"2026-09-20T10:00:00.000Z","updatedAt":"2026-09-21T10:00:00.000Z",
              "draft":{"posts":[{"text":"a video","embedVideos":[{"localRef":{"path":"file:///v.mp4"},"alt":"clip","captions":[{"lang":"en","content":"WEBVTT"}]}],
                "embedExternals":[{"uri":"https://example.com"}],
                "labels":{"$type":"com.atproto.label.defs#selfLabels","values":[{"val":"graphic-media"}]},
                "embedGallery":{"items":[{"$type":"app.bsky.draft.defs#draftEmbedImage","localRef":{"path":"file:///g.jpg"}}]}}]}}]}
            """,
            c => c.Bsky.Draft.GetDraftsAsync(limit: 1), "limit=1");
        Post("app.bsky.draft.updateDraft", "{}",
            c => c.Bsky.Draft.UpdateDraftAsync(
                Tid.Parse("3lwinfmsd2k2h"),
                new ATProtoNet.Lexicon.App.Bsky.Draft.Draft { Posts = [new ATProtoNet.Lexicon.App.Bsky.Draft.DraftPost { Text = "edited" }] }),
            """{"draft":{"id":"3lwinfmsd2k2h","draft":{"posts":[{"text":"edited"}]}}}""");
        Post("app.bsky.draft.deleteDraft", "{}", c => c.Bsky.Draft.DeleteDraftAsync(Tid.Parse("3lwinfmsd2k2h")), """{"id":"3lwinfmsd2k2h"}""");

        Post("app.bsky.ageassurance.begin", """{"lastInitiatedAt":"2026-09-25T10:00:00.000Z","status":"pending","access":"unknown"}""",
            c => c.Bsky.AgeAssurance.BeginAsync("alice@example.com", "en", "GB"),
            """{"email":"alice@example.com","language":"en","countryCode":"GB"}""");
        Get("app.bsky.ageassurance.getState",
            """{"state":{"status":"assured","access":"full"},"metadata":{"accountCreatedAt":"2023-04-12T04:53:57.057Z"}}""",
            c => c.Bsky.AgeAssurance.GetStateAsync("US", "TX"), "countryCode=US&regionCode=TX");

        Post("app.bsky.video.startUpload", """{"jobId":"job-1","partSizeBytes":8388608,"partCount":3,"expiresAt":"2026-09-25T13:00:00.000Z"}""",
            c => c.Bsky.Video.StartUploadAsync(20_000_000, "video/mp4", name: "clip.mp4", durationMs: 61_000, width: 1920, height: 1080),
            """{"sizeBytes":20000000,"mimeType":"video/mp4","name":"clip.mp4","durationMs":61000,"width":1920,"height":1080}""");
        Get("app.bsky.video.getUploadStatus", """
            {"jobId":"job-1","partSizeBytes":5242880,"partCount":3,"receivedParts":[1,3],"expiresAt":"2026-09-25T13:00:00.000Z",
             "state":"failed","failureReason":"parts missing at expiry"}
            """,
            c => c.Bsky.Video.GetUploadStatusAsync("job-1"), "jobId=job-1");

        // ─── com.atproto.repo / com.atproto.identity ───

        const string note = $"at://{ModDid}/com.example.note/n1";
        Get("com.atproto.repo.getRecord", $$$"""{"uri":"{{{note}}}","value":{}}""", c => c.Repo.GetRecordAsync(AtUri.Parse(note)),
            $"repo={ModDid}&collection=com.example.note&rkey=n1", name: "com.atproto.repo.getRecord (by AT URI)");
        Post("com.atproto.repo.deleteRecord", "{}", c => c.Repo.DeleteRecordAsync(AtUri.Parse(note)),
            $$"""{"repo":"{{ModDid}}","collection":"com.example.note","rkey":"n1"}""", name: "com.atproto.repo.deleteRecord (by AT URI)");
        Get("com.atproto.repo.listRecords", """{"records":[]}""",
            c => c.Repo.ListRecordsAsync(Did.Parse(ModDid), Nsid.Parse("com.example.note"), reverse: true, limit: 5, cursor: "c"),
            $"repo={ModDid}&collection=com.example.note&limit=5&cursor=c&reverse=true");

        const string identity = $$"""{"did":"{{ModDid}}","handle":"atproto.com","didDoc":{{ATProtoNet.Tests.Identity.DidDocs.AtprotoDotCom}}}""";
        Get("com.atproto.identity.resolveDid", $$"""{"didDoc":{{ATProtoNet.Tests.Identity.DidDocs.AtprotoDotCom}}}""",
            c => c.Identity.ResolveDidAsync(Did.Parse(ModDid)), $"did={ModDid}");
        Post("com.atproto.identity.refreshIdentity", identity, c => c.Identity.RefreshIdentityAsync(AtIdentifier.Parse(ModDid)),
            $$"""{"identifier":"{{ModDid}}"}""");

        // ─── com.atproto.admin / com.atproto.temp ───

        Get("com.atproto.admin.searchAccounts",
            $$"""{"cursor":"c2","accounts":[{"did":"{{ModDid}}","handle":"alice.test","email":"a@example.com","indexedAt":"2026-01-01T00:00:00.000Z"}]}""",
            c => c.Admin.SearchAccountsAsync(email: "a@example.com", limit: 10, cursor: "c1"), "email=a@example.com&limit=10&cursor=c1");
        Post("com.atproto.admin.updateAccountSigningKey", "{}",
            c => c.Admin.UpdateAccountSigningKeyAsync(Did.Parse(ModDid), Did.Parse("did:key:zQ3shTbmthbWnaziXpJifLJvXvGwVyW3zeWetTatAXUtA1b2S")),
            $$"""{"did":"{{ModDid}}","signingKey":"did:key:zQ3shTbmthbWnaziXpJifLJvXvGwVyW3zeWetTatAXUtA1b2S"}""");

        Get("com.atproto.temp.checkSignupQueue", """{"activated":false,"placeInQueue":12,"estimatedTimeMs":3600000}""",
            c => c.Temp.CheckSignupQueueAsync());
        Add("com.atproto.temp.dereferenceScope", async f =>
        {
            // The client unwraps the output object to its one string.
            f.On("com.atproto.temp.dereferenceScope", """{"scope":"atproto repo:app.bsky.feed.post?action=create"}""");
            var scope = await f.Client.Temp.DereferenceScopeAsync("ref:abc123");
            f.AssertGet("com.atproto.temp.dereferenceScope", "scope=ref:abc123");
            Assert.Equal("atproto repo:app.bsky.feed.post?action=create", scope);
        });
        Post("com.atproto.temp.requestPhoneVerification", "{}", c => c.Temp.RequestPhoneVerificationAsync("+15555550123"),
            """{"phoneNumber":"+15555550123"}""");
        Post("com.atproto.temp.revokeAccountCredentials", "{}", c => c.Temp.RevokeAccountCredentialsAsync(Did.Parse(ModDid)),
            $$"""{"account":"{{ModDid}}"}""");

        // ─── tools.ozone.queue ───

        const string queue = $$$"""
            {"id":4,"name":"Post spam","subjectTypes":["record"],"collection":"app.bsky.feed.post",
             "reportTypes":["tools.ozone.report.defs#reasonMisleadingSpam"],"description":"Spammy posts",
             "recommendedPolicies":["spam"],"createdBy":"{{{ModDid}}}","createdAt":"2026-09-01T00:00:00.000Z",
             "updatedAt":"2026-09-02T00:00:00.000Z","enabled":true,
             "stats":{"pendingCount":3,"actionedCount":10,"inboundCount":13,"actionRate":77,"lastUpdated":"2026-09-02T00:00:00.000Z"}}
            """;
        const string queueAssignment = $$"""{"id":8,"did":"{{ModDid}}","queue":{{queue}},"startAt":"2026-09-01T00:00:00.000Z"}""";

        Post("tools.ozone.queue.createQueue", $$"""{"queue":{{queue}}}""",
            c => c.Ozone.Queue.CreateQueueAsync("Post spam", [ReportSubjectType.Record], Nsid.Parse("app.bsky.feed.post"),
                [ReportReasons.MisleadingSpam], "Spammy posts", ["spam"]),
            """
            {"name":"Post spam","subjectTypes":["record"],"collection":"app.bsky.feed.post",
             "reportTypes":["tools.ozone.report.defs#reasonMisleadingSpam"],"description":"Spammy posts","recommendedPolicies":["spam"]}
            """);
        Post("tools.ozone.queue.createQueue", $$"""{"queue":{{queue}}}""", c => c.Ozone.Queue.CreateQueueAsync("Manual"),
            """{"name":"Manual"}""", name: "tools.ozone.queue.createQueue (name only)");
        Post("tools.ozone.queue.updateQueue", $$"""{"queue":{{queue}}}""",
            c => c.Ozone.Queue.UpdateQueueAsync(4, enabled: false, description: "Paused"),
            """{"queueId":4,"enabled":false,"description":"Paused"}""");
        Post("tools.ozone.queue.deleteQueue", """{"deleted":true,"reportsMigrated":12}""",
            c => c.Ozone.Queue.DeleteQueueAsync(4, migrateToQueueId: 5), """{"queueId":4,"migrateToQueueId":5}""");
        Get("tools.ozone.queue.listQueues", $$"""{"cursor":"c1","queues":[{{queue}}]}""",
            c => c.Ozone.Queue.ListQueuesAsync(enabled: true, subjectType: ReportSubjectType.Record,
                collection: Nsid.Parse("app.bsky.feed.post"), reportTypes: [ReportReasons.MisleadingSpam], limit: 10),
            "enabled=true&subjectType=record&collection=app.bsky.feed.post&reportTypes=tools.ozone.report.defs#reasonMisleadingSpam&limit=10");
        Post("tools.ozone.queue.routeReports", """{"assigned":40,"unmatched":2}""", c => c.Ozone.Queue.RouteReportsAsync(1000, 1999),
            """{"startReportId":1000,"endReportId":1999}""");
        Post("tools.ozone.queue.assignModerator", queueAssignment, c => c.Ozone.Queue.AssignModeratorAsync(4, Did.Parse(ModDid)),
            $$"""{"queueId":4,"did":"{{ModDid}}"}""");
        Post("tools.ozone.queue.unassignModerator", "{}", c => c.Ozone.Queue.UnassignModeratorAsync(4, Did.Parse(ModDid)),
            $$"""{"queueId":4,"did":"{{ModDid}}"}""");
        Get("tools.ozone.queue.getAssignments", $$"""{"assignments":[{{queueAssignment}}]}""",
            c => c.Ozone.Queue.GetAssignmentsAsync([4, 5], [Did.Parse(ModDid)], onlyActive: true, limit: 20, cursor: "c0"),
            $"onlyActive=true&queueIds=4&queueIds=5&dids={ModDid}&limit=20&cursor=c0");

        // ─── tools.ozone.report ───

        const string userDid = "did:plc:z72i7hdynmk6r22z27h6tvur";
        const string userPost = $"at://{userDid}/app.bsky.feed.post/3k2la";
        const string report = $$"""
            {"id":42,"eventId":7,"status":"open","subject":{"type":"record","subject":"{{userPost}}"},
             "reportType":"tools.ozone.report.defs#reasonMisleadingSpam","reportedBy":"{{userDid}}",
             "reporter":{"type":"account","subject":"{{userDid}}"},"comment":"spam","createdAt":"2026-09-01T00:00:00.000Z",
             "actionEventIds":[9,8],"assignment":{"did":"{{ModDid}}","assignedAt":"2026-09-01T00:00:00.000Z"},"isMuted":false}
            """;
        const string reportAssignment =
            $$$"""{"id":11,"did":"{{{ModDid}}}","reportId":42,"startAt":"2026-09-01T00:00:00.000Z","moderator":{"did":"{{{ModDid}}}","role":"tools.ozone.team.defs#roleModerator"}}""";
        const string activity = $$"""
            {"id":5,"reportId":42,"activity":{"$type":"tools.ozone.report.defs#closeActivity","previousStatus":"assigned"},
             "internalNote":"dupe","meta":{"assignmentId":11},"isAutomated":false,"createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z"}
            """;

        Get("tools.ozone.report.getReport", report, c => c.Ozone.Report.GetReportAsync(42), "id=42");
        Get("tools.ozone.report.getLatestReport", $$"""{"report":{{report}}}""", c => c.Ozone.Report.GetLatestReportAsync());
        Get("tools.ozone.report.queryReports", $$"""{"cursor":"c1","reports":[{{report}}]}""",
            c => c.Ozone.Report.QueryReportsAsync(
                ReportStatus.Open,
                new ReportFilter
                {
                    QueueId = -1,
                    ReportTypes = [ReportReasons.MisleadingSpam, ReportReasons.Spam],
                    Did = Did.Parse(userDid),
                    SubjectType = ReportSubjectType.Record,
                    Collections = [Nsid.Parse("app.bsky.feed.post")],
                    ReportedAfter = AtDatetime.Parse("2026-09-01T00:00:00.000Z"),
                    IsMuted = true,
                    AssignedTo = Did.Parse(ModDid),
                    SortField = "updatedAt",
                    SortDirection = "asc",
                },
                limit: 25,
                cursor: "c0"),
            "queueId=-1&reportTypes=tools.ozone.report.defs#reasonMisleadingSpam&reportTypes=com.atproto.moderation.defs#reasonSpam" +
            $"&status=open&did={userDid}&subjectType=record&collections=app.bsky.feed.post&reportedAfter=2026-09-01T00:00:00.000Z" +
            $"&isMuted=true&assignedTo={ModDid}&sortField=updatedAt&sortDirection=asc&limit=25&cursor=c0");
        Get("tools.ozone.report.queryReports", """{"reports":[]}""", c => c.Ozone.Report.QueryReportsAsync(ReportStatus.Escalated),
            "status=escalated", name: "tools.ozone.report.queryReports (status only)");
        Post("tools.ozone.report.closeReports", """{"closedCount":2,"reportIds":[42,43]}""",
            c => c.Ozone.Report.CloseReportsAsync(userPost, [ReportReasons.MisleadingSpam], internalNote: "resolved upstream", isAutomated: true),
            $$"""{"subject":"{{userPost}}","reportTypes":["tools.ozone.report.defs#reasonMisleadingSpam"],"internalNote":"resolved upstream","isAutomated":true}""");
        Post("tools.ozone.report.reassignQueue", $$"""{"report":{{report}}}""", c => c.Ozone.Report.ReassignQueueAsync(42, -1, "wrong queue"),
            """{"reportId":42,"queueId":-1,"comment":"wrong queue"}""");
        Post("tools.ozone.report.assignModerator", reportAssignment,
            c => c.Ozone.Report.AssignModeratorAsync(42, Did.Parse(ModDid), queueId: 4, isPermanent: true),
            $$"""{"reportId":42,"did":"{{ModDid}}","queueId":4,"isPermanent":true}""");
        Post("tools.ozone.report.assignModerator", reportAssignment, c => c.Ozone.Report.AssignModeratorAsync(42),
            """{"reportId":42}""", name: "tools.ozone.report.assignModerator (report only)");
        Post("tools.ozone.report.unassignModerator", reportAssignment, c => c.Ozone.Report.UnassignModeratorAsync(42), """{"reportId":42}""");
        Get("tools.ozone.report.getAssignments", $$"""{"cursor":"c1","assignments":[{{reportAssignment}}]}""",
            c => c.Ozone.Report.GetAssignmentsAsync([42, 43], [Did.Parse(ModDid)], onlyActive: false, limit: 5),
            $"onlyActive=false&reportIds=42&reportIds=43&dids={ModDid}&limit=5");
        Post("tools.ozone.report.createActivity", $$"""{"activity":{{activity}}}""",
            c => c.Ozone.Report.CreateActivityForEventAsync(7, new NoteActivity(), internalNote: "seen"),
            """{"eventId":7,"activity":{"$type":"tools.ozone.report.defs#noteActivity"},"internalNote":"seen"}""",
            name: "tools.ozone.report.createActivity (for an event)");
        Get("tools.ozone.report.listActivities", $$"""{"cursor":"c1","activities":[{{activity}}]}""",
            c => c.Ozone.Report.ListActivitiesAsync(42, limit: 10, cursor: "c0"), "reportId=42&limit=10&cursor=c0");
        Get("tools.ozone.report.queryActivities", $$"""{"activities":[{{activity}}]}""",
            c => c.Ozone.Report.QueryActivitiesAsync(["closeActivity", "escalationActivity"],
                createdAfter: AtDatetime.Parse("2026-09-01T00:00:00.000Z"), sortDirection: "asc", limit: 50),
            "activityTypes=closeActivity&activityTypes=escalationActivity&createdAfter=2026-09-01T00:00:00.000Z&sortDirection=asc&limit=50");
        Get("tools.ozone.report.getLiveStats", """
            {"stats":{"pendingCount":12,"actionedCount":30,"escalatedCount":2,"inboundCount":40,"actionRate":75,
                      "avgHandlingTimeSec":300,"lastUpdated":"2026-09-01T12:00:00.000Z"}}
            """,
            c => c.Ozone.Report.GetLiveStatsAsync(4, Did.Parse(ModDid), [ReportReasons.MisleadingSpam]),
            $"queueId=4&moderatorDid={ModDid}&reportTypes=tools.ozone.report.defs#reasonMisleadingSpam");
        Get("tools.ozone.report.getHistoricalStats",
            """{"stats":[{"date":"2026-09-01","computedAt":"2026-09-02T00:00:00.000Z","inboundCount":40}],"cursor":"c1"}""",
            c => c.Ozone.Report.GetHistoricalStatsAsync(startDate: AtDatetime.Parse("2026-08-01T00:00:00.000Z"),
                endDate: AtDatetime.Parse("2026-09-01T00:00:00.000Z"), limit: 30),
            "startDate=2026-08-01T00:00:00.000Z&endDate=2026-09-01T00:00:00.000Z&limit=30");
        Post("tools.ozone.report.refreshStats", "{}",
            c => c.Ozone.Report.RefreshStatsAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), [4, -1]),
            """{"startDate":"2026-08-01","endDate":"2026-08-31","queueIds":[4,-1]}""");

        // ─── tools.ozone.moderation ───

        const string repo = $$$"""{"did":"{{{userDid}}}","handle":"user.example.com","relatedRecords":[],"indexedAt":"2026-09-01T00:00:00.000Z","moderation":{}}""";

        Post("tools.ozone.moderation.emitEvent", $$"""
            {"id":1,"event":{"$type":"tools.ozone.moderation.defs#modEventTakedown","comment":"spam","durationInHours":24},
             "subject":{"$type":"com.atproto.admin.defs#repoRef","did":"did:plc:abc"},"createdBy":"{{ModDid}}","createdAt":"2024-01-01T00:00:00Z"}
            """,
            c => c.Ozone.Moderation.EmitEventAsync(new EmitEventRequest
            {
                Event = new ModEventTakedown { Comment = "spam", DurationInHours = 24 },
                Subject = new RepoSubject { Did = Did.Parse("did:plc:abc") },
                SubjectBlobCids = [Cid.Parse(PostCid)],
                CreatedBy = Did.Parse(ModDid),
            }),
            $$"""
            {"event":{"$type":"tools.ozone.moderation.defs#modEventTakedown","comment":"spam","durationInHours":24},
             "subject":{"$type":"com.atproto.admin.defs#repoRef","did":"did:plc:abc"},"subjectBlobCids":["{{PostCid}}"],"createdBy":"{{ModDid}}"}
            """);
        Get("tools.ozone.moderation.getEvent", $$"""
            {"id":42,"event":{"$type":"tools.ozone.moderation.defs#modEventComment","comment":"test"},
             "subject":{"$type":"com.atproto.admin.defs#repoRef","did":"{{ModDid}}"},"createdBy":"{{ModDid}}","createdAt":"2024-01-01T00:00:00Z"}
            """,
            c => c.Ozone.Moderation.GetEventAsync(42), "id=42");
        Get("tools.ozone.moderation.getRepo", repo, c => c.Ozone.Moderation.GetRepoAsync(Did.Parse(userDid)), $"did={userDid}");
        Get("tools.ozone.moderation.getRecord", $$"""
            {"uri":"{{userPost}}","cid":"{{PostCid}}","value":{},"blobs":[],"indexedAt":"2024-01-01T00:00:00Z","moderation":{},"repo":{{repo}}}
            """,
            c => c.Ozone.Moderation.GetRecordAsync(AtUri.Parse(userPost), Cid.Parse(PostCid)), $"uri={userPost}&cid={PostCid}");
        Get("tools.ozone.moderation.getRecords", $$"""{"records":[{"$type":"tools.ozone.moderation.defs#recordViewNotFound","uri":"{{userPost}}"}]}""",
            c => c.Ozone.Moderation.GetRecordsAsync([AtUri.Parse(userPost), AtUri.Parse(PostUri)]), $"uris={userPost}&uris={PostUri}");
        Get("tools.ozone.moderation.getSubjects", $$"""{"subjects":[{"type":"account","subject":"{{userDid}}","repo":{{repo}}}]}""",
            c => c.Ozone.Moderation.GetSubjectsAsync([userDid, userPost]), $"subjects={userDid}&subjects={userPost}");
        Get("tools.ozone.moderation.getAccountPreferences",
            """{"preferences":[{"$type":"app.bsky.actor.defs#adultContentPref","enabled":true}]}""",
            c => c.Ozone.Moderation.GetAccountPreferencesAsync(Did.Parse(userDid)), $"did={userDid}");
        Get("tools.ozone.moderation.queryEvents", """{"events":[]}""",
            c => c.Ozone.Moderation.QueryEventsAsync(subject: "did:plc:abc", limit: 10, sortDirection: "desc"),
            "subject=did:plc:abc&sortDirection=desc&limit=10");
        Get("tools.ozone.moderation.queryEvents", """{"events":[]}""",
            c => c.Ozone.Moderation.QueryEventsAsync(
                subject: "did:plc:abc",
                createdBy: Did.Parse(ModDid),
                createdAfter: AtDatetime.Parse("2024-01-01T00:00:00Z"),
                createdBefore: AtDatetime.FromDateTimeOffset(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero)),
                types: ["tools.ozone.moderation.defs#modEventTakedown"],
                limit: 5,
                cursor: "c"),
            $"subject=did:plc:abc&createdBy={ModDid}&createdAfter=2024-01-01T00:00:00Z" +
            "&createdBefore=2024-02-01T00:00:00.000Z&types=tools.ozone.moderation.defs#modEventTakedown&limit=5&cursor=c",
            name: "tools.ozone.moderation.queryEvents (typed filters)");
        // Regression: querySubjects never existed; the moderation queue is queryStatuses, with boolean filters.
        Get("tools.ozone.moderation.queryStatuses", """{"subjectStatuses":[]}""",
            c => c.Ozone.Moderation.QueryStatusesAsync(
                new SubjectStatusFilter
                {
                    ReviewState = SubjectReviewState.Escalated,
                    Takendown = true,
                    Appealed = false,
                    Collections = [Nsid.Parse("app.bsky.feed.post")],
                    MinPriorityScore = 5,
                },
                limit: 10),
            "collections=app.bsky.feed.post&reviewState=tools.ozone.moderation.defs#reviewEscalated&takendown=true&appealed=false&minPriorityScore=5&limit=10");
        Get("tools.ozone.moderation.getAccountTimeline", """
            {"timeline":[{"day":"2026-09-01","summary":[
              {"eventSubjectType":"account","eventType":"tools.ozone.moderation.defs#modEventTakedown","count":1}]}]}
            """,
            c => c.Ozone.Moderation.GetAccountTimelineAsync(Did.Parse(ModDid)), $"did={ModDid}");
        Get("tools.ozone.moderation.getReporterStats", $$"""
            {"stats":[{"did":"{{ModDid}}","accountReportCount":1,"recordReportCount":2,"reportedAccountCount":3,
              "reportedRecordCount":4,"takendownAccountCount":5,"takendownRecordCount":6,
              "labeledAccountCount":7,"labeledRecordCount":8}]}
            """,
            c => c.Ozone.Moderation.GetReporterStatsAsync([Did.Parse(ModDid)]), $"dids={ModDid}");
        Post("tools.ozone.moderation.scheduleAction", $$"""
            {"succeeded":["{{userDid}}"],"failed":[{"subject":"{{ModDid}}","error":"Already scheduled","errorCode":"Conflict"}]}
            """,
            c => c.Ozone.Moderation.ScheduleActionAsync(
                [Did.Parse(userDid), Did.Parse(ModDid)],
                new ScheduledTakedown { Comment = "ban wave", StrikeCount = 1, EmailSubject = "Your account" },
                new SchedulingConfig
                {
                    ExecuteAfter = AtDatetime.Parse("2026-10-01T00:00:00.000Z"),
                    ExecuteUntil = AtDatetime.Parse("2026-10-02T00:00:00.000Z"),
                },
                Did.Parse(ModDid),
                new ATProtoNet.Lexicon.Tools.Ozone.Moderation.ModTool { Name = "ozone/workspace" }),
            $$$"""
            {"subjects":["{{{userDid}}}","{{{ModDid}}}"],
             "action":{"$type":"tools.ozone.moderation.scheduleAction#takedown","comment":"ban wave","strikeCount":1,"emailSubject":"Your account"},
             "scheduling":{"executeAfter":"2026-10-01T00:00:00.000Z","executeUntil":"2026-10-02T00:00:00.000Z"},
             "createdBy":"{{{ModDid}}}","modTool":{"name":"ozone/workspace"}}
            """);
        Post("tools.ozone.moderation.listScheduledActions", $$"""
            {"cursor":"c1","actions":[{
              "id":9,"action":"takedown","eventData":{"comment":"ban wave"},"did":"{{userDid}}",
              "executeAt":"2026-10-01T00:00:00.000Z","createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z",
              "status":"executed","lastExecutedAt":"2026-10-01T00:00:01.000Z","executionEventId":77}]}
            """,
            c => c.Ozone.Moderation.ListScheduledActionsAsync(
                [ScheduledActionStatus.Pending, ScheduledActionStatus.Executed],
                subjects: [Did.Parse(userDid)],
                startsAfter: AtDatetime.Parse("2026-09-01T00:00:00.000Z"),
                limit: 10,
                cursor: "c0"),
            $$"""{"statuses":["pending","executed"],"subjects":["{{userDid}}"],"startsAfter":"2026-09-01T00:00:00.000Z","limit":10,"cursor":"c0"}""");
        Post("tools.ozone.moderation.cancelScheduledActions",
            $$"""{"succeeded":["{{userDid}}"],"failed":[{"did":"{{ModDid}}","error":"Nothing pending"}]}""",
            c => c.Ozone.Moderation.CancelScheduledActionsAsync([Did.Parse(userDid), Did.Parse(ModDid)], "appeal granted"),
            $$"""{"subjects":["{{userDid}}","{{ModDid}}"],"comment":"appeal granted"}""");

        // ─── tools.ozone.communication / .team / .set / .server / .signature ───

        const string template =
            $$"""{"id":"tmpl-1","name":"Warning","contentMarkdown":"You violated...","disabled":false,"lastUpdatedBy":"{{ModDid}}","createdAt":"2024-01-01T00:00:00Z","updatedAt":"2024-01-01T00:00:00Z"}""";
        Post("tools.ozone.communication.createTemplate", template,
            c => c.Ozone.Communication.CreateTemplateAsync(new ATProtoNet.Lexicon.Tools.Ozone.Communication.CreateTemplateRequest
            {
                Name = "Warning",
                ContentMarkdown = "You violated...",
                Subject = "Policy Warning",
            }),
            """{"name":"Warning","contentMarkdown":"You violated...","subject":"Policy Warning"}""");
        Get("tools.ozone.communication.listTemplates", $$"""{"communicationTemplates":[{{template}}]}""",
            c => c.Ozone.Communication.ListTemplatesAsync());

        Post("tools.ozone.team.addMember", """{"did":"did:plc:newmod","role":"tools.ozone.team.defs#roleModerator"}""",
            c => c.Ozone.Team.AddMemberAsync(new AddMemberRequest { Did = Did.Parse("did:plc:newmod"), Role = TeamMemberRole.Moderator }),
            """{"did":"did:plc:newmod","role":"tools.ozone.team.defs#roleModerator"}""");
        // lastUpdatedBy stays a string: Ozone writes "admin_token" there, not a DID, for changes made with the admin password.
        Get("tools.ozone.team.listMembers",
            """{"members":[{"did":"did:plc:admin","role":"tools.ozone.team.defs#roleAdmin","lastUpdatedBy":"admin_token"}],"cursor":"next-page"}""",
            c => c.Ozone.Team.ListMembersAsync(limit: 25), "limit=25");

        const string set = """{"name":"bad-words","setSize":0,"createdAt":"2024-01-01T00:00:00Z","updatedAt":"2024-01-01T00:00:00Z"}""";
        Post("tools.ozone.set.upsertSet", set,
            c => c.Ozone.Set.UpsertSetAsync(
                new ATProtoNet.Lexicon.Tools.Ozone.Set.UpsertSetRequest { Name = "bad-words", Description = "Known bad words" }),
            """{"name":"bad-words","description":"Known bad words"}""");
        Get("tools.ozone.set.getValues", $$"""{"set":{{set}},"values":["word1","word2"]}""",
            c => c.Ozone.Set.GetValuesAsync("bad-words"), "name=bad-words");

        Get("tools.ozone.server.getConfig",
            """{"appview":{"url":"https://api.bsky.app"},"pds":{"url":"https://pds.example.com"},"viewer":{"role":"tools.ozone.team.defs#roleAdmin"}}""",
            c => c.Ozone.Server.GetConfigAsync());

        Get("tools.ozone.signature.findRelatedAccounts", """{"accounts":[]}""",
            c => c.Ozone.Signature.FindRelatedAccountsAsync(Did.Parse(ModDid), 10, "c"), $"did={ModDid}&cursor=c&limit=10");
        Get("tools.ozone.signature.searchAccounts",
            $$"""{"accounts":[{"did":"{{ModDid}}","handle":"mod.example.com","indexedAt":"2026-09-01T00:00:00.000Z"}]}""",
            c => c.Ozone.Signature.SearchAccountsAsync(["192.0.2.1", "device-7"], 10, "c"), "values=192.0.2.1&values=device-7&limit=10&cursor=c");

        // ─── tools.ozone.setting ───

        const string option = $$"""
            {"key":"tools.ozone.setting.client.queues","did":"{{ModDid}}","value":{"order":["spam"]},
             "managerRole":"tools.ozone.team.defs#roleAdmin","scope":"instance","createdBy":"{{ModDid}}","lastUpdatedBy":"{{ModDid}}"}
            """;
        Get("tools.ozone.setting.listOptions", $$"""{"cursor":"c1","options":[{{option}}]}""",
            c => c.Ozone.Setting.ListOptionsAsync(SettingScope.Personal,
                keys: [Nsid.Parse("tools.ozone.setting.client.queues"), Nsid.Parse("tools.ozone.setting.client.tags")], limit: 10),
            "limit=10&scope=personal&keys=tools.ozone.setting.client.queues&keys=tools.ozone.setting.client.tags");
        Post("tools.ozone.setting.upsertOption", $$"""{"option":{{option}}}""",
            c => c.Ozone.Setting.UpsertOptionAsync(Nsid.Parse("tools.ozone.setting.client.queues"), SettingScope.Instance,
                System.Text.Json.JsonSerializer.SerializeToElement(new { order = new[] { "spam" } }),
                description: "Queue order", managerRole: TeamMemberRole.Admin),
            """{"key":"tools.ozone.setting.client.queues","scope":"instance","value":{"order":["spam"]},"description":"Queue order","managerRole":"tools.ozone.team.defs#roleAdmin"}""");
        Post("tools.ozone.setting.removeOptions", "{}",
            c => c.Ozone.Setting.RemoveOptionsAsync([Nsid.Parse("tools.ozone.setting.client.queues")], SettingScope.Personal),
            """{"keys":["tools.ozone.setting.client.queues"],"scope":"personal"}""");

        // ─── tools.ozone.safelink: its listings are procedures, so filters and cursor travel in the body ───

        const string safelinkEvent = $$"""
            {"id":3,"eventType":"addRule","url":"scam.example.com","pattern":"domain","action":"block","reason":"phishing",
             "createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z","comment":"reported"}
            """;
        Post("tools.ozone.safelink.addRule", safelinkEvent,
            c => c.Ozone.Safelink.AddRuleAsync("scam.example.com", SafelinkPatternType.Domain, SafelinkActionType.Block,
                SafelinkReasonType.Phishing, comment: "reported"),
            """{"url":"scam.example.com","pattern":"domain","action":"block","reason":"phishing","comment":"reported"}""");
        Post("tools.ozone.safelink.updateRule", safelinkEvent,
            c => c.Ozone.Safelink.UpdateRuleAsync("https://scam.example.com/x", SafelinkPatternType.Url, SafelinkActionType.Warn,
                SafelinkReasonType.Spam, createdBy: Did.Parse(ModDid)),
            $$"""{"url":"https://scam.example.com/x","pattern":"url","action":"warn","reason":"spam","createdBy":"{{ModDid}}"}""");
        Post("tools.ozone.safelink.removeRule", safelinkEvent,
            c => c.Ozone.Safelink.RemoveRuleAsync("scam.example.com", SafelinkPatternType.Domain, "false positive"),
            """{"url":"scam.example.com","pattern":"domain","comment":"false positive"}""");
        Post("tools.ozone.safelink.queryRules", $$"""
            {"cursor":"c1","rules":[{"url":"scam.example.com","pattern":"domain","action":"block","reason":"phishing",
              "createdBy":"{{ModDid}}","createdAt":"2026-09-01T00:00:00.000Z","updatedAt":"2026-09-02T00:00:00.000Z"}]}
            """,
            c => c.Ozone.Safelink.QueryRulesAsync(urls: ["scam.example.com"], actions: [SafelinkActionType.Block, SafelinkActionType.Warn],
                createdBy: Did.Parse(ModDid), limit: 10, cursor: "c0"),
            $$"""{"urls":["scam.example.com"],"actions":["block","warn"],"createdBy":"{{ModDid}}","limit":10,"cursor":"c0"}""");
        Post("tools.ozone.safelink.queryEvents", $$"""{"events":[{{safelinkEvent}}]}""",
            c => c.Ozone.Safelink.QueryEventsAsync(patternType: SafelinkPatternType.Domain, sortDirection: "asc", limit: 5, cursor: "c0"),
            """{"limit":5,"cursor":"c0","patternType":"domain","sortDirection":"asc"}""");

        // ─── tools.ozone.verification ───

        const string verificationUri = $"at://{ModDid}/app.bsky.graph.verification/3lndpmyzszx2b";
        Post("tools.ozone.verification.grantVerifications", $$"""
            {"verifications":[{"issuer":"{{ModDid}}","uri":"{{verificationUri}}","subject":"{{userDid}}","handle":"user.example.com",
              "displayName":"User","createdAt":"2026-09-01T00:00:00.000Z"}],
             "failedVerifications":[{"error":"Handle mismatch","subject":"{{ModDid}}"}]}
            """,
            c => c.Ozone.Verification.GrantVerificationsAsync(
            [
                new VerificationInput { Subject = Did.Parse(userDid), Handle = Handle.Parse("user.example.com"), DisplayName = "User" },
                new VerificationInput
                {
                    Subject = Did.Parse(ModDid),
                    Handle = Handle.Parse("mod.example.com"),
                    DisplayName = "Mod",
                    CreatedAt = AtDatetime.Parse("2026-09-01T00:00:00.000Z"),
                },
            ]),
            $$"""
            {"verifications":[{"subject":"{{userDid}}","handle":"user.example.com","displayName":"User"},
              {"subject":"{{ModDid}}","handle":"mod.example.com","displayName":"Mod","createdAt":"2026-09-01T00:00:00.000Z"}]}
            """);
        Post("tools.ozone.verification.revokeVerifications", $$"""{"revokedVerifications":["{{verificationUri}}"],"failedRevocations":[]}""",
            c => c.Ozone.Verification.RevokeVerificationsAsync([AtUri.Parse(verificationUri)], "handle changed"),
            $$"""{"uris":["{{verificationUri}}"],"revokeReason":"handle changed"}""");
        Get("tools.ozone.verification.listVerifications", $$"""
            {"verifications":[{"issuer":"{{ModDid}}","uri":"{{verificationUri}}","subject":"{{userDid}}",
              "handle":"user.example.com","displayName":"User","createdAt":"2026-09-01T00:00:00.000Z",
              "revokeReason":"x","revokedAt":"2026-09-02T00:00:00.000Z","revokedBy":"{{ModDid}}"}]}
            """,
            c => c.Ozone.Verification.ListVerificationsAsync(subjects: [Did.Parse(userDid)], issuers: [Did.Parse(ModDid)],
                createdAfter: AtDatetime.Parse("2026-08-01T00:00:00.000Z"), isRevoked: true, limit: 10),
            $"limit=10&createdAfter=2026-08-01T00:00:00.000Z&issuers={ModDid}&subjects={userDid}&isRevoked=true");

        // ─── site.standard: com.atproto.repo CRUD on each record's own collection ───

        var test = Did.Parse("did:plc:test");
        var written = $$"""{"uri":"at://did:plc:test/site.standard.publication/abc","cid":"{{PostCid}}"}""";
        var deleted = $$$"""{"commit":{"cid":"{{{PostCid}}}","rev":"3jzfcijpj2z2a"}}""";
        const string publication = """{"$type":"site.standard.publication","url":"https://myblog.example.com","name":"My Blog"}""";
        const string document =
            """{"$type":"site.standard.document","site":"https://myblog.example.com","title":"My First Post","publishedAt":"2024-01-20T14:30:00.000Z"}""";
        const string subscription = """{"$type":"site.standard.graph.subscription","publication":"at://did:plc:author/site.standard.publication/abc"}""";
        const string recommend =
            """{"$type":"site.standard.graph.recommend","document":"at://did:plc:author/site.standard.document/doc1","createdAt":"2026-09-25T10:00:00.000Z"}""";
        var publicationRecord = new ATProtoNet.Lexicon.Site.Standard.Publication.PublicationRecord { Url = "https://myblog.example.com", Name = "My Blog" };
        var documentRecord = new ATProtoNet.Lexicon.Site.Standard.Document.DocumentRecord
        {
            Site = "https://myblog.example.com",
            Title = "My First Post",
            PublishedAt = AtDatetime.Parse("2024-01-20T14:30:00.000Z"),
        };

        void SiteRecord(string collection, string record, string rkey,
            Func<AtProtoClient, Task> create, Func<AtProtoClient, Task> get, Func<AtProtoClient, Task> list,
            Func<AtProtoClient, Task> delete, Func<AtProtoClient, Task>? put = null)
        {
            Post("com.atproto.repo.createRecord", written, create,
                $$"""{"repo":"did:plc:test","collection":"{{collection}}","record":{{record}}}""", name: $"{collection} (create)");
            Get("com.atproto.repo.getRecord", $$"""{"uri":"at://did:plc:test/{{collection}}/{{rkey}}","value":{{record}}}""", get,
                $"repo=did:plc:test&collection={collection}&rkey={rkey}", name: $"{collection} (get)");
            Get("com.atproto.repo.listRecords", $$"""{"records":[{"uri":"at://did:plc:test/{{collection}}/{{rkey}}","cid":"{{PostCid}}","value":{{record}}}]}""", list,
                $"repo=did:plc:test&collection={collection}&limit=10", name: $"{collection} (list)");
            Post("com.atproto.repo.deleteRecord", deleted, delete,
                $$"""{"repo":"did:plc:test","collection":"{{collection}}","rkey":"{{rkey}}"}""", name: $"{collection} (delete)");
            if (put is not null)
            {
                Post("com.atproto.repo.putRecord", written, put,
                    $$"""{"repo":"did:plc:test","collection":"{{collection}}","rkey":"{{rkey}}","record":{{record}}}""", name: $"{collection} (put)");
            }
        }

        SiteRecord("site.standard.publication", publication, "abc",
            c => c.Site.CreatePublicationAsync(test, publicationRecord),
            c => c.Site.GetPublicationAsync(AtUri.Parse("at://did:plc:test/site.standard.publication/abc")),
            c => c.Site.ListPublicationsAsync(test, limit: 10),
            c => c.Site.DeletePublicationAsync(test, RecordKey.Parse("abc")),
            c => c.Site.PutPublicationAsync(test, RecordKey.Parse("abc"), publicationRecord));
        SiteRecord("site.standard.document", document, "doc1",
            c => c.Site.CreateDocumentAsync(test, documentRecord),
            c => c.Site.GetDocumentAsync(test, RecordKey.Parse("doc1")),
            c => c.Site.ListDocumentsAsync(test, limit: 10),
            c => c.Site.DeleteDocumentAsync(test, RecordKey.Parse("doc1")),
            c => c.Site.PutDocumentAsync(test, RecordKey.Parse("doc1"), documentRecord));
        SiteRecord("site.standard.graph.subscription", subscription, "s1",
            c => c.Site.CreateSubscriptionAsync(test, new ATProtoNet.Lexicon.Site.Standard.Graph.SubscriptionRecord
            {
                Publication = AtUri.Parse("at://did:plc:author/site.standard.publication/abc"),
            }),
            c => c.Site.GetSubscriptionAsync(test, RecordKey.Parse("s1")),
            c => c.Site.ListSubscriptionsAsync(test, limit: 10),
            c => c.Site.DeleteSubscriptionAsync(test, RecordKey.Parse("s1")));
        SiteRecord("site.standard.graph.recommend", recommend, "r1",
            c => c.Site.CreateRecommendationAsync(test, new ATProtoNet.Lexicon.Site.Standard.Graph.RecommendRecord
            {
                Document = AtUri.Parse("at://did:plc:author/site.standard.document/doc1"),
                CreatedAt = AtDatetime.Parse("2026-09-25T10:00:00.000Z"),
            }),
            c => c.Site.GetRecommendationAsync(AtUri.Parse("at://did:plc:test/site.standard.graph.recommend/r1")),
            c => c.Site.ListRecommendationsAsync(test, limit: 10),
            c => c.Site.DeleteRecommendationAsync(test, RecordKey.Parse("r1")));

        return data;
    }
}
