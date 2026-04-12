using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// EF Core-backed implementation of <see cref="IAtProtoTokenStore"/>.
/// Stores OAuth tokens in a relational database with encryption at rest
/// using ASP.NET Core Data Protection.
/// </summary>
/// <remarks>
/// <para>Suitable for multi-server / load-balanced deployments where all instances
/// share the same database. Ensure that the Data Protection key ring is also shared
/// across instances (e.g., via Azure Blob Storage, a shared file system, or a database).</para>
/// <para>Use <see cref="AtProtoTokenStoreExtensions.AddAtProtoEfCoreTokenStore{TContext}"/>
/// to register this store with dependency injection.</para>
/// </remarks>
/// <typeparam name="TContext">
/// A <see cref="DbContext"/> that contains a <see cref="DbSet{TEntity}"/>
/// for <see cref="AtProtoTokenEntity"/>. Use <see cref="AtProtoTokenDbContext"/>
/// or your own context with the entity configured.
/// </typeparam>
public sealed class EfCoreAtProtoTokenStore<TContext> : IAtProtoTokenStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly IDataProtector _protector;
    private readonly ILogger<EfCoreAtProtoTokenStore<TContext>> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Creates a new <see cref="EfCoreAtProtoTokenStore{TContext}"/>.
    /// </summary>
    public EfCoreAtProtoTokenStore(
        IDbContextFactory<TContext> contextFactory,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<EfCoreAtProtoTokenStore<TContext>> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        _contextFactory = contextFactory;
        _protector = dataProtectionProvider.CreateProtector("ATProtoNet.TokenStore");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task StoreAsync(string did, AtProtoTokenData data, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        ArgumentNullException.ThrowIfNull(data);

        var json = JsonSerializer.Serialize(data, JsonOptions);
        var encrypted = _protector.Protect(json);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var tokens = context.Set<AtProtoTokenEntity>();

        var existing = await tokens.FindAsync([did], cancellationToken);
        if (existing is not null)
        {
            existing.EncryptedTokenData = encrypted;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            tokens.Add(new AtProtoTokenEntity
            {
                Did = did,
                EncryptedTokenData = encrypted,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogDebug("Stored tokens for DID {Did}", did);
    }

    /// <inheritdoc/>
    public async Task<AtProtoTokenData?> GetAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<AtProtoTokenEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Did == did, cancellationToken);

        if (entity is null)
            return null;

        try
        {
            var json = _protector.Unprotect(entity.EncryptedTokenData);
            return JsonSerializer.Deserialize<AtProtoTokenData>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to decrypt token data for DID {Did}; removing corrupted entry", did);
            await RemoveAsync(did, cancellationToken);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<AtProtoTokenEntity>().FindAsync([did], cancellationToken);

        if (entity is not null)
        {
            context.Set<AtProtoTokenEntity>().Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Removed tokens for DID {Did}", did);
        }
    }
}
