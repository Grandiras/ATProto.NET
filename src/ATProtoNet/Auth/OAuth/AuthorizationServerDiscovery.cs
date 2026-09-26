using System.Collections.Concurrent;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// Discovers Authorization Server and Protected Resource metadata for AT Protocol OAuth.
/// Handles the full resolution chain: handle → DID → DID document → PDS → Authorization Server.
/// </summary>
/// <remarks>
/// <para>Identity resolution (handle → DID → DID document → PDS) goes through an
/// <see cref="IIdentityResolver"/>, under the SDK's identity fetch policy unless one configured
/// otherwise is supplied. This class adds the OAuth metadata steps and reports failures as
/// <see cref="OAuthException"/>.</para>
/// <para>The PDS and authorization server URLs come from a DID document, which anyone can write,
/// so the metadata requests follow the same policy by default: HTTPS only, no query or fragment,
/// public addresses only (checked after DNS), no redirects, a 64 KiB body cap and a 10-second
/// timeout.</para>
/// <para>Metadata documents are cached for <see cref="MetadataCacheLifetime"/> by URL, so the
/// callback of a login and the refreshes of many sessions on one server do not fetch them again.
/// Authorization server metadata is held to the AT Protocol profile: its <c>issuer</c> must be
/// exactly the issuer it was looked up as, its endpoints absolute HTTPS URLs without a query or
/// fragment, and it must require pushed authorization requests and support the <c>iss</c>
/// response parameter and client ID metadata documents. A PDS's protected-resource metadata must
/// name the PDS itself as its <c>resource</c>.</para>
/// </remarks>
public sealed class AuthorizationServerDiscovery : IDisposable
{
    /// <summary>
    /// Default budget applied to each handle resolution round (5 seconds).
    /// </summary>
    public static readonly TimeSpan DefaultHandleResolutionTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long a fetched metadata document is reused (5 minutes).</summary>
    public static readonly TimeSpan MetadataCacheLifetime = TimeSpan.FromMinutes(5);

    // Protected-resource and authorization-server metadata are a few kilobytes.
    private const int MaxMetadataBytes = 64 * 1024;
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The most metadata documents cached.</summary>
    private const int MetadataCacheCapacity = 256;

    private readonly HttpClient _metadataClient;
    private readonly bool _ownsMetadataClient;
    private readonly bool _allowPrivateNetworks;
    private readonly IdentityResolver? _ownedIdentityResolver;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    // The raw documents, not the deserialized models: those are mutable and handed to callers.
    private readonly ConcurrentDictionary<string, CachedDocument> _metadataCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a new discovery instance.
    /// </summary>
    /// <param name="httpClient">
    /// The client the metadata requests go through, used as is and owned by the caller: the
    /// address check lives in the SDK's own handler, while the URL rules (HTTPS, no query or
    /// fragment) and the body cap still apply. Pass <see langword="null"/> to fetch under the
    /// SDK's identity fetch policy, as <see cref="OAuthClient"/> does by default.
    /// </param>
    /// <param name="logger">Logger.</param>
    /// <param name="identityResolver">
    /// Resolves handles and DIDs. When omitted, one is created with
    /// <see cref="Identity.IdentityResolver.CreateDefault"/> and owned by this instance.
    /// </param>
    /// <param name="allowPrivateNetworks">
    /// The development opt-out for the metadata requests, as
    /// <see cref="IdentityResolverOptions.AllowPrivateNetworks"/>: plain HTTP and private
    /// addresses, for a local PDS.
    /// </param>
    public AuthorizationServerDiscovery(
        HttpClient? httpClient, ILogger logger, IIdentityResolver? identityResolver = null, bool allowPrivateNetworks = false)
    {
        _logger = logger;
        _jsonOptions = AtProtoJsonDefaults.Options;
        _allowPrivateNetworks = allowPrivateNetworks;
        _ownsMetadataClient = httpClient is null;
        _metadataClient = httpClient ?? IdentityNetworkPolicy.CreateClient(allowPrivateNetworks);

        if (identityResolver is null)
        {
            identityResolver = _ownedIdentityResolver = Identity.IdentityResolver.CreateDefault(
                new IdentityResolverOptions { AllowPrivateNetworks = allowPrivateNetworks }, logger);
        }

        IdentityResolver = identityResolver;
    }

    /// <summary>The resolver handles and DIDs are resolved through.</summary>
    public IIdentityResolver IdentityResolver { get; }

    /// <summary>The clock the metadata cache expires by.</summary>
    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Resolves an account identifier (handle or DID) to the PDS URL and Authorization Server metadata.
    /// </summary>
    /// <param name="identifier">A handle (e.g., "alice.bsky.social") or DID (e.g., "did:plc:...").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved PDS URL, Authorization Server metadata, and DID.</returns>
    /// <exception cref="OAuthException">
    /// Thrown when the identifier is malformed (<c>invalid_handle</c>, <c>invalid_did</c>), does not
    /// resolve (<c>handle_resolution_failed</c>, <c>handle_resolution_conflict</c>,
    /// <c>unsupported_did_method</c>, <c>did_resolution_failed</c>), publishes no PDS
    /// (<c>pds_not_found</c>), or the PDS's metadata is unusable.
    /// </exception>
    public async Task<(string PdsUrl, AuthorizationServerMetadata Metadata, string Did)>
        ResolveFromIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resolving identity for OAuth: {Identifier}", identifier);

        var identity = await ResolveIdentityAsync(ParseIdentifier(identifier), cancellationToken);
        var pds = identity.PdsEndpoint
            ?? throw new OAuthException(
                $"DID document for '{identity.Did}' does not contain an atproto PDS service.", "pds_not_found");

        _logger.LogDebug("Resolved {Identifier} to {Did} at PDS {PdsUrl}", identifier, identity.Did, pds);

        var metadata = await ResolveAuthorizationServerAsync(pds.OriginalString, cancellationToken);
        return (pds.OriginalString, metadata, identity.Did.Value);
    }

    /// <summary>
    /// Resolves an identity, reporting failure as the <see cref="OAuthException"/> the OAuth flow
    /// raises.
    /// </summary>
    internal Task<ResolvedIdentity> ResolveIdentityAsync(AtIdentifier identifier, CancellationToken cancellationToken) =>
        MapFailureAsync(IdentityResolver.ResolveAsync(identifier, cancellationToken));

    /// <summary>
    /// Resolves a DID from a document fetched afresh (<see cref="IIdentityResolver.ResolveUncachedAsync"/>),
    /// reporting failure as <see cref="ResolveIdentityAsync"/> does.
    /// </summary>
    internal Task<ResolvedIdentity> ResolveIdentityUncachedAsync(Did did, CancellationToken cancellationToken) =>
        MapFailureAsync(IdentityResolver.ResolveUncachedAsync(did, cancellationToken));

    private static async Task<ResolvedIdentity> MapFailureAsync(Task<ResolvedIdentity> resolution)
    {
        try
        {
            return await resolution;
        }
        catch (DidResolutionException ex)
        {
            throw new OAuthException(ex.Message, ex.Kind switch
            {
                DidResolutionErrorKind.HandleNotFound => "handle_resolution_failed",
                DidResolutionErrorKind.HandleConflict => "handle_resolution_conflict",
                DidResolutionErrorKind.UnsupportedMethod => "unsupported_did_method",
                DidResolutionErrorKind.InvalidDid or DidResolutionErrorKind.Blocked => "invalid_did",
                _ => "did_resolution_failed",
            }, ex);
        }
    }

    /// <summary>
    /// Parses a sign-in identifier (a handle or DID, optionally <c>at://</c>-prefixed), reporting
    /// a malformed one as <c>invalid_handle</c> or <c>invalid_did</c>.
    /// </summary>
    internal static AtIdentifier ParseIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new OAuthException("Handle cannot be empty.", "invalid_handle");

        var trimmed = identifier.Trim();
        if (trimmed.StartsWith("at://", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[5..];

        if (AtIdentifier.TryParse(trimmed, out var parsed))
            return parsed;

        // A malformed identifier is never sent anywhere: a handle becomes a URL host and a DNS
        // name, so anything a hostname cannot hold (a path, a query, an @) stops here.
        throw trimmed.StartsWith("did:", StringComparison.OrdinalIgnoreCase)
            ? new OAuthException($"'{identifier}' is not a valid DID.", "invalid_did")
            : new OAuthException($"'{identifier}' is not a valid handle.", "invalid_handle");
    }

    /// <summary>
    /// Resolves a PDS URL or hostname to its Authorization Server metadata.
    /// Used when the user provides a PDS URL directly instead of a handle.
    /// </summary>
    /// <param name="pdsUrl">The PDS URL (e.g., "https://bsky.social").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Authorization Server metadata, validated.</returns>
    /// <exception cref="OAuthException">
    /// The PDS's protected-resource metadata names no usable authorization server or describes
    /// another resource (<c>invalid_resource_metadata</c>), or the authorization server's metadata
    /// fails validation (<c>issuer_mismatch</c>, <c>invalid_metadata</c>, <c>unsupported_scope</c>,
    /// <c>unsupported_dpop_alg</c>), besides the fetch failures of
    /// <see cref="FetchProtectedResourceMetadataAsync"/>.
    /// </exception>
    public Task<AuthorizationServerMetadata> ResolveAuthorizationServerAsync(
        string pdsUrl, CancellationToken cancellationToken = default) =>
        ResolveAuthorizationServerAsync(pdsUrl, bypassCache: false, cancellationToken);

    /// <summary>
    /// <see cref="ResolveAuthorizationServerAsync(string, CancellationToken)"/>, fetching both
    /// documents afresh when <paramref name="bypassCache"/> is set, as confirming an account's
    /// authorization server requires.
    /// </summary>
    internal async Task<AuthorizationServerMetadata> ResolveAuthorizationServerAsync(
        string pdsUrl, bool bypassCache, CancellationToken cancellationToken)
    {
        var resource = await ResolveResourceAsync(pdsUrl, bypassCache, cancellationToken);
        return await ResolveAuthorizationServerAsync(resource, bypassCache, cancellationToken);
    }

    /// <summary>
    /// The authorization server of a resource whose protected-resource metadata has been read:
    /// its metadata, which must list the resource when it lists any.
    /// </summary>
    private async Task<AuthorizationServerMetadata> ResolveAuthorizationServerAsync(
        (string Resource, string Issuer) resource, bool bypassCache, CancellationToken cancellationToken)
    {
        var metadata = await GetAuthorizationServerMetadataAsync(resource.Issuer, bypassCache, cancellationToken);

        // RFC 9728 section 4: an authorization server that lists the resources it protects must
        // list this one.
        if (metadata.ProtectedResources is { } protectedResources &&
            !protectedResources.Contains(resource.Resource, StringComparer.Ordinal))
        {
            throw new OAuthException(
                $"Authorization server '{resource.Issuer}' does not protect '{resource.Resource}'.",
                "invalid_resource_metadata");
        }

        return metadata;
    }

    /// <summary>
    /// Resolves the URL a sign-in starts from: a PDS, through its protected-resource metadata, or
    /// failing that an authorization server (an entryway) that serves no protected-resource
    /// metadata, which is then its own issuer.
    /// </summary>
    /// <exception cref="OAuthException">
    /// Neither worked; the error is the PDS resolution's, as the URL is a PDS in the usual case.
    /// </exception>
    internal async Task<AuthorizationServerMetadata> ResolveFromServerUrlAsync(
        string serverUrl, CancellationToken cancellationToken)
    {
        (string Resource, string Issuer) resource;
        try
        {
            resource = await ResolveResourceAsync(serverUrl, bypassCache: false, cancellationToken);
        }
        catch (OAuthException ex) when (ex.Error != "invalid_server_url")
        {
            _logger.LogDebug(ex, "{Url} is not a usable PDS; trying it as an authorization server", serverUrl);

            try
            {
                return await GetAuthorizationServerMetadataAsync(NormalizeUrl(serverUrl), bypassCache: false, cancellationToken);
            }
            catch (OAuthException)
            {
                // The URL was most likely meant as a PDS, so its failure explains more.
            }

            throw;
        }

        return await ResolveAuthorizationServerAsync(resource, bypassCache: false, cancellationToken);
    }

    /// <summary>
    /// Fetches and validates the metadata of the authorization server whose issuer is
    /// <paramref name="issuer"/>, afresh when <paramref name="bypassCache"/> is set.
    /// </summary>
    /// <exception cref="OAuthException">The metadata cannot be fetched or fails validation.</exception>
    internal async Task<AuthorizationServerMetadata> GetAuthorizationServerMetadataAsync(
        string issuer, bool bypassCache, CancellationToken cancellationToken)
    {
        var metadata = await FetchMetadataAsync<AuthorizationServerMetadata>(
            issuer, ".well-known/oauth-authorization-server", bypassCache, cancellationToken);
        ValidateAuthorizationServerMetadata(metadata, issuer);
        return metadata;
    }

    /// <summary>
    /// Reads a PDS's protected-resource metadata and checks it as the reference client does: it
    /// describes this PDS, and names exactly one authorization server, by a canonical issuer.
    /// </summary>
    /// <returns>The resource as the metadata names it, and its authorization server's issuer.</returns>
    private async Task<(string Resource, string Issuer)> ResolveResourceAsync(
        string pdsUrl, bool bypassCache, CancellationToken cancellationToken)
    {
        var resourceMetadata = await FetchMetadataAsync<ProtectedResourceMetadata>(
            pdsUrl, ".well-known/oauth-protected-resource", bypassCache, cancellationToken);

        // RFC 9728 section 3.3: the metadata must be about the resource it was fetched for, or a
        // server could vouch for someone else's PDS.
        if (!ResourceMatches(resourceMetadata.Resource, NormalizeUrl(pdsUrl)))
        {
            throw new OAuthException(
                $"The protected-resource metadata of '{pdsUrl}' describes '{resourceMetadata.Resource}'.",
                "invalid_resource_metadata");
        }

        if (resourceMetadata.AuthorizationServers is not { Count: > 0 } servers)
        {
            throw new OAuthException(
                "PDS protected resource metadata does not contain any authorization servers.",
                "invalid_resource_metadata");
        }

        // With several there is no telling which one the account's tokens should come from.
        if (servers.Count > 1)
        {
            throw new OAuthException(
                $"The protected-resource metadata of '{pdsUrl}' names {servers.Count} authorization servers; exactly one is expected.",
                "invalid_resource_metadata");
        }

        var issuer = servers[0];
        if (!IsCanonicalIssuer(issuer, _allowPrivateNetworks))
        {
            throw new OAuthException(
                $"The protected-resource metadata of '{pdsUrl}' names '{issuer}', which is not an issuer identifier.",
                "invalid_resource_metadata");
        }

        _logger.LogDebug("PDS {PdsUrl} points to Authorization Server {AuthServer}", pdsUrl, issuer);
        return (resourceMetadata.Resource!, issuer);
    }

    /// <summary>
    /// Fetches the Protected Resource metadata from a PDS, without validating it.
    /// </summary>
    /// <exception cref="OAuthException">
    /// Thrown when the URL is refused (<c>invalid_server_url</c>), the request fails
    /// (<c>metadata_fetch_failed</c>) or the answer is not metadata (<c>invalid_metadata</c>).
    /// </exception>
    public Task<ProtectedResourceMetadata> FetchProtectedResourceMetadataAsync(
        string pdsUrl, CancellationToken cancellationToken = default) =>
        FetchMetadataAsync<ProtectedResourceMetadata>(pdsUrl, ".well-known/oauth-protected-resource", bypassCache: false, cancellationToken);

    /// <summary>
    /// Fetches the Authorization Server metadata, without validating it.
    /// </summary>
    /// <exception cref="OAuthException">
    /// Thrown when the URL is refused (<c>invalid_server_url</c>), the request fails
    /// (<c>metadata_fetch_failed</c>) or the answer is not metadata (<c>invalid_metadata</c>).
    /// </exception>
    public Task<AuthorizationServerMetadata> FetchAuthorizationServerMetadataAsync(
        string authServerUrl, CancellationToken cancellationToken = default) =>
        FetchMetadataAsync<AuthorizationServerMetadata>(authServerUrl, ".well-known/oauth-authorization-server", bypassCache: false, cancellationToken);

    private async Task<T> FetchMetadataAsync<T>(string serverUrl, string wellKnown, bool bypassCache, CancellationToken cancellationToken)
        where T : class
    {
        Uri baseUrl;
        try
        {
            baseUrl = IdentityNetworkPolicy.ValidateServiceUrl(
                new Uri(NormalizeUrl(serverUrl), UriKind.Absolute), _allowPrivateNetworks, nameof(serverUrl));
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException)
        {
            throw new OAuthException($"'{serverUrl}' is not a usable server URL: {ex.Message}", "invalid_server_url", ex);
        }

        var url = new Uri(baseUrl, wellKnown);
        var body = await GetMetadataDocumentAsync(url, bypassCache, cancellationToken);

        try
        {
            return JsonSerializer.Deserialize<T>(body.Span, _jsonOptions)
                ?? throw new OAuthException($"{url} returned no metadata.", "metadata_fetch_failed");
        }
        catch (JsonException ex)
        {
            throw new OAuthException($"{url} returned malformed metadata: {ex.Message}", "invalid_metadata", ex);
        }
    }

    /// <summary>
    /// Returns a metadata document from the cache, or fetches it (always, with
    /// <paramref name="bypassCache"/>) and caches what it fetched.
    /// </summary>
    private async Task<ReadOnlyMemory<byte>> GetMetadataDocumentAsync(Uri url, bool bypassCache, CancellationToken cancellationToken)
    {
        var key = url.AbsoluteUri;
        if (!bypassCache && _metadataCache.TryGetValue(key, out var cached) && cached.ExpiresAt > TimeProvider.GetUtcNow())
            return cached.Body;

        _logger.LogDebug("Fetching OAuth metadata from {Url}", url);

        IdentityFetch.Result result;
        try
        {
            result = await IdentityFetch.GetAsync(
                _metadataClient, url, "application/json", MaxMetadataBytes, MetadataTimeout, did: null, cancellationToken);
        }
        catch (DidResolutionException ex)
        {
            throw new OAuthException($"Could not fetch {url}: {ex.Message}", "metadata_fetch_failed", ex);
        }

        if (result.FinalUri is { } final && final != url)
            throw new OAuthException($"{url} redirected to {final.Host}.", "metadata_fetch_failed");

        if (!result.IsSuccess)
            throw new OAuthException($"{url} answered HTTP {(int)result.Status}.", "metadata_fetch_failed");

        CacheMetadataDocument(key, result.Body);
        return result.Body;
    }

    private void CacheMetadataDocument(string key, ReadOnlyMemory<byte> body)
    {
        var now = TimeProvider.GetUtcNow();
        _metadataCache[key] = new CachedDocument(body, now + MetadataCacheLifetime);
        if (_metadataCache.Count <= MetadataCacheCapacity)
            return;

        foreach (var (candidate, document) in _metadataCache)
        {
            if (document.ExpiresAt <= now)
                _metadataCache.TryRemove(candidate, out _);
        }

        // Still over: the URLs come from logins anyone can start, so no entry is worth more than
        // another.
        foreach (var candidate in _metadataCache.Keys)
        {
            if (_metadataCache.Count <= MetadataCacheCapacity)
                break;
            _metadataCache.TryRemove(candidate, out _);
        }
    }

    private void ValidateAuthorizationServerMetadata(AuthorizationServerMetadata metadata, string expectedIssuer)
    {
        if (string.IsNullOrEmpty(metadata.Issuer))
            throw new OAuthException("Authorization server metadata missing 'issuer' field.", "invalid_metadata");

        // RFC 8414 section 3.3 and the AT Protocol profile: the issuer is exactly the one the
        // metadata was looked up as, in canonical form, port and path included. A server that
        // could claim another's issuer would enable mix-up attacks.
        if (!string.Equals(metadata.Issuer, expectedIssuer, StringComparison.Ordinal) ||
            !IsCanonicalIssuer(metadata.Issuer, _allowPrivateNetworks))
        {
            throw new OAuthException(
                $"Authorization server issuer '{metadata.Issuer}' does not match expected '{expectedIssuer}'.",
                "issuer_mismatch");
        }

        RequireEndpoint(metadata.AuthorizationEndpoint, "authorization_endpoint");
        RequireEndpoint(metadata.TokenEndpoint, "token_endpoint");
        RequireEndpoint(metadata.PushedAuthorizationRequestEndpoint, "pushed_authorization_request_endpoint");
        if (metadata.RevocationEndpoint is not null)
            RequireEndpoint(metadata.RevocationEndpoint, "revocation_endpoint");

        if (!metadata.RequirePushedAuthorizationRequests)
            throw new OAuthException("Authorization server does not require pushed authorization requests.", "invalid_metadata");

        if (!metadata.AuthorizationResponseIssParameterSupported)
            throw new OAuthException("Authorization server does not return the 'iss' response parameter.", "invalid_metadata");

        if (!metadata.ClientIdMetadataDocumentSupported)
            throw new OAuthException("Authorization server does not support client ID metadata documents.", "invalid_metadata");

        if (!metadata.ScopesSupported.Contains("atproto"))
            throw new OAuthException("Authorization server does not support the 'atproto' scope.", "unsupported_scope");

        if (!metadata.DpopSigningAlgValuesSupported.Contains("ES256"))
            throw new OAuthException("Authorization server does not support 'ES256' for DPoP.", "unsupported_dpop_alg");
    }

    private void RequireEndpoint(string? value, string name)
    {
        if (string.IsNullOrEmpty(value))
            throw new OAuthException($"Authorization server metadata missing '{name}'.", "invalid_metadata");

        if (!IsEndpoint(value, _allowPrivateNetworks))
        {
            throw new OAuthException(
                $"Authorization server metadata '{name}' is not an absolute HTTPS URL without query or fragment: '{value}'.",
                "invalid_metadata");
        }
    }

    /// <summary>
    /// Whether <paramref name="value"/> is usable as an authorization server endpoint: an absolute
    /// <c>https</c> URL (or <c>http</c> under the development opt-out) naming a host, with no
    /// userinfo, query or fragment.
    /// </summary>
    internal static bool IsEndpoint(string value, bool allowPrivateNetworks) =>
        !value.Contains('?') && !value.Contains('#') &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && IsEndpoint(uri, allowPrivateNetworks);

    /// <inheritdoc cref="IsEndpoint(string, bool)"/>
    internal static bool IsEndpoint(Uri uri, bool allowPrivateNetworks) =>
        uri.IsAbsoluteUri &&
        (uri.Scheme == Uri.UriSchemeHttps || (allowPrivateNetworks && uri.Scheme == Uri.UriSchemeHttp)) &&
        !string.IsNullOrEmpty(uri.Host) &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    /// <summary>
    /// Whether <paramref name="value"/> is an issuer identifier in canonical form: an endpoint URL
    /// (see <see cref="IsEndpoint(string, bool)"/>) with a lower-case scheme and host, no default
    /// port and no trailing <c>/</c>, as the reference client requires.
    /// </summary>
    internal static bool IsCanonicalIssuer(string? value, bool allowPrivateNetworks)
    {
        if (value is null || !IsEndpoint(value, allowPrivateNetworks))
            return false;

        var uri = new Uri(value, UriKind.Absolute);
        var canonical = uri.GetLeftPart(UriPartial.Authority) + (uri.AbsolutePath == "/" ? "" : uri.AbsolutePath);
        return string.Equals(value, canonical, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a protected resource's <c>resource</c> is the PDS its metadata was fetched from:
    /// the same scheme, host, port and path, a trailing <c>/</c> aside.
    /// </summary>
    internal static bool ResourceMatches(string? resource, string pdsUrl)
    {
        if (resource is null ||
            !Uri.TryCreate(resource, UriKind.Absolute, out var resourceUri) ||
            !Uri.TryCreate(pdsUrl, UriKind.Absolute, out var pdsUri) ||
            resourceUri.UserInfo.Length > 0 || resourceUri.Query.Length > 0 || resourceUri.Fragment.Length > 0)
        {
            return false;
        }

        return string.Equals(Normalize(resourceUri), Normalize(pdsUri), StringComparison.Ordinal);

        static string Normalize(Uri uri) => uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/');
    }

    internal static string NormalizeUrl(string url)
    {
        // A bare host gets https; a URL with another scheme is left for the URL rules to refuse.
        if (!url.Contains("://", StringComparison.Ordinal))
        {
            url = "https://" + url;
        }
        return url.TrimEnd('/');
    }

    /// <summary>Releases the metadata client and identity resolver this instance created, if any.</summary>
    public void Dispose()
    {
        if (_ownsMetadataClient)
            _metadataClient.Dispose();
        _ownedIdentityResolver?.Dispose();
    }

    private readonly record struct CachedDocument(ReadOnlyMemory<byte> Body, DateTimeOffset ExpiresAt);
}

/// <summary>
/// Exception thrown for OAuth-specific errors.
/// </summary>
public sealed class OAuthException : AtProtoException
{
    /// <summary>
    /// The error code: an OAuth <c>error</c> value the authorization server returned (such as
    /// <c>invalid_grant</c>), or one the SDK assigns to a client-side failure (such as
    /// <c>invalid_state</c> or <c>issuer_mismatch</c>).
    /// </summary>
    public string Error { get; }

    /// <summary>
    /// Creates a new OAuth exception.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="error">The error code.</param>
    public OAuthException(string message, string error)
        : base(message)
    {
        Error = error;
    }

    /// <summary>
    /// Creates a new OAuth exception with an inner exception.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="error">The error code.</param>
    /// <param name="innerException">The underlying cause.</param>
    public OAuthException(string message, string error, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }
}
