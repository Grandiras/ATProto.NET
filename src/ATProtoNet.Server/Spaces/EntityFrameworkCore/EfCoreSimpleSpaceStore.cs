using System.Text.Json;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Serialization;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using Microsoft.EntityFrameworkCore;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="ISimpleSpaceStore"/>: the spaces a
/// <c>com.atproto.simplespace</c> authority hosts, and their member lists, in a relational
/// database.
/// </summary>
/// <remarks>
/// <para>A member list is never published to the network and cannot be rebuilt from anything on
/// it, so a durable store is not optional for an authority that means to survive a restart —
/// losing one loses the space's access control, and the space keeps existing without it.</para>
/// <para>The two policies are stored as the JSON of their Lexicon union variants, discriminator
/// and all, so a variant added later needs no schema change.</para>
/// <para>Register with
/// <see cref="SpaceStoreExtensions.AddAtProtoEfCoreSimpleSpace{TContext}"/>.</para>
/// </remarks>
/// <typeparam name="TContext">
/// A <see cref="DbContext"/> carrying the <c>simplespace</c> entities. Use
/// <see cref="SpaceDbContext"/>, or your own context configured with
/// <see cref="SpaceDbContext.ConfigureSimpleSpaceModel"/>.
/// </typeparam>
public sealed class EfCoreSimpleSpaceStore<TContext> : ISimpleSpaceStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Creates the store.
    /// </summary>
    /// <param name="contextFactory">Supplies a context per operation.</param>
    public EfCoreSimpleSpaceStore(IDbContextFactory<TContext> contextFactory)
        : this(contextFactory, AtProtoJsonDefaults.Options)
    {
    }

    /// <summary>
    /// Creates the store, serializing the stored policies with the given options.
    /// </summary>
    /// <param name="contextFactory">Supplies a context per operation.</param>
    /// <param name="jsonOptions">
    /// The options the policy unions are read and written with. Supply the same ones the rest of
    /// the application uses if it has registered union variants of its own.
    /// </param>
    public EfCoreSimpleSpaceStore(IDbContextFactory<TContext> contextFactory, JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        _contextFactory = contextFactory;
        _jsonOptions = jsonOptions;
    }

    /// <inheritdoc/>
    public async Task<SimpleSpaceRecord?> GetSpaceAsync(
        SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<SimpleSpaceEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Space == space.Value, cancellationToken);

        return entity is null ? null : ToRecord(entity);
    }

    /// <inheritdoc/>
    public async Task<bool> CreateSpaceAsync(
        SimpleSpaceRecord space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var spaces = context.Set<SimpleSpaceEntity>();

        if (await spaces.FindAsync([space.Uri.Value], cancellationToken) is not null)
            return false;

        spaces.Add(new SimpleSpaceEntity
        {
            Space = space.Uri.Value,
            Owner = space.Owner,
            Policy = JsonSerializer.Serialize(space.Policy, _jsonOptions),
            AppAccess = JsonSerializer.Serialize(space.AppAccess, _jsonOptions),
            Deleted = space.Deleted,
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Two creates of the same URI raced. The key violation is the answer the caller
            // wants: exactly one of them created the space, and this was not it.
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task UpdateSpaceAsync(SimpleSpaceRecord space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<SimpleSpaceEntity>().FindAsync([space.Uri.Value], cancellationToken);

        // A space that is not there is left alone rather than created, matching the in-memory
        // store: an update reaches here only through an endpoint that already loaded it.
        if (entity is null)
            return;

        entity.Owner = space.Owner;
        entity.Policy = JsonSerializer.Serialize(space.Policy, _jsonOptions);
        entity.AppAccess = JsonSerializer.Serialize(space.AppAccess, _jsonOptions);
        entity.Deleted = space.Deleted;

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteSpaceAsync(SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<SimpleSpaceEntity>().FindAsync([space.Value], cancellationToken);

        // Flagged rather than removed: a deleted space keeps answering SpaceDeleted, which is how
        // a syncer that missed the notification learns to drop its copy.
        if (entity is null || entity.Deleted)
            return;

        entity.Deleted = true;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddMemberAsync(SpaceUri space, string did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // A member list belongs to a space; adding to one that does not exist is a no-op rather
        // than a row nothing would ever read.
        if (await context.Set<SimpleSpaceEntity>().FindAsync([space.Value], cancellationToken) is null)
            return;

        var members = context.Set<SimpleSpaceMemberEntity>();
        if (await members.FindAsync([space.Value, did], cancellationToken) is not null)
            return;

        members.Add(new SimpleSpaceMemberEntity { Space = space.Value, Did = did });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Adding a member is idempotent, and the racing add stored the same row.
        }
    }

    /// <inheritdoc/>
    public async Task RemoveMemberAsync(SpaceUri space, string did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var members = context.Set<SimpleSpaceMemberEntity>();
        var entity = await members.FindAsync([space.Value, did], cancellationToken);

        if (entity is null)
            return;

        members.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> IsMemberAsync(
        SpaceUri space, string did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<SimpleSpaceMemberEntity>()
            .AsNoTracking()
            .AnyAsync(e => e.Space == space.Value && e.Did == did, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ListSimpleSpaceMembersResponse> ListMembersAsync(
        SpaceUri space, int limit, string? cursor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Set<SimpleSpaceMemberEntity>()
            .AsNoTracking()
            .Where(e => e.Space == space.Value);

        if (cursor is not null)
            query = query.Where(e => string.Compare(e.Did, cursor) > 0);

        // One row past the page, so the presence of a next page is known without a count.
        var page = await query
            .OrderBy(e => e.Did)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > limit;
        var members = page.Take(limit).Select(e => new SimpleSpaceMember { Did = e.Did }).ToList();

        return new ListSimpleSpaceMembersResponse
        {
            Members = members,
            Cursor = hasMore && members.Count > 0 ? members[^1].Did : null,
        };
    }

    private SimpleSpaceRecord ToRecord(SimpleSpaceEntity entity)
    {
        var policy = JsonSerializer.Deserialize<SimpleSpaceUserPolicy>(entity.Policy, _jsonOptions)
            ?? throw new InvalidOperationException(
                $"The stored user policy for '{entity.Space}' could not be read.");
        var appAccess = JsonSerializer.Deserialize<SimpleSpaceAppAccess>(entity.AppAccess, _jsonOptions)
            ?? throw new InvalidOperationException(
                $"The stored app access policy for '{entity.Space}' could not be read.");

        return new SimpleSpaceRecord(
            SpaceUri.Parse(entity.Space), entity.Owner, policy, appAccess, entity.Deleted);
    }
}
