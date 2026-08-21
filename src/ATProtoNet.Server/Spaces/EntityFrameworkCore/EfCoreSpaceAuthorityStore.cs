using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using Microsoft.EntityFrameworkCore;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="ISpaceAuthorityStore"/>: the writer set and the notification
/// registrations, in a relational database.
/// </summary>
/// <remarks>
/// <para>Suitable for a multi-instance authority, where every instance shares one database.
/// Losing the writer set is not catastrophic — it is only what the authority <em>claims</em>,
/// and the next <c>notifyWrite</c> from any repo host rebuilds an entry — but until then syncers
/// see a space that has no repos in it, which is a worse answer than a stale one.</para>
/// <para>Pagination is by DID, so a cursor names a position rather than an offset into a set
/// that reorders as writes arrive. The ordering and the cursor comparison are both evaluated by
/// the database, so they agree with each other under any collation; a database whose collation
/// is not ordinal orders a page differently from
/// <see cref="InMemorySpaceAuthorityStore"/> but never skips or repeats a row.</para>
/// <para>Register with
/// <see cref="SpaceStoreExtensions.AddAtProtoEfCoreSpaceAuthority{TContext}"/>.</para>
/// </remarks>
/// <typeparam name="TContext">
/// A <see cref="DbContext"/> carrying the authority entities. Use <see cref="SpaceDbContext"/>,
/// or your own context configured with
/// <see cref="SpaceDbContext.ConfigureSpaceAuthorityModel"/>.
/// </typeparam>
public sealed class EfCoreSpaceAuthorityStore<TContext> : ISpaceAuthorityStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the store.
    /// </summary>
    /// <param name="contextFactory">Supplies a context per operation.</param>
    public EfCoreSpaceAuthorityStore(IDbContextFactory<TContext> contextFactory)
        : this(contextFactory, TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates the store, reading the current time from <paramref name="timeProvider"/>.
    /// </summary>
    /// <param name="contextFactory">Supplies a context per operation.</param>
    /// <param name="timeProvider">The clock lapsed registrations are measured against.</param>
    public EfCoreSpaceAuthorityStore(IDbContextFactory<TContext> contextFactory, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _contextFactory = contextFactory;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Declares a space this authority gates, so reads and registrations for it are answered.
    /// Idempotent.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The durable counterpart of <see cref="InMemorySpaceAuthorityStore.DeclareSpace"/>. A
    /// service whose spaces are managed through <c>com.atproto.simplespace</c> declares each one
    /// here when it is created, since the two stores hold separate state.
    /// </remarks>
    public async Task DeclareSpaceAsync(SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        await MutateAsync(
            (context, ct) => EnsureSpaceAsync(context, space.Value, ct), cancellationToken);
    }

    /// <summary>
    /// Marks a space deleted, so it answers <see cref="SpaceErrors.SpaceDeleted"/>. Idempotent.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task MarkDeletedAsync(SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        await MutateAsync(async (context, ct) =>
        {
            var entity = await context.Set<SpaceEntity>().FindAsync([space.Value], ct);

            if (entity is null)
                context.Add(new SpaceEntity { Space = space.Value, Deleted = true });
            else
                entity.Deleted = true;
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SpaceAccessOutcome> GetSpaceStateAsync(
        SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<SpaceEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Space == space.Value, cancellationToken);

        if (entity is null)
            return SpaceAccessOutcome.SpaceNotFound;

        return entity.Deleted ? SpaceAccessOutcome.SpaceDeleted : SpaceAccessOutcome.Granted;
    }

    /// <inheritdoc/>
    public async Task<ListSpaceReposResponse> ListReposAsync(
        SpaceUri space, int limit, string? cursor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Set<SpaceWriterEntity>()
            .AsNoTracking()
            .Where(e => e.Space == space.Value);

        if (cursor is not null)
            query = query.Where(e => string.Compare(e.Did, cursor) > 0);

        // One row past the page, so the presence of a next page is known without a count.
        var page = await query
            .OrderBy(e => e.Did)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > limit;
        var repos = page.Take(limit)
            .Select(e => new SpaceRepoView { Did = e.Did, Rev = e.Rev, Hash = e.Hash })
            .ToList();

        return new ListSpaceReposResponse
        {
            Repos = repos,
            Cursor = hasMore && repos.Count > 0 ? repos[^1].Did : null,
        };
    }

    /// <inheritdoc/>
    public async Task RecordWriteAsync(
        SpaceUri space, string repoDid, string rev, byte[] hash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoDid);
        ArgumentException.ThrowIfNullOrWhiteSpace(rev);
        ArgumentNullException.ThrowIfNull(hash);

        // Two notifications for the same repo can both find no row and both insert; the loser's
        // insert violates the key, and the retry takes the update path instead.
        await MutateAsync(async (context, ct) =>
        {
            await EnsureSpaceAsync(context, space.Value, ct);

            var writers = context.Set<SpaceWriterEntity>();
            var existing = await writers.FindAsync([space.Value, repoDid], ct);

            if (existing is null)
            {
                writers.Add(new SpaceWriterEntity
                {
                    Space = space.Value,
                    Did = repoDid,
                    Rev = rev,
                    Hash = hash,
                });
            }
            // A notification that arrives out of order must not walk a repo's revision backwards;
            // a syncer reads the writer set to decide what advanced. TIDs sort
            // lexicographically, and the comparison is ordinal here rather than in the database
            // because a collation that ignores case would call two different revisions equal.
            else if (string.CompareOrdinal(rev, existing.Rev) >= 0)
            {
                existing.Rev = rev;
                existing.Hash = hash;
            }
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RegisterNotifyAsync(
        SpaceUri space, string service, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        await MutateAsync(async (context, ct) =>
        {
            await EnsureSpaceAsync(context, space.Value, ct);

            var subscribers = context.Set<SpaceSubscriberEntity>();
            var existing = await subscribers.FindAsync([space.Value, service], ct);

            if (existing is null)
            {
                subscribers.Add(new SpaceSubscriberEntity
                {
                    Space = space.Value,
                    Service = service,
                    ExpiresAt = expiresAt,
                });
            }
            else
            {
                existing.ExpiresAt = expiresAt;
            }
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UnregisterNotifyAsync(
        SpaceUri space, string service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var subscribers = context.Set<SpaceSubscriberEntity>();
        var existing = await subscribers.FindAsync([space.Value, service], cancellationToken);

        if (existing is null)
            return;

        subscribers.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SpaceNotifySubscriber>> ListSubscribersAsync(
        SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        var now = _timeProvider.GetUtcNow();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var live = await context.Set<SpaceSubscriberEntity>()
            .AsNoTracking()
            .Where(e => e.Space == space.Value && e.ExpiresAt > now)
            .OrderBy(e => e.Service)
            .ToListAsync(cancellationToken);

        return live.Select(e => new SpaceNotifySubscriber(e.Service, e.ExpiresAt)).ToList();
    }

    /// <summary>
    /// Adds the space row when it is missing, mirroring the in-memory store's behaviour of
    /// declaring a space on first use. The endpoints check
    /// <see cref="GetSpaceStateAsync"/> before writing, so in practice this only matters to a
    /// caller driving the store directly.
    /// </summary>
    private static async Task EnsureSpaceAsync(
        TContext context, string space, CancellationToken cancellationToken)
    {
        var spaces = context.Set<SpaceEntity>();
        if (await spaces.FindAsync([space], cancellationToken) is not null)
            return;

        spaces.Add(new SpaceEntity { Space = space });
    }

    /// <summary>
    /// Applies a mutation and saves it, retrying once on a failed save.
    /// </summary>
    /// <remarks>
    /// Every write here is an upsert done as read-then-insert-or-update, so two instances acting
    /// on the same row at once can both find nothing and both insert. The retry runs the whole
    /// mutation again on a fresh context, which now sees the winner's row and takes the update
    /// path. A second failure is not a race — a value too long for its column fails identically
    /// both times — so it propagates rather than being swallowed.
    /// </remarks>
    private async Task MutateAsync(
        Func<TContext, CancellationToken, Task> mutate, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await mutate(context, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return;
        }
        catch (DbUpdateException)
        {
            // Fall through to the retry, on a context that never saw the failed change.
        }

        await using var retry = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await mutate(retry, cancellationToken);
        await retry.SaveChangesAsync(cancellationToken);
    }
}
