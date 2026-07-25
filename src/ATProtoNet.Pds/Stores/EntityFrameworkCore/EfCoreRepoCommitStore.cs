using Microsoft.EntityFrameworkCore;

namespace ATProtoNet.Pds.EntityFrameworkCore;

/// <summary>
/// EF Core-backed implementation of <see cref="IRepoCommitStore"/>.
/// Persists the signed head of each hosted repository.
/// </summary>
/// <remarks>
/// <para>A federating PDS needs this to survive a restart: <see cref="InMemoryRepoCommitStore"/>
/// forgets every head, and the next commit then starts a fresh revision sequence, which relays
/// read as the repository rewinding.</para>
/// <para><see cref="ListAsync"/> pages on the DID with a keyset (seek) cursor, exclusive of the
/// cursor value, matching <see cref="InMemoryRepoCommitStore"/>. Ordering follows the database
/// collation for the <c>Did</c> column; DIDs are ASCII, so any ASCII-compatible collation orders
/// them identically to the in-memory store.</para>
/// <para>Use
/// <see cref="PdsEfCoreStoreExtensions.AddAtProtoPdsEfCoreStores{TContext}(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{PdsEfCoreStoreOptions}?)"/>
/// to register this store with dependency injection.</para>
/// </remarks>
/// <typeparam name="TContext">
/// A <see cref="DbContext"/> containing a <see cref="DbSet{TEntity}"/> for
/// <see cref="PdsRepoHeadEntity"/>. Use <see cref="PdsDbContext"/> or your own context
/// with the entity configured via <see cref="PdsDbContext.ConfigurePdsModel"/>.
/// </typeparam>
public sealed class EfCoreRepoCommitStore<TContext> : IRepoCommitStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;

    /// <summary>
    /// Creates a new <see cref="EfCoreRepoCommitStore{TContext}"/>.
    /// </summary>
    /// <param name="contextFactory">Factory for the backing <see cref="DbContext"/>.</param>
    public EfCoreRepoCommitStore(IDbContextFactory<TContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<RepoCommitState?> GetAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<PdsRepoHeadEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Did == did, cancellationToken);

        return entity is null ? null : ToState(entity);
    }

    /// <inheritdoc />
    /// <remarks>Upserts: one row per repository, replaced on every commit.</remarks>
    public async Task SetAsync(RepoCommitState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var heads = context.Set<PdsRepoHeadEntity>();

        var existing = await heads.FirstOrDefaultAsync(e => e.Did == state.Did, cancellationToken);

        if (existing is null)
        {
            heads.Add(new PdsRepoHeadEntity
            {
                Did = state.Did,
                CommitCid = state.CommitCid,
                Rev = state.Rev,
                DataCid = state.DataCid,
                CommitBlock = state.CommitBlock,
                CreatedAt = state.CreatedAt,
            });
        }
        else
        {
            existing.CommitCid = state.CommitCid;
            existing.Rev = state.Rev;
            existing.DataCid = state.DataCid;
            existing.CommitBlock = state.CommitBlock;
            existing.CreatedAt = state.CreatedAt;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepoCommitState>> ListAsync(
        int limit, string? cursor, CancellationToken cancellationToken = default)
    {
        if (limit <= 0) return [];

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Set<PdsRepoHeadEntity>()
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrEmpty(cursor))
            query = query.Where(e => e.Did.CompareTo(cursor) > 0);

        var page = await query
            .OrderBy(e => e.Did)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return page.Select(ToState).ToList();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var heads = context.Set<PdsRepoHeadEntity>();

        var existing = await heads.FirstOrDefaultAsync(e => e.Did == did, cancellationToken);
        if (existing is null) return;

        heads.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static RepoCommitState ToState(PdsRepoHeadEntity entity) => new()
    {
        Did = entity.Did,
        CommitCid = entity.CommitCid,
        Rev = entity.Rev,
        DataCid = entity.DataCid,
        CommitBlock = entity.CommitBlock,
        CreatedAt = entity.CreatedAt,
    };
}
