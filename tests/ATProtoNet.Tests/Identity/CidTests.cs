using ATProtoNet.Identity;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// What <see cref="Cid"/> adds to the shared identifier contract (<see cref="IdentifierContractTests"/>)
/// and the interop syntax fixtures (<see cref="SyntaxInteropTests"/>): the atproto-blessed subset,
/// its codec and digest, and the binary form.
/// </summary>
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
    [InlineData("bafybeihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku")]   // dag-pb codec
    [InlineData("bafyr4ihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku")]   // BLAKE3 hash
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvykv")]   // Non-zero padding bits
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyk")]    // Truncated
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku=")]  // Padded
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvy1u")]   // Not base32
    public void TryParse_NotABlessedCid_ReturnsFalse(string value)
    {
        Assert.False(Cid.TryParse(value, out _));
        Assert.ThrowsAny<ArgumentException>(() => Cid.Parse(value));
    }
}
