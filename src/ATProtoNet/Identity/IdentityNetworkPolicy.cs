using System.Net;
using System.Net.Sockets;
using ATProtoNet.Http;

namespace ATProtoNet.Identity;

/// <summary>
/// The SSRF policy behind every identity fetch: a <c>did:web</c> document, a PLC directory
/// lookup, a handle's <c>/.well-known/atproto-did</c> and the DNS-over-HTTPS query.
/// </summary>
/// <remarks>
/// <para>Identity hosts come from identifiers anyone can mint, and the services that resolve
/// them — a space server checking a token, a firehose consumer, an OAuth login — do it on behalf
/// of whoever sent the identifier. Without a policy, <c>did:web:internal.corp%3A6379</c> is a
/// request into the resolver's own network.</para>
/// <para>The address check runs in the connect callback, after DNS, against every address the
/// name resolved to, and the connection is made to those same addresses. Checking the name or a
/// URL instead would let a public-looking name that resolves to <c>10.0.0.1</c> through, and
/// resolving twice would let a rebinding DNS server answer differently the second time.</para>
/// <para>A proxy would make the checked address the proxy's rather than the target's, so the
/// hardened handler never uses one.</para>
/// <para>Redirects are not followed: a DID document, a PLC answer or OAuth metadata that
/// redirects elsewhere is refused, as <c>@atproto/identity</c> refuses it
/// (<c>redirect: 'error'</c>). The one caller that accepts a redirect, a handle's well-known,
/// follows same-host redirects itself.</para>
/// </remarks>
internal static class IdentityNetworkPolicy
{
    /// <summary>How long establishing a connection to an identity host may take.</summary>
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private static readonly Lazy<HttpMessageHandler> Hardened = new(() => CreateHandler(allowPrivateNetworks: false));
    private static readonly Lazy<HttpMessageHandler> Development = new(() => CreateHandler(allowPrivateNetworks: true));

    /// <summary>
    /// The shared handler for the given policy. SDK-owned identity clients are created over it and
    /// never dispose it, so they share one connection pool.
    /// </summary>
    internal static HttpMessageHandler SharedHandler(bool allowPrivateNetworks) =>
        allowPrivateNetworks ? Development.Value : Hardened.Value;

    /// <summary>Creates an <see cref="HttpClient"/> over <see cref="SharedHandler"/>.</summary>
    internal static HttpClient CreateClient(bool allowPrivateNetworks)
    {
        var client = new HttpClient(SharedHandler(allowPrivateNetworks), disposeHandler: false)
        {
            // Each fetch is bounded by its own budget; this is only the backstop.
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.TryParseAdd(AtProtoHttp.DefaultUserAgent);
        return client;
    }

    /// <summary>
    /// Creates a primary handler that enforces the policy, or, with
    /// <paramref name="allowPrivateNetworks"/>, one that only carries the connection settings.
    /// </summary>
    /// <remarks>
    /// Where sockets are unavailable (Blazor WebAssembly) the browser makes the connection and no
    /// address check is possible; the platform handler is returned.
    /// </remarks>
    internal static HttpMessageHandler CreateHandler(bool allowPrivateNetworks)
    {
        if (!SocketsHttpHandler.IsSupported)
            return new HttpClientHandler();

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = AtProtoHttp.PooledConnectionLifetime,
            ConnectTimeout = ConnectTimeout,
            AllowAutoRedirect = false,
            MaxResponseHeadersLength = 16,
        };

        if (!allowPrivateNetworks)
        {
            handler.UseProxy = false;
            handler.ConnectCallback = ConnectToPublicAddressAsync;
        }

        return handler;
    }

    /// <summary>
    /// Whether an address is public unicast: not loopback, private, link-local, CGNAT, multicast,
    /// documentation, benchmarking or otherwise reserved. IPv4 carried in IPv6 (mapped or NAT64)
    /// is judged by the IPv4 address.
    /// </summary>
    internal static bool IsPublicAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        Span<byte> bytes = stackalloc byte[16];
        if (!address.TryWriteBytes(bytes, out var length))
            return false;

        return length switch
        {
            4 => IsPublicV4(bytes[..4]),
            16 => IsPublicV6(bytes),
            _ => false,
        };
    }

    private static bool IsPublicV4(ReadOnlySpan<byte> b) =>
        !(b[0] == 0                                        // 0.0.0.0/8 "this network"
          || b[0] == 10                                    // 10.0.0.0/8 private
          || (b[0] == 100 && (b[1] & 0xC0) == 64)          // 100.64.0.0/10 CGNAT
          || b[0] == 127                                   // 127.0.0.0/8 loopback
          || (b[0] == 169 && b[1] == 254)                  // 169.254.0.0/16 link-local, cloud metadata
          || (b[0] == 172 && (b[1] & 0xF0) == 16)          // 172.16.0.0/12 private
          || (b[0] == 192 && b[1] == 0 && b[2] == 0)       // 192.0.0.0/24 protocol assignments
          || (b[0] == 192 && b[1] == 0 && b[2] == 2)       // 192.0.2.0/24 documentation
          || (b[0] == 192 && b[1] == 88 && b[2] == 99)     // 192.88.99.0/24 6to4 relay
          || (b[0] == 192 && b[1] == 168)                  // 192.168.0.0/16 private
          || (b[0] == 198 && (b[1] & 0xFE) == 18)          // 198.18.0.0/15 benchmarking
          || (b[0] == 198 && b[1] == 51 && b[2] == 100)    // 198.51.100.0/24 documentation
          || (b[0] == 203 && b[1] == 0 && b[2] == 113)     // 203.0.113.0/24 documentation
          || b[0] >= 224);                                 // multicast, reserved, broadcast

    private static bool IsPublicV6(ReadOnlySpan<byte> b)
    {
        // 64:ff9b::/96, the NAT64 well-known prefix, carries an IPv4 address in its last 32 bits.
        ReadOnlySpan<byte> nat64 = [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0];
        if (b[..12].SequenceEqual(nat64))
            return IsPublicV4(b[12..]);

        // Only 2000::/3 is global unicast: this excludes ::, ::1, IPv4-compatible, 100::/64,
        // fc00::/7, fe80::/10, fec0::/10 and ff00::/8 in one go.
        if ((b[0] & 0xE0) != 0x20)
            return false;

        return !((b[0] == 0x20 && b[1] == 0x01 && b[2] < 0x02)                   // 2001::/23 protocol assignments, Teredo
                 || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) // 2001:db8::/32 documentation
                 || (b[0] == 0x20 && b[1] == 0x02)                                // 2002::/16 6to4
                 || (b[0] == 0x3f && b[1] == 0xff && (b[2] & 0xF0) == 0));         // 3fff::/20 documentation
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host.Trim('[', ']');

        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

        if (addresses.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        // Every address, not just the first: a name answering with one public and one private
        // address would otherwise reach the private one whenever the public one is down.
        foreach (var address in addresses)
        {
            if (!IsPublicAddress(address))
                throw new IdentityFetchBlockedException($"'{host}' resolves to {address}, which is not a public address.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Whether a request failed because the policy refused its connection.</summary>
    internal static bool IsBlocked(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is IdentityFetchBlockedException)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Validates the URL of a configured identity service (a PLC directory, a DNS-over-HTTPS
    /// endpoint): absolute, <c>https</c> unless the development opt-out is set, and free of a
    /// query or fragment.
    /// </summary>
    /// <returns>The URL, normalized with a trailing <c>/</c>.</returns>
    /// <exception cref="ArgumentException">The URL is not acceptable.</exception>
    internal static Uri ValidateServiceUrl(Uri url, bool allowPrivateNetworks, string paramName)
    {
        ArgumentNullException.ThrowIfNull(url, paramName);

        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException($"'{url}' is not an absolute http(s) URL.", paramName);

        if (url.Scheme != Uri.UriSchemeHttps && !allowPrivateNetworks)
        {
            throw new ArgumentException(
                $"Identity service URL '{url}' must use HTTPS. Plain HTTP needs the development opt-out " +
                $"({nameof(IdentityResolverOptions)}.{nameof(IdentityResolverOptions.AllowPrivateNetworks)}).",
                paramName);
        }

        if (url.Query.Length > 0 || url.Fragment.Length > 0)
            throw new ArgumentException($"Identity service URL '{url}' must not have a query or fragment.", paramName);

        return AtProtoHttp.NormalizeBaseUrl(url);
    }
}

/// <summary>A connection the identity fetch policy refused.</summary>
internal sealed class IdentityFetchBlockedException(string message) : IOException(message);
