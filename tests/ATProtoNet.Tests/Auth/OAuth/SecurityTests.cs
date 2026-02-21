using System.Net;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// Tests for security-sensitive validation logic in the OAuth flow.
/// </summary>
public class SecurityTests
{
    // ──────────────────────────────────────────────────────────
    //  SSRF: ValidateDidWebHost (via FetchDidDocumentAsync)
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    [InlineData("127.255.255.255")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.255.255")]
    [InlineData("169.254.1.1")]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    [InlineData("[::1]")]
    public async Task FetchDidDocument_BlocksPrivateAddresses(string host)
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var discovery = new AuthorizationServerDiscovery(httpClient, logger);

        var did = $"did:web:{host}";

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => discovery.FetchDidDocumentAsync(did));

        Assert.True(
            ex.Message.Contains("private address", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("IP address", StringComparison.OrdinalIgnoreCase),
            $"Expected private/IP address error, got: {ex.Message}");
    }

    [Theory]
    [InlineData("172.15.255.255")] // Just below 172.16.0.0/12 — should be allowed
    [InlineData("172.32.0.1")]     // Just above 172.31.255.255 — should be allowed
    [InlineData("192.169.0.1")]    // Outside 192.168.0.0/16
    [InlineData("11.0.0.1")]       // Outside 10.0.0.0/8
    public async Task FetchDidDocument_AllowsPublicIps(string host)
    {
        // These IPs should NOT be blocked by SSRF checks.
        // They'll fail for network reasons but should NOT throw "private address".
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var discovery = new AuthorizationServerDiscovery(httpClient, logger);

        var did = $"did:web:{host}";

        // Should not throw OAuthException with "private address"
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => discovery.FetchDidDocumentAsync(did));

        if (ex is OAuthException oex)
        {
            Assert.DoesNotContain("private address", oex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task FetchDidDocument_RejectsEmptyHost(string host)
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var discovery = new AuthorizationServerDiscovery(httpClient, logger);

        var did = $"did:web:{host}";

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => discovery.FetchDidDocumentAsync(did));

        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("host?query")]
    [InlineData("host#fragment")]
    [InlineData("host@user")]
    [InlineData("host with spaces")]
    public async Task FetchDidDocument_RejectsInvalidCharacters(string host)
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var discovery = new AuthorizationServerDiscovery(httpClient, logger);

        var did = $"did:web:{host}";

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => discovery.FetchDidDocumentAsync(did));

        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────
    //  Handle validation (SSRF prevention)
    // ──────────────────────────────────────────────────────────

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
    public async Task ResolveHandle_RejectsInvalidFormats(string handle)
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var discovery = new AuthorizationServerDiscovery(httpClient, logger);

        await Assert.ThrowsAsync<OAuthException>(
            () => discovery.ResolveHandleToDidAsync(handle));
    }

    // ──────────────────────────────────────────────────────────
    //  TLS enforcement in XrpcClient.SetBaseUrl
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://evil.example.com")]
    [InlineData("http://192.168.1.1:8080")]
    public void SetBaseUrl_RejectsNonTlsPublicUrls(string url)
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("https://example.com/") };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var xrpc = new XrpcClient(httpClient, logger);

        Assert.Throws<ArgumentException>(() => xrpc.SetBaseUrl(url));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://bsky.social")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:3000")]
    [InlineData("http://[::1]:5000")]
    public void SetBaseUrl_AcceptsValidUrls(string url)
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("https://example.com/") };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var xrpc = new XrpcClient(httpClient, logger);

        // Should not throw
        xrpc.SetBaseUrl(url);
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

    // ──────────────────────────────────────────────────────────
    //  IPv6 bracket hosts are blocked
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("[::1]")]
    [InlineData("[fe80::1]")]
    [InlineData("[fc00::1]")]
    public async Task FetchDidDocument_BlocksIPv6BracketHosts(string host)
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var discovery = new AuthorizationServerDiscovery(httpClient, logger);

        var did = $"did:web:{host}";

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => discovery.FetchDidDocumentAsync(did));

        Assert.True(
            ex.Message.Contains("private address", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("IP address", StringComparison.OrdinalIgnoreCase),
            $"Expected private address or IP address error, got: {ex.Message}");
    }
}
