using ATProtoNet.Pds;

namespace ATProtoNet.Tests.Pds;

public class InMemoryStoreTests
{
    // ── AccountStore tests ──

    [Fact]
    public async Task AccountStore_CreateAndGet_ByDid()
    {
        var store = new InMemoryAccountStore();
        var account = new PdsAccount { Did = "did:plc:test", Handle = "alice.test", Email = "alice@test.local", PasswordHash = "hash", SigningKey = "testkey" };
        await store.CreateAsync(account);

        var fetched = await store.GetByDidAsync("did:plc:test");
        Assert.NotNull(fetched);
        Assert.Equal("alice.test", fetched!.Handle);
    }

    [Fact]
    public async Task AccountStore_GetByHandle()
    {
        var store = new InMemoryAccountStore();
        var account = new PdsAccount { Did = "did:plc:test", Handle = "alice.test", PasswordHash = "hash", SigningKey = "testkey" };
        await store.CreateAsync(account);

        var fetched = await store.GetByHandleAsync("alice.test");
        Assert.NotNull(fetched);
        Assert.Equal("did:plc:test", fetched!.Did);
    }

    [Fact]
    public async Task AccountStore_GetByEmail()
    {
        var store = new InMemoryAccountStore();
        var account = new PdsAccount { Did = "did:plc:test", Handle = "alice.test", Email = "alice@test.local", PasswordHash = "hash", SigningKey = "testkey" };
        await store.CreateAsync(account);

        var fetched = await store.GetByEmailAsync("alice@test.local");
        Assert.NotNull(fetched);
        Assert.Equal("did:plc:test", fetched!.Did);
    }

    [Fact]
    public async Task AccountStore_HandleExists()
    {
        var store = new InMemoryAccountStore();
        Assert.False(await store.HandleExistsAsync("alice.test"));

        var account = new PdsAccount { Did = "did:plc:test", Handle = "alice.test", PasswordHash = "hash", SigningKey = "testkey" };
        await store.CreateAsync(account);

        Assert.True(await store.HandleExistsAsync("alice.test"));
    }

    [Fact]
    public async Task AccountStore_Update()
    {
        var store = new InMemoryAccountStore();
        var account = new PdsAccount { Did = "did:plc:test", Handle = "alice.test", PasswordHash = "hash", SigningKey = "testkey" };
        await store.CreateAsync(account);

        account.Handle = "alice2.test";
        await store.UpdateAsync(account);

        var fetched = await store.GetByDidAsync("did:plc:test");
        Assert.Equal("alice2.test", fetched!.Handle);
    }

    [Fact]
    public async Task AccountStore_Delete()
    {
        var store = new InMemoryAccountStore();
        var account = new PdsAccount { Did = "did:plc:test", Handle = "alice.test", PasswordHash = "hash", SigningKey = "testkey" };
        await store.CreateAsync(account);

        await store.DeleteAsync("did:plc:test");

        Assert.Null(await store.GetByDidAsync("did:plc:test"));
        Assert.False(await store.HandleExistsAsync("alice.test"));
    }

    [Fact]
    public async Task AccountStore_GetByDid_NotFound_ReturnsNull()
    {
        var store = new InMemoryAccountStore();
        Assert.Null(await store.GetByDidAsync("did:plc:unknown"));
    }

    // ── RepoStore tests ──

    [Fact]
    public async Task RepoStore_PutAndGetRecord()
    {
        var store = new InMemoryRepoStore();
        var record = new RepoRecord
        {
            Did = "did:plc:test",
            Collection = "app.bsky.feed.post",
            Rkey = "test123",
            Value = System.Text.Json.JsonSerializer.SerializeToElement(new { text = "Hello" }),
            Cid = "bafytest",
        };

        await store.PutRecordAsync(record);
        var fetched = await store.GetRecordAsync("did:plc:test", "app.bsky.feed.post", "test123");

        Assert.NotNull(fetched);
        Assert.Equal("bafytest", fetched!.Cid);
    }

    [Fact]
    public async Task RepoStore_DeleteRecord()
    {
        var store = new InMemoryRepoStore();
        var record = new RepoRecord
        {
            Did = "did:plc:test", Collection = "app.bsky.feed.post", Rkey = "test123",
            Value = System.Text.Json.JsonSerializer.SerializeToElement(new { text = "hi" }), Cid = "bafytest",
        };
        await store.PutRecordAsync(record);
        await store.DeleteRecordAsync("did:plc:test", "app.bsky.feed.post", "test123");

        Assert.Null(await store.GetRecordAsync("did:plc:test", "app.bsky.feed.post", "test123"));
    }

    [Fact]
    public async Task RepoStore_ListRecords_Pagination()
    {
        var store = new InMemoryRepoStore();
        for (int i = 0; i < 5; i++)
        {
            var record = new RepoRecord
            {
                Did = "did:plc:test", Collection = "app.bsky.feed.post", Rkey = $"post{i}",
                Value = System.Text.Json.JsonSerializer.SerializeToElement(new { text = $"Post {i}" }),
                Cid = $"bafycid{i}",
            };
            await store.PutRecordAsync(record);
        }

        var page1 = await store.ListRecordsAsync("did:plc:test", "app.bsky.feed.post", 3);
        Assert.Equal(3, page1.Records.Count);
        Assert.NotNull(page1.Cursor);

        var page2 = await store.ListRecordsAsync("did:plc:test", "app.bsky.feed.post", 3, cursor: page1.Cursor);
        Assert.Equal(2, page2.Records.Count);
        Assert.Null(page2.Cursor);
    }

    [Fact]
    public async Task RepoStore_DeleteAll()
    {
        var store = new InMemoryRepoStore();
        for (int i = 0; i < 3; i++)
        {
            await store.PutRecordAsync(new RepoRecord
            {
                Did = "did:plc:test", Collection = "app.bsky.feed.post", Rkey = $"post{i}",
                Value = System.Text.Json.JsonSerializer.SerializeToElement(new { }), Cid = $"cid{i}",
            });
        }

        await store.PutBlobAsync(new RepoBlob
        {
            Did = "did:plc:test", Cid = "blobcid", MimeType = "image/png", Size = 4, Data = [0x89, 0x50, 0x4E, 0x47],
        });

        await store.DeleteAllAsync("did:plc:test");

        var page = await store.ListRecordsAsync("did:plc:test", "app.bsky.feed.post", 10);
        Assert.Empty(page.Records);
        Assert.Null(await store.GetBlobAsync("did:plc:test", "blobcid"));
    }

    [Fact]
    public async Task RepoStore_Blobs_PutAndGet()
    {
        var store = new InMemoryRepoStore();
        var blob = new RepoBlob
        {
            Did = "did:plc:test", Cid = "blobcid", MimeType = "image/png",
            Size = 4, Data = [0x89, 0x50, 0x4E, 0x47],
        };

        await store.PutBlobAsync(blob);
        var fetched = await store.GetBlobAsync("did:plc:test", "blobcid");

        Assert.NotNull(fetched);
        Assert.Equal("image/png", fetched!.MimeType);
        Assert.Equal(4, fetched.Size);
    }

    [Fact]
    public async Task RepoStore_Blobs_Delete()
    {
        var store = new InMemoryRepoStore();
        await store.PutBlobAsync(new RepoBlob
        {
            Did = "did:plc:test", Cid = "blobcid", MimeType = "image/png", Size = 4, Data = [0x89, 0x50, 0x4E, 0x47],
        });

        await store.DeleteBlobAsync("did:plc:test", "blobcid");
        Assert.Null(await store.GetBlobAsync("did:plc:test", "blobcid"));
    }
}
