using ATProtoNet.Identity;
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
/// <para>The writes on the notification path are updates first. A renewed registration costs one
/// <c>UPDATE</c> and no read; <c>notifyWrite</c> for a writer already in the set costs a
/// primary-key read and one <c>UPDATE</c> conditioned on the revision it read. Revisions are
/// compared here rather than in SQL, because a SQL comparison follows the column's collation and a
/// TID's order is its bytes' only under some collations. Only a row that does not exist yet takes
/// the insert path.</para>
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
    /// The durable counterpart of <see cref="InMemorySpaceAuthorityStore.DeclareSpace"/>, and
    /// for the same case: a bespoke space type. Spaces managed through
    /// <c>com.atproto.simplespace</c> need no declaration — <see cref="SimpleSpaceAuthorityStore"/>
    /// reads their existence from the <see cref="ISimpleSpaceStore"/> that <c>createSpace</c>
    /// wrote them to.
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

        var spaceValue = space.Value;
        await using (var context = await _contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var updated = await context.Set<SpaceEntity>()
                .Where(e => e.Space == spaceValue)
                .ExecuteUpdateAsync(set => set.SetProperty(e => e.Deleted, true), cancellationToken);

            if (updated > 0)
                return;
        }

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

        var (repos, next) = SpacePaging.Page(
            page,
            limit,
            e => new SpaceRepoView { Did = Did.Parse(e.Did), Rev = Tid.Parse(e.Rev), Hash = e.Hash },
            repo => repo.Did.Value);

        return new ListSpaceReposResponse { Repos = repos, Cursor = next };
    }

    /// <inheritdoc/>
    public async Task RecordWriteAsync(
        SpaceUri space, Did repoDid, Tid rev, byte[] hash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repoDid);
        ArgumentNullException.ThrowIfNull(rev);
        ArgumentNullException.ThrowIfNull(hash);

        var spaceValue = space.Value;
        var did = repoDid.Value;
        var revValue = rev.Value;

        for (var attempt = 1; ; attempt++)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var writers = context.Set<SpaceWriterEntity>();

            var current = await writers
                .Where(e => e.Space == spaceValue && e.Did == did)
                .Select(e => e.Rev)
                .FirstOrDefaultAsync(cancellationToken);

            if (current is null)
            {
                // A first write. Two first notifications can both find no row and both insert;
                // the loser's insert violates the key, and its next attempt finds the row.
                await EnsureSpaceAsync(context, spaceValue, cancellationToken);
                writers.Add(new SpaceWriterEntity { Space = spaceValue, Did = did, Rev = revValue, Hash = hash });
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    return;
                }
                catch (DbUpdateException) when (attempt < MaxWriteAttempts)
                {
                    continue;
                }
            }

            // A notification that arrives out of order must not walk a repo's revision backwards;
            // a syncer reads the writer set to decide what advanced. TIDs order by their bytes,
            // which is compared here: in SQL the column's collation decides, and one that is not
            // ordinal (Danish, say, which sorts "aa" after "z") would order two TIDs the other way.
            if (string.CompareOrdinal(revValue, current) < 0)
                return;

            // Advance only from the revision just read, so a newer notification landing in between
            // is never overwritten by this one. Equality needs no collation to agree on an order.
            var updated = await writers
                .Where(e => e.Space == spaceValue && e.Did == did && e.Rev == current)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(e => e.Rev, revValue).SetProperty(e => e.Hash, hash),
                    cancellationToken);

            if (updated > 0)
                return;

            if (attempt >= MaxWriteAttempts)
            {
                throw new DbUpdateConcurrencyException(
                    $"The revision of '{did}' in '{spaceValue}' kept changing while recording '{revValue}'.");
            }
        }
    }

    // How often RecordWriteAsync re-reads a writer whose row changed under it. Each retry means
    // another notification for the same repo landed in between, so this is rarely more than one.
    private const int MaxWriteAttempts = 5;

    /// <inheritdoc/>
    public async Task RegisterNotifyAsync(
        SpaceUri space, string service, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var spaceValue = space.Value;

        // A renewal, the common case, is one statement.
        await using (var context = await _contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var updated = await context.Set<SpaceSubscriberEntity>()
                .Where(e => e.Space == spaceValue && e.Service == service)
                .ExecuteUpdateAsync(set => set.SetProperty(e => e.ExpiresAt, expiresAt), cancellationToken);

            if (updated > 0)
                return;
        }

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

        var spaceValue = space.Value;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Set<SpaceSubscriberEntity>()
            .Where(e => e.Space == spaceValue && e.Service == service)
            .ExecuteDeleteAsync(cancellationToken);
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
