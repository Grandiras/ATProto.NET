using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using ATProtoNet.Auth.OAuth;
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
public sealed class AtProtoOAuthService : IDisposable
{
    private readonly AtProtoOAuthServerOptions _serverOptions;
    private readonly ILogger<AtProtoOAuthService> _logger;
    private readonly ILogger<OAuthClient> _oauthClientLogger;
    private OAuthClient? _oauthClient;
    private HttpClient? _httpClient;
    private readonly object _lock = new();
    private bool _disposed;
    private readonly ConcurrentDictionary<string, LoginContext> _loginContexts = new();
    private readonly ConcurrentDictionary<string, RelayEntry> _relayCodes = new();

    /// <summary>
    /// Creates a new <see cref="AtProtoOAuthService"/>.
    /// </summary>
    public AtProtoOAuthService(AtProtoOAuthServerOptions serverOptions, ILoggerFactory loggerFactory)
    {
        _serverOptions = serverOptions ?? throw new ArgumentNullException(nameof(serverOptions));
        _logger = loggerFactory.CreateLogger<AtProtoOAuthService>();
        _oauthClientLogger = loggerFactory.CreateLogger<OAuthClient>();
    }

    private OAuthClient GetOrCreateClient(string callbackUrl)
    {
        if (_oauthClient is not null) return _oauthClient;

        lock (_lock)
        {
            if (_oauthClient is not null) return _oauthClient;

            var clientMetadata = _serverOptions.ClientMetadata
                ?? CreateLoopbackMetadata(callbackUrl, _serverOptions.Scopes, _serverOptions.ClientName);

            var oauthOptions = new OAuthOptions
            {
                ClientMetadata = clientMetadata,
                Scope = _serverOptions.Scopes,
            };

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(
                $"ATProtoNet/{typeof(OAuthClient).Assembly.GetName().Version}");

            _httpClient = httpClient;
            _oauthClient = new OAuthClient(oauthOptions, httpClient, _oauthClientLogger);

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
    /// <param name="returnUrl">Optional URL to redirect to after successful login. Stored in a temporary cookie.</param>
    /// <param name="pdsUrl">Optional explicit PDS URL to skip automatic discovery.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization URL to redirect the user to.</returns>
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

        // Store returnUrl in a temporary cookie so we can retrieve it after the OAuth redirect
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            context.Response.Cookies.Append("atproto_return_url", returnUrl, new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(10),
                Path = _serverOptions.RoutePrefix,
            });
        }

        var (authorizationUrl, state) = await client.StartAuthorizationAsync(
            handle, callbackUrl, pdsUrl, cancellationToken);

        // Store login context server-side for cross-origin cookie relay.
        // When the OAuth callback arrives on a different origin (e.g., http://127.0.0.1)
        // than the user's browser (e.g., https://localhost), the SDK automatically
        // relays the auth cookie back to the correct origin.
        var loginOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
        _loginContexts[state] = new LoginContext(loginOrigin, returnUrl, DateTime.UtcNow.AddMinutes(10));
        CleanupExpiredLoginContexts();

        _logger.LogInformation("OAuth login started for handle: {Handle}", handle);

        return authorizationUrl;
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
    /// <returns>The return URL to redirect to after successful authentication.</returns>
    public async Task<string> CompleteCallbackAsync(
        HttpContext context,
        string code,
        string state,
        string issuer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var client = _oauthClient
            ?? throw new InvalidOperationException(
                "OAuth client not initialized. Ensure StartLoginAsync was called first.");

        // Retrieve login context (stored by StartLoginAsync) for cross-origin cookie relay
        var loginContext = _loginContexts.TryRemove(state, out var lc) ? lc : null;

        // Exchange authorization code for tokens
        var result = await client.CompleteAuthorizationAsync(code, state, issuer, cancellationToken);

        try
        {
            // Create claims
            var claims = _serverOptions.ClaimsFactory is not null
                ? _serverOptions.ClaimsFactory(result).ToList()
                : CreateDefaultClaims(result);

            var identity = new ClaimsIdentity(claims, "ATProto");
            var principal = new ClaimsPrincipal(identity);

            var properties = new AuthenticationProperties
            {
                IsPersistent = _serverOptions.IsPersistent,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(_serverOptions.CookieExpiration),
                AllowRefresh = true,
            };

            // Store tokens server-side if IAtProtoTokenStore is registered (regardless of relay)
            var tokenStore = context.RequestServices.GetService<IAtProtoTokenStore>();
            if (tokenStore is not null)
            {
                var tokenData = new AtProtoTokenData
                {
                    Did = result.Did,
                    Handle = result.Handle,
                    AccessToken = result.AccessToken,
                    RefreshToken = result.RefreshToken,
                    PdsUrl = result.PdsUrl,
                    Issuer = result.Issuer,
                    TokenEndpoint = result.TokenEndpoint,
                    DPoPPrivateKey = result.DPoP.ExportPrivateKey(),
                    AuthServerDpopNonce = result.AuthServerDpopNonce,
                    ResourceServerDpopNonce = result.ResourceServerDpopNonce,
                    TokenObtainedAt = result.TokenObtainedAt,
                    ExpiresIn = result.ExpiresIn,
                    Scope = result.Scope,
                };

                await tokenStore.StoreAsync(result.Did, tokenData, cancellationToken);
                _logger.LogInformation("Stored OAuth tokens for DID: {Did}", result.Did);
            }

            // Determine return URL: prefer server-side store, fall back to cookie, then default
            var returnUrl = loginContext?.ReturnUrl
                ?? context.Request.Cookies["atproto_return_url"]
                ?? _serverOptions.DefaultReturnUrl;

            // Clean up the return URL cookie (best-effort; may be on a different domain)
            context.Response.Cookies.Delete("atproto_return_url", new CookieOptions
            {
                Path = _serverOptions.RoutePrefix,
            });

            // Check if cookie relay is needed (callback domain ≠ login domain).
            // This handles Aspire / Kestrel multi-bind where the OAuth callback arrives on
            // http://127.0.0.1 but the user's browser is on https://localhost.
            var callbackOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
            if (loginContext is not null &&
                !callbackOrigin.Equals(loginContext.Origin, StringComparison.OrdinalIgnoreCase))
            {
                // Callback arrived on a different origin than the user's browser.
                // Don't issue a cookie here (it would be on the wrong domain).
                // Instead, redirect to the login origin with a one-time relay code.
                var relayCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                _relayCodes[relayCode] = new RelayEntry(
                    principal, properties, returnUrl, DateTime.UtcNow.AddMinutes(2));
                CleanupExpiredRelayCodes();

                _logger.LogInformation(
                    "Cookie relay initiated: {CallbackOrigin} -> {LoginOrigin} for {Handle}",
                    callbackOrigin, loginContext.Origin, result.Handle);

                return $"{loginContext.Origin}{_serverOptions.RoutePrefix}/relay?code={relayCode}";
            }

            // Same origin — issue the cookie directly
            await context.SignInAsync(_serverOptions.CookieScheme, principal, properties);

            _logger.LogInformation(
                "OAuth login completed for DID: {Did}, Handle: {Handle}",
                result.Did, result.Handle);

            return returnUrl;
        }
        finally
        {
            // Clean up the OAuth session result — DPoP keys and tokens have been
            // extracted to AtProtoTokenData (if token store is registered) or are
            // no longer needed (cookie-only mode)
            result.Dispose();
        }
    }

    /// <summary>
    /// Signs out by clearing the authentication cookie.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The URL to redirect to after logout.</returns>
    public async Task<string> LogoutAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Remove stored tokens if IAtProtoTokenStore is registered
        var tokenStore = context.RequestServices.GetService<IAtProtoTokenStore>();
        if (tokenStore is not null)
        {
            var did = context.User.FindFirst("did")?.Value
                ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrWhiteSpace(did))
            {
                await tokenStore.RemoveAsync(did, cancellationToken);
                _logger.LogInformation("Removed stored OAuth tokens for DID: {Did}", did);
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

    private static List<Claim> CreateDefaultClaims(OAuthSessionResult result)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.Did),
            new(ClaimTypes.Name, result.Handle),
            new("did", result.Did),
            new("handle", result.Handle),
            new("pds_url", result.PdsUrl),
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
    /// <returns>The return URL to redirect to, or null if the code is invalid or expired.</returns>
    public async Task<string?> TryRedeemRelayCodeAsync(
        HttpContext context, string? code, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(code))
            return null;

        CleanupExpiredRelayCodes();

        if (!_relayCodes.TryRemove(code, out var entry) || entry.Expiry < DateTime.UtcNow)
            return null;

        // Issue the cookie on this domain (the user's actual browsing domain)
        await context.SignInAsync(_serverOptions.CookieScheme, entry.Principal, entry.Properties);

        _logger.LogInformation(
            "Cookie relay completed: auth cookie issued on {Host}",
            context.Request.Host);

        return entry.ReturnUrl;
    }

    private void CleanupExpiredLoginContexts()
    {
        var expired = _loginContexts
            .Where(kv => kv.Value.Expiry < DateTime.UtcNow)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in expired)
            _loginContexts.TryRemove(key, out _);
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
    /// Stores the login origin and return URL for a pending OAuth flow,
    /// enabling cross-origin cookie relay when the callback arrives on a different domain.
    /// </summary>
    private sealed record LoginContext(string Origin, string? ReturnUrl, DateTime Expiry);

    /// <summary>
    /// Stores the authentication result for a one-time cookie relay redirect,
    /// allowing the SDK to issue the cookie on the user's actual browsing domain.
    /// </summary>
    private sealed record RelayEntry(
        ClaimsPrincipal Principal, AuthenticationProperties Properties,
        string ReturnUrl, DateTime Expiry);

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
