using System.Collections.Concurrent;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>An in-process <see cref="ISpaceAuthorityStore"/>: the writer set and the notification registrations, held in memory.</summary>
/// <remarks>
/// Intended for tests, samples, and single-instance development. Losing the writer set on a
/// restart is not catastrophic — it is only what the authority <em>claims</em>, and a
/// notification from any repo host rebuilds an entry — but losing it means syncers see an empty
/// space until then, so back a real authority with durable storage
/// (<c>EfCoreSpaceAuthorityStore&lt;TContext&gt;</c>, in the <c>ATProtoNet.Server.EntityFrameworkCore</c> package).
/// </remarks>
public sealed class InMemorySpaceAuthorityStore : ISpaceAuthorityStore
{
    private readonly ConcurrentDictionary<string, SpaceState> _spaces = new(StringComparer.Ordinal);

    /// <summary>Declares a space this authority gates, so reads and registrations for it are answered.</summary>
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
    /// <remarks>
    /// The counterpart of <see cref="DeclareSpace"/> for a bespoke space type. A space deleted
    /// through <c>com.atproto.simplespace.deleteSpace</c> needs no call here: its deletion is read
    /// from the <see cref="ISimpleSpaceStore"/>.
    /// </remarks>
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

        // Selected under the sequencing lock: enumerating the dictionary while a write lands could show a
        // later revision without an earlier one, and the cursor would then skip the earlier.
        List<KeyValuePair<Did, WriterState>> rows;
        lock (state.Sequence)
            rows = SpacePaging.After(state.Writers, entry => entry.Value.SpaceRev.Value, cursor, limit);

        var (repos, next) = SpacePaging.CheckpointPage(
            rows,
            limit,
            entry => new SpaceRepoView { Did = entry.Key, RepoRev = entry.Value.RepoRev, Hash = entry.Value.Hash, SpaceRev = entry.Value.SpaceRev },
            repo => repo.SpaceRev.Value);

        return Task.FromResult(new ListSpaceReposResponse { Repos = repos, Cursor = next });
    }

    /// <inheritdoc/>
    public Task<SpaceWriteSequence?> RecordWriteAsync(
        SpaceUri space, Did repoDid, Tid repoRev, byte[] hash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repoDid);
        ArgumentNullException.ThrowIfNull(repoRev);

        var state = _spaces.GetOrAdd(space.Value, _ => new SpaceState());

        // One lock per space: the revision is allocated and the writer updated as a single step, so
        // revisions increase in the order writes are accepted.
        lock (state.Sequence)
        {
            // A repo's revision never moves backwards, and a duplicate changes nothing.
            if (state.Writers.TryGetValue(repoDid, out var existing) && repoRev.CompareTo(existing.RepoRev) <= 0)
                return Task.FromResult<SpaceWriteSequence?>(null);

            var spaceRev = SpacePaging.NextSpaceRev(state.LastSpaceRev);
            var sequence = new SpaceWriteSequence(spaceRev, state.LastSpaceRev);
            state.Writers[repoDid] = new WriterState(repoRev, hash, spaceRev);
            state.LastSpaceRev = spaceRev;
            return Task.FromResult<SpaceWriteSequence?>(sequence);
        }
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
        public object Sequence { get; } = new();
        public Tid? LastSpaceRev { get; set; }
        public ConcurrentDictionary<Did, WriterState> Writers { get; } = new();
        public ConcurrentDictionary<string, DateTimeOffset> Subscribers { get; } = new(StringComparer.Ordinal);
    }

    private sealed record WriterState(Tid RepoRev, byte[] Hash, Tid SpaceRev);
}

/// <summary>An in-process <see cref="ISimpleSpaceStore"/>.</summary>
/// <remarks>
/// Intended for tests, samples, and single-instance development. Unlike the writer set, a member
/// list cannot be rebuilt from anything on the network — it is never published — so a real
/// authority must persist it
/// (<c>EfCoreSimpleSpaceStore&lt;TContext&gt;</c>, in the <c>ATProtoNet.Server.EntityFrameworkCore</c> package).
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

        var rows = SpacePaging.After(entry.Members, member => member.Key.Value, cursor, limit);
        var (members, next) = SpacePaging.Page(
            rows, limit, member => ToMember(member.Key, member.Value), member => member.Did.Value);

        return Task.FromResult(new ListSimpleSpaceMembersResponse { Members = members, Cursor = next });
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
