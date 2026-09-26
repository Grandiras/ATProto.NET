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

namespace ATProtoNet.Server.Authentication;

/// <summary>
/// The hosted AT Protocol OAuth login: starts and completes the OAuth flow for a browser and
/// signs it in with an ASP.NET Core authentication cookie.
/// </summary>
/// <remarks>
/// <para>After a successful OAuth flow, it creates claims and issues a standard authentication
/// cookie, which works with <c>[Authorize]</c>, Blazor's <c>&lt;AuthorizeView&gt;</c> and
/// <c>AuthorizeRouteView</c>. With an <see cref="IAtProtoSessionStore"/> registered, it stores
/// the session there for <c>IAtProtoClientFactory</c>, and revokes and removes it on sign-out.</para>
/// <para>Its <see cref="Client"/> is built from the options alone, without a request, so a
/// process that has just started can refresh the sessions it stored before. Registered as a
/// singleton by <see cref="AtProtoOAuthExtensions.AddAtProtoAuthentication"/>, which also
/// registers that client as the <see cref="OAuthClient"/>.</para>
/// </remarks>
public sealed class AtProtoOAuthService : IDisposable
{
    /// <summary>How long a relayed login waits to be redeemed on its own origin.</summary>
    internal static readonly TimeSpan RelayLifetime = TimeSpan.FromMinutes(2);

    /// <summary>The most relayed logins waiting at once; beyond it the oldest is revoked.</summary>
    internal const int MaxRelayEntries = 256;

    private readonly AtProtoOAuthServerOptions _serverOptions;
    private readonly ILogger<AtProtoOAuthService> _logger;
    private readonly ILogger<OAuthClient> _oauthClientLogger;
    private readonly IIdentityResolver? _identityResolver;
    private readonly IOAuthStateStore? _stateStore;
    private readonly IServer? _server;
    private readonly ISessionRefreshCoordinator? _refreshCoordinator;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, RelayCode> _relayCodes = new(StringComparer.Ordinal);
    private volatile OAuthClient? _oauthClient;
    private string? _loopbackCallbackUrl;
    private HttpClient? _httpClient;
    private volatile bool _disposed;

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
    /// <param name="server">
    /// The server, whose plain HTTP address the development loopback client's callback uses when
    /// neither <see cref="AtProtoOAuthServerOptions.BaseUrl"/> nor
    /// <see cref="AtProtoOAuthServerOptions.ClientMetadata"/> is set.
    /// </param>
    /// <param name="refreshCoordinator">
    /// The coordinator the client factory's clients refresh under (registered by
    /// <c>AddAtProtoServer()</c>). Storing a new session and signing out take the account's
    /// lock too, so neither interleaves with a refresh.
    /// </param>
    public AtProtoOAuthService(
        AtProtoOAuthServerOptions serverOptions,
        ILoggerFactory loggerFactory,
        IIdentityResolver? identityResolver = null,
        IOAuthStateStore? stateStore = null,
        IServer? server = null,
        ISessionRefreshCoordinator? refreshCoordinator = null)
    {
        _serverOptions = serverOptions ?? throw new ArgumentNullException(nameof(serverOptions));
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger<AtProtoOAuthService>();
        _oauthClientLogger = loggerFactory.CreateLogger<OAuthClient>();
        _identityResolver = identityResolver;
        _stateStore = stateStore;
        _server = server;
        _refreshCoordinator = refreshCoordinator;
    }

    /// <summary>The clock relayed logins expire by.</summary>
    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>How many relayed logins are waiting to be redeemed.</summary>
    internal int PendingRelayCount => _relayCodes.Count;

    /// <summary>
    /// The OAuth client every login, refresh and revocation goes through, built from the options
    /// on first use.
    /// </summary>
    /// <remarks>
    /// Its <c>client_id</c> is the configured <see cref="AtProtoOAuthServerOptions.ClientMetadata"/>'s,
    /// or for the development loopback client one derived from its callback URL:
    /// <see cref="AtProtoOAuthServerOptions.BaseUrl"/>, or else the server's plain HTTP address on
    /// <c>127.0.0.1</c>. Either way it is the same after a restart, so stored sessions refresh.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The loopback client's callback URL cannot be determined: set
    /// <see cref="AtProtoOAuthServerOptions.BaseUrl"/>, or bind the server to a plain HTTP address.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The client authentication is inconsistent, as <see cref="OAuthClient(OAuthOptions, ILogger)"/>
    /// checks it: for example <see cref="AtProtoOAuthServerOptions.ClientKeys"/> without
    /// <c>private_key_jwt</c> client metadata.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public OAuthClient Client
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _oauthClient ?? CreateClient();
        }
    }

    private OAuthClient CreateClient()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_oauthClient is { } existing)
                return existing;

            OAuthClientMetadata clientMetadata;
            if (_serverOptions.ClientMetadata is { } configured)
            {
                clientMetadata = configured;
            }
            else
            {
                if (_serverOptions.ClientKeys.Count > 0)
                {
                    throw new InvalidOperationException(
                        "ClientKeys are for a confidential client, which needs ClientMetadata: its client_id " +
                        "document must publish the keys, which a loopback client has nowhere to do.");
                }

                _loopbackCallbackUrl = LoopbackCallbackUrl();
                clientMetadata = CreateLoopbackMetadata(_loopbackCallbackUrl, _serverOptions.Scopes, _serverOptions.ClientName);
            }

            // A caller-supplied client is theirs: it is used as is (so it is theirs to secure),
            // its Timeout is left alone, and it is not disposed with this service. An owned one
            // runs under the identity fetch policy, public addresses only, with the configured
            // timeout.
            var httpClient = _serverOptions.HttpClient;
            HttpClient? owned = null;
            if (httpClient is null)
            {
                httpClient = owned = IdentityNetworkPolicy.CreateClient(_serverOptions.AllowPrivateNetworks);
                httpClient.Timeout = _serverOptions.HttpClientTimeout;
            }

            try
            {
                var oauthOptions = new OAuthOptions
                {
                    ClientMetadata = clientMetadata,
                    ClientKeys = [.. _serverOptions.ClientKeys],
                    Scope = _serverOptions.Scopes,
                    HandleResolutionTimeout = _serverOptions.HandleResolutionTimeout,
                    IdentityResolver = _identityResolver,
                    AllowPrivateNetworks = _serverOptions.AllowPrivateNetworks,
                    HttpClient = httpClient,
                    StateStore = _stateStore,
                };

                _oauthClient = new OAuthClient(oauthOptions, _oauthClientLogger);
            }
            catch
            {
                owned?.Dispose();
                throw;
            }

            _httpClient = owned;
            _logger.LogInformation("AT Proto OAuth client initialized with client_id: {ClientId}", clientMetadata.ClientId);
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
    /// <exception cref="OAuthException">Resolution, discovery or the pushed authorization request failed.</exception>
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
        ArgumentNullException.ThrowIfNull(context);

        var client = Client;
        var callbackUrl = CallbackUrl(context);

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
    /// Whom the login signed in and where to redirect: the login's local return URL, or, when the
    /// callback arrived on another loopback origin than the login started on, that origin's relay
    /// URL.
    /// </returns>
    /// <exception cref="OAuthException">
    /// The login failed, or the browser does not present the binding cookie of the login it
    /// completes (<c>login_not_bound</c>); the tokens are then revoked and nothing is stored.
    /// </exception>
    public async Task<AtProtoOAuthCallbackResult> CompleteCallbackAsync(
        HttpContext context,
        string code,
        string state,
        string issuer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);

        // The client is built from the options, so a login started before a restart, or on
        // another instance sharing the state store, completes.
        var client = Client;

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
                TimeProvider.GetUtcNow() + RelayLifetime, session, loginState.BindingHash));

            _logger.LogInformation(
                "Cookie relay initiated: {CallbackOrigin} -> {LoginOrigin} for {Did}",
                callbackOrigin, loginState.Origin, session.Did);

            return AtProtoOAuthCallbackResult.Relayed(
                session.Did, $"{loginState.Origin}{RelayPath}?code={relayCode}");
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

        return AtProtoOAuthCallbackResult.SignedIn(session.Did, returnUrl);
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
    /// The session is removed under the account's refresh lock, so a refresh running meanwhile
    /// finishes first, and one waiting finds the session gone instead of bringing it back. The
    /// local sign-out always completes; a revocation the authorization server refuses or cannot
    /// be reached for is logged as a warning.
    /// </remarks>
    public async Task<string> LogoutAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);

        var sessionStore = context.RequestServices?.GetService<IAtProtoSessionStore>();
        // Only the user this login signed in: not a service auth caller naming the same DID.
        if (sessionStore is not null && OAuthUser.DidOf(context.User) is { } did)
        {
            AtProtoSession? session;
            await using (await AcquireRefreshLeaseAsync(did, cancellationToken))
            {
                session = await sessionStore.GetAsync(did, cancellationToken);
                await sessionStore.RemoveAsync(did, cancellationToken);
            }

            _logger.LogInformation("Removed the stored OAuth session of {Did}", did);

            if (session is OAuthSession oauth)
            {
                // The client factory's cached copy of the session's DPoP key goes with it.
                if (context.RequestServices?.GetService<IAtProtoClientFactory>() is AtProtoClientFactory factory)
                    factory.ForgetKey(did, oauth.DPoPKey);

                try
                {
                    await Client.RevokeAsync(oauth, cancellationToken);
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

    /// <summary>
    /// Redeems a one-time cookie relay code, issuing the authentication cookie on the
    /// correct domain. Used by the relay endpoint mapped by <c>MapAtProtoOAuth()</c>.
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
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(code))
            return null;

        CleanupExpiredRelayCodes();

        if (!_relayCodes.TryRemove(code, out var relay))
            return null;

        relay.Timer?.Dispose();
        var entry = relay.Entry;

        if (entry.Expiry <= TimeProvider.GetUtcNow())
        {
            RevokeInBackground(entry.Session, "expired");
            return null;
        }

        if (!OAuthLoginBinding.Verify(context, entry.BindingHash))
        {
            _logger.LogWarning("Cookie relay refused: the browser did not start the login it completes");
            if (entry.Session is not null)
                await RevokeQuietlyAsync(Client, entry.Session);
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

    /// <summary>
    /// Keeps a relay entry under a new one-time code until its expiry, when a session it still
    /// holds is revoked, and returns the code.
    /// </summary>
    internal string AddRelayEntry(RelayEntry entry)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var relay = new RelayCode(entry);
        _relayCodes[code] = relay;

        var due = entry.Expiry - TimeProvider.GetUtcNow();
        relay.Timer = TimeProvider.CreateTimer(
            _ => ExpireRelayCode(code, relay), null, due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        CleanupExpiredRelayCodes();
        EvictExcessRelayCodes();
        return code;
    }

    /// <summary>
    /// Drops the relay entries whose time is up, revoking the sessions nobody came back for: a
    /// relayed login holds tokens the authorization server has issued, and they would otherwise
    /// stay live there.
    /// </summary>
    internal void CleanupExpiredRelayCodes()
    {
        var now = TimeProvider.GetUtcNow();
        foreach (var (code, relay) in _relayCodes)
        {
            if (relay.Entry.Expiry <= now)
                ExpireRelayCode(code, relay);
        }
    }

    private void ExpireRelayCode(string code, RelayCode relay)
    {
        // Whoever removes the entry owns it: a redemption and the timer never both act on it.
        if (!_relayCodes.TryRemove(new KeyValuePair<string, RelayCode>(code, relay)))
            return;

        relay.Timer?.Dispose();
        _logger.LogInformation("Cookie relay expired unredeemed; revoking its session");
        RevokeInBackground(relay.Entry.Session, "expired");
    }

    private void EvictExcessRelayCodes()
    {
        while (_relayCodes.Count > MaxRelayEntries)
        {
            var oldest = _relayCodes.MinBy(pair => pair.Value.Entry.Expiry);
            if (oldest.Value is null || !_relayCodes.TryRemove(oldest))
                continue;

            oldest.Value.Timer?.Dispose();
            _logger.LogWarning("Too many cookie relays waiting; revoking the oldest");
            RevokeInBackground(oldest.Value.Entry.Session, "evicted");
        }
    }

    /// <summary>The callback URL a login started from <paramref name="context"/> registers.</summary>
    private string CallbackUrl(HttpContext context)
    {
        if (!string.IsNullOrWhiteSpace(_serverOptions.BaseUrl))
            return $"{_serverOptions.BaseUrl.TrimEnd('/')}{RoutePrefix}/callback";

        if (_serverOptions.ClientMetadata is { } metadata)
        {
            // A registered redirect URI, never one made up from the request's Host header: the
            // one on the request's origin when there is one, as with several hosts, else the first.
            var origin = $"{context.Request.Scheme}://{context.Request.Host}";
            return metadata.RedirectUris.FirstOrDefault(uri =>
                       uri.StartsWith(origin + "/", StringComparison.OrdinalIgnoreCase))
                   ?? metadata.RedirectUris.FirstOrDefault()
                   ?? throw new InvalidOperationException("The client metadata registers no redirect URI.");
        }

        return _loopbackCallbackUrl!;
    }

    /// <summary>
    /// The development loopback client's callback: <see cref="AtProtoOAuthServerOptions.BaseUrl"/>,
    /// or the server's plain HTTP address on a loopback IP, which AT Protocol loopback clients
    /// require even when the browser uses HTTPS.
    /// </summary>
    private string LoopbackCallbackUrl()
    {
        if (!string.IsNullOrWhiteSpace(_serverOptions.BaseUrl))
            return $"{_serverOptions.BaseUrl.TrimEnd('/')}{RoutePrefix}/callback";

        var addresses = _server?.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        foreach (var address in addresses)
        {
            if (TryGetLoopbackHttpOrigin(address) is { } origin)
                return $"{origin}{RoutePrefix}/callback";
        }

        throw new InvalidOperationException(
            "The development loopback OAuth client takes its callback from the server's plain HTTP loopback " +
            "address, and the server reports none (it knows its addresses once it has started). Bind one (for " +
            "example http://127.0.0.1:5000), set BaseUrl, or configure ClientMetadata for a client_id you publish.");
    }

    /// <summary>
    /// The loopback origin a server address is reached at over plain HTTP: <c>http://127.0.0.1:port</c>
    /// for a loopback, <c>localhost</c> or any-address binding, <c>http://[::1]:port</c> for the
    /// IPv6 loopback, and <see langword="null"/> for anything else.
    /// </summary>
    internal static string? TryGetLoopbackHttpOrigin(string address)
    {
        const string scheme = "http://";
        if (!address.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return null;

        var authority = address[scheme.Length..].Split('/', 2)[0];
        var colon = authority.LastIndexOf(':');
        if (colon <= 0 || authority.EndsWith(']') ||
            !ushort.TryParse(authority[(colon + 1)..], out var port) || port == 0)
        {
            return null;
        }

        var host = authority[..colon];
        return host.ToLowerInvariant() switch
        {
            "localhost" or "127.0.0.1" or "0.0.0.0" or "[::]" or "*" or "+" => $"http://127.0.0.1:{port}",
            "[::1]" => $"http://[::1]:{port}",
            _ => null,
        };
    }

    private string RoutePrefix => _serverOptions.RoutePrefix.TrimEnd('/');

    private string RelayPath => $"{RoutePrefix}/relay";

    private static List<Claim> CreateDefaultClaims(OAuthSession session)
    {
        // An unverified handle is 'handle.invalid' in the session. That sentinel goes into
        // ClaimTypes.Name (a DID there would pollute UI greetings, URL slugs and log filters
        // keyed on User.Identity.Name), while the `handle` claim carries the DID instead, so it
        // still tells users apart; `handle_verified` says which case applies.
        var verified = session.Handle != Handle.Invalid;

        return
        [
            new(ClaimTypes.NameIdentifier, session.Did.Value),
            new(ClaimTypes.Name, session.Handle.Value),
            new(AtProtoClaimTypes.Did, session.Did.Value),
            new(AtProtoClaimTypes.Handle, verified ? session.Handle.Value : session.Did.Value),
            new(AtProtoClaimTypes.HandleVerified, verified ? "true" : "false"),
            new(AtProtoClaimTypes.PdsUrl, session.ServiceEndpoint.OriginalString),
            new(AtProtoClaimTypes.AuthMethod, OAuthUser.AuthMethod),
        ];
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

    private ClaimsPrincipal CreatePrincipal(OAuthSession session)
    {
        var claims = _serverOptions.ClaimsFactory is not null
            ? _serverOptions.ClaimsFactory(session).ToList()
            : CreateDefaultClaims(session);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, OAuthUser.IdentityType));
    }

    private AuthenticationProperties CreateAuthenticationProperties() => new()
    {
        IsPersistent = _serverOptions.IsPersistent,
        ExpiresUtc = DateTimeOffset.UtcNow.Add(_serverOptions.CookieExpiration),
        AllowRefresh = true,
    };

    /// <summary>
    /// Stores the session server-side when an <see cref="IAtProtoSessionStore"/> is registered,
    /// under the account's refresh lock, so a refresh of the account's previous session cannot
    /// overwrite it.
    /// </summary>
    private async Task StoreSessionAsync(HttpContext context, OAuthSession session, CancellationToken cancellationToken)
    {
        if (context.RequestServices?.GetService<IAtProtoSessionStore>() is { } sessionStore)
        {
            await using (await AcquireRefreshLeaseAsync(session.Did, cancellationToken))
                await sessionStore.SetAsync(session, cancellationToken);

            _logger.LogInformation("Stored the OAuth session of {Did}", session.Did);
        }
    }

    private async ValueTask<IAsyncDisposable> AcquireRefreshLeaseAsync(Did did, CancellationToken cancellationToken) =>
        _refreshCoordinator is null ? NoLease.Instance : await _refreshCoordinator.AcquireAsync(did, cancellationToken);

    /// <summary>Revokes a session this service refuses to sign in with, best effort.</summary>
    private async Task RevokeQuietlyAsync(OAuthClient client, OAuthSession session)
    {
        try
        {
            await client.RevokeAsync(session, CancellationToken.None);
        }
        catch (Exception ex) when (ex is OAuthException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "Could not revoke the refused OAuth session of {Did}", session.Did);
        }
    }

    /// <summary>Revokes the session of a relayed login nobody redeemed, without waiting.</summary>
    private void RevokeInBackground(OAuthSession? session, string reason)
    {
        if (session is null || _disposed || _oauthClient is not { } client)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await client.RevokeAsync(session, CancellationToken.None);
                _logger.LogDebug("Revoked the {Reason} relayed session of {Did}", reason, session.Did);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not revoke the {Reason} relayed session of {Did}", reason, session.Did);
            }
        });
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
        string ReturnUrl, DateTimeOffset Expiry, OAuthSession? Session, string BindingHash);

    /// <summary>A waiting relay entry and the timer that expires it.</summary>
    private sealed class RelayCode(RelayEntry entry)
    {
        public RelayEntry Entry { get; } = entry;

        public ITimer? Timer { get; set; }
    }

    /// <summary>The lease when no coordinator is registered: nothing to release.</summary>
    private sealed class NoLease : IAsyncDisposable
    {
        public static readonly NoLease Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Relayed logins still waiting are dropped without being revoked; they expire at the
    /// authorization server with their tokens.
    /// </remarks>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        foreach (var relay in _relayCodes.Values)
            relay.Timer?.Dispose();
        _relayCodes.Clear();

        _oauthClient?.Dispose();
        _httpClient?.Dispose();
    }
}
