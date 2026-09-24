using System.Collections.Concurrent;
using ATProtoNet.Identity;
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
/// space until then, so back a real authority with durable storage
/// (<see cref="ATProtoNet.Server.EntityFrameworkCore.EfCoreSpaceAuthorityStore{TContext}"/>).
/// </remarks>
public sealed class InMemorySpaceAuthorityStore : ISpaceAuthorityStore
{
    private readonly ConcurrentDictionary<string, SpaceState> _spaces = new(StringComparer.Ordinal);

    /// <summary>
    /// Declares a space this authority gates, so reads and registrations for it are answered.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <remarks>
    /// A service whose spaces are managed through <c>com.atproto.simplespace</c> does not call
    /// this: <see cref="SimpleSpaceAuthorityStore"/> reads space existence from the
    /// <see cref="ISimpleSpaceStore"/>, and <c>createSpace</c> is what writes it. This is for a
    /// service running a bespoke space type, whose spaces nothing else here knows about.
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
            .OrderBy(entry => entry.Key.Value, StringComparer.Ordinal)
            .Where(entry => cursor is null || string.CompareOrdinal(entry.Key.Value, cursor) > 0)
            .Take(limit + 1)
            .ToList();

        var hasMore = page.Count > limit;
        var repos = page.Take(limit)
            .Select(entry => new SpaceRepoView { Did = entry.Key, Rev = entry.Value.Rev, Hash = entry.Value.Hash })
            .ToList();

        return Task.FromResult(new ListSpaceReposResponse
        {
            Repos = repos,
            Cursor = hasMore && repos.Count > 0 ? repos[^1].Did.Value : null,
        });
    }

    /// <inheritdoc/>
    public Task RecordWriteAsync(
        SpaceUri space, Did repoDid, Tid rev, byte[] hash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repoDid);
        ArgumentNullException.ThrowIfNull(rev);

        var state = _spaces.GetOrAdd(space.Value, _ => new SpaceState());

        // A notification that arrives out of order must not walk a repo's revision backwards; a
        // syncer reads the writer set to decide what advanced.
        state.Writers.AddOrUpdate(
            repoDid,
            _ => new WriterState(rev, hash),
            (_, existing) => rev.CompareTo(existing.Rev) >= 0
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
        public ConcurrentDictionary<Did, WriterState> Writers { get; } = new();
        public ConcurrentDictionary<string, DateTimeOffset> Subscribers { get; } = new(StringComparer.Ordinal);
    }

    private sealed record WriterState(Tid Rev, byte[] Hash);
}

/// <summary>
/// An in-process <see cref="ISimpleSpaceStore"/>.
/// </summary>
/// <remarks>
/// Intended for tests, samples, and single-instance development. Unlike the writer set, a member
/// list cannot be rebuilt from anything on the network — it is never published — so a real
/// authority must persist it
/// (<see cref="ATProtoNet.Server.EntityFrameworkCore.EfCoreSimpleSpaceStore{TContext}"/>).
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
    public Task PutMemberAsync(
        SpaceUri space, Did did, bool read, bool write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(did);

        if (_spaces.TryGetValue(space.Value, out var entry))
            entry.Members[did] = new MemberAccess(read, write);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveMemberAsync(SpaceUri space, Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(did);

        if (_spaces.TryGetValue(space.Value, out var entry))
            entry.Members.TryRemove(did, out _);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<SimpleSpaceMember?> GetMemberAsync(
        SpaceUri space, Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(did);

        return Task.FromResult(
            _spaces.TryGetValue(space.Value, out var entry) && entry.Members.TryGetValue(did, out var access)
                ? ToMember(did, access)
                : null);
    }

    /// <inheritdoc/>
    public Task<ListSimpleSpaceMembersResponse> ListMembersAsync(
        SpaceUri space, int limit, string? cursor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        if (!_spaces.TryGetValue(space.Value, out var entry))
            return Task.FromResult(new ListSimpleSpaceMembersResponse { Members = [] });

        var page = entry.Members
            .OrderBy(member => member.Key.Value, StringComparer.Ordinal)
            .Where(member => cursor is null || string.CompareOrdinal(member.Key.Value, cursor) > 0)
            .Take(limit + 1)
            .ToList();

        var hasMore = page.Count > limit;
        var members = page.Take(limit).Select(member => ToMember(member.Key, member.Value)).ToList();

        return Task.FromResult(new ListSimpleSpaceMembersResponse
        {
            Members = members,
            Cursor = hasMore && members.Count > 0 ? members[^1].Did.Value : null,
        });
    }

    private static SimpleSpaceMember ToMember(Did did, MemberAccess access) =>
        new() { Did = did, Read = access.Read, Write = access.Write };

    private sealed record MemberAccess(bool Read, bool Write);

    private sealed class Entry
    {
        public required SimpleSpaceRecord Record { get; set; }

        public ConcurrentDictionary<Did, MemberAccess> Members { get; } = new();
    }
}
