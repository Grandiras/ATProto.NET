using System.Globalization;
using System.Net;
using ATProtoNet.Net;

namespace ATProtoNet.Identity;

/// <summary>Options of <see cref="DidWebResolver"/>.</summary>
public sealed record DidWebOptions
{
    /// <summary>The largest document accepted, in bytes; at least 1 KiB.</summary>
    public int MaxDidDocumentBytes { get; init; } = 64 * 1024;

    internal void Validate() => ArgumentOutOfRangeException.ThrowIfLessThan(MaxDidDocumentBytes, 1024, nameof(MaxDidDocumentBytes));
}

/// <summary>Resolves <c>did:web</c> from <c>https://{host}/.well-known/did.json</c>.</summary>
/// <remarks>
/// AT Protocol supports hostname-level <c>did:web</c> only: a path, an IP address, a single-label name, a reserved TLD or a port
/// is refused, except <c>localhost</c> with a port, fetched over <c>http</c> under <see cref="UntrustedFetchPolicy.AllowPrivateNetworks"/>.
/// See <see href="https://atproto.com/specs/did"/>.
/// </remarks>
public sealed class DidWebResolver : IDidResolver
{
    private readonly UntrustedHttp _http;
    private readonly int _maxBytes;

    /// <summary>Creates a resolver.</summary>
    /// <param name="http">The fetch layer, which the caller owns.</param>
    /// <param name="options">Options. Defaults apply when omitted.</param>
    public DidWebResolver(UntrustedHttp http, DidWebOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        options ??= new();
        options.Validate();
        (_http, _maxBytes) = (http, options.MaxDidDocumentBytes);
    }

    /// <inheritdoc />
    public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default) =>
        DidDocumentFetch.GetAsync(_http, DocumentUrl(did, _http.Policy.AllowPrivateNetworks), did, _maxBytes, cancellationToken);

    private static Uri DocumentUrl(Did did, bool allowPrivateNetworks)
    {
        ArgumentNullException.ThrowIfNull(did);
        if (did.Method != "web") throw new IdentityResolutionException(IdentityResolutionError.UnsupportedMethod, $"'{did}' is not a did:web.", did);
        var id = did.MethodSpecificId;
        // Colons separate path segments; a port is percent-encoded as %3A.
        if (id.Contains(':')) throw Invalid(did, "is a path-based did:web, which AT Protocol does not support");
        var decoded = Uri.UnescapeDataString(id.ToString());
        var colon = decoded.IndexOf(':');
        var host = (colon < 0 ? decoded : decoded[..colon]).ToLowerInvariant();
        var port = -1;
        if (colon >= 0 && (host != "localhost" || !int.TryParse(decoded.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
            throw Invalid(did, "carries a port; AT Protocol allows one only on did:web:localhost");
        if (host == "localhost") return new UriBuilder(Uri.UriSchemeHttp, host, port, "/.well-known/did.json").Uri;
        if (!Handle.TryParse(host, out var name) || host.StartsWith('@') || !HandleResolver.IsResolvable(name, allowPrivateNetworks))
            throw Invalid(did, "does not name a public hostname");
        return new UriBuilder(Uri.UriSchemeHttps, host, -1, "/.well-known/did.json").Uri;
    }

    private static IdentityResolutionException Invalid(Did did, string why) => new(IdentityResolutionError.InvalidDid, $"'{did}' {why}.", did);
}

// The one path a DID document takes: fetched, mapped to resolution errors, parsed and held to the DID asked for.
internal static class DidDocumentFetch
{
    private const string Accept = "application/did+ld+json, application/json";

    public static async Task<DidDocument> GetAsync(UntrustedHttp http, Uri url, Did did, int maxBytes, CancellationToken cancellationToken) =>
        Validate(Parse((await FetchAsync(http, url, did, maxBytes, Accept, cancellationToken).ConfigureAwait(false)).Span, did), did);

    // A success body, with 404 and 410 told apart from other failures.
    public static async Task<ReadOnlyMemory<byte>> FetchAsync(UntrustedHttp http, Uri url, Did? did, int maxBytes, string accept, CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.GetAsync(url, new() { MaxBodyBytes = maxBytes, Accept = accept }, cancellationToken).ConfigureAwait(false);
            return response.Status switch
            {
                HttpStatusCode.NotFound => throw new IdentityResolutionException(IdentityResolutionError.NotFound, $"Nothing at {url} for {did}.", did),
                HttpStatusCode.Gone => throw new IdentityResolutionException(IdentityResolutionError.Deactivated, $"{did} is deactivated.", did),
                _ => response.EnsureSuccess().Body,
            };
        }
        catch (UntrustedFetchException ex)
        {
            throw new IdentityResolutionException(IdentityResolutionError.Unavailable, $"Fetching {url} for {did} failed: {ex.Message}", did, ex);
        }
    }

    public static DidDocument Parse(ReadOnlySpan<byte> json, Did did)
    {
        try
        {
            return DidDocument.Parse(json);
        }
        catch (FormatException ex)
        {
            throw new IdentityResolutionException(IdentityResolutionError.InvalidDocument, $"The DID document for {did} is malformed: {ex.Message}", did, ex);
        }
    }

    // Ordinal: a did:web host matched case-insensitively would give one identity as many spellings as its host has casings.
    public static DidDocument Validate(DidDocument document, Did did) => document.Id.Value == did.Value ? document
        : throw new IdentityResolutionException(IdentityResolutionError.InvalidDocument, $"The DID document for {did} names '{document.Id}' as its id.", did);
}
