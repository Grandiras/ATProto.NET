using Microsoft.AspNetCore.Builder;

namespace ATProtoNet.Pds;

/// <summary>
/// Controls which XRPC endpoints <c>MapAtProtoPds()</c> maps, and lets route conventions
/// (auth policies, metadata, filters) be applied to the ones it does map.
/// </summary>
/// <remarks>
/// Excluding an endpoint lets the host map its own implementation on the same route
/// without producing an ambiguous-match conflict:
/// <code>
/// app.MapAtProtoPds(o => o.Exclude(PdsEndpointNames.CreateAccount));
/// app.MapPost("/xrpc/com.atproto.server.createAccount", MyCreateAccount);
/// </code>
/// </remarks>
public sealed class PdsEndpointOptions
{
    private readonly HashSet<string> _excluded = new(StringComparer.Ordinal);
    private HashSet<string>? _only;
    private readonly Dictionary<string, List<Action<IEndpointConventionBuilder>>> _conventions = new(StringComparer.Ordinal);
    private readonly List<Action<string, IEndpointConventionBuilder>> _sharedConventions = [];

    /// <summary>
    /// Excludes the given endpoints from being mapped, so the host can map its own
    /// implementation of them.
    /// </summary>
    /// <param name="nsids">Endpoint NSIDs — use <see cref="PdsEndpointNames"/> constants.</param>
    /// <exception cref="ArgumentException">An NSID is not one of <see cref="PdsEndpointNames.All"/>.</exception>
    public PdsEndpointOptions Exclude(params string[] nsids)
    {
        ArgumentNullException.ThrowIfNull(nsids);
        foreach (var nsid in nsids)
        {
            ValidateNsid(nsid);
            _excluded.Add(nsid);
        }

        return this;
    }

    /// <summary>
    /// Maps only the given endpoints; every other PDS endpoint is skipped. Calling this
    /// more than once unions the sets. <see cref="Exclude"/> still wins over
    /// anything listed here.
    /// </summary>
    /// <param name="nsids">Endpoint NSIDs — use <see cref="PdsEndpointNames"/> constants.</param>
    /// <exception cref="ArgumentException">An NSID is not one of <see cref="PdsEndpointNames.All"/>.</exception>
    public PdsEndpointOptions Only(params string[] nsids)
    {
        ArgumentNullException.ThrowIfNull(nsids);
        _only ??= new HashSet<string>(StringComparer.Ordinal);
        foreach (var nsid in nsids)
        {
            ValidateNsid(nsid);
            _only.Add(nsid);
        }

        return this;
    }

    /// <summary>
    /// Applies a route convention to a single endpoint — for example an authorization
    /// policy, an endpoint filter, or extra metadata.
    /// </summary>
    /// <param name="nsid">Endpoint NSID — use <see cref="PdsEndpointNames"/> constants.</param>
    /// <param name="configure">Callback invoked with the mapped route's convention builder.</param>
    /// <exception cref="ArgumentException"><paramref name="nsid"/> is not one of <see cref="PdsEndpointNames.All"/>.</exception>
    public PdsEndpointOptions Configure(string nsid, Action<IEndpointConventionBuilder> configure)
    {
        ValidateNsid(nsid);
        ArgumentNullException.ThrowIfNull(configure);

        if (!_conventions.TryGetValue(nsid, out var list))
            _conventions[nsid] = list = [];

        list.Add(configure);
        return this;
    }

    /// <summary>
    /// Applies a route convention to every mapped endpoint. The callback receives the
    /// endpoint's NSID so it can vary its behaviour per route.
    /// </summary>
    /// <param name="configure">Callback invoked with each mapped route's NSID and convention builder.</param>
    public PdsEndpointOptions ConfigureAll(Action<string, IEndpointConventionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _sharedConventions.Add(configure);
        return this;
    }

    /// <summary>
    /// Whether the given endpoint will be mapped under the current configuration.
    /// </summary>
    /// <param name="nsid">Endpoint NSID.</param>
    public bool IsMapped(string nsid)
    {
        ArgumentNullException.ThrowIfNull(nsid);
        if (_excluded.Contains(nsid)) return false;
        return _only is null || _only.Contains(nsid);
    }

    /// <summary>
    /// Runs the per-endpoint and shared conventions against a freshly mapped route.
    /// </summary>
    internal void ApplyConventions(string nsid, IEndpointConventionBuilder builder)
    {
        foreach (var convention in _sharedConventions)
            convention(nsid, builder);

        if (_conventions.TryGetValue(nsid, out var list))
        {
            foreach (var convention in list)
                convention(builder);
        }
    }

    private static void ValidateNsid(string nsid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nsid);
        if (!PdsEndpointNames.All.Contains(nsid))
        {
            throw new ArgumentException(
                $"'{nsid}' is not a PDS endpoint mapped by MapAtProtoPds(). Known endpoints: {string.Join(", ", PdsEndpointNames.All)}.",
                nameof(nsid));
        }
    }
}
