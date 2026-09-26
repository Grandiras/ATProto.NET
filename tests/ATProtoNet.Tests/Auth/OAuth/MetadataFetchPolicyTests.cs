using System.Net;
using System.Text;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using ATProtoNet.Tests.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// The PDS and authorization-server metadata requests: their URLs come from a DID document anyone
/// can write, so by default they go out under the identity fetch policy, and every failure is an
/// <see cref="OAuthException"/>. Also who disposes the identity resolver an <see cref="OAuthClient"/>
/// runs on.
/// </summary>
public class MetadataFetchPolicyTests
{
    private const string RedirectUri = "https://app.example.com/callback";

    private static OAuthOptions Options(IIdentityResolver? resolver = null, HttpClient? metadata = null, bool allowPrivate = false) => new()
    {
        ClientMetadata = new OAuthClientMetadata
        {
            ClientId = "https://app.example.com/client-metadata.json",
            RedirectUris = [RedirectUri],
        },
        IdentityResolver = resolver,
        HttpClient = metadata,
        AllowPrivateNetworks = allowPrivate,
    };

    // ── Who owns the identity resolver ───────────────────────

    [Fact]
    public async Task Dispose_ResolverTheClientCreated_IsDisposedWithIt()
    {
        var client = new OAuthClient(Options(), NullLogger.Instance);
        var resolver = client.Discovery.IdentityResolver;

        client.Dispose();

        await Assert.ThrowsAnyAsync<ObjectDisposedException>(
            () => resolver.ResolveAsync(AtIdentifier.Parse("did:web:example.com")));
    }

    [Fact]
    public void Dispose_ResolverTheOptionsSupplied_IsLeftToTheCaller()
    {
        var resolver = Substitute.For<IIdentityResolver, IDisposable>();
        var client = new OAuthClient(Options(resolver), NullLogger.Instance);

        client.Dispose();

        ((IDisposable)resolver).DidNotReceive().Dispose();
    }

    // ── The address and URL rules ────────────────────────────

    [Fact]
    public async Task StartAuthorization_PdsOnALoopbackAddress_IsRefusedWithoutConnecting()
    {
        using var server = new LoopbackServer(_ => "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var client = new OAuthClient(Options(Substitute.For<IIdentityResolver>()), NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => client.StartAuthorizationAsync(
                "alice.example.com", RedirectUri, new OAuthAuthorizationOptions { ServerUrl = $"https://localhost:{server.Port}" }));

        Assert.Equal("metadata_fetch_failed", ex.Error);
        Assert.Equal(0, server.Connections);
    }

    [Fact]
    public async Task ResolveFromIdentifier_DocumentNamingAnInternalUrl_IsRefusedWithoutConnecting()
    {
        // The adversarial probe: a DID document whose #atproto_pds is an internal admin URL.
        using var server = new LoopbackServer(_ => "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        var did = Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz");
        var document = DidDocs.Parse(did.Value, pds: $"http://127.0.0.1:{server.Port}/internal/admin/purge?confirm=yes&x=");
        var dids = Substitute.For<IDidResolver>();
        dids.ResolveAsync(did, Arg.Any<CancellationToken>()).Returns(document);
        using var identity = new IdentityResolver(dids, new HandleResolver(new IdentityResolverOptions { DnsOverHttpsUrl = null }));
        using var discovery = new AuthorizationServerDiscovery(null, NullLogger.Instance, identity);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.ResolveFromIdentifierAsync(did.Value));

        Assert.Equal("invalid_server_url", ex.Error);
        Assert.Equal(0, server.Connections);
    }

    [Theory]
    [InlineData("http://pds.example.com")]
    [InlineData("https://pds.example.com/?next=/admin")]
    [InlineData("https://pds.example.com/#frag")]
    [InlineData("ftp://pds.example.com")]
    public async Task FetchMetadata_UnusableUrl_IsRefusedWithoutARequest(string url)
    {
        var handler = new ScriptedHandler(_ => ScriptedHandler.Json("{}"));
        using var discovery = new AuthorizationServerDiscovery(new HttpClient(handler), NullLogger.Instance, Substitute.For<IIdentityResolver>());

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.FetchProtectedResourceMetadataAsync(url));

        Assert.Equal("invalid_server_url", ex.Error);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task FetchMetadata_DevelopmentOptOut_ReachesALocalPds()
    {
        const string body = """{"resource":"http://127.0.0.1","authorization_servers":["http://127.0.0.1"]}""";
        using var server = new LoopbackServer(_ =>
            $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}");
        using var discovery = new AuthorizationServerDiscovery(null, NullLogger.Instance, Substitute.For<IIdentityResolver>(), allowPrivateNetworks: true);

        var metadata = await discovery.FetchProtectedResourceMetadataAsync($"http://127.0.0.1:{server.Port}");

        Assert.Equal("http://127.0.0.1", Assert.Single(metadata.AuthorizationServers!));
    }

    // ── What comes back ──────────────────────────────────────

    public static TheoryData<string, Func<HttpRequestMessage, HttpResponseMessage>> Failures => new()
    {
        { "metadata_fetch_failed", _ => ScriptedHandler.Status(HttpStatusCode.InternalServerError) },
        { "metadata_fetch_failed", _ => ScriptedHandler.Json("{\"x\":\"" + new string('a', 70 * 1024) + "\"}") },
        {
            "metadata_fetch_failed", _ => new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("https://elsewhere.example.net/.well-known/oauth-protected-resource") },
                Content = new StringContent(""),
            }
        },
        {
            "metadata_fetch_failed", _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://elsewhere.example.net/.well-known/oauth-protected-resource"),
            }
        },
        { "invalid_metadata", _ => ScriptedHandler.Json("{\"authorization_servers\":") },
        { "invalid_metadata", _ => ScriptedHandler.Json("[1,2,3]") },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task FetchMetadata_UnusableAnswer_IsAnOAuthException(string error, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        using var discovery = new AuthorizationServerDiscovery(
            new HttpClient(new ScriptedHandler(respond)), NullLogger.Instance, Substitute.For<IIdentityResolver>());

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.FetchProtectedResourceMetadataAsync("https://pds.example.com"));

        Assert.Equal(error, ex.Error);
    }
}
