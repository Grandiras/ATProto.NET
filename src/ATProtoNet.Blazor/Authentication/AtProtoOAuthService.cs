using System.Security.Claims;
using ATProtoNet.Auth.OAuth;
using Microsoft.AspNetCore.Authentication;
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
    private readonly object _lock = new();
    private bool _disposed;

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

        var (authorizationUrl, _) = await client.StartAuthorizationAsync(
            handle, callbackUrl, pdsUrl, cancellationToken);

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

            // Issue the authentication cookie
            await context.SignInAsync(_serverOptions.CookieScheme, principal, properties);

            // Store tokens server-side if IAtProtoTokenStore is registered (for backend API access)
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

            _logger.LogInformation(
                "OAuth login completed for DID: {Did}, Handle: {Handle}",
                result.Did, result.Handle);
        }
        finally
        {
            // Clean up the OAuth session result — DPoP keys and tokens have been
            // extracted to AtProtoTokenData (if token store is registered) or are
            // no longer needed (cookie-only mode)
            result.Dispose();
        }

        // Read and delete the return URL cookie
        var returnUrl = context.Request.Cookies["atproto_return_url"];
        context.Response.Cookies.Delete("atproto_return_url", new CookieOptions
        {
            Path = _serverOptions.RoutePrefix,
        });

        return returnUrl ?? _serverOptions.DefaultReturnUrl;
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

        return $"{context.Request.Scheme}://{context.Request.Host}{_serverOptions.RoutePrefix}/callback";
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

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _oauthClient?.Dispose();
        }
    }
}
