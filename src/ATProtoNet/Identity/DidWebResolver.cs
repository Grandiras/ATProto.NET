using System.Net;
using System.Text.RegularExpressions;

namespace ATProtoNet.Identity;

/// <summary>
/// Resolves <c>did:web</c> identifiers by fetching <c>https://&lt;host&gt;/.well-known/did.json</c>.
/// </summary>
/// <remarks>
/// <para>AT Protocol supports hostname-level <c>did:web</c> only: a path-based DID
/// (<c>did:web:example.com:user:alice</c>) is refused, and so is a port on any host but
/// <c>localhost</c>, which itself resolves only under the development opt-out
/// (<see cref="IdentityResolverOptions.AllowPrivateNetworks"/>).</para>
/// <para>Fetches follow the SDK's identity fetch policy: see
/// <see cref="IdentityResolverOptions"/>.</para>
/// </remarks>
/// <seealso href="https://atproto.com/specs/did">AT Protocol DID specification</seealso>
public sealed partial class DidWebResolver : IDidResolver, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IdentityResolverOptions _options;

    // One DNS hostname: dot-separated labels of letters, digits and inner hyphens.
    [GeneratedRegex(@"^(?=.{1,253}\z)[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*\z", RegexOptions.IgnoreCase)]
    private static partial Regex HostnamePattern();

    /// <summary>
    /// Creates a resolver with its own client under the SDK's identity fetch policy.
    /// </summary>
    /// <param name="options">Resolver options. Defaults apply when omitted.</param>
    public DidWebResolver(IdentityResolverOptions? options = null)
    {
        _options = options ?? new IdentityResolverOptions();
        _options.Validate();
        _httpClient = IdentityNetworkPolicy.CreateClient(_options.AllowPrivateNetworks);
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Creates a resolver that sends its requests through <paramref name="httpClient"/>.
    /// </summary>
    /// <param name="httpClient">
    /// The client to use, which the caller owns. Its handler is used as is: the connection-level
    /// address check applies only to the SDK's own handler, while the identifier rules (hostname
    /// only, no port but on <c>localhost</c>, HTTPS) still apply.
    /// </param>
    /// <param name="options">Resolver options. Defaults apply when omitted.</param>
    public DidWebResolver(HttpClient httpClient, IdentityResolverOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _options = options ?? new IdentityResolverOptions();
        _options.Validate();
        _httpClient = httpClient;
        _ownsHttpClient = false;
    }

    /// <summary>
    /// Resolves a <c>did:web</c> identifier to its DID document.
    /// </summary>
    /// <param name="did">The DID (e.g. <c>did:web:example.com</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document.</returns>
    /// <exception cref="DidResolutionException">Thrown when resolution fails.</exception>
    public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        var url = BuildResolutionUrl(did, _options.AllowPrivateNetworks);
        return IdentityFetch.GetDidDocumentAsync(
            _httpClient, url, did, _options.MaxDidDocumentBytes, _options.RequestTimeout, cancellationToken);
    }

    /// <summary>
    /// Builds the URL a <c>did:web</c> document is fetched from, enforcing AT Protocol's
    /// <c>did:web</c> rules and the policy's host rules.
    /// </summary>
    /// <exception cref="DidResolutionException">
    /// <see cref="DidResolutionErrorKind.UnsupportedMethod"/> for another method,
    /// <see cref="DidResolutionErrorKind.InvalidDid"/> for a path, a port on a host other than
    /// <c>localhost</c>, an IP address or a malformed host, and
    /// <see cref="DidResolutionErrorKind.Blocked"/> for <c>localhost</c> without the development
    /// opt-out.
    /// </exception>
    internal static Uri BuildResolutionUrl(Did did, bool allowPrivateNetworks)
    {
        if (did.Method != "web")
            throw new DidResolutionException($"'{did}' is not a did:web.", DidResolutionErrorKind.UnsupportedMethod, did);

        var id = did.MethodSpecificId;

        // Colons separate path segments; a port is carried percent-encoded as %3A.
        if (id.Contains(':'))
        {
            throw new DidResolutionException(
                $"'{did}' is a path-based did:web, which AT Protocol does not support.",
                DidResolutionErrorKind.InvalidDid, did);
        }

        var decoded = Uri.UnescapeDataString(id);
        var colon = decoded.IndexOf(':');
        var host = (colon < 0 ? decoded : decoded[..colon]).ToLowerInvariant();
        int? port = null;

        if (colon >= 0)
        {
            if (!int.TryParse(decoded.AsSpan(colon + 1), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) ||
                parsed is < 1 or > 65535)
            {
                throw new DidResolutionException($"'{did}' carries an invalid port.", DidResolutionErrorKind.InvalidDid, did);
            }

            port = parsed;
        }

        var isLocalhost = host == "localhost";

        // A fully qualified name: an address, or a bare label that the resolver's DNS search
        // domains would complete into some internal name, is not one.
        if (!HostnamePattern().IsMatch(host) || IPAddress.TryParse(host, out _) || (!isLocalhost && !host.Contains('.')))
            throw new DidResolutionException($"'{did}' does not name a hostname.", DidResolutionErrorKind.InvalidDid, did);

        // The DID spec allows a port for localhost only, as a testing affordance.
        if (port is not null && !isLocalhost)
        {
            throw new DidResolutionException(
                $"'{did}' carries a port; AT Protocol allows one only on did:web:localhost.",
                DidResolutionErrorKind.InvalidDid, did);
        }

        if (isLocalhost)
        {
            if (!allowPrivateNetworks)
            {
                throw new DidResolutionException(
                    $"'{did}' resolves only under the development opt-out " +
                    $"({nameof(IdentityResolverOptions)}.{nameof(IdentityResolverOptions.AllowPrivateNetworks)}).",
                    DidResolutionErrorKind.Blocked, did);
            }

            return new UriBuilder(Uri.UriSchemeHttp, host, port ?? -1, "/.well-known/did.json").Uri;
        }

        return new UriBuilder(Uri.UriSchemeHttps, host, -1, "/.well-known/did.json").Uri;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
