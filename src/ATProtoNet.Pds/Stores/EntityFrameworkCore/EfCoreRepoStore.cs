using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ATProtoNet.Pds.EntityFrameworkCore;

/// <summary>
/// EF Core-backed implementation of <see cref="IRepoStore"/>.
/// Persists repository records and blobs in a relational database.
/// </summary>
/// <remarks>
/// <para>Record listings page on <c>rkey</c> with a keyset (seek) cursor rather than an
/// offset, so paging cost does not grow with the page number. The cursor is the last
/// returned <c>rkey</c> and is exclusive, matching <see cref="InMemoryRepoStore"/>.
/// Ordering follows the database collation for the <c>Rkey</c> column; AT Protocol record
/// keys are ASCII, so any ASCII-compatible collation orders them identically to the
/// in-memory store.</para>
/// <para>Blobs are content-addressed and de-duplicated: the bytes live in one row keyed
/// by CID, and each uploading account gets a reference row. Deleting a blob removes the
/// caller's reference, and the shared content only when the last reference is gone — so
/// one account deleting a blob cannot destroy another account's copy.</para>
/// <para>Use
/// <see cref="PdsEfCoreStoreExtensions.AddAtProtoPdsEfCoreStores{TContext}(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{PdsEfCoreStoreOptions}?)"/>
/// to register this store with dependency injection.</para>
/// </remarks>
/// <typeparam name="TContext">
/// A <see cref="DbContext"/> containing <see cref="DbSet{TEntity}"/>s for
/// <see cref="PdsRecordEntity"/>, <see cref="PdsBlobEntity"/>, and
/// <see cref="PdsBlobRefEntity"/>. Use <see cref="PdsDbContext"/> or your own context
/// with the entities configured via <see cref="PdsDbContext.ConfigurePdsModel"/>.
/// </typeparam>
public sealed class EfCoreRepoStore<TContext> : IRepoStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;

    /// <summary>
    /// Creates a new <see cref="EfCoreRepoStore{TContext}"/>.
    /// </summary>
    /// <param name="contextFactory">Factory for the backing <see cref="DbContext"/>.</param>
    public EfCoreRepoStore(IDbContextFactory<TContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    // ── Records ──

    /// <inheritdoc />
    public async Task PutRecordAsync(RepoRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var records = context.Set<PdsRecordEntity>();

        var existing = await records.FirstOrDefaultAsync(
            e => e.Did == record.Did && e.Collection == record.Collection && e.Rkey == record.Rkey,
            cancellationToken);

        if (existing is null)
        {
            records.Add(new PdsRecordEntity
            {
                Did = record.Did,
                Collection = record.Collection,
                Rkey = record.Rkey,
                Value = record.Value.GetRawText(),
                Cid = record.Cid,
                IndexedAt = record.IndexedAt,
            });
        }
        else
        {
            existing.Value = record.Value.GetRawText();
            existing.Cid = record.Cid;
            existing.IndexedAt = record.IndexedAt;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RepoRecord?> GetRecordAsync(string did, string collection, string rkey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(rkey);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<PdsRecordEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.Did == did && e.Collection == collection && e.Rkey == rkey,
                cancellationToken);

        return entity is null ? null : ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task<RecordPage> ListRecordsAsync(string did, string collection,
        int limit = 50, string? cursor = null, bool reverse = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);

        if (limit <= 0)
            return new RecordPage { Records = [], Cursor = null };

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Set<PdsRecordEntity>()
            .AsNoTracking()
            .Where(e => e.Did == did && e.Collection == collection);

        // Keyset pagination: the cursor is the last rkey of the previous page and is
        // exclusive, so the seek predicate matches the sort direction.
        if (reverse)
        {
            if (!string.IsNullOrEmpty(cursor))
                query = query.Where(e => e.Rkey.CompareTo(cursor) < 0);
            query = query.OrderByDescending(e => e.Rkey);
        }
        else
        {
            if (!string.IsNullOrEmpty(cursor))
                query = query.Where(e => e.Rkey.CompareTo(cursor) > 0);
            query = query.OrderBy(e => e.Rkey);
        }

        var page = await query.Take(limit).ToListAsync(cancellationToken);

        return new RecordPage
        {
            Records = page.Select(ToRecord).ToList(),
            Cursor = page.Count == limit ? page[^1].Rkey : null,
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Loads the whole repository, then orders it in memory by the ordinal MST key
    /// (<c>collection/rkey</c>) rather than in SQL: <c>ORDER BY Collection, Rkey</c> is not the
    /// same ordering — <c>"a.b.c/x"</c> sorts before <c>"a.b/y"</c> because <c>'.'</c> precedes
    /// <c>'/'</c> — and a database collation need not be ordinal either. The MST is a function
    /// of its key/value set, so a mismatch would not corrupt the tree, but the sort makes the
    /// result byte-identical to <see cref="InMemoryRepoStore"/>'s.
    /// </remarks>
    public async Task<IReadOnlyList<RepoRecord>> ListAllRecordsAsync(
        string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.Set<PdsRecordEntity>()
            .AsNoTracking()
            .Where(e => e.Did == did)
            .ToListAsync(cancellationToken);

        return entities
            .Select(ToRecord)
            .OrderBy(r => $"{r.Collection}/{r.Rkey}", StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListBlobCidsAsync(
        string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cids = await context.Set<PdsBlobRefEntity>()
            .AsNoTracking()
            .Where(e => e.Did == did)
            .Select(e => e.Cid)
            .ToListAsync(cancellationToken);

        // Ordinal here too, so the listing does not depend on the column's collation.
        cids.Sort(StringComparer.Ordinal);
        return cids;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteRecordAsync(string did, string collection, string rkey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(rkey);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var records = context.Set<PdsRecordEntity>();

        var existing = await records.FirstOrDefaultAsync(
            e => e.Did == did && e.Collection == collection && e.Rkey == rkey,
            cancellationToken);

        if (existing is null) return false;

        records.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Deletes the account's records and blob references, then the blob contents that no
    /// other account still references.
    /// </remarks>
    public async Task DeleteAllAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Delete by key without materializing record values or blob bytes.
        var recordKeys = await context.Set<PdsRecordEntity>()
            .AsNoTracking()
            .Where(e => e.Did == did)
            .Select(e => new { e.Collection, e.Rkey })
            .ToListAsync(cancellationToken);

        foreach (var key in recordKeys)
        {
            context.Remove(new PdsRecordEntity
            {
                Did = did,
                Collection = key.Collection,
                Rkey = key.Rkey,
                Value = "{}",
                Cid = "",
            });
        }

        var blobCids = await context.Set<PdsBlobRefEntity>()
            .AsNoTracking()
            .Where(e => e.Did == did)
            .Select(e => e.Cid)
            .ToListAsync(cancellationToken);

        foreach (var cid in blobCids)
        {
            context.Remove(new PdsBlobRefEntity { Did = did, Cid = cid, MimeType = "" });
        }

        await context.SaveChangesAsync(cancellationToken);

        if (blobCids.Count > 0)
            await CollectOrphanedBlobsAsync(context, blobCids, cancellationToken);
    }

    // ── Blobs ──

    /// <inheritdoc />
    /// <remarks>
    /// <para>The blob bytes are stored once per CID and shared between accounts that upload
    /// identical content; this call adds or updates the caller's reference to them.</para>
    /// <para>The "does this content already exist" check and the insert are not one atomic
    /// statement, so two accounts uploading identical bytes concurrently — the case dedup
    /// exists to handle — can both decide to insert and one will lose on the primary key.
    /// That loss is retried once against a fresh context: the winner stored the same
    /// content-addressed bytes, so the retry finds the row and only writes its own reference.
    /// One retry is enough, because after it neither row can still be missing (content is
    /// shared and the reference is keyed by this DID, so no third writer can insert it).</para>
    /// </remarks>
    public async Task PutBlobAsync(RepoBlob blob, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blob);

        for (var attempt = 0; ; attempt++)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var contentExists = await context.Set<PdsBlobEntity>()
                .AnyAsync(e => e.Cid == blob.Cid, cancellationToken);

            if (!contentExists)
            {
                context.Set<PdsBlobEntity>().Add(new PdsBlobEntity
                {
                    Cid = blob.Cid,
                    Size = blob.Size,
                    Data = blob.Data,
                });
            }

            var existingRef = await context.Set<PdsBlobRefEntity>()
                .FirstOrDefaultAsync(e => e.Did == blob.Did && e.Cid == blob.Cid, cancellationToken);

            if (existingRef is null)
            {
                context.Set<PdsBlobRefEntity>().Add(new PdsBlobRefEntity
                {
                    Did = blob.Did,
                    Cid = blob.Cid,
                    MimeType = blob.MimeType,
                });
            }
            else
            {
                existingRef.MimeType = blob.MimeType;
            }

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Lost the insert race (or the reference row moved under us). Re-read and retry.
            }
        }
    }

    /// <inheritdoc />
    public async Task<RepoBlob?> GetBlobAsync(string did, string cid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        ArgumentException.ThrowIfNullOrWhiteSpace(cid);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var result = await context.Set<PdsBlobRefEntity>()
            .AsNoTracking()
            .Where(r => r.Did == did && r.Cid == cid)
            .Join(context.Set<PdsBlobEntity>().AsNoTracking(),
                r => r.Cid, b => b.Cid,
                (r, b) => new { r.MimeType, b.Size, b.Data })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null) return null;

        return new RepoBlob
        {
            Did = did,
            Cid = cid,
            MimeType = result.MimeType,
            Size = result.Size,
            Data = result.Data,
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Removes this account's reference to the blob. The shared content row is removed
    /// only once no account references it any more.
    /// </remarks>
    public async Task<bool> DeleteBlobAsync(string did, string cid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        ArgumentException.ThrowIfNullOrWhiteSpace(cid);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var existingRef = await context.Set<PdsBlobRefEntity>()
            .FirstOrDefaultAsync(e => e.Did == did && e.Cid == cid, cancellationToken);

        if (existingRef is null) return false;

        context.Set<PdsBlobRefEntity>().Remove(existingRef);
        await context.SaveChangesAsync(cancellationToken);

        await CollectOrphanedBlobsAsync(context, [cid], cancellationToken);
        return true;
    }

    /// <summary>
    /// Deletes blob content rows among <paramref name="cids"/> that no reference points at
    /// any more. Content rows are removed through key-only stubs so the bytes are never
    /// loaded just to delete them.
    /// </summary>
    /// <remarks>
    /// Collection is best-effort and never fails the delete that triggered it. Another writer
    /// may collect the same orphan first (the row is already gone — the outcome we wanted), or
    /// re-reference the content between the check and the delete, which the foreign key from
    /// <see cref="PdsBlobRefEntity"/> then refuses. Either way the content ends up in the right
    /// state, and at worst an unreferenced row survives until the next delete touching that CID.
    /// </remarks>
    private static async Task CollectOrphanedBlobsAsync(
        TContext context, IReadOnlyCollection<string> cids, CancellationToken cancellationToken)
    {
        var stillReferenced = await context.Set<PdsBlobRefEntity>()
            .AsNoTracking()
            .Where(e => cids.Contains(e.Cid))
            .Select(e => e.Cid)
            .Distinct()
            .ToListAsync(cancellationToken);

        var orphaned = cids.Distinct(StringComparer.Ordinal).Except(stillReferenced, StringComparer.Ordinal).ToList();
        if (orphaned.Count == 0) return;

        // Only the CID matters for the DELETE; Size/Data are never read back.
        foreach (var cid in orphaned)
            context.Remove(new PdsBlobEntity { Cid = cid, Size = 0, Data = [] });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Already collected, or referenced again since the check above.
        }
    }

    private static RepoRecord ToRecord(PdsRecordEntity entity)
    {
        using var document = JsonDocument.Parse(entity.Value);

        return new RepoRecord
        {
            Did = entity.Did,
            Collection = entity.Collection,
            Rkey = entity.Rkey,
            // Clone so the value outlives the JsonDocument we just parsed it from.
            Value = document.RootElement.Clone(),
            Cid = entity.Cid,
            IndexedAt = entity.IndexedAt,
        };
    }
}
