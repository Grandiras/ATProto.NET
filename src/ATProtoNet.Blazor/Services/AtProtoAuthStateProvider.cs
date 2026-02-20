using System.Security.Claims;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using Microsoft.AspNetCore.Components.Authorization;

namespace ATProtoNet.Blazor.Services;

/// <summary>
/// Blazor authentication state provider backed by an AT Protocol session.
/// Integrates with Blazor's <c>&lt;AuthorizeView&gt;</c> and <c>[Authorize]</c> components.
/// Supports both legacy password auth and OAuth authentication flows.
/// </summary>
public sealed class AtProtoAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly AtProtoClient _client;
    private OAuthClient? _oauthClient;
    private bool _disposed;

    /// <summary>
    /// Creates a new instance wrapping the given AT Protocol client.
    /// </summary>
    public AtProtoAuthStateProvider(AtProtoClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Creates a new instance with OAuth support.
    /// </summary>
    public AtProtoAuthStateProvider(AtProtoClient client, OAuthClient oauthClient)
    {
        _client = client;
        _oauthClient = oauthClient;
    }

    /// <summary>
    /// The current AT Protocol session, if authenticated.
    /// </summary>
    public Session? Session => _client.Session;

    /// <summary>
    /// Whether the user is currently authenticated.
    /// </summary>
    public bool IsAuthenticated => _client.IsAuthenticated;

    /// <summary>
    /// The OAuth client, if configured.
    /// </summary>
    public OAuthClient? OAuthClient => _oauthClient;

    /// <summary>
    /// Whether OAuth is available for authentication.
    /// </summary>
    public bool IsOAuthEnabled => _oauthClient is not null;

    /// <inheritdoc/>
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_client.Session is { } session)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, session.Did),
                new(ClaimTypes.Name, session.Handle),
                new("did", session.Did),
                new("handle", session.Handle),
            };

            if (session.Email is not null)
                claims.Add(new Claim(ClaimTypes.Email, session.Email));

            // Add PDS URL claim
            claims.Add(new Claim("pds_url", _client.PdsUrl));

            // Add auth method claim
            claims.Add(new Claim("auth_method", _client.OAuthSession is not null ? "oauth" : "password"));

            var identity = new ClaimsIdentity(claims, "ATProto");
            var user = new ClaimsPrincipal(identity);
            return Task.FromResult(new AuthenticationState(user));
        }
        else
        {
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
            return Task.FromResult(new AuthenticationState(anonymous));
        }
    }

    /// <summary>
    /// Log in with identifier and password (legacy auth).
    /// </summary>
    /// <param name="identifier">Handle or email.</param>
    /// <param name="password">Password or app password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Session> LoginAsync(
        string identifier, string password, CancellationToken cancellationToken = default)
    {
        var session = await _client.LoginAsync(identifier, password, cancellationToken: cancellationToken);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        return session;
    }

    /// <summary>
    /// Log in with identifier and password against a specific PDS (dynamic PDS).
    /// </summary>
    /// <param name="pdsUrl">The PDS URL to authenticate against.</param>
    /// <param name="identifier">Handle or email.</param>
    /// <param name="password">Password or app password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Session> LoginAsync(
        string pdsUrl, string identifier, string password, CancellationToken cancellationToken = default)
    {
        _client.SetPdsUrl(pdsUrl);
        var session = await _client.LoginAsync(identifier, password, cancellationToken: cancellationToken);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        return session;
    }

    /// <summary>
    /// Start an OAuth authorization flow. Returns the URL to redirect the user to.
    /// </summary>
    /// <param name="identifier">Handle, DID, or PDS URL.</param>
    /// <param name="redirectUri">The callback URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization URL and state parameter.</returns>
    public async Task<(string AuthorizationUrl, string State)> StartOAuthLoginAsync(
        string identifier, string redirectUri, CancellationToken cancellationToken = default)
    {
        if (_oauthClient is null)
            throw new InvalidOperationException(
                "OAuth is not configured. Call AddAtProtoBlazor with OAuth options.");

        return await _oauthClient.StartAuthorizationAsync(identifier, redirectUri, cancellationToken);
    }

    /// <summary>
    /// Complete an OAuth authorization flow after the user is redirected back.
    /// </summary>
    /// <param name="code">The authorization code from the callback.</param>
    /// <param name="state">The state parameter from the callback.</param>
    /// <param name="issuer">The issuer parameter from the callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CompleteOAuthLoginAsync(
        string code, string state, string issuer, CancellationToken cancellationToken = default)
    {
        if (_oauthClient is null)
            throw new InvalidOperationException("OAuth is not configured.");

        var oauthSession = await _oauthClient.CompleteAuthorizationAsync(code, state, issuer, cancellationToken);
        await _client.ApplyOAuthSessionAsync(oauthSession, cancellationToken);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <summary>
    /// Log out and clear the session.
    /// </summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _client.LogoutAsync(cancellationToken);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <summary>
    /// Resume a session from a stored session.
    /// </summary>
    public async Task ResumeSessionAsync(Session session, CancellationToken cancellationToken = default)
    {
        await _client.ResumeSessionAsync(session, cancellationToken);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
