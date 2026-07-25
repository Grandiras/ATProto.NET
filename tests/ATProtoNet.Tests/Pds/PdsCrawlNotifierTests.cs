using ATProtoNet.Pds;

namespace ATProtoNet.Tests.Pds;

public sealed class PdsCrawlNotifierTests
{
    [Theory]
    [InlineData("bsky.network", "https://bsky.network")]
    [InlineData("bsky.network/", "https://bsky.network")]
    [InlineData("  bsky.network  ", "https://bsky.network")]
    [InlineData("https://relay.example.com", "https://relay.example.com")]
    [InlineData("http://localhost:2470", "http://localhost:2470")]
    [InlineData("http://localhost:2470/", "http://localhost:2470")]
    public void NormalizeRelayUrl_DefaultsToHttpsAndTrimsTrailingSlash(string input, string expected)
    {
        Assert.Equal(expected, PdsCrawlNotifier.NormalizeRelayUrl(input));
    }

    [Fact]
    public async Task RequestCrawlAsync_UnreachableRelay_ReportsFailureInsteadOfThrowing()
    {
        // A host that notifies relays at startup must not fail to start because one is down.
        var options = new PdsOptions { Hostname = "test.local", RelayHosts = ["http://127.0.0.1:1"] };
        using var notifier = new PdsCrawlNotifier(options, new HttpClient { Timeout = TimeSpan.FromSeconds(5) });

        var result = Assert.Single(await notifier.RequestCrawlAsync());

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal("http://127.0.0.1:1", result.Relay);
    }

    [Fact]
    public async Task RequestCrawlAsync_NoRelaysConfigured_ReturnsAnEmptyResult()
    {
        using var notifier = new PdsCrawlNotifier(new PdsOptions { Hostname = "test.local" });
        Assert.Empty(await notifier.RequestCrawlAsync());
    }

    [Fact]
    public async Task RequestCrawlAsync_PostsThePdsHostnameToTheRelay()
    {
        var handler = new CapturingHandler();
        var options = new PdsOptions { Hostname = "pds.example.com", RelayHosts = ["relay.example.com"] };
        using var notifier = new PdsCrawlNotifier(options, new HttpClient(handler));

        var result = Assert.Single(await notifier.RequestCrawlAsync());

        Assert.True(result.Success);
        Assert.Equal("https://relay.example.com/xrpc/com.atproto.sync.requestCrawl", handler.Url);
        Assert.Contains("\"hostname\":\"pds.example.com\"", handler.Body);
    }

    [Fact]
    public async Task RequestCrawlAsync_RelayRejects_SurfacesStatusAndBody()
    {
        var handler = new CapturingHandler
        {
            StatusCode = System.Net.HttpStatusCode.BadRequest,
            ResponseBody = """{"error":"InvalidRequest"}""",
        };
        var options = new PdsOptions { Hostname = "pds.example.com", RelayHosts = ["relay.example.com"] };
        using var notifier = new PdsCrawlNotifier(options, new HttpClient(handler));

        var result = Assert.Single(await notifier.RequestCrawlAsync());

        Assert.False(result.Success);
        Assert.Contains("400", result.Error);
        Assert.Contains("InvalidRequest", result.Error);
    }

    [Fact]
    public async Task RequestCrawlAsync_OneRelayDown_StillNotifiesTheRest()
    {
        var options = new PdsOptions
        {
            Hostname = "pds.example.com",
            RelayHosts = ["http://127.0.0.1:1", "relay.example.com"],
        };
        using var notifier = new PdsCrawlNotifier(options, new HttpClient(new CapturingHandler { FailFirstHost = true }));

        var results = await notifier.RequestCrawlAsync();

        Assert.Equal(2, results.Count);
        Assert.False(results[0].Success);
        Assert.True(results[1].Success);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string Body { get; private set; } = "";
        public System.Net.HttpStatusCode StatusCode { get; init; } = System.Net.HttpStatusCode.OK;
        public string ResponseBody { get; init; } = "{}";
        public bool FailFirstHost { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);

            if (FailFirstHost && request.RequestUri.Host == "127.0.0.1")
                throw new HttpRequestException("Connection refused");

            return new HttpResponseMessage(StatusCode) { Content = new StringContent(ResponseBody) };
        }
    }
}
