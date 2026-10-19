using System.Text.Json;

namespace ATProtoNet.Tests.Syntax;

/// <summary>The string form of <see cref="Cid"/>; its binary form and computation are tested with the data model.</summary>
public class CidTextTests
{
    private const string EmptyMstNode = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";
    private const string EmptyRaw = "bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku";

    [Theory]
    [InlineData(EmptyMstNode, CidCodec.DagCbor)]
    [InlineData(EmptyRaw, CidCodec.Raw)]
    [Trait("Spec", "SYN-60"), Trait("Spec", "SYN-63")]
    public void Parse_BlessedCid_KeepsItsTextAndExposesTheCodec(string text, CidCodec codec)
    {
        var cid = Cid.Parse(text);

        Assert.Equal(text, cid.Value);
        Assert.Equal(codec, cid.Codec);
        Assert.Equal(Cid.StringLength, text.Length);
        Assert.Equal(cid, Cid.FromBytes(cid.ToByteArray()));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("")]
    [InlineData("bafybeihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku")]
    [InlineData("bafyr4ihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku")]
    [InlineData("QmYwAPJzv5CZsnA625s3Xf2nemtYgPpHdWEz79ojWnPbdG")]
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyk")]
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku=")]
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvy1u")]
    [Trait("Spec", "SYN-60"), Trait("Spec", "SYN-62")]
    public void TryParse_NotABlessedCid_ReturnsFalse(string value)
    {
        Assert.False(Cid.TryParse(value, out _));
        Assert.Throws<FormatException>(() => Cid.Parse(value));
    }

    [Theory]
    [InlineData("BAFKREIHDWDCEFGH4DQKJV67UZCMW7OJEE6XEDZDETOJUZJEVTENXQUVYKU")]
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvykV")]
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvykv")]
    [Trait("Spec", "SYN-61")]
    public void TryParse_UpperCaseOrNonCanonicalBase32_ReturnsFalse(string value) => Assert.False(Cid.TryParse(value, out _));

    [Fact]
    [Trait("Spec", "SYN-63")]
    public void Json_CidFromText_WritesBackItsText()
    {
        var cid = JsonSerializer.Deserialize<Cid>($"\"{EmptyRaw}\"")!;

        Assert.Equal($"\"{EmptyRaw}\"", JsonSerializer.Serialize(cid));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Cid>("\"hello\""));
    }
}
