using System.Text.Json.Serialization;
using ATProtoNet.Http;

namespace ATProtoNet.IntegrationTests;

/// <summary>
/// A custom record type for integration testing.
/// Demonstrates using custom Lexicon record types with RecordCollection&lt;T&gt;.
/// </summary>
public class TestNote : AtProtoRecord
{
    public override string Type => "com.atprotonet.test.note";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}

/// <summary>
/// A second custom record type in a different collection, for multi-collection testing.
/// </summary>
public class OtherNote : AtProtoRecord
{
    public override string Type => "com.atprotonet.test.other";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
}

/// <summary>
/// Tests for the custom RecordCollection&lt;T&gt; API against a real PDS.
/// These tests demonstrate the primary custom-app use case.
/// </summary>
[Collection("Authenticated")]
public class CustomRecordTests
{
    private readonly AuthenticatedClientFixture _fixture;
    private const string Collection = "com.atprotonet.test.note";

    public CustomRecordTests(AuthenticatedClientFixture fixture)
    {
        _fixture = fixture;
    }

    [RequiresPdsFact]
    public async Task CreateRecord_ReturnsRecordRef()
    {
        var client = _fixture.Client;
        var notes = client.GetCollection<TestNote>(Collection);

        var created = await notes.CreateAsync(new TestNote
        {
            Title = "Test Note",
            Body = "Created by integration test",
            Priority = 1,
        });

        Assert.NotNull(created);
        Assert.NotEmpty(created.Uri);
        Assert.NotEmpty(created.Cid);
        Assert.NotEmpty(created.RecordKey);
        Assert.Contains(Collection, created.Uri);

        // Cleanup
        await notes.DeleteAsync(created.RecordKey);
    }

    [RequiresPdsFact]
    public async Task CreateAndGet_RoundTrips()
    {
        var client = _fixture.Client;
        var notes = client.GetCollection<TestNote>(Collection);

        var created = await notes.CreateAsync(new TestNote
        {
            Title = "Roundtrip Test",
            Body = "This should round-trip correctly",
            Priority = 5,
            Tags = ["integration", "test"],
        });

        var fetched = await notes.GetAsync(created.RecordKey);

        Assert.Equal("Roundtrip Test", fetched.Value.Title);
        Assert.Equal("This should round-trip correctly", fetched.Value.Body);
        Assert.Equal(5, fetched.Value.Priority);
        Assert.NotNull(fetched.Value.Tags);
        Assert.Contains("integration", fetched.Value.Tags);
        Assert.Contains("test", fetched.Value.Tags);
        Assert.Equal(created.RecordKey, fetched.RecordKey);
        Assert.Equal(created.Uri, fetched.Uri);

        // Cleanup
        await notes.DeleteAsync(created.RecordKey);
    }

    [RequiresPdsFact]
    public async Task PutRecord_CreatesOrUpdates()
    {
        var client = _fixture.Client;
        var notes = client.GetCollection<TestNote>(Collection);

        // Create using Put (upsert)
        var rkey = Identity.Tid.Next().Value;
        var putResult = await notes.PutAsync(rkey, new TestNote
        {
            Title = "Put Test",
            Body = "Created via put",
            Priority = 3,
        });

        Assert.NotEmpty(putResult.Uri);
        Assert.Equal(rkey, putResult.RecordKey);

        // Update using Put
        var updateResult = await notes.PutAsync(rkey, new TestNote
        {
            Title = "Put Test Updated",
            Body = "Updated via put",
            Priority = 10,
        });

        Assert.NotEmpty(updateResult.Uri);

        // Verify update
        var fetched = await notes.GetAsync(rkey);
        Assert.Equal("Put Test Updated", fetched.Value.Title);
        Assert.Equal(10, fetched.Value.Priority);

        // Cleanup
        await notes.DeleteAsync(rkey);
    }

    [RequiresPdsFact]
    public async Task DeleteRecord_RemovesRecord()
    {
        var client = _fixture.Client;
        var notes = client.GetCollection<TestNote>(Collection);

        var created = await notes.CreateAsync(new TestNote
        {
            Title = "To Be Deleted",
            Body = "This will be deleted",
        });

        // Delete it
        await notes.DeleteAsync(created.RecordKey);

        // Verify it's gone
        bool exists = await notes.ExistsAsync(created.RecordKey);
        Assert.False(exists);
    }

    [RequiresPdsFact]
    public async Task ExistsAsync_ReturnsTrueForExistingRecord()
    {
        var client = _fixture.Client;
        var notes = client.GetCollection<TestNote>(Collection);

        var created = await notes.CreateAsync(new TestNote
        {
            Title = "Exists Test",
            Body = "Testing existence check",
        });

        Assert.True(await notes.ExistsAsync(created.RecordKey));

        // Cleanup
        await notes.DeleteAsync(created.RecordKey);
    }

    [RequiresPdsFact]
    public async Task ExistsAsync_ReturnsFalseForMissingRecord()
    {
        var client = _fixture.Client;
        var notes = client.GetCollection<TestNote>(Collection);

        Assert.False(await notes.ExistsAsync("nonexistent-key-12345"));
    }

    [RequiresPdsFact]
    public async Task ListRecords_ReturnsPage()
    {
        var client = _fixture.Client;
        var notes = client.GetCollection<TestNote>(Collection);

        // Create a few records
        var keys = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var created = await notes.CreateAsync(new TestNote
            {
                Title = $"List Test #{i}",
                Body = $"Item {i}",
                Priority = i,
            });
            keys.Add(created.RecordKey);
        }

        try
        {
            var page = await notes.ListAsync(limit: 10);

            Assert.NotNull(page);
            Assert.True(page.Records.Count >= 3);
            Assert.All(page.Records, r =>
            {
                Assert.NotEmpty(r.Uri);
                Assert.NotEmpty(r.RecordKey);
                Assert.NotNull(r.Value);
                Assert.NotEmpty(r.Value.Title);
            });
        }
        finally
        {
            // Cleanup
            foreach (var key in keys)
                await notes.DeleteAsync(key);
        }
    }

    [RequiresPdsFact]
    public async Task EnumerateAsync_IteratesAllRecords()
    {
        var client = _fixture.Client;
        var notes = client.GetCollection<TestNote>(Collection);

        // Create some records
        var keys = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var created = await notes.CreateAsync(new TestNote
            {
                Title = $"Enumerate Test #{i}",
                Body = $"Item {i}",
            });
            keys.Add(created.RecordKey);
        }

        try
        {
            var allRecords = new List<RecordView<TestNote>>();
            await foreach (var record in notes.EnumerateAsync(pageSize: 2))
            {
                allRecords.Add(record);
            }

            Assert.True(allRecords.Count >= 5);
        }
        finally
        {
            foreach (var key in keys)
                await notes.DeleteAsync(key);
        }
    }

    [RequiresPdsFact]
    public async Task GetFromOtherUser_WorksWithOwnDid()
    {
        var client = _fixture.Client;
        var notes = client.GetCollection<TestNote>(Collection);

        var created = await notes.CreateAsync(new TestNote
        {
            Title = "Cross-User Test",
            Body = "Read from own DID explicitly",
        });

        try
        {
            // GetFrom using own DID — should work just like Get
            var fetched = await notes.GetFromAsync(client.Did!, created.RecordKey);
            Assert.Equal("Cross-User Test", fetched.Value.Title);
        }
        finally
        {
            await notes.DeleteAsync(created.RecordKey);
        }
    }

    [RequiresPdsFact]
    public async Task MultipleCollections_WorkIndependently()
    {
        var client = _fixture.Client;
        var notes = client.GetCollection<TestNote>(Collection);
        var otherCollection = client.GetCollection<OtherNote>("com.atprotonet.test.other");

        var noteRef = await notes.CreateAsync(new TestNote { Title = "In notes" });
        var otherRef = await otherCollection.CreateAsync(new OtherNote { Title = "In other" });

        try
        {
            var note = await notes.GetAsync(noteRef.RecordKey);
            var otherRecord = await otherCollection.GetAsync(otherRef.RecordKey);

            Assert.Equal("In notes", note.Value.Title);
            Assert.Equal("In other", otherRecord.Value.Title);

            // They're in different collections
            Assert.Contains("com.atprotonet.test.note", note.Uri);
            Assert.Contains("com.atprotonet.test.other", otherRecord.Uri);
        }
        finally
        {
            await notes.DeleteAsync(noteRef.RecordKey);
            await otherCollection.DeleteAsync(otherRef.RecordKey);
        }
    }
}
