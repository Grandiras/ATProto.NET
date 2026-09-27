using ATProtoNet.Http;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Http;

/// <summary>
/// The client-wide <c>atproto-proxy</c> and <c>atproto-accept-labelers</c> headers: sent while
/// set, gone once cleared, absent when never set.
/// </summary>
public sealed class XrpcClientHeaderTests : IDisposable
{
    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;

    public XrpcClientHeaderTests()
    {
        _httpClient = new HttpClient(_stub) { BaseAddress = new Uri("https://pds.example.com/") };
        _xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
        _stub.On("app.bsky.feed.getTimeline", "{}");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _stub.Dispose();
    }

    [Theory]
    [InlineData("did:web:api.bsky.app#bsky_appview", false, "did:web:api.bsky.app#bsky_appview")]
    [InlineData("did:web:api.bsky.app#bsky_appview", true, null)]
    [InlineData(null, false, null)]
    public async Task Proxy_IsSentOnlyWhileSet(string? proxy, bool clear, string? expected)
    {
        if (proxy is not null)
            _xrpc.SetProxy(proxy);
        if (clear)
            _xrpc.ClearProxy();

        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Equal(expected, Assert.Single(_stub.Requests).Proxy);
    }

    [Fact]
    public void SetProxy_EmptyValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => _xrpc.SetProxy(""));
    }

    [Theory]
    [InlineData(new[] { "did:plc:labeler1", "did:plc:labeler2" }, false, "did:plc:labeler1, did:plc:labeler2")]
    [InlineData(new[] { "did:plc:labeler1" }, false, "did:plc:labeler1")] // no trailing comma
    [InlineData(new[] { "did:plc:labeler1" }, true, null)]
    [InlineData(null, false, null)]
    public async Task Labelers_AreSentOnlyWhileSet(string[]? labelers, bool clear, string? expected)
    {
        if (labelers is not null)
            _xrpc.SetLabelers(labelers);
        if (clear)
            _xrpc.ClearLabelers();

        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Equal(expected, Assert.Single(_stub.Requests).HeaderOrDefault("atproto-accept-labelers"));
    }
}
