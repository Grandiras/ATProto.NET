using System.Buffers.Binary;
using System.Net;
using System.Text;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class JetstreamDictionaryClientTests
{
    private sealed class CapturingHandler(HttpStatusCode status, HttpContent content) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
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

    private static (JetstreamDictionaryClient Client, CapturingHandler Handler) Create(
        HttpStatusCode status, HttpContent content, string serviceUrl = JetstreamEndpoints.UsEast)
    {
        var handler = new CapturingHandler(status, content);
        return (new JetstreamDictionaryClient(serviceUrl, new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task GetDictionaryAsync_ReadsIdOutOfTheDictionaryHeader()
    {
        // The subscription's zstdDictionary parameter takes the same ID the dictionary
        // carries in its own header, so one fetch is enough to configure a subscription.
        var (client, _) = Create(HttpStatusCode.OK, new ByteArrayContent(Dictionary(4242)));

        var dictionary = await client.GetDictionaryAsync();

        Assert.Equal(4242, dictionary.Id);
        Assert.Equal(16, dictionary.Data.Length);
    }

    [Fact]
    public async Task GetDictionaryAsync_NoId_RequestsTheCurrentDictionary()
    {
        var (client, handler) = Create(HttpStatusCode.OK, new ByteArrayContent(Dictionary(1)));

        await client.GetDictionaryAsync();

        Assert.Equal(
            "https://jetstream.us-east.bsky.network/xrpc/network.bsky.jetstream.getZstdDictionary",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task GetDictionaryAsync_ExplicitId_SentAsQueryParameter()
    {
        var (client, handler) = Create(HttpStatusCode.OK, new ByteArrayContent(Dictionary(7)));

        await client.GetDictionaryAsync(7);

        Assert.Equal("?id=7", handler.LastRequestUri?.Query);
    }

    [Fact]
    public async Task GetDictionaryAsync_WebSocketServiceUrl_ConvertedToHttp()
    {
        // The same ServiceUrl configures the subscription and this client.
        var (client, handler) = Create(
            HttpStatusCode.OK, new ByteArrayContent(Dictionary(1)), "ws://localhost:6008");

        await client.GetDictionaryAsync();

        Assert.Equal("http", handler.LastRequestUri?.Scheme);
        Assert.Equal("localhost:6008", handler.LastRequestUri?.Authority);
    }

    [Fact]
    public async Task GetDictionaryAsync_ErrorResponse_ThrowsWithXrpcErrorName()
    {
        var (client, _) = Create(
            HttpStatusCode.BadRequest,
            new StringContent("""{"error":"DictionaryNotFound","message":"no such id"}""",
                Encoding.UTF8, "application/json"));

        var ex = await Assert.ThrowsAsync<JetstreamConnectException>(() => client.GetDictionaryAsync(99));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("DictionaryNotFound", ex.Message);
    }

    [Fact]
    public async Task GetDictionaryAsync_NonXrpcErrorBody_StillThrows()
    {
        // A proxy error page rather than an XRPC envelope must not become a parse failure.
        var (client, _) = Create(
            HttpStatusCode.BadGateway, new StringContent("<html>502</html>", Encoding.UTF8, "text/html"));

        var ex = await Assert.ThrowsAsync<JetstreamConnectException>(() => client.GetDictionaryAsync());

        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task GetDictionaryAsync_NotAZstdDictionary_Throws()
    {
        var (client, _) = Create(HttpStatusCode.OK, new ByteArrayContent(Dictionary(1, magic: 0xDEADBEEF)));

        await Assert.ThrowsAsync<JetstreamConnectException>(() => client.GetDictionaryAsync());
    }

    [Fact]
    public void Constructor_EmptyServiceUrl_Throws()
    {
        Assert.Throws<ArgumentException>(() => new JetstreamDictionaryClient("  "));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var client = new JetstreamDictionaryClient(JetstreamEndpoints.UsEast);

        client.Dispose();
        client.Dispose();
    }
}
