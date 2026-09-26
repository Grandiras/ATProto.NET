using ATProtoNet.Identity;

namespace ATProtoNet.Auth;

/// <summary>
/// Serializes the refreshes of an account's session among the <see cref="AtProtoClient"/>
/// instances that share one <see cref="IAtProtoSessionStore"/>, so that its single-use refresh
/// token is spent once.
/// </summary>
/// <remarks>
/// <para>A client given one (<see cref="AtProtoClientOptions.RefreshCoordinator"/>) together with
/// a session store refreshes under the account's lock, and reads the store once it holds it.
/// When another client has refreshed meanwhile, it takes up the stored session instead of
/// spending its own, already spent, copy of the refresh token; when the store no longer holds
/// the session, the session was signed out, and it ends in this client too. Otherwise it
/// refreshes and stores the result before letting go, so the next client to take the lock reads
/// the new tokens.</para>
/// <para>This matters wherever several clients act for one account at once, as the per-request
/// clients of a web application do: two requests that each find the access token about to
/// expire would otherwise both refresh, and the authorization server, seeing its refresh token
/// used twice, would end the session. <see cref="InProcessSessionRefreshCoordinator"/>
/// coordinates the clients of one process; instances that share a store across processes need
/// a distributed lock behind this interface.</para>
/// </remarks>
public interface ISessionRefreshCoordinator
{
    /// <summary>
    /// Waits for the exclusive right to refresh the session of <paramref name="did"/>, which lasts
    /// until the returned lease is disposed.
    /// </summary>
    /// <param name="did">The account.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The lease; disposing it lets the next waiter in.</returns>
    ValueTask<IAsyncDisposable> AcquireAsync(Did did, CancellationToken cancellationToken = default);
}

/// <summary>
/// An <see cref="ISessionRefreshCoordinator"/> for the clients of one process: a lock per
/// account, kept only while it is held or waited for.
/// </summary>
/// <remarks>
/// Share one instance among every client that shares the store, as the server integration's
/// client factory does with the one it registers.
/// </remarks>
public sealed class InProcessSessionRefreshCoordinator : ISessionRefreshCoordinator
{
    private readonly KeyedLock<Did> _locks = new();

    /// <summary>How many accounts' locks are held or waited for.</summary>
    internal int ActiveCount => _locks.Count;

    /// <inheritdoc/>
    public async ValueTask<IAsyncDisposable> AcquireAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);
        return await _locks.AcquireAsync(did, cancellationToken);
    }
}
