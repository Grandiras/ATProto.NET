using System.Formats.Cbor;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

public sealed class MstNodeDataTests
{
    [Fact]
    public void Roundtrip_EmptyNode()
    {
        var node = new MstNodeData
        {
            Left = null,
            Entries = [],
        };

        var bytes = node.ToBytes();
        var decoded = MstNodeData.FromBytes(bytes);

        Assert.Null(decoded.Left);
        Assert.Empty(decoded.Entries);
    }

    [Fact]
    public void Roundtrip_NodeWithEntries()
    {
        var valueCid = CidComputation.ComputeBinaryForDagCbor([0xA0]); // empty CBOR map

        var node = new MstNodeData
        {
            Left = null,
            Entries =
            [
                new MstTreeEntry(0, "app.bsky.feed.post/abc"u8.ToArray(), valueCid, null),
                new MstTreeEntry(19, "xyz"u8.ToArray(), valueCid, null), // shares prefix with prev
            ],
        };

        var bytes = node.ToBytes();
        var decoded = MstNodeData.FromBytes(bytes);

        Assert.Null(decoded.Left);
        Assert.Equal(2, decoded.Entries.Count);

        Assert.Equal(0, decoded.Entries[0].PrefixLength);
        Assert.Equal("app.bsky.feed.post/abc"u8.ToArray(), decoded.Entries[0].KeySuffix);
        Assert.Equal(valueCid, decoded.Entries[0].Value);
        Assert.Null(decoded.Entries[0].Tree);

        Assert.Equal(19, decoded.Entries[1].PrefixLength);
        Assert.Equal("xyz"u8.ToArray(), decoded.Entries[1].KeySuffix);
    }

    [Fact]
    public void Roundtrip_NodeWithLeftPointer()
    {
        var leftCid = CidComputation.ComputeBinaryForDagCbor([0xA0]);
        var valueCid = CidComputation.ComputeBinaryForDagCbor([0xA1, 0x61, 0x61, 0x01]);

        var node = new MstNodeData
        {
            Left = leftCid,
            Entries =
            [
                new MstTreeEntry(0, "key"u8.ToArray(), valueCid, null),
            ],
        };

        var bytes = node.ToBytes();
        var decoded = MstNodeData.FromBytes(bytes);

        Assert.NotNull(decoded.Left);
        Assert.Equal(leftCid, decoded.Left);
        Assert.Single(decoded.Entries);
    }

    [Fact]
    public void Roundtrip_NodeWithSubtreeLinks()
    {
        var treeCid = CidComputation.ComputeBinaryForDagCbor([0xA0]);
        var valueCid = CidComputation.ComputeBinaryForDagCbor([0x01]);

        var node = new MstNodeData
        {
            Left = null,
            Entries =
            [
                new MstTreeEntry(0, "key"u8.ToArray(), valueCid, treeCid),
            ],
        };

        var bytes = node.ToBytes();
        var decoded = MstNodeData.FromBytes(bytes);

        Assert.NotNull(decoded.Entries[0].Tree);
        Assert.Equal(treeCid, decoded.Entries[0].Tree);
    }

    [Fact]
    public void ToBytes_IsDeterministic()
    {
        var valueCid = CidComputation.ComputeBinaryForDagCbor([0x42]);

        var node = new MstNodeData
        {
            Left = null,
            Entries =
            [
                new MstTreeEntry(0, "a"u8.ToArray(), valueCid, null),
                new MstTreeEntry(0, "b"u8.ToArray(), valueCid, null),
            ],
        };

        var bytes1 = node.ToBytes();
        var bytes2 = node.ToBytes();
        Assert.Equal(bytes1, bytes2);
    }

    [Fact]
    public void ToBytes_AbsentLinks_AreWrittenAsNull()
    {
        // {"e": [{"k": h'61', "p": 0, "t": null, "v": <cid>}], "l": null}: the spec schema always
        // carries l and t. Leaving them out hashes to a CID no other implementation produces.
        var valueCid = CidComputation.ComputeBinaryForDagCbor([0xA0]);
        var node = new MstNodeData { Entries = [new MstTreeEntry(0, "a"u8.ToArray(), valueCid, null)] };

        var expected = "a26165" + "81" + "a4616b4161" + "617000" + "6174f6" + "6176d82a582500" +
                       Convert.ToHexStringLower(valueCid) + "616cf6";

        Assert.Equal(expected, Convert.ToHexStringLower(node.ToBytes()));
    }

    [Fact]
    public void FromBytes_OmittedLinks_ReadAsNull()
    {
        // Nodes written without l/t (as this SDK used to) still decode.
        var valueCid = CidComputation.ComputeBinaryForDagCbor([0xA0]);
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(1);
        writer.WriteTextString("e");
        writer.WriteStartArray(1);
        writer.WriteStartMap(3);
        writer.WriteTextString("k");
        writer.WriteByteString("a/b"u8);
        writer.WriteTextString("p");
        writer.WriteInt32(0);
        writer.WriteTextString("v");
        writer.WriteTag((CborTag)42);
        writer.WriteByteString([0x00, .. valueCid]);
        writer.WriteEndMap();
        writer.WriteEndArray();
        writer.WriteEndMap();

        var decoded = MstNodeData.FromBytes(writer.Encode());

        Assert.Null(decoded.Left);
        Assert.Null(Assert.Single(decoded.Entries).Tree);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, 0)]       // the first entry has no previous key
    [InlineData(4, 3)]       // one past the previous key
    [InlineData(int.MaxValue, 3)]
    public void FromBytes_PrefixOutsideThePreviousKey_ThrowsFormatException(int secondPrefix, int firstKeyLength)
    {
        var valueCid = CidComputation.ComputeBinaryForDagCbor([0xA0]);
        var entries = new List<MstTreeEntry>();
        if (firstKeyLength > 0)
            entries.Add(new MstTreeEntry(0, new byte[firstKeyLength], valueCid, null));
        entries.Add(new MstTreeEntry(secondPrefix, "x"u8.ToArray(), valueCid, null));

        var bytes = new MstNodeData { Entries = entries }.ToBytes();

        Assert.Throws<FormatException>(() => MstNodeData.FromBytes(bytes));
    }

    [Theory]
    [InlineData("")]
    [InlineData("80")]                     // an array, not a map
    [InlineData("a1616581a1616b4161")]     // an entry without v
    [InlineData("a1616581a2616b416161760a")] // v is not a link
    [InlineData("a161658161")]             // truncated
    [InlineData("bf6165806161f6ff")]       // indefinite-length map
    public void FromBytes_Malformed_ThrowsFormatException(string hex)
    {
        Assert.Throws<FormatException>(() => MstNodeData.FromBytes(Convert.FromHexString(hex)));
    }
}
