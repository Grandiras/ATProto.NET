using System.Security.Claims;
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
/// on first use and kept while the same account stays signed in; when the user changes, signs
/// out, or the session ends, the next call creates it again (or returns
/// <see langword="null"/>). It is disposed with the scope.</para>
/// <para>A circuit can live for hours; the client refreshes its session as it needs to, under the
/// factory's refresh coordinator, so it keeps working alongside the requests and circuits that
/// act for the same account.</para>
/// </remarks>
public sealed class AtProtoUserClientAccessor : IAsyncDisposable
{
    private readonly AuthenticationStateProvider _authenticationState;
    private readonly IAtProtoClientFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AtProtoClient? _client;
    private bool _disposed;

    /// <summary>Creates the accessor.</summary>
    /// <param name="authenticationState">The authentication state of the circuit or request.</param>
    /// <param name="factory">Creates the client from the user's stored session.</param>
    public AtProtoUserClientAccessor(AuthenticationStateProvider authenticationState, IAtProtoClientFactory factory)
    {
        _authenticationState = authenticationState ?? throw new ArgumentNullException(nameof(authenticationState));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
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
                return current;

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

    private static Did? DidOf(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;

        var claim = user.FindFirst(AtProtoClaimTypes.Did)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Did.TryParse(claim, out var did) ? did : null;
    }

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
