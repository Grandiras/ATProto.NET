using System.Text.Json.Serialization;
using ATProtoNet;

namespace ATProtoNet.Tests;

/// <summary>
/// Tests for RecordCollection types and AtProtoRecord base class.
/// </summary>
public class RecordCollectionTests
{
    // ──────────────────────────────────────────────────────────
    //  AtProtoRecord
    // ──────────────────────────────────────────────────────────

    private sealed class TodoItem : AtProtoRecord
    {
        [JsonPropertyName("$type")]
        public override string Type => "com.example.todo.item";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("completed")]
        public bool Completed { get; set; }
    }

    private sealed class BookmarkRecord : AtProtoRecord
    {
        [JsonPropertyName("$type")]
        public override string Type => "com.example.bookmarks.bookmark";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }
    }

    [Fact]
    public void AtProtoRecord_Type_ReturnsCorrectNsid()
    {
        var todo = new TodoItem();
        Assert.Equal("com.example.todo.item", todo.Type);
    }

    [Fact]
    public void AtProtoRecord_CreatedAt_IsAutoPopulated()
    {
        var before = DateTimeOffset.UtcNow;
        var todo = new TodoItem();
        var after = DateTimeOffset.UtcNow;

        var createdAt = DateTimeOffset.Parse(todo.CreatedAt);
        Assert.InRange(createdAt, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void AtProtoRecord_SerializesWithDollarType()
    {
        var todo = new TodoItem { Title = "Buy milk", Completed = false };
        var json = System.Text.Json.JsonSerializer.Serialize(
            todo, ATProtoNet.Serialization.AtProtoJsonDefaults.Options);

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
        var todo = new TodoItem { Title = "Buy milk" };

        var json = System.Text.Json.JsonSerializer.Serialize(todo, ATProtoNet.Serialization.AtProtoJsonDefaults.Options);

        Assert.DoesNotContain("\"type\":", json);
        Assert.Equal(1, json.Split("\"$type\":").Length - 1);
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
            CreatedAt = "2024-01-15T12:00:00.000Z"
        };
        Assert.Equal("2024-01-15T12:00:00.000Z", todo.CreatedAt);
    }

    // ──────────────────────────────────────────────────────────
    //  RecordRef
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void RecordRef_HasRequiredProperties()
    {
        var recordRef = new RecordRef
        {
            Uri = "at://did:plc:abc123/com.example.todo.item/3abc",
            Cid = "bafyreia...",
            RecordKey = "3abc",
        };

        Assert.Equal("at://did:plc:abc123/com.example.todo.item/3abc", recordRef.Uri);
        Assert.Equal("bafyreia...", recordRef.Cid);
        Assert.Equal("3abc", recordRef.RecordKey);
    }

    // ──────────────────────────────────────────────────────────
    //  RecordView
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void RecordView_ContainsTypedValue()
    {
        var view = new RecordView<TodoItem>
        {
            Uri = "at://did:plc:abc123/com.example.todo.item/3abc",
            Cid = "bafyreia...",
            Value = new TodoItem { Title = "Test" },
            RecordKey = "3abc",
        };

        Assert.Equal("Test", view.Value.Title);
        Assert.Equal("3abc", view.RecordKey);
    }

    // ──────────────────────────────────────────────────────────
    //  RecordPage
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void RecordPage_HasMore_TrueWhenCursorPresent()
    {
        var page = new RecordPage<TodoItem>
        {
            Records = new List<RecordView<TodoItem>>(),
            Cursor = "nextpage",
        };

        Assert.True(page.HasMore);
    }

    [Fact]
    public void RecordPage_HasMore_FalseWhenNoCursor()
    {
        var page = new RecordPage<TodoItem>
        {
            Records = new List<RecordView<TodoItem>>(),
            Cursor = null,
        };

        Assert.False(page.HasMore);
    }

    [Fact]
    public void RecordPage_CanContainMultipleRecords()
    {
        var page = new RecordPage<TodoItem>
        {
            Records = new List<RecordView<TodoItem>>
            {
                new() { Uri = "at://did:plc:abc/col/1", Value = new TodoItem { Title = "a" }, RecordKey = "1" },
                new() { Uri = "at://did:plc:abc/col/2", Value = new TodoItem { Title = "b" }, RecordKey = "2" },
                new() { Uri = "at://did:plc:abc/col/3", Value = new TodoItem { Title = "c" }, RecordKey = "3" },
            },
        };

        Assert.Equal(3, page.Records.Count);
    }
}
