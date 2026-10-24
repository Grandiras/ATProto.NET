using ATProtoNet.Identity.Plc;
using ATProtoNet.Net;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Identity;

/// <summary>What <see cref="IdentityResolverFactory.CreateDefault"/> builds the resolver from; every member is optional.</summary>
public sealed record IdentityDefaults
{
    /// <summary>The PLC directory. Defaults to <see cref="PlcClient.DefaultDirectory"/>.</summary>
    public Uri? PlcDirectory { get; init; }

    /// <summary>The TXT resolver of handles. Defaults to <see cref="DnsOverHttpsTxtResolver"/> on its default endpoint.</summary>
    public IDnsTxtResolver? Dns { get; init; }

    /// <summary>Options of the <c>did:web</c> resolver.</summary>
    public DidWebOptions? DidWeb { get; init; }

    /// <summary>Options of the handle resolver.</summary>
    public HandleResolverOptions? Handles { get; init; }

    /// <summary>Options of the document cache.</summary>
    public DidCacheOptions? Cache { get; init; }

    /// <summary>A cache of documents shared between instances.</summary>
    public IDistributedCache? SharedCache { get; init; }

    /// <summary>The clock. Defaults to the system clock.</summary>
    public TimeProvider? Time { get; init; }

    /// <summary>The logger.</summary>
    public ILogger? Logger { get; init; }
}

/// <summary>Builds the SDK's default identity resolver.</summary>
public static class IdentityResolverFactory
{
    /// <summary>A resolver over a <see cref="CachingDidResolver"/> (<c>did:plc</c> and <c>did:web</c>) and a <see cref="HandleResolver"/> (DNS-over-HTTPS and HTTPS).</summary>
    /// <param name="http">The fetch layer, which the caller owns; nothing else needs disposing.</param>
    /// <param name="defaults">What to build it from. Defaults apply when omitted.</param>
    /// <returns>The resolver.</returns>
    public static IIdentityResolver CreateDefault(UntrustedHttp http, IdentityDefaults? defaults = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        var d = defaults ?? new();
        var methods = new Dictionary<string, IDidResolver> { ["plc"] = new PlcClient(http, d.PlcDirectory), ["web"] = new DidWebResolver(http, d.DidWeb) };
        var dids = new CachingDidResolver(new DidResolver(methods), d.Cache, d.SharedCache, d.Time, d.Logger);
        var handles = new HandleResolver(http, d.Dns ?? new DnsOverHttpsTxtResolver(http), d.Handles, d.Time, d.Logger);
        return new IdentityResolver(dids, handles, d.Logger);
    }
}
