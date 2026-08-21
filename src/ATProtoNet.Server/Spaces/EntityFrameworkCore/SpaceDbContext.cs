using Microsoft.EntityFrameworkCore;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// DbContext for the space server's durable state: the authority's writer set and notification
/// registrations, the <c>com.atproto.simplespace</c> spaces and member lists, and the
/// single-use-token replay table.
/// </summary>
/// <remarks>
/// <para>Use this context as-is, or add the entities to a context you already have by calling
/// <see cref="ConfigureSpaceModel(ModelBuilder)"/> — or one of the three narrower
/// <c>Configure…</c> methods — from your own <c>OnModelCreating</c>. A service that is an
/// authority but not a repo host, or that keeps its replay entries in Redis, only needs the
/// parts it registers.</para>
/// <para>The string columns are sized for real-world DIDs, NSIDs, and record keys rather than
/// for the protocol's theoretical maxima, because they are index keys: a provider with a tight
/// index-key limit (SQL Server's 900 bytes) rejects wider ones. Narrow or widen them from your
/// own <c>OnModelCreating</c> after calling the configuration methods if your deployment needs
/// something else.</para>
/// </remarks>
public class SpaceDbContext : DbContext
{
    /// <summary>The spaces this authority gates.</summary>
    public DbSet<SpaceEntity> AtProtoSpaces => Set<SpaceEntity>();

    /// <summary>The writer sets of the spaces this authority gates.</summary>
    public DbSet<SpaceWriterEntity> AtProtoSpaceWriters => Set<SpaceWriterEntity>();

    /// <summary>The services registered for write notifications.</summary>
    public DbSet<SpaceSubscriberEntity> AtProtoSpaceSubscribers => Set<SpaceSubscriberEntity>();

    /// <summary>The <c>com.atproto.simplespace</c> spaces.</summary>
    public DbSet<SimpleSpaceEntity> AtProtoSimpleSpaces => Set<SimpleSpaceEntity>();

    /// <summary>The <c>com.atproto.simplespace</c> member lists.</summary>
    public DbSet<SimpleSpaceMemberEntity> AtProtoSimpleSpaceMembers => Set<SimpleSpaceMemberEntity>();

    /// <summary>The single-use token identifiers already spent.</summary>
    public DbSet<SpaceReplayEntity> AtProtoSpaceReplay => Set<SpaceReplayEntity>();

    /// <summary>Creates a new <see cref="SpaceDbContext"/>.</summary>
    /// <param name="options">The context options.</param>
    public SpaceDbContext(DbContextOptions<SpaceDbContext> options) : base(options)
    {
    }

    /// <summary>Creates a new instance with generic options (for derived contexts).</summary>
    /// <param name="options">The context options.</param>
    protected SpaceDbContext(DbContextOptions options) : base(options)
    {
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureSpaceModel(modelBuilder);
    }

    /// <summary>
    /// Applies every space entity configuration: the authority's, <c>simplespace</c>'s, and the
    /// replay table's.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void ConfigureSpaceModel(ModelBuilder modelBuilder)
    {
        ConfigureSpaceAuthorityModel(modelBuilder);
        ConfigureSimpleSpaceModel(modelBuilder);
        ConfigureSpaceReplayModel(modelBuilder);
    }

    /// <summary>
    /// Applies the configuration <see cref="EfCoreSpaceAuthorityStore{TContext}"/> needs: the
    /// spaces, their writer sets, and their notification registrations.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void ConfigureSpaceAuthorityModel(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SpaceEntity>(entity =>
        {
            entity.ToTable("AtProtoSpaces");
            entity.HasKey(e => e.Space);
            entity.Property(e => e.Space).HasMaxLength(512);
            entity.Property(e => e.Deleted);
        });

        modelBuilder.Entity<SpaceWriterEntity>(entity =>
        {
            entity.ToTable("AtProtoSpaceWriters");
            entity.HasKey(e => new { e.Space, e.Did });
            entity.Property(e => e.Space).HasMaxLength(512);
            entity.Property(e => e.Did).HasMaxLength(512);
            entity.Property(e => e.Rev).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Hash).IsRequired();
        });

        modelBuilder.Entity<SpaceSubscriberEntity>(entity =>
        {
            entity.ToTable("AtProtoSpaceSubscribers");
            entity.HasKey(e => new { e.Space, e.Service });
            entity.Property(e => e.Space).HasMaxLength(512);
            entity.Property(e => e.Service).HasMaxLength(512);

            // Stored as Unix milliseconds. A DateTimeOffset column is ordered differently by
            // every provider — SQLite cannot compare one at all — and lapsed registrations are
            // filtered by an inequality on this. The conversion is order-preserving, and it
            // normalizes to UTC: an expiry set with an offset reads back as the same instant.
            entity.Property(e => e.ExpiresAt).HasConversion(
                value => value.ToUnixTimeMilliseconds(),
                value => DateTimeOffset.FromUnixTimeMilliseconds(value));
        });
    }

    /// <summary>
    /// Applies the configuration <see cref="EfCoreSimpleSpaceStore{TContext}"/> needs: the
    /// spaces and their member lists.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void ConfigureSimpleSpaceModel(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SimpleSpaceEntity>(entity =>
        {
            entity.ToTable("AtProtoSimpleSpaces");
            entity.HasKey(e => e.Space);
            entity.Property(e => e.Space).HasMaxLength(512);
            entity.Property(e => e.Owner).HasMaxLength(512).IsRequired();
            entity.Property(e => e.Policy).IsRequired();
            entity.Property(e => e.AppAccess).IsRequired();
            entity.Property(e => e.Deleted);

            // Listing a member page is always scoped to one space, and so is every access check.
            entity.HasIndex(e => e.Owner);
        });

        modelBuilder.Entity<SimpleSpaceMemberEntity>(entity =>
        {
            entity.ToTable("AtProtoSimpleSpaceMembers");
            entity.HasKey(e => new { e.Space, e.Did });
            entity.Property(e => e.Space).HasMaxLength(512);
            entity.Property(e => e.Did).HasMaxLength(512);
        });
    }

    /// <summary>
    /// Applies the configuration <see cref="EfCoreSpaceReplayStore{TContext}"/> needs.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void ConfigureSpaceReplayModel(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SpaceReplayEntity>(entity =>
        {
            entity.ToTable("AtProtoSpaceReplay");
            entity.HasKey(e => new { e.Issuer, e.TokenId, e.ExpiresAt });
            entity.Property(e => e.Issuer).HasMaxLength(512);
            entity.Property(e => e.TokenId).HasMaxLength(255);
            entity.Property(e => e.ExpiresAt);

            // The sweep deletes by expiry across every issuer, so it needs its own index.
            entity.HasIndex(e => e.ExpiresAt);
        });
    }
}
