using System.Data.Common;
using System.Text.Json;
using ATProtoNet.Pds;
using ATProtoNet.Pds.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Tests.Pds;

/// <summary>
/// Store behaviour, run against every provider the stores are expected to work on.
/// The EF in-memory provider evaluates LINQ in memory, so the SQLite run is what proves
/// the queries actually translate to SQL.
/// </summary>
public abstract class EfCoreStoreTests : IAsyncLifetime
{
    private IDbContextFactory<PdsDbContext> _contextFactory = null!;
    private EfCoreAccountStore<PdsDbContext> _accounts = null!;
    private EfCoreRepoStore<PdsDbContext> _repos = null!;
    private EfCoreRepoCommitStore<PdsDbContext> _heads = null!;
    private protected readonly string _dbName = $"Pds_{Guid.NewGuid():N}";

    /// <summary>Points the builder at the provider under test.</summary>
    private protected abstract void ConfigureProvider(DbContextOptionsBuilder builder);

    /// <summary>
    /// Whether the provider surfaces a duplicate key as <see cref="DbUpdateException"/>, the
    /// contract the stores' race recovery is written against. The EF in-memory provider does
    /// not — it lets the raw <see cref="ArgumentException"/> out of its backing dictionary —
    /// so the concurrency tests only run against a real database.
    /// </summary>
    private protected virtual bool ReportsConstraintViolations => true;

    public async Task InitializeAsync()
    {
        var builder = new DbContextOptionsBuilder<PdsDbContext>();
        ConfigureProvider(builder);

        _contextFactory = new TestPdsDbContextFactory(builder.Options);

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        await ctx.Database.EnsureCreatedAsync();

        _accounts = new EfCoreAccountStore<PdsDbContext>(_contextFactory);
        _repos = new EfCoreRepoStore<PdsDbContext>(_contextFactory);
        _heads = new EfCoreRepoCommitStore<PdsDbContext>(_contextFactory);
    }

    public virtual Task DisposeAsync() => Task.CompletedTask;

    private static PdsAccount CreateAccount(
        string did = "did:plc:test",
        string handle = "alice.test",
        string? email = "alice@test.local") => new()
    {
        Did = did,
        Handle = handle,
        Email = email,
        PasswordHash = "hash",
        SigningKey = "testkey",
    };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static RepoRecord CreateRecord(string rkey, string did = "did:plc:test",
        string collection = "app.bsky.feed.post", string text = "hello") => new()
    {
        Did = did,
        Collection = collection,
        Rkey = rkey,
        Value = Json($$"""{"text":"{{text}}"}"""),
        Cid = $"cid-{rkey}",
    };

    private static RepoBlob CreateBlob(string did, string cid, byte[]? data = null,
        string mimeType = "image/png") => new()
    {
        Did = did,
        Cid = cid,
        MimeType = mimeType,
        Size = (data ?? [1, 2, 3]).Length,
        Data = data ?? [1, 2, 3],
    };

    // ── AccountStore ──

    [Fact]
    public async Task AccountStore_CreateAndGet_ByDid_RoundTrips()
    {
        var created = DateTimeOffset.UtcNow.AddDays(-3);
        await _accounts.CreateAsync(new PdsAccount
        {
            Did = "did:plc:test",
            Handle = "alice.test",
            Email = "alice@test.local",
            EmailConfirmed = true,
            PasswordHash = "hash",
            CreatedAt = created,
            IsActive = false,
            SigningKey = "testkey",
        });

        var fetched = await _accounts.GetByDidAsync("did:plc:test");

        Assert.NotNull(fetched);
        Assert.Equal("alice.test", fetched.Handle);
        Assert.Equal("alice@test.local", fetched.Email);
        Assert.True(fetched.EmailConfirmed);
        Assert.Equal("hash", fetched.PasswordHash);
        Assert.Equal(created, fetched.CreatedAt);
        Assert.False(fetched.IsActive);
        Assert.Equal("testkey", fetched.SigningKey);
    }

    [Fact]
    public async Task AccountStore_RotationKey_RoundTripsAndUpdates()
    {
        var account = CreateAccount();
        account.RotationKey = "rotation-1";
        await _accounts.CreateAsync(account);

        var fetched = await _accounts.GetByDidAsync("did:plc:test");
        Assert.Equal("rotation-1", fetched!.RotationKey);

        fetched.RotationKey = "rotation-2";
        await _accounts.UpdateAsync(fetched);
        Assert.Equal("rotation-2", (await _accounts.GetByDidAsync("did:plc:test"))!.RotationKey);

        fetched.RotationKey = null;
        await _accounts.UpdateAsync(fetched);
        Assert.Null((await _accounts.GetByDidAsync("did:plc:test"))!.RotationKey);
    }

    [Fact]
    public async Task AccountStore_GetByDid_ReturnsNull_WhenNotFound()
    {
        Assert.Null(await _accounts.GetByDidAsync("did:plc:missing"));
    }

    [Fact]
    public async Task AccountStore_Create_Throws_OnDuplicateDid()
    {
        await _accounts.CreateAsync(CreateAccount());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _accounts.CreateAsync(CreateAccount(handle: "other.test", email: "other@test.local")));
    }

    [Fact]
    public async Task AccountStore_GetByHandle_IsCaseInsensitive()
    {
        await _accounts.CreateAsync(CreateAccount());

        var fetched = await _accounts.GetByHandleAsync("ALICE.TEST");

        Assert.NotNull(fetched);
        Assert.Equal("did:plc:test", fetched.Did);
    }

    [Fact]
    public async Task AccountStore_GetByEmail_IsCaseInsensitive()
    {
        await _accounts.CreateAsync(CreateAccount());

        var fetched = await _accounts.GetByEmailAsync("Alice@Test.Local");

        Assert.NotNull(fetched);
        Assert.Equal("did:plc:test", fetched.Did);
    }

    [Fact]
    public async Task AccountStore_GetByEmail_ReturnsNull_ForAccountWithoutEmail()
    {
        await _accounts.CreateAsync(CreateAccount(email: null));

        Assert.Null(await _accounts.GetByEmailAsync("alice@test.local"));
    }

    [Fact]
    public async Task AccountStore_HandleExists_ReflectsStoredAccounts()
    {
        await _accounts.CreateAsync(CreateAccount());

        Assert.True(await _accounts.HandleExistsAsync("alice.test"));
        Assert.True(await _accounts.HandleExistsAsync("Alice.Test"));
        Assert.False(await _accounts.HandleExistsAsync("bob.test"));
    }

    [Fact]
    public async Task AccountStore_Update_PersistsMutableFields()
    {
        await _accounts.CreateAsync(CreateAccount());

        var account = await _accounts.GetByDidAsync("did:plc:test");
        account!.Handle = "alice2.test";
        account.Email = "alice2@test.local";
        account.EmailConfirmed = true;
        account.PasswordHash = "newhash";
        account.IsActive = false;
        await _accounts.UpdateAsync(account);

        var updated = await _accounts.GetByDidAsync("did:plc:test");
        Assert.Equal("alice2.test", updated!.Handle);
        Assert.Equal("alice2@test.local", updated.Email);
        Assert.True(updated.EmailConfirmed);
        Assert.Equal("newhash", updated.PasswordHash);
        Assert.False(updated.IsActive);

        // The old handle no longer resolves
        Assert.Null(await _accounts.GetByHandleAsync("alice.test"));
        Assert.NotNull(await _accounts.GetByHandleAsync("alice2.test"));
    }

    [Fact]
    public async Task AccountStore_Update_InsertsWhenMissing()
    {
        await _accounts.UpdateAsync(CreateAccount());

        Assert.NotNull(await _accounts.GetByDidAsync("did:plc:test"));
    }

    [Fact]
    public async Task AccountStore_Delete_RemovesAccount()
    {
        await _accounts.CreateAsync(CreateAccount());

        await _accounts.DeleteAsync("did:plc:test");

        Assert.Null(await _accounts.GetByDidAsync("did:plc:test"));
        Assert.Null(await _accounts.GetByHandleAsync("alice.test"));
        Assert.False(await _accounts.HandleExistsAsync("alice.test"));
    }

    [Fact]
    public async Task AccountStore_Delete_IsNoOp_WhenMissing()
    {
        await _accounts.DeleteAsync("did:plc:missing");
    }

    [Fact]
    public async Task AccountStore_ClientSideLookup_FindsHandleAndEmail()
    {
        var store = new EfCoreAccountStore<PdsDbContext>(
            _contextFactory, new PdsEfCoreStoreOptions { ClientSideAccountLookup = true });

        await store.CreateAsync(CreateAccount());
        await store.CreateAsync(CreateAccount("did:plc:bob", "bob.test", "bob@test.local"));

        Assert.Equal("did:plc:bob", (await store.GetByHandleAsync("BOB.TEST"))?.Did);
        Assert.Equal("did:plc:bob", (await store.GetByEmailAsync("Bob@Test.Local"))?.Did);
        Assert.True(await store.HandleExistsAsync("alice.test"));
        Assert.Null(await store.GetByHandleAsync("carol.test"));
    }

    [Fact]
    public async Task AccountStore_ClientSideLookup_StopsAtMaxRows()
    {
        var store = new EfCoreAccountStore<PdsDbContext>(
            _contextFactory,
            new PdsEfCoreStoreOptions { ClientSideAccountLookup = true, MaxClientSideLookupRows = 1 });

        // Ordered by DID, so did:plc:a is the only row scanned.
        await store.CreateAsync(CreateAccount("did:plc:a", "a.test", "a@test.local"));
        await store.CreateAsync(CreateAccount("did:plc:b", "b.test", "b@test.local"));

        Assert.NotNull(await store.GetByHandleAsync("a.test"));
        Assert.Null(await store.GetByHandleAsync("b.test"));
    }

    // ── RepoStore: records ──

    [Fact]
    public async Task RepoStore_PutAndGetRecord_RoundTripsValue()
    {
        var indexed = DateTimeOffset.UtcNow.AddMinutes(-5);
        await _repos.PutRecordAsync(new RepoRecord
        {
            Did = "did:plc:test",
            Collection = "app.bsky.feed.post",
            Rkey = "3k1",
            Value = Json("""{"text":"hello","nested":{"n":1}}"""),
            Cid = "cid1",
            IndexedAt = indexed,
        });

        var fetched = await _repos.GetRecordAsync("did:plc:test", "app.bsky.feed.post", "3k1");

        Assert.NotNull(fetched);
        Assert.Equal("cid1", fetched.Cid);
        Assert.Equal(indexed, fetched.IndexedAt);
        Assert.Equal("hello", fetched.Value.GetProperty("text").GetString());
        Assert.Equal(1, fetched.Value.GetProperty("nested").GetProperty("n").GetInt32());
    }

    [Fact]
    public async Task RepoStore_PutRecord_OverwritesExisting()
    {
        await _repos.PutRecordAsync(CreateRecord("3k1", text: "first"));
        await _repos.PutRecordAsync(CreateRecord("3k1", text: "second"));

        var fetched = await _repos.GetRecordAsync("did:plc:test", "app.bsky.feed.post", "3k1");
        Assert.Equal("second", fetched!.Value.GetProperty("text").GetString());

        var page = await _repos.ListRecordsAsync("did:plc:test", "app.bsky.feed.post");
        Assert.Single(page.Records);
    }

    [Fact]
    public async Task RepoStore_GetRecord_ReturnsNull_WhenNotFound()
    {
        Assert.Null(await _repos.GetRecordAsync("did:plc:test", "app.bsky.feed.post", "nope"));
    }

    [Fact]
    public async Task RepoStore_ListRecords_IsScopedToDidAndCollection()
    {
        await _repos.PutRecordAsync(CreateRecord("3k1"));
        await _repos.PutRecordAsync(CreateRecord("3k2", collection: "app.bsky.feed.like"));
        await _repos.PutRecordAsync(CreateRecord("3k3", did: "did:plc:other"));

        var page = await _repos.ListRecordsAsync("did:plc:test", "app.bsky.feed.post");

        Assert.Single(page.Records);
        Assert.Equal("3k1", page.Records[0].Rkey);
    }

    [Fact]
    public async Task RepoStore_ListRecords_PagesWithCursor()
    {
        foreach (var rkey in new[] { "3k1", "3k2", "3k3", "3k4", "3k5" })
            await _repos.PutRecordAsync(CreateRecord(rkey));

        var first = await _repos.ListRecordsAsync("did:plc:test", "app.bsky.feed.post", limit: 2);
        Assert.Equal(["3k1", "3k2"], first.Records.Select(r => r.Rkey));
        Assert.Equal("3k2", first.Cursor);

        var second = await _repos.ListRecordsAsync("did:plc:test", "app.bsky.feed.post",
            limit: 2, cursor: first.Cursor);
        Assert.Equal(["3k3", "3k4"], second.Records.Select(r => r.Rkey));
        Assert.Equal("3k4", second.Cursor);

        var third = await _repos.ListRecordsAsync("did:plc:test", "app.bsky.feed.post",
            limit: 2, cursor: second.Cursor);
        Assert.Equal(["3k5"], third.Records.Select(r => r.Rkey));
        Assert.Null(third.Cursor);
    }

    [Fact]
    public async Task RepoStore_ListRecords_ReversePagesDescending()
    {
        foreach (var rkey in new[] { "3k1", "3k2", "3k3" })
            await _repos.PutRecordAsync(CreateRecord(rkey));

        var first = await _repos.ListRecordsAsync("did:plc:test", "app.bsky.feed.post",
            limit: 2, reverse: true);
        Assert.Equal(["3k3", "3k2"], first.Records.Select(r => r.Rkey));
        Assert.Equal("3k2", first.Cursor);

        var second = await _repos.ListRecordsAsync("did:plc:test", "app.bsky.feed.post",
            limit: 2, cursor: first.Cursor, reverse: true);
        Assert.Equal(["3k1"], second.Records.Select(r => r.Rkey));
        Assert.Null(second.Cursor);
    }

    [Fact]
    public async Task RepoStore_ListRecords_MatchesInMemoryStore()
    {
        var inMemory = new InMemoryRepoStore();
        foreach (var rkey in new[] { "3k1", "3k2", "3k3", "3k4" })
        {
            await _repos.PutRecordAsync(CreateRecord(rkey));
            await inMemory.PutRecordAsync(CreateRecord(rkey));
        }

        foreach (var reverse in new[] { false, true })
        {
            string? efCursor = null;
            string? memCursor = null;

            for (var i = 0; i < 3; i++)
            {
                var ef = await _repos.ListRecordsAsync("did:plc:test", "app.bsky.feed.post",
                    limit: 2, cursor: efCursor, reverse: reverse);
                var mem = await inMemory.ListRecordsAsync("did:plc:test", "app.bsky.feed.post",
                    limit: 2, cursor: memCursor, reverse: reverse);

                Assert.Equal(mem.Records.Select(r => r.Rkey), ef.Records.Select(r => r.Rkey));
                Assert.Equal(mem.Cursor, ef.Cursor);

                efCursor = ef.Cursor;
                memCursor = mem.Cursor;
                if (efCursor is null) break;
            }
        }
    }

    [Fact]
    public async Task RepoStore_DeleteRecord_ReturnsWhetherItExisted()
    {
        await _repos.PutRecordAsync(CreateRecord("3k1"));

        Assert.True(await _repos.DeleteRecordAsync("did:plc:test", "app.bsky.feed.post", "3k1"));
        Assert.False(await _repos.DeleteRecordAsync("did:plc:test", "app.bsky.feed.post", "3k1"));
        Assert.Null(await _repos.GetRecordAsync("did:plc:test", "app.bsky.feed.post", "3k1"));
    }

    [Fact]
    public async Task RepoStore_DeleteAll_RemovesOnlyThatDidsData()
    {
        await _repos.PutRecordAsync(CreateRecord("3k1"));
        await _repos.PutRecordAsync(CreateRecord("3k2", collection: "app.bsky.feed.like"));
        await _repos.PutRecordAsync(CreateRecord("3k3", did: "did:plc:other"));
        await _repos.PutBlobAsync(CreateBlob("did:plc:test", "blob1"));
        await _repos.PutBlobAsync(CreateBlob("did:plc:other", "blob2", [9, 9]));

        await _repos.DeleteAllAsync("did:plc:test");

        Assert.Empty((await _repos.ListRecordsAsync("did:plc:test", "app.bsky.feed.post")).Records);
        Assert.Empty((await _repos.ListRecordsAsync("did:plc:test", "app.bsky.feed.like")).Records);
        Assert.Null(await _repos.GetBlobAsync("did:plc:test", "blob1"));

        Assert.Single((await _repos.ListRecordsAsync("did:plc:other", "app.bsky.feed.post")).Records);
        Assert.NotNull(await _repos.GetBlobAsync("did:plc:other", "blob2"));
    }

    // ── RepoStore: federation surface ──

    [Fact]
    public async Task RepoStore_ListAllRecords_MatchesInMemoryStore_AcrossCollections()
    {
        var inMemory = new InMemoryRepoStore();

        // "a.b.c" vs "a.b" is the case where ORDER BY (Collection, Rkey) and ordinal ordering
        // of "collection/rkey" disagree, because '.' (0x2E) sorts before '/' (0x2F).
        var records = new[]
        {
            CreateRecord("3k2", collection: "app.bsky.feed.post"),
            CreateRecord("3k1", collection: "app.bsky.feed.post"),
            CreateRecord("3k1", collection: "app.bsky.feed.like"),
            CreateRecord("3k1", collection: "a.b"),
            CreateRecord("3k1", collection: "a.b.c"),
        };

        foreach (var record in records)
        {
            await _repos.PutRecordAsync(record);
            await inMemory.PutRecordAsync(record);
        }

        await _repos.PutRecordAsync(CreateRecord("3k9", did: "did:plc:other"));

        var ef = await _repos.ListAllRecordsAsync("did:plc:test");
        var mem = await inMemory.ListAllRecordsAsync("did:plc:test");

        Assert.Equal(
            mem.Select(r => $"{r.Collection}/{r.Rkey}"),
            ef.Select(r => $"{r.Collection}/{r.Rkey}"));
        Assert.Equal(5, ef.Count);
        Assert.Equal("hello", ef[0].Value.GetProperty("text").GetString());
    }

    [Fact]
    public async Task RepoStore_ListBlobCids_IsScopedToDidAndOrdered()
    {
        await _repos.PutBlobAsync(CreateBlob("did:plc:test", "cid-b", [1]));
        await _repos.PutBlobAsync(CreateBlob("did:plc:test", "cid-a", [2]));
        await _repos.PutBlobAsync(CreateBlob("did:plc:other", "cid-c", [3]));

        Assert.Equal(["cid-a", "cid-b"], await _repos.ListBlobCidsAsync("did:plc:test"));
        Assert.Equal(["cid-c"], await _repos.ListBlobCidsAsync("did:plc:other"));
        Assert.Empty(await _repos.ListBlobCidsAsync("did:plc:nobody"));
    }

    // ── RepoStore: blobs ──

    [Fact]
    public async Task RepoStore_PutAndGetBlob_RoundTrips()
    {
        await _repos.PutBlobAsync(CreateBlob("did:plc:test", "blob1", [1, 2, 3, 4]));

        var blob = await _repos.GetBlobAsync("did:plc:test", "blob1");

        Assert.NotNull(blob);
        Assert.Equal("did:plc:test", blob.Did);
        Assert.Equal("blob1", blob.Cid);
        Assert.Equal("image/png", blob.MimeType);
        Assert.Equal(4, blob.Size);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, blob.Data);
    }

    [Fact]
    public async Task RepoStore_GetBlob_IsScopedToOwner()
    {
        await _repos.PutBlobAsync(CreateBlob("did:plc:test", "blob1"));

        Assert.Null(await _repos.GetBlobAsync("did:plc:other", "blob1"));
    }

    [Fact]
    public async Task RepoStore_PutBlob_DeduplicatesContentAcrossAccounts()
    {
        await _repos.PutBlobAsync(CreateBlob("did:plc:test", "blob1", [7, 7, 7]));
        await _repos.PutBlobAsync(CreateBlob("did:plc:other", "blob1", [7, 7, 7], "image/jpeg"));

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        Assert.Equal(1, await ctx.PdsBlobs.CountAsync());
        Assert.Equal(2, await ctx.PdsBlobRefs.CountAsync());

        // Each owner keeps its own declared MIME type
        Assert.Equal("image/png", (await _repos.GetBlobAsync("did:plc:test", "blob1"))!.MimeType);
        Assert.Equal("image/jpeg", (await _repos.GetBlobAsync("did:plc:other", "blob1"))!.MimeType);
    }

    [Fact]
    public async Task RepoStore_DeleteBlob_KeepsContentWhileOtherOwnersReferenceIt()
    {
        await _repos.PutBlobAsync(CreateBlob("did:plc:test", "blob1"));
        await _repos.PutBlobAsync(CreateBlob("did:plc:other", "blob1"));

        Assert.True(await _repos.DeleteBlobAsync("did:plc:test", "blob1"));

        Assert.Null(await _repos.GetBlobAsync("did:plc:test", "blob1"));
        var survivor = await _repos.GetBlobAsync("did:plc:other", "blob1");
        Assert.NotNull(survivor);
        Assert.Equal(new byte[] { 1, 2, 3 }, survivor.Data);

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        Assert.Equal(1, await ctx.PdsBlobs.CountAsync());
    }

    [Fact]
    public async Task RepoStore_DeleteBlob_CollectsContent_WhenLastReferenceGoes()
    {
        await _repos.PutBlobAsync(CreateBlob("did:plc:test", "blob1"));

        Assert.True(await _repos.DeleteBlobAsync("did:plc:test", "blob1"));
        Assert.False(await _repos.DeleteBlobAsync("did:plc:test", "blob1"));

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        Assert.Equal(0, await ctx.PdsBlobs.CountAsync());
        Assert.Equal(0, await ctx.PdsBlobRefs.CountAsync());
    }

    [Fact]
    public async Task RepoStore_DeleteAll_CollectsOrphanedBlobContentOnly()
    {
        await _repos.PutBlobAsync(CreateBlob("did:plc:test", "shared"));
        await _repos.PutBlobAsync(CreateBlob("did:plc:other", "shared"));
        await _repos.PutBlobAsync(CreateBlob("did:plc:test", "solo", [4, 5]));

        await _repos.DeleteAllAsync("did:plc:test");

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        Assert.Equal(["shared"], await ctx.PdsBlobs.Select(b => b.Cid).ToListAsync());
        Assert.NotNull(await _repos.GetBlobAsync("did:plc:other", "shared"));
    }

    [Fact]
    public async Task RepoStore_PutBlob_Recovers_WhenAnotherWriterInsertsSameContentFirst()
    {
        if (!ReportsConstraintViolations) return;

        // Simulates the check-then-insert race: the interceptor slips the same content row in
        // between this store's "does it exist" query and its SaveChanges, so the insert loses
        // on the primary key exactly as a concurrent upload of identical bytes would.
        var interceptor = new OneShotBlobInserter(
            _contextFactory,
            new PdsBlobEntity { Cid = "blob1", Size = 3, Data = [1, 2, 3] });

        var builder = new DbContextOptionsBuilder<PdsDbContext>();
        ConfigureProvider(builder);
        builder.AddInterceptors(interceptor);

        var racing = new EfCoreRepoStore<PdsDbContext>(new TestPdsDbContextFactory(builder.Options));

        await racing.PutBlobAsync(CreateBlob("did:plc:test", "blob1"));

        // Two saves: the one that lost the race and the retry — so the recovery path really ran.
        Assert.Equal(2, interceptor.SaveCount);

        var blob = await _repos.GetBlobAsync("did:plc:test", "blob1");
        Assert.NotNull(blob);
        Assert.Equal(new byte[] { 1, 2, 3 }, blob.Data);

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        Assert.Equal(1, await ctx.PdsBlobs.CountAsync());
        Assert.Equal(1, await ctx.PdsBlobRefs.CountAsync());
    }

    // ── RepoCommitStore ──

    private static RepoCommitState CreateHead(string did = "did:plc:test", string rev = "3k1") => new()
    {
        Did = did,
        CommitCid = $"bafyrei-{rev}",
        Rev = rev,
        DataCid = $"bafyrei-data-{rev}",
        CommitBlock = [1, 2, 3],
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
    };

    [Fact]
    public async Task CommitStore_SetAndGet_RoundTrips()
    {
        var head = CreateHead();
        await _heads.SetAsync(head);

        var fetched = await _heads.GetAsync("did:plc:test");

        Assert.NotNull(fetched);
        Assert.Equal(head.CommitCid, fetched.CommitCid);
        Assert.Equal(head.Rev, fetched.Rev);
        Assert.Equal(head.DataCid, fetched.DataCid);
        Assert.Equal(head.CommitBlock, fetched.CommitBlock);
        Assert.Equal(head.CreatedAt, fetched.CreatedAt);
    }

    [Fact]
    public async Task CommitStore_Get_ReturnsNull_WhenRepoHasNeverCommitted()
    {
        Assert.Null(await _heads.GetAsync("did:plc:missing"));
    }

    [Fact]
    public async Task CommitStore_Set_ReplacesPreviousHead()
    {
        await _heads.SetAsync(CreateHead(rev: "3k1"));
        await _heads.SetAsync(CreateHead(rev: "3k2"));

        Assert.Equal("3k2", (await _heads.GetAsync("did:plc:test"))!.Rev);

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        Assert.Equal(1, await ctx.PdsRepoHeads.CountAsync());
    }

    [Fact]
    public async Task CommitStore_List_PagesWithCursor_LikeInMemoryStore()
    {
        var inMemory = new InMemoryRepoCommitStore();
        foreach (var did in new[] { "did:plc:c", "did:plc:a", "did:plc:b" })
        {
            await _heads.SetAsync(CreateHead(did));
            await inMemory.SetAsync(CreateHead(did));
        }

        var first = await _heads.ListAsync(2, null);
        Assert.Equal(["did:plc:a", "did:plc:b"], first.Select(h => h.Did));
        Assert.Equal((await inMemory.ListAsync(2, null)).Select(h => h.Did), first.Select(h => h.Did));

        var second = await _heads.ListAsync(2, first[^1].Did);
        Assert.Equal(["did:plc:c"], second.Select(h => h.Did));

        Assert.Empty(await _heads.ListAsync(2, "did:plc:c"));
        Assert.Empty(await _heads.ListAsync(0, null));
    }

    [Fact]
    public async Task CommitStore_Delete_RemovesHead()
    {
        await _heads.SetAsync(CreateHead());

        await _heads.DeleteAsync("did:plc:test");
        await _heads.DeleteAsync("did:plc:test");   // no-op the second time

        Assert.Null(await _heads.GetAsync("did:plc:test"));
    }

    // ── DI registration ──

    [Fact]
    public void AddAtProtoPdsEfCoreStores_ReplacesInMemoryStores_RegardlessOfOrder()
    {
        var afterPds = new ServiceCollection();
        afterPds.AddLogging();
        afterPds.AddDbContextFactory<PdsDbContext>(ConfigureProvider);
        afterPds.AddAtProtoPds();
        afterPds.AddAtProtoPdsEfCoreStores<PdsDbContext>();

        using var afterProvider = afterPds.BuildServiceProvider();
        Assert.IsType<EfCoreAccountStore<PdsDbContext>>(afterProvider.GetRequiredService<IAccountStore>());
        Assert.IsType<EfCoreRepoStore<PdsDbContext>>(afterProvider.GetRequiredService<IRepoStore>());
        Assert.IsType<EfCoreRepoCommitStore<PdsDbContext>>(afterProvider.GetRequiredService<IRepoCommitStore>());

        var beforePds = new ServiceCollection();
        beforePds.AddLogging();
        beforePds.AddDbContextFactory<PdsDbContext>(ConfigureProvider);
        beforePds.AddAtProtoPdsEfCoreStores<PdsDbContext>();
        beforePds.AddAtProtoPds();

        using var beforeProvider = beforePds.BuildServiceProvider();
        Assert.IsType<EfCoreAccountStore<PdsDbContext>>(beforeProvider.GetRequiredService<IAccountStore>());
        Assert.IsType<EfCoreRepoStore<PdsDbContext>>(beforeProvider.GetRequiredService<IRepoStore>());
        Assert.IsType<EfCoreRepoCommitStore<PdsDbContext>>(beforeProvider.GetRequiredService<IRepoCommitStore>());
    }

    [Fact]
    public void AddAtProtoPdsEfCoreStores_AppliesConfiguredOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<PdsDbContext>(ConfigureProvider);
        services.AddAtProtoPdsEfCoreStores<PdsDbContext>(o =>
        {
            o.ClientSideAccountLookup = true;
            o.MaxClientSideLookupRows = 500;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<PdsEfCoreStoreOptions>();

        Assert.True(options.ClientSideAccountLookup);
        Assert.Equal(500, options.MaxClientSideLookupRows);
    }

    [Fact]
    public async Task AddAtProtoPdsEfCoreStores_CalledTwice_UsesTheLastCallsOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<PdsDbContext>(ConfigureProvider);
        services.AddAtProtoPdsEfCoreStores<PdsDbContext>();
        services.AddAtProtoPdsEfCoreStores<PdsDbContext>(o =>
        {
            o.ClientSideAccountLookup = true;
            o.MaxClientSideLookupRows = 1;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<PdsEfCoreStoreOptions>();
        Assert.True(options.ClientSideAccountLookup);
        Assert.Equal(1, options.MaxClientSideLookupRows);

        // …and the registered store actually honours them: the scan stops after one row.
        var store = provider.GetRequiredService<IAccountStore>();
        await store.CreateAsync(CreateAccount("did:plc:a", "a.test", "a@test.local"));
        await store.CreateAsync(CreateAccount("did:plc:b", "b.test", "b@test.local"));

        Assert.NotNull(await store.GetByHandleAsync("a.test"));
        Assert.Null(await store.GetByHandleAsync("b.test"));
    }

    [Fact]
    public void AddAtProtoPds_KeepsInMemoryStores_ByDefault()
    {
        var services = new ServiceCollection();
        services.AddAtProtoPds();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<InMemoryAccountStore>(provider.GetRequiredService<IAccountStore>());
        Assert.IsType<InMemoryRepoStore>(provider.GetRequiredService<IRepoStore>());
        Assert.IsType<InMemoryRepoCommitStore>(provider.GetRequiredService<IRepoCommitStore>());
    }

    private sealed class TestPdsDbContextFactory : IDbContextFactory<PdsDbContext>
    {
        private readonly DbContextOptions<PdsDbContext> _options;

        public TestPdsDbContextFactory(DbContextOptions<PdsDbContext> options) => _options = options;

        public PdsDbContext CreateDbContext() => new(_options);
    }

    /// <summary>
    /// Inserts a row through a second context the first time a save is about to run, standing
    /// in for a concurrent writer that commits between another store's read and its write.
    /// </summary>
    private sealed class OneShotBlobInserter : SaveChangesInterceptor
    {
        private readonly IDbContextFactory<PdsDbContext> _factory;
        private readonly PdsBlobEntity _row;
        private bool _fired;

        public OneShotBlobInserter(IDbContextFactory<PdsDbContext> factory, PdsBlobEntity row)
        {
            _factory = factory;
            _row = row;
        }

        /// <summary>How many saves the intercepted store attempted.</summary>
        public int SaveCount { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;

            if (!_fired)
            {
                _fired = true;
                await using var other = await _factory.CreateDbContextAsync(cancellationToken);
                other.Add(_row);
                await other.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }
}

/// <summary>Runs the store suite on the EF Core in-memory provider.</summary>
public sealed class EfCoreStoreInMemoryTests : EfCoreStoreTests
{
    private protected override void ConfigureProvider(DbContextOptionsBuilder builder)
        => builder.UseInMemoryDatabase(_dbName);

    private protected override bool ReportsConstraintViolations => false;
}

/// <summary>
/// Runs the store suite on SQLite, so every query has to survive translation to real SQL —
/// the case-insensitive handle/email comparison and the keyset cursor predicate in
/// particular, neither of which the in-memory provider would catch.
/// </summary>
public sealed class EfCoreStoreSqliteTests : EfCoreStoreTests
{
    // A shared open connection keeps the in-memory database alive for the whole test.
    private readonly DbConnection _connection = new SqliteConnection("Filename=:memory:");

    private protected override void ConfigureProvider(DbContextOptionsBuilder builder)
    {
        if (_connection.State != System.Data.ConnectionState.Open)
            _connection.Open();

        builder.UseSqlite(_connection);
    }

    public override async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }
}
