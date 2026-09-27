using System.Formats.Cbor;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

/// <summary>
/// <see cref="CommitBlock"/>: the signed bytes are the block minus its <c>sig</c> pair, byte for
/// byte, and anything that is not a well-formed version 3 commit is refused.
/// </summary>
public class CommitBlockTests
{
    private static readonly byte[] Data = CidComputation.ComputeBinaryForDagCbor([0xA0]);

    /// <summary>A commit map with the given fields around the four a commit needs.</summary>
    private static byte[] Block(
        Action<CborWriter>? sig = null, int version = 3, int extra = 0, bool duplicateDid = false,
        byte[]? data = null, Action<CborWriter>? prev = null)
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(4 + (sig is null ? 0 : 1) + (prev is null ? 0 : 1) + extra + (duplicateDid ? 1 : 0));
        writer.WriteTextString("did");
        writer.WriteTextString("did:plc:abc");
        if (sig is not null)
        {
            writer.WriteTextString("sig");
            sig(writer);
        }

        writer.WriteTextString("rev");
        writer.WriteTextString("3jzfcijpj2z2a");
        writer.WriteTextString("data");
        Link(writer, data ?? Data);
        if (prev is not null)
        {
            writer.WriteTextString("prev");
            prev(writer);
        }

        writer.WriteTextString("version");
        writer.WriteInt32(version);
        for (var i = 0; i < extra; i++)
        {
            writer.WriteTextString($"k{i:D2}");
            writer.WriteInt32(i);
        }

        if (duplicateDid)
        {
            writer.WriteTextString("did");
            writer.WriteTextString("did:plc:someoneelse");
        }

        writer.WriteEndMap();
        return writer.Encode();
    }

    private static void Link(CborWriter writer, byte[] cid)
    {
        writer.WriteTag((CborTag)42);
        writer.WriteByteString([0x00, .. cid]);
    }

    private static readonly Action<CborWriter> Signature = w => w.WriteByteString([0xAB, 0xCD]);

    /// <summary>A CID link that is not a CID: a SHA-256 multihash header with no digest behind it.</summary>
    private static readonly byte[] NotACid = [0x01, 0x71, 0x12, 0x20, 0x00];

    [Theory]
    [InlineData(0, false)]
    [InlineData(20, true)] // 25 entries lose sig for 24, which needs the two-byte header
    public void Read_SplicesOutTheSigPairAndKeepsEveryOtherByte(int extra, bool withPrev)
    {
        Action<CborWriter>? prev = withPrev ? w => Link(w, Data) : null;
        var block = CommitBlock.Read(Block(Signature, extra: extra, prev: prev));

        Assert.Equal([0xAB, 0xCD], block.Signature);
        Assert.Equal("did:plc:abc", block.Did);
        Assert.Equal("3jzfcijpj2z2a", block.Rev);
        Assert.Equal(Data, block.Data);
        Assert.Equal(Block(extra: extra, prev: prev), block.Unsigned);
    }

    public static TheoryData<string, byte[]> Malformed => new()
    {
        { "no sig", Block() },
        { "sig as text", Block(w => w.WriteTextString("not bytes")) },
        { "empty sig", Block(w => w.WriteByteString([])) },
        { "version 2", Block(Signature, version: 2) },
        { "duplicate key", Block(Signature, duplicateDid: true) },
        { "truncated", Block(Signature)[..20] },
        { "not a map", [0x80] },
        { "indefinite map", [0xBF, 0xFF] },
        { "empty", [] },
        // A signed commit whose data is not a CID used to reach Cid.FromBytes and throw there.
        { "data not a CID", Block(Signature, data: NotACid) },
        { "prev not a CID", Block(Signature, prev: w => Link(w, NotACid)) },
        // The block's CID covers trailing bytes, the signature does not.
        { "trailing bytes", [.. Block(Signature), 0x00] },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Read_NotAWellFormedSignedCommit_ThrowsFormatException(string reason, byte[] block)
    {
        var ex = Record.Exception(() => CommitBlock.Read(block));

        Assert.True(ex is FormatException, $"{reason}: {ex?.GetType().Name ?? "no exception"}");
    }

    [Fact]
    public void Read_NullPrev_IsAccepted() =>
        Assert.Equal(Data, CommitBlock.Read(Block(Signature, prev: w => w.WriteNull())).Data);

    // Every signature check builds a header for one entry fewer; a wrong encoding at a 24, 256
    // or 65536 boundary makes the count disagree with the bytes that follow, hashing to garbage.
    [Theory]
    [InlineData(0, new byte[] { 0xa0 })]
    [InlineData(1, new byte[] { 0xa1 })]
    [InlineData(23, new byte[] { 0xb7 })]
    [InlineData(24, new byte[] { 0xb8, 24 })]
    [InlineData(255, new byte[] { 0xb8, 0xFF })]
    [InlineData(256, new byte[] { 0xb9, 0x01, 0x00 })]
    [InlineData(65535, new byte[] { 0xb9, 0xFF, 0xFF })]
    [InlineData(65536, new byte[] { 0xba, 0x00, 0x01, 0x00, 0x00 })]
    [InlineData(0x10203040, new byte[] { 0xba, 0x10, 0x20, 0x30, 0x40 })]
    public void WriteMapHeader_EncodesTheShortestLength(int count, byte[] expected)
    {
        var buffer = new byte[8];

        Assert.Equal(expected, buffer[..CommitBlock.WriteMapHeader(buffer, count)]);
    }

    [Fact]
    public void WriteMapHeader_NegativeCount_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CommitBlock.WriteMapHeader(new byte[8], -1));
}
