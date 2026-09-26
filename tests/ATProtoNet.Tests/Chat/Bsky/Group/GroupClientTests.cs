using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
using ATProtoNet.Lexicon.Chat.Bsky.Group;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

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

    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly GroupClient _group;

    public GroupClientTests()
    {
        _httpClient = new HttpClient(_stub) { BaseAddress = new Uri("https://pds.example.com/") };
        var xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        xrpc.SetTokens("test-token");
        _group = new GroupClient(xrpc);
    }

    // ──────────────────────────────────────────────────────────
    //  Groups and membership
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateGroupAsync_PostsNameAndMembers_ReturnsTheGroup()
    {
        _stub.On("chat.bsky.group.createGroup", $$"""{"convo":{{GroupConvoJson}}}""");

        var convo = await _group.CreateGroupAsync("Book club", [Alice, Bob]);

        AssertPost("chat.bsky.group.createGroup", """{"members":["did:plc:alice","did:plc:bob"],"name":"Book club"}""");
        Assert.Equal("Book club", Assert.IsType<GroupConvo>(convo.Kind).Name);
    }

    [Fact]
    public async Task EditGroupAsync_PostsTheNewName_ReturnsTheGroup()
    {
        _stub.On("chat.bsky.group.editGroup", $$"""{"convo":{{GroupConvoJson}}}""");

        var convo = await _group.EditGroupAsync("convo-1", "Book club");

        AssertPost("chat.bsky.group.editGroup", """{"convoId":"convo-1","name":"Book club"}""");
        Assert.Equal("convo-1", convo.Id);
    }

    [Fact]
    public async Task AddMembersAsync_PostsTheMembers_ReadsTheAddedProfiles()
    {
        _stub.On("chat.bsky.group.addMembers",
            $$"""{"convo":{{GroupConvoJson}},"addedMembers":[{"did":"did:plc:alice","handle":"alice.bsky.social"}]}""");

        var result = await _group.AddMembersAsync("convo-1", [Alice]);

        AssertPost("chat.bsky.group.addMembers", """{"convoId":"convo-1","members":["did:plc:alice"]}""");
        Assert.Equal("convo-1", result.Convo.Id);
        Assert.Equal(Alice, Assert.Single(result.AddedMembers!).Did);
    }

    [Fact]
    public async Task RemoveMembersAsync_PostsTheMembers_ReturnsTheGroup()
    {
        _stub.On("chat.bsky.group.removeMembers", $$"""{"convo":{{GroupConvoJson}}}""");

        var convo = await _group.RemoveMembersAsync("convo-1", [Alice, Bob]);

        AssertPost("chat.bsky.group.removeMembers", """{"convoId":"convo-1","members":["did:plc:alice","did:plc:bob"]}""");
        Assert.Equal(1, Assert.IsType<GroupConvo>(convo.Kind).MemberCount);
    }

    [Fact]
    public async Task ListMutualGroupsAsync_SendsTheSubjectAndPaging_ReadsThePage()
    {
        _stub.On("chat.bsky.group.listMutualGroups", $$"""{"cursor":"next","convos":[{{GroupConvoJson}}]}""");

        var page = await _group.ListMutualGroupsAsync(Alice, limit: 10, cursor: "abc");

        AssertGet("chat.bsky.group.listMutualGroups", "subject=did:plc:alice&limit=10&cursor=abc");
        Assert.Equal("next", page.Cursor);
        Assert.Equal("convo-1", Assert.Single(page.Convos).Id);
    }

    [Fact]
    public async Task EnumerateMutualGroupsAsync_WalksPages()
    {
        _stub.On("chat.bsky.group.listMutualGroups", $$"""{"cursor":"page-2","convos":[{{GroupConvoJson}}]}""");
        _stub.On("chat.bsky.group.listMutualGroups", $$"""{"convos":[{{GroupConvoJson}}]}""");

        var convos = await _group.EnumerateMutualGroupsAsync(Alice, pageSize: 1).ToListAsync();

        Assert.Equal(2, convos.Count);
        Assert.Equal(
            ["?subject=did:plc:alice&limit=1", "?subject=did:plc:alice&limit=1&cursor=page-2"],
            _stub.To("chat.bsky.group.listMutualGroups").Select(r => $"?{Uri.UnescapeDataString(r.Query)}"));
    }

    // ──────────────────────────────────────────────────────────
    //  Join links
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateJoinLinkAsync_PostsTheRuleAndApproval_ReturnsTheLink()
    {
        _stub.On("chat.bsky.group.createJoinLink", $$"""{"joinLink":{{JoinLinkJson}}}""");

        var link = await _group.CreateJoinLinkAsync("convo-1", JoinRule.FollowedByOwner, requireApproval: true);

        AssertPost("chat.bsky.group.createJoinLink", """{"convoId":"convo-1","requireApproval":true,"joinRule":"followedByOwner"}""");
        Assert.Equal(("abc123", JoinLinkEnabledStatus.Enabled, JoinRule.FollowedByOwner), (link.Code, link.EnabledStatus, link.JoinRule));
        Assert.True(link.RequireApproval);
        Assert.Equal(AtDatetime.Parse("2026-06-01T12:00:10.000Z"), link.CreatedAt);
    }

    [Fact]
    public async Task CreateJoinLinkAsync_WithoutApproval_LeavesItToTheServer()
    {
        _stub.On("chat.bsky.group.createJoinLink", $$"""{"joinLink":{{JoinLinkJson}}}""");

        await _group.CreateJoinLinkAsync("convo-1", JoinRule.Anyone);

        AssertPost("chat.bsky.group.createJoinLink", """{"convoId":"convo-1","joinRule":"anyone"}""");
    }

    [Fact]
    public async Task EditJoinLinkAsync_OnlyTheChangedSetting_SendsOnlyIt()
    {
        _stub.On("chat.bsky.group.editJoinLink", $$"""{"joinLink":{{JoinLinkJson}}}""");

        var link = await _group.EditJoinLinkAsync("convo-1", requireApproval: false);

        AssertPost("chat.bsky.group.editJoinLink", """{"convoId":"convo-1","requireApproval":false}""");
        Assert.Equal("abc123", link.Code);
    }

    [Theory]
    [InlineData("enableJoinLink")]
    [InlineData("disableJoinLink")]
    public async Task EnableAndDisableJoinLinkAsync_PostTheConvoId_ReturnTheLink(string method)
    {
        var nsid = $"chat.bsky.group.{method}";
        _stub.On(nsid, $$"""{"joinLink":{{JoinLinkJson}}}""");

        var link = method == "enableJoinLink"
            ? await _group.EnableJoinLinkAsync("convo-1")
            : await _group.DisableJoinLinkAsync("convo-1");

        AssertPost(nsid, """{"convoId":"convo-1"}""");
        Assert.Equal("abc123", link.Code);
    }

    [Fact]
    public async Task GetJoinLinkPreviewsAsync_SendsEachCode_ReadsEachPreviewKind()
    {
        _stub.On("chat.bsky.group.getJoinLinkPreviews",
            """
            {"joinLinkPreviews":[
              {"$type":"chat.bsky.group.defs#joinLinkPreviewView","convoId":"convo-1","code":"abc123","name":"Book club",
               "owner":{"did":"did:plc:owner","handle":"owner.bsky.social"},"memberCount":12,"memberLimit":100,
               "requireApproval":false,"joinRule":"anyone"},
              {"$type":"chat.bsky.group.defs#disabledJoinLinkPreviewView","code":"off456"},
              {"$type":"chat.bsky.group.defs#invalidJoinLinkPreviewView","code":"nope"}]}
            """);

        var result = await _group.GetJoinLinkPreviewsAsync(["abc123", "off456", "nope"]);

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
        _stub.On("chat.bsky.group.requestJoin", $$"""{"status":"joined","convo":{{GroupConvoJson}}}""");

        var result = await _group.RequestJoinAsync("abc123");

        AssertPost("chat.bsky.group.requestJoin", """{"code":"abc123"}""");
        Assert.Equal(RequestJoinStatus.Joined, result.Status);
        Assert.Equal("convo-1", result.Convo!.Id);
    }

    [Fact]
    public async Task RequestJoinAsync_LinkNeedingApproval_IsPending()
    {
        _stub.On("chat.bsky.group.requestJoin", """{"status":"pending"}""");

        var result = await _group.RequestJoinAsync("abc123");

        Assert.Equal(RequestJoinStatus.Pending, result.Status);
        Assert.Null(result.Convo);
    }

    [Fact]
    public async Task WithdrawJoinRequestAsync_PostsTheConvoId()
    {
        _stub.On("chat.bsky.group.withdrawJoinRequest", "{}");

        await _group.WithdrawJoinRequestAsync("convo-1");

        AssertPost("chat.bsky.group.withdrawJoinRequest", """{"convoId":"convo-1"}""");
    }

    [Fact]
    public async Task ListJoinRequestsAsync_SendsTheConvoAndPaging_ReadsTheRequests()
    {
        _stub.On("chat.bsky.group.listJoinRequests",
            """
            {"cursor":"next","requests":[{"convoId":"convo-1","requestedAt":"2026-06-01T12:05:00.000Z",
              "requestedBy":{"did":"did:plc:alice","handle":"alice.bsky.social"}}]}
            """);

        var page = await _group.ListJoinRequestsAsync("convo-1", limit: 5, cursor: "abc");

        AssertGet("chat.bsky.group.listJoinRequests", "convoId=convo-1&limit=5&cursor=abc");
        var request = Assert.Single(page.Requests);
        Assert.Equal(Alice, request.RequestedBy.Did);
        Assert.Equal(AtDatetime.Parse("2026-06-01T12:05:00.000Z"), request.RequestedAt);
    }

    [Fact]
    public async Task EnumerateJoinRequestsAsync_WalksPages()
    {
        _stub.On("chat.bsky.group.listJoinRequests",
            """
            {"cursor":"page-2","requests":[{"convoId":"convo-1","requestedAt":"2026-06-01T12:05:00.000Z",
              "requestedBy":{"did":"did:plc:alice","handle":"x.bsky.social"}}]}
            """);
        _stub.On("chat.bsky.group.listJoinRequests",
            """
            {"requests":[{"convoId":"convo-1","requestedAt":"2026-06-01T12:05:00.000Z",
              "requestedBy":{"did":"did:plc:bob","handle":"x.bsky.social"}}]}
            """);

        var requests = await _group.EnumerateJoinRequestsAsync("convo-1", pageSize: 1).ToListAsync();

        Assert.Equal([Alice, Bob], requests.Select(r => r.RequestedBy.Did));
        Assert.Equal(
            ["?convoId=convo-1&limit=1", "?convoId=convo-1&limit=1&cursor=page-2"],
            _stub.To("chat.bsky.group.listJoinRequests").Select(r => $"?{r.Query}"));
    }

    [Fact]
    public async Task ApproveJoinRequestAsync_PostsTheMember_ReturnsTheGroup()
    {
        _stub.On("chat.bsky.group.approveJoinRequest", $$"""{"convo":{{GroupConvoJson}}}""");

        var convo = await _group.ApproveJoinRequestAsync("convo-1", Alice);

        AssertPost("chat.bsky.group.approveJoinRequest", """{"convoId":"convo-1","member":"did:plc:alice"}""");
        Assert.Equal("convo-1", convo.Id);
    }

    [Fact]
    public async Task RejectJoinRequestAsync_PostsTheMember()
    {
        _stub.On("chat.bsky.group.rejectJoinRequest", "{}");

        await _group.RejectJoinRequestAsync("convo-1", Alice);

        AssertPost("chat.bsky.group.rejectJoinRequest", """{"convoId":"convo-1","member":"did:plc:alice"}""");
    }

    [Fact]
    public async Task UpdateJoinRequestsReadAsync_PostsTheConvoId()
    {
        _stub.On("chat.bsky.group.updateJoinRequestsRead", "{}");

        await _group.UpdateJoinRequestsReadAsync("convo-1");

        AssertPost("chat.bsky.group.updateJoinRequestsRead", """{"convoId":"convo-1"}""");
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private void AssertPost(string nsid, string expectedBody)
    {
        var request = Assert.Single(_stub.To(nsid));
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
        Assert.True(
            JsonElement.DeepEquals(JsonDocument.Parse(expectedBody).RootElement, JsonDocument.Parse(request.BodyText).RootElement),
            $"expected {expectedBody}\nactual   {request.BodyText}");
    }

    private void AssertGet(string nsid, string expectedQuery)
    {
        var request = Assert.Single(_stub.To(nsid));
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(expectedQuery, Uri.UnescapeDataString(request.Query));
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
    }

    public void Dispose() => _httpClient.Dispose();
}
