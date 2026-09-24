using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Serialization;

public class IdentitySerializationTests
{
    private readonly JsonSerializerOptions _options = AtProtoJsonDefaults.Options;

    // ── DID ──

    [Fact]
    public void Did_Serializes_AsString()
    {
        var did = Did.Parse("did:plc:abc123");
        var json = JsonSerializer.Serialize(did, _options);
        Assert.Equal("\"did:plc:abc123\"", json);
    }

    [Fact]
    public void Did_Deserializes_FromString()
    {
        var did = JsonSerializer.Deserialize<Did>("\"did:plc:abc123\"", _options);
        Assert.NotNull(did);
        Assert.Equal("did:plc:abc123", did.Value);
    }

    [Fact]
    public void Did_RoundTrips()
    {
        var original = Did.Parse("did:web:example.com");
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<Did>(json, _options);

        Assert.Equal(original, deserialized);
    }

    // ── Handle ──

    [Fact]
    public void Handle_Serializes_AsString()
    {
        var handle = Handle.Parse("alice.bsky.social");
        var json = JsonSerializer.Serialize(handle, _options);
        Assert.Equal("\"alice.bsky.social\"", json);
    }

    [Fact]
    public void Handle_Deserializes_FromString()
    {
        var handle = JsonSerializer.Deserialize<Handle>("\"alice.bsky.social\"", _options);
        Assert.NotNull(handle);
        Assert.Equal("alice.bsky.social", handle.Value);
    }

    // ── AtIdentifier ──

    [Fact]
    public void AtIdentifier_Did_Serializes()
    {
        var id = AtIdentifier.Parse("did:plc:abc123");
        var json = JsonSerializer.Serialize(id, _options);
        Assert.Equal("\"did:plc:abc123\"", json);
    }

    [Fact]
    public void AtIdentifier_Handle_Serializes()
    {
        var id = AtIdentifier.Parse("alice.bsky.social");
        var json = JsonSerializer.Serialize(id, _options);
        Assert.Equal("\"alice.bsky.social\"", json);
    }

    [Fact]
    public void AtIdentifier_Did_Deserializes()
    {
        var id = JsonSerializer.Deserialize<AtIdentifier>("\"did:plc:abc123\"", _options);
        Assert.NotNull(id);
        Assert.True(id.IsDid);
        Assert.Equal("did:plc:abc123", id.Value);
    }

    [Fact]
    public void AtIdentifier_Handle_Deserializes()
    {
        var id = JsonSerializer.Deserialize<AtIdentifier>("\"alice.bsky.social\"", _options);
        Assert.NotNull(id);
        Assert.True(id.IsHandle);
    }

    // ── Nsid ──

    [Fact]
    public void Nsid_Serializes_AsString()
    {
        var nsid = Nsid.Parse("com.atproto.repo.createRecord");
        var json = JsonSerializer.Serialize(nsid, _options);
        Assert.Equal("\"com.atproto.repo.createRecord\"", json);
    }

    [Fact]
    public void Nsid_RoundTrips()
    {
        var original = Nsid.Parse("app.bsky.feed.post");
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<Nsid>(json, _options);

        Assert.Equal(original, deserialized);
    }

    // ── AtUri ──

    [Fact]
    public void AtUri_Serializes_AsString()
    {
        var uri = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.post/3k2la");
        var json = JsonSerializer.Serialize(uri, _options);
        Assert.Equal("\"at://did:plc:abc123/app.bsky.feed.post/3k2la\"", json);
    }

    [Fact]
    public void AtUri_RoundTrips()
    {
        var original = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.post/3k2la");
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<AtUri>(json, _options);

        Assert.Equal(original, deserialized);
    }

    // ── Tid ──

    [Fact]
    public void Tid_Serializes_AsString()
    {
        var tid = Tid.Parse("abcdefghijklm");
        var json = JsonSerializer.Serialize(tid, _options);
        Assert.Equal("\"abcdefghijklm\"", json);
    }

    [Fact]
    public void Tid_RoundTrips()
    {
        var original = Tid.Next();
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<Tid>(json, _options);

        Assert.Equal(original, deserialized);
    }

    // ── RecordKey ──

    [Fact]
    public void RecordKey_Serializes_AsString()
    {
        var rkey = RecordKey.Parse("self");
        var json = JsonSerializer.Serialize(rkey, _options);
        Assert.Equal("\"self\"", json);
    }

    [Fact]
    public void RecordKey_RoundTrips()
    {
        var original = RecordKey.NewTid();
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<RecordKey>(json, _options);

        Assert.Equal(original, deserialized);
    }

    // ── Cid ──

    [Fact]
    public void Cid_RoundTrips()
    {
        var original = Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm");
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<Cid>(json, _options);

        Assert.Equal("\"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm\"", json);
        Assert.Equal(original, deserialized);
    }

    // ── Space URIs ──

    [Fact]
    public void SpaceUris_SerializeAsStrings()
    {
        var space = SpaceUri.Parse("at://did:plc:abc123/space/com.example.forum/a");
        var record = space.Record(Did.Parse("did:plc:def456"), Nsid.Parse("com.example.post"), RecordKey.Parse("1"));

        var spaceJson = JsonSerializer.Serialize(space, _options);
        var recordJson = JsonSerializer.Serialize(record, _options);

        Assert.Equal("\"at://did:plc:abc123/space/com.example.forum/a\"", spaceJson);
        Assert.Equal(space, JsonSerializer.Deserialize<SpaceUri>(spaceJson, _options));
        Assert.Equal(record, JsonSerializer.Deserialize<SpaceRecordUri>(recordJson, _options));
    }

    // ── Invalid values ──

    [Fact]
    public void InvalidValue_ThrowsJsonExceptionWithPath()
    {
        // Regression: an invalid identifier escaped as an ArgumentException with no JSON path.
        var json = """{"Did":"did:plc:abc123","Uri":"at://did:plc:abc123/"}""";

        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TestModel>(json, _options));

        Assert.Equal("$.Uri", ex.Path);
        Assert.Contains("$.Uri", ex.Message);
        var inner = Assert.IsType<FormatException>(ex.InnerException);
        Assert.Contains("at://did:plc:abc123/", inner.Message);
    }

    [Theory]
    [InlineData("\"not-a-did\"")]
    [InlineData("\"\"")]
    [InlineData("42")]
    public void Did_InvalidJson_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Did>(json, _options));
    }

    [Fact]
    public void Cid_InvalidString_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Cid>("\"hello\"", _options));
    }

    [Fact]
    public void NullValue_DeserializesAsNull()
    {
        var model = JsonSerializer.Deserialize<TestModel>("""{"Did":null,"Uri":null}""", _options);

        Assert.NotNull(model);
        Assert.Null(model.Did);
        Assert.Null(model.Uri);
    }

    // ── Embedded in objects ──

    [Fact]
    public void IdentityTypes_SerializeInObject()
    {
        var obj = new TestModel
        {
            Did = Did.Parse("did:plc:abc123"),
            Handle = Handle.Parse("alice.bsky.social"),
            Uri = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.post/test"),
        };

        var json = JsonSerializer.Serialize(obj, _options);
        var deserialized = JsonSerializer.Deserialize<TestModel>(json, _options);

        Assert.NotNull(deserialized);
        Assert.Equal(obj.Did, deserialized.Did);
        Assert.Equal(obj.Handle, deserialized.Handle);
        Assert.Equal(obj.Uri, deserialized.Uri);
    }

    private sealed class TestModel
    {
        public Did? Did { get; set; }
        public Handle? Handle { get; set; }
        public AtUri? Uri { get; set; }
    }
}
