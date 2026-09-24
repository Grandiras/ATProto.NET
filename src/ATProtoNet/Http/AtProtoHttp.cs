using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace ATProtoNet.Http;

/// <summary>
/// HTTP plumbing shared by every client the SDK creates for itself.
/// </summary>
internal static class AtProtoHttp
{
    /// <summary>
    /// How long a pooled connection is reused. Bounding it is what makes a long-lived process
    /// notice a DNS change behind a host — a PDS migrating, a load balancer rotating.
    /// </summary>
    internal static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>How long establishing a connection may take before the attempt fails.</summary>
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The <c>User-Agent</c> sent when the caller configures none.</summary>
    internal static readonly string DefaultUserAgent =
        $"ATProtoNet/{typeof(AtProtoHttp).Assembly.GetName().Version}";

    /// <summary>
    /// The one handler behind every <see cref="HttpClient"/> the SDK owns, so they share a
    /// connection pool and are never disposed with it (see <see cref="CreateClient"/>).
    /// </summary>
    internal static HttpMessageHandler SharedHandler { get; } = CreateHandler();

    /// <summary>
    /// A primary handler with the SDK's connection settings: response decompression (without it
    /// no <c>Accept-Encoding</c> is sent, and a service answers JSON uncompressed), a bounded
    /// connection lifetime and a connect timeout.
    /// </summary>
    /// <remarks>
    /// Where sockets are unavailable (Blazor WebAssembly) this falls back to the platform's
    /// handler, which delegates all three to the browser.
    /// </remarks>
    internal static HttpMessageHandler CreateHandler() =>
        SocketsHttpHandler.IsSupported
            ? new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = PooledConnectionLifetime,
                ConnectTimeout = ConnectTimeout,
            }
            : new HttpClientHandler();

    /// <summary>
    /// Creates an <see cref="HttpClient"/> over <see cref="SharedHandler"/>. Disposing it leaves
    /// the handler, and so every other SDK-owned client, intact.
    /// </summary>
    /// <param name="timeout">The client's <see cref="HttpClient.Timeout"/>, when not the default.</param>
    internal static HttpClient CreateClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient(SharedHandler, disposeHandler: false);
        if (timeout is { } value)
            client.Timeout = value;
        return client;
    }

    /// <summary>
    /// Parses a service base URL into an absolute URI whose path ends in <c>/</c>, so a relative
    /// path resolves beneath it rather than replacing its last segment.
    /// </summary>
    /// <remarks>
    /// A base URL is an <c>http</c> or <c>https</c> scheme, authority and path only. One with a
    /// query or fragment is refused rather than trimmed: the query would otherwise end up in
    /// front of every request path, and silently dropping it would hide a misconfiguration.
    /// </remarks>
    /// <param name="url">The URL, with or without a trailing slash.</param>
    /// <exception cref="UriFormatException">The URL is not absolute.</exception>
    /// <exception cref="ArgumentException">The URL is not http(s), or has a query or fragment.</exception>
    internal static Uri NormalizeBaseUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return NormalizeBaseUrl(new Uri(url, UriKind.Absolute));
    }

    /// <summary>
    /// Normalizes a service base URL to an absolute URI whose path ends in <c>/</c>.
    /// </summary>
    /// <remarks>
    /// The result is built from scheme, authority and path; a URL that is not http(s), or has a
    /// query or fragment, is refused, as by <see cref="NormalizeBaseUrl(string)"/>.
    /// </remarks>
    /// <param name="url">The URL, with or without a trailing slash.</param>
    /// <exception cref="ArgumentException">
    /// The URL is relative or not http(s), or has a query or fragment.
    /// </exception>
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

    /// <summary>
    /// <see cref="NormalizeBaseUrl(string)"/> for an endpoint read from a DID document, where an
    /// unusable value is the publisher's fault and the caller reports it in its own terms.
    /// </summary>
    /// <param name="url">The endpoint, or <see langword="null"/>.</param>
    /// <param name="normalized">The normalized base URL, on success.</param>
    internal static bool TryNormalizeBaseUrl(string? url, [NotNullWhen(true)] out Uri? normalized)
    {
        normalized = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || !IsHttp(parsed) || HasQueryOrFragment(parsed))
            return false;

        normalized = NormalizeBaseUrl(parsed);
        return true;
    }

    private static bool IsHttp(Uri url) =>
        url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp);

    private static bool HasQueryOrFragment(Uri url) => url.Query.Length > 0 || url.Fragment.Length > 0;

    /// <summary>
    /// Validates the URL of a service the SDK is to send credentials to: absolute
    /// <c>http</c>/<c>https</c> with no query or fragment, and HTTPS unless it is a loopback
    /// address (for development) or <paramref name="allowInsecure"/> is set.
    /// </summary>
    /// <param name="url">The service URL.</param>
    /// <param name="paramName">The argument name to report.</param>
    /// <param name="allowInsecure">Accept plain HTTP to any host.</param>
    /// <returns>The URL, normalized with <see cref="NormalizeBaseUrl(Uri)"/>.</returns>
    /// <exception cref="ArgumentException">The URL is not an acceptable service URL.</exception>
    internal static Uri ValidateServiceUrl(Uri url, string paramName, bool allowInsecure = false)
    {
        ArgumentNullException.ThrowIfNull(url, paramName);

        if (!IsHttp(url))
            throw new ArgumentException($"'{url}' is not an absolute http(s) URL.", paramName);

        if (HasQueryOrFragment(url))
            throw new ArgumentException($"Service URL '{url}' must not have a query or fragment.", paramName);

        if (url.Scheme != Uri.UriSchemeHttps && !url.IsLoopback && !allowInsecure)
        {
            throw new ArgumentException(
                $"Service URL '{url}' must use HTTPS. HTTP is only allowed for localhost during development.",
                paramName);
        }

        return NormalizeBaseUrl(url);
    }
}
