using System.Text.Json;
using ATProtoNet.Data;

namespace ATProtoNet.Tests.Data;

public class LinkAndBlobTests
{
    private static readonly Cid Raw = Cid.Compute(CidCodec.Raw, "blob"u8);
    private static readonly Cid DagCbor = Cid.Compute(CidCodec.DagCbor, "record"u8);

    [Fact]
    [Trait("Spec", "DAT-50")]
    public void CidLink_Json_IsTheLinkObject()
    {
        var link = new CidLink { Cid = DagCbor };
        var json = JsonSerializer.Serialize(link);

        Assert.Equal($$"""{"$link":"{{DagCbor}}"}""", json);
        Assert.Equal(link, JsonSerializer.Deserialize<CidLink>(json));
    }

    [Theory]
    [Trait("Spec", "DAT-50")]
    [InlineData("""{"$link":1}""")]
    [InlineData("""{"$link":"bafy"}""")]
    [InlineData("""{"cid":"x"}""")]
    [InlineData("\"bafkrei\"")]
    public void CidLink_MalformedJson_ThrowsJsonException(string json) =>
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<CidLink>(json));

    [Fact]
    [Trait("Spec", "DAT-51")]
    public void BlobRef_Json_IsTheBlobObject()
    {
        var blob = new BlobRef { Ref = new() { Cid = Raw }, MimeType = "image/png", Size = 12 };
        var json = JsonSerializer.Serialize(blob);

        Assert.Equal($$"""{"$type":"blob","ref":{"$link":"{{Raw}}"},"mimeType":"image/png","size":12}""", json);
        Assert.Equal(blob, JsonSerializer.Deserialize<BlobRef>(json));
        Assert.False(blob.IsLegacy);
    }

    [Theory]
    [Trait("Spec", "DAT-51")]
    [InlineData("""{"$type":"blob","ref":{"$link":"RAW"},"mimeType":"image/png","size":"12"}""")] // string size
    [InlineData("""{"$type":"blob","mimeType":"image/png","size":12}""")] // no ref
    [InlineData("""{"$type":"blob","ref":{"$link":"RAW"},"mimeType":"","size":12}""")] // empty mimeType
    [InlineData("""{"$type":"blob","ref":{"$link":"RAW"},"mimeType":"image/png","size":-1}""")] // negative size
    [InlineData("""{"$type":"blob","ref":{"$link":"DAG"},"mimeType":"image/png","size":12}""")] // not a raw CID
    [InlineData("""{"$type":"blob","ref":{"$link":"RAW"},"mimeType":"image/png","size":12,"x":1}""")] // another key
    [InlineData("""{"$type":"other","ref":{"$link":"RAW"},"mimeType":"image/png","size":12}""")]
    public void BlobRef_InvalidJson_ThrowsJsonException(string json) =>
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<BlobRef>(json.Replace("RAW", Raw.Value).Replace("DAG", DagCbor.Value)));

    [Fact]
    [Trait("Spec", "DAT-51")]
    public void BlobRef_DagCborRef_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => new BlobRef { Ref = new() { Cid = DagCbor }, MimeType = "image/png", Size = 1 });

    [Fact]
    [Trait("Spec", "DAT-52")]
    public void BlobRef_LegacyJson_ReadsAndWritesBackInTheLegacyForm()
    {
        var json = $$"""{"cid":"{{Raw}}","mimeType":"image/jpeg"}""";

        var blob = JsonSerializer.Deserialize<BlobRef>(json)!;

        Assert.True(blob.IsLegacy);
        Assert.Null(blob.Size);
        Assert.Equal(Raw, blob.Ref.Cid);
        Assert.Equal(json, JsonSerializer.Serialize(blob));
    }

    [Fact]
    [Trait("Spec", "DAT-52")]
    public void BlobRef_PublicConstruction_IsNeverLegacy()
    {
        Assert.DoesNotContain(typeof(BlobRef).GetConstructors(), c => c.GetParameters().Length > 0);
        Assert.False(new BlobRef { Ref = new() { Cid = Raw }, MimeType = "a/b", Size = 0 }.IsLegacy);
    }
}
