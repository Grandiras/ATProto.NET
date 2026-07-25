using Microsoft.EntityFrameworkCore;

namespace ATProtoNet.Pds.EntityFrameworkCore;

/// <summary>
/// DbContext for PDS account, record, blob, and repository-head storage.
/// Add this context to your application's EF Core configuration, or use
/// <see cref="PdsEfCoreStoreExtensions.AddAtProtoPdsEfCoreStores{TContext}(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{PdsEfCoreStoreOptions}?)"/>
/// for automatic store registration.
/// </summary>
/// <remarks>
/// <para>To use these tables inside your own DbContext instead, add a
/// <see cref="DbSet{TEntity}"/> for <see cref="PdsAccountEntity"/>,
/// <see cref="PdsRecordEntity"/>, <see cref="PdsBlobEntity"/>,
/// <see cref="PdsBlobRefEntity"/>, and <see cref="PdsRepoHeadEntity"/>, then call
/// <see cref="ConfigurePdsModel"/> from your context's <c>OnModelCreating</c>.</para>
/// </remarks>
public class PdsDbContext : DbContext
{
    /// <summary>The stored PDS accounts.</summary>
    public DbSet<PdsAccountEntity> PdsAccounts => Set<PdsAccountEntity>();

    /// <summary>The stored repository records.</summary>
    public DbSet<PdsRecordEntity> PdsRecords => Set<PdsRecordEntity>();

    /// <summary>The stored blob contents, keyed by CID.</summary>
    public DbSet<PdsBlobEntity> PdsBlobs => Set<PdsBlobEntity>();

    /// <summary>The per-account references to stored blobs.</summary>
    public DbSet<PdsBlobRefEntity> PdsBlobRefs => Set<PdsBlobRefEntity>();

    /// <summary>The signed head of each hosted repository.</summary>
    public DbSet<PdsRepoHeadEntity> PdsRepoHeads => Set<PdsRepoHeadEntity>();

    /// <summary>
    /// Creates a new <see cref="PdsDbContext"/>.
    /// </summary>
    public PdsDbContext(DbContextOptions<PdsDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Creates a new instance with generic options (for derived contexts).
    /// </summary>
    protected PdsDbContext(DbContextOptions options) : base(options)
    {
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigurePdsModel(modelBuilder);
    }

    /// <summary>
    /// Applies the PDS entity configuration to the given model builder.
    /// Call this from your own DbContext's <c>OnModelCreating</c> if you are adding
    /// the PDS tables to an existing database context instead of using the standalone
    /// <see cref="PdsDbContext"/>.
    /// </summary>
    public static void ConfigurePdsModel(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<PdsAccountEntity>(entity =>
        {
            entity.ToTable("PdsAccounts");
            entity.HasKey(e => e.Did);
            entity.Property(e => e.Did).HasMaxLength(2048);
            entity.Property(e => e.Handle).HasMaxLength(512).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(512);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.SigningKey).IsRequired();

            // Non-unique: a provider's collation decides whether "Alice.test" and
            // "alice.test" collide, and the store compares case-insensitively itself.
            // Enforce uniqueness in a migration if your collation makes that safe.
            entity.HasIndex(e => e.Handle);
            entity.HasIndex(e => e.Email);
        });

        modelBuilder.Entity<PdsRecordEntity>(entity =>
        {
            entity.ToTable("PdsRecords");
            entity.HasKey(e => new { e.Did, e.Collection, e.Rkey });
            entity.Property(e => e.Did).HasMaxLength(2048);
            entity.Property(e => e.Collection).HasMaxLength(512);
            entity.Property(e => e.Rkey).HasMaxLength(512);
            entity.Property(e => e.Value).IsRequired();
            entity.Property(e => e.Cid).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<PdsBlobEntity>(entity =>
        {
            entity.ToTable("PdsBlobs");
            entity.HasKey(e => e.Cid);
            entity.Property(e => e.Cid).HasMaxLength(256);
            entity.Property(e => e.Data).IsRequired();
        });

        modelBuilder.Entity<PdsBlobRefEntity>(entity =>
        {
            entity.ToTable("PdsBlobRefs");
            entity.HasKey(e => new { e.Did, e.Cid });
            entity.Property(e => e.Did).HasMaxLength(2048);
            entity.Property(e => e.Cid).HasMaxLength(256);
            entity.Property(e => e.MimeType).HasMaxLength(256).IsRequired();

            entity.HasIndex(e => e.Cid);

            // Restrict, not cascade: the reference rows are what keeps shared content alive,
            // so the database refuses to drop content another account still points at. That
            // closes the window where one account's delete runs between another's
            // "content already exists" check and its reference insert.
            entity.HasOne<PdsBlobEntity>()
                .WithMany()
                .HasForeignKey(e => e.Cid)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PdsRepoHeadEntity>(entity =>
        {
            entity.ToTable("PdsRepoHeads");
            entity.HasKey(e => e.Did);
            entity.Property(e => e.Did).HasMaxLength(2048);
            entity.Property(e => e.CommitCid).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Rev).HasMaxLength(64).IsRequired();
            entity.Property(e => e.DataCid).HasMaxLength(256).IsRequired();
            entity.Property(e => e.CommitBlock).IsRequired();
        });
    }
}
