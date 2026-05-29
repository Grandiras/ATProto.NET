using Microsoft.EntityFrameworkCore;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// DbContext for AT Protocol token storage.
/// Add this context to your application's EF Core configuration,
/// or use <see cref="AtProtoTokenStoreExtensions.AddAtProtoEfCoreTokenStore{TContext}"/>
/// for automatic registration.
/// </summary>
/// <remarks>
/// <para>To use this with your own DbContext, add a <see cref="DbSet{TEntity}"/>
/// for <see cref="AtProtoTokenEntity"/> and call
/// <see cref="OnModelCreating(ModelBuilder)"/> from your context's <c>OnModelCreating</c>.</para>
/// </remarks>
public class AtProtoTokenDbContext : DbContext
{
    /// <summary>
    /// The stored AT Protocol tokens.
    /// </summary>
    public DbSet<AtProtoTokenEntity> AtProtoTokens => Set<AtProtoTokenEntity>();

    /// <summary>
    /// Creates a new <see cref="AtProtoTokenDbContext"/>.
    /// </summary>
    public AtProtoTokenDbContext(DbContextOptions<AtProtoTokenDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Creates a new instance with generic options (for derived contexts).
    /// </summary>
    protected AtProtoTokenDbContext(DbContextOptions options) : base(options)
    {
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAtProtoTokenModel(modelBuilder);
    }

    /// <summary>
    /// Applies the AT Protocol token entity configuration to the given model builder.
    /// Call this from your own DbContext's <c>OnModelCreating</c> if you are adding
    /// the token table to an existing database context instead of using the standalone
    /// <see cref="AtProtoTokenDbContext"/>.
    /// </summary>
    public static void ConfigureAtProtoTokenModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AtProtoTokenEntity>(entity =>
        {
            entity.ToTable("AtProtoTokens");
            entity.HasKey(e => e.Did);
            entity.Property(e => e.Did).HasMaxLength(2048);
            entity.Property(e => e.EncryptedTokenData).IsRequired();
            entity.Property(e => e.UpdatedAt);
        });
    }
}
