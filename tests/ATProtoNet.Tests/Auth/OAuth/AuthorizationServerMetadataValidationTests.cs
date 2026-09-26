using System.Net;
using System.Text.Json.Nodes;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using ATProtoNet.Tests.Auth;
using ATProtoNet.Tests.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// The AT Protocol profile of authorization server metadata (exact issuer, HTTPS endpoints
/// without query or fragment, the three required capabilities), the protected-resource check,
/// signing in at an entryway, and the metadata cache.
/// </summary>
public class AuthorizationServerMetadataValidationTests
{
    private const string PdsUrl = "https://pds.example.com";

    private static string ResourceMetadata(string resource = PdsUrl, string authorizationServer = Issuer) =>
        $$"""{"resource":"{{resource}}","authorization_servers":["{{authorizationServer}}"]}""";

    /// <summary>Valid authorization server metadata with one member replaced (or removed, for null).</summary>
    private static string ServerMetadata(string? member = null, JsonNode? value = null)
    {
        var json = JsonNode.Parse(AuthorizationServerMetadataJson())!.AsObject();
        if (member is not null)
        {
            if (value is null)
                json.Remove(member);
            else
                json[member] = value;
        }

        return json.ToJsonString();
    }

    private static (AuthorizationServerDiscovery Discovery, ScriptedHandler Handler) Discovery(
        string resourceMetadata, string serverMetadata, bool allowPrivateNetworks = false)
    {
        var handler = new ScriptedHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/.well-known/oauth-protected-resource", StringComparison.Ordinal)
                ? ScriptedHandler.Json(resourceMetadata)
                : request.RequestUri.AbsolutePath.EndsWith("/.well-known/oauth-authorization-server", StringComparison.Ordinal)
                    ? ScriptedHandler.Json(serverMetadata)
                    : ScriptedHandler.Status(HttpStatusCode.NotFound));
        var discovery = new AuthorizationServerDiscovery(
            new HttpClient(handler), NullLogger.Instance, Substitute.For<IIdentityResolver>(), allowPrivateNetworks);
        return (discovery, handler);
    }

    [Fact]
    public async Task ValidMetadata_IsAccepted()
    {
        var (discovery, _) = Discovery(ResourceMetadata(), ServerMetadata());

        var metadata = await discovery.ResolveAuthorizationServerAsync(PdsUrl);

        Assert.Equal(Issuer, metadata.Issuer);
    }

    // ── Issuer ────────────────────────────────────────────────

    [Theory]
    [InlineData("https://auth.example.com:8443")]    // another port
    [InlineData("https://auth.example.com/tenant")]  // another path
    [InlineData("https://auth.example.com/")]        // not canonical
    [InlineData("https://AUTH.example.com")]         // not canonical
    [InlineData("http://auth.example.com")]          // another scheme
    [InlineData("https://evil.example.com")]
    public async Task AnIssuerThatIsNotExactlyTheExpectedOne_IsRefused(string issuer)
    {
        var (discovery, _) = Discovery(ResourceMetadata(), ServerMetadata("issuer", issuer));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.ResolveAuthorizationServerAsync(PdsUrl));

        Assert.Equal("issuer_mismatch", ex.Error);
    }

    [Fact]
    public async Task AnIssuerWithAPortOrPath_MatchesWhenTheResourceNamesExactlyIt()
    {
        const string issuer = "https://auth.example.com:8443/tenant";
        var (discovery, _) = Discovery(
            ResourceMetadata(authorizationServer: issuer),
            AuthorizationServerMetadataJson(issuer));

        var metadata = await discovery.ResolveAuthorizationServerAsync(PdsUrl);

        Assert.Equal(issuer, metadata.Issuer);
    }

    // ── Endpoints ─────────────────────────────────────────────

    [Theory]
    [InlineData("token_endpoint", "http://auth.example.com/oauth/token")]
    [InlineData("token_endpoint", "https://auth.example.com/oauth/token?tenant=1")]
    [InlineData("token_endpoint", "https://auth.example.com/oauth/token#frag")]
    [InlineData("token_endpoint", "/oauth/token")]
    [InlineData("token_endpoint", "https://user:pass@auth.example.com/oauth/token")]
    [InlineData("authorization_endpoint", "javascript:alert(1)")]
    [InlineData("pushed_authorization_request_endpoint", "ftp://auth.example.com/oauth/par")]
    [InlineData("revocation_endpoint", "https://auth.example.com/oauth/revoke?x=1")]
    public async Task AnEndpointThatIsNotAnHttpsUrlWithoutQueryOrFragment_IsRefused(string member, string value)
    {
        var (discovery, _) = Discovery(ResourceMetadata(), ServerMetadata(member, value));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.ResolveAuthorizationServerAsync(PdsUrl));

        Assert.Equal("invalid_metadata", ex.Error);
    }

    [Theory]
    [InlineData("authorization_endpoint")]
    [InlineData("token_endpoint")]
    [InlineData("pushed_authorization_request_endpoint")]
    public async Task AMissingEndpoint_IsRefused(string member)
    {
        var (discovery, _) = Discovery(ResourceMetadata(), ServerMetadata(member));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.ResolveAuthorizationServerAsync(PdsUrl));

        Assert.Equal("invalid_metadata", ex.Error);
    }

    [Fact]
    public async Task NoRevocationEndpoint_IsAccepted()
    {
        var (discovery, _) = Discovery(ResourceMetadata(), ServerMetadata("revocation_endpoint"));

        var metadata = await discovery.ResolveAuthorizationServerAsync(PdsUrl);

        Assert.Null(metadata.RevocationEndpoint);
    }

    [Fact]
    public async Task UnderTheDevelopmentOptOut_PlainHttpEndpointsAreAccepted()
    {
        const string issuer = "http://localhost:2583";
        var (discovery, _) = Discovery(
            ResourceMetadata("http://localhost:2583", issuer), AuthorizationServerMetadataJson(issuer), allowPrivateNetworks: true);

        var metadata = await discovery.ResolveAuthorizationServerAsync("http://localhost:2583");

        Assert.Equal(issuer, metadata.Issuer);
    }

    // ── Required capabilities ─────────────────────────────────

    [Theory]
    [InlineData("require_pushed_authorization_requests")]
    [InlineData("authorization_response_iss_parameter_supported")]
    [InlineData("client_id_metadata_document_supported")]
    public async Task ARequiredCapabilityMissingOrFalse_IsRefused(string member)
    {
        var (missing, _) = Discovery(ResourceMetadata(), ServerMetadata(member));
        var (off, _) = Discovery(ResourceMetadata(), ServerMetadata(member, false));

        Assert.Equal("invalid_metadata", (await Assert.ThrowsAsync<OAuthException>(() => missing.ResolveAuthorizationServerAsync(PdsUrl))).Error);
        Assert.Equal("invalid_metadata", (await Assert.ThrowsAsync<OAuthException>(() => off.ResolveAuthorizationServerAsync(PdsUrl))).Error);
    }

    // ── Protected resource ────────────────────────────────────

    [Theory]
    [InlineData("https://other-pds.example.com")]
    [InlineData("https://pds.example.com:8443")]
    [InlineData("https://pds.example.com/?x=1")]
    public async Task ResourceMetadataDescribingAnotherResource_IsRefused(string resource)
    {
        var (discovery, handler) = Discovery(ResourceMetadata(resource), ServerMetadata());

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.ResolveAuthorizationServerAsync(PdsUrl));

        Assert.Equal("invalid_resource_metadata", ex.Error);
        Assert.Equal(1, handler.Count); // the authorization server was never asked
    }

    [Fact]
    public async Task ResourceMetadataWithoutAResource_IsRefused()
    {
        var (discovery, _) = Discovery($$"""{"authorization_servers":["{{Issuer}}"]}""", ServerMetadata());

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.ResolveAuthorizationServerAsync(PdsUrl));

        Assert.Equal("invalid_resource_metadata", ex.Error);
    }

    [Fact]
    public async Task AResourceWithATrailingSlash_StillNamesThePds()
    {
        var (discovery, _) = Discovery(ResourceMetadata(PdsUrl + "/"), ServerMetadata());

        var metadata = await discovery.ResolveAuthorizationServerAsync(PdsUrl);

        Assert.Equal(Issuer, metadata.Issuer);
    }

    [Theory]
    [InlineData("https://auth.example.com/")]
    [InlineData("auth.example.com")]
    [InlineData("https://auth.example.com?x=1")]
    public async Task AnAuthorizationServerThatIsNotAnIssuerIdentifier_IsRefused(string authorizationServer)
    {
        var (discovery, _) = Discovery(ResourceMetadata(authorizationServer: authorizationServer), ServerMetadata());

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.ResolveAuthorizationServerAsync(PdsUrl));

        Assert.Equal("invalid_resource_metadata", ex.Error);
    }

    [Theory]
    [InlineData("scopes_supported", """["transition:generic"]""", "unsupported_scope")]
    [InlineData("dpop_signing_alg_values_supported", """["RS256","ES384"]""", "unsupported_dpop_alg")]
    public async Task MetadataWithoutAtprotoOrES256_IsRefused(string member, string value, string error)
    {
        var (discovery, _) = Discovery(ResourceMetadata(), ServerMetadata(member, JsonNode.Parse(value)));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.ResolveAuthorizationServerAsync(PdsUrl));

        Assert.Equal(error, ex.Error);
    }

    [Fact]
    public async Task ResourceMetadataNamingTwoAuthorizationServers_IsRefused()
    {
        var (discovery, _) = Discovery(
            $$"""{"resource":"{{PdsUrl}}","authorization_servers":["{{Issuer}}","https://other-auth.example.com"]}""",
            ServerMetadata());

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.ResolveAuthorizationServerAsync(PdsUrl));

        Assert.Equal("invalid_resource_metadata", ex.Error);
    }

    [Fact]
    public async Task AnAuthorizationServerListingOtherResources_IsRefusedForThisPds()
    {
        var (discovery, _) = Discovery(
            ResourceMetadata(), ServerMetadata("protected_resources", JsonNode.Parse("""["https://other-pds.example.com"]""")));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.ResolveAuthorizationServerAsync(PdsUrl));

        Assert.Equal("invalid_resource_metadata", ex.Error);
    }

    [Fact]
    public async Task AnAuthorizationServerListingThisPds_IsAccepted()
    {
        var (discovery, _) = Discovery(
            ResourceMetadata(), ServerMetadata("protected_resources", JsonNode.Parse($$"""["https://other-pds.example.com","{{PdsUrl}}"]""")));

        var metadata = await discovery.ResolveAuthorizationServerAsync(PdsUrl);

        Assert.Equal(Issuer, metadata.Issuer);
    }

    // ── Cache ─────────────────────────────────────────────────

    [Fact]
    public async Task Metadata_IsFetchedOncePerDocument()
    {
        var (discovery, handler) = Discovery(ResourceMetadata(), ServerMetadata());

        await discovery.ResolveAuthorizationServerAsync(PdsUrl);
        await discovery.ResolveAuthorizationServerAsync(PdsUrl);
        await discovery.FetchAuthorizationServerMetadataAsync(Issuer);

        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task CachedMetadata_IsHandedOutAsAFreshCopy()
    {
        var (discovery, _) = Discovery(ResourceMetadata(), ServerMetadata());

        var first = await discovery.ResolveAuthorizationServerAsync(PdsUrl);
        first.TokenEndpoint = "https://evil.example.com/token";
        var second = await discovery.ResolveAuthorizationServerAsync(PdsUrl);

        Assert.Equal($"{Issuer}/oauth/token", second.TokenEndpoint);
    }

    [Fact]
    public async Task AFailedFetch_IsNotCached()
    {
        var calls = 0;
        var handler = new ScriptedHandler(_ =>
            Interlocked.Increment(ref calls) == 1
                ? ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable)
                : ScriptedHandler.Json(ResourceMetadata()));
        using var discovery = new AuthorizationServerDiscovery(
            new HttpClient(handler), NullLogger.Instance, Substitute.For<IIdentityResolver>());

        await Assert.ThrowsAsync<OAuthException>(() => discovery.FetchProtectedResourceMetadataAsync(PdsUrl));
        var metadata = await discovery.FetchProtectedResourceMetadataAsync(PdsUrl);

        Assert.Equal(PdsUrl, metadata.Resource);
    }

    // ── Signing in at an entryway ─────────────────────────────

    [Fact]
    public async Task AServerWithoutResourceMetadata_IsTriedAsAnAuthorizationServer()
    {
        const string entryway = "https://entryway.example.com";
        var handler = new ScriptedHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/.well-known/oauth-authorization-server" => ScriptedHandler.Json(AuthorizationServerMetadataJson(entryway)),
            _ => ScriptedHandler.Status(HttpStatusCode.NotFound),
        });
        using var discovery = new AuthorizationServerDiscovery(
            new HttpClient(handler), NullLogger.Instance, Substitute.For<IIdentityResolver>());

        var metadata = await discovery.ResolveFromServerUrlAsync(entryway, CancellationToken.None);

        Assert.Equal(entryway, metadata.Issuer);
    }

    [Fact]
    public async Task ResolveFromServerUrlAsync_APds_FetchesItsResourceMetadataOnce()
    {
        var handler = new ScriptedHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/.well-known/oauth-protected-resource" => ScriptedHandler.Json(ResourceMetadata()),
            "/.well-known/oauth-authorization-server" => ScriptedHandler.Json(ServerMetadata()),
            _ => ScriptedHandler.Status(HttpStatusCode.NotFound),
        });

        // A clock past the cache lifetime at every reading, so nothing the cache holds hides a fetch.
        using var discovery = new AuthorizationServerDiscovery(
            new HttpClient(handler), NullLogger.Instance, Substitute.For<IIdentityResolver>())
        {
            TimeProvider = new RacingClock(),
        };

        var metadata = await discovery.ResolveFromServerUrlAsync(PdsUrl, CancellationToken.None);

        Assert.Equal(Issuer, metadata.Issuer);
        Assert.Equal(
            ["/.well-known/oauth-protected-resource", "/.well-known/oauth-authorization-server"],
            handler.Requests.Select(uri => uri.AbsolutePath));
    }

    /// <summary>A clock that moves an hour on with every reading.</summary>
    private sealed class RacingClock : TimeProvider
    {
        private long _hours;

        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.UnixEpoch.AddHours(Interlocked.Increment(ref _hours));
    }

    [Fact]
    public async Task AServerThatIsNeither_ReportsThePdsFailure()
    {
        var handler = new ScriptedHandler(_ => ScriptedHandler.Status(HttpStatusCode.NotFound));
        using var discovery = new AuthorizationServerDiscovery(
            new HttpClient(handler), NullLogger.Instance, Substitute.For<IIdentityResolver>());

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => discovery.ResolveFromServerUrlAsync("https://nowhere.example.com", CancellationToken.None));

        Assert.Equal("metadata_fetch_failed", ex.Error);
        Assert.Contains("oauth-protected-resource", ex.Message);
    }

    [Fact]
    public async Task SignIn_StartedAtAnEntryway_CompletesOnTheAccountsPds()
    {
        const string entryway = "https://entryway.example.com";
        using var server = new StubServer { ServesOAuthMetadata = false };
        server.Respond = r => (r.Uri.Host, r.Path) switch
        {
            ("entryway.example.com", "/.well-known/oauth-protected-resource") => JsonResponse("{}", HttpStatusCode.NotFound),
            ("entryway.example.com", "/.well-known/oauth-authorization-server") => JsonResponse(AuthorizationServerMetadataJson(entryway)),
            ("pds.example.com", "/.well-known/oauth-protected-resource") => JsonResponse(ResourceMetadata(PdsUrl, entryway)),
            ("entryway.example.com", _) => AuthorizationServer(r),
            _ => throw new InvalidOperationException($"Unexpected request to {r.Uri}"),
        };
        using var http = new HttpClient(server);
        using var oauth = OAuthClient(http);

        var authorization = await oauth.StartAuthorizationAsync(entryway, RedirectUri);
        var session = await oauth.CompleteAuthorizationAsync("code", authorization.State, entryway);

        Assert.StartsWith($"{entryway}/oauth/authorize?", authorization.AuthorizationUrl.AbsoluteUri);
        Assert.Single(server.To("/oauth/par"));
        Assert.Equal(Alice, session.Did);
        Assert.Equal(entryway, session.Issuer);

        // The PDS is the account's, from its DID document, not the entryway the login began at.
        Assert.Equal(new Uri(PdsUrl), session.ServiceEndpoint);
    }
}
