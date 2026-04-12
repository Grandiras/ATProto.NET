using ATProtoNet.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Http;

public class XrpcClientLabelerHeaderTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;

    public XrpcClientLabelerHeaderTests()
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
    public async Task SetLabelers_IncludesHeaderInRequests()
    {
        string? capturedHeader = null;
        _handler.ResponseFactory = request =>
        {
            capturedHeader = request.Headers.TryGetValues("atproto-accept-labelers", out var values)
                ? values.FirstOrDefault()
                : null;
            return new HttpResponseMessage
            {
                Content = new StringContent("{}")
            };
        };

        _xrpc.SetLabelers(["did:plc:labeler1", "did:plc:labeler2"]);
        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Equal("did:plc:labeler1, did:plc:labeler2", capturedHeader);
    }

    [Fact]
    public async Task ClearLabelers_RemovesHeader()
    {
        string? capturedHeader = null;
        _handler.ResponseFactory = request =>
        {
            capturedHeader = request.Headers.TryGetValues("atproto-accept-labelers", out var values)
                ? values.FirstOrDefault()
                : null;
            return new HttpResponseMessage
            {
                Content = new StringContent("{}")
            };
        };

        _xrpc.SetLabelers(["did:plc:labeler1"]);
        _xrpc.ClearLabelers();
        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Null(capturedHeader);
    }

    [Fact]
    public async Task NoLabelers_NoHeader()
    {
        bool hasHeader = false;
        _handler.ResponseFactory = request =>
        {
            hasHeader = request.Headers.Contains("atproto-accept-labelers");
            return new HttpResponseMessage
            {
                Content = new StringContent("{}")
            };
        };

        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.False(hasHeader);
    }

    [Fact]
    public async Task SingleLabeler_NoTrailingComma()
    {
        string? capturedHeader = null;
        _handler.ResponseFactory = request =>
        {
            capturedHeader = request.Headers.TryGetValues("atproto-accept-labelers", out var values)
                ? values.FirstOrDefault()
                : null;
            return new HttpResponseMessage
            {
                Content = new StringContent("{}")
            };
        };

        _xrpc.SetLabelers(["did:plc:labeler1"]);
        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Equal("did:plc:labeler1", capturedHeader);
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
