using System.Collections.Concurrent;
using ATProtoNet.Identity;

namespace ATProtoNet.Auth;

/// <summary>
/// Persists sessions, one per account, keyed by DID.
/// </summary>
/// <remarks>
/// <para>Give one to the <see cref="AtProtoClient"/> constructor
/// and the client keeps it current: it writes each session it installs and every refreshed
/// version, and removes the session when it is signed out or expires.
/// <see cref="AtProtoClient.TryRestoreSessionAsync"/> reads it back, and the server-side client
/// factory reads it for each request.</para>
/// <para>Refresh tokens are single-use, so a store shared by several processes must see each
/// write before the next refresh reads it; a client that refreshes from a stale copy is
/// rejected and loses the session.</para>
/// <para>Sessions serialize with <see cref="System.Text.Json"/> as <see cref="AtProtoSession"/>.
/// <b>Security:</b> they hold bearer tokens and DPoP private keys, so encrypt them at rest.</para>
/// </remarks>
public interface IAtProtoSessionStore
{
    /// <summary>Reads the stored session of an account.</summary>
    /// <param name="did">The account's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session, or <see langword="null"/> when none is stored.</returns>
    ValueTask<AtProtoSession?> GetAsync(Did did, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a session, replacing any stored for the same account (<see cref="AtProtoSession.Did"/>).
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SetAsync(AtProtoSession session, CancellationToken cancellationToken = default);

    /// <summary>Removes the stored session of an account, if there is one.</summary>
    /// <param name="did">The account's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask RemoveAsync(Did did, CancellationToken cancellationToken = default);
}

/// <summary>
/// An <see cref="IAtProtoSessionStore"/> in process memory. Sessions are lost when the process
/// exits, and are not shared with other processes.
/// </summary>
/// <remarks>
/// Thread-safe. Sessions are immutable, so what it returns is exactly what was stored.
/// </remarks>
public sealed class InMemoryAtProtoSessionStore : IAtProtoSessionStore
{
    private readonly ConcurrentDictionary<Did, AtProtoSession> _sessions = new();

    /// <inheritdoc/>
    public ValueTask<AtProtoSession?> GetAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);
        return ValueTask.FromResult(_sessions.GetValueOrDefault(did));
    }

    /// <inheritdoc/>
    public ValueTask SetAsync(AtProtoSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        _sessions[session.Did] = session;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask RemoveAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);
        _sessions.TryRemove(did, out _);
        return ValueTask.CompletedTask;
    }
}
