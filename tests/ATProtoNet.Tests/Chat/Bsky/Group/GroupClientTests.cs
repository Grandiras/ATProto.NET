using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
using ATProtoNet.Lexicon.Chat.Bsky.Group;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Chat.Bsky.Group;

/// <summary>
/// One test per <c>chat.bsky.group.*</c> method: the request it sends (HTTP method, NSID,
/// parameters or body, chat proxy) and how it reads the answer. The chat service is not open
/// source, so the responses follow the vendored Lexicons field by field.
/// </summary>
public class GroupClientTests : IDisposable
{
    private const string GroupConvoJson =
        """
        {"id":"convo-1","rev":"2222222222225","members":[{"did":"did:plc:owner","handle":"owner.bsky.social",
          "kind":{"$type":"chat.bsky.actor.defs#groupConvoMember","role":"owner"}}],"muted":false,"unreadCount":0,"status":"accepted",
         "kind":{"$type":"chat.bsky.convo.defs#groupConvo","name":"Book club","memberCount":1,"memberLimit":100,
                 "lockStatus":"unlocked","lockStatusModerationOverride":false,"createdAt":"2026-06-01T12:00:00.000Z"}}
        """;

    private const string JoinLinkJson =
        """
        {"code":"abc123","enabledStatus":"enabled","requireApproval":true,"joinRule":"followedByOwner",
         "createdAt":"2026-06-01T12:00:10.000Z"}
        """;

    private static readonly Did Alice = Did.Parse("did:plc:alice");
    private static readonly Did Bob = Did.Parse("did:plc:bob");

    private readonly XrpcTestClient _fixture = new();

    private GroupClient Group => _fixture.Client.Chat.Group;

    public void Dispose() => _fixture.Dispose();

    // ──────────────────────────────────────────────────────────
    //  Groups and membership
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateGroupAsync_PostsNameAndMembers_ReturnsTheGroup()
    {
        _fixture.On("chat.bsky.group.createGroup", $$"""{"convo":{{GroupConvoJson}}}""");

        var convo = await Group.CreateGroupAsync("Book club", [Alice, Bob]);

        AssertPost("chat.bsky.group.createGroup", """{"members":["did:plc:alice","did:plc:bob"],"name":"Book club"}""");
        Assert.Equal("Book club", Assert.IsType<GroupConvo>(convo.Kind).Name);
    }

    [Fact]
    public async Task EditGroupAsync_PostsTheNewName_ReturnsTheGroup()
    {
        _fixture.On("chat.bsky.group.editGroup", $$"""{"convo":{{GroupConvoJson}}}""");

        var convo = await Group.EditGroupAsync("convo-1", "Book club");

        AssertPost("chat.bsky.group.editGroup", """{"convoId":"convo-1","name":"Book club"}""");
        Assert.Equal("convo-1", convo.Id);
    }

    [Fact]
    public async Task AddMembersAsync_PostsTheMembers_ReadsTheAddedProfiles()
    {
        _fixture.On("chat.bsky.group.addMembers",
            $$"""{"convo":{{GroupConvoJson}},"addedMembers":[{"did":"did:plc:alice","handle":"alice.bsky.social"}]}""");

        var result = await Group.AddMembersAsync("convo-1", [Alice]);

        AssertPost("chat.bsky.group.addMembers", """{"convoId":"convo-1","members":["did:plc:alice"]}""");
        Assert.Equal("convo-1", result.Convo.Id);
        Assert.Equal(Alice, Assert.Single(result.AddedMembers!).Did);
    }

    [Fact]
    public async Task RemoveMembersAsync_PostsTheMembers_ReturnsTheGroup()
    {
        _fixture.On("chat.bsky.group.removeMembers", $$"""{"convo":{{GroupConvoJson}}}""");

        var convo = await Group.RemoveMembersAsync("convo-1", [Alice, Bob]);

        AssertPost("chat.bsky.group.removeMembers", """{"convoId":"convo-1","members":["did:plc:alice","did:plc:bob"]}""");
        Assert.Equal(1, Assert.IsType<GroupConvo>(convo.Kind).MemberCount);
    }

    [Fact]
    public async Task ListMutualGroupsAsync_SendsTheSubjectAndPaging_ReadsThePage()
    {
        _fixture.On("chat.bsky.group.listMutualGroups", $$"""{"cursor":"next","convos":[{{GroupConvoJson}}]}""");

        var page = await Group.ListMutualGroupsAsync(Alice, limit: 10, cursor: "abc");

        AssertGet("chat.bsky.group.listMutualGroups", "subject=did:plc:alice&limit=10&cursor=abc");
        Assert.Equal("next", page.Cursor);
        Assert.Equal("convo-1", Assert.Single(page.Convos).Id);
    }

    // ──────────────────────────────────────────────────────────
    //  Join links
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateJoinLinkAsync_PostsTheRuleAndApproval_ReturnsTheLink()
    {
        _fixture.On("chat.bsky.group.createJoinLink", $$"""{"joinLink":{{JoinLinkJson}}}""");

        var link = await Group.CreateJoinLinkAsync("convo-1", JoinRule.FollowedByOwner, requireApproval: true);

        AssertPost("chat.bsky.group.createJoinLink", """{"convoId":"convo-1","requireApproval":true,"joinRule":"followedByOwner"}""");
        Assert.Equal(("abc123", JoinLinkEnabledStatus.Enabled, JoinRule.FollowedByOwner), (link.Code, link.EnabledStatus, link.JoinRule));
        Assert.True(link.RequireApproval);
        Assert.Equal(AtDatetime.Parse("2026-06-01T12:00:10.000Z"), link.CreatedAt);
    }

    [Fact]
    public async Task CreateJoinLinkAsync_WithoutApproval_LeavesItToTheServer()
    {
        _fixture.On("chat.bsky.group.createJoinLink", $$"""{"joinLink":{{JoinLinkJson}}}""");

        await Group.CreateJoinLinkAsync("convo-1", JoinRule.Anyone);

        AssertPost("chat.bsky.group.createJoinLink", """{"convoId":"convo-1","joinRule":"anyone"}""");
    }

    [Fact]
    public async Task EditJoinLinkAsync_OnlyTheChangedSetting_SendsOnlyIt()
    {
        _fixture.On("chat.bsky.group.editJoinLink", $$"""{"joinLink":{{JoinLinkJson}}}""");

        var link = await Group.EditJoinLinkAsync("convo-1", requireApproval: false);

        AssertPost("chat.bsky.group.editJoinLink", """{"convoId":"convo-1","requireApproval":false}""");
        Assert.Equal("abc123", link.Code);
    }

    [Theory]
    [InlineData("enableJoinLink")]
    [InlineData("disableJoinLink")]
    public async Task EnableAndDisableJoinLinkAsync_PostTheConvoId_ReturnTheLink(string method)
    {
        var nsid = $"chat.bsky.group.{method}";
        _fixture.On(nsid, $$"""{"joinLink":{{JoinLinkJson}}}""");

        var link = method == "enableJoinLink"
            ? await Group.EnableJoinLinkAsync("convo-1")
            : await Group.DisableJoinLinkAsync("convo-1");

        AssertPost(nsid, """{"convoId":"convo-1"}""");
        Assert.Equal("abc123", link.Code);
    }

    [Fact]
    public async Task GetJoinLinkPreviewsAsync_SendsEachCode_ReadsEachPreviewKind()
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

        var result = await Group.GetJoinLinkPreviewsAsync(["abc123", "off456", "nope"]);

        AssertGet("chat.bsky.group.getJoinLinkPreviews", "codes=abc123&codes=off456&codes=nope");
        Assert.Collection(result.JoinLinkPreviews,
            p => Assert.Equal(12, Assert.IsType<JoinLinkPreviewView>(p).MemberCount),
            p => Assert.Equal("off456", Assert.IsType<DisabledJoinLinkPreviewView>(p).Code),
            p => Assert.Equal("nope", Assert.IsType<InvalidJoinLinkPreviewView>(p).Code));
    }

    // ──────────────────────────────────────────────────────────
    //  Join requests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestJoinAsync_LinkWithoutApproval_ReturnsTheJoinedGroup()
    {
        _fixture.On("chat.bsky.group.requestJoin", $$"""{"status":"joined","convo":{{GroupConvoJson}}}""");

        var result = await Group.RequestJoinAsync("abc123");

        AssertPost("chat.bsky.group.requestJoin", """{"code":"abc123"}""");
        Assert.Equal(RequestJoinStatus.Joined, result.Status);
        Assert.Equal("convo-1", result.Convo!.Id);
    }

    [Fact]
    public async Task RequestJoinAsync_LinkNeedingApproval_IsPending()
    {
        _fixture.On("chat.bsky.group.requestJoin", """{"status":"pending"}""");

        var result = await Group.RequestJoinAsync("abc123");

        Assert.Equal(RequestJoinStatus.Pending, result.Status);
        Assert.Null(result.Convo);
    }

    // WithdrawJoinRequestAsync, RejectJoinRequestAsync and UpdateJoinRequestsReadAsync (all
    // ack-only) are covered by ATProtoNet.Tests.Lexicon.EndpointRequestTests.

    [Fact]
    public async Task ListJoinRequestsAsync_SendsTheConvoAndPaging_ReadsTheRequests()
    {
        _fixture.On("chat.bsky.group.listJoinRequests",
            """
            {"cursor":"next","requests":[{"convoId":"convo-1","requestedAt":"2026-06-01T12:05:00.000Z",
              "requestedBy":{"did":"did:plc:alice","handle":"alice.bsky.social"}}]}
            """);

        var page = await Group.ListJoinRequestsAsync("convo-1", limit: 5, cursor: "abc");

        AssertGet("chat.bsky.group.listJoinRequests", "convoId=convo-1&limit=5&cursor=abc");
        var request = Assert.Single(page.Requests);
        Assert.Equal(Alice, request.RequestedBy.Did);
        Assert.Equal(AtDatetime.Parse("2026-06-01T12:05:00.000Z"), request.RequestedAt);
    }

    [Fact]
    public async Task ApproveJoinRequestAsync_PostsTheMember_ReturnsTheGroup()
    {
        _fixture.On("chat.bsky.group.approveJoinRequest", $$"""{"convo":{{GroupConvoJson}}}""");

        var convo = await Group.ApproveJoinRequestAsync("convo-1", Alice);

        AssertPost("chat.bsky.group.approveJoinRequest", """{"convoId":"convo-1","member":"did:plc:alice"}""");
        Assert.Equal("convo-1", convo.Id);
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private void AssertPost(string nsid, string expectedBody) => _fixture.AssertPost(nsid, expectedBody, ServiceProxy.BskyChatHeader);

    private void AssertGet(string nsid, string expectedQuery) => _fixture.AssertGet(nsid, expectedQuery, ServiceProxy.BskyChatHeader);
}
