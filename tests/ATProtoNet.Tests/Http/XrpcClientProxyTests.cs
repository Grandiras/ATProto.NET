using ATProtoNet.Http;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Http;

public class XrpcClientProxyTests : IDisposable
{
    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;

    public XrpcClientProxyTests()
    {
        _httpClient = new HttpClient(_stub) { BaseAddress = new Uri("https://pds.example.com/") };
        _xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
    }

    [Fact]
    public async Task SetProxy_IncludesHeaderInRequests()
    {
        _stub.On("app.bsky.feed.getTimeline", "{}");

        _xrpc.SetProxy("did:web:api.bsky.app#bsky_appview");
        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Equal("did:web:api.bsky.app#bsky_appview", Assert.Single(_stub.Requests).Proxy);
    }

    [Fact]
    public async Task ClearProxy_RemovesHeader()
    {
        _stub.On("app.bsky.feed.getTimeline", "{}");

        _xrpc.SetProxy("did:web:api.bsky.app#bsky_appview");
        _xrpc.ClearProxy();
        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Null(Assert.Single(_stub.Requests).Proxy);
    }

    [Fact]
    public async Task NoProxy_NoHeader()
    {
        _stub.On("app.bsky.feed.getTimeline", "{}");

        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Null(Assert.Single(_stub.Requests).Proxy);
    }

    [Fact]
    public void SetProxy_EmptyValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => _xrpc.SetProxy(""));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _stub.Dispose();
    }
}
