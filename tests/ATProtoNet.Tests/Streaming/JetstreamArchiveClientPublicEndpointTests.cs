using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ATProtoNet.Aspire;
using ATProtoNet.Streaming;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ATProtoNet.Tests.Streaming;

/// <summary>
/// The Jetstream endpoints outside the metered archive: the zstd dictionary, the health check, and
/// the free <c>HEAD</c> size probes.
/// </summary>
public class JetstreamArchiveClientPublicEndpointTests
{
    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public CapturingHandler(HttpStatusCode status, HttpContent content)
            : this(_ => new HttpResponseMessage(status) { Content = content })
        {
        }

        public HttpRequestMessage? Last => Requests.Count > 0 ? Requests[^1] : null;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>A minimal zstd structured dictionary header: magic, then the dictionary ID.</summary>
    private static byte[] Dictionary(int id, uint magic = 0xEC30A437)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, magic);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)id);
        return bytes;
    }

    private static (JetstreamArchiveClient Client, CapturingHandler Handler) Create(
        HttpStatusCode status, HttpContent content, string serviceUrl = JetstreamEndpoints.UsEast)
        => Create(new CapturingHandler(status, content), serviceUrl);

    private static (JetstreamArchiveClient Client, CapturingHandler Handler) Create(
        CapturingHandler handler, string serviceUrl = JetstreamEndpoints.UsEast)
        => (new JetstreamArchiveClient(serviceUrl, "secret-key", new HttpClient(handler)) { MaxRetryAttempts = 0 }, handler);

    [Fact]
    public async Task GetZstdDictionaryAsync_ReadsIdOutOfTheDictionaryHeader()
    {
        // The subscription's zstdDictionary parameter takes the same ID the dictionary
        // carries in its own header, so one fetch is enough to configure a subscription.
        var (client, _) = Create(HttpStatusCode.OK, new ByteArrayContent(Dictionary(4242)));

        var dictionary = await client.GetZstdDictionaryAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4242, dictionary.Id);
        Assert.Equal(16, dictionary.Data.Length);
    }

    [Fact]
    public async Task GetZstdDictionaryAsync_NoId_RequestsTheCurrentDictionaryWithoutTheApiKey()
    {
        var (client, handler) = Create(HttpStatusCode.OK, new ByteArrayContent(Dictionary(1)));

        await client.GetZstdDictionaryAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://jetstream.us-east.bsky.network/xrpc/network.bsky.jetstream.getZstdDictionary",
            handler.Last?.RequestUri?.ToString());
        // The endpoint is public: the metered archive's key is not sent to it.
        Assert.Null(handler.Last?.Headers.Authorization);
    }

    [Fact]
    public async Task GetZstdDictionaryAsync_ExplicitId_SentAsQueryParameter()
    {
        var (client, handler) = Create(HttpStatusCode.OK, new ByteArrayContent(Dictionary(7)));

        await client.GetZstdDictionaryAsync(7, TestContext.Current.CancellationToken);

        Assert.Equal("?id=7", handler.Last?.RequestUri?.Query);
    }

    [Fact]
    public async Task GetZstdDictionaryAsync_WebSocketServiceUrl_ConvertedToHttp()
    {
        // The same ServiceUrl configures the subscription and this client.
        var (client, handler) = Create(
            HttpStatusCode.OK, new ByteArrayContent(Dictionary(1)), "ws://localhost:6008");

        await client.GetZstdDictionaryAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("http", handler.Last?.RequestUri?.Scheme);
        Assert.Equal("localhost:6008", handler.Last?.RequestUri?.Authority);
    }

    [Fact]
    public async Task GetZstdDictionaryAsync_ErrorResponse_ThrowsWithXrpcErrorName()
    {
        var (client, _) = Create(
            HttpStatusCode.BadRequest,
            new StringContent("""{"error":"DictionaryNotFound","message":"no such id"}""",
                Encoding.UTF8, "application/json"));

        var ex = await Assert.ThrowsAsync<JetstreamException>(
            () => client.GetZstdDictionaryAsync(99, TestContext.Current.CancellationToken));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("DictionaryNotFound", ex.Error);
        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public async Task GetZstdDictionaryAsync_NonXrpcErrorBody_StillThrows()
    {
        // A proxy error page rather than an XRPC envelope must not become a parse failure.
        var (client, _) = Create(
            HttpStatusCode.BadGateway, new StringContent("<html>502</html>", Encoding.UTF8, "text/html"));

        var ex = await Assert.ThrowsAsync<JetstreamException>(
            () => client.GetZstdDictionaryAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task GetZstdDictionaryAsync_NotAZstdDictionary_Throws()
    {
        var (client, _) = Create(HttpStatusCode.OK, new ByteArrayContent(Dictionary(1, magic: 0xDEADBEEF)));

        await Assert.ThrowsAsync<JetstreamException>(
            () => client.GetZstdDictionaryAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetHealthAsync_ReadsTheVersionFromThePublicEndpoint()
    {
        var (client, handler) = Create(
            HttpStatusCode.OK, new StringContent("""{"version":"v2.3.1"}""", Encoding.UTF8, "application/json"));

        var health = await client.GetHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal("v2.3.1", health.Version);
        Assert.Equal("https://jetstream.us-east.bsky.network/xrpc/_health", handler.Last?.RequestUri?.ToString());
        Assert.Null(handler.Last?.Headers.Authorization);
    }

    [Fact]
    public async Task GetHealthAsync_ErrorStatus_Throws()
    {
        var (client, _) = Create(HttpStatusCode.ServiceUnavailable, new StringContent(""));

        var ex = await Assert.ThrowsAsync<JetstreamException>(() => client.GetHealthAsync(TestContext.Current.CancellationToken));

        Assert.Equal(503, ex.StatusCode);
    }

    [Fact]
    public async Task ProbeSegmentAsync_SendsAHeadRequestAndReadsSizeAndETag()
    {
        var handler = new CapturingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentLength = 123_456_789;
            response.Headers.ETag = new EntityTagHeaderValue("\"0123456789abcdef\"");
            return response;
        });
        var (client, _) = Create(handler);

        var probe = await client.ProbeSegmentAsync("seg_000000002a.jss", TestContext.Current.CancellationToken);

        Assert.Equal(123_456_789, probe.ContentLength);
        Assert.Equal("0123456789abcdef", probe.ETag);
        Assert.Equal(HttpMethod.Head, handler.Last?.Method);
        Assert.Equal("?name=seg_000000002a.jss", handler.Last?.RequestUri?.Query);
        Assert.Equal("Bearer", handler.Last?.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task ProbeBlockAsync_NamesTheBlock()
    {
        var handler = new CapturingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentLength = 4096;
            return response;
        });
        var (client, _) = Create(handler);

        var probe = await client.ProbeBlockAsync("seg_000000002a.jss", 3, TestContext.Current.CancellationToken);

        Assert.Equal(4096, probe.ContentLength);
        Assert.Null(probe.ETag);
        Assert.Equal(HttpMethod.Head, handler.Last?.Method);
        Assert.Equal("/xrpc/network.bsky.jetstream.getBlock", handler.Last?.RequestUri?.AbsolutePath);
        Assert.Equal("?segment=seg_000000002a.jss&blockIndex=3", handler.Last?.RequestUri?.Query);
    }

    [Fact]
    public async Task ProbeBlockAsync_BlockNotFound_Throws()
    {
        var (client, _) = Create(
            HttpStatusCode.NotFound,
            new StringContent("""{"error":"BlockNotFound"}""", Encoding.UTF8, "application/json"));

        var ex = await Assert.ThrowsAsync<JetstreamException>(
            () => client.ProbeBlockAsync("seg_000000002a.jss", 99, TestContext.Current.CancellationToken));

        Assert.Equal("BlockNotFound", ex.Error);
    }

    [Fact]
    public async Task JetstreamHealthCheck_ReportsHealthyAndUnhealthy()
    {
        var up = Create(HttpStatusCode.OK, new StringContent("""{"version":"v2"}""", Encoding.UTF8, "application/json")).Client;
        var down = Create(HttpStatusCode.BadGateway, new StringContent("")).Client;

        var healthy = await new JetstreamHealthCheck(up, JetstreamEndpoints.UsEast)
            .CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        var unhealthy = await new JetstreamHealthCheck(down, JetstreamEndpoints.UsEast)
            .CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, healthy.Status);
        Assert.Contains("v2", healthy.Description);
        Assert.Equal(HealthStatus.Unhealthy, unhealthy.Status);
        Assert.IsType<JetstreamException>(unhealthy.Exception);
    }

    [Fact]
    public void Constructor_EmptyServiceUrl_Throws()
    {
        Assert.Throws<ArgumentException>(() => new JetstreamArchiveClient("  "));
    }
}
