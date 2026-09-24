using ATProtoNet.Identity;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Identity;

public class CidTests
{
    // The empty MST node, {"e":[],"l":null}: a DRISL CID every atproto implementation shares.
    private const string EmptyMstNode = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";

    // The raw CID of zero bytes.
    private const string EmptyRaw = "bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku";

    private const string EmptySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Fact]
    public void Parse_DagCborCid_ExposesCodecAndDigest()
    {
        var cid = Cid.Parse(EmptyMstNode);

        Assert.Equal(EmptyMstNode, cid.Value);
        Assert.Equal(CidCodec.DagCbor, cid.Codec);
        Assert.Equal(32, cid.Digest.Length);
    }

    [Fact]
    public void Parse_RawCid_ExposesCodecAndDigest()
    {
        var cid = Cid.Parse(EmptyRaw);

        Assert.Equal(CidCodec.Raw, cid.Codec);
        Assert.Equal(EmptySha256, Convert.ToHexStringLower(cid.Digest.Span));
    }

    [Fact]
    public void ToBytes_ReturnsBinaryCid()
    {
        var bytes = Cid.Parse(EmptyRaw).ToBytes();

        Assert.Equal("01551220" + EmptySha256, Convert.ToHexStringLower(bytes));
        Assert.Equal(EmptyRaw, CidComputation.EncodeCidToString(bytes));
    }

    [Fact]
    public void ToBytes_ReturnsACopy()
    {
        var cid = Cid.Parse(EmptyRaw);

        cid.ToBytes()[4] ^= 0xFF;

        Assert.Equal(EmptySha256, Convert.ToHexStringLower(cid.Digest.Span));
    }

    [Fact]
    public void ComputeForDagCbor_ReturnsParsedCid()
    {
        var cid = CidComputation.ComputeForDagCbor([0xA2, 0x61, 0x65, 0x80, 0x61, 0x6C, 0xF6]);

        Assert.Equal(EmptyMstNode, cid.Value);
        Assert.Equal(CidCodec.DagCbor, cid.Codec);
    }

    [Theory]
    [InlineData("hello")]                                                           // Regression: accepted before
    [InlineData("BAFKREIHDWDCEFGH4DQKJV67UZCMW7OJEE6XEDZDETOJUZJEVTENXQUVYKU")]    // Upper case
    [InlineData("QmbWqxBEKC3P8tqsKc98xmWNzrzDtRLMiMPL8wBuTGsMnR")]                // CIDv0
    [InlineData("bafybeihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku")]   // dag-pb codec
    [InlineData("bafyr4ihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku")]   // BLAKE3 hash
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvykv")]   // Non-zero padding bits
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyk")]    // Truncated
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku=")]  // Padded
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvy1u")]   // Not base32
    [InlineData("")]
    public void TryParse_NotABlessedCid_ReturnsFalse(string value)
    {
        Assert.False(Cid.TryParse(value, out _));
        Assert.ThrowsAny<ArgumentException>(() => Cid.Parse(value));
    }

    [Fact]
    public void Parse_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Cid.Parse(null!));
        Assert.False(Cid.TryParse(null, out _));
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var a = Cid.Parse(EmptyRaw);
        var b = Cid.Parse(EmptyRaw);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, Cid.Parse(EmptyMstNode));
    }

    [Fact]
    public void CompareTo_IsOrdinal()
    {
        var raw = Cid.Parse(EmptyRaw);
        var dagCbor = Cid.Parse(EmptyMstNode);

        Assert.Equal(Math.Sign(string.CompareOrdinal(EmptyRaw, EmptyMstNode)), Math.Sign(raw.CompareTo(dagCbor)));
    }

    [Fact]
    public void Conversions_RoundTripThroughString()
    {
        var cid = (Cid)EmptyRaw;
        string value = cid;

        Assert.Equal(EmptyRaw, value);
        Assert.ThrowsAny<ArgumentException>(() => (Cid)"hello");
    }
}
