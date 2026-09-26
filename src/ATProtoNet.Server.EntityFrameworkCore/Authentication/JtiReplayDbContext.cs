using System.ComponentModel.DataAnnotations;
using ATProtoNet.Server.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// A single-use token identifier this service has already accepted.
/// </summary>
/// <remarks>
/// The primary key is <c>(Issuer, TokenId, ExpiresAt)</c>, matching how
/// <see cref="IJtiReplayStore"/> is keyed: the uniqueness of that key <em>is</em> the replay
/// check, so consuming a token is one insert with no read-modify-write.
/// </remarks>
public sealed class JtiReplayEntity
{
    /// <summary>
    /// What scopes the identifier: the token's <c>iss</c>, or a DPoP key's thumbprint. Part of the
    /// composite primary key.
    /// </summary>
    [MaxLength(512)]
    public required string Issuer { get; set; }

    /// <summary>The token's <c>jti</c>. Part of the composite primary key.</summary>
    [MaxLength(255)]
    public required string TokenId { get; set; }

    /// <summary>
    /// The token's expiry as Unix seconds. Part of the composite primary key, and what expired
    /// rows are swept on.
    /// </summary>
    /// <remarks>
    /// Held as a number rather than a timestamp so the key is byte-identical across providers
    /// that store <see cref="DateTimeOffset"/> differently, and so the sweep is a plain integer
    /// comparison.
    /// </remarks>
    public long ExpiresAt { get; set; }
}

/// <summary>
/// A DbContext holding only the replay table behind
/// <see cref="EfCoreJtiReplayStore{TContext}"/>, for a service with no other EF Core state.
/// </summary>
/// <remarks>
/// To keep the table in a context you already have, call
/// <see cref="ConfigureJtiReplayModel(ModelBuilder)"/> from its <c>OnModelCreating</c>.
/// <see cref="SpaceDbContext"/> already does.
/// </remarks>
public class JtiReplayDbContext : DbContext
{
    /// <summary>The single-use token identifiers already spent.</summary>
    public DbSet<JtiReplayEntity> AtProtoJtiReplay => Set<JtiReplayEntity>();

    /// <summary>Creates a new <see cref="JtiReplayDbContext"/>.</summary>
    /// <param name="options">The context options.</param>
    public JtiReplayDbContext(DbContextOptions<JtiReplayDbContext> options) : base(options)
    {
    }

    /// <summary>Creates a new instance with generic options (for derived contexts).</summary>
    /// <param name="options">The context options.</param>
    protected JtiReplayDbContext(DbContextOptions options) : base(options)
    {
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureJtiReplayModel(modelBuilder);
    }

    /// <summary>
    /// Applies the configuration <see cref="EfCoreJtiReplayStore{TContext}"/> needs: the
    /// <c>AtProtoJtiReplay</c> table.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void ConfigureJtiReplayModel(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<JtiReplayEntity>(entity =>
        {
            entity.ToTable("AtProtoJtiReplay");
            entity.HasKey(e => new { e.Issuer, e.TokenId, e.ExpiresAt });
            entity.Property(e => e.Issuer).HasMaxLength(512);
            entity.Property(e => e.TokenId).HasMaxLength(255);
            entity.Property(e => e.ExpiresAt);

            // The sweep deletes by expiry across every issuer, so it needs its own index.
            entity.HasIndex(e => e.ExpiresAt);
        });
    }
}

/// <summary>
/// Registers the EF Core-backed <see cref="IJtiReplayStore"/>.
/// </summary>
public static class JtiReplayStoreExtensions
{
    /// <summary>
    /// Replaces the in-process <see cref="IJtiReplayStore"/> with an EF Core-backed one, so
    /// single-use tokens are spent once across every instance rather than once per process.
    /// </summary>
    /// <typeparam name="TContext">
    /// A <see cref="DbContext"/> configured with
    /// <see cref="JtiReplayDbContext.ConfigureJtiReplayModel"/>: <see cref="JtiReplayDbContext"/>,
    /// <see cref="SpaceDbContext"/>, or your own.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Register the context's <c>IDbContextFactory</c> yourself
    /// (<c>AddDbContextFactory&lt;TContext&gt;</c>): the store opens a context per operation. The
    /// one store serves service auth and the space server alike, whichever registers first.
    /// </remarks>
    public static IServiceCollection AddAtProtoEfCoreJtiReplayStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // Replace rather than TryAdd: AddAtProtoServiceAuth() or AddAtProtoSpaces() may already
        // have registered the in-process default, and this call is the deployment saying it
        // wants the shared one.
        services.Replace(ServiceDescriptor.Singleton<IJtiReplayStore, EfCoreJtiReplayStore<TContext>>());

        return services;
    }
}
