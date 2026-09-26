using System.Collections.Concurrent;
using ATProtoNet.Identity;

namespace ATProtoNet.Streaming;

/// <summary>
/// Where a repository's chain of verified commits stands: the sync spec's repo sync status.
/// </summary>
public enum RepoSyncStatus
{
    /// <summary>
    /// Every commit up to <see cref="RepoSyncState.Rev"/> has been verified and chained: the
    /// next <c>#commit</c> must build on <see cref="RepoSyncState.Data"/>.
    /// </summary>
    Synchronized,

    /// <summary>
    /// The chain is broken, or a <c>#sync</c> event reset the repository: its records must be
    /// fetched again (the spec's <c>desynchronized</c>). Commits for it are not delivered until then.
    /// </summary>
    Desynchronized,

    /// <summary>
    /// A resynchronization is fetching the repository (the spec's <c>in-progress</c>). A process
    /// that stopped during one treats it as <see cref="Desynchronized"/>.
    /// </summary>
    Resynchronizing,
}

/// <summary>
/// The sync state of one repository: its last verified revision, the MST root of that revision,
/// and whether the chain of commits leading there is intact.
/// </summary>
/// <param name="Did">The repository.</param>
/// <param name="Rev">
/// The last revision seen for the repository, or null when it is not yet known, as for a
/// repository marked <see cref="RepoSyncStatus.Desynchronized"/> to have it fetched.
/// </param>
/// <param name="Data">
/// The MST root (the commit's <c>data</c>) of <paramref name="Rev"/>: the <c>prevData</c> the next
/// commit must carry. Null when not known.
/// </param>
/// <param name="Status">Whether the chain is intact.</param>
public sealed record RepoSyncState(Did Did, Tid? Rev, Cid? Data, RepoSyncStatus Status);

/// <summary>
/// Keeps each repository's <see cref="RepoSyncState"/>, the small amount of state Sync 1.1
/// verification needs per account.
/// </summary>
/// <remarks>
/// <para>The verifier reads a repository's state for every commit and writes it once the commit is
/// processed, so the store sits on the firehose's hot path: keep reads cheap.
/// <see cref="InMemoryRepoSyncStateStore"/> holds it in process; <c>ATProtoNet.Server</c> has an
/// EF Core store that survives restarts.</para>
/// <para>A repository the store has no state for is taken as it comes: its first verified commit
/// becomes the start of its chain. Set a <see cref="RepoSyncStatus.Desynchronized"/> state (with
/// no revision) for a repository you want fetched in full, such as one discovered through
/// <c>com.atproto.sync.listReposByCollection</c>.</para>
/// </remarks>
public interface IRepoSyncStateStore
{
    /// <summary>Reads a repository's state, or null when there is none.</summary>
    /// <param name="did">The repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<RepoSyncState?> GetAsync(Did did, CancellationToken cancellationToken = default);

    /// <summary>Records a repository's state, replacing any it had.</summary>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SetAsync(RepoSyncState state, CancellationToken cancellationToken = default);

    /// <summary>Forgets a repository. Removing one that has no state does nothing.</summary>
    /// <param name="did">The repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask RemoveAsync(Did did, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists up to <paramref name="limit"/> repositories whose status is not
    /// <see cref="RepoSyncStatus.Synchronized"/>: the ones a resynchronization should fetch.
    /// </summary>
    /// <param name="limit">The most to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<RepoSyncState>> ListUnsynchronizedAsync(int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// An <see cref="IRepoSyncStateStore"/> held in process memory: fast, and gone when the process
/// stops, after which every repository starts a new chain at its next commit.
/// </summary>
/// <remarks>
/// A state takes a couple of hundred bytes, so tracking the whole network (tens of millions of
/// repositories) takes gigabytes; use a persistent store, or track fewer repositories, at that
/// scale.
/// </remarks>
public sealed class InMemoryRepoSyncStateStore : IRepoSyncStateStore
{
    private readonly ConcurrentDictionary<Did, RepoSyncState> _states = new();

    /// <summary>How many repositories have a state.</summary>
    public int Count => _states.Count;

    /// <inheritdoc/>
    public ValueTask<RepoSyncState?> GetAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);
        return ValueTask.FromResult(_states.TryGetValue(did, out var state) ? state : null);
    }

    /// <inheritdoc/>
    public ValueTask SetAsync(RepoSyncState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        _states[state.Did] = state;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask RemoveAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);
        _states.TryRemove(did, out _);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<RepoSyncState>> ListUnsynchronizedAsync(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var found = new List<RepoSyncState>();
        foreach (var state in _states.Values)
        {
            if (state.Status == RepoSyncStatus.Synchronized)
                continue;

            found.Add(state);
            if (found.Count == limit)
                break;
        }

        return ValueTask.FromResult<IReadOnlyList<RepoSyncState>>(found);
    }
}
