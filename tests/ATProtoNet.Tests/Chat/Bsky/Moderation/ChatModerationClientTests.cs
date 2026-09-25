using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
using ATProtoNet.Lexicon.Chat.Bsky.Moderation;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Chat.Bsky.Moderation;

/// <summary>
/// One test per <c>chat.bsky.moderation.*</c> XRPC method. These calls carry no fixed proxy
/// header, so they reach whatever the client-wide default names (for example Ozone).
/// </summary>
public class ChatModerationClientTests : IDisposable
{
    private const string OzoneProxy = "did:plc:ozone#atproto_labeler";

    private const string GroupConvoJson =
        """
        {"id":"convo-1","rev":"2222222222225",
         "kind":{"$type":"chat.bsky.moderation.defs#groupConvo","name":"Book club","memberCount":12,"memberLimit":100,
                 "joinRequestCount":4,"lockStatus":"locked","createdAt":"2026-06-01T12:00:00.000Z",
                 "joinLink":{"code":"abc123","enabledStatus":"disabled","requireApproval":false,"joinRule":"anyone",
                             "createdAt":"2026-06-01T12:00:10.000Z"}}}
        """;

    private static readonly Did Alice = Did.Parse("did:plc:alice");

    private readonly MockHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;
    private readonly ChatModerationClient _moderation;

    private string? _method;
    private string? _pathAndQuery;
    private string? _body;
    private string? _proxy;

    public ChatModerationClientTests()
    {
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://pds.example.com/") };
        _xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
        _xrpc.SetProxy(OzoneProxy);
        _moderation = new ChatModerationClient(_xrpc);
    }

    [Fact]
    public async Task GetActorMetadataAsync_SendsTheActor_ReadsEachPeriod()
    {
        Respond(
            """
            {"day":{"messagesSent":3,"messagesReceived":4,"convos":1,"convosStarted":1},
             "month":{"messagesSent":30,"messagesReceived":40,"convos":5,"convosStarted":2},
             "all":{"messagesSent":300,"messagesReceived":400,"convos":9,"convosStarted":6}}
            """);

        var metadata = await _moderation.GetActorMetadataAsync(Alice);

        AssertCall("GET", "chat.bsky.moderation.getActorMetadata?actor=did:plc:alice");
        Assert.Equal((3, 4, 1, 1), (metadata.Day.MessagesSent, metadata.Day.MessagesReceived, metadata.Day.Convos, metadata.Day.ConvosStarted));
        Assert.Equal(40, metadata.Month.MessagesReceived);
        Assert.Equal(6, metadata.All.ConvosStarted);
    }

    [Fact]
    public async Task GetMessageContextAsync_SendsEveryParameter_ReadsMessagesAndSystemMessages()
    {
        Respond(
            """
            {"messages":[
              {"$type":"chat.bsky.convo.defs#systemMessageView","id":"msg-1","rev":"r1","sentAt":"2026-06-01T12:00:00.000Z",
               "data":{"$type":"chat.bsky.convo.defs#systemMessageDataMemberJoin","member":{"did":"did:plc:alice"},"role":"standard"}},
              {"$type":"chat.bsky.convo.defs#messageView","id":"msg-2","rev":"r2","text":"reported",
               "sender":{"did":"did:plc:alice"},"sentAt":"2026-06-01T12:01:00.000Z"}]}
            """);

        var context = await _moderation.GetMessageContextAsync(
            "msg-2", convoId: "convo-1", before: 3, after: 0, maxInterleavedSystemMessages: 2);

        AssertCall("GET",
            "chat.bsky.moderation.getMessageContext?convoId=convo-1&messageId=msg-2&before=3&after=0&maxInterleavedSystemMessages=2");
        Assert.Collection(context.Messages,
            m => Assert.Null(Assert.IsType<SystemMessageDataMemberJoin>(Assert.IsType<SystemMessageView>(m).Data).ApprovedBy),
            m => Assert.Equal("reported", Assert.IsType<MessageView>(m).Text));
    }

    [Fact]
    public async Task GetMessageContextAsync_OnlyTheMessage_SendsOnlyIt()
    {
        Respond("""{"messages":[]}""");

        await _moderation.GetMessageContextAsync("msg-2");

        AssertCall("GET", "chat.bsky.moderation.getMessageContext?messageId=msg-2");
    }

    [Fact]
    public async Task GetConvoAsync_SendsTheConvoId_UnwrapsTheModerationView()
    {
        Respond($$"""{"convo":{{GroupConvoJson}}}""");

        var convo = await _moderation.GetConvoAsync("convo-1");

        AssertCall("GET", "chat.bsky.moderation.getConvo?convoId=convo-1");
        var group = Assert.IsType<ModerationGroupConvo>(convo.Kind);
        Assert.Equal(("Book club", 12, 100, 4), (group.Name, group.MemberCount, group.MemberLimit, group.JoinRequestCount));
        Assert.Equal(ConvoLockStatus.Locked, group.LockStatus);
        Assert.Equal("abc123", group.JoinLink!.Code);
    }

    [Fact]
    public async Task GetConvosAsync_SendsEachId_ReadsTheConvos()
    {
        Respond(
            $$$"""
            {"convos":[{{{GroupConvoJson}}},
              {"id":"convo-2","rev":"r","kind":{"$type":"chat.bsky.moderation.defs#directConvo"}},
              {"id":"convo-3","rev":"r","kind":{"$type":"chat.bsky.moderation.defs#channelConvo","topic":"x"}}]}
            """);

        var result = await _moderation.GetConvosAsync(["convo-1", "convo-2", "convo-3"]);

        AssertCall("GET", "chat.bsky.moderation.getConvos?convoIds=convo-1&convoIds=convo-2&convoIds=convo-3");
        Assert.Collection(result.Convos,
            c => Assert.IsType<ModerationGroupConvo>(c.Kind),
            c => Assert.IsType<ModerationDirectConvo>(c.Kind),
            c => Assert.Equal("chat.bsky.moderation.defs#channelConvo", Assert.IsType<UnknownModerationConvoKind>(c.Kind).Type));
    }

    [Fact]
    public async Task GetConvoMembersAsync_SendsTheConvoAndPaging_ReadsTheMembers()
    {
        Respond(
            """
            {"cursor":"next","members":[{"did":"did:plc:alice","handle":"alice.bsky.social",
              "kind":{"$type":"chat.bsky.actor.defs#pastGroupConvoMember"}}]}
            """);

        var page = await _moderation.GetConvoMembersAsync("convo-1", limit: 10, cursor: "abc");

        AssertCall("GET", "chat.bsky.moderation.getConvoMembers?convoId=convo-1&limit=10&cursor=abc");
        Assert.Equal("next", page.Cursor);
        Assert.Equal(Alice, Assert.Single(page.Members).Did);
    }

    [Fact]
    public async Task EnumerateConvoMembersAsync_WalksPages()
    {
        var queries = new List<string>();
        _handler.ResponseFactory = request =>
        {
            queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            return Json(queries.Count == 1
                ? """{"cursor":"page-2","members":[{"did":"did:plc:alice","handle":"alice.bsky.social"}]}"""
                : """{"members":[{"did":"did:plc:bob","handle":"bob.bsky.social"}]}""");
        };

        var members = await _moderation.EnumerateConvoMembersAsync("convo-1", pageSize: 1).ToListAsync();

        Assert.Equal(["alice.bsky.social", "bob.bsky.social"], members.Select(m => m.Handle.ToString()));
        Assert.Equal(["?convoId=convo-1&limit=1", "?convoId=convo-1&limit=1&cursor=page-2"], queries);
    }

    [Fact]
    public async Task UpdateActorAccessAsync_PostsTheActorAccessAndRef()
    {
        Respond("");

        await _moderation.UpdateActorAccessAsync(Alice, allowAccess: false, reference: "ozone-event-42");

        AssertCall("POST", "chat.bsky.moderation.updateActorAccess");
        Assert.Equal("""{"actor":"did:plc:alice","allowAccess":false,"ref":"ozone-event-42"}""", _body);
    }

    [Fact]
    public async Task Calls_WithoutAClientWideProxy_SendNoProxyHeader()
    {
        _xrpc.ClearProxy();
        Respond("""{"messages":[]}""");

        await _moderation.GetMessageContextAsync("msg-2");

        Assert.Null(_proxy);
    }

    private void Respond(string json) =>
        _handler.ResponseFactory = request =>
        {
            _method = request.Method.Method;
            _pathAndQuery = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
            _body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            _proxy = request.Headers.TryGetValues("atproto-proxy", out var v) ? v.FirstOrDefault() : null;
            return Json(json);
        };

    // Every call follows the client-wide proxy rather than naming the Bluesky chat service.
    private void AssertCall(string method, string nsidAndQuery)
    {
        Assert.Equal(method, _method);
        Assert.Equal($"/xrpc/{nsidAndQuery}", _pathAndQuery);
        Assert.Equal(OzoneProxy, _proxy);
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
