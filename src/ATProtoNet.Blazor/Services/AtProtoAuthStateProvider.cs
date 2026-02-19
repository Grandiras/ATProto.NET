using System.Security.Claims;
using ATProtoNet.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace ATProtoNet.Blazor.Services;

/// <summary>
/// Blazor authentication state provider backed by an AT Protocol session.
/// Integrates with Blazor's <c>&lt;AuthorizeView&gt;</c> and <c>[Authorize]</c> components.
/// </summary>
public sealed class AtProtoAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly AtProtoClient _client;
    private bool _disposed;

    /// <summary>
    /// Creates a new instance wrapping the given AT Protocol client.
    /// </summary>
    public AtProtoAuthStateProvider(AtProtoClient client)
    {
        _client = client;
    }

    /// <summary>
    /// The current AT Protocol session, if authenticated.
    /// </summary>
    public Session? Session => _client.Session;

    /// <summary>
    /// Whether the user is currently authenticated.
    /// </summary>
    public bool IsAuthenticated => _client.IsAuthenticated;

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
    /// Log in with identifier and password.
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
