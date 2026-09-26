using ATProtoNet.Http;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Http;

public class XrpcClientLabelerHeaderTests : IDisposable
{
    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;

    public XrpcClientLabelerHeaderTests()
    {
        _httpClient = new HttpClient(_stub) { BaseAddress = new Uri("https://pds.example.com/") };
        _xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
    }

    [Fact]
    public async Task SetLabelers_IncludesHeaderInRequests()
    {
        _stub.On("app.bsky.feed.getTimeline", "{}");

        _xrpc.SetLabelers(["did:plc:labeler1", "did:plc:labeler2"]);
        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Equal(
            "did:plc:labeler1, did:plc:labeler2",
            Assert.Single(_stub.Requests).HeaderOrDefault("atproto-accept-labelers"));
    }

    [Fact]
    public async Task ClearLabelers_RemovesHeader()
    {
        _stub.On("app.bsky.feed.getTimeline", "{}");

        _xrpc.SetLabelers(["did:plc:labeler1"]);
        _xrpc.ClearLabelers();
        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Null(Assert.Single(_stub.Requests).HeaderOrDefault("atproto-accept-labelers"));
    }

    [Fact]
    public async Task NoLabelers_NoHeader()
    {
        _stub.On("app.bsky.feed.getTimeline", "{}");

        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Null(Assert.Single(_stub.Requests).HeaderOrDefault("atproto-accept-labelers"));
    }

    [Fact]
    public async Task SingleLabeler_NoTrailingComma()
    {
        _stub.On("app.bsky.feed.getTimeline", "{}");

        _xrpc.SetLabelers(["did:plc:labeler1"]);
        await _xrpc.QueryAsync<object>("app.bsky.feed.getTimeline");

        Assert.Equal("did:plc:labeler1", Assert.Single(_stub.Requests).HeaderOrDefault("atproto-accept-labelers"));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _stub.Dispose();
    }
}
