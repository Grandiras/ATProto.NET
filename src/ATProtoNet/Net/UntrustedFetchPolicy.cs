using System.Net;
using System.Net.Sockets;

namespace ATProtoNet.Net;

/// <summary>The limits every fetch of an attacker-chosen URL runs under: which addresses a connection may reach, and how long and how much a fetch may take.</summary>
/// <remarks>
/// Identity hosts, OAuth metadata and Lexicon authorities come from identifiers anyone can mint, and are fetched on behalf of
/// whoever sent the identifier. Without these limits, <c>did:web:internal.corp</c> is a request into the fetcher's own network.
/// </remarks>
public sealed record UntrustedFetchPolicy
{
    private static readonly IPNetwork[] ReservedV4 = Networks(
        "0.0.0.0/8 10.0.0.0/8 100.64.0.0/10 127.0.0.0/8 169.254.0.0/16 172.16.0.0/12 192.0.0.0/24 192.0.2.0/24 192.88.99.0/24 192.168.0.0/16 198.18.0.0/15 198.51.100.0/24 203.0.113.0/24 224.0.0.0/3");

    private static readonly IPNetwork[] ReservedV6 = Networks("2001::/23 2001:db8::/32 2002::/16 3fff::/20");

    private static readonly IPNetwork GlobalUnicast = IPNetwork.Parse("2000::/3");

    private static readonly IPNetwork Nat64 = IPNetwork.Parse("64:ff9b::/96");

    /// <summary>The development opt-out: also fetch <c>http</c> URLs, explicit ports, loopback and private addresses. Logged as a warning.</summary>
    public bool AllowPrivateNetworks { get; init; }

    /// <summary>How long establishing a connection may take.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The total budget of one fetch: waiting for a slot, connecting, headers and body.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The most fetches one <see cref="UntrustedHttp"/> runs at once; further fetches wait within their budget.</summary>
    public int MaxConcurrentFetches { get; init; } = 64;

    /// <summary>The most response-header bytes a fetch accepts.</summary>
    public int MaxResponseHeadersBytes { get; init; } = 16 * 1024;

    /// <summary>Whether <paramref name="address"/> is public unicast: not loopback, private, link-local, CGNAT, multicast, documentation, benchmarking or otherwise reserved.</summary>
    /// <param name="address">The address. IPv4 carried in IPv6 (mapped, or NAT64 <c>64:ff9b::/96</c>) is judged by its IPv4 address.</param>
    /// <returns>Whether a connection to it is allowed without <see cref="AllowPrivateNetworks"/>.</returns>
    public static bool IsPublicAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        else if (Nat64.Contains(address)) address = new IPAddress(address.GetAddressBytes().AsSpan(12));
        return address.AddressFamily == AddressFamily.InterNetwork
            ? !Array.Exists(ReservedV4, n => n.Contains(address))
            : GlobalUnicast.Contains(address) && !Array.Exists(ReservedV6, n => n.Contains(address));
    }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ConnectTimeout, TimeSpan.Zero, nameof(ConnectTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(RequestTimeout, TimeSpan.Zero, nameof(RequestTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrentFetches, 1, nameof(MaxConcurrentFetches));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxResponseHeadersBytes, 1024, nameof(MaxResponseHeadersBytes));
    }

    private static IPNetwork[] Networks(string list) => [.. list.Split(' ').Select(n => IPNetwork.Parse(n))];
}
