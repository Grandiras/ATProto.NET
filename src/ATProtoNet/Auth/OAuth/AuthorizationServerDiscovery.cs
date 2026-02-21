using System.Net.Http.Json;
using System.Text.Json;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// Discovers Authorization Server and Protected Resource metadata for AT Protocol OAuth.
/// Handles the full resolution chain: handle → DID → DID document → PDS → Authorization Server.
/// </summary>
public sealed class AuthorizationServerDiscovery
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Creates a new discovery instance.
    /// </summary>
    /// <param name="httpClient">HTTP client for making metadata requests.</param>
    /// <param name="logger">Logger.</param>
    public AuthorizationServerDiscovery(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _jsonOptions = AtProtoJsonDefaults.Options;
    }

    /// <summary>
    /// Resolves an account identifier (handle or DID) to the PDS URL and Authorization Server metadata.
    /// </summary>
    /// <param name="identifier">A handle (e.g., "alice.bsky.social") or DID (e.g., "did:plc:...").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved PDS URL, Authorization Server metadata, and DID.</returns>
    public async Task<(string PdsUrl, AuthorizationServerMetadata Metadata, string Did)>
        ResolveFromIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resolving identity for OAuth: {Identifier}", identifier);

        // Step 1: Resolve to DID
        string did;
        if (identifier.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
        {
            did = identifier;
        }
        else
        {
            did = await ResolveHandleToDidAsync(identifier, cancellationToken);
            _logger.LogDebug("Resolved handle {Handle} to DID {Did}", identifier, did);
        }

        // Step 2: Resolve DID → DID document → PDS
        var pdsUrl = await ResolvePdsFromDidAsync(did, cancellationToken);
        _logger.LogDebug("Resolved DID {Did} to PDS {PdsUrl}", did, pdsUrl);

        // Step 3: Fetch PDS metadata → Authorization Server
        var metadata = await ResolveAuthorizationServerAsync(pdsUrl, cancellationToken);

        return (pdsUrl, metadata, did);
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
    /// Resolves a handle to a DID using the AT Protocol handle resolution methods.
    /// Tries HTTPS resolution first (via any PDS), then DNS TXT.
    /// </summary>
    public async Task<string> ResolveHandleToDidAsync(
        string handle, CancellationToken cancellationToken = default)
    {
        // Validate handle format to prevent SSRF via path traversal or host injection.
        // AT Proto handles are domain names: labels separated by dots, each 1-63 alphanumeric/hyphen chars.
        ValidateHandleFormat(handle);

        // Try HTTPS resolution: GET https://handle/.well-known/atproto-did
        try
        {
            var url = $"https://{handle}/.well-known/atproto-did";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var did = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
                if (did.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
                    return did;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HTTPS handle resolution failed for {Handle}, trying DNS", handle);
        }

        // Try DNS TXT resolution via DNS-over-HTTPS (works in browser environments too)
        try
        {
            var dnsUrl = $"https://dns.google/resolve?name=_atproto.{handle}&type=TXT";
            var dnsResponse = await _httpClient.GetFromJsonAsync<DnsResponse>(dnsUrl, cancellationToken);
            if (dnsResponse?.Answer is { } answers)
            {
                foreach (var answer in answers)
                {
                    var data = answer.Data?.Trim('"');
                    if (data?.StartsWith("did=", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return data["did=".Length..];
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DNS-over-HTTPS handle resolution failed for {Handle}", handle);
        }

        // Fallback: try resolving via bsky.social API
        try
        {
            var apiUrl = $"https://bsky.social/xrpc/com.atproto.identity.resolveHandle?handle={Uri.EscapeDataString(handle)}";
            var resolveResponse = await _httpClient.GetFromJsonAsync<ResolveHandleFallbackResponse>(
                apiUrl, _jsonOptions, cancellationToken);
            if (resolveResponse?.Did is not null)
                return resolveResponse.Did;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Fallback handle resolution via bsky.social failed for {Handle}", handle);
        }

        throw new OAuthException(
            $"Could not resolve handle '{handle}' to a DID.",
            "handle_resolution_failed");
    }

    /// <summary>
    /// Resolves a DID to the PDS URL by fetching the DID document.
    /// </summary>
    public async Task<string> ResolvePdsFromDidAsync(
        string did, CancellationToken cancellationToken = default)
    {
        var didDoc = await FetchDidDocumentAsync(did, cancellationToken);

        var pdsService = didDoc.Service?.FirstOrDefault(s =>
            s.Id == "#atproto_pds" || s.Type == "AtprotoPersonalDataServer");

        if (pdsService is null)
            throw new OAuthException(
                $"DID document for '{did}' does not contain an atproto PDS service.",
                "pds_not_found");

        return pdsService.ServiceEndpoint;
    }

    /// <summary>
    /// Fetches a DID document for the given DID.
    /// Supports did:plc and did:web methods.
    /// </summary>
    public async Task<DidDocument> FetchDidDocumentAsync(
        string did, CancellationToken cancellationToken = default)
    {
        string url;
        if (did.StartsWith("did:plc:", StringComparison.OrdinalIgnoreCase))
        {
            url = $"https://plc.directory/{did}";
        }
        else if (did.StartsWith("did:web:", StringComparison.OrdinalIgnoreCase))
        {
            var host = did["did:web:".Length..].Replace(':', '/');
            ValidateDidWebHost(host.Split('/')[0]);
            url = $"https://{host}/.well-known/did.json";
        }
        else
        {
            throw new OAuthException($"Unsupported DID method in '{did}'.", "unsupported_did_method");
        }

        _logger.LogDebug("Fetching DID document from {Url}", url);

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var didDoc = await response.Content.ReadFromJsonAsync<DidDocument>(_jsonOptions, cancellationToken)
            ?? throw new OAuthException($"Failed to deserialize DID document for '{did}'.", "did_resolution_failed");

        return didDoc;
    }

    /// <summary>
    /// Fetches the Protected Resource metadata from a PDS.
    /// </summary>
    public async Task<ProtectedResourceMetadata> FetchProtectedResourceMetadataAsync(
        string pdsUrl, CancellationToken cancellationToken = default)
    {
        pdsUrl = NormalizeUrl(pdsUrl);
        var metadataUrl = $"{pdsUrl}/.well-known/oauth-protected-resource";

        _logger.LogDebug("Fetching protected resource metadata from {Url}", metadataUrl);

        var response = await _httpClient.GetAsync(metadataUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ProtectedResourceMetadata>(_jsonOptions, cancellationToken)
            ?? throw new OAuthException("Failed to deserialize protected resource metadata.", "metadata_fetch_failed");
    }

    /// <summary>
    /// Fetches the Authorization Server metadata.
    /// </summary>
    public async Task<AuthorizationServerMetadata> FetchAuthorizationServerMetadataAsync(
        string authServerUrl, CancellationToken cancellationToken = default)
    {
        authServerUrl = NormalizeUrl(authServerUrl);
        var metadataUrl = $"{authServerUrl}/.well-known/oauth-authorization-server";

        _logger.LogDebug("Fetching authorization server metadata from {Url}", metadataUrl);

        var response = await _httpClient.GetAsync(metadataUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AuthorizationServerMetadata>(_jsonOptions, cancellationToken)
            ?? throw new OAuthException("Failed to deserialize authorization server metadata.", "metadata_fetch_failed");
    }

    private static void ValidateAuthorizationServerMetadata(AuthorizationServerMetadata metadata, string expectedIssuerOrigin)
    {
        if (string.IsNullOrEmpty(metadata.Issuer))
            throw new OAuthException("Authorization server metadata missing 'issuer' field.", "invalid_metadata");

        // Verify issuer matches the origin URL
        var issuerUri = new Uri(metadata.Issuer);
        var expectedUri = new Uri(expectedIssuerOrigin);
        if (issuerUri.Scheme != expectedUri.Scheme || issuerUri.Host != expectedUri.Host)
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

    /// <summary>
    /// Validates that the handle conforms to the AT Protocol handle format (RFC-compliant hostname).
    /// Prevents SSRF attacks via path traversal or host injection in handle resolution URLs.
    /// </summary>
    private static void ValidateHandleFormat(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw new OAuthException("Handle cannot be empty.", "invalid_handle");

        if (handle.Length > 253)
            throw new OAuthException("Handle exceeds maximum length.", "invalid_handle");

        // Must not contain characters that could break URL structure
        if (handle.Contains('/') || handle.Contains('\\') || handle.Contains('?') ||
            handle.Contains('#') || handle.Contains('@') || handle.Contains(' ') ||
            handle.Contains("..", StringComparison.Ordinal) ||
            handle.StartsWith('.') || handle.EndsWith('.'))
            throw new OAuthException($"Handle '{handle}' contains invalid characters.", "invalid_handle");

        // Each label must be 1-63 chars, alphanumeric or hyphen
        var labels = handle.Split('.');
        if (labels.Length < 2)
            throw new OAuthException($"Handle '{handle}' must contain at least two domain labels.", "invalid_handle");

        foreach (var label in labels)
        {
            if (label.Length == 0 || label.Length > 63)
                throw new OAuthException($"Handle '{handle}' has an invalid domain label length.", "invalid_handle");

            if (label.StartsWith('-') || label.EndsWith('-'))
                throw new OAuthException($"Handle '{handle}' has a label starting or ending with hyphen.", "invalid_handle");

            foreach (var c in label)
            {
                if (!char.IsLetterOrDigit(c) && c != '-')
                    throw new OAuthException($"Handle '{handle}' contains invalid character '{c}'.", "invalid_handle");
            }
        }
    }

    /// <summary>
    /// Validates a DID:web host segment to prevent SSRF.
    /// </summary>
    private static void ValidateDidWebHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new OAuthException("DID:web host cannot be empty.", "invalid_did");

        // Block URL-unsafe characters
        if (host.Contains('?') || host.Contains('#') || host.Contains('@') || host.Contains(' '))
            throw new OAuthException($"DID:web host '{host}' contains invalid characters.", "invalid_did");

        // Block localhost (case-insensitive)
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            throw new OAuthException($"DID:web host '{host}' points to a private address.", "invalid_did");

        // Block IPv6 loopback and private addresses
        if (host.StartsWith('['))
        {
            // IPv6 addresses in brackets — block all (public IPv6 should use domain names)
            throw new OAuthException($"DID:web host '{host}' uses an IP address. Use a domain name.", "invalid_did");
        }

        // Parse as IP address to accurately check private ranges
        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            if (IsPrivateOrReservedIp(ip))
                throw new OAuthException($"DID:web host '{host}' points to a private address.", "invalid_did");
        }
    }

    /// <summary>
    /// Checks whether an IP address belongs to private, loopback, or reserved ranges.
    /// </summary>
    private static bool IsPrivateOrReservedIp(System.Net.IPAddress ip)
    {
        // Map IPv6-mapped IPv4 addresses to their IPv4 equivalent
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        // Loopback: 127.0.0.0/8, ::1
        if (System.Net.IPAddress.IsLoopback(ip))
            return true;

        var bytes = ip.GetAddressBytes();

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10)
                return true;

            // 172.16.0.0/12 (172.16.x.x – 172.31.x.x)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;

            // 169.254.0.0/16 (link-local)
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;

            // 0.0.0.0/8
            if (bytes[0] == 0)
                return true;

            // 100.64.0.0/10 (Carrier-grade NAT / shared address space)
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                return true;
        }
        else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // Link-local: fe80::/10
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                return true;

            // Unique local: fc00::/7
            if ((bytes[0] & 0xfe) == 0xfc)
                return true;

            // Unspecified address ::
            if (ip.Equals(System.Net.IPAddress.IPv6None))
                return true;
        }

        return false;
    }

    private static string NormalizeUrl(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }
        return url.TrimEnd('/');
    }

    // Helper types for DNS-over-HTTPS response
    private sealed class DnsResponse
    {
        public List<DnsAnswer>? Answer { get; set; }
    }

    private sealed class DnsAnswer
    {
        public string? Data { get; set; }
    }

    private sealed class ResolveHandleFallbackResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("did")]
        public string? Did { get; set; }
    }
}

/// <summary>
/// Exception thrown for OAuth-specific errors.
/// </summary>
public sealed class OAuthException : Exception
{
    /// <summary>
    /// The OAuth error code.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Creates a new OAuth exception.
    /// </summary>
    public OAuthException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Creates a new OAuth exception with an inner exception.
    /// </summary>
    public OAuthException(string message, string errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
