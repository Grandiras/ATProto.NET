using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Lexicon.Chat.Bsky.Actor;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Chat.Bsky.Actor;

public class ChatActorClientTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;
    private readonly ChatActorClient _actor;

    public ChatActorClientTests()
    {
        _handler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://pds.example.com/")
        };
        _xrpc = new XrpcClient(_httpClient, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
        _actor = new ChatActorClient(_xrpc, NullLogger.Instance);
    }

    [Fact]
    public async Task DeleteAccount_PostsWithProxy()
    {
        string? capturedUrl = null;
        string? capturedProxy = null;
        string? capturedMethod = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            capturedMethod = request.Method.Method;
            capturedProxy = request.Headers.TryGetValues("atproto-proxy", out var v)
                ? v.FirstOrDefault() : null;
            return new HttpResponseMessage { Content = new StringContent("{}") };
        };

        await _actor.DeleteAccountAsync();

        Assert.Contains("chat.bsky.actor.deleteAccount", capturedUrl);
        Assert.Equal("POST", capturedMethod);
        Assert.Equal(ServiceProxy.BskyChatHeader, capturedProxy);
    }

    [Fact]
    public void ChatDeclarationRecord_HasCorrectType()
    {
        var decl = new ChatDeclarationRecord { AllowIncoming = ChatAllowIncoming.Following };

        Assert.Equal("chat.bsky.actor.declaration", decl.Type);
        Assert.Equal("following", decl.AllowIncoming);
    }

    [Fact]
    public void ChatDeclarationRecord_Serializes()
    {
        var decl = new ChatDeclarationRecord { AllowIncoming = ChatAllowIncoming.All };
        var json = JsonSerializer.Serialize(decl);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("chat.bsky.actor.declaration", doc.RootElement.GetProperty("$type").GetString());
        Assert.Equal("all", doc.RootElement.GetProperty("allowIncoming").GetString());
    }

    [Fact]
    public void ChatAllowIncoming_HasExpectedValues()
    {
        Assert.Equal("all", ChatAllowIncoming.All);
        Assert.Equal("none", ChatAllowIncoming.None);
        Assert.Equal("following", ChatAllowIncoming.Following);
    }

    public void Dispose()
    {
        _xrpc.Dispose();
        _httpClient.Dispose();
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } =
            _ => new HttpResponseMessage { Content = new StringContent("{}") };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(ResponseFactory(request));
    }
}
