using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Serialization;

/// <summary>
/// Regression for #49: <see cref="AtProtoRecord"/> declares the discriminator as an abstract
/// property carrying <c>[JsonPropertyName("$type")]</c>. System.Text.Json neither inherits that
/// attribute through an <c>override</c> nor collapses the base member and the override into a
/// single contract property, so records written the documented way carried BOTH a correct
/// <c>"$type"</c> and a stray camelCased <c>"type"</c> — polluting records other apps read.
/// </summary>
public class RecordTypeDiscriminatorTests
{
    /// <summary>The documented pattern from the README — a plain override, no attribute.</summary>
    private sealed class TodoItem : AtProtoRecord
    {
        public override string Type => "com.example.todo.item";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";
    }

    /// <summary>The workaround consumers adopted: attribute re-declared on the override.</summary>
    private sealed class AnnotatedItem : AtProtoRecord
    {
        [JsonPropertyName("$type")]
        public override string Type => "com.example.todo.annotated";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";
    }

    /// <summary>A record carrying an unrelated member that must not be treated as the discriminator.</summary>
    private sealed class LabelledItem : AtProtoRecord
    {
        public override string Type => "com.example.todo.labelled";

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "note";
    }

    private static JsonObject SerializeToObject<T>(T value, JsonSerializerOptions options)
        => JsonNode.Parse(JsonSerializer.Serialize(value, options))!.AsObject();

    [Fact]
    public void Serialize_PlainOverride_WritesDollarTypeAndNoStrayType()
    {
        var json = SerializeToObject(new TodoItem { Title = "Buy milk" }, AtProtoJsonDefaults.Options);

        Assert.Equal("com.example.todo.item", (string?)json["$type"]);
        Assert.False(json.ContainsKey("type"), "the camelCased override leaked a stray 'type' property");
        Assert.False(json.ContainsKey("Type"));
    }

    [Fact]
    public void Serialize_PlainOverride_PreservesOtherRecordProperties()
    {
        var json = SerializeToObject(new TodoItem { Title = "Buy milk" }, AtProtoJsonDefaults.Options);

        Assert.Equal("Buy milk", (string?)json["title"]);
        Assert.NotNull(json["createdAt"]);
    }

    [Fact]
    public void Serialize_PlainOverride_WritesDollarTypeFirst()
    {
        var json = JsonSerializer.Serialize(new TodoItem { Title = "Buy milk" }, AtProtoJsonDefaults.Options);

        Assert.StartsWith("{\"$type\":\"com.example.todo.item\"", json);
    }

    [Fact]
    public void Serialize_AsObject_WritesDollarTypeAndNoStrayType()
    {
        // The path CreateRecordAsync/PutRecordAsync actually take: the record is boxed into
        // CreateRecordRequest.Record, which is declared as `object`.
        var json = SerializeToObject((object)new TodoItem { Title = "Buy milk" }, AtProtoJsonDefaults.Options);

        Assert.Equal("com.example.todo.item", (string?)json["$type"]);
        Assert.False(json.ContainsKey("type"));
    }

    [Fact]
    public void Serialize_ThroughTypeRegistryOptions_WritesDollarTypeAndNoStrayType()
    {
        var json = SerializeToObject(
            (object)new TodoItem { Title = "Buy milk" }, LexiconTypeRegistry.Instance.CreateOptions());

        Assert.Equal("com.example.todo.item", (string?)json["$type"]);
        Assert.False(json.ContainsKey("type"));
    }

    [Fact]
    public void Serialize_OverrideWithRedeclaredAttribute_StillWritesSingleDollarType()
    {
        // Consumers who applied the workaround must not now collide with the base member.
        var json = SerializeToObject(new AnnotatedItem { Title = "Buy milk" }, AtProtoJsonDefaults.Options);

        Assert.Equal("com.example.todo.annotated", (string?)json["$type"]);
        Assert.Equal("Buy milk", (string?)json["title"]);
        Assert.False(json.ContainsKey("type"));
    }

    [Fact]
    public void Serialize_UnrelatedMembers_AreLeftAlone()
    {
        var json = SerializeToObject(new LabelledItem(), AtProtoJsonDefaults.Options);

        Assert.Equal("com.example.todo.labelled", (string?)json["$type"]);
        Assert.Equal("note", (string?)json["kind"]);
    }

    [Fact]
    public void Deserialize_RecordWithDollarType_RoundTripsPayload()
    {
        const string json =
            """{"$type":"com.example.todo.item","title":"Buy milk","createdAt":"2024-01-15T12:00:00.000Z"}""";

        var todo = JsonSerializer.Deserialize<TodoItem>(json, AtProtoJsonDefaults.Options);

        Assert.NotNull(todo);
        Assert.Equal("Buy milk", todo.Title);
        Assert.Equal("2024-01-15T12:00:00.000Z", todo.CreatedAt);
        Assert.Equal("com.example.todo.item", todo.Type);
    }

    [Fact]
    public void ApplyRecordTypeDiscriminator_CanBeAddedToCustomOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
            {
                Modifiers = { AtProtoJsonDefaults.ApplyRecordTypeDiscriminator },
            },
        };

        var json = SerializeToObject(new TodoItem { Title = "Buy milk" }, options);

        Assert.Equal("com.example.todo.item", (string?)json["$type"]);
        Assert.False(json.ContainsKey("type"));
    }

    [Fact]
    public void ApplyRecordTypeDiscriminator_IgnoresNonRecordTypes()
    {
        // A non-AtProtoRecord type with a Type property must be untouched.
        var json = SerializeToObject(new NotARecord(), AtProtoJsonDefaults.Options);

        Assert.Equal("plain", (string?)json["type"]);
        Assert.False(json.ContainsKey("$type"));
    }

    private sealed class NotARecord
    {
        public string Type { get; set; } = "plain";
    }
}
