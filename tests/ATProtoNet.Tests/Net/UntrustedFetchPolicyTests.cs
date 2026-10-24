using System.Net;
using System.Net.Sockets;
using ATProtoNet.Identity;
using ATProtoNet.Net;

namespace ATProtoNet.Tests.Net;

/// <summary>Which addresses a connection may reach, and the hardened handler that enforces it over real sockets.</summary>
public class UntrustedFetchPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly UntrustedRequest Request = new() { MaxBodyBytes = 64 * 1024 };

    public static TheoryData<string, bool> Addresses => new()
    {
        // Public IPv4
        { "8.8.8.8", true },
        { "1.1.1.1", true },
        { "100.63.255.255", true },
        { "100.128.0.0", true },
        { "172.15.255.255", true },
        { "172.32.0.0", true },
        { "198.17.255.255", true },
        { "198.20.0.0", true },

        // "This network", private, loopback, link-local (cloud metadata), CGNAT
        { "0.0.0.0", false },
        { "0.1.2.3", false },
        { "10.0.0.1", false },
        { "10.255.255.255", false },
        { "100.64.0.1", false },
        { "100.127.255.255", false },
        { "127.0.0.1", false },
        { "127.255.255.254", false },
        { "169.254.169.254", false },
        { "172.16.0.1", false },
        { "172.31.255.255", false },
        { "192.168.1.1", false },

        // Protocol assignments, documentation, 6to4 relay, benchmarking, multicast, reserved
        { "192.0.0.8", false },
        { "192.0.2.1", false },
        { "192.88.99.1", false },
        { "198.18.0.1", false },
        { "198.19.255.255", false },
        { "198.51.100.7", false },
        { "203.0.113.9", false },
        { "224.0.0.1", false },
        { "239.255.255.250", false },
        { "240.0.0.1", false },
        { "255.255.255.255", false },

        // Public IPv6
        { "2606:4700:4700::1111", true },
        { "2001:4860:4860::8888", true },
        { "2a00:1450:4001::1", true },

        // IPv4 carried in IPv6: judged by the IPv4 address it names
        { "::ffff:8.8.8.8", true },
        { "::ffff:127.0.0.1", false },
        { "::ffff:10.1.2.3", false },
        { "64:ff9b::8.8.8.8", true },
        { "64:ff9b::a00:1", false },
        { "64:ff9b:1::1", false },
        { "::127.0.0.1", false },

        // Unspecified, loopback, discard, Teredo/protocol assignments, documentation, 6to4,
        // unique local, link-local, site-local, multicast
        { "::", false },
        { "::1", false },
        { "100::1", false },
        { "2001::1", false },
        { "2001:db8::1", false },
        { "2002:c0a8:101::1", false },
        { "3fff::1", false },
        { "fc00::1", false },
        { "fd12:3456::1", false },
        { "fe80::1", false },
        { "fec0::1", false },
        { "ff02::1", false },
    };

    [Theory]
    [Trait("Spec", "IDN-2")]
    [MemberData(nameof(Addresses))]
    public void IsPublicAddress_Table_MatchesPolicy(string address, bool expected) =>
        Assert.Equal(expected, UntrustedFetchPolicy.IsPublicAddress(IPAddress.Parse(address)));

    [Fact]
    [Trait("Spec", "IDN-1")]
    public async Task HardenedHandler_NameResolvingToLoopback_IsRefusedWithoutConnecting()
    {
        using var http = new UntrustedHttp();

        // "localhost" is a name, not an address: the refusal comes from what it resolves to. Nothing listens on 443, so
        // a connection attempt would be a Network failure, not Blocked.
        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => http.GetAsync(new Uri("https://localhost/.well-known/did.json"), Request, Ct));

        Assert.Equal(UntrustedFetchError.Blocked, ex.Error);
        Assert.True(http.IsEnforced);
    }

    [Theory]
    [Trait("Spec", "IDN-1")]
    [Trait("Spec", "IDN-8")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("https://10.0.0.1/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://[::ffff:127.0.0.1]/")]
    public async Task HardenedHandler_PrivateAddressLiteral_IsRefused(string url)
    {
        using var http = new UntrustedHttp();

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => http.GetAsync(new Uri(url), Request, Ct));

        Assert.Equal(UntrustedFetchError.Blocked, ex.Error);
    }

    [Fact]
    [Trait("Spec", "IDN-8")]
    public async Task HardenedHandler_LoopbackPort_IsRefusedWithoutTheListenerSeeingAConnection()
    {
        using var server = new LoopbackServer(_ => LoopbackServer.Response("200 OK", "", "did:plc:dev"));
        using var http = new UntrustedHttp();

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => http.GetAsync(new Uri($"https://localhost:{server.Port}/.well-known/atproto-did"), Request, Ct));

        Assert.Equal(UntrustedFetchError.Blocked, ex.Error);
        Assert.Equal(0, server.Connections);
    }

    [Fact]
    [Trait("Spec", "IDN-8")]
    public async Task DevelopmentHandler_Loopback_IsReachable()
    {
        using var server = new LoopbackServer(_ => LoopbackServer.Response("200 OK", "Content-Type: text/plain\r\n", "did:plc:dev"));
        using var http = new UntrustedHttp(new() { AllowPrivateNetworks = true });

        var response = await http.GetAsync(new Uri($"http://localhost:{server.Port}/.well-known/atproto-did"), Request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("did:plc:dev", System.Text.Encoding.UTF8.GetString(response.Body.Span));
        Assert.False(http.IsEnforced);
    }

    [Fact]
    [Trait("Spec", "IDN-3")]
    [Trait("Spec", "IDN-6")]
    public async Task Handler_RedirectToAnotherOrigin_IsRefusedWithoutFollowing()
    {
        using var target = new LoopbackServer(_ => LoopbackServer.Response("200 OK", "", "did:plc:elsewhere"));
        using var origin = new LoopbackServer(_ => $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{target.Port}/anything\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var http = new UntrustedHttp(new() { AllowPrivateNetworks = true });

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => http.GetAsync(new Uri($"http://localhost:{origin.Port}/"), Request with { MaxSameHostRedirects = 3 }, Ct));

        Assert.Equal(UntrustedFetchError.RedirectRefused, ex.Error);
        Assert.Equal(HttpStatusCode.Found, ex.Status);
        Assert.Equal(0, target.Connections);
    }

    [Theory]
    [Trait("Spec", "IDN-3")]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public async Task Handler_EncodedBody_IsNotDecompressed(string encoding)
    {
        const string body = "\x1f\x8b\x08\x00garbage";
        using var server = new LoopbackServer(_ => LoopbackServer.Response("200 OK", $"Content-Encoding: {encoding}\r\n", body));
        using var http = new UntrustedHttp(new() { AllowPrivateNetworks = true });

        var response = await http.GetAsync(new Uri($"http://localhost:{server.Port}/"), Request, Ct);

        Assert.Equal(System.Text.Encoding.Latin1.GetBytes(body), response.Body.ToArray());
    }

    [Fact]
    [Trait("Spec", "IDN-3")]
    public async Task Handler_HeadersOverTheLimit_AreRefused()
    {
        using var server = new LoopbackServer(_ => LoopbackServer.Response("200 OK", $"X-Padding: {new string('x', 4096)}\r\n", "ok"));
        using var http = new UntrustedHttp(new() { AllowPrivateNetworks = true, MaxResponseHeadersBytes = 1024 });

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => http.GetAsync(new Uri($"http://localhost:{server.Port}/"), Request, Ct));

        Assert.Equal(UntrustedFetchError.Network, ex.Error);
    }

    [Fact]
    [Trait("Spec", "IDN-5")]
    public async Task Handler_BodyCutShort_IsNetwork()
    {
        // Content-Length promises 1000 bytes; the host sends six and hangs up.
        using var server = new LoopbackServer(_ => "HTTP/1.1 200 OK\r\nContent-Length: 1000\r\nConnection: close\r\n\r\n{\"id\":");
        using var http = new UntrustedHttp(new() { AllowPrivateNetworks = true });

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => http.GetAsync(new Uri($"http://localhost:{server.Port}/"), Request, Ct));

        Assert.Equal(UntrustedFetchError.Network, ex.Error);
    }

    [Fact]
    [Trait("Spec", "IDN-5")]
    [Trait("Spec", "IDN-10")]
    public async Task DidWeb_BodyCutShortOverTheSocket_IsUnavailableWithANetworkError()
    {
        using var server = new LoopbackServer(_ => "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 1000\r\nConnection: close\r\n\r\n{\"id\":");
        using var http = new UntrustedHttp(new() { AllowPrivateNetworks = true });
        var web = new DidWebResolver(http);

        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => web.ResolveAsync(Did.Parse($"did:web:localhost%3A{server.Port}"), Ct));

        Assert.Equal(IdentityResolutionError.Unavailable, ex.Error);
        Assert.Equal(UntrustedFetchError.Network, Assert.IsType<UntrustedFetchException>(ex.InnerException).Error);
    }

    [Theory]
    [Trait("Spec", "IDN-3")]
    [Trait("Spec", "IDN-24")]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public async Task DidWeb_BodyThatDoesNotDecode_IsAnInvalidDocument(string encoding)
    {
        using var server = new LoopbackServer(_ => LoopbackServer.Response(
            "200 OK", $"Content-Type: application/json\r\nContent-Encoding: {encoding}\r\n", "\x1f\x8b\x08\x00garbagegarbagegarbage"));
        using var http = new UntrustedHttp(new() { AllowPrivateNetworks = true });
        var web = new DidWebResolver(http);

        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => web.ResolveAsync(Did.Parse($"did:web:localhost%3A{server.Port}"), Ct));

        Assert.Equal(IdentityResolutionError.InvalidDocument, ex.Error);
    }
}
