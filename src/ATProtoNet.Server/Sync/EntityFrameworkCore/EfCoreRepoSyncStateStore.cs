using ATProtoNet.Identity;
using ATProtoNet.Streaming;
using Microsoft.EntityFrameworkCore;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IRepoSyncStateStore"/>: each repository's Sync 1.1 state in a
/// relational table, so a firehose consumer resumes its chains after a restart.
/// </summary>
/// <remarks>
/// <para>The verifier reads a repository's state for every commit and writes it once the commit
/// is processed: two round trips per event, which a busy network firehose outpaces. Track the
/// repositories you care about, or keep state in memory and persist it in batches, at full-network
/// scale.</para>
/// <para>Register it with
/// <see cref="RepoSyncStateStoreExtensions.AddAtProtoEfCoreRepoSyncStateStore{TContext}"/>.</para>
/// </remarks>
/// <typeparam name="TContext">
/// A <see cref="DbContext"/> carrying <see cref="RepoSyncStateEntity"/>: <see cref="RepoSyncStateDbContext"/>,
/// or your own context configured with <see cref="RepoSyncStateDbContext.ConfigureRepoSyncStateModel"/>.
/// </typeparam>
public sealed class EfCoreRepoSyncStateStore<TContext> : IRepoSyncStateStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the store.
    /// </summary>
    /// <param name="contextFactory">Supplies a context per operation.</param>
    public EfCoreRepoSyncStateStore(IDbContextFactory<TContext> contextFactory)
        : this(contextFactory, TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates the store, stamping rows with the time from <paramref name="timeProvider"/>.
    /// </summary>
    /// <param name="contextFactory">Supplies a context per operation.</param>
    /// <param name="timeProvider">The clock rows are stamped with.</param>
    public EfCoreRepoSyncStateStore(IDbContextFactory<TContext> contextFactory, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _contextFactory = contextFactory;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async ValueTask<RepoSyncState?> GetAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<RepoSyncStateEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Did == did.Value, cancellationToken);

        return entity is null ? null : ToState(entity);
    }

    /// <inheritdoc/>
    public async ValueTask SetAsync(RepoSyncState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var set = context.Set<RepoSyncStateEntity>();
        var entity = await set.FirstOrDefaultAsync(e => e.Did == state.Did.Value, cancellationToken);
        if (entity is null)
        {
            entity = new RepoSyncStateEntity { Did = state.Did.Value };
            set.Add(entity);
        }

        Copy(state, entity);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (context.Entry(entity).State == EntityState.Added)
        {
            // Another writer inserted the row between the read and the insert: update it instead.
            context.ChangeTracker.Clear();
            var existing = await set.FirstOrDefaultAsync(e => e.Did == state.Did.Value, cancellationToken);
            if (existing is null)
                throw;

            Copy(state, existing);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async ValueTask RemoveAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var set = context.Set<RepoSyncStateEntity>();
        if (await set.FirstOrDefaultAsync(e => e.Did == did.Value, cancellationToken) is { } entity)
        {
            set.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<RepoSyncState>> ListUnsynchronizedAsync(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.Set<RepoSyncStateEntity>()
            .AsNoTracking()
            .Where(e => e.Status != RepoSyncStatus.Synchronized)
            .OrderBy(e => e.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. entities.Select(ToState)];
    }

    private void Copy(RepoSyncState state, RepoSyncStateEntity entity)
    {
        entity.Rev = state.Rev?.Value;
        entity.Data = state.Data?.Value;
        entity.Status = state.Status;
        entity.UpdatedAt = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
    }

    private static RepoSyncState ToState(RepoSyncStateEntity entity) => new(
        Did.Parse(entity.Did),
        Tid.TryParse(entity.Rev, out var rev) ? rev : null,
        Cid.TryParse(entity.Data, out var data) ? data : null,
        entity.Status);
}
