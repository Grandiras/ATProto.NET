using System.ComponentModel.DataAnnotations;
using ATProtoNet.Streaming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// One repository's Sync 1.1 state, as <see cref="EfCoreRepoSyncStateStore{TContext}"/> keeps it.
/// </summary>
public sealed class RepoSyncStateEntity
{
    /// <summary>The repository's DID. The primary key.</summary>
    [MaxLength(2048)]
    public required string Did { get; set; }

    /// <summary>The last revision seen, a TID, or null when not known.</summary>
    [MaxLength(13)]
    public string? Rev { get; set; }

    /// <summary>The MST root CID of <see cref="Rev"/>, or null when not known.</summary>
    [MaxLength(64)]
    public string? Data { get; set; }

    /// <summary>Whether the repository's chain of commits is intact.</summary>
    public RepoSyncStatus Status { get; set; }

    /// <summary>
    /// When the row last changed, as Unix milliseconds: repositories to fetch are listed oldest
    /// first, so none waits behind a stream of newer ones.
    /// </summary>
    public long UpdatedAt { get; set; }
}

/// <summary>
/// A DbContext holding only the table behind <see cref="EfCoreRepoSyncStateStore{TContext}"/>, for
/// a service with no other EF Core state.
/// </summary>
/// <remarks>
/// To keep the table in a context you already have, call
/// <see cref="ConfigureRepoSyncStateModel(ModelBuilder)"/> from its <c>OnModelCreating</c>.
/// </remarks>
public class RepoSyncStateDbContext : DbContext
{
    /// <summary>Each repository's sync state.</summary>
    public DbSet<RepoSyncStateEntity> AtProtoRepoSyncStates => Set<RepoSyncStateEntity>();

    /// <summary>Creates a new <see cref="RepoSyncStateDbContext"/>.</summary>
    /// <param name="options">The context options.</param>
    public RepoSyncStateDbContext(DbContextOptions<RepoSyncStateDbContext> options) : base(options)
    {
    }

    /// <summary>Creates a new instance with generic options (for derived contexts).</summary>
    /// <param name="options">The context options.</param>
    protected RepoSyncStateDbContext(DbContextOptions options) : base(options)
    {
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureRepoSyncStateModel(modelBuilder);
    }

    /// <summary>
    /// Applies the configuration <see cref="EfCoreRepoSyncStateStore{TContext}"/> needs: the
    /// <c>AtProtoRepoSyncStates</c> table.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void ConfigureRepoSyncStateModel(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<RepoSyncStateEntity>(entity =>
        {
            entity.ToTable("AtProtoRepoSyncStates");
            entity.HasKey(e => e.Did);
            entity.Property(e => e.Did).HasMaxLength(2048);
            entity.Property(e => e.Rev).HasMaxLength(13);
            entity.Property(e => e.Data).HasMaxLength(64);

            // Stored by name, so a row reads the same whatever order the enum is declared in.
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);

            // Resynchronization lists the repositories that are not synchronized, oldest first.
            entity.HasIndex(e => new { e.Status, e.UpdatedAt });
        });
    }
}

/// <summary>
/// Registers the EF Core-backed <see cref="IRepoSyncStateStore"/>.
/// </summary>
public static class RepoSyncStateStoreExtensions
{
    /// <summary>
    /// Registers <see cref="EfCoreRepoSyncStateStore{TContext}"/> as the <see cref="IRepoSyncStateStore"/>,
    /// so each repository's sync state survives restarts. Pass it to
    /// <see cref="RepoSyncVerifierOptions.StateStore"/>.
    /// </summary>
    /// <typeparam name="TContext">
    /// A <see cref="DbContext"/> configured with
    /// <see cref="RepoSyncStateDbContext.ConfigureRepoSyncStateModel"/>:
    /// <see cref="RepoSyncStateDbContext"/> or your own.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Register the context's <c>IDbContextFactory</c> yourself
    /// (<c>AddDbContextFactory&lt;TContext&gt;</c>): the store opens a context per operation.
    /// </remarks>
    public static IServiceCollection AddAtProtoEfCoreRepoSyncStateStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<IRepoSyncStateStore, EfCoreRepoSyncStateStore<TContext>>());
        return services;
    }
}
