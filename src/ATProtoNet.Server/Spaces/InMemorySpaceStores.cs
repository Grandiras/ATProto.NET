using System.Collections.Concurrent;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// An in-process <see cref="ISpaceAuthorityStore"/>: the writer set and the notification
/// registrations, held in memory.
/// </summary>
/// <remarks>
/// Intended for tests, samples, and single-instance development. Losing the writer set on a
/// restart is not catastrophic — it is only what the authority <em>claims</em>, and a
/// notification from any repo host rebuilds an entry — but losing it means syncers see an empty
/// space until then, so back a real authority with durable storage.
/// </remarks>
public sealed class InMemorySpaceAuthorityStore : ISpaceAuthorityStore
{
    private readonly ConcurrentDictionary<string, SpaceState> _spaces = new(StringComparer.Ordinal);

    /// <summary>
    /// Declares a space this authority gates, so reads and registrations for it are answered.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <remarks>
    /// A store used alongside <see cref="InMemorySimpleSpaceStore"/> does not need this — the
    /// simplespace store is the one that knows which spaces exist. It is here for a service whose
    /// spaces are declared elsewhere.
    /// </remarks>
    public void DeclareSpace(SpaceUri space)
    {
        ArgumentNullException.ThrowIfNull(space);
        _spaces.GetOrAdd(space.Value, _ => new SpaceState());
    }

    /// <summary>Marks a space deleted, so it answers <see cref="SpaceErrors.SpaceDeleted"/>.</summary>
    /// <param name="space">The space.</param>
    public void MarkDeleted(SpaceUri space)
    {
        ArgumentNullException.ThrowIfNull(space);
        _spaces.GetOrAdd(space.Value, _ => new SpaceState()).Deleted = true;
    }

    /// <inheritdoc/>
    public Task<SpaceAccessOutcome> GetSpaceStateAsync(
        SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        if (!_spaces.TryGetValue(space.Value, out var state))
            return Task.FromResult(SpaceAccessOutcome.SpaceNotFound);

        return Task.FromResult(state.Deleted ? SpaceAccessOutcome.SpaceDeleted : SpaceAccessOutcome.Granted);
    }

    /// <inheritdoc/>
    public Task<ListSpaceReposResponse> ListReposAsync(
        SpaceUri space, int limit, string? cursor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        if (!_spaces.TryGetValue(space.Value, out var state))
            return Task.FromResult(new ListSpaceReposResponse { Repos = [] });

        // Ordered by DID so the cursor is a stable position rather than an index into a set that
        // reorders as writes arrive.
        var page = state.Writers
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(entry => cursor is null || string.CompareOrdinal(entry.Key, cursor) > 0)
            .Take(limit + 1)
            .ToList();

        var hasMore = page.Count > limit;
        var repos = page.Take(limit)
            .Select(entry => new SpaceRepoView { Did = entry.Key, Rev = entry.Value.Rev, Hash = entry.Value.Hash })
            .ToList();

        return Task.FromResult(new ListSpaceReposResponse
        {
            Repos = repos,
            Cursor = hasMore && repos.Count > 0 ? repos[^1].Did : null,
        });
    }

    /// <inheritdoc/>
    public Task RecordWriteAsync(
        SpaceUri space, string repoDid, string rev, byte[] hash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoDid);

        var state = _spaces.GetOrAdd(space.Value, _ => new SpaceState());

        // A notification that arrives out of order must not walk a repo's revision backwards; a
        // syncer reads the writer set to decide what advanced.
        state.Writers.AddOrUpdate(
            repoDid,
            _ => new WriterState(rev, hash),
            (_, existing) => string.CompareOrdinal(rev, existing.Rev) >= 0
                ? new WriterState(rev, hash)
                : existing);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RegisterNotifyAsync(
        SpaceUri space, string service, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        _spaces.GetOrAdd(space.Value, _ => new SpaceState()).Subscribers[service] = expiresAt;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UnregisterNotifyAsync(SpaceUri space, string service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        if (_spaces.TryGetValue(space.Value, out var state))
            state.Subscribers.TryRemove(service, out _);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SpaceNotifySubscriber>> ListSubscribersAsync(
        SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        if (!_spaces.TryGetValue(space.Value, out var state))
            return Task.FromResult<IReadOnlyList<SpaceNotifySubscriber>>([]);

        var now = DateTimeOffset.UtcNow;
        var live = state.Subscribers
            .Where(entry => entry.Value > now)
            .Select(entry => new SpaceNotifySubscriber(entry.Key, entry.Value))
            .ToList();

        return Task.FromResult<IReadOnlyList<SpaceNotifySubscriber>>(live);
    }

    private sealed class SpaceState
    {
        public bool Deleted { get; set; }
        public ConcurrentDictionary<string, WriterState> Writers { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, DateTimeOffset> Subscribers { get; } = new(StringComparer.Ordinal);
    }

    private sealed record WriterState(string Rev, byte[] Hash);
}

/// <summary>
/// An in-process <see cref="ISimpleSpaceStore"/>.
/// </summary>
/// <remarks>
/// Intended for tests, samples, and single-instance development. Unlike the writer set, a member
/// list cannot be rebuilt from anything on the network — it is never published — so a real
/// authority must persist it.
/// </remarks>
public sealed class InMemorySimpleSpaceStore : ISimpleSpaceStore
{
    private readonly ConcurrentDictionary<string, Entry> _spaces = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<SimpleSpaceRecord?> GetSpaceAsync(SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        return Task.FromResult(_spaces.TryGetValue(space.Value, out var entry) ? entry.Record : null);
    }

    /// <inheritdoc/>
    public Task<bool> CreateSpaceAsync(SimpleSpaceRecord space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        return Task.FromResult(_spaces.TryAdd(space.Uri.Value, new Entry { Record = space }));
    }

    /// <inheritdoc/>
    public Task UpdateSpaceAsync(SimpleSpaceRecord space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        if (_spaces.TryGetValue(space.Uri.Value, out var entry))
            entry.Record = space;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteSpaceAsync(SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        // Flagged rather than removed: a deleted space keeps answering SpaceDeleted, which is how
        // a syncer that missed the notification learns to drop its copy.
        if (_spaces.TryGetValue(space.Value, out var entry))
            entry.Record = entry.Record with { Deleted = true };

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AddMemberAsync(SpaceUri space, string did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        if (_spaces.TryGetValue(space.Value, out var entry))
            entry.Members[did] = 0;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveMemberAsync(SpaceUri space, string did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        if (_spaces.TryGetValue(space.Value, out var entry))
            entry.Members.TryRemove(did, out _);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> IsMemberAsync(SpaceUri space, string did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        return Task.FromResult(
            _spaces.TryGetValue(space.Value, out var entry) && entry.Members.ContainsKey(did));
    }

    /// <inheritdoc/>
    public Task<ListSimpleSpaceMembersResponse> ListMembersAsync(
        SpaceUri space, int limit, string? cursor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        if (!_spaces.TryGetValue(space.Value, out var entry))
            return Task.FromResult(new ListSimpleSpaceMembersResponse { Members = [] });

        var page = entry.Members.Keys
            .OrderBy(did => did, StringComparer.Ordinal)
            .Where(did => cursor is null || string.CompareOrdinal(did, cursor) > 0)
            .Take(limit + 1)
            .ToList();

        var hasMore = page.Count > limit;
        var members = page.Take(limit).Select(did => new SimpleSpaceMember { Did = did }).ToList();

        return Task.FromResult(new ListSimpleSpaceMembersResponse
        {
            Members = members,
            Cursor = hasMore && members.Count > 0 ? members[^1].Did : null,
        });
    }

    private sealed class Entry
    {
        public required SimpleSpaceRecord Record { get; set; }

        // A set; the value is unused. ConcurrentDictionary is the only concurrent set available.
        public ConcurrentDictionary<string, byte> Members { get; } = new(StringComparer.Ordinal);
    }
}
