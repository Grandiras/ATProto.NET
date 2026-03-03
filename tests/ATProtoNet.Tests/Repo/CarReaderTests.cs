using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

/// <summary>Tests for <see cref="CarReader"/>.</summary>
public sealed class CarReaderTests
{
    /// <summary>
    /// Builds a minimal valid CAR v1 file with a single block.
    /// CAR format: varint(headerLen) + CBOR(header) + varint(blockLen) + CID + data
    /// </summary>
    private static byte[] BuildMinimalCar(byte[] blockData)
    {
        // Build CBOR header: { "version": 1, "roots": [] }
        // Map with 2 entries
        var cborHeader = new List<byte>();
        cborHeader.Add(0xA2); // map(2)

        // Key: "version" (text string, 7 bytes)
        cborHeader.Add(0x67); // text(7)
        cborHeader.AddRange("version"u8.ToArray());

        // Value: 1 (unsigned int)
        cborHeader.Add(0x01);

        // Key: "roots" (text string, 5 bytes)
        cborHeader.Add(0x65); // text(5)
        cborHeader.AddRange("roots"u8.ToArray());

        // Value: empty array
        cborHeader.Add(0x80); // array(0)

        var header = cborHeader.ToArray();

        // CID for the block: CIDv1 + dag-cbor + sha2-256 identity
        // Simple CIDv0: 0x12 0x20 + 32 bytes of SHA-256
        var hash = System.Security.Cryptography.SHA256.HashData(blockData);
        var cid = new byte[34];
        cid[0] = 0x12; // sha2-256
        cid[1] = 0x20; // 32 bytes
        hash.CopyTo(cid, 2);

        // Build the full CAR
        var result = new List<byte>();

        // Header length varint + header
        WriteUvarint(result, (ulong)header.Length);
        result.AddRange(header);

        // Block: varint(cid.Length + data.Length) + cid + data
        var blockLen = cid.Length + blockData.Length;
        WriteUvarint(result, (ulong)blockLen);
        result.AddRange(cid);
        result.AddRange(blockData);

        return result.ToArray();
    }

    /// <summary>
    /// Builds a CAR with a root CID pointing to a specific block.
    /// </summary>
    private static byte[] BuildCarWithRoot(byte[] blockData)
    {
        // Compute CID for the block
        var hash = System.Security.Cryptography.SHA256.HashData(blockData);
        var cid = new byte[34];
        cid[0] = 0x12;
        cid[1] = 0x20;
        hash.CopyTo(cid, 2);

        // Build CBOR header: { "version": 1, "roots": [ <cid> ] }
        var cborHeader = new List<byte>();
        cborHeader.Add(0xA2); // map(2)

        // "version": 1
        cborHeader.Add(0x67);
        cborHeader.AddRange("version"u8.ToArray());
        cborHeader.Add(0x01);

        // "roots": [ tag(42, bytes(<cid>)) ]
        cborHeader.Add(0x65);
        cborHeader.AddRange("roots"u8.ToArray());
        cborHeader.Add(0x81); // array(1)
        cborHeader.Add(0xD8); // tag(42)
        cborHeader.Add(42);

        // Byte string with identity multibase prefix
        var cidWithPrefix = new byte[1 + cid.Length];
        cidWithPrefix[0] = 0x00; // identity multibase
        cid.CopyTo(cidWithPrefix, 1);

        if (cidWithPrefix.Length < 24)
            cborHeader.Add((byte)(0x40 | cidWithPrefix.Length));
        else
        {
            cborHeader.Add(0x58);
            cborHeader.Add((byte)cidWithPrefix.Length);
        }
        cborHeader.AddRange(cidWithPrefix);

        var header = cborHeader.ToArray();

        // Build full CAR
        var result = new List<byte>();
        WriteUvarint(result, (ulong)header.Length);
        result.AddRange(header);

        // Block
        var blockLen = cid.Length + blockData.Length;
        WriteUvarint(result, (ulong)blockLen);
        result.AddRange(cid);
        result.AddRange(blockData);

        return result.ToArray();
    }

    private static void WriteUvarint(List<byte> output, ulong value)
    {
        while (value >= 0x80)
        {
            output.Add((byte)(value | 0x80));
            value >>= 7;
        }
        output.Add((byte)value);
    }

    // ── Tests ────────────────────────────────────────────────

    [Fact]
    public void FromBytes_ParsesMinimalCar()
    {
        var data = "Hello CAR"u8.ToArray();
        var carBytes = BuildMinimalCar(data);

        var reader = CarReader.FromBytes(carBytes);

        Assert.Equal(1, reader.Header.Version);
        Assert.Single(reader.Blocks);
        Assert.Equal(data, reader.Blocks[0].Data);
    }

    [Fact]
    public void FromBytes_ParsesHeaderVersion()
    {
        var carBytes = BuildMinimalCar([0x01, 0x02, 0x03]);
        var reader = CarReader.FromBytes(carBytes);

        Assert.Equal(1, reader.Header.Version);
    }

    [Fact]
    public void FromBytes_ParsesEmptyRoots()
    {
        var carBytes = BuildMinimalCar([0xAA]);
        var reader = CarReader.FromBytes(carBytes);

        Assert.Empty(reader.Roots);
    }

    [Fact]
    public void FromBytes_ParsesRoots()
    {
        var data = "root block"u8.ToArray();
        var carBytes = BuildCarWithRoot(data);

        var reader = CarReader.FromBytes(carBytes);

        Assert.Single(reader.Roots);
        Assert.Equal(34, reader.Roots[0].Length); // SHA-256 CID = 2 + 32
    }

    [Fact]
    public void FindBlock_ReturnsMatchingBlock()
    {
        var data = "find me"u8.ToArray();
        var carBytes = BuildMinimalCar(data);
        var reader = CarReader.FromBytes(carBytes);

        var cid = reader.Blocks[0].Cid;
        var found = reader.FindBlock(cid);

        Assert.NotNull(found);
        Assert.Equal(data, found.Data);
    }

    [Fact]
    public void FindBlock_ReturnsNullForMissing()
    {
        var carBytes = BuildMinimalCar([0x01]);
        var reader = CarReader.FromBytes(carBytes);

        var result = reader.FindBlock(new byte[] { 0xFF, 0xFF });

        Assert.Null(result);
    }

    [Fact]
    public void GetRootBlock_ReturnsRootData()
    {
        var data = "root content"u8.ToArray();
        var carBytes = BuildCarWithRoot(data);
        var reader = CarReader.FromBytes(carBytes);

        var root = reader.GetRootBlock();

        Assert.NotNull(root);
        Assert.Equal(data, root.Data);
    }

    [Fact]
    public void GetRootBlock_ReturnsNullWhenNoRoots()
    {
        var carBytes = BuildMinimalCar([0x01]);
        var reader = CarReader.FromBytes(carBytes);

        Assert.Null(reader.GetRootBlock());
    }

    [Fact]
    public void CarBlock_CidHex_ReturnsHexString()
    {
        var carBytes = BuildMinimalCar([0x42]);
        var reader = CarReader.FromBytes(carBytes);

        var hex = reader.Blocks[0].CidHex;

        Assert.NotEmpty(hex);
        Assert.Matches("^[0-9a-f]+$", hex);
    }

    [Fact]
    public void CarBlock_DataLength_ReturnsCorrectSize()
    {
        var data = new byte[100];
        Random.Shared.NextBytes(data);
        var carBytes = BuildMinimalCar(data);
        var reader = CarReader.FromBytes(carBytes);

        Assert.Equal(100, reader.Blocks[0].DataLength);
    }

    [Fact]
    public void FromBytes_MultipleBlocks_ParsesAll()
    {
        var block1 = "block one"u8.ToArray();
        var block2 = "block two"u8.ToArray();

        // Build a CAR with two blocks
        var cborHeader = new List<byte>();
        cborHeader.Add(0xA2);
        cborHeader.Add(0x67);
        cborHeader.AddRange("version"u8.ToArray());
        cborHeader.Add(0x01);
        cborHeader.Add(0x65);
        cborHeader.AddRange("roots"u8.ToArray());
        cborHeader.Add(0x80);

        var header = cborHeader.ToArray();
        var result = new List<byte>();
        WriteUvarint(result, (ulong)header.Length);
        result.AddRange(header);

        // Block 1
        var hash1 = System.Security.Cryptography.SHA256.HashData(block1);
        var cid1 = new byte[34];
        cid1[0] = 0x12; cid1[1] = 0x20;
        hash1.CopyTo(cid1, 2);
        WriteUvarint(result, (ulong)(cid1.Length + block1.Length));
        result.AddRange(cid1);
        result.AddRange(block1);

        // Block 2
        var hash2 = System.Security.Cryptography.SHA256.HashData(block2);
        var cid2 = new byte[34];
        cid2[0] = 0x12; cid2[1] = 0x20;
        hash2.CopyTo(cid2, 2);
        WriteUvarint(result, (ulong)(cid2.Length + block2.Length));
        result.AddRange(cid2);
        result.AddRange(block2);

        var reader = CarReader.FromBytes(result.ToArray());

        Assert.Equal(2, reader.Blocks.Count);
        Assert.Equal(block1, reader.Blocks[0].Data);
        Assert.Equal(block2, reader.Blocks[1].Data);
    }

    [Fact]
    public void FromBytes_EmptyData_Throws()
    {
        Assert.Throws<FormatException>(() => CarReader.FromBytes(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public async Task FromStreamAsync_ParsesSameAsFromBytes()
    {
        var data = "stream test"u8.ToArray();
        var carBytes = BuildMinimalCar(data);

        using var stream = new MemoryStream(carBytes);
        var reader = await CarReader.FromStreamAsync(stream);

        Assert.Single(reader.Blocks);
        Assert.Equal(data, reader.Blocks[0].Data);
    }
}
