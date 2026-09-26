using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Lexicon.App.Bsky.Graph;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests;

/// <summary>
/// Tests for RecordCollection types and AtProtoRecord base class.
/// </summary>
public class RecordCollectionTests
{
    // ──────────────────────────────────────────────────────────
    //  AtProtoRecord
    // ──────────────────────────────────────────────────────────

    private sealed class TodoItem : AtProtoRecord, IAtProtoRecord
    {
        public static Nsid Collection { get; } = Nsid.Parse("com.example.todo.item");

        public override string Type => Collection;

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("completed")]
        public bool Completed { get; set; }
    }

    private sealed class BookmarkRecord : AtProtoRecord, IAtProtoRecord
    {
        // An explicit implementation is read the same way as an implicit one.
        static Nsid IAtProtoRecord.Collection => Nsid.Parse("com.example.bookmarks.bookmark");

        [JsonPropertyName("$type")]
        public override string Type => "com.example.bookmarks.bookmark";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }
    }

    private sealed class PlainNote
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }

    [Fact]
    public void AtProtoRecord_Type_ReturnsCorrectNsid()
    {
        var todo = new TodoItem();
        Assert.Equal("com.example.todo.item", todo.Type);
    }

    [Fact]
    public void AtProtoRecord_CreatedAt_IsNullUntilSet()
    {
        Assert.Null(new TodoItem().CreatedAt);
    }

    [Fact]
    public void AtProtoRecord_DeserializedWithoutCreatedAt_DoesNotFabricateOne()
    {
        // A record some other writer stored without createdAt must not come back with "now",
        // which a later PutAsync would persist as if it were the original creation time.
        var todo = JsonSerializer.Deserialize<TodoItem>(
            """{"$type":"com.example.todo.item","title":"Buy milk"}""", AtProtoJsonDefaults.Options)!;

        Assert.Null(todo.CreatedAt);
        Assert.DoesNotContain("createdAt", JsonSerializer.Serialize(todo, AtProtoJsonDefaults.Options));
    }

    [Fact]
    public void AtProtoRecord_DeserializedWithCreatedAt_KeepsItsText()
    {
        var todo = JsonSerializer.Deserialize<TodoItem>(
            """{"$type":"com.example.todo.item","title":"Buy milk","createdAt":"2024-01-15T12:00:00Z"}""",
            AtProtoJsonDefaults.Options)!;

        Assert.Equal("2024-01-15T12:00:00Z", todo.CreatedAt?.ToString());
    }

    [Fact]
    public void AtProtoRecord_SerializesWithDollarType()
    {
        var todo = new TodoItem { Title = "Buy milk", Completed = false, CreatedAt = AtDatetime.Now() };
        var json = JsonSerializer.Serialize(todo, AtProtoJsonDefaults.Options);

        Assert.Contains("\"$type\":\"com.example.todo.item\"", json);
        Assert.Contains("\"title\":\"Buy milk\"", json);
        Assert.Contains("\"completed\":false", json);
        Assert.Contains("\"createdAt\":", json);
        // #49: the override must not also emit a camelCased "type" alongside "$type".
        Assert.DoesNotContain("\"type\":", json);
    }

    [Fact]
    public void AtProtoRecord_SubclassRepeatingTypeAttribute_WritesOnlyDollarType()
    {
        // System.Text.Json does not inherit [JsonPropertyName] onto an override, so a
        // subclass that omits it emits both "type" and "$type".
        var bookmark = new BookmarkRecord { Url = "https://example.com" };

        var json = JsonSerializer.Serialize(bookmark, AtProtoJsonDefaults.Options);

        Assert.DoesNotContain("\"type\":", json);
        Assert.Equal(1, json.Split("\"$type\":").Length - 1);
    }

    [Fact]
    public void AtProtoRecord_StaticCollection_IsNotSerialized()
    {
        var json = JsonSerializer.Serialize(new TodoItem(), AtProtoJsonDefaults.Options);

        Assert.DoesNotContain("collection", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AtProtoRecord_DifferentTypes_HaveDifferentNsids()
    {
        var todo = new TodoItem();
        var bookmark = new BookmarkRecord();

        Assert.NotEqual(todo.Type, bookmark.Type);
    }

    [Fact]
    public void AtProtoRecord_CanSetCustomCreatedAt()
    {
        var todo = new TodoItem
        {
            CreatedAt = AtDatetime.Parse("2024-01-15T12:00:00.000Z")
        };
        Assert.Equal("2024-01-15T12:00:00.000Z", todo.CreatedAt.ToString());
    }

    // ──────────────────────────────────────────────────────────
    //  GetCollection
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void GetCollection_WithoutNsid_UsesTheDeclaredCollection()
    {
        using var client = new AtProtoClient();

        Assert.Equal(TodoItem.Collection, client.GetCollection<TodoItem>().Collection);
        Assert.Equal(
            Nsid.Parse("com.example.bookmarks.bookmark"),
            client.GetCollection<BookmarkRecord>().Collection);
    }

    [Fact]
    public void GetCollection_OfABuiltInRecord_UsesItsLexiconCollection()
    {
        using var client = new AtProtoClient();

        Assert.Equal("app.bsky.feed.post", client.GetCollection<PostRecord>().Collection.Value);
        Assert.Equal("app.bsky.graph.follow", client.GetCollection<FollowRecord>().Collection.Value);
        Assert.Throws<ArgumentException>(
            () => client.GetCollection<PostRecord>(Nsid.Parse("app.bsky.feed.like")));
    }

    [Fact]
    public void GetCollection_WithTheDeclaredNsid_Succeeds()
    {
        using var client = new AtProtoClient();

        var todos = client.GetCollection<TodoItem>(Nsid.Parse("com.example.todo.item"));

        Assert.Equal(TodoItem.Collection, todos.Collection);
    }

    [Fact]
    public void GetCollection_WithAnotherNsidThanDeclared_Throws()
    {
        using var client = new AtProtoClient();

        var ex = Assert.Throws<ArgumentException>(
            () => client.GetCollection<TodoItem>(Nsid.Parse("com.example.todo.list")));

        Assert.Equal("collection", ex.ParamName);
        Assert.Contains("com.example.todo.item", ex.Message);
    }

    [Fact]
    public void GetCollection_ForATypeThatDeclaresNothing_TakesAnyNsid()
    {
        using var client = new AtProtoClient();

        var notes = client.GetCollection<PlainNote>(Nsid.Parse("com.example.note"));

        Assert.Equal(Nsid.Parse("com.example.note"), notes.Collection);
    }

    // ──────────────────────────────────────────────────────────
    //  RecordRef
    // ──────────────────────────────────────────────────────────

    private static readonly AtUri TodoUri = AtUri.Parse("at://did:plc:abc123/com.example.todo.item/3abc");
    private static readonly Cid TodoCid = Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm");

    [Fact]
    public void RecordRef_DerivesTheRecordKeyFromTheUri()
    {
        var recordRef = new RecordRef(TodoUri, TodoCid);

        Assert.Equal(TodoUri, recordRef.Uri);
        Assert.Equal(TodoCid, recordRef.Cid);
        Assert.Equal("3abc", recordRef.RecordKey);
        Assert.Null(recordRef.Commit);
    }

    [Fact]
    public void RecordRef_UriWithoutRecordKey_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new RecordRef(AtUri.Parse("at://did:plc:abc123/com.example.todo.item"), TodoCid));

        Assert.Equal("uri", ex.ParamName);
    }

    [Fact]
    public void RecordRef_ToStrongRef_ReferencesThisVersion()
    {
        var strongRef = new RecordRef(TodoUri, TodoCid).ToStrongRef();

        Assert.Equal(TodoUri, strongRef.Uri);
        Assert.Equal(TodoCid, strongRef.Cid);
    }

    [Fact]
    public void RecordRef_EqualReferences_AreEqual()
    {
        Assert.Equal(new RecordRef(TodoUri, TodoCid), new RecordRef(AtUri.Parse(TodoUri), Cid.Parse(TodoCid)));
    }

    // ──────────────────────────────────────────────────────────
    //  RecordView
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void RecordView_ContainsTypedValue()
    {
        var view = new RecordView<TodoItem>(TodoUri, TodoCid, new TodoItem { Title = "Test" });

        Assert.Equal("Test", view.Value.Title);
        Assert.Equal("3abc", view.RecordKey);
        Assert.Equal(TodoCid, view.Cid);
    }

    [Fact]
    public void RecordView_UriWithoutRecordKey_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new RecordView<TodoItem>(AtUri.Parse("at://did:plc:abc123"), null, new TodoItem()));
    }

    // ──────────────────────────────────────────────────────────
    //  RecordPage
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void RecordPage_HasMore_TrueWhenCursorPresent()
    {
        var page = new RecordPage<TodoItem>
        {
            Records = [],
            Cursor = "nextpage",
        };

        Assert.True(page.HasMore);
    }

    [Fact]
    public void RecordPage_HasMore_FalseWhenNoCursor()
    {
        var page = new RecordPage<TodoItem>
        {
            Records = [],
            Cursor = null,
        };

        Assert.False(page.HasMore);
    }

    [Fact]
    public void RecordPage_CanContainMultipleRecords()
    {
        var page = new RecordPage<TodoItem>
        {
            Records =
            [
                new(AtUri.Parse("at://did:plc:abc/com.example.col/1"), null, new TodoItem { Title = "a" }),
                new(AtUri.Parse("at://did:plc:abc/com.example.col/2"), null, new TodoItem { Title = "b" }),
                new(AtUri.Parse("at://did:plc:abc/com.example.col/3"), null, new TodoItem { Title = "c" }),
            ],
        };

        Assert.Equal(3, page.Records.Count);
    }
}
