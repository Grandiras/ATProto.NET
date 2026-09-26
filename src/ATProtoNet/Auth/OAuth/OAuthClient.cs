using System.Net.Http.Headers;
using System.Text.Json;
using ATProtoNet.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// Orchestrates the full AT Protocol OAuth authorization flow including
/// server discovery, PAR, DPoP, PKCE, token exchange, and identity verification.
/// </summary>
/// <remarks>
/// <para>By default this is a public client (<c>token_endpoint_auth_method</c> <c>none</c>). A
/// confidential client sets <see cref="OAuthClientMetadata.TokenEndpointAuthMethod"/> to
/// <c>private_key_jwt</c>, publishes its keys in the client metadata, and supplies them in
/// <see cref="OAuthOptions.ClientKeys"/>; every request to the authorization server then carries
/// an ES256 client assertion (RFC 7523), and the reference authorization server grants its
/// sessions a far longer lifetime.</para>
/// <para>Usage flow:</para>
/// <list type="number">
/// <item>Call <see cref="StartAuthorizationAsync"/> to get an authorization URL</item>
/// <item>Redirect the user to that URL</item>
/// <item>Handle the callback via <see cref="CompleteAuthorizationAsync"/></item>
/// <item>Install the returned <see cref="Auth.OAuthSession"/> with
/// <see cref="AtProtoClient.ApplySessionAsync"/>, passing this client so the session can be
/// refreshed</item>
/// </list>
/// <para>Every request to an authorization server (pushed authorization, token exchange, refresh,
/// revocation) takes one path: a DPoP proof carrying the server's latest nonce, shared across the
/// process by origin; one retry when the server asks for a fresh nonce; a capped response that is
/// always disposed; and any error body reported as <see cref="OAuthException"/>. The endpoints
/// come from metadata whoever controls an account's DID document chooses, so by default these
/// requests follow the identity fetch policy (see <see cref="OAuthOptions.HttpClient"/>).</para>
/// <para>The client is thread-safe and meant to be shared: one per application (one
/// <c>client_id</c>), not one per user.</para>
/// </remarks>
public sealed class OAuthClient : IDisposable
{
    /// <summary>How long a started authorization waits for its callback (10 minutes).</summary>
    public static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The most of a response body read from an authorization server.</summary>
    private const int MaxResponseBytes = 64 * 1024;

    /// <summary>The longest <c>state</c> looked up; the client's own are 43 characters.</summary>
    private const int MaxStateLength = 256;

    private const string ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private static readonly MediaTypeWithQualityHeaderValue JsonMediaType = new("application/json");

    private readonly OAuthOptions _options;
    private readonly OAuthClientKey[] _clientKeys;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly AuthorizationServerDiscovery _discovery;
    private readonly IdentityResolver? _ownedIdentityResolver;
    private readonly IOAuthStateStore _stateStore;
    private readonly ILogger _logger;

    private bool _disposed;

    /// <summary>
    /// Creates a new OAuth client with the specified options.
    /// </summary>
    /// <param name="options">The client's configuration.</param>
    /// <param name="logger">An optional logger.</param>
    /// <exception cref="ArgumentException">
    /// The client authentication is inconsistent: a <c>token_endpoint_auth_method</c> other than
    /// <c>none</c> or <c>private_key_jwt</c>; <see cref="OAuthOptions.ClientKeys"/> for a public
    /// client; or a <c>private_key_jwt</c> client without keys, without
    /// <c>token_endpoint_auth_signing_alg</c> <c>ES256</c>, without <c>jwks</c> or
    /// <c>jwks_uri</c>, or with a key its inline <c>jwks</c> does not publish.
    /// </exception>
    public OAuthClient(OAuthOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.RequestTimeout <= TimeSpan.Zero && options.RequestTimeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(options), "RequestTimeout must be positive, or Timeout.InfiniteTimeSpan.");

        _options = options;
        _clientKeys = ValidateClientAuthentication(options);
        _logger = logger ?? NullLogger.Instance;

        // A client the options supply is the caller's, used as is; one created here enforces the
        // fetch policy and goes with this client on Dispose.
        _ownsHttpClient = options.HttpClient is null;
        _httpClient = options.HttpClient ?? IdentityNetworkPolicy.CreateClient(options.AllowPrivateNetworks);

        // Likewise for the resolver.
        var identityResolver = options.IdentityResolver;
        if (identityResolver is null)
        {
            identityResolver = _ownedIdentityResolver = IdentityResolver.CreateDefault(
                new IdentityResolverOptions
                {
                    HandleResolutionTimeout = options.HandleResolutionTimeout,
                    AllowPrivateNetworks = options.AllowPrivateNetworks,
                },
                _logger);
        }

        _discovery = new AuthorizationServerDiscovery(_httpClient, _logger, identityResolver, options.AllowPrivateNetworks);
        _stateStore = options.StateStore ?? new InMemoryOAuthStateStore();
    }

    /// <summary>
    /// The authorization server discovery service.
    /// </summary>
    public AuthorizationServerDiscovery Discovery => _discovery;

    /// <summary>The client every request to a PDS or authorization server goes through.</summary>
    internal HttpClient HttpClient => _httpClient;

    /// <summary>The <c>client_id</c> this client identifies itself with.</summary>
    internal string ClientId => _options.ClientMetadata.ClientId;

    /// <summary>The DPoP nonces of the authorization servers: the process-wide cache unless a test supplies one.</summary>
    internal DPoPNonceCache NonceCache { get; init; } = DPoPNonceCache.Shared;

    /// <summary>The clock for expiry times and client assertions.</summary>
    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Begins the OAuth authorization flow. Resolves the user's identity, performs
    /// server discovery, makes a Pushed Authorization Request (PAR), stores the pending
    /// authorization, and returns the URL to redirect the user to for authentication.
    /// </summary>
    /// <param name="identifier">
    /// A handle (e.g., "alice.bsky.social"), DID, or server URL. A URL names a PDS, or an
    /// authorization server such as an entryway, and the account is learned at the callback.
    /// </param>
    /// <param name="redirectUri">The callback URL the user will be redirected to after authorization.</param>
    /// <param name="options">
    /// Where to sign in when the identifier should only be the login hint, and who is signing in.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization URL, the <c>state</c> the callback will carry, and its expiry.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="redirectUri"/> is not an absolute HTTPS URL (or HTTP on a loopback address),
    /// or the options' <see cref="OAuthAuthorizationOptions.AppState"/> is longer than
    /// <see cref="OAuthAuthorizationOptions.MaxAppStateLength"/>.
    /// </exception>
    /// <exception cref="OAuthException">Resolution, discovery or the pushed authorization request failed.</exception>
    public async Task<OAuthAuthorizationRequest> StartAuthorizationAsync(
        string identifier,
        string redirectUri,
        OAuthAuthorizationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        if (options?.AppState is { Length: > OAuthAuthorizationOptions.MaxAppStateLength })
        {
            throw new ArgumentException(
                $"AppState is longer than {OAuthAuthorizationOptions.MaxAppStateLength} characters.", nameof(options));
        }

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

        // Step 1: the authorization server, and the account when the identifier names one.
        AuthorizationServerMetadata metadata;
        ResolvedIdentity? identity = null;
        string? loginHint = null;

        if (!string.IsNullOrWhiteSpace(options?.ServerUrl))
        {
            metadata = await _discovery.ResolveFromServerUrlAsync(options.ServerUrl, cancellationToken);
            loginHint = identifier;
        }
        else if (IsUrl(identifier))
        {
            metadata = await _discovery.ResolveFromServerUrlAsync(identifier, cancellationToken);
        }
        else
        {
            identity = await _discovery.ResolveIdentityAsync(
                AuthorizationServerDiscovery.ParseIdentifier(identifier), cancellationToken);
            var pds = identity.PdsEndpoint
                ?? throw new OAuthException(
                    $"DID document for '{identity.Did}' does not contain an atproto PDS service.", "pds_not_found");
            metadata = await _discovery.ResolveAuthorizationServerAsync(pds.OriginalString, cancellationToken);
            loginHint = identifier;
        }

        var clientKey = SelectClientKey(metadata);

        // Step 2: PKCE, the state, and the DPoP key the session's tokens will be bound to.
        var codeVerifier = PkceGenerator.GenerateCodeVerifier();
        var codeChallenge = PkceGenerator.ComputeCodeChallenge(codeVerifier);
        var state = PkceGenerator.GenerateState();
        using var dpop = new DPoPProofGenerator();

        // Step 3: the pushed authorization request.
        var clientId = _options.ClientMetadata.ClientId;
        var par = await PostFormWithDpopAsync<PushedAuthorizationResponse>(
            new Uri(metadata.PushedAuthorizationRequestEndpoint, UriKind.Absolute),
            () =>
            {
                var form = new Dictionary<string, string>
                {
                    ["response_type"] = "code",
                    ["redirect_uri"] = redirectUri,
                    ["state"] = state,
                    ["scope"] = _options.Scope,
                    ["code_challenge"] = codeChallenge,
                    ["code_challenge_method"] = "S256",
                };
                if (loginHint is not null)
                    form["login_hint"] = loginHint;
                AddClientAuthentication(form, clientKey, metadata.Issuer);
                return form;
            },
            dpop,
            "Pushed authorization request",
            "par_failed",
            cancellationToken);

        if (string.IsNullOrEmpty(par.RequestUri))
            throw new OAuthException("The pushed authorization response carries no request_uri.", "par_failed");

        // Step 4: what the callback needs, until it comes.
        var now = TimeProvider.GetUtcNow();
        var pending = new OAuthPendingAuthorization
        {
            State = state,
            RequesterId = options?.RequesterId,
            Issuer = metadata.Issuer,
            TokenEndpoint = new Uri(metadata.TokenEndpoint, UriKind.Absolute),
            RevocationEndpoint = metadata.RevocationEndpoint is { } revocation ? new Uri(revocation, UriKind.Absolute) : null,
            RedirectUri = redirectUri,
            CodeVerifier = codeVerifier,
            DPoPKey = dpop.ExportPrivateKey(),
            ClientKeyId = clientKey?.KeyId,
            Did = identity?.Did,
            AppState = options?.AppState,
            CreatedAt = now,
            ExpiresAt = now + AuthorizationLifetime,
        };

        await _stateStore.SetAsync(pending, cancellationToken);

        _logger.LogDebug("OAuth authorization URL generated, state={State}", state);

        return new OAuthAuthorizationRequest(
            BuildAuthorizationUrl(metadata.AuthorizationEndpoint, par.RequestUri, clientId), state, pending.ExpiresAt);
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
    /// <para>Before the session is returned, the account the tokens name is resolved from a
    /// freshly fetched DID document, and its authorization server, read from freshly fetched
    /// metadata, must be the issuer, as the reference client checks at every code exchange. An
    /// authorization started from a handle or DID must also have produced tokens for that
    /// account. Tokens that fail these checks are revoked, best effort, before the exception.</para>
    /// <para>Handle verification is best-effort: an unreachable, slow, or silent handle
    /// authority yields a session whose <see cref="Auth.AtProtoSession.Handle"/> is
    /// <c>handle.invalid</c>, never an exception.</para>
    /// <para>The <c>state</c> ties the callback to a pending authorization, but not to the browser
    /// that started it: a web front end must bind the two itself (the Blazor integration uses a
    /// cookie), or a victim could be signed in as the account of whoever sent them a callback URL.</para>
    /// </remarks>
    /// <exception cref="OAuthException">
    /// The state is unknown (<c>invalid_state</c>) or expired (<c>state_expired</c>), the issuer
    /// differs (<c>issuer_mismatch</c>), the token exchange failed, or the tokens are not for the
    /// expected account on this authorization server.
    /// </exception>
    public async Task<Auth.OAuthSession> CompleteAuthorizationAsync(
        string code,
        string state,
        string issuer,
        CancellationToken cancellationToken = default) =>
        (await CompleteAuthorizationWithAppStateAsync(code, state, issuer, cancellationToken)).Session;

    /// <summary>
    /// <see cref="CompleteAuthorizationAsync"/>, also returning the
    /// <see cref="OAuthAuthorizationOptions.AppState"/> the authorization was started with.
    /// </summary>
    /// <param name="code">The authorization code from the callback.</param>
    /// <param name="state">The state parameter from the callback.</param>
    /// <param name="issuer">The issuer (iss) parameter from the callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session and the application state.</returns>
    /// <exception cref="OAuthException">As <see cref="CompleteAuthorizationAsync"/>.</exception>
    public async Task<OAuthAuthorizationResult> CompleteAuthorizationWithAppStateAsync(
        string code,
        string state,
        string issuer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogDebug("Completing OAuth authorization, state={State}", state);

        // Step 1: the pending authorization, single use. A state no store could hold is not looked up.
        var pending = state is { Length: > 0 and <= MaxStateLength }
            ? await _stateStore.TakeAsync(state, cancellationToken)
            : null;
        if (pending is null)
            throw new OAuthException("Unknown or expired OAuth state parameter.", "invalid_state");

        // Step 2: the callback comes from the server the authorization was pushed to (RFC 9207).
        if (!string.Equals(pending.Issuer, issuer, StringComparison.Ordinal))
            throw new OAuthException(
                $"Issuer mismatch. Expected '{pending.Issuer}', got '{issuer}'.",
                "issuer_mismatch");

        if (TimeProvider.GetUtcNow() >= pending.ExpiresAt)
            throw new OAuthException("OAuth authorization state has expired.", "state_expired");

        // Step 3: exchange the code for tokens, authenticated as the pushed request was.
        var clientKey = pending.ClientKeyId is { } keyId ? FindClientKey(keyId) : null;
        using var dpop = new DPoPProofGenerator(pending.DPoPKey.ToArray());

        var tokens = await PostFormWithDpopAsync<OAuthTokenResponse>(
            pending.TokenEndpoint,
            () =>
            {
                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = pending.RedirectUri,
                    ["code_verifier"] = pending.CodeVerifier,
                };
                AddClientAuthentication(form, clientKey, pending.Issuer);
                return form;
            },
            dpop,
            "Token exchange",
            "token_error",
            cancellationToken);

        // Step 4: the account. The authoritative DID is the token response's `sub`, and the
        // issuer must be its authorization server now, not as some cache remembers it. A handle
        // counts only when verified in both directions: the DID document claims it AND the
        // handle's own authorities map it back to this DID, or a PDS could announce any handle.
        Did did;
        ResolvedIdentity identity;
        try
        {
            did = ValidateTokenResponse(tokens, pending.Did, "Token response");
            identity = await VerifyIssuerAsync(did, pending.Issuer, cancellationToken);
        }
        catch (OAuthException)
        {
            await RevokeUnusableTokensAsync(tokens, pending.RevocationEndpoint, clientKey, pending.Issuer, dpop);
            throw;
        }

        var handle = identity.HandleVerified && identity.Handle is { } verified ? verified : Handle.Invalid;
        if (handle == Handle.Invalid && identity.Document.GetHandle() is { } claimed)
            _logger.LogWarning("Handle '{Handle}' of {Did} does not verify; treating as unverified.", claimed, did);

        _logger.LogInformation("OAuth flow completed for {Did} (Handle={Handle})", did, handle);

        var session = new Auth.OAuthSession
        {
            Did = did,
            Handle = handle,
            ServiceEndpoint = identity.PdsEndpoint!,
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresAt = ExpiresAt(tokens),
            Scope = tokens.Scope,
            DPoPKey = pending.DPoPKey,
            Issuer = pending.Issuer,
            TokenEndpoint = pending.TokenEndpoint,
            RevocationEndpoint = pending.RevocationEndpoint,
            ClientKeyId = pending.ClientKeyId,
        };

        return new OAuthAuthorizationResult(session, pending.AppState);
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
    /// used already, in which case the user has to authorize again. Also thrown when the
    /// account's authorization server is no longer the session's issuer
    /// (<c>auth_server_mismatch</c>), in which case nothing was sent, and when the response is not
    /// for the session's account (<c>did_mismatch</c>) or lacks the <c>atproto</c> scope
    /// (<c>invalid_scope</c>).
    /// </exception>
    /// <remarks>
    /// <para>Before the refresh token is spent, the account's DID is resolved again and its
    /// authorization server must still be the session's issuer; the session moves to the PDS the
    /// DID document names now.</para>
    /// <para><see cref="AtProtoClient"/> refreshes the sessions it holds by itself; call this only to
    /// manage a session outside one.</para>
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

        var clientKey = ClientKeyOf(session);

        _logger.LogDebug("Refreshing OAuth tokens for {Did}", session.Did);

        // The account may have moved to another authorization server since it signed in; the
        // refresh token goes only to the one authoritative for it now, confirmed from freshly
        // fetched documents, as in the reference client.
        var identity = await VerifyIssuerAsync(session.Did, session.Issuer, cancellationToken);

        var tokens = await PostFormWithDpopAsync<OAuthTokenResponse>(
            session.TokenEndpoint,
            () =>
            {
                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = session.RefreshToken,
                };
                AddClientAuthentication(form, clientKey, session.Issuer);
                return form;
            },
            dpop,
            "Token refresh",
            "token_error",
            cancellationToken);

        // Tokens for another account, or without the atproto scope, must never replace the
        // session, even though the server has already rotated the refresh token; they are revoked.
        try
        {
            ValidateTokenResponse(tokens, session.Did, "The token refresh response");
        }
        catch (OAuthException)
        {
            await RevokeUnusableTokensAsync(tokens, session.RevocationEndpoint, clientKey, session.Issuer, dpop);
            throw;
        }

        return session with
        {
            AccessToken = tokens.AccessToken,

            // A refresh response may omit refresh_token to mean "keep using the current one".
            RefreshToken = tokens.RefreshToken ?? session.RefreshToken,
            ExpiresAt = ExpiresAt(tokens),
            Scope = tokens.Scope,
            ServiceEndpoint = identity.PdsEndpoint ?? session.ServiceEndpoint,
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

        var clientKey = ClientKeyOf(session);

        var endpoint = session.RevocationEndpoint;
        if (endpoint is null)
        {
            var metadata = await _discovery.GetAuthorizationServerMetadataAsync(session.Issuer, bypassCache: false, cancellationToken);
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

        await PostFormWithDpopAsync(
            endpoint,
            () =>
            {
                var form = new Dictionary<string, string>
                {
                    ["token"] = token,
                    ["token_type_hint"] = hint,
                };
                AddClientAuthentication(form, clientKey, session.Issuer);
                return form;
            },
            dpop,
            "Token revocation",
            cancellationToken);

        _logger.LogDebug("Revoked the OAuth session of {Did}", session.Did);
    }

    /// <summary>
    /// Revokes tokens the client has refused, best effort, as the reference client does: tokens
    /// for another account or from the wrong server should not outlive the refusal. The refresh
    /// token goes, which ends the grant; failing that, the access token. Nothing is thrown, and
    /// without a known revocation endpoint nothing is sent.
    /// </summary>
    private async Task RevokeUnusableTokensAsync(
        OAuthTokenResponse tokens, Uri? endpoint, OAuthClientKey? clientKey, string issuer, DPoPProofGenerator dpop)
    {
        var (token, hint) = !string.IsNullOrEmpty(tokens.RefreshToken)
            ? (tokens.RefreshToken, "refresh_token")
            : (tokens.AccessToken, "access_token");

        if (endpoint is null || string.IsNullOrEmpty(token))
            return;

        try
        {
            await PostFormWithDpopAsync(
                endpoint,
                () =>
                {
                    var form = new Dictionary<string, string> { ["token"] = token, ["token_type_hint"] = hint };
                    AddClientAuthentication(form, clientKey, issuer);
                    return form;
                },
                dpop,
                "Revoking refused tokens",
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Best effort: the refusal that led here is what the caller must see.
            _logger.LogDebug(ex, "Could not revoke the tokens {Issuer} issued and the client refused", issuer);
        }
    }

    private DateTimeOffset? ExpiresAt(OAuthTokenResponse tokens) =>
        tokens.ExpiresIn is { } seconds && seconds > 0 ? TimeProvider.GetUtcNow().AddSeconds(seconds) : null;

    /// <summary>
    /// Checks a token response from the code exchange or a refresh: a DPoP-bound access token,
    /// for <paramref name="expected"/> when given, with the <c>atproto</c> scope.
    /// </summary>
    /// <returns>The account the tokens are for.</returns>
    private static Did ValidateTokenResponse(OAuthTokenResponse tokens, Did? expected, string what)
    {
        // A success status with no usable token is as much a failure as an error body: storing
        // it would replace a working session with one that cannot authenticate anything.
        if (string.IsNullOrEmpty(tokens.AccessToken))
            throw new OAuthException($"{what} carries no access token.", "token_error");

        if (!string.Equals(tokens.TokenType, "DPoP", StringComparison.OrdinalIgnoreCase))
        {
            throw new OAuthException(
                $"{what} has token type '{tokens.TokenType}'; AT Protocol tokens are DPoP-bound.",
                "token_error");
        }

        if (tokens.Sub is null)
            throw new OAuthException($"{what} is missing the 'sub' field.", "missing_sub");

        if (!Did.TryParse(tokens.Sub, out var did))
            throw new OAuthException($"{what} 'sub' is not a valid DID: '{tokens.Sub}'.", "invalid_sub");

        if (expected is not null && !did.Equals(expected))
        {
            throw new OAuthException(
                $"{what} is for '{did}', not the expected '{expected}'.",
                "did_mismatch");
        }

        // An exact token match, not a substring.
        if (tokens.Scope is null ||
            !tokens.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(AtProtoScopes.AtProto, StringComparer.Ordinal))
        {
            throw new OAuthException($"{what} does not include the 'atproto' scope.", "invalid_scope");
        }

        return did;
    }

    /// <summary>
    /// POSTs a form to an authorization server endpoint with a DPoP proof and reads the answer:
    /// the one path every authorization server request takes.
    /// </summary>
    /// <param name="endpoint">The endpoint, from validated metadata or a stored session.</param>
    /// <param name="buildForm">
    /// Builds the form; called for each attempt, so a client assertion is never sent twice.
    /// </param>
    /// <param name="dpop">The key the proof is signed with.</param>
    /// <param name="operation">What the request is, for messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The body of the successful response, or <see langword="null"/> when it is over the cap.</returns>
    /// <remarks>
    /// The proof carries the endpoint origin's latest nonce, and a <c>use_dpop_nonce</c> answer
    /// that brings a fresh one is retried once with it (RFC 9449 section 8); every nonce a
    /// response brings is remembered for the next request. Responses are read up to 64 KiB and
    /// disposed, and the exchange as a whole, body included, is bounded by
    /// <see cref="OAuthOptions.RequestTimeout"/>.
    /// </remarks>
    /// <exception cref="OAuthException">
    /// The server answered with an error, carried as <see cref="OAuthException.Error"/>; the
    /// endpoint is not one the client may send to (<c>invalid_server_url</c>); the exchange ran
    /// out of time (<c>request_timeout</c>); or the connection or the body failed
    /// (<c>request_failed</c>).
    /// </exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    private async Task<ReadOnlyMemory<byte>?> PostFormWithDpopAsync(
        Uri endpoint,
        Func<Dictionary<string, string>> buildForm,
        DPoPProofGenerator dpop,
        string operation,
        CancellationToken cancellationToken)
    {
        // The endpoint came from metadata whoever controls the account's DID document chose; the
        // fetch policy's handler checks the address, and this the URL.
        if (!AuthorizationServerDiscovery.IsEndpoint(endpoint, _options.AllowPrivateNetworks))
        {
            throw new OAuthException(
                $"{operation} refused: '{endpoint}' is not an absolute HTTPS URL without query or fragment.",
                "invalid_server_url");
        }

        // One budget for the whole exchange, retry and body included: HttpClient.Timeout stops
        // applying once the headers are in, so a server stalling mid-body is bounded only by this.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.RequestTimeout);

        try
        {
            return await SendFormWithDpopAsync(endpoint, buildForm, dpop, operation, budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OAuthException(
                $"{operation} did not complete within {_options.RequestTimeout.TotalSeconds:0.#} s.", "request_timeout", ex);
        }
        catch (HttpRequestException ex) when (IdentityNetworkPolicy.IsBlocked(ex))
        {
            throw new OAuthException(
                $"{operation} refused: {endpoint.Host} is not a public address.", "invalid_server_url", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        {
            // IOException covers a body broken off mid-read, InvalidDataException one that does
            // not decode under its Content-Encoding.
            throw new OAuthException($"{operation} could not reach {endpoint.Host}: {ex.Message}", "request_failed", ex);
        }
    }

    private async Task<ReadOnlyMemory<byte>?> SendFormWithDpopAsync(
        Uri endpoint,
        Func<Dictionary<string, string>> buildForm,
        DPoPProofGenerator dpop,
        string operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent(buildForm()),
            };
            request.Headers.Accept.Add(JsonMediaType);
            request.Headers.TryAddWithoutValidation(
                "DPoP", dpop.GenerateProof("POST", endpoint.AbsoluteUri, NonceCache.Get(endpoint)));

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var freshNonce = NonceCache.Observe(endpoint, response);

            ReadOnlyMemory<byte>? body;
            try
            {
                body = await response.Content.ReadBoundedAsync(MaxResponseBytes, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                // Brotli reports a body that does not decode this way.
                throw new InvalidDataException(ex.Message, ex);
            }

            if (response.IsSuccessStatusCode)
                return body;

            var error = ParseError(body);
            if (attempt == 1 && freshNonce is not null && error?.Error == "use_dpop_nonce")
            {
                _logger.LogDebug("{Operation} asked for a DPoP nonce; retrying with it", operation);
                continue;
            }

            var code = string.IsNullOrEmpty(error?.Error) ? "server_error" : error.Error;
            _logger.LogDebug("{Operation} failed with {Status} {Error}", operation, (int)response.StatusCode, code);
            throw new OAuthException(
                $"{operation} failed with {(int)response.StatusCode} {code}" +
                (string.IsNullOrEmpty(error?.ErrorDescription) ? "." : $": {error.ErrorDescription}"),
                code);
        }
    }

    /// <summary>
    /// <see cref="PostFormWithDpopAsync(Uri, Func{Dictionary{string, string}}, DPoPProofGenerator, string, CancellationToken)"/>,
    /// reading the answer as <typeparamref name="T"/>; an answer that is not one is
    /// <paramref name="invalidResponseError"/>.
    /// </summary>
    private async Task<T> PostFormWithDpopAsync<T>(
        Uri endpoint,
        Func<Dictionary<string, string>> buildForm,
        DPoPProofGenerator dpop,
        string operation,
        string invalidResponseError,
        CancellationToken cancellationToken)
        where T : class
    {
        var body = await PostFormWithDpopAsync(endpoint, buildForm, dpop, operation, cancellationToken)
            ?? throw new OAuthException($"The {operation} response is larger than {MaxResponseBytes} bytes.", invalidResponseError);

        try
        {
            return JsonSerializer.Deserialize<T>(body.Span)
                ?? throw new OAuthException($"The {operation} response is empty.", invalidResponseError);
        }
        catch (JsonException ex)
        {
            throw new OAuthException($"The {operation} response is not valid JSON.", invalidResponseError, ex);
        }
    }

    private static OAuthErrorResponse? ParseError(ReadOnlyMemory<byte>? body)
    {
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
    /// Adds the client's identification to a form: its <c>client_id</c>, and for a confidential
    /// client a fresh assertion for <paramref name="audience"/> signed with <paramref name="key"/>.
    /// </summary>
    private void AddClientAuthentication(Dictionary<string, string> form, OAuthClientKey? key, string audience)
    {
        var clientId = _options.ClientMetadata.ClientId;
        form["client_id"] = clientId;

        if (key is not null)
        {
            form["client_assertion_type"] = ClientAssertionType;
            form["client_assertion"] = key.CreateAssertion(clientId, audience, TimeProvider.GetUtcNow());
        }
    }

    /// <summary>
    /// The key a new authorization is authenticated with: the first client key, for an
    /// authorization server that accepts <c>private_key_jwt</c> with ES256; none for a public client.
    /// </summary>
    private OAuthClientKey? SelectClientKey(AuthorizationServerMetadata metadata)
    {
        if (_clientKeys.Length == 0)
            return null;

        if (!metadata.TokenEndpointAuthMethodsSupported.Contains("private_key_jwt") ||
            !metadata.TokenEndpointAuthSigningAlgValuesSupported.Contains(OAuthClientKey.Algorithm))
        {
            throw new OAuthException(
                $"Authorization server '{metadata.Issuer}' does not accept private_key_jwt client authentication with ES256.",
                "unsupported_client_auth");
        }

        return _clientKeys[0];
    }

    /// <summary>
    /// The key a session's requests are authenticated with: the one its grant was issued to, or
    /// none for a session of a public client.
    /// </summary>
    private OAuthClientKey? ClientKeyOf(Auth.OAuthSession session) =>
        session.ClientKeyId is { } keyId ? FindClientKey(keyId) : null;

    private OAuthClientKey FindClientKey(string keyId)
    {
        foreach (var key in _clientKeys)
        {
            if (string.Equals(key.KeyId, keyId, StringComparison.Ordinal))
                return key;
        }

        // The authorization server binds a grant to the key that authenticated it, so no other
        // key can stand in.
        throw new OAuthException(
            $"The session was authorized with client key '{keyId}', which this client no longer has.",
            "client_key_unavailable");
    }

    private static OAuthClientKey[] ValidateClientAuthentication(OAuthOptions options)
    {
        var metadata = options.ClientMetadata
            ?? throw new ArgumentException("The options carry no client metadata.", nameof(options));
        var keys = options.ClientKeys?.ToArray() ?? [];

        if (keys.Any(key => key is null))
            throw new ArgumentException("ClientKeys contains a null key.", nameof(options));

        switch (metadata.TokenEndpointAuthMethod)
        {
            case "none":
                if (keys.Length > 0)
                {
                    throw new ArgumentException(
                        "ClientKeys are only used by a confidential client: set the client metadata's " +
                        "TokenEndpointAuthMethod to 'private_key_jwt' and publish the keys.",
                        nameof(options));
                }

                return keys;

            case "private_key_jwt":
                if (keys.Length == 0)
                    throw new ArgumentException("A private_key_jwt client needs at least one key in ClientKeys.", nameof(options));

                if (metadata.TokenEndpointAuthSigningAlg != OAuthClientKey.Algorithm)
                {
                    throw new ArgumentException(
                        "A private_key_jwt client must declare TokenEndpointAuthSigningAlg 'ES256'.", nameof(options));
                }

                if (metadata.Jwks is null && string.IsNullOrEmpty(metadata.JwksUri))
                {
                    throw new ArgumentException(
                        "A private_key_jwt client must publish its keys in the client metadata, as Jwks or at JwksUri.",
                        nameof(options));
                }

                if (keys.Select(key => key.KeyId).Distinct(StringComparer.Ordinal).Count() != keys.Length)
                    throw new ArgumentException("ClientKeys contains two keys with the same key id.", nameof(options));

                if (metadata.Jwks is { } published)
                {
                    foreach (var key in keys)
                    {
                        if (!published.Keys.Any(jwk => jwk.Kid == key.KeyId && key.Matches(jwk)))
                        {
                            throw new ArgumentException(
                                $"Client key '{key.KeyId}' is not published in the client metadata's Jwks.", nameof(options));
                        }
                    }
                }

                return keys;

            default:
                throw new ArgumentException(
                    $"Unsupported token_endpoint_auth_method '{metadata.TokenEndpointAuthMethod}': " +
                    "AT Protocol clients use 'none' or 'private_key_jwt'.",
                    nameof(options));
        }
    }

    /// <summary>
    /// Resolves the account and checks that its authorization server is
    /// <paramref name="expectedIssuer"/>: the DID → PDS → authorization server chain must lead
    /// back to the server that issued the tokens.
    /// </summary>
    /// <returns>The account's identity, with its PDS.</returns>
    private async Task<ResolvedIdentity> VerifyIssuerAsync(
        Did did, string expectedIssuer, CancellationToken cancellationToken)
    {
        try
        {
            // No cache on this path: a copy from before the account moved would confirm the
            // server it left, which may be the one asking.
            var identity = await _discovery.ResolveIdentityUncachedAsync(did, cancellationToken);
            var pdsUrl = identity.PdsEndpoint
                ?? throw new OAuthException(
                    $"DID document for '{did}' does not contain an atproto PDS service.", "pds_not_found");
            var metadata = await _discovery.ResolveAuthorizationServerAsync(
                pdsUrl.OriginalString, bypassCache: true, cancellationToken);

            if (!string.Equals(metadata.Issuer, expectedIssuer, StringComparison.Ordinal))
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

    private static Uri BuildAuthorizationUrl(string authorizationEndpoint, string requestUri, string clientId) =>
        new($"{authorizationEndpoint}?client_id={Uri.EscapeDataString(clientId)}&request_uri={Uri.EscapeDataString(requestUri)}",
            UriKind.Absolute);

    private static bool IsUrl(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               (value.Contains('.') && !value.Contains('@') && !value.Contains(':') &&
                !value.StartsWith("did:", StringComparison.OrdinalIgnoreCase) &&
                // A bare domain with '/' is a URL
                value.Contains('/'));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _discovery.Dispose();
            _ownedIdentityResolver?.Dispose();
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }
    }
}
