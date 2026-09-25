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
        _xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
        _actor = new ChatActorClient(_xrpc);
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

    public void Dispose()
    {
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
