using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using ATProtoNet.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Blazor.Authentication;

/// <summary>
/// Server-side service that manages AT Protocol OAuth flows with cookie authentication integration.
/// Lazily creates an <see cref="OAuthClient"/> on first use, auto-generating loopback client
/// metadata when no explicit <see cref="OAuthClientMetadata"/> is configured.
/// </summary>
/// <remarks>
/// <para>This service bridges AT Protocol OAuth with ASP.NET Core's cookie authentication.
/// After a successful OAuth flow, it creates claims and issues a standard authentication cookie.
/// This integrates seamlessly with Blazor's
/// <c>&lt;AuthorizeView&gt;</c>, <c>[Authorize]</c>, and <c>AuthorizeRouteView</c>.</para>
/// <para>Registered as a singleton by <see cref="AtProtoAuthenticationExtensions.AddAtProtoAuthentication"/>.</para>
/// </remarks>
public sealed class AtProtoOAuthService : IOAuthClientProvider, IDisposable
{
    private readonly AtProtoOAuthServerOptions _serverOptions;
    private readonly ILogger<AtProtoOAuthService> _logger;
    private readonly ILogger<OAuthClient> _oauthClientLogger;
    private readonly IIdentityResolver? _identityResolver;
    private readonly IOAuthStateStore? _stateStore;
    private volatile OAuthClient? _oauthClient;
    private HttpClient? _httpClient;
    private readonly object _lock = new();
    private bool _disposed;
    private readonly ConcurrentDictionary<string, RelayEntry> _relayCodes = new();

    /// <summary>
    /// Creates a new <see cref="AtProtoOAuthService"/>.
    /// </summary>
    /// <param name="serverOptions">The OAuth server options.</param>
    /// <param name="loggerFactory">Creates the service's loggers.</param>
    /// <param name="identityResolver">
    /// Resolves the handles and DIDs the login flow handles. Taken from dependency injection when
    /// registered (see <c>AddAtProtoIdentity</c>); when <see langword="null"/>, the OAuth client
    /// creates its own.
    /// </param>
    /// <param name="stateStore">
    /// Where pending logins wait for their callbacks. Taken from dependency injection when
    /// registered, such as a <see cref="DistributedCacheOAuthStateStore"/> shared by several
    /// instances; when <see langword="null"/>, the OAuth client keeps them in memory.
    /// </param>
    public AtProtoOAuthService(
        AtProtoOAuthServerOptions serverOptions,
        ILoggerFactory loggerFactory,
        IIdentityResolver? identityResolver = null,
        IOAuthStateStore? stateStore = null)
    {
        _serverOptions = serverOptions ?? throw new ArgumentNullException(nameof(serverOptions));
        _logger = loggerFactory.CreateLogger<AtProtoOAuthService>();
        _oauthClientLogger = loggerFactory.CreateLogger<OAuthClient>();
        _identityResolver = identityResolver;
        _stateStore = stateStore;
    }

    /// <summary>
    /// Returns the shared OAuth client used to refresh expired tokens on
    /// factory-built per-request clients. Constructs one lazily on first call
    /// when <see cref="AtProtoOAuthServerOptions.ClientMetadata"/> is set
    /// explicitly — that's the only case where the synthesized
    /// <c>client_id</c> is guaranteed to match the one registered at login,
    /// because the redirect URI is part of <c>ClientMetadata</c> and stable
    /// across calls.
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> when no user has logged in on this process AND
    /// either (a) no explicit <c>ClientMetadata</c> is configured (auto
    /// loopback), or (b) only <c>BaseUrl</c> is set. The loopback case cannot
    /// be lazily constructed without an <see cref="HttpContext"/>: the
    /// client_id encodes the callback URL, and the SDK rewrites
    /// <c>localhost</c> → <c>127.0.0.1</c> based on the live request, so a
    /// BaseUrl-derived URL would differ from the one a real login produced and
    /// every refresh would fail with <c>invalid_client</c>. Factory-built
    /// clients fall back to <c>null</c>, the OAuth refresh path then throws a
    /// loud <see cref="InvalidOperationException"/> — operators see the issue
    /// instead of silent logout on next token expiry. Production deployments
    /// should set <c>ClientMetadata</c> explicitly to enable this path.
    /// </remarks>
    public OAuthClient? TryGetClient()
    {
        var existing = _oauthClient;
        if (existing is not null) return existing;

        // Only construct eagerly when ClientMetadata is explicit. The redirect
        // URI registered with the AS is the first entry; this is the URL the
        // OAuthClient binds its client_id to. Loopback (no ClientMetadata) has
        // no stable callback to derive at this point — fall through to null.
        var registeredCallback = _serverOptions.ClientMetadata?.RedirectUris.FirstOrDefault();
        if (registeredCallback is null)
            return null;

        return GetOrCreateClient(registeredCallback);
    }

    private OAuthClient GetOrCreateClient(string callbackUrl)
    {
        if (_oauthClient is not null) return _oauthClient;

        lock (_lock)
        {
            if (_oauthClient is not null) return _oauthClient;

            var clientMetadata = _serverOptions.ClientMetadata
                ?? CreateLoopbackMetadata(callbackUrl, _serverOptions.Scopes, _serverOptions.ClientName);

            // A caller-supplied client is theirs: it is used as is (so it is theirs to secure),
            // its Timeout is left alone, and it is not disposed with this service. An owned one
            // runs under the identity fetch policy, public addresses only, with the configured
            // timeout.
            var httpClient = _serverOptions.HttpClient;
            if (httpClient is null)
            {
                httpClient = IdentityNetworkPolicy.CreateClient(_serverOptions.AllowPrivateNetworks);
                httpClient.Timeout = _serverOptions.HttpClientTimeout;
                _httpClient = httpClient;
            }

            var oauthOptions = new OAuthOptions
            {
                ClientMetadata = clientMetadata,
                Scope = _serverOptions.Scopes,
                HandleResolutionTimeout = _serverOptions.HandleResolutionTimeout,
                IdentityResolver = _identityResolver,
                AllowPrivateNetworks = _serverOptions.AllowPrivateNetworks,
                HttpClient = httpClient,
                StateStore = _stateStore,
            };

            _oauthClient = new OAuthClient(oauthOptions, _oauthClientLogger);

            _logger.LogInformation(
                "AT Proto OAuth client initialized with client_id: {ClientId}",
                clientMetadata.ClientId);

            return _oauthClient;
        }
    }

    /// <summary>
    /// Starts the OAuth login flow and returns the authorization URL to redirect the user to.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="handle">The user's AT Protocol handle (e.g., "alice.bsky.social"), DID, or PDS URL.</param>
    /// <param name="returnUrl">
    /// Optional local URL to return to after the login (a path such as <c>/admin</c>); anything
    /// else is ignored in favour of <see cref="AtProtoOAuthServerOptions.DefaultReturnUrl"/>. It is
    /// kept with the pending login on the server.
    /// </param>
    /// <param name="pdsUrl">Optional explicit PDS URL to skip automatic discovery.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization URL to redirect the user to.</returns>
    /// <remarks>
    /// Sets the browser's login binding cookie, which the callback requires. Pending logins are
    /// limited per remote address (an IPv6 address by its /64); behind a reverse proxy, restore
    /// the client's address with <c>UseForwardedHeaders</c>, or every user shares one limit.
    /// </remarks>
    public async Task<string> StartLoginAsync(
        HttpContext context,
        string handle,
        string? returnUrl = null,
        string? pdsUrl = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var callbackUrl = BuildCallbackUrl(context);
        var client = GetOrCreateClient(callbackUrl);

        var loginState = new OAuthLoginState(
            $"{context.Request.Scheme}://{context.Request.Host}",
            OAuthLoginBinding.IsLocalUrl(returnUrl) ? returnUrl : null,
            OAuthLoginBinding.Issue(context));

        var authorization = await client.StartAuthorizationAsync(
            handle,
            callbackUrl,
            new OAuthAuthorizationOptions
            {
                ServerUrl = pdsUrl,
                RequesterId = OAuthAuthorizationOptions.RequesterIdFor(context.Connection.RemoteIpAddress),
                AppState = loginState.Serialize(),
            },
            cancellationToken);

        _logger.LogInformation("OAuth login started for handle: {Handle}", handle);

        return authorization.AuthorizationUrl.AbsoluteUri;
    }

    /// <summary>
    /// Completes the OAuth callback by exchanging the authorization code for tokens,
    /// creating claims, and issuing an authentication cookie.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="code">The authorization code from the callback.</param>
    /// <param name="state">The state parameter from the callback.</param>
    /// <param name="issuer">The issuer parameter from the callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Where to redirect: the login's local return URL, or, when the callback arrived on another
    /// loopback origin than the login started on, that origin's relay URL.
    /// </returns>
    /// <exception cref="OAuthException">
    /// The login failed, or the browser does not present the binding cookie of the login it
    /// completes (<c>login_not_bound</c>); the tokens are then revoked and nothing is stored.
    /// </exception>
    public async Task<string> CompleteCallbackAsync(
        HttpContext context,
        string code,
        string state,
        string issuer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // With explicit client metadata the client can be built here too, so a login started
        // before a restart, or on another instance sharing the state store, still completes.
        var client = _oauthClient ?? TryGetClient()
            ?? throw new InvalidOperationException(
                "OAuth client not initialized. Ensure StartLoginAsync was called first.");

        var result = await client.CompleteAuthorizationWithAppStateAsync(code, state, issuer, cancellationToken);
        var session = result.Session;
        var loginState = OAuthLoginState.TryParse(result.AppState);
        var callbackOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
        var returnUrl = loginState?.ReturnUrl ?? _serverOptions.DefaultReturnUrl;

        // A callback on another origin than the login's (Aspire and Kestrel multi-bind: the
        // loopback callback on http://127.0.0.1, the browser on https://localhost) cannot see the
        // binding cookie, so the relay on the login's origin checks it. Only between loopback
        // origins: a relay elsewhere would redirect to whatever Host the login was started with.
        if (loginState is not null &&
            !callbackOrigin.Equals(loginState.Origin, StringComparison.OrdinalIgnoreCase) &&
            OAuthLoginBinding.IsLoopbackOrigin(loginState.Origin) &&
            OAuthLoginBinding.IsLoopbackOrigin(callbackOrigin))
        {
            var relayCode = AddRelayEntry(new RelayEntry(
                CreatePrincipal(session), CreateAuthenticationProperties(), returnUrl,
                DateTime.UtcNow.AddMinutes(2), session, loginState.BindingHash));

            _logger.LogInformation(
                "Cookie relay initiated: {CallbackOrigin} -> {LoginOrigin} for {Did}",
                callbackOrigin, loginState.Origin, session.Did);

            return $"{loginState.Origin}{_serverOptions.RoutePrefix}/relay?code={relayCode}";
        }

        if (loginState is null || !OAuthLoginBinding.Verify(context, loginState.BindingHash))
        {
            await RevokeQuietlyAsync(client, session);
            throw new OAuthException(
                "This sign-in was not started in this browser. Start it again from the sign-in page.",
                "login_not_bound");
        }

        OAuthLoginBinding.Clear(context);
        await StoreSessionAsync(context, session, cancellationToken);
        await context.SignInAsync(_serverOptions.CookieScheme, CreatePrincipal(session), CreateAuthenticationProperties());

        _logger.LogInformation(
            "OAuth login completed for DID: {Did}, Handle: {Handle}",
            session.Did, session.Handle);

        return returnUrl;
    }

    /// <summary>
    /// Signs out: revokes the user's stored OAuth session at its authorization server, removes
    /// it from the <see cref="IAtProtoSessionStore"/> (when one is registered), and clears the
    /// authentication cookie.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The URL to redirect to after logout.</returns>
    /// <remarks>
    /// The local sign-out always completes; a revocation the authorization server refuses or
    /// cannot be reached for is logged as a warning.
    /// </remarks>
    public async Task<string> LogoutAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var sessionStore = context.RequestServices.GetService<IAtProtoSessionStore>();
        var claim = context.User.FindFirst("did")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (sessionStore is not null && Did.TryParse(claim, out var did))
        {
            var session = await sessionStore.GetAsync(did, cancellationToken);
            await sessionStore.RemoveAsync(did, cancellationToken);
            _logger.LogInformation("Removed the stored OAuth session of {Did}", did);

            if (session is OAuthSession oauth && TryGetClient() is { } client)
            {
                try
                {
                    await client.RevokeAsync(oauth, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not revoke the OAuth session of {Did}", did);
                }
            }
        }

        await context.SignOutAsync(_serverOptions.CookieScheme);
        _logger.LogInformation("User logged out");
        return _serverOptions.PostLogoutRedirectUri;
    }

    private string BuildCallbackUrl(HttpContext context)
    {
        if (!string.IsNullOrWhiteSpace(_serverOptions.BaseUrl))
            return $"{_serverOptions.BaseUrl.TrimEnd('/')}{_serverOptions.RoutePrefix}/callback";

        // Auto-detect loopback HTTP URL from server bindings (e.g. Aspire, Kestrel multi-bind).
        // AT Proto loopback OAuth requires http:// with 127.0.0.1, but the incoming request may
        // arrive on HTTPS. Check the server's bound addresses for an HTTP URL.
        if (_serverOptions.ClientMetadata is null)
        {
            var httpUrl = TryGetLoopbackHttpUrl(context);
            if (httpUrl is not null)
                return $"{httpUrl.TrimEnd('/')}{_serverOptions.RoutePrefix}/callback";
        }

        return $"{context.Request.Scheme}://{context.Request.Host}{_serverOptions.RoutePrefix}/callback";
    }

    /// <summary>
    /// Attempts to find an HTTP loopback URL from the server's bound addresses.
    /// Used for AT Proto loopback OAuth when no explicit BaseUrl is configured.
    /// </summary>
    private static string? TryGetLoopbackHttpUrl(HttpContext context)
    {
        var server = context.RequestServices.GetService<IServer>();
        var addressFeature = server?.Features.Get<IServerAddressesFeature>();
        if (addressFeature is null)
            return null;

        foreach (var address in addressFeature.Addresses)
        {
            if (address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                // Normalize localhost → 127.0.0.1 for AT Proto loopback compatibility
                return address.Replace("://localhost", "://127.0.0.1", StringComparison.OrdinalIgnoreCase);
            }
        }

        return null;
    }

    private static List<Claim> CreateDefaultClaims(OAuthSession session)
    {
        // An unverified handle is 'handle.invalid' in the session. That sentinel goes into
        // ClaimTypes.Name (a DID there would pollute UI greetings, URL slugs and log filters
        // keyed on User.Identity.Name), while the `handle` claim carries the DID instead, so it
        // still tells users apart; `handle_verified` says which case applies.
        var verified = session.Handle.Value != "handle.invalid";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.Did.Value),
            new(ClaimTypes.Name, session.Handle.Value),
            new("did", session.Did.Value),
            new("handle", verified ? session.Handle.Value : session.Did.Value),
            new("handle_verified", verified ? "true" : "false"),
            new("pds_url", session.ServiceEndpoint.OriginalString),
            new("auth_method", "oauth"),
        };

        return claims;
    }

    private static OAuthClientMetadata CreateLoopbackMetadata(
        string callbackUrl, string scopes, string? clientName)
    {
        var encodedRedirectUri = Uri.EscapeDataString(callbackUrl);
        var encodedScope = Uri.EscapeDataString(scopes);
        var clientId = $"http://localhost?redirect_uri={encodedRedirectUri}&scope={encodedScope}";

        return new OAuthClientMetadata
        {
            ClientId = clientId,
            ClientName = clientName,
            RedirectUris = [callbackUrl],
            GrantTypes = ["authorization_code", "refresh_token"],
            ResponseTypes = ["code"],
            Scope = scopes,
            TokenEndpointAuthMethod = "none",
            ApplicationType = "web",
            DpopBoundAccessTokens = true,
        };
    }

    /// <summary>
    /// Redeems a one-time cookie relay code, issuing the authentication cookie on the
    /// correct domain. Used internally by the relay endpoint mapped by <c>MapAtProtoOAuth()</c>.
    /// </summary>
    /// <param name="context">The current HTTP context (on the user's browsing domain).</param>
    /// <param name="code">The one-time relay code from the query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The return URL to redirect to, or null if the code is invalid or expired, or the browser
    /// does not present the binding cookie of the login the code completes.
    /// </returns>
    public async Task<string?> TryRedeemRelayCodeAsync(
        HttpContext context, string? code, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(code))
            return null;

        CleanupExpiredRelayCodes();

        if (!_relayCodes.TryRemove(code, out var entry) || entry.Expiry < DateTime.UtcNow)
            return null;

        if (!OAuthLoginBinding.Verify(context, entry.BindingHash))
        {
            _logger.LogWarning("Cookie relay refused: the browser did not start the login it completes");
            if (entry.Session is not null && TryGetClient() is { } client)
                await RevokeQuietlyAsync(client, entry.Session);
            return null;
        }

        OAuthLoginBinding.Clear(context);
        if (entry.Session is not null)
            await StoreSessionAsync(context, entry.Session, cancellationToken);

        // Issue the cookie on this domain (the user's actual browsing domain)
        await context.SignInAsync(_serverOptions.CookieScheme, entry.Principal, entry.Properties);

        _logger.LogInformation(
            "Cookie relay completed: auth cookie issued on {Host}",
            context.Request.Host);

        return entry.ReturnUrl;
    }

    /// <summary>Whether <paramref name="url"/> is a relay URL this service issued and has not redeemed yet.</summary>
    internal bool IsIssuedRelayUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.AbsolutePath != $"{_serverOptions.RoutePrefix.TrimEnd('/')}/relay" ||
            !uri.Query.StartsWith("?code=", StringComparison.Ordinal))
        {
            return false;
        }

        return _relayCodes.ContainsKey(uri.Query["?code=".Length..]);
    }

    /// <summary>Keeps a relay entry under a new one-time code, and returns the code.</summary>
    internal string AddRelayEntry(RelayEntry entry)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _relayCodes[code] = entry;
        CleanupExpiredRelayCodes();
        return code;
    }

    private ClaimsPrincipal CreatePrincipal(OAuthSession session)
    {
        var claims = _serverOptions.ClaimsFactory is not null
            ? _serverOptions.ClaimsFactory(session).ToList()
            : CreateDefaultClaims(session);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "ATProto"));
    }

    private AuthenticationProperties CreateAuthenticationProperties() => new()
    {
        IsPersistent = _serverOptions.IsPersistent,
        ExpiresUtc = DateTimeOffset.UtcNow.Add(_serverOptions.CookieExpiration),
        AllowRefresh = true,
    };

    /// <summary>Stores the session server-side when an <see cref="IAtProtoSessionStore"/> is registered.</summary>
    private async Task StoreSessionAsync(HttpContext context, OAuthSession session, CancellationToken cancellationToken)
    {
        if (context.RequestServices?.GetService<IAtProtoSessionStore>() is { } sessionStore)
        {
            await sessionStore.SetAsync(session, cancellationToken);
            _logger.LogInformation("Stored the OAuth session of {Did}", session.Did);
        }
    }

    /// <summary>Revokes a session this service refuses to sign in with, best effort.</summary>
    private async Task RevokeQuietlyAsync(OAuthClient client, OAuthSession session)
    {
        try
        {
            await client.RevokeAsync(session, CancellationToken.None);
        }
        catch (Exception ex) when (ex is OAuthException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not revoke the refused OAuth session of {Did}", session.Did);
        }
    }

    private void CleanupExpiredRelayCodes()
    {
        var expired = _relayCodes
            .Where(kv => kv.Value.Expiry < DateTime.UtcNow)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in expired)
            _relayCodes.TryRemove(key, out _);
    }

    /// <summary>
    /// Stores the authentication result for a one-time cookie relay redirect,
    /// allowing the SDK to issue the cookie on the user's actual browsing domain.
    /// </summary>
    /// <param name="Principal">The user to sign in.</param>
    /// <param name="Properties">The authentication cookie's properties.</param>
    /// <param name="ReturnUrl">Where to send the browser afterwards; a local URL.</param>
    /// <param name="Expiry">When the relay code stops working.</param>
    /// <param name="Session">The session to store once the browser is confirmed, if any.</param>
    /// <param name="BindingHash">The hash of the binding cookie the redeeming browser must present.</param>
    internal sealed record RelayEntry(
        ClaimsPrincipal Principal, AuthenticationProperties Properties,
        string ReturnUrl, DateTime Expiry, OAuthSession? Session, string BindingHash);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _oauthClient?.Dispose();
            _httpClient?.Dispose();
        }
    }
}
