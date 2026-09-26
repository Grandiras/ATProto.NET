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
/// timeout. The pushed-authorization, token and revocation endpoints the metadata names are not
/// yet under it.</para>
/// </remarks>
public sealed class AuthorizationServerDiscovery : IDisposable
{
    /// <summary>
    /// Default budget applied to each handle resolution round (5 seconds).
    /// </summary>
    public static readonly TimeSpan DefaultHandleResolutionTimeout = TimeSpan.FromSeconds(5);

    // Protected-resource and authorization-server metadata are a few kilobytes.
    private const int MaxMetadataBytes = 64 * 1024;
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _metadataClient;
    private readonly bool _ownsMetadataClient;
    private readonly bool _allowPrivateNetworks;
    private readonly IdentityResolver? _ownedIdentityResolver;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;

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
    internal async Task<ResolvedIdentity> ResolveIdentityAsync(AtIdentifier identifier, CancellationToken cancellationToken)
    {
        try
        {
            return await IdentityResolver.ResolveAsync(identifier, cancellationToken);
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

    private static AtIdentifier ParseIdentifier(string identifier)
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
    /// <returns>The Authorization Server metadata.</returns>
    public async Task<AuthorizationServerMetadata> ResolveAuthorizationServerAsync(
        string pdsUrl, CancellationToken cancellationToken = default)
    {
        pdsUrl = NormalizeUrl(pdsUrl);

        // Step 1: Fetch the Resource Server (PDS) protected resource metadata
        var resourceMetadata = await FetchProtectedResourceMetadataAsync(pdsUrl, cancellationToken);

        if (resourceMetadata?.AuthorizationServers is not { Count: > 0 })
        {
            throw new OAuthException(
                "PDS protected resource metadata does not contain any authorization servers.",
                "invalid_resource_metadata");
        }

        var authServerUrl = resourceMetadata.AuthorizationServers[0];
        _logger.LogDebug("PDS {PdsUrl} points to Authorization Server {AuthServer}", pdsUrl, authServerUrl);

        // Step 2: Fetch the Authorization Server metadata
        var metadata = await FetchAuthorizationServerMetadataAsync(authServerUrl, cancellationToken);

        // Validate essential fields
        ValidateAuthorizationServerMetadata(metadata, authServerUrl);

        return metadata;
    }

    /// <summary>
    /// Fetches the Protected Resource metadata from a PDS.
    /// </summary>
    /// <exception cref="OAuthException">
    /// Thrown when the URL is refused (<c>invalid_server_url</c>), the request fails
    /// (<c>metadata_fetch_failed</c>) or the answer is not metadata (<c>invalid_metadata</c>).
    /// </exception>
    public Task<ProtectedResourceMetadata> FetchProtectedResourceMetadataAsync(
        string pdsUrl, CancellationToken cancellationToken = default) =>
        FetchMetadataAsync<ProtectedResourceMetadata>(pdsUrl, ".well-known/oauth-protected-resource", cancellationToken);

    /// <summary>
    /// Fetches the Authorization Server metadata.
    /// </summary>
    /// <exception cref="OAuthException">
    /// Thrown when the URL is refused (<c>invalid_server_url</c>), the request fails
    /// (<c>metadata_fetch_failed</c>) or the answer is not metadata (<c>invalid_metadata</c>).
    /// </exception>
    public Task<AuthorizationServerMetadata> FetchAuthorizationServerMetadataAsync(
        string authServerUrl, CancellationToken cancellationToken = default) =>
        FetchMetadataAsync<AuthorizationServerMetadata>(authServerUrl, ".well-known/oauth-authorization-server", cancellationToken);

    private async Task<T> FetchMetadataAsync<T>(string serverUrl, string wellKnown, CancellationToken cancellationToken)
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

        try
        {
            return JsonSerializer.Deserialize<T>(result.Body.Span, _jsonOptions)
                ?? throw new OAuthException($"{url} returned no metadata.", "metadata_fetch_failed");
        }
        catch (JsonException ex)
        {
            throw new OAuthException($"{url} returned malformed metadata: {ex.Message}", "invalid_metadata", ex);
        }
    }

    private static void ValidateAuthorizationServerMetadata(AuthorizationServerMetadata metadata, string expectedIssuerOrigin)
    {
        if (string.IsNullOrEmpty(metadata.Issuer))
            throw new OAuthException("Authorization server metadata missing 'issuer' field.", "invalid_metadata");

        // Verify issuer matches the origin URL
        if (!Uri.TryCreate(metadata.Issuer, UriKind.Absolute, out var issuerUri) ||
            !Uri.TryCreate(NormalizeUrl(expectedIssuerOrigin), UriKind.Absolute, out var expectedUri) ||
            issuerUri.Scheme != expectedUri.Scheme || issuerUri.Host != expectedUri.Host)
            throw new OAuthException(
                $"Authorization server issuer '{metadata.Issuer}' does not match expected '{expectedIssuerOrigin}'.",
                "issuer_mismatch");

        if (string.IsNullOrEmpty(metadata.AuthorizationEndpoint))
            throw new OAuthException("Authorization server metadata missing 'authorization_endpoint'.", "invalid_metadata");

        if (string.IsNullOrEmpty(metadata.TokenEndpoint))
            throw new OAuthException("Authorization server metadata missing 'token_endpoint'.", "invalid_metadata");

        if (string.IsNullOrEmpty(metadata.PushedAuthorizationRequestEndpoint))
            throw new OAuthException("Authorization server metadata missing 'pushed_authorization_request_endpoint'. PAR is required.", "invalid_metadata");

        if (!metadata.ScopesSupported.Contains("atproto"))
            throw new OAuthException("Authorization server does not support the 'atproto' scope.", "unsupported_scope");

        if (!metadata.DpopSigningAlgValuesSupported.Contains("ES256"))
            throw new OAuthException("Authorization server does not support 'ES256' for DPoP.", "unsupported_dpop_alg");
    }

    private static string NormalizeUrl(string url)
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
