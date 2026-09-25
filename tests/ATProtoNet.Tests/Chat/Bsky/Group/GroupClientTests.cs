using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
using ATProtoNet.Lexicon.Chat.Bsky.Group;
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

    private readonly MockHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly GroupClient _group;

    private string? _method;
    private string? _pathAndQuery;
    private string? _body;
    private string? _proxy;

    public GroupClientTests()
    {
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://pds.example.com/") };
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
        Respond($$"""{"convo":{{GroupConvoJson}}}""");

        var convo = await _group.CreateGroupAsync("Book club", [Alice, Bob]);

        AssertPost("chat.bsky.group.createGroup", """{"members":["did:plc:alice","did:plc:bob"],"name":"Book club"}""");
        Assert.Equal("Book club", Assert.IsType<GroupConvo>(convo.Kind).Name);
    }

    [Fact]
    public async Task EditGroupAsync_PostsTheNewName_ReturnsTheGroup()
    {
        Respond($$"""{"convo":{{GroupConvoJson}}}""");

        var convo = await _group.EditGroupAsync("convo-1", "Book club");

        AssertPost("chat.bsky.group.editGroup", """{"convoId":"convo-1","name":"Book club"}""");
        Assert.Equal("convo-1", convo.Id);
    }

    [Fact]
    public async Task AddMembersAsync_PostsTheMembers_ReadsTheAddedProfiles()
    {
        Respond($$"""{"convo":{{GroupConvoJson}},"addedMembers":[{"did":"did:plc:alice","handle":"alice.bsky.social"}]}""");

        var result = await _group.AddMembersAsync("convo-1", [Alice]);

        AssertPost("chat.bsky.group.addMembers", """{"convoId":"convo-1","members":["did:plc:alice"]}""");
        Assert.Equal("convo-1", result.Convo.Id);
        Assert.Equal(Alice, Assert.Single(result.AddedMembers!).Did);
    }

    [Fact]
    public async Task RemoveMembersAsync_PostsTheMembers_ReturnsTheGroup()
    {
        Respond($$"""{"convo":{{GroupConvoJson}}}""");

        var convo = await _group.RemoveMembersAsync("convo-1", [Alice, Bob]);

        AssertPost("chat.bsky.group.removeMembers", """{"convoId":"convo-1","members":["did:plc:alice","did:plc:bob"]}""");
        Assert.Equal(1, Assert.IsType<GroupConvo>(convo.Kind).MemberCount);
    }

    [Fact]
    public async Task ListMutualGroupsAsync_SendsTheSubjectAndPaging_ReadsThePage()
    {
        Respond($$"""{"cursor":"next","convos":[{{GroupConvoJson}}]}""");

        var page = await _group.ListMutualGroupsAsync(Alice, limit: 10, cursor: "abc");

        AssertGet("chat.bsky.group.listMutualGroups?subject=did:plc:alice&limit=10&cursor=abc");
        Assert.Equal("next", page.Cursor);
        Assert.Equal("convo-1", Assert.Single(page.Convos).Id);
    }

    [Fact]
    public async Task EnumerateMutualGroupsAsync_WalksPages()
    {
        var queries = new List<string>();
        _handler.ResponseFactory = request =>
        {
            queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            return Json(queries.Count == 1
                ? $$"""{"cursor":"page-2","convos":[{{GroupConvoJson}}]}"""
                : $$"""{"convos":[{{GroupConvoJson}}]}""");
        };

        var convos = await _group.EnumerateMutualGroupsAsync(Alice, pageSize: 1).ToListAsync();

        Assert.Equal(2, convos.Count);
        Assert.Equal(["?subject=did:plc:alice&limit=1", "?subject=did:plc:alice&limit=1&cursor=page-2"], queries);
    }

    // ──────────────────────────────────────────────────────────
    //  Join links
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateJoinLinkAsync_PostsTheRuleAndApproval_ReturnsTheLink()
    {
        Respond($$"""{"joinLink":{{JoinLinkJson}}}""");

        var link = await _group.CreateJoinLinkAsync("convo-1", JoinRule.FollowedByOwner, requireApproval: true);

        AssertPost("chat.bsky.group.createJoinLink", """{"convoId":"convo-1","requireApproval":true,"joinRule":"followedByOwner"}""");
        Assert.Equal(("abc123", JoinLinkEnabledStatus.Enabled, JoinRule.FollowedByOwner), (link.Code, link.EnabledStatus, link.JoinRule));
        Assert.True(link.RequireApproval);
        Assert.Equal(AtDatetime.Parse("2026-06-01T12:00:10.000Z"), link.CreatedAt);
    }

    [Fact]
    public async Task CreateJoinLinkAsync_WithoutApproval_LeavesItToTheServer()
    {
        Respond($$"""{"joinLink":{{JoinLinkJson}}}""");

        await _group.CreateJoinLinkAsync("convo-1", JoinRule.Anyone);

        AssertPost("chat.bsky.group.createJoinLink", """{"convoId":"convo-1","joinRule":"anyone"}""");
    }

    [Fact]
    public async Task EditJoinLinkAsync_OnlyTheChangedSetting_SendsOnlyIt()
    {
        Respond($$"""{"joinLink":{{JoinLinkJson}}}""");

        var link = await _group.EditJoinLinkAsync("convo-1", requireApproval: false);

        AssertPost("chat.bsky.group.editJoinLink", """{"convoId":"convo-1","requireApproval":false}""");
        Assert.Equal("abc123", link.Code);
    }

    [Theory]
    [InlineData("enableJoinLink")]
    [InlineData("disableJoinLink")]
    public async Task EnableAndDisableJoinLinkAsync_PostTheConvoId_ReturnTheLink(string method)
    {
        Respond($$"""{"joinLink":{{JoinLinkJson}}}""");

        var link = method == "enableJoinLink"
            ? await _group.EnableJoinLinkAsync("convo-1")
            : await _group.DisableJoinLinkAsync("convo-1");

        AssertPost($"chat.bsky.group.{method}", """{"convoId":"convo-1"}""");
        Assert.Equal("abc123", link.Code);
    }

    [Fact]
    public async Task GetJoinLinkPreviewsAsync_SendsEachCode_ReadsEachPreviewKind()
    {
        Respond(
            """
            {"joinLinkPreviews":[
              {"$type":"chat.bsky.group.defs#joinLinkPreviewView","convoId":"convo-1","code":"abc123","name":"Book club",
               "owner":{"did":"did:plc:owner","handle":"owner.bsky.social"},"memberCount":12,"memberLimit":100,
               "requireApproval":false,"joinRule":"anyone"},
              {"$type":"chat.bsky.group.defs#disabledJoinLinkPreviewView","code":"off456"},
              {"$type":"chat.bsky.group.defs#invalidJoinLinkPreviewView","code":"nope"}]}
            """);

        var result = await _group.GetJoinLinkPreviewsAsync(["abc123", "off456", "nope"]);

        AssertGet("chat.bsky.group.getJoinLinkPreviews?codes=abc123&codes=off456&codes=nope");
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
        Respond($$"""{"status":"joined","convo":{{GroupConvoJson}}}""");

        var result = await _group.RequestJoinAsync("abc123");

        AssertPost("chat.bsky.group.requestJoin", """{"code":"abc123"}""");
        Assert.Equal(RequestJoinStatus.Joined, result.Status);
        Assert.Equal("convo-1", result.Convo!.Id);
    }

    [Fact]
    public async Task RequestJoinAsync_LinkNeedingApproval_IsPending()
    {
        Respond("""{"status":"pending"}""");

        var result = await _group.RequestJoinAsync("abc123");

        Assert.Equal(RequestJoinStatus.Pending, result.Status);
        Assert.Null(result.Convo);
    }

    [Fact]
    public async Task WithdrawJoinRequestAsync_PostsTheConvoId()
    {
        Respond("{}");

        await _group.WithdrawJoinRequestAsync("convo-1");

        AssertPost("chat.bsky.group.withdrawJoinRequest", """{"convoId":"convo-1"}""");
    }

    [Fact]
    public async Task ListJoinRequestsAsync_SendsTheConvoAndPaging_ReadsTheRequests()
    {
        Respond(
            """
            {"cursor":"next","requests":[{"convoId":"convo-1","requestedAt":"2026-06-01T12:05:00.000Z",
              "requestedBy":{"did":"did:plc:alice","handle":"alice.bsky.social"}}]}
            """);

        var page = await _group.ListJoinRequestsAsync("convo-1", limit: 5, cursor: "abc");

        AssertGet("chat.bsky.group.listJoinRequests?convoId=convo-1&limit=5&cursor=abc");
        var request = Assert.Single(page.Requests);
        Assert.Equal(Alice, request.RequestedBy.Did);
        Assert.Equal(AtDatetime.Parse("2026-06-01T12:05:00.000Z"), request.RequestedAt);
    }

    [Fact]
    public async Task EnumerateJoinRequestsAsync_WalksPages()
    {
        var queries = new List<string>();
        _handler.ResponseFactory = request =>
        {
            queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            var did = queries.Count == 1 ? "did:plc:alice" : "did:plc:bob";
            var cursor = queries.Count == 1 ? "\"cursor\":\"page-2\"," : "";
            return Json(
                $$$"""
                {{{{cursor}}}"requests":[{"convoId":"convo-1","requestedAt":"2026-06-01T12:05:00.000Z",
                  "requestedBy":{"did":"{{{did}}}","handle":"x.bsky.social"}}]}
                """);
        };

        var requests = await _group.EnumerateJoinRequestsAsync("convo-1", pageSize: 1).ToListAsync();

        Assert.Equal([Alice, Bob], requests.Select(r => r.RequestedBy.Did));
        Assert.Equal(["?convoId=convo-1&limit=1", "?convoId=convo-1&limit=1&cursor=page-2"], queries);
    }

    [Fact]
    public async Task ApproveJoinRequestAsync_PostsTheMember_ReturnsTheGroup()
    {
        Respond($$"""{"convo":{{GroupConvoJson}}}""");

        var convo = await _group.ApproveJoinRequestAsync("convo-1", Alice);

        AssertPost("chat.bsky.group.approveJoinRequest", """{"convoId":"convo-1","member":"did:plc:alice"}""");
        Assert.Equal("convo-1", convo.Id);
    }

    [Fact]
    public async Task RejectJoinRequestAsync_PostsTheMember()
    {
        Respond("{}");

        await _group.RejectJoinRequestAsync("convo-1", Alice);

        AssertPost("chat.bsky.group.rejectJoinRequest", """{"convoId":"convo-1","member":"did:plc:alice"}""");
    }

    [Fact]
    public async Task UpdateJoinRequestsReadAsync_PostsTheConvoId()
    {
        Respond("{}");

        await _group.UpdateJoinRequestsReadAsync("convo-1");

        AssertPost("chat.bsky.group.updateJoinRequestsRead", """{"convoId":"convo-1"}""");
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private void Respond(string json) =>
        _handler.ResponseFactory = request =>
        {
            _method = request.Method.Method;
            _pathAndQuery = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
            _body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            _proxy = request.Headers.TryGetValues("atproto-proxy", out var v) ? v.FirstOrDefault() : null;
            return Json(json);
        };

    private void AssertPost(string nsid, string expectedBody)
    {
        Assert.Equal("POST", _method);
        Assert.Equal($"/xrpc/{nsid}", _pathAndQuery);
        Assert.Equal(ServiceProxy.BskyChatHeader, _proxy);
        Assert.True(
            JsonElement.DeepEquals(JsonDocument.Parse(expectedBody).RootElement, JsonDocument.Parse(_body!).RootElement),
            $"expected {expectedBody}\nactual   {_body}");
    }

    private void AssertGet(string nsidAndQuery)
    {
        Assert.Equal("GET", _method);
        Assert.Equal($"/xrpc/{nsidAndQuery}", _pathAndQuery);
        Assert.Equal(ServiceProxy.BskyChatHeader, _proxy);
    }

    private static HttpResponseMessage Json(string json) => new()
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    public void Dispose() => _httpClient.Dispose();

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } =
            _ => new HttpResponseMessage { Content = new StringContent("{}") };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(ResponseFactory(request));
    }
}
