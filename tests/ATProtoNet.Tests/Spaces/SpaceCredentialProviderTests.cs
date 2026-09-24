using System.Net;
using System.Text;
using ATProtoNet.Identity;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

/// <summary>
/// How the credential provider treats the host URLs it is given or resolves.
/// </summary>
public class SpaceCredentialProviderTests : IDisposable
{
    private const string Did = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";

    private readonly AtProtoClient _client = new(new AtProtoClientOptions { AutoRefreshSession = false });

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("https://host.example.com/?x=1")]
    [InlineData("https://host.example.com/#frag")]
    [InlineData("host.example.com")]
    public async Task CreateReaderAsync_WithAnUnusableHostUrl_ThrowsBeforeMintingACredential(string hostUrl)
    {
        await using var provider = new SpaceCredentialProvider(_client);
        var space = SpaceUri.Parse($"at://{Did}/space/com.example.forum/default");

        // No session and no network: the argument check must come first.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => provider.CreateReaderAsync(space, hostUrl));

        Assert.Equal("hostUrl", ex.ParamName);
    }

    [Theory]
    [InlineData("https://pds.example.com/?tenant=1")]
    [InlineData("https://pds.example.com/#frag")]
    [InlineData("not a url")]
    public async Task ResolveHostAsync_WhenTheDidPublishesAnUnusableEndpoint_ThrowsSpaceCredentialException(
        string endpoint)
    {
        using var resolver = ResolverPublishing(endpoint);
        await using var provider = new SpaceCredentialProvider(_client, didResolver: resolver);

        var ex = await Assert.ThrowsAsync<SpaceCredentialException>(() => provider.ResolveHostAsync(ATProtoNet.Identity.Did.Parse(Did)));

        Assert.Contains(endpoint, ex.Message);
    }

    [Fact]
    public async Task ResolveHostAsync_ReturnsAUsableEndpoint()
    {
        using var resolver = ResolverPublishing("https://pds.example.com");
        await using var provider = new SpaceCredentialProvider(_client, didResolver: resolver);

        Assert.Equal("https://pds.example.com", await provider.ResolveHostAsync(ATProtoNet.Identity.Did.Parse(Did)));
    }

    private static DidResolver ResolverPublishing(string endpoint)
    {
        var document = $$"""
            {
              "id": "{{Did}}",
              "alsoKnownAs": ["at://alice.example.com"],
              "verificationMethod": [],
              "service": [{
                "id": "#atproto_pds",
                "type": "AtprotoPersonalDataServer",
                "serviceEndpoint": "{{endpoint}}"
              }]
            }
            """;

        var plc = new HttpClient(new StaticHandler(document)) { BaseAddress = new Uri("https://plc.directory/") };
        return new DidResolver(new PlcClient(plc), new DidWebResolver(new HttpClient(new StaticHandler("{}"))));
    }

    private sealed class StaticHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
