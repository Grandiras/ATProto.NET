using ATProtoNet.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Http;

public class XrpcClientProxyTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;

    public XrpcClientProxyTests()
    {
        _handler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://pds.example.com/")
        };
        _xrpc = new XrpcClient(_httpClient, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
    }

    [Fact]
    public async Task SetProxy_IncludesHeaderInRequests()
    {
        string? capturedHeader = null;
        _handler.ResponseFactory = request =>
        {
            capturedHeader = request.Headers.TryGetValues("atproto-proxy", out var values)
                ? values.FirstOrDefault()
                : null;
            return new HttpResponseMessage
            {
                Content = new StringContent("{}")
            };
        };

        _xrpc.SetProxy("did:web:api.bsky.app#bsky_appview");
        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Equal("did:web:api.bsky.app#bsky_appview", capturedHeader);
    }

    [Fact]
    public async Task ClearProxy_RemovesHeader()
    {
        string? capturedHeader = null;
        _handler.ResponseFactory = request =>
        {
            capturedHeader = request.Headers.TryGetValues("atproto-proxy", out var values)
                ? values.FirstOrDefault()
                : null;
            return new HttpResponseMessage
            {
                Content = new StringContent("{}")
            };
        };

        _xrpc.SetProxy("did:web:api.bsky.app#bsky_appview");
        _xrpc.ClearProxy();
        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Null(capturedHeader);
    }

    [Fact]
    public async Task NoProxy_NoHeader()
    {
        bool hasHeader = false;
        _handler.ResponseFactory = request =>
        {
            hasHeader = request.Headers.Contains("atproto-proxy");
            return new HttpResponseMessage
            {
                Content = new StringContent("{}")
            };
        };

        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.False(hasHeader);
    }

    [Fact]
    public void SetProxy_EmptyValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => _xrpc.SetProxy(""));
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
