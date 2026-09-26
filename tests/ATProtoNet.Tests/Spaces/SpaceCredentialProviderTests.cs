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

    [Fact]
    public async Task ResolveHostAsync_WhenTheDidPublishesNoUrlAtAll_ThrowsSpaceCredentialException()
    {
        using var resolver = ResolverPublishing("not a url");
        await using var provider = new SpaceCredentialProvider(_client, didResolver: resolver);

        var ex = await Assert.ThrowsAsync<SpaceCredentialException>(() => provider.ResolveHostAsync(ATProtoNet.Identity.Did.Parse(Did)));

        Assert.Contains("absolute http(s) URL", ex.Message);
    }

    [Theory]
    [InlineData("https://pds.example.com/?tenant=1")]
    [InlineData("https://pds.example.com/#frag")]
    public async Task ResolveHostAsync_WhenTheDidPublishesAnUnusableEndpoint_ThrowsSpaceCredentialException(
        string endpoint)
    {
        using var resolver = ResolverPublishing(endpoint);
        await using var provider = new SpaceCredentialProvider(_client, didResolver: resolver);

        var ex = await Assert.ThrowsAsync<SpaceCredentialException>(() => provider.ResolveHostAsync(ATProtoNet.Identity.Did.Parse(Did)));

        Assert.Contains(endpoint, ex.Message);
    }

    [Theory]
    [InlineData("AtprotoPersonalDataServer", "https://spaces.example.com")]
    [InlineData("AtprotoSpaceHost", "http://spaces.example.com")]
    public async Task ResolveHostAsync_MalformedSpaceHostEntry_ThrowsRatherThanUsingThePds(string type, string endpoint)
    {
        // Proposal 0016: a #atproto_space_host that is published but malformed is an error, and
        // the #atproto_pds fallback is for an absent entry only.
        using var resolver = ResolverPublishing(
            "https://pds.example.com",
            $$"""{"id":"#atproto_space_host","type":"{{type}}","serviceEndpoint":"{{endpoint}}"}""");
        await using var provider = new SpaceCredentialProvider(_client, didResolver: resolver);

        var ex = await Assert.ThrowsAsync<SpaceCredentialException>(() => provider.ResolveHostAsync(ATProtoNet.Identity.Did.Parse(Did)));

        Assert.Contains("#atproto_space_host", ex.Message);
    }

    [Fact]
    public async Task ResolveHostAsync_PublishedSpaceHost_IsPreferredOverThePds()
    {
        using var resolver = ResolverPublishing(
            "https://pds.example.com",
            """{"id":"#atproto_space_host","type":"AtprotoSpaceHost","serviceEndpoint":"https://spaces.example.com"}""");
        await using var provider = new SpaceCredentialProvider(_client, didResolver: resolver);

        Assert.Equal("https://spaces.example.com", await provider.ResolveHostAsync(ATProtoNet.Identity.Did.Parse(Did)));
    }

    [Fact]
    public async Task ResolveHostAsync_ReturnsAUsableEndpoint()
    {
        using var resolver = ResolverPublishing("https://pds.example.com");
        await using var provider = new SpaceCredentialProvider(_client, didResolver: resolver);

        Assert.Equal("https://pds.example.com", await provider.ResolveHostAsync(ATProtoNet.Identity.Did.Parse(Did)));
    }

    private static DidResolver ResolverPublishing(string endpoint, string? extraService = null)
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
              }{{(extraService is null ? "" : "," + extraService)}}]
            }
            """;

        var plc = new HttpClient(new StaticHandler(document)) { BaseAddress = new Uri("https://plc.directory/") };
        return new DidResolver(
            new PlcClient(plc, new Uri("https://plc.directory/")),
            new DidWebResolver(new HttpClient(new StaticHandler("{}"))));
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
