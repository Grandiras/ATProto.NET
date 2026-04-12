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
}
