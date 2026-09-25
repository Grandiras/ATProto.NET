using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// EF Core-backed implementation of <see cref="IAtProtoSessionStore"/>.
/// Stores sessions in a relational database with encryption at rest
/// using ASP.NET Core Data Protection.
/// </summary>
/// <remarks>
/// <para>Suitable for multi-server / load-balanced deployments where all instances
/// share the same database. Ensure that the Data Protection key ring is also shared
/// across instances (e.g., via Azure Blob Storage, a shared file system, or a database).</para>
/// <para>Each session is one <see cref="AtProtoTokenEntity"/> row holding the encrypted session
/// JSON; rows written by the 0.6 token store are read as OAuth sessions.</para>
/// <para>Use <see cref="AtProtoTokenStoreExtensions.AddAtProtoEfCoreSessionStore{TContext}"/>
/// to register this store with dependency injection.</para>
/// </remarks>
/// <typeparam name="TContext">
/// A <see cref="DbContext"/> that contains a <see cref="DbSet{TEntity}"/>
/// for <see cref="AtProtoTokenEntity"/>. Use <see cref="AtProtoTokenDbContext"/>
/// or your own context with the entity configured.
/// </typeparam>
public sealed class EfCoreAtProtoSessionStore<TContext> : IAtProtoSessionStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly IDataProtector _protector;
    private readonly ILogger<EfCoreAtProtoSessionStore<TContext>> _logger;

    /// <summary>
    /// Creates a new <see cref="EfCoreAtProtoSessionStore{TContext}"/>.
    /// </summary>
    public EfCoreAtProtoSessionStore(
        IDbContextFactory<TContext> contextFactory,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<EfCoreAtProtoSessionStore<TContext>> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        _contextFactory = contextFactory;
        _protector = dataProtectionProvider.CreateProtector("ATProtoNet.TokenStore");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async ValueTask SetAsync(AtProtoSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var did = session.Did.Value;
        var encrypted = _protector.Protect(AtProtoSessionJson.Serialize(session));

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
        _logger.LogDebug("Stored the session of {Did}", did);
    }

    /// <inheritdoc/>
    public async ValueTask<AtProtoSession?> GetAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        var key = did.Value;
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<AtProtoTokenEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Did == key, cancellationToken);

        if (entity is null)
            return null;

        try
        {
            var json = _protector.Unprotect(entity.EncryptedTokenData);
            return AtProtoSessionJson.Deserialize(json);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or FormatException)
        {
            _logger.LogWarning(ex, "Failed to decrypt the session of {Did}; removing corrupted entry", did);
            await RemoveAsync(did, cancellationToken);
            return null;
        }
    }

    /// <inheritdoc/>
    public async ValueTask RemoveAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<AtProtoTokenEntity>().FindAsync([did.Value], cancellationToken);

        if (entity is not null)
        {
            context.Set<AtProtoTokenEntity>().Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Removed the session of {Did}", did);
        }
    }
}
