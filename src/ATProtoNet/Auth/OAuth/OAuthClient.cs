using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// Orchestrates the full AT Protocol OAuth authorization flow including
/// server discovery, PAR, DPoP, PKCE, token exchange, and identity verification.
/// </summary>
/// <remarks>
/// <para>This implements a "public" OAuth client (no client secret).
/// For confidential clients, use <see cref="OAuthClientMetadata.TokenEndpointAuthMethod"/>
/// set to <c>"private_key_jwt"</c> and provide keys via <c>Jwks</c>.</para>
/// <para>Usage flow:</para>
/// <list type="number">
/// <item>Call <see cref="StartAuthorizationAsync"/> to get an authorization URL</item>
/// <item>Redirect the user to that URL</item>
/// <item>Handle the callback via <see cref="CompleteAuthorizationAsync"/></item>
/// <item>Use the returned session with <see cref="AtProtoClient"/></item>
/// </list>
/// </remarks>
public sealed class OAuthClient : IDisposable
{
    private readonly OAuthOptions _options;
    private readonly HttpClient _httpClient;
    private readonly AuthorizationServerDiscovery _discovery;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, PendingAuthorization> _pendingAuthorizations = new();
    private static readonly TimeSpan PendingAuthorizationTimeout = TimeSpan.FromMinutes(10);
    private const int MaxPendingAuthorizations = 100;
    private bool _disposed;

    /// <summary>
    /// Creates a new OAuth client with the specified options.
    /// </summary>
    public OAuthClient(OAuthOptions options, HttpClient httpClient, ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _discovery = new AuthorizationServerDiscovery(httpClient, logger);
    }

    /// <summary>
    /// The authorization server discovery service.
    /// </summary>
    public AuthorizationServerDiscovery Discovery => _discovery;

    /// <summary>
    /// Begins the OAuth authorization flow. Resolves the user's identity, performs
    /// server discovery, makes a Pushed Authorization Request (PAR), and returns
    /// the URL to redirect the user to for authentication.
    /// </summary>
    /// <param name="identifier">
    /// A handle (e.g., "alice.bsky.social"), DID, or PDS/entryway URL.
    /// When a URL is provided, it is used directly as the PDS.
    /// </param>
    /// <param name="redirectUri">The callback URL the user will be redirected to after authorization.</param>
    /// <param name="pdsUrl">
    /// Optional PDS URL. When provided, skips the automatic handle → DID → PDS resolution
    /// and uses this URL directly. The identifier is still passed as a login hint.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The authorization URL to redirect the user to, and the state parameter for verification.
    /// </returns>
    public async Task<(string AuthorizationUrl, string State)> StartAuthorizationAsync(
        string identifier,
        string redirectUri,
        string? pdsUrl = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        // Validate redirect_uri: must be HTTPS (except localhost for development)
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirectUriParsed))
            throw new ArgumentException("Redirect URI must be a valid absolute URL.", nameof(redirectUri));

        if (redirectUriParsed.Scheme != "https" &&
            !(redirectUriParsed.Scheme == "http" && redirectUriParsed.IsLoopback))
        {
            throw new ArgumentException(
                "Redirect URI must use HTTPS (HTTP is only allowed for localhost during development).",
                nameof(redirectUri));
        }

        _logger.LogInformation("Starting OAuth authorization flow for {Identifier}", identifier);

        // Step 1: Resolve identity and Authorization Server
        string resolvedPdsUrl;
        string? expectedDid = null;
        AuthorizationServerMetadata metadata;
        string? loginHint = null;

        if (!string.IsNullOrWhiteSpace(pdsUrl))
        {
            // PDS URL explicitly provided — skip resolution, use identifier as login hint
            resolvedPdsUrl = pdsUrl;
            metadata = await _discovery.ResolveAuthorizationServerAsync(pdsUrl, cancellationToken);
            loginHint = identifier;
        }
        else if (IsUrl(identifier))
        {
            // Starting with PDS URL (identifier IS the URL)
            resolvedPdsUrl = identifier;
            metadata = await _discovery.ResolveAuthorizationServerAsync(resolvedPdsUrl, cancellationToken);
        }
        else
        {
            // Starting with handle or DID — full resolution chain
            var (resolvedPds, resolvedMetadata, did) =
                await _discovery.ResolveFromIdentifierAsync(identifier, cancellationToken);
            resolvedPdsUrl = resolvedPds;
            metadata = resolvedMetadata;
            expectedDid = did;
            loginHint = identifier; // Pass original identifier as login_hint
        }

        // Step 2: Generate PKCE challenge
        var codeVerifier = PkceGenerator.GenerateCodeVerifier();
        var codeChallenge = PkceGenerator.ComputeCodeChallenge(codeVerifier);

        // Step 3: Generate DPoP keypair for this session
        var dpop = new DPoPProofGenerator();

        // Step 4: Generate state parameter
        var state = PkceGenerator.GenerateState();

        // Step 5: Make Pushed Authorization Request (PAR)
        var requestUri = await SendPushedAuthorizationRequestAsync(
            metadata, dpop, state, codeChallenge, redirectUri, loginHint, cancellationToken);

        // Step 6: Clean up expired pending authorizations (prevent unbounded growth)
        CleanupExpiredPendingAuthorizations();

        // Step 7: Store pending authorization state
        var pending = new PendingAuthorization
        {
            State = state,
            CodeVerifier = codeVerifier,
            ExpectedDid = expectedDid,
            Issuer = metadata.Issuer,
            TokenEndpoint = metadata.TokenEndpoint,
            PdsUrl = resolvedPdsUrl,
            DPoP = dpop,
            RedirectUri = redirectUri,
            ClientId = _options.ClientMetadata.ClientId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Enforce maximum pending authorizations to prevent resource exhaustion
        if (_pendingAuthorizations.Count >= MaxPendingAuthorizations)
            throw new OAuthException(
                "Too many pending authorization requests. Please try again later.",
                "server_error");

        _pendingAuthorizations[state] = pending;

        // Step 7: Build authorization URL
        var authUrl = BuildAuthorizationUrl(metadata.AuthorizationEndpoint, requestUri, _options.ClientMetadata.ClientId);

        _logger.LogDebug("OAuth authorization URL generated, state={State}", state);

        return (authUrl, state);
    }

    /// <summary>
    /// Completes the OAuth authorization flow after the user is redirected back.
    /// Exchanges the authorization code for tokens and verifies the identity.
    /// </summary>
    /// <param name="code">The authorization code from the callback.</param>
    /// <param name="state">The state parameter from the callback.</param>
    /// <param name="issuer">The issuer (iss) parameter from the callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The OAuth session containing tokens, DID, and PDS URL.</returns>
    public async Task<OAuthSessionResult> CompleteAuthorizationAsync(
        string code,
        string state,
        string issuer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogDebug("Completing OAuth authorization, state={State}", state);

        // Step 1: Look up pending authorization
        if (!_pendingAuthorizations.TryRemove(state, out var pending))
            throw new OAuthException("Unknown or expired OAuth state parameter.", "invalid_state");

        // Step 2: Verify issuer matches
        if (!string.Equals(pending.Issuer, issuer, StringComparison.OrdinalIgnoreCase))
            throw new OAuthException(
                $"Issuer mismatch. Expected '{pending.Issuer}', got '{issuer}'.",
                "issuer_mismatch");

        // Step 3: Check expiration (10 minutes max)
        if (DateTimeOffset.UtcNow - pending.CreatedAt > TimeSpan.FromMinutes(10))
        {
            pending.DPoP.Dispose();
            throw new OAuthException("OAuth authorization state has expired.", "state_expired");
        }

        // Step 4: Exchange code for tokens
        OAuthTokenResponse tokenResponse;
        try
        {
            tokenResponse = await ExchangeCodeForTokensAsync(
                pending, code, cancellationToken);
        }
        catch
        {
            pending.DPoP.Dispose();
            throw;
        }

        // Step 5: Verify the sub (DID) matches expected
        if (tokenResponse.Sub is null)
        {
            pending.DPoP.Dispose();
            throw new OAuthException("Token response missing 'sub' field.", "missing_sub");
        }

        // Validate DID format (must start with "did:")
        if (!tokenResponse.Sub.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
        {
            pending.DPoP.Dispose();
            throw new OAuthException(
                $"Token response 'sub' is not a valid DID: '{tokenResponse.Sub}'.",
                "invalid_sub");
        }

        if (pending.ExpectedDid is not null &&
            !string.Equals(pending.ExpectedDid, tokenResponse.Sub, StringComparison.Ordinal))
        {
            pending.DPoP.Dispose();
            throw new OAuthException(
                $"Token response DID '{tokenResponse.Sub}' does not match expected '{pending.ExpectedDid}'.",
                "did_mismatch");
        }

        // Step 6: Verify scope includes 'atproto' (exact token match, not substring)
        if (tokenResponse.Scope is null ||
            !tokenResponse.Scope.Split(' ').Contains("atproto", StringComparer.Ordinal))
        {
            pending.DPoP.Dispose();
            throw new OAuthException("Token response does not include 'atproto' scope.", "invalid_scope");
        }

        // Step 7: If started from server (no expected DID), verify DID → PDS → AS consistency
        if (pending.ExpectedDid is null)
        {
            try
            {
                await VerifyDidToAuthServerConsistencyAsync(
                    tokenResponse.Sub, pending.Issuer, cancellationToken);
            }
            catch
            {
                pending.DPoP.Dispose();
                throw;
            }
        }

        // Step 8: Resolve handle from DID document
        string? handle = null;
        try
        {
            var didDoc = await _discovery.FetchDidDocumentAsync(tokenResponse.Sub, cancellationToken);
            handle = didDoc.AlsoKnownAs?
                .FirstOrDefault(a => a.StartsWith("at://", StringComparison.OrdinalIgnoreCase))
                ?["at://".Length..];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve handle from DID document");
        }

        _logger.LogInformation("OAuth flow completed for {Did} ({Handle})", tokenResponse.Sub, handle);

        return new OAuthSessionResult
        {
            Did = tokenResponse.Sub,
            Handle = handle ?? tokenResponse.Sub,
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            TokenType = tokenResponse.TokenType,
            ExpiresIn = tokenResponse.ExpiresIn,
            Scope = tokenResponse.Scope,
            PdsUrl = pending.PdsUrl,
            Issuer = pending.Issuer,
            TokenEndpoint = pending.TokenEndpoint,
            DPoP = pending.DPoP,
            DpopKeyId = pending.DPoP.KeyThumbprint,
        };
    }

    /// <summary>
    /// Refreshes OAuth tokens using the refresh token and DPoP.
    /// </summary>
    /// <param name="session">The current OAuth session result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated token information.</returns>
    public async Task<OAuthTokenResponse> RefreshTokensAsync(
        OAuthSessionResult session,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (session.RefreshToken is null)
            throw new OAuthException("No refresh token available.", "no_refresh_token");

        _logger.LogDebug("Refreshing OAuth tokens for {Did}", session.Did);

        var dpopProof = session.DPoP.GenerateProof("POST", session.TokenEndpoint, session.AuthServerDpopNonce);

        var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = session.RefreshToken,
            ["client_id"] = _options.ClientMetadata.ClientId,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, session.TokenEndpoint)
        {
            Content = requestContent,
        };
        request.Headers.TryAddWithoutValidation("DPoP", dpopProof);

        var response = await SendWithDpopRetryAsync(request, session.DPoP, session.TokenEndpoint, "POST",
            nonce => session.AuthServerDpopNonce = nonce, session.AuthServerDpopNonce, cancellationToken);

        var tokenResponse = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken)
            ?? throw new OAuthException("Failed to deserialize token refresh response.", "token_error");

        // Update nonce
        if (response.Headers.TryGetValues("DPoP-Nonce", out var nonceValues))
            session.AuthServerDpopNonce = nonceValues.First();

        return tokenResponse;
    }

    /// <summary>
    /// Returns the public state of a pending authorization (for serialization/persistence).
    /// </summary>
    /// <remarks>
    /// <b>Security warning:</b> The returned <see cref="OAuthAuthorizationState"/> contains
    /// the PKCE code verifier, which is a secret. Store it securely (e.g. server-side session
    /// or encrypted storage) and never expose it to the client or log it.
    /// </remarks>
    public OAuthAuthorizationState? GetPendingAuthorizationState(string state)
    {
        if (!_pendingAuthorizations.TryGetValue(state, out var pending))
            return null;

        return new OAuthAuthorizationState
        {
            State = pending.State,
            CodeVerifier = pending.CodeVerifier,
            ExpectedDid = pending.ExpectedDid,
            Issuer = pending.Issuer,
            TokenEndpoint = pending.TokenEndpoint,
            PdsUrl = pending.PdsUrl,
            DpopKeyId = pending.DPoP.KeyThumbprint,
            CreatedAt = pending.CreatedAt,
            RedirectUri = pending.RedirectUri,
            ClientId = pending.ClientId,
        };
    }

    private async Task<string> SendPushedAuthorizationRequestAsync(
        AuthorizationServerMetadata metadata,
        DPoPProofGenerator dpop,
        string state,
        string codeChallenge,
        string redirectUri,
        string? loginHint,
        CancellationToken cancellationToken)
    {
        var parUrl = metadata.PushedAuthorizationRequestEndpoint;

        var parameters = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientMetadata.ClientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["scope"] = _options.Scope,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };

        if (loginHint is not null)
            parameters["login_hint"] = loginHint;

        // First attempt - expect DPoP nonce error
        string? dpopNonce = null;
        var dpopProof = dpop.GenerateProof("POST", parUrl, dpopNonce);

        var content = new FormUrlEncodedContent(parameters);
        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, parUrl)
        {
            Content = content,
        };
        firstRequest.Headers.TryAddWithoutValidation("DPoP", dpopProof);

        var firstResponse = await _httpClient.SendAsync(firstRequest, cancellationToken);

        // Extract DPoP nonce from response
        if (firstResponse.Headers.TryGetValues("DPoP-Nonce", out var nonceValues))
            dpopNonce = nonceValues.First();

        // If we got a use_dpop_nonce error, retry with the nonce
        if (firstResponse.StatusCode == HttpStatusCode.BadRequest && dpopNonce is not null)
        {
            _logger.LogDebug("PAR returned use_dpop_nonce, retrying with nonce");

            dpopProof = dpop.GenerateProof("POST", parUrl, dpopNonce);
            content = new FormUrlEncodedContent(parameters);
            using var retryRequest = new HttpRequestMessage(HttpMethod.Post, parUrl)
            {
                Content = content,
            };
            retryRequest.Headers.TryAddWithoutValidation("DPoP", dpopProof);

            var retryResponse = await _httpClient.SendAsync(retryRequest, cancellationToken);

            if (retryResponse.Headers.TryGetValues("DPoP-Nonce", out var retryNonceValues))
                dpopNonce = retryNonceValues.First();

            if (!retryResponse.IsSuccessStatusCode)
            {
                var errorBody = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("PAR failed: {StatusCode} {Body}", retryResponse.StatusCode, errorBody);

                var errorResponse = JsonSerializer.Deserialize<OAuthErrorResponse>(errorBody);
                throw new OAuthException(
                    $"PAR failed: {errorResponse?.Error} - {errorResponse?.ErrorDescription ?? errorBody}",
                    errorResponse?.Error ?? "par_failed");
            }

            var parResult = await retryResponse.Content.ReadFromJsonAsync<PushedAuthorizationResponse>(cancellationToken)
                ?? throw new OAuthException("Failed to deserialize PAR response.", "par_failed");

            return parResult.RequestUri;
        }

        if (!firstResponse.IsSuccessStatusCode)
        {
            var errorBody = await firstResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("PAR failed: {StatusCode} {Body}", firstResponse.StatusCode, errorBody);

            var errorResponse = JsonSerializer.Deserialize<OAuthErrorResponse>(errorBody);
            throw new OAuthException(
                $"PAR failed: {errorResponse?.Error} - {errorResponse?.ErrorDescription ?? errorBody}",
                errorResponse?.Error ?? "par_failed");
        }

        var result = await firstResponse.Content.ReadFromJsonAsync<PushedAuthorizationResponse>(cancellationToken)
            ?? throw new OAuthException("Failed to deserialize PAR response.", "par_failed");

        return result.RequestUri;
    }

    private async Task<OAuthTokenResponse> ExchangeCodeForTokensAsync(
        PendingAuthorization pending,
        string code,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = pending.RedirectUri,
            ["client_id"] = pending.ClientId,
            ["code_verifier"] = pending.CodeVerifier,
        };

        string? dpopNonce = null;
        var dpopProof = pending.DPoP.GenerateProof("POST", pending.TokenEndpoint, dpopNonce);

        var content = new FormUrlEncodedContent(parameters);
        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, pending.TokenEndpoint)
        {
            Content = content,
        };
        firstRequest.Headers.TryAddWithoutValidation("DPoP", dpopProof);

        var firstResponse = await _httpClient.SendAsync(firstRequest, cancellationToken);

        // Extract DPoP nonce
        if (firstResponse.Headers.TryGetValues("DPoP-Nonce", out var nonceValues))
            dpopNonce = nonceValues.First();

        // Handle use_dpop_nonce error - retry with nonce
        if ((firstResponse.StatusCode == HttpStatusCode.BadRequest ||
             firstResponse.StatusCode == HttpStatusCode.Unauthorized) && dpopNonce is not null)
        {
            var firstBody = await firstResponse.Content.ReadAsStringAsync(cancellationToken);
            if (firstBody.Contains("use_dpop_nonce", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Token request returned use_dpop_nonce, retrying with nonce");

                dpopProof = pending.DPoP.GenerateProof("POST", pending.TokenEndpoint, dpopNonce);
                content = new FormUrlEncodedContent(parameters);
                using var retryRequest = new HttpRequestMessage(HttpMethod.Post, pending.TokenEndpoint)
                {
                    Content = content,
                };
                retryRequest.Headers.TryAddWithoutValidation("DPoP", dpopProof);

                var retryResponse = await _httpClient.SendAsync(retryRequest, cancellationToken);

                if (retryResponse.Headers.TryGetValues("DPoP-Nonce", out var retryNonceValues))
                    dpopNonce = retryNonceValues.First();

                if (!retryResponse.IsSuccessStatusCode)
                {
                    var errorBody = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
                    var errorResponse = JsonSerializer.Deserialize<OAuthErrorResponse>(errorBody);
                    throw new OAuthException(
                        $"Token exchange failed: {errorResponse?.Error} - {errorResponse?.ErrorDescription ?? errorBody}",
                        errorResponse?.Error ?? "token_error");
                }

                return await retryResponse.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken)
                    ?? throw new OAuthException("Failed to deserialize token response.", "token_error");
            }
        }

        if (!firstResponse.IsSuccessStatusCode)
        {
            var errorBody = await firstResponse.Content.ReadAsStringAsync(cancellationToken);
            var errorResponse = JsonSerializer.Deserialize<OAuthErrorResponse>(errorBody);
            throw new OAuthException(
                $"Token exchange failed: {errorResponse?.Error} - {errorResponse?.ErrorDescription ?? errorBody}",
                errorResponse?.Error ?? "token_error");
        }

        return await firstResponse.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken)
            ?? throw new OAuthException("Failed to deserialize token response.", "token_error");
    }

    private async Task VerifyDidToAuthServerConsistencyAsync(
        string did, string expectedIssuer, CancellationToken cancellationToken)
    {
        try
        {
            var pdsUrl = await _discovery.ResolvePdsFromDidAsync(did, cancellationToken);
            var metadata = await _discovery.ResolveAuthorizationServerAsync(pdsUrl, cancellationToken);

            if (!string.Equals(metadata.Issuer, expectedIssuer, StringComparison.OrdinalIgnoreCase))
            {
                throw new OAuthException(
                    $"DID '{did}' resolves to Authorization Server '{metadata.Issuer}' " +
                    $"but token was received from '{expectedIssuer}'. Possible security issue.",
                    "auth_server_mismatch");
            }
        }
        catch (OAuthException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OAuthException(
                $"Could not verify DID-to-AuthServer consistency for '{did}'.",
                "verification_failed", ex);
        }
    }

    private async Task<HttpResponseMessage> SendWithDpopRetryAsync(
        HttpRequestMessage request,
        DPoPProofGenerator dpop,
        string url,
        string method,
        Action<string> updateNonce,
        string? currentNonce,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.Headers.TryGetValues("DPoP-Nonce", out var nonceValues))
        {
            var newNonce = nonceValues.First();
            updateNonce(newNonce);

            if ((response.StatusCode == HttpStatusCode.BadRequest ||
                 response.StatusCode == HttpStatusCode.Unauthorized))
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (body.Contains("use_dpop_nonce", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("DPoP nonce error, retrying request");

                    var dpopProof = dpop.GenerateProof(method, url, newNonce);

                    // Rebuild request (can't reuse)
                    using var retryRequest = new HttpRequestMessage(request.Method, request.RequestUri);
                    if (request.Content is not null)
                    {
                        var contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                        retryRequest.Content = new ByteArrayContent(contentBytes);
                        if (request.Content.Headers.ContentType is not null)
                            retryRequest.Content.Headers.ContentType = request.Content.Headers.ContentType;
                    }
                    retryRequest.Headers.TryAddWithoutValidation("DPoP", dpopProof);

                    response = await _httpClient.SendAsync(retryRequest, cancellationToken);

                    if (response.Headers.TryGetValues("DPoP-Nonce", out var retryNonceValues))
                        updateNonce(retryNonceValues.First());
                }
            }
        }

        return response;
    }

    private static string BuildAuthorizationUrl(string authorizationEndpoint, string requestUri, string clientId)
    {
        var uriBuilder = new UriBuilder(authorizationEndpoint);
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);
        query["request_uri"] = requestUri;
        query["client_id"] = clientId;
        uriBuilder.Query = query.ToString();
        return uriBuilder.ToString();
    }

    private static bool IsUrl(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               (value.Contains('.') && !value.Contains('@') && !value.Contains(':') &&
                !value.StartsWith("did:", StringComparison.OrdinalIgnoreCase) &&
                // A bare domain with '/' is a URL
                value.Contains('/'));
    }

    /// <summary>
    /// Removes pending authorizations that are older than <see cref="PendingAuthorizationTimeout"/>.
    /// Called before adding new entries to prevent unbounded growth of the dictionary.
    /// </summary>
    private void CleanupExpiredPendingAuthorizations()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _pendingAuthorizations)
        {
            if (now - kvp.Value.CreatedAt > PendingAuthorizationTimeout)
            {
                if (_pendingAuthorizations.TryRemove(kvp.Key, out var expired))
                {
                    expired.DPoP.Dispose();
                    _logger.LogDebug("Cleaned up expired pending authorization, state={State}", kvp.Key);
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            foreach (var pending in _pendingAuthorizations.Values)
                pending.DPoP.Dispose();
            _pendingAuthorizations.Clear();
        }
    }

    /// <summary>
    /// Internal state for a pending authorization.
    /// </summary>
    private sealed class PendingAuthorization
    {
        public string State { get; init; } = string.Empty;
        public string CodeVerifier { get; init; } = string.Empty;
        public string? ExpectedDid { get; init; }
        public string Issuer { get; init; } = string.Empty;
        public string TokenEndpoint { get; init; } = string.Empty;
        public string PdsUrl { get; init; } = string.Empty;
        public DPoPProofGenerator DPoP { get; init; } = null!;
        public string RedirectUri { get; init; } = string.Empty;
        public string ClientId { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; }
    }
}

/// <summary>
/// Result of a completed OAuth authorization flow.
/// Contains the tokens, identity info, and DPoP key needed for authenticated requests.
/// </summary>
public sealed class OAuthSessionResult : IDisposable
{
    /// <summary>The DID of the authenticated account.</summary>
    public string Did { get; init; } = string.Empty;

    /// <summary>The handle of the authenticated account.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>The OAuth access token (opaque, DPoP-bound).</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>The OAuth refresh token.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>The token type (should be "DPoP").</summary>
    public string TokenType { get; init; } = string.Empty;

    /// <summary>Token expiration in seconds.</summary>
    public int? ExpiresIn { get; init; }

    /// <summary>The granted scopes.</summary>
    public string? Scope { get; init; }

    /// <summary>The PDS (Resource Server) URL.</summary>
    public string PdsUrl { get; init; } = string.Empty;

    /// <summary>The Authorization Server issuer URL.</summary>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>The token endpoint URL (for refresh).</summary>
    public string TokenEndpoint { get; init; } = string.Empty;

    /// <summary>The DPoP proof generator bound to this session.</summary>
    public DPoPProofGenerator DPoP { get; init; } = null!;

    /// <summary>The DPoP key thumbprint.</summary>
    public string DpopKeyId { get; init; } = string.Empty;

    /// <summary>The current DPoP nonce for the Authorization Server.</summary>
    public string? AuthServerDpopNonce { get; set; }

    /// <summary>The current DPoP nonce for the Resource Server (PDS).</summary>
    public string? ResourceServerDpopNonce { get; set; }

    /// <summary>When the access token was obtained.</summary>
    public DateTimeOffset TokenObtainedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public void Dispose()
    {
        DPoP?.Dispose();
    }
}
