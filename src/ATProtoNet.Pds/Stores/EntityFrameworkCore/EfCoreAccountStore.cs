using Microsoft.EntityFrameworkCore;

namespace ATProtoNet.Pds.EntityFrameworkCore;

/// <summary>
/// EF Core-backed implementation of <see cref="IAccountStore"/>.
/// Persists accounts in a relational database so they survive process restarts and
/// can be shared across load-balanced PDS instances.
/// </summary>
/// <remarks>
/// <para>Handle and email lookups are case-insensitive, matching
/// <see cref="InMemoryAccountStore"/>. They are translated to SQL as a
/// <c>LOWER(column) = @p</c> comparison, which most providers can only serve from an
/// expression index — add one in a migration if your account table is large. When the
/// columns are encrypted at rest with a non-deterministic scheme, enable
/// <see cref="PdsEfCoreStoreOptions.ClientSideAccountLookup"/> and the comparison moves
/// into memory.</para>
/// <para>Use
/// <see cref="PdsEfCoreStoreExtensions.AddAtProtoPdsEfCoreStores{TContext}(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{PdsEfCoreStoreOptions}?)"/>
/// to register this store with dependency injection.</para>
/// </remarks>
/// <typeparam name="TContext">
/// A <see cref="DbContext"/> containing a <see cref="DbSet{TEntity}"/> for
/// <see cref="PdsAccountEntity"/>. Use <see cref="PdsDbContext"/> or your own context
/// with the entity configured via <see cref="PdsDbContext.ConfigurePdsModel"/>.
/// </typeparam>
public sealed class EfCoreAccountStore<TContext> : IAccountStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly PdsEfCoreStoreOptions _options;

    /// <summary>
    /// Creates a new <see cref="EfCoreAccountStore{TContext}"/>.
    /// </summary>
    /// <param name="contextFactory">Factory for the backing <see cref="DbContext"/>.</param>
    /// <param name="options">Store options, or <see langword="null"/> for the defaults.</param>
    public EfCoreAccountStore(
        IDbContextFactory<TContext> contextFactory,
        PdsEfCoreStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);

        _contextFactory = contextFactory;
        _options = options ?? new PdsEfCoreStoreOptions();
    }

    /// <inheritdoc />
    public async Task CreateAsync(PdsAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var accounts = context.Set<PdsAccountEntity>();

        if (await accounts.AnyAsync(e => e.Did == account.Did, cancellationToken))
            throw new InvalidOperationException($"Account with DID {account.Did} already exists.");

        accounts.Add(ToEntity(account));
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PdsAccount?> GetByDidAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<PdsAccountEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Did == did, cancellationToken);

        return entity is null ? null : ToAccount(entity);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Runs as a case-insensitive SQL comparison, or as an in-memory scan when
    /// <see cref="PdsEfCoreStoreOptions.ClientSideAccountLookup"/> is enabled.
    /// </remarks>
    public async Task<PdsAccount?> GetByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(handle)) return null;

        if (_options.ClientSideAccountLookup)
        {
            var found = await FindClientSideAsync(
                e => string.Equals(e.Handle, handle, StringComparison.OrdinalIgnoreCase),
                cancellationToken);
            return found is null ? null : ToAccount(found);
        }

        var normalized = handle.ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<PdsAccountEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Handle.ToLower() == normalized, cancellationToken);

        return entity is null ? null : ToAccount(entity);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Runs as a case-insensitive SQL comparison, or as an in-memory scan when
    /// <see cref="PdsEfCoreStoreOptions.ClientSideAccountLookup"/> is enabled.
    /// </remarks>
    public async Task<PdsAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(email)) return null;

        if (_options.ClientSideAccountLookup)
        {
            var found = await FindClientSideAsync(
                e => e.Email is not null && string.Equals(e.Email, email, StringComparison.OrdinalIgnoreCase),
                cancellationToken);
            return found is null ? null : ToAccount(found);
        }

        var normalized = email.ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<PdsAccountEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Email != null && e.Email.ToLower() == normalized, cancellationToken);

        return entity is null ? null : ToAccount(entity);
    }

    /// <inheritdoc />
    /// <remarks>Upserts: an account that does not exist yet is inserted.</remarks>
    public async Task UpdateAsync(PdsAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var accounts = context.Set<PdsAccountEntity>();

        var existing = await accounts.FirstOrDefaultAsync(e => e.Did == account.Did, cancellationToken);
        if (existing is null)
        {
            accounts.Add(ToEntity(account));
        }
        else
        {
            existing.Handle = account.Handle;
            existing.Email = account.Email;
            existing.EmailConfirmed = account.EmailConfirmed;
            existing.PasswordHash = account.PasswordHash;
            existing.IsActive = account.IsActive;
            existing.RotationKey = account.RotationKey;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var accounts = context.Set<PdsAccountEntity>();

        var existing = await accounts.FirstOrDefaultAsync(e => e.Did == did, cancellationToken);
        if (existing is null) return;

        accounts.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> HandleExistsAsync(string handle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(handle)) return false;

        if (_options.ClientSideAccountLookup)
        {
            var found = await FindClientSideAsync(
                e => string.Equals(e.Handle, handle, StringComparison.OrdinalIgnoreCase),
                cancellationToken);
            return found is not null;
        }

        var normalized = handle.ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<PdsAccountEntity>()
            .AnyAsync(e => e.Handle.ToLower() == normalized, cancellationToken);
    }

    /// <summary>
    /// Streams accounts out of the database and returns the first match, decrypting each
    /// row through whatever value converters the context configures. Used when the
    /// columns being compared cannot be matched in SQL.
    /// </summary>
    private async Task<PdsAccountEntity?> FindClientSideAsync(
        Func<PdsAccountEntity, bool> predicate,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Set<PdsAccountEntity>()
            .AsNoTracking()
            .OrderBy(e => e.Did)
            .AsQueryable();

        if (_options.MaxClientSideLookupRows is { } max)
            query = query.Take(max);

        await foreach (var entity in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (predicate(entity))
                return entity;
        }

        return null;
    }

    private static PdsAccountEntity ToEntity(PdsAccount account) => new()
    {
        Did = account.Did,
        Handle = account.Handle,
        Email = account.Email,
        EmailConfirmed = account.EmailConfirmed,
        PasswordHash = account.PasswordHash,
        CreatedAt = account.CreatedAt,
        IsActive = account.IsActive,
        SigningKey = account.SigningKey,
        RotationKey = account.RotationKey,
    };

    private static PdsAccount ToAccount(PdsAccountEntity entity) => new()
    {
        Did = entity.Did,
        Handle = entity.Handle,
        Email = entity.Email,
        EmailConfirmed = entity.EmailConfirmed,
        PasswordHash = entity.PasswordHash,
        CreatedAt = entity.CreatedAt,
        IsActive = entity.IsActive,
        SigningKey = entity.SigningKey,
        RotationKey = entity.RotationKey,
    };
}
