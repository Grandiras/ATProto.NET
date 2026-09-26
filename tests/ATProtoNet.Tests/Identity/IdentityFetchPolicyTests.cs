using System.Net;
using System.Net.Sockets;
using System.Text;
using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// The SSRF policy every identity fetch goes through: which addresses a connection may reach,
/// which <c>did:web</c> identifiers turn into a request at all, which handles are resolved, and
/// what the development opt-out lifts.
/// </summary>
public class IdentityFetchPolicyTests
{
    // ─── Addresses, checked after DNS ────────────────────────

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
    [MemberData(nameof(Addresses))]
    public void IsPublicAddress_Table_MatchesPolicy(string address, bool expected) =>
        Assert.Equal(expected, IdentityNetworkPolicy.IsPublicAddress(IPAddress.Parse(address)));

    // ─── did:web → request URL ───────────────────────────────

    public static TheoryData<string, bool, string?, DidResolutionErrorKind?> DidWebUrls => new()
    {
        { "did:web:example.com", false, "https://example.com/.well-known/did.json", null },
        { "did:web:api.bsky.app", false, "https://api.bsky.app/.well-known/did.json", null },
        { "did:web:Example.COM", false, "https://example.com/.well-known/did.json", null },
        { "did:web:xn--bcher-kva.ch", false, "https://xn--bcher-kva.ch/.well-known/did.json", null },
        { "did:web:example.com", true, "https://example.com/.well-known/did.json", null },

        // Localhost is a development affordance only, and the only host a port is allowed on.
        { "did:web:localhost", false, null, DidResolutionErrorKind.Blocked },
        { "did:web:localhost%3A2583", false, null, DidResolutionErrorKind.Blocked },
        { "did:web:localhost", true, "http://localhost/.well-known/did.json", null },
        { "did:web:localhost%3A2583", true, "http://localhost:2583/.well-known/did.json", null },
        { "did:web:LOCALHOST%3a2583", true, "http://localhost:2583/.well-known/did.json", null },

        // A port on any other host is refused, opt-out or not.
        { "did:web:example.com%3A8443", false, null, DidResolutionErrorKind.InvalidDid },
        { "did:web:example.com%3A8443", true, null, DidResolutionErrorKind.InvalidDid },
        { "did:web:internal.corp%3A6379", false, null, DidResolutionErrorKind.InvalidDid },
        { "did:web:localhost%3A", true, null, DidResolutionErrorKind.InvalidDid },
        { "did:web:localhost%3A99999", true, null, DidResolutionErrorKind.InvalidDid },

        // No path-based did:web in atproto.
        { "did:web:example.com:user:alice", false, null, DidResolutionErrorKind.InvalidDid },
        { "did:web:example.com:user:alice", true, null, DidResolutionErrorKind.InvalidDid },

        // A hostname, not an address, and nothing that reshapes the URL once decoded.
        { "did:web:127.0.0.1", false, null, DidResolutionErrorKind.InvalidDid },
        { "did:web:127.0.0.1", true, null, DidResolutionErrorKind.InvalidDid },
        { "did:web:169.254.169.254", false, null, DidResolutionErrorKind.InvalidDid },
        { "did:web:example.com%2Fevil", false, null, DidResolutionErrorKind.InvalidDid },
        { "did:web:evil.com%40example.com", false, null, DidResolutionErrorKind.InvalidDid },
        { "did:web:example.com%23frag", false, null, DidResolutionErrorKind.InvalidDid },
        { "did:web:-bad.example.com", false, null, DidResolutionErrorKind.InvalidDid },
        { "did:web:bad-.example.com", false, null, DidResolutionErrorKind.InvalidDid },
        { "did:web:example..com", false, null, DidResolutionErrorKind.InvalidDid },
    };

    [Theory]
    [MemberData(nameof(DidWebUrls))]
    public void DidWebResolutionUrl_Table_MatchesPolicy(
        string did, bool allowPrivateNetworks, string? expectedUrl, DidResolutionErrorKind? expectedError)
    {
        if (expectedError is { } kind)
        {
            var ex = Assert.Throws<DidResolutionException>(
                () => DidWebResolver.BuildResolutionUrl(Did.Parse(did), allowPrivateNetworks));
            Assert.Equal(kind, ex.Kind);
        }
        else
        {
            Assert.Equal(expectedUrl, DidWebResolver.BuildResolutionUrl(Did.Parse(did), allowPrivateNetworks).ToString());
        }
    }

    // ─── Configured services (PLC directory, DNS-over-HTTPS) ─

    public static TheoryData<string, bool, bool> ServiceUrls => new()
    {
        { "https://plc.directory", false, true },
        { "https://plc.example.com:8443/mirror/", false, true },
        { "http://plc.directory", false, false },
        { "http://localhost:2582", false, false },
        { "http://localhost:2582", true, true },
        { "http://10.0.0.5:2582", true, true },
        { "ftp://plc.directory", true, false },
        { "https://plc.directory/?q=1", false, false },
        { "https://plc.directory/#x", false, false },
    };

    [Theory]
    [MemberData(nameof(ServiceUrls))]
    public void ValidateServiceUrl_Table_MatchesPolicy(string url, bool allowPrivateNetworks, bool accepted)
    {
        if (accepted)
            IdentityNetworkPolicy.ValidateServiceUrl(new Uri(url), allowPrivateNetworks, "url");
        else
            Assert.Throws<ArgumentException>(
                () => IdentityNetworkPolicy.ValidateServiceUrl(new Uri(url), allowPrivateNetworks, "url"));
    }

    // ─── Handles ─────────────────────────────────────────────

    public static TheoryData<string, bool, bool> Handles => new()
    {
        { "alice.bsky.social", false, true },
        { "example.com", false, true },
        { "alice.test", false, false },
        { "alice.test", true, true },
        { "alice.local", true, false },
        { "alice.localhost", true, false },
        { "alice.internal", false, false },
        { "alice.arpa", false, false },
        { "alice.onion", false, false },
        { "alice.alt", false, false },
        { "alice.example", false, false },
        { "handle.invalid", true, false },
    };

    [Theory]
    [MemberData(nameof(Handles))]
    public void IsResolvableHandle_Table_MatchesPolicy(string handle, bool allowPrivateNetworks, bool expected) =>
        Assert.Equal(expected, HandleResolver.IsResolvable(Handle.Parse(handle), allowPrivateNetworks));

    // ─── The connect callback ────────────────────────────────

    [Fact]
    public async Task HardenedHandler_NameResolvingToLoopback_IsRefusedWithoutConnecting()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new HttpClient(IdentityNetworkPolicy.CreateHandler(allowPrivateNetworks: false));

        // "localhost" is a name, not an address: the refusal comes from what it resolves to.
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync($"https://localhost:{port}/.well-known/did.json"));

        Assert.True(IdentityNetworkPolicy.IsBlocked(ex), $"Expected a policy refusal, got: {ex}");
        Assert.False(listener.Pending(), "The refused request must not have opened a connection.");
    }

    [Fact]
    public async Task HardenedHandler_PrivateAddressLiteral_IsRefused()
    {
        using var client = new HttpClient(IdentityNetworkPolicy.CreateHandler(allowPrivateNetworks: false));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("https://169.254.169.254/latest/meta-data/"));

        Assert.True(IdentityNetworkPolicy.IsBlocked(ex), $"Expected a policy refusal, got: {ex}");
    }

    [Fact]
    public async Task DevelopmentHandler_Loopback_IsReachable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var served = ServeOnceAsync(listener, "did:plc:dev");

        using var client = new HttpClient(IdentityNetworkPolicy.CreateHandler(allowPrivateNetworks: true));
        var body = await client.GetStringAsync($"http://localhost:{port}/.well-known/atproto-did");

        Assert.Equal("did:plc:dev", body);
        await served;
    }

    private static async Task ServeOnceAsync(TcpListener listener, string body)
    {
        using var socket = await listener.AcceptSocketAsync();
        var buffer = new byte[4096];
        var request = new StringBuilder();
        while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await socket.ReceiveAsync(buffer);
            if (read == 0)
                break;
            request.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        var payload = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        await socket.SendAsync(head);
        await socket.SendAsync(payload);
        socket.Shutdown(SocketShutdown.Both);
    }
}
