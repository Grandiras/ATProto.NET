using System.Collections.Concurrent;

namespace ATProtoNet.Pds;

/// <summary>
/// In-memory repository head store for development and testing.
/// Not suitable for production use — all state is lost on restart, which for a federating
/// PDS means relays would see the repo rewind.
/// </summary>
public sealed class InMemoryRepoCommitStore : IRepoCommitStore
{
    private readonly ConcurrentDictionary<string, RepoCommitState> _heads = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<RepoCommitState?> GetAsync(string did, CancellationToken cancellationToken = default)
    {
        _heads.TryGetValue(did, out var state);
        return Task.FromResult(state);
    }

    /// <inheritdoc />
    public Task SetAsync(RepoCommitState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        _heads[state.Did] = state;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RepoCommitState>> ListAsync(
        int limit, string? cursor, CancellationToken cancellationToken = default)
    {
        IEnumerable<RepoCommitState> ordered = _heads.Values.OrderBy(s => s.Did, StringComparer.Ordinal);

        if (!string.IsNullOrEmpty(cursor))
            ordered = ordered.Where(s => string.CompareOrdinal(s.Did, cursor) > 0);

        IReadOnlyList<RepoCommitState> page = ordered.Take(Math.Max(limit, 0)).ToList();
        return Task.FromResult(page);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string did, CancellationToken cancellationToken = default)
    {
        _heads.TryRemove(did, out _);
        return Task.CompletedTask;
    }
}
