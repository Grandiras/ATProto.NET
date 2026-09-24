using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
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
                id = "convo-1", rev = "rev-2",
                members = Array.Empty<object>(),
                muted = true, unreadCount = 0,
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
            return new HttpResponseMessage { Content = new StringContent("{}") };
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
                id = "msg-1", rev = "rev-1",
                sender = new { did = "did:plc:user1" },
                sentAt = "2024-01-01T00:00:00Z",
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

        await _convo.ListConvosAsync(true, "accepted", 10, "abc");

        Assert.Equal("?readOnly=true&status=accepted&limit=10&cursor=abc", capturedQuery);
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
            return JsonResponse(new
            {
                cursor = "same",
                messages = new[] { new { id = $"msg-{requests}", rev = "rev-1" } },
            });
        };

        var messages = await _convo.EnumerateMessagesAsync("convo-1").ToListAsync();

        Assert.Equal(2, requests);
        Assert.Equal(["msg-1", "msg-2"], messages.Select(m => m.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task EnumerateLogAsync_StopsWhenTheLogHasNoNewerEntries()
    {
        var queries = new List<string>();
        _handler.ResponseFactory = request =>
        {
            queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            return queries.Count == 1
                ? JsonResponse(new { cursor = "rev-5", logs = new[] { new { rev = "rev-5", convoId = "convo-1" } } })
                : JsonResponse(new { cursor = "rev-5", logs = Array.Empty<object>() });
        };

        var entries = await _convo.EnumerateLogAsync().ToListAsync();

        Assert.Equal("rev-5", Assert.Single(entries).Rev);
        Assert.Equal(["", "?cursor=rev-5"], queries);
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

    private static HttpResponseMessage JsonResponse(object body) => new()
    {
        Content = new StringContent(
            JsonSerializer.Serialize(body),
            System.Text.Encoding.UTF8,
            "application/json")
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
