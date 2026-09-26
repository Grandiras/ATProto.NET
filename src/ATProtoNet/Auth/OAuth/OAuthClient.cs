using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using ATProtoNet.Identity;
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
/// <item>Install the returned <see cref="Auth.OAuthSession"/> with
/// <see cref="AtProtoClient.ApplySessionAsync"/>, passing this client so the session can be
/// refreshed</item>
/// </list>
/// <para>The client is thread-safe and meant to be shared: one per application (one
/// <c>client_id</c>), not one per user.</para>
/// </remarks>
public sealed class OAuthClient : IDisposable
{
    private readonly OAuthOptions _options;
    private readonly HttpClient _httpClient;
    private readonly AuthorizationServerDiscovery _discovery;
    private readonly IdentityResolver? _ownedIdentityResolver;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, PendingAuthorization> _pendingAuthorizations = new();

    // The latest DPoP nonce each authorization server handed out, by origin, so a refresh or
    // revocation does not pay a use_dpop_nonce round trip every time.
    private readonly ConcurrentDictionary<string, string> _authServerNonces = new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan PendingAuthorizationTimeout = TimeSpan.FromMinutes(10);
    private const int MaxPendingAuthorizations = 100;

    /// <summary>The most of an error body read from an authorization server.</summary>
    private const int MaxErrorBodyBytes = 64 * 1024;

    private bool _disposed;

    /// <summary>
    /// Creates a new OAuth client with the specified options.
    /// </summary>
    public OAuthClient(OAuthOptions options, HttpClient httpClient, ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // A resolver the options supply is the caller's; one created here is this client's, and
        // goes with it on Dispose.
        var identityResolver = _options.IdentityResolver;
        if (identityResolver is null)
        {
            identityResolver = _ownedIdentityResolver = IdentityResolver.CreateDefault(
                new IdentityResolverOptions
                {
                    HandleResolutionTimeout = _options.HandleResolutionTimeout,
                    AllowPrivateNetworks = _options.AllowPrivateNetworks,
                },
                logger);
        }

        _discovery = new AuthorizationServerDiscovery(
            _options.MetadataHttpClient, logger, identityResolver, _options.AllowPrivateNetworks);
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
            RevocationEndpoint = metadata.RevocationEndpoint,
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
    /// <returns>
    /// The session: the account's DID and PDS, its tokens and the DPoP key they are bound to.
    /// </returns>
    /// <remarks>
    /// Handle verification is best-effort: an unreachable, slow, or silent handle
    /// authority yields a session whose <see cref="Auth.AtProtoSession.Handle"/> is
    /// <c>handle.invalid</c>, never an exception. Only <paramref name="cancellationToken"/>
    /// being cancelled aborts the flow at that point — the DID is already established by the
    /// token response's <c>sub</c>.
    /// </remarks>
    public async Task<Auth.OAuthSession> CompleteAuthorizationAsync(
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

        if (!Did.TryParse(tokenResponse.Sub, out var did))
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
        ResolvedIdentity? identity = null;
        if (pending.ExpectedDid is null)
        {
            try
            {
                identity = await VerifyDidToAuthServerConsistencyAsync(
                    tokenResponse.Sub, pending.Issuer, cancellationToken);
            }
            catch
            {
                pending.DPoP.Dispose();
                throw;
            }
        }

        // Step 8: The handle, verified bidirectionally: the DID document claims it in
        // alsoKnownAs AND the handle's own authorities (DNS TXT _atproto.<handle>,
        // /.well-known/atproto-did) map it back to this DID. Without the second check, a PDS
        // could announce any handle for its users.
        //
        // Nothing here can fail the login: the authoritative DID is the token response's `sub`,
        // already in hand. A handle authority that cannot be reached leaves the handle
        // unverified, and a DID document that cannot be fetched does the same — only the
        // CALLER cancelling means the session is no longer wanted.
        string? handle = null;
        try
        {
            identity ??= await _discovery.IdentityResolver.ResolveAsync(
                AtIdentifier.FromDid(Did.Parse(tokenResponse.Sub)), cancellationToken);

            if (identity.HandleVerified)
                handle = identity.Handle!.Value;
            else if (identity.Document.GetHandle() is { } claimed)
                _logger.LogWarning("Handle '{Handle}' of {Did} does not verify; treating as unverified.", claimed, tokenResponse.Sub);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // No session is being returned, so nothing else will dispose the key.
            pending.DPoP.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve the identity of {Did} to verify its handle.", tokenResponse.Sub);
        }

        // A handle that did not verify is no handle: the session carries 'handle.invalid', the
        // atproto convention, and the DID stays the account's identifier.
        var verifiedHandle = handle is not null && Handle.TryParse(handle, out var parsedHandle) ? parsedHandle : null;

        _logger.LogInformation("OAuth flow completed for {Did} (Handle={Handle}, Verified={Verified})",
            did, handle ?? did.Value, verifiedHandle is not null);

        try
        {
            return new Auth.OAuthSession
            {
                Did = did,
                Handle = verifiedHandle ?? Handle.Invalid,
                ServiceEndpoint = new Uri(pending.PdsUrl, UriKind.Absolute),
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAt = ExpiresAt(tokenResponse),
                Scope = tokenResponse.Scope,
                DPoPKey = pending.DPoP.ExportPrivateKey(),
                Issuer = pending.Issuer,
                TokenEndpoint = new Uri(pending.TokenEndpoint, UriKind.Absolute),
                RevocationEndpoint = Uri.TryCreate(pending.RevocationEndpoint, UriKind.Absolute, out var revocation)
                    ? revocation
                    : null,
            };
        }
        finally
        {
            // The session carries the key as bytes; this key object has served its purpose.
            pending.DPoP.Dispose();
        }
    }

    /// <summary>
    /// Exchanges the session's refresh token for new tokens, and returns the refreshed session.
    /// </summary>
    /// <param name="session">The session to refresh.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The session with the new access token, its expiry, and the rotated refresh token. The old
    /// refresh token is spent: persist the result before anything else can refresh.
    /// </returns>
    /// <exception cref="OAuthException">
    /// The authorization server refused the refresh. <see cref="OAuthException.Error"/> is the
    /// OAuth error, <c>invalid_grant</c> when the refresh token has expired, been revoked or been
    /// used already, in which case the user has to authorize again.
    /// </exception>
    /// <remarks>
    /// <see cref="AtProtoClient"/> refreshes the sessions it holds by itself; call this only to
    /// manage a session outside one.
    /// </remarks>
    public async Task<Auth.OAuthSession> RefreshAsync(
        Auth.OAuthSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using var dpop = new DPoPProofGenerator(session.DPoPKey.ToArray());
        return await RefreshAsync(session, dpop, cancellationToken);
    }

    /// <summary>
    /// <see cref="RefreshAsync(Auth.OAuthSession, CancellationToken)"/> with the session's DPoP
    /// key already loaded.
    /// </summary>
    internal async Task<Auth.OAuthSession> RefreshAsync(
        Auth.OAuthSession session,
        DPoPProofGenerator dpop,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrEmpty(session.RefreshToken))
            throw new OAuthException("The session has no refresh token.", "no_refresh_token");

        _logger.LogDebug("Refreshing OAuth tokens for {Did}", session.Did);

        using var response = await PostWithDPoPAsync(
            session.TokenEndpoint,
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = session.RefreshToken,
                ["client_id"] = _options.ClientMetadata.ClientId,
            },
            dpop,
            "Token refresh",
            cancellationToken);

        OAuthTokenResponse? tokens;
        try
        {
            tokens = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new OAuthException("The token refresh response is not valid JSON.", "token_error", ex);
        }

        // A success status with no usable token is as much a failure as an error body: storing
        // it would replace a working session with one that cannot authenticate anything.
        if (tokens is null || string.IsNullOrEmpty(tokens.AccessToken))
            throw new OAuthException("The token refresh response carries no access token.", "token_error");

        if (!string.Equals(tokens.TokenType, "DPoP", StringComparison.OrdinalIgnoreCase))
        {
            throw new OAuthException(
                $"The token refresh response has token type '{tokens.TokenType}'; AT Protocol tokens are DPoP-bound.",
                "token_error");
        }

        return session with
        {
            AccessToken = tokens.AccessToken,

            // A refresh response may omit refresh_token to mean "keep using the current one".
            RefreshToken = tokens.RefreshToken ?? session.RefreshToken,
            ExpiresAt = ExpiresAt(tokens),
            Scope = tokens.Scope ?? session.Scope,
        };
    }

    /// <summary>
    /// Revokes the session at its authorization server (RFC 7009), so its tokens stop working
    /// everywhere. The refresh token is revoked, which ends the whole grant; a session without
    /// one revokes its access token.
    /// </summary>
    /// <param name="session">The session to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="OAuthException">The authorization server answered with an error.</exception>
    /// <remarks>
    /// <para>A server that publishes no <c>revocation_endpoint</c> has nothing to call, and this
    /// returns without error. Per RFC 7009 a token the server no longer knows is not an error
    /// either, so revoking an expired session succeeds.</para>
    /// <para><see cref="AtProtoClient.LogoutAsync"/> calls this for an OAuth session.</para>
    /// </remarks>
    public async Task RevokeAsync(Auth.OAuthSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using var dpop = new DPoPProofGenerator(session.DPoPKey.ToArray());
        await RevokeAsync(session, dpop, cancellationToken);
    }

    /// <summary>
    /// <see cref="RevokeAsync(Auth.OAuthSession, CancellationToken)"/> with the session's DPoP
    /// key already loaded.
    /// </summary>
    internal async Task RevokeAsync(Auth.OAuthSession session, DPoPProofGenerator dpop, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var endpoint = session.RevocationEndpoint;
        if (endpoint is null)
        {
            var metadata = await _discovery.FetchAuthorizationServerMetadataAsync(session.Issuer, cancellationToken);
            if (!Uri.TryCreate(metadata.RevocationEndpoint, UriKind.Absolute, out endpoint))
            {
                _logger.LogInformation(
                    "Authorization server {Issuer} publishes no revocation endpoint; nothing to revoke", session.Issuer);
                return;
            }
        }

        var (token, hint) = string.IsNullOrEmpty(session.RefreshToken)
            ? (session.AccessToken, "access_token")
            : (session.RefreshToken, "refresh_token");

        using var response = await PostWithDPoPAsync(
            endpoint,
            new Dictionary<string, string>
            {
                ["token"] = token,
                ["token_type_hint"] = hint,
                ["client_id"] = _options.ClientMetadata.ClientId,
            },
            dpop,
            "Token revocation",
            cancellationToken);

        _logger.LogDebug("Revoked the OAuth session of {Did}", session.Did);
    }

    private static DateTimeOffset? ExpiresAt(OAuthTokenResponse tokens) =>
        tokens.ExpiresIn is { } seconds && seconds > 0 ? DateTimeOffset.UtcNow.AddSeconds(seconds) : null;

    /// <summary>
    /// POSTs a form to an authorization server endpoint with a DPoP proof, answering one
    /// <c>use_dpop_nonce</c> challenge (RFC 9449 section 8), and returns the successful response.
    /// </summary>
    /// <exception cref="OAuthException">The server answered with an error, carried as <see cref="OAuthException.Error"/>.</exception>
    private async Task<HttpResponseMessage> PostWithDPoPAsync(
        Uri endpoint,
        Dictionary<string, string> form,
        DPoPProofGenerator dpop,
        string operation,
        CancellationToken cancellationToken)
    {
        var origin = endpoint.GetLeftPart(UriPartial.Authority);

        for (var attempt = 1; ; attempt++)
        {
            _authServerNonces.TryGetValue(origin, out var nonce);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent(form),
            };
            request.Headers.TryAddWithoutValidation("DPoP", dpop.GenerateProof("POST", endpoint.ToString(), nonce));

            var response = await _httpClient.SendAsync(request, cancellationToken);

            string? freshNonce = null;
            if (response.Headers.TryGetValues("DPoP-Nonce", out var nonces) &&
                nonces.FirstOrDefault() is { Length: > 0 } value)
            {
                freshNonce = value;
                _authServerNonces[origin] = value;
            }

            if (response.IsSuccessStatusCode)
                return response;

            OAuthErrorResponse? error;
            using (response)
                error = await ReadErrorAsync(response, cancellationToken);

            if (attempt == 1 && freshNonce is not null && error?.Error == "use_dpop_nonce")
            {
                _logger.LogDebug("{Operation} asked for a DPoP nonce; retrying with it", operation);
                continue;
            }

            var code = string.IsNullOrEmpty(error?.Error) ? "server_error" : error.Error;
            throw new OAuthException(
                $"{operation} failed with {(int)response.StatusCode} {code}" +
                (string.IsNullOrEmpty(error?.ErrorDescription) ? "." : $": {error.ErrorDescription}"),
                code);
        }
    }

    private static async Task<OAuthErrorResponse?> ReadErrorAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadBoundedAsync(MaxErrorBodyBytes, cancellationToken);
        if (body is not { Length: > 0 } bytes)
            return null;

        try
        {
            return JsonSerializer.Deserialize<OAuthErrorResponse>(bytes.Span);
        }
        catch (JsonException)
        {
            return null;
        }
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

    private async Task<ResolvedIdentity> VerifyDidToAuthServerConsistencyAsync(
        string did, string expectedIssuer, CancellationToken cancellationToken)
    {
        try
        {
            var identity = await _discovery.ResolveIdentityAsync(AtIdentifier.FromDid(Did.Parse(did)), cancellationToken);
            var pdsUrl = identity.PdsEndpoint
                ?? throw new OAuthException(
                    $"DID document for '{did}' does not contain an atproto PDS service.", "pds_not_found");
            var metadata = await _discovery.ResolveAuthorizationServerAsync(pdsUrl.OriginalString, cancellationToken);

            if (!string.Equals(metadata.Issuer, expectedIssuer, StringComparison.OrdinalIgnoreCase))
            {
                throw new OAuthException(
                    $"DID '{did}' resolves to Authorization Server '{metadata.Issuer}' " +
                    $"but token was received from '{expectedIssuer}'. Possible security issue.",
                    "auth_server_mismatch");
            }

            return identity;
        }
        catch (OAuthException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelling is not a failed security check — report it as
            // cancellation rather than as an inconsistent identity. A probe that
            // merely timed out still falls through to the fail-closed wrap below.
            throw;
        }
        catch (Exception ex)
        {
            throw new OAuthException(
                $"Could not verify DID-to-AuthServer consistency for '{did}'.",
                "verification_failed", ex);
        }
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
            _discovery.Dispose();
            _ownedIdentityResolver?.Dispose();
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
        public string? RevocationEndpoint { get; init; }
        public string PdsUrl { get; init; } = string.Empty;
        public DPoPProofGenerator DPoP { get; init; } = null!;
        public string RedirectUri { get; init; } = string.Empty;
        public string ClientId { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; }
    }
}
