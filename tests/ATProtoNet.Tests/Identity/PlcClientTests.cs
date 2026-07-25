using System.Net;
using System.Text;
using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

public class PlcClientTests
{
    private const string TestDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";

    private const string DidDocumentJson = $$"""
        {
          "id": "{{TestDid}}",
          "alsoKnownAs": ["at://atproto.com"],
          "verificationMethod": [],
          "service": [{
            "id": "#atproto_pds",
            "type": "AtprotoPersonalDataServer",
            "serviceEndpoint": "https://pds.example.com"
          }]
        }
        """;

    /// <summary>Captures the final request URI so tests can assert BaseAddress composition.</summary>
    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (PlcClient Client, CapturingHandler Handler) CreateClient(
        HttpStatusCode status = HttpStatusCode.OK, string body = DidDocumentJson)
    {
        var handler = new CapturingHandler(status, body);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://plc.directory/"),
        };
        return (new PlcClient(httpClient), handler);
    }

    // Regression for #47: a bare DID ("did:plc:…") parses as an ABSOLUTE URI with scheme
    // "did", silently bypassing BaseAddress. Requests must compose onto the directory URL.

    [Fact]
    public async Task ResolveDidAsync_ComposesDidOntoDirectoryBaseAddress()
    {
        var (client, handler) = CreateClient();

        await client.ResolveDidAsync(TestDid);

        Assert.NotNull(handler.LastRequestUri);
        Assert.Equal("https", handler.LastRequestUri!.Scheme);
        Assert.Equal($"https://plc.directory/{TestDid}", handler.LastRequestUri.ToString());
    }

    [Fact]
    public async Task ResolveDidAsync_ReturnsParsedDocumentWithPdsEndpoint()
    {
        var (client, _) = CreateClient();

        var doc = await client.ResolveDidAsync(TestDid);

        Assert.Equal(TestDid, doc.Id);
        Assert.Equal("https://pds.example.com", doc.GetPdsEndpoint());
        Assert.Equal("atproto.com", doc.GetHandle());
    }

    [Fact]
    public async Task ResolveDidAsync_NotFound_ThrowsPlcException()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, "{}");

        var ex = await Assert.ThrowsAsync<PlcException>(() => client.ResolveDidAsync(TestDid));
        Assert.Equal(PlcErrorKind.NotFound, ex.Kind);
    }
}
