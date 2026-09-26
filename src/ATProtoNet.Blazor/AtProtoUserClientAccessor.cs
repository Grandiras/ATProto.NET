using System.Security.Claims;
using ATProtoNet.Auth;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace ATProtoNet.Blazor;

/// <summary>
/// The signed-in user's <see cref="AtProtoClient"/>, for the components of one circuit (or one
/// server-rendered request): created through <see cref="IAtProtoClientFactory"/> from the
/// authentication state, and shared by every component in the scope.
/// </summary>
/// <remarks>
/// <para>Registered as a scoped service by
/// <see cref="AtProtoBlazorServiceCollectionExtensions.AddAtProtoBlazor"/>. The client is created
/// on first use and kept while the same account stays signed in and the session store still holds
/// its session: every call reads the store, so a session signed out elsewhere (another tab, another
/// request) or ended by a refused refresh stops being handed out. When the user changes, signs out,
/// or the session ends, the next call creates the client again (or returns
/// <see langword="null"/>). It is disposed with the scope.</para>
/// <para>A circuit can live for hours; the client refreshes its session as it needs to, under the
/// factory's refresh coordinator, so it keeps working alongside the requests and circuits that
/// act for the same account.</para>
/// </remarks>
public sealed class AtProtoUserClientAccessor : IAsyncDisposable
{
    private readonly AuthenticationStateProvider _authenticationState;
    private readonly IAtProtoClientFactory _factory;
    private readonly IAtProtoSessionStore _sessionStore;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AtProtoClient? _client;
    private bool _disposed;

    /// <summary>Creates the accessor.</summary>
    /// <param name="authenticationState">The authentication state of the circuit or request.</param>
    /// <param name="factory">Creates the client from the user's stored session.</param>
    /// <param name="sessionStore">The store the factory reads, checked for the session on every call.</param>
    public AtProtoUserClientAccessor(
        AuthenticationStateProvider authenticationState, IAtProtoClientFactory factory, IAtProtoSessionStore sessionStore)
    {
        _authenticationState = authenticationState ?? throw new ArgumentNullException(nameof(authenticationState));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
    }

    /// <summary>
    /// Returns the client acting as the signed-in user.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The client, or <see langword="null"/> when nobody is signed in, the user carries no DID
    /// claim, or no session is stored for them (signed out elsewhere, say). Do not dispose it:
    /// the scope does.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The scope has ended.</exception>
    public async Task<AtProtoClient?> GetClientAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var user = (await _authenticationState.GetAuthenticationStateAsync()).User;
        var did = DidOf(user);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_client is { IsAuthenticated: true } current && did is not null && current.Did == did)
            {
                // Signed out elsewhere: the client's own copy stays valid until its access token
                // expires, and must not outlive the sign-out that long.
                if (await _sessionStore.GetAsync(did, cancellationToken) is not null)
                    return current;

                await ReleaseClientAsync();
                return null;
            }

            await ReleaseClientAsync();
            if (did is not null)
                _client = await _factory.CreateClientForUserAsync(user, cancellationToken);

            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Did? DidOf(ClaimsPrincipal user) => OAuthUser.DidOf(user);

    private async ValueTask ReleaseClientAsync()
    {
        var client = _client;
        _client = null;
        if (client is not null)
            await client.DisposeAsync();
    }

    /// <inheritdoc/>
    /// <remarks>Waits for a refresh the client has under way to be stored.</remarks>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            await ReleaseClientAsync();
        }
        finally
        {
            _gate.Release();
        }
    }
}
