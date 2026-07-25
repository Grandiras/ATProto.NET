using System.Text;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

public sealed class CarWriterTests
{
    private static CarBlock Block(string content)
    {
        var data = Encoding.UTF8.GetBytes(content);
        return new CarBlock(CidComputation.ComputeBinaryForDagCbor(data), data);
    }

    // ── Round-trip through CarReader ─────────────────────────

    [Fact]
    public void Write_SingleRootAndBlocks_RoundTripsThroughCarReader()
    {
        var root = Block("root");
        var child = Block("child");

        var car = CarWriter.Write(root.Cid, new[] { root, child });
        var reader = CarReader.FromBytes(car);

        Assert.Equal(1, reader.Header.Version);
        Assert.Single(reader.Roots);
        Assert.Equal(root.Cid, reader.Roots[0]);
        Assert.Equal(2, reader.Blocks.Count);
        Assert.Equal(root.Data, reader.Blocks[0].Data);
        Assert.Equal(child.Data, reader.Blocks[1].Data);
    }

    [Fact]
    public void Write_BlocksCarryComputedCids_VerifyAllBlockCidsPasses()
    {
        var blocks = new[] { Block("a"), Block("b"), Block("c") };

        var car = CarWriter.Write(blocks[0].Cid, blocks);
        var reader = CarReader.FromBytes(car, verifyBlockCids: true);

        reader.VerifyAllBlockCids();
        Assert.Equal(3, reader.Blocks.Count);
    }

    [Fact]
    public void Write_NoRoots_ProducesRootlessCar()
    {
        var car = CarWriter.Write(Array.Empty<byte[]>(), new[] { Block("only") });
        var reader = CarReader.FromBytes(car);

        Assert.Empty(reader.Roots);
        Assert.Single(reader.Blocks);
    }

    [Fact]
    public void Write_NoBlocks_ProducesHeaderOnlyCar()
    {
        var root = Block("root");
        var car = CarWriter.Write(root.Cid, Array.Empty<CarBlock>());
        var reader = CarReader.FromBytes(car);

        Assert.Single(reader.Roots);
        Assert.Empty(reader.Blocks);
    }

    [Fact]
    public void Write_FromBlockDictionary_UsesBase32CidKeys()
    {
        var block = Block("dictionary");
        var key = CidComputation.EncodeCidToString(block.Cid);

        var car = CarWriter.Write(block.Cid, new Dictionary<string, byte[]> { [key] = block.Data });
        var reader = CarReader.FromBytes(car, verifyBlockCids: true);

        Assert.Single(reader.Blocks);
        Assert.Equal(block.Cid, reader.Blocks[0].Cid);
    }

    [Fact]
    public void Write_MerkleSearchTreeBlocks_RoundTrips()
    {
        var mst = MerkleSearchTree.Create();
        for (var i = 0; i < 40; i++)
            mst.Add($"app.bsky.feed.post/{i:D4}", CidComputation.ComputeBinaryForDagCbor(Encoding.UTF8.GetBytes($"r{i}")));

        var (rootCid, blocks) = mst.Serialize();
        var car = CarWriter.Write(rootCid, blocks);

        var reader = CarReader.FromBytes(car, verifyBlockCids: true);
        Assert.Equal(rootCid, reader.Roots[0]);
        Assert.Equal(blocks.Count, reader.Blocks.Count);

        // The tree can be rebuilt purely from the CAR's blocks.
        var byCid = reader.Blocks.ToDictionary(
            b => CidComputation.EncodeCidToString(b.Cid), b => b.Data, StringComparer.Ordinal);
        var restored = MerkleSearchTree.Deserialize(rootCid, cid => byCid.GetValueOrDefault(cid));

        Assert.Equal(40, restored.Count);
        Assert.True(restored.Validate());
    }

    // ── Streaming ────────────────────────────────────────────

    [Fact]
    public async Task WriteToAsync_ProducesIdenticalBytesToWrite()
    {
        var blocks = new[] { Block("x"), Block("y") };

        using var stream = new MemoryStream();
        await CarWriter.WriteToAsync(stream, new[] { blocks[0].Cid }, blocks);

        Assert.Equal(CarWriter.Write(blocks[0].Cid, blocks), stream.ToArray());
    }

    // ── Varint encoding ──────────────────────────────────────

    [Theory]
    [InlineData(0UL, 1)]
    [InlineData(127UL, 1)]
    [InlineData(128UL, 2)]
    [InlineData(16383UL, 2)]
    [InlineData(16384UL, 3)]
    [InlineData(ulong.MaxValue, 10)]
    public void EncodeUvarint_UsesMinimalLength(ulong value, int expectedLength)
    {
        Span<byte> buffer = stackalloc byte[10];
        Assert.Equal(expectedLength, CarWriter.EncodeUvarint(value, buffer));
    }

    [Fact]
    public void Write_BlockLargerThan127Bytes_UsesMultiByteVarint()
    {
        // A block whose length crosses the single-byte varint boundary would be truncated by a
        // naive length prefix, so the reader would resynchronize onto garbage.
        var data = new byte[5000];
        Random.Shared.NextBytes(data);
        var block = new CarBlock(CidComputation.ComputeBinaryForRaw(data), data);

        var reader = CarReader.FromBytes(CarWriter.Write(block.Cid, new[] { block }));

        Assert.Single(reader.Blocks);
        Assert.Equal(data, reader.Blocks[0].Data);
    }

    // ── Header encoding ──────────────────────────────────────

    [Fact]
    public void EncodeHeader_IsDagCborMapWithRootsAndVersion()
    {
        var root = Block("root").Cid;
        var header = CarWriter.EncodeHeader(new[] { root });

        var decoded = DagCborDecoder.Decode(header);
        Assert.Equal(1, decoded.GetProperty("version").GetInt32());

        // CID links decode to { "$link": "<base32 cid>" } in AT Protocol's JSON projection.
        var link = decoded.GetProperty("roots")[0].GetProperty("$link").GetString();
        Assert.Equal(CidComputation.EncodeCidToString(root), link);
    }
}
