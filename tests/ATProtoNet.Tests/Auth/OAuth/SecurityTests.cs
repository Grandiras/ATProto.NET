using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// Tests for security-sensitive validation logic in the OAuth flow.
/// </summary>
public class SecurityTests
{
    // ──────────────────────────────────────────────────────────
    //  SSRF: identifiers are refused before any request is made
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("did:web:localhost")]
    [InlineData("did:web:LOCALHOST")]
    [InlineData("did:web:localhost%3A2583")]
    [InlineData("did:web:127.0.0.1")]
    [InlineData("did:web:10.0.0.1")]
    [InlineData("did:web:169.254.169.254")]
    [InlineData("did:web:internal.corp%3A6379")]
    [InlineData("did:web:example.com:user:alice")]
    public async Task ResolveFromIdentifier_RefusedDidWeb_IsInvalidDidWithoutARequest(string did)
    {
        var (discovery, handler) = CreateDiscovery();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.ResolveFromIdentifierAsync(did));

        Assert.Equal("invalid_did", ex.Error);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task ResolveFromIdentifier_UnsupportedDidMethod_IsReported()
    {
        var (discovery, handler) = CreateDiscovery();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => discovery.ResolveFromIdentifierAsync("did:key:z6Mkfriq1MqLBoPWecGoDLjguo1sB9brj6wT3qZ5BxkKpuP6"));

        Assert.Equal("unsupported_did_method", ex.Error);
        Assert.Equal(0, handler.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("single")]        // Must have at least 2 labels
    [InlineData("has spaces.com")]
    [InlineData("path/traversal.com")]
    [InlineData("..invalid.com")]
    [InlineData("invalid..com")]
    [InlineData(".invalid.com")]
    [InlineData("invalid.com.")]
    [InlineData("host?injection.com")]
    [InlineData("host#injection.com")]
    [InlineData("user@host.com")]
    [InlineData("did:not valid")]
    public async Task ResolveFromIdentifier_MalformedIdentifier_IsRefusedWithoutARequest(string identifier)
    {
        var (discovery, handler) = CreateDiscovery();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => discovery.ResolveFromIdentifierAsync(identifier));

        Assert.Contains(ex.Error, new[] { "invalid_handle", "invalid_did" });
        Assert.Equal(0, handler.Count);
    }

    /// <summary>
    /// Discovery with the SDK's default identity resolver, which fetches through its own
    /// policy-enforcing client; the handler only sees the OAuth metadata requests.
    /// </summary>
    private static (AuthorizationServerDiscovery Discovery, Identity.ScriptedHandler Handler) CreateDiscovery()
    {
        var handler = new Identity.ScriptedHandler(_ => Identity.ScriptedHandler.Status(System.Net.HttpStatusCode.NotFound));
        var discovery = new AuthorizationServerDiscovery(
            new HttpClient(handler),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            ATProtoNet.Identity.IdentityResolver.CreateDefault(new ATProtoNet.Identity.IdentityResolverOptions
            {
                DnsOverHttpsUrl = null,
                HandleResolutionTimeout = TimeSpan.FromMilliseconds(1),
            }));
        return (discovery, handler);
    }

    // ──────────────────────────────────────────────────────────
    //  TLS enforcement in XrpcClient.SetServiceUrl
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://evil.example.com")]
    [InlineData("http://192.168.1.1:8080")]
    [InlineData("ftp://example.com")]
    public void SetServiceUrl_RejectsNonTlsPublicUrls(string url)
    {
        using var httpClient = new HttpClient();
        var xrpc = new XrpcClient(httpClient, new Uri("https://example.com/"));

        Assert.Throws<ArgumentException>(() => xrpc.SetServiceUrl(new Uri(url)));
        Assert.Equal(new Uri("https://example.com/"), xrpc.ServiceUrl);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://bsky.social")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:3000")]
    [InlineData("http://[::1]:5000")]
    public void SetServiceUrl_AcceptsValidUrls(string url)
    {
        using var httpClient = new HttpClient();
        var xrpc = new XrpcClient(httpClient, new Uri("https://example.com/"));

        xrpc.SetServiceUrl(new Uri(url));

        Assert.Equal(new Uri(url.TrimEnd('/') + "/"), xrpc.ServiceUrl);
    }

    // ──────────────────────────────────────────────────────────
    //  Scope validation (exact token match, not substring)
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("atproto transition:generic", true)]
    [InlineData("atproto", true)]
    [InlineData("transition:generic atproto", true)]
    [InlineData("notatproto", false)]         // Substring should NOT match
    [InlineData("atproto2", false)]            // Prefix should NOT match
    [InlineData("my-atproto-scope", false)]    // Infix should NOT match
    [InlineData("", false)]
    public void ScopeValidation_UsesExactTokenMatch(string scope, bool shouldContainAtproto)
    {
        var tokens = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var contains = tokens.Contains("atproto", StringComparer.Ordinal);
        Assert.Equal(shouldContainAtproto, contains);
    }
}
