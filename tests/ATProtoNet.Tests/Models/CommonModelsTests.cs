using System.Text.Json;
using ATProtoNet.Lexicon.Com.AtProto.Label;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Models;

public class CommonModelsTests
{
    private readonly JsonSerializerOptions _options = AtProtoJsonDefaults.Options;

    [Fact]
    public void BlobRef_Serializes()
    {
        var blob = new BlobRef
        {
            Ref = new BlobLink { Link = "bafkreibme22gw2h7y2h7tg2fhqotaqjucnbc24deqo72b6mkl2egezxhvy" },
            MimeType = "image/jpeg",
            Size = 12345,
        };

        var json = JsonSerializer.Serialize(blob, _options);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("blob", root.GetProperty("$type").GetString());
        Assert.Equal("image/jpeg", root.GetProperty("mimeType").GetString());
        Assert.Equal(12345, root.GetProperty("size").GetInt64());
    }

    [Fact]
    public void BlobRef_Deserializes()
    {
        var json = """
        {
            "$type": "blob",
            "ref": { "$link": "bafkreibme22gw2h7y2h7tg2fhqotaqjucnbc24deqo72b6mkl2egezxhvy" },
            "mimeType": "image/jpeg",
            "size": 12345
        }
        """;

        var blob = JsonSerializer.Deserialize<BlobRef>(json, _options);

        Assert.NotNull(blob);
        Assert.Equal("blob", blob.Type);
        Assert.Equal("image/jpeg", blob.MimeType);
        Assert.Equal(12345, blob.Size);
        Assert.NotNull(blob.Ref);
        Assert.Equal("bafkreibme22gw2h7y2h7tg2fhqotaqjucnbc24deqo72b6mkl2egezxhvy", blob.Ref.Link);
    }

    [Fact]
    public void StrongRef_Serializes()
    {
        var strongRef = new StrongRef
        {
            Uri = "at://did:plc:abc/app.bsky.feed.post/3k2la",
            Cid = "bafyreibabaqu374rfnqs5fmd5txi345nhut3pupavzkwx752whmqwpsjhie",
        };

        var json = JsonSerializer.Serialize(strongRef, _options);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("at://did:plc:abc/app.bsky.feed.post/3k2la", doc.RootElement.GetProperty("uri").GetString());
        Assert.Equal("bafyreibabaqu374rfnqs5fmd5txi345nhut3pupavzkwx752whmqwpsjhie", doc.RootElement.GetProperty("cid").GetString());
    }

    [Fact]
    public void StrongRef_Deserializes()
    {
        var json = """
        {
            "uri": "at://did:plc:abc/app.bsky.feed.post/3k2la",
            "cid": "bafyreibabaqu374rfnqs5fmd5txi345nhut3pupavzkwx752whmqwpsjhie"
        }
        """;

        var strongRef = JsonSerializer.Deserialize<StrongRef>(json, _options);

        Assert.NotNull(strongRef);
        Assert.Equal("at://did:plc:abc/app.bsky.feed.post/3k2la", strongRef.Uri);
    }

    [Fact]
    public void Label_Serializes()
    {
        var label = new Label
        {
            Version = 1,
            Src = "did:plc:labeler",
            Uri = "at://did:plc:abc/app.bsky.feed.post/xyz",
            Val = "nsfw",
            Cts = "2024-01-01T00:00:00.000Z",
        };

        var json = JsonSerializer.Serialize(label, _options);
        var doc = JsonDocument.Parse(json);

        Assert.Equal(1, doc.RootElement.GetProperty("ver").GetInt32());
        Assert.Equal("nsfw", doc.RootElement.GetProperty("val").GetString());
    }

    [Fact]
    public void Label_NegationLabel()
    {
        var label = new Label
        {
            Src = "did:plc:labeler",
            Uri = "at://did:plc:abc/app.bsky.feed.post/xyz",
            Val = "nsfw",
            Neg = true,
            Cts = "2024-01-01T00:00:00.000Z",
        };

        var json = JsonSerializer.Serialize(label, _options);
        Assert.Contains("\"neg\":true", json);
    }

    [Fact]
    public void Label_OptionalFieldsOmitted()
    {
        var label = new Label
        {
            Src = "did:plc:labeler",
            Uri = "at://did:plc:abc",
            Val = "spam",
            Cts = "2024-01-01T00:00:00.000Z",
        };

        var json = JsonSerializer.Serialize(label, _options);

        Assert.DoesNotContain("\"neg\"", json);
        Assert.DoesNotContain("\"exp\"", json);
        Assert.DoesNotContain("\"sig\"", json);
        Assert.DoesNotContain("\"cid\"", json);
    }

    // The shape com.atproto.label.queryLabels returns from mod.bsky.app: every label is signed,
    // and the 64-byte signature is a Lexicon bytes value with unpadded base64.
    private const string SignedLabelsJson =
        """
        {"cursor":"1","labels":[{"ver":1,"src":"did:plc:ar7c4by46qjdydhdevvrndac","uri":"did:plc:x2ptifqmpdbncpfbwxgfvl5c","val":"!hide","neg":false,"cts":"2024-05-29T19:21:44.113Z","sig":{"$bytes":"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+Pw"}}]}
        """;

    [Fact]
    public void Label_SignedLabelFromQueryLabels_DeserializesSignature()
    {
        var response = JsonSerializer.Deserialize<QueryLabelsResponse>(SignedLabelsJson, _options)!;

        var label = Assert.Single(response.Labels);
        Assert.Equal("!hide", label.Val);
        Assert.Equal(Enumerable.Range(0, 64).Select(i => (byte)i), label.Sig!);
    }

    [Fact]
    public void Label_Signature_SerializesAsUnpaddedBytesObject()
    {
        var label = new Label
        {
            Src = "did:plc:labeler",
            Uri = "at://did:plc:abc",
            Val = "spam",
            Cts = "2024-01-01T00:00:00.000Z",
            Sig = [0xde, 0xad, 0xbe, 0xef],
        };

        var json = JsonSerializer.Serialize(label, _options);

        using var doc = JsonDocument.Parse(json);
        var sig = doc.RootElement.GetProperty("sig");
        Assert.Equal("3q2+7w", sig.GetProperty("$bytes").GetString());
        Assert.Single(sig.EnumerateObject());
        Assert.Equal(label.Sig, JsonSerializer.Deserialize<Label>(json, _options)!.Sig);
    }
}

public class AtProtoJsonDefaultsTests
{
    [Fact]
    public void Options_AreCachedSingleton()
    {
        var a = AtProtoJsonDefaults.Options;
        var b = AtProtoJsonDefaults.Options;

        Assert.Same(a, b);
    }

    [Fact]
    public void Options_CamelCaseNaming()
    {
        var options = AtProtoJsonDefaults.Options;

        var json = JsonSerializer.Serialize(new { MyProperty = "test" }, options);
        Assert.Contains("myProperty", json);
    }

    [Fact]
    public void Options_IgnoresNullValues()
    {
        var options = AtProtoJsonDefaults.Options;

        var json = JsonSerializer.Serialize(new { Value = (string?)null, Name = "test" }, options);
        Assert.DoesNotContain("value", json);
        Assert.Contains("name", json);
    }

    [Fact]
    public void Options_CaseInsensitiveDeserialization()
    {
        var options = AtProtoJsonDefaults.Options;

        var result = JsonSerializer.Deserialize<TestDto>("""{"MyName":"test"}""", options);
        Assert.Equal("test", result?.MyName);
    }

    private sealed class TestDto
    {
        public string? MyName { get; set; }
    }
}
