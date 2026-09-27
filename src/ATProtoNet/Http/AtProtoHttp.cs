using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace ATProtoNet.Http;

// HTTP plumbing shared by every client the SDK creates for itself.
internal static class AtProtoHttp
{
    // How long a pooled connection is reused. Bounding it is what makes a long-lived process notice a
    // DNS change behind a host — a PDS migrating, a load balancer rotating.
    internal static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    // How long establishing a connection may take before the attempt fails.
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    // The User-Agent sent when the caller configures none.
    internal static readonly string DefaultUserAgent =
        $"ATProtoNet/{typeof(AtProtoHttp).Assembly.GetName().Version}";

    // The one handler behind every HttpClient the SDK owns, so they share a connection pool and are
    // never disposed with it (see CreateClient).
    internal static HttpMessageHandler SharedHandler { get; } = CreateHandler();

    // A primary handler with the SDK's connection settings: response decompression (without it no
    // Accept-Encoding is sent, and a service answers JSON uncompressed), a bounded connection lifetime
    // and a connect timeout.
    //
    // Where sockets are unavailable (Blazor WebAssembly) this falls back to the platform's handler,
    // which delegates all three to the browser.
    internal static HttpMessageHandler CreateHandler() =>
        SocketsHttpHandler.IsSupported
            ? new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = PooledConnectionLifetime,
                ConnectTimeout = ConnectTimeout,
            }
            : new HttpClientHandler();

    // Creates an HttpClient over SharedHandler. Disposing it leaves the handler, and so every other
    // SDK-owned client, intact.
    //
    // timeout: The client's HttpClient.Timeout, when not the default.
    internal static HttpClient CreateClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient(SharedHandler, disposeHandler: false);
        if (timeout is { } value)
            client.Timeout = value;
        return client;
    }

    // Parses a service base URL into an absolute URI whose path ends in /, so a relative path resolves
    // beneath it rather than replacing its last segment.
    //
    // A base URL is an http or https scheme, authority and path only. One with a query or fragment is
    // refused rather than trimmed: the query would otherwise end up in front of every request path, and
    // silently dropping it would hide a misconfiguration.
    //
    // url: The URL, with or without a trailing slash.
    //
    // Throws UriFormatException: The URL is not absolute.
    //
    // Throws ArgumentException: The URL is not http(s), or has a query or fragment.
    internal static Uri NormalizeBaseUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return NormalizeBaseUrl(new Uri(url, UriKind.Absolute));
    }

    // Normalizes a service base URL to an absolute URI whose path ends in /.
    //
    // The result is built from scheme, authority and path; a URL that is not http(s), or has a query or
    // fragment, is refused, as by NormalizeBaseUrl.
    //
    // url: The URL, with or without a trailing slash.
    //
    // Throws ArgumentException: The URL is relative or not http(s), or has a query or fragment.
    internal static Uri NormalizeBaseUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        // On Unix a rooted path such as "/xrpc" parses as an absolute file: URI, hence the scheme check.
        if (!IsHttp(url))
            throw new ArgumentException($"'{url}' is not an absolute http(s) URL.", nameof(url));

        if (HasQueryOrFragment(url))
            throw new ArgumentException($"Service URL '{url}' must not have a query or fragment.", nameof(url));

        return new Uri(url.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/", UriKind.Absolute);
    }

    // NormalizeBaseUrl for an endpoint read from a DID document, where an unusable value is the
    // publisher's fault and the caller reports it in its own terms.
    //
    // url: The endpoint, or null.
    //
    // normalized: The normalized base URL, on success.
    internal static bool TryNormalizeBaseUrl(string? url, [NotNullWhen(true)] out Uri? normalized)
    {
        normalized = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || !IsHttp(parsed) || HasQueryOrFragment(parsed))
            return false;

        normalized = NormalizeBaseUrl(parsed);
        return true;
    }

    // The same URL with its scheme switched between http(s) and ws(s), keeping it secure or not: a
    // service serves its WebSocket and HTTP endpoints on one host. Any other scheme is kept.
    internal static Uri WithScheme(Uri url, bool webSocket)
    {
        bool secure;
        switch (url.Scheme)
        {
            case "https" or "wss":
                secure = true;
                break;
            case "http" or "ws":
                secure = false;
                break;
            default:
                return url;
        }

        var scheme = webSocket ? (secure ? "wss" : "ws") : (secure ? Uri.UriSchemeHttps : Uri.UriSchemeHttp);
        return url.Scheme == scheme ? url : new UriBuilder(url) { Scheme = scheme, Port = url.IsDefaultPort ? -1 : url.Port }.Uri;
    }

    private static bool IsHttp(Uri url) =>
        url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp);

    private static bool HasQueryOrFragment(Uri url) => url.Query.Length > 0 || url.Fragment.Length > 0;

    // Validates the URL of a service the SDK is to send credentials to, or of an identity service it
    // fetches from: absolute http/https with no query or fragment, and HTTPS unless it is a loopback
    // address (for development) or allowInsecure is set.
    //
    // url: The service URL.
    //
    // paramName: The argument name to report.
    //
    // allowInsecure: Accept plain HTTP to any host.
    //
    // allowLoopback: Accept plain HTTP to a loopback address. An identity service (a PLC directory, a
    // DNS-over-HTTPS endpoint) passes false: HTTP there takes the development opt-out,
    // IdentityResolverOptions.AllowPrivateNetworks.
    //
    // Returns: The URL, normalized with NormalizeBaseUrl.
    //
    // Throws ArgumentException: The URL is not an acceptable service URL.
    internal static Uri ValidateServiceUrl(Uri url, string paramName, bool allowInsecure = false, bool allowLoopback = true)
    {
        ArgumentNullException.ThrowIfNull(url, paramName);

        if (!IsHttp(url))
            throw new ArgumentException($"'{url}' is not an absolute http(s) URL.", paramName);

        if (HasQueryOrFragment(url))
            throw new ArgumentException($"Service URL '{url}' must not have a query or fragment.", paramName);

        if (url.Scheme != Uri.UriSchemeHttps && !(allowLoopback && url.IsLoopback) && !allowInsecure)
        {
            throw new ArgumentException(
                allowLoopback
                    ? $"Service URL '{url}' must use HTTPS. HTTP is only allowed for localhost during development."
                    : $"Service URL '{url}' must use HTTPS. Plain HTTP needs the development opt-out " +
                      $"({nameof(Identity.IdentityResolverOptions)}.{nameof(Identity.IdentityResolverOptions.AllowPrivateNetworks)}).",
                paramName);
        }

        return NormalizeBaseUrl(url);
    }
}
