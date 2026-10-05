using System.Data.Common;
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
/// <c>UPDATE</c> and no read. <c>notifyWrite</c> takes the next space revision in one transaction: the
/// space row holds the last one assigned and is updated conditioned on the value just read, so
/// concurrent writers of a space serialize and commit in revision order. Repo revisions are compared
/// here rather than in SQL, because a SQL comparison follows the column's collation and a TID's order
/// is its bytes' only under some collations.</para>
/// <para><c>listRepos</c> pages by space revision, a checkpoint rather than an offset. The ordering and
/// the cursor comparison are both evaluated by the database, so the <c>SpaceRev</c> column needs an
/// ordinal (binary) collation for them to follow revision order: under a collation like Danish, which
/// sorts "aa" after "z", a syncer's checkpoint could skip a repo.</para>
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

    /// <summary>Creates the store.</summary>
    /// <param name="contextFactory">Supplies a context per operation.</param>
    public EfCoreSpaceAuthorityStore(IDbContextFactory<TContext> contextFactory)
        : this(contextFactory, TimeProvider.System)
    {
    }

    /// <summary>Creates the store, reading the current time from <paramref name="timeProvider"/>.</summary>
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
            (context, ct) => EnsureSpaceAsync(context, space.Value, ct), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Marks a space deleted, so it answers <see cref="SpaceErrors.SpaceDeleted"/>. Idempotent.</summary>
    public async Task MarkDeletedAsync(SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        var spaceValue = space.Value;
        var updated = await _contextFactory.UseAsync((context, ct) => context.Set<SpaceEntity>()
            .Where(e => e.Space == spaceValue)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.Deleted, true), ct), cancellationToken).ConfigureAwait(false);

        if (updated > 0)
            return;

        await MutateAsync(async (context, ct) =>
        {
            var entity = await context.Set<SpaceEntity>().FindAsync([space.Value], ct).ConfigureAwait(false);

            if (entity is null)
                context.Add(new SpaceEntity { Space = space.Value, Deleted = true });
            else
                entity.Deleted = true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<SpaceAccessOutcome> GetSpaceStateAsync(
        SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        var entity = await _contextFactory.UseAsync((context, ct) => context.Set<SpaceEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Space == space.Value, ct), cancellationToken).ConfigureAwait(false);

        if (entity is null)
            return SpaceAccessOutcome.SpaceNotFound;

        return entity.Deleted ? SpaceAccessOutcome.SpaceDeleted : SpaceAccessOutcome.Granted;
    }

    /// <inheritdoc/>
    public async Task<ListSpaceReposResponse> ListReposAsync(
        SpaceUri space, int limit, string? cursor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        // One row past the page, so the presence of a next page is known without a count.
        var page = await _contextFactory.UseAsync((context, ct) =>
        {
            // Filtered only with a cursor, so the first page's SQL has no "@cursor IS NULL OR" to defeat the index.
            var query = context.Set<SpaceWriterEntity>().AsNoTracking().Where(e => e.Space == space.Value);
            if (cursor is not null)
                query = query.Where(e => string.Compare(e.SpaceRev, cursor) > 0);
            return query.OrderBy(e => e.SpaceRev).Take(limit + 1).ToListAsync(ct);
        }, cancellationToken).ConfigureAwait(false);

        var (repos, next) = SpacePaging.CheckpointPage(
            page,
            limit,
            e => new SpaceRepoView { Did = Did.Parse(e.Did), RepoRev = Tid.Parse(e.RepoRev), Hash = e.Hash, SpaceRev = Tid.Parse(e.SpaceRev) },
            repo => repo.SpaceRev.Value);

        return new ListSpaceReposResponse { Repos = repos, Cursor = next };
    }

    /// <inheritdoc/>
    public async Task<SpaceWriteSequence?> RecordWriteAsync(
        SpaceUri space, Did repoDid, Tid repoRev, byte[] hash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(repoDid);
        ArgumentNullException.ThrowIfNull(repoRev);
        ArgumentNullException.ThrowIfNull(hash);

        for (var attempt = 1; ; attempt++)
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var contextScope = context.ConfigureAwait(false);
            var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var transactionScope = transaction.ConfigureAwait(false);

            try
            {
                var (sequence, stale) = await TrySequenceAsync(context, space.Value, repoDid.Value, repoRev, hash, cancellationToken).ConfigureAwait(false);
                if (stale)
                    return null;

                if (sequence is not null)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return sequence;
                }
            }
            catch (Exception ex) when (ex is DbUpdateException or DbException && attempt < MaxWriteAttempts)
            {
                // Two first notifications can both find no row and both insert (DbUpdateException), or the
                // database refused a concurrent claim: a serialization failure, deadlock or busy database,
                // which ExecuteUpdate surfaces as the provider's own DbException. The loser's next attempt,
                // after a short jittered pause, reads what the winner wrote.
                await Task.Delay(Random.Shared.Next(1, 10 * attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (attempt >= MaxWriteAttempts)
                throw new DbUpdateConcurrencyException(
                    $"The sequence of '{space.Value}' kept changing while recording '{repoRev}' for '{repoDid}'.");
        }
    }

    // One attempt at recording a write: Stale when the repo already stands at that revision or past it,
    // and a null sequence when another writer got there first and the attempt must start over.
    private static async Task<(SpaceWriteSequence? Sequence, bool Stale)> TrySequenceAsync(
        TContext context, string space, string did, Tid repoRev, byte[] hash, CancellationToken cancellationToken)
    {
        var repoRevValue = repoRev.Value;

        // A notification that arrives out of order must not walk a repo's revision backwards, and one that
        // repeats the recorded revision changes nothing. TIDs order by their bytes, which is compared here:
        // in SQL the column's collation decides, and one that is not ordinal (Danish, say, which sorts "aa"
        // after "z") would order two TIDs the other way.
        var current = await context.Set<SpaceWriterEntity>()
            .Where(e => e.Space == space && e.Did == did)
            .Select(e => e.RepoRev)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (current is not null && string.CompareOrdinal(repoRevValue, current) <= 0)
            return (null, true);

        var sequencer = await context.Set<SpaceEntity>()
            .Where(e => e.Space == space)
            .Select(e => new { e.LastSpaceRev })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var previousValue = sequencer?.LastSpaceRev;
        var previous = previousValue is null ? null : Tid.Parse(previousValue);
        var spaceRev = SpacePaging.NextSpaceRev(previous);
        var spaceRevValue = spaceRev.Value;

        // Claiming the revision is an update conditioned on the one just read, so a concurrent writer of
        // the space either waits for this transaction or finds the condition false: writers of a space
        // are serialized and revisions commit in the order they were taken. Without that, a writer could
        // commit a lower revision after a syncer had checkpointed a higher one, and listRepos would
        // never show it.
        if (sequencer is null)
            context.Add(new SpaceEntity { Space = space, LastSpaceRev = spaceRevValue });
        else if (await context.Set<SpaceEntity>()
            .Where(e => e.Space == space && e.LastSpaceRev == previousValue)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.LastSpaceRev, spaceRevValue), cancellationToken).ConfigureAwait(false) == 0)
            return (null, false);

        if (current is null)
            context.Add(new SpaceWriterEntity { Space = space, Did = did, RepoRev = repoRevValue, SpaceRev = spaceRevValue, Hash = hash });
        else if (await context.Set<SpaceWriterEntity>()
            .Where(e => e.Space == space && e.Did == did && e.RepoRev == current)
            .ExecuteUpdateAsync(
                set => set.SetProperty(e => e.RepoRev, repoRevValue).SetProperty(e => e.SpaceRev, spaceRevValue).SetProperty(e => e.Hash, hash),
                cancellationToken).ConfigureAwait(false) == 0)
            return (null, false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (new SpaceWriteSequence(spaceRev, previous), false);
    }

    // How often RecordWriteAsync starts over when another writer got there first. Each retry means a
    // notification for the same space landed in between, so it is rarely more than one or two.
    private const int MaxWriteAttempts = 8;

    /// <inheritdoc/>
    public async Task RegisterNotifyAsync(
        SpaceUri space, string service, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var spaceValue = space.Value;

        // A renewal, the common case, is one statement.
        var updated = await _contextFactory.UseAsync((context, ct) => context.Set<SpaceSubscriberEntity>()
            .Where(e => e.Space == spaceValue && e.Service == service)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.ExpiresAt, expiresAt), ct), cancellationToken).ConfigureAwait(false);

        if (updated > 0)
            return;

        await MutateAsync(async (context, ct) =>
        {
            await EnsureSpaceAsync(context, space.Value, ct).ConfigureAwait(false);

            var subscribers = context.Set<SpaceSubscriberEntity>();
            var existing = await subscribers.FindAsync([space.Value, service], ct).ConfigureAwait(false);

            if (existing is null)
                subscribers.Add(new SpaceSubscriberEntity { Space = space.Value, Service = service, ExpiresAt = expiresAt });
            else
                existing.ExpiresAt = expiresAt;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UnregisterNotifyAsync(
        SpaceUri space, string service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var spaceValue = space.Value;

        await _contextFactory.UseAsync((context, ct) => context.Set<SpaceSubscriberEntity>()
            .Where(e => e.Space == spaceValue && e.Service == service)
            .ExecuteDeleteAsync(ct), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SpaceNotifySubscriber>> ListSubscribersAsync(
        SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        var now = _timeProvider.GetUtcNow();

        var live = await _contextFactory.UseAsync((context, ct) => context.Set<SpaceSubscriberEntity>()
            .AsNoTracking()
            .Where(e => e.Space == space.Value && e.ExpiresAt > now)
            .OrderBy(e => e.Service)
            .ToListAsync(ct), cancellationToken).ConfigureAwait(false);

        return live.Select(e => new SpaceNotifySubscriber(e.Service, e.ExpiresAt)).ToList();
    }

    // Adds the space row when it is missing, mirroring the in-memory store's behaviour of declaring a
    // space on first use. The endpoints check GetSpaceStateAsync before writing, so in practice this only
    // matters to a caller driving the store directly.
    private static async Task EnsureSpaceAsync(
        TContext context, string space, CancellationToken cancellationToken)
    {
        var spaces = context.Set<SpaceEntity>();
        if (await spaces.FindAsync([space], cancellationToken).ConfigureAwait(false) is not null)
            return;

        spaces.Add(new SpaceEntity { Space = space });
    }

    // Applies a mutation and saves it, retrying once on a failed save.
    //
    // Every write here is an upsert done as read-then-insert-or-update, so two instances acting on the
    // same row at once can both find nothing and both insert. The retry runs the whole mutation again on a
    // fresh context, which now sees the winner's row and takes the update path. A second failure is not a
    // race — a value too long for its column fails identically both times — so it propagates rather than
    // being swallowed.
    private async Task MutateAsync(
        Func<TContext, CancellationToken, Task> mutate, CancellationToken cancellationToken)
    {
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var contextScope = context.ConfigureAwait(false);
            await mutate(context, cancellationToken).ConfigureAwait(false);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (DbUpdateException)
        {
            // Fall through to the retry, on a context that never saw the failed change.
        }

        var retry = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var retryScope = retry.ConfigureAwait(false);
        await mutate(retry, cancellationToken).ConfigureAwait(false);
        await retry.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
