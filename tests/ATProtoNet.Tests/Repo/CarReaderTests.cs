using System.Text;
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

        // CIDv1, raw codec, SHA-256: the block bytes are opaque here.
        var cid = CidComputation.ComputeBinaryForRaw(blockData);

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
        var cid = CidComputation.ComputeBinaryForRaw(blockData);

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
        Assert.Equal(CidComputation.ComputeBinaryForRaw(data), reader.Roots[0]);
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
    public void FindBlock_RepeatedLookupsResolveEveryBlock()
    {
        // FindBlock builds a CID index on first use; make sure every block is reachable
        // through it and that the index survives being reused.
        var blocks = new List<CarBlock>();
        for (var i = 0; i < 64; i++)
        {
            var data = Encoding.UTF8.GetBytes($"block-{i}");
            blocks.Add(new CarBlock(CidComputation.ComputeBinaryForDagCbor(data), data));
        }

        var reader = CarReader.FromBytes(CarWriter.Write(blocks[0].Cid, blocks));

        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var block in blocks)
            {
                var found = reader.FindBlock(block.Cid);
                Assert.NotNull(found);
                Assert.Equal(block.Data, found.Data);
            }
        }

        Assert.Null(reader.FindBlock(CidComputation.ComputeBinaryForDagCbor("absent"u8)));
    }

    [Fact]
    public void FindBlock_DuplicateCid_ReturnsFirstOccurrence()
    {
        // A CAR is not required to be duplicate-free. The linear scan FindBlock replaced
        // returned the earliest match, so the index must too.
        var data = "duplicated"u8.ToArray();
        var cid = CidComputation.ComputeBinaryForDagCbor(data);
        var reader = CarReader.FromBytes(
            CarWriter.Write(cid, [new CarBlock(cid, data), new CarBlock(cid, "shadowed"u8.ToArray())]));

        Assert.Equal(2, reader.Blocks.Count);
        Assert.Equal(data, reader.FindBlock(cid)!.Data);
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
        var cid1 = CidComputation.ComputeBinaryForRaw(block1);
        WriteUvarint(result, (ulong)(cid1.Length + block1.Length));
        result.AddRange(cid1);
        result.AddRange(block1);

        // Block 2
        var cid2 = CidComputation.ComputeBinaryForRaw(block2);
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

    // ── Untrusted input ──────────────────────────────────────

    /// <summary>Frames a raw header and raw block sections into a CAR, with no validation.</summary>
    private static byte[] Frame(byte[] header, params byte[][] sections)
    {
        var result = new List<byte>();
        WriteUvarint(result, (ulong)header.Length);
        result.AddRange(header);
        foreach (var section in sections)
        {
            WriteUvarint(result, (ulong)section.Length);
            result.AddRange(section);
        }
        return result.ToArray();
    }

    /// <summary>A valid <c>{"roots": [], "version": 1}</c> header.</summary>
    private static byte[] EmptyHeader => CarWriter.EncodeHeader([]);

    [Fact]
    public void FromBytes_CidV0Block_ThrowsFormatException()
    {
        // A CIDv0 is a bare SHA-256 multihash that implies dag-pb, which AT Protocol never uses.
        // It used to be accepted and then pass VerifyBlockCid, sidestepping the codec rule.
        var data = "cidv0"u8.ToArray();
        byte[] cid = [0x12, 0x20, .. System.Security.Cryptography.SHA256.HashData(data)];

        var car = Frame(EmptyHeader, [.. cid, .. data]);

        Assert.Throws<FormatException>(() => CarReader.FromBytes(car));
    }

    [Fact]
    public void VerifyBlockCid_CidV0Block_ReportsUnknownCodec()
    {
        var data = "cidv0"u8.ToArray();
        byte[] cid = [0x12, 0x20, .. System.Security.Cryptography.SHA256.HashData(data)];

        Assert.Equal(BlockCidVerification.UnknownCodec, CarReader.VerifyBlockCid(new CarBlock(cid, data)));
    }

    [Fact]
    public void FromBytes_UnsupportedCidVersion_ThrowsFormatException()
    {
        var car = Frame(EmptyHeader, [0x02, 0x71, 0x12, 0x01, 0xAA, 0xBB]);

        Assert.Throws<FormatException>(() => CarReader.FromBytes(car));
    }

    [Fact]
    public void FromBytes_CidDigestPastTheEndOfItsBlock_ThrowsFormatException()
    {
        // The digest claims 32 bytes, but the section holds 4 in all after the CID prefix.
        var car = Frame(EmptyHeader, [0x01, 0x71, 0x12, 0x20, 0xAA, 0xBB, 0xCC, 0xDD]);

        Assert.Throws<FormatException>(() => CarReader.FromBytes(car));
    }

    [Fact]
    public void FromBytes_BlockLongerThanTheFile_ThrowsFormatException()
    {
        var car = new List<byte>(Frame(EmptyHeader));
        WriteUvarint(car, 1000);
        car.AddRange(CidComputation.ComputeBinaryForRaw("x"u8));

        Assert.Throws<FormatException>(() => CarReader.FromBytes(car.ToArray()));
    }

    [Theory]
    [InlineData(1UL << 31)]
    [InlineData(1UL << 32)]
    [InlineData((1UL << 63) - 1)]
    public void FromBytes_HugeLengthPrefixes_ThrowFormatException(ulong length)
    {
        // Lengths that overflow an int must be rejected, not wrapped into a negative slice.
        var header = new List<byte>();
        WriteUvarint(header, length);
        header.AddRange(EmptyHeader);
        Assert.Throws<FormatException>(() => CarReader.FromBytes(header.ToArray()));

        var block = new List<byte>(Frame(EmptyHeader));
        WriteUvarint(block, length);
        block.AddRange(CidComputation.ComputeBinaryForRaw("x"u8));
        Assert.Throws<FormatException>(() => CarReader.FromBytes(block.ToArray()));
    }

    [Fact]
    public void FromBytes_DeeplyNestedHeaderValue_IsSkippedWithoutOverflowingTheStack()
    {
        // ~100 KB of nested arrays under a key the reader does not know. The hand-written CBOR
        // skipper this replaced recursed once per level and took the process down.
        const int depth = 100_000;
        var header = new List<byte> { 0xA3 };
        header.AddRange([0x67, .. "version"u8, 0x01]);
        header.AddRange([0x65, .. "roots"u8, 0x80]);
        header.AddRange([0x65, .. "junk!"u8]);
        header.AddRange(Enumerable.Repeat((byte)0x81, depth));
        header.Add(0x00);

        var reader = CarReader.FromBytes(Frame(header.ToArray()));

        Assert.Equal(1, reader.Header.Version);
        Assert.Empty(reader.Roots);
    }

    [Fact]
    public void FromBytes_TruncatedDeeplyNestedHeader_ThrowsFormatException()
    {
        List<byte> header = [0xA1, 0x65, .. "junk!"u8];
        header.AddRange(Enumerable.Repeat((byte)0x81, 100_000));

        Assert.Throws<FormatException>(() => CarReader.FromBytes(Frame(header.ToArray())));
    }

    public static TheoryData<string, string> MalformedHeaders => new()
    {
        { "not a map", "8101" },
        { "no version", "a165726f6f747380" },
        { "version not an integer", "a26776657273696f6e61316572 6f6f747380".Replace(" ", "") },
        { "version out of range", "a26776657273696f6e1b0000000100000000" + "65726f6f747380" },
        { "roots not an array", "a26776657273696f6e0165726f6f747301" },
        { "root not tagged", "a26776657273696f6e0165726f6f747381420001" },
        { "root missing the 0x00 prefix", "a26776657273696f6e0165726f6f747381d82a420101" },
        { "non-string key", "a201016776657273696f6e01" },
        { "trailing bytes", "a26776657273696f6e0165726f6f74738000" },
        { "truncated", "a26776657273696f6e01" },
    };

    [Theory]
    [MemberData(nameof(MalformedHeaders))]
    public void FromBytes_MalformedHeader_ThrowsFormatException(string reason, string headerHex)
    {
        _ = reason;
        var car = Frame(Convert.FromHexString(headerHex));

        Assert.Throws<FormatException>(() => CarReader.FromBytes(car));
    }

    [Fact]
    public void FromBytes_UnsupportedVersion_ThrowsFormatException()
    {
        // {"roots": [], "version": 2}
        var car = Frame(Convert.FromHexString("a265726f6f7473806776657273696f6e02"));

        Assert.Throws<FormatException>(() => CarReader.FromBytes(car));
    }

    // ── Varints ──────────────────────────────────────────────

    [Theory]
    [InlineData("00", 0UL, 1)]
    [InlineData("7f", 127UL, 1)]
    [InlineData("8001", 128UL, 2)]
    [InlineData("ff7f", 16383UL, 2)]
    [InlineData("808001", 16384UL, 3)]
    [InlineData("ffffff7f", 268435455UL, 4)]
    [InlineData("ffffffffffffffff7f", 9223372036854775807UL, 9)]
    public void ReadUvarint_ValidEncodings_DecodeAndAdvance(string hex, ulong expected, int length)
    {
        var bytes = Convert.FromHexString(hex + "aa"); // a trailing byte the read must not touch
        var offset = 0;

        Assert.Equal(expected, CarReader.ReadUvarint(bytes, ref offset));
        Assert.Equal(length, offset);
    }

    [Theory]
    [InlineData("")]                     // nothing at all
    [InlineData("80")]                   // continuation bit, then the end
    [InlineData("ffff")]
    [InlineData("ffffffffffffffffff")]   // nine continuation bytes
    public void ReadUvarint_Truncated_ThrowsFormatException(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        var offset = 0;

        Assert.Throws<FormatException>(() => CarReader.ReadUvarint(bytes, ref offset));
    }

    [Theory]
    [InlineData("ffffffffffffffffff01")]   // 2^63: ten bytes
    [InlineData("ffffffffffffffffff7f")]
    [InlineData("80808080808080808001")]
    public void ReadUvarint_LongerThanNineBytes_ThrowsFormatException(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        var offset = 0;

        Assert.Throws<FormatException>(() => CarReader.ReadUvarint(bytes, ref offset));
    }

    [Theory]
    [InlineData("8000")]     // 0 in two bytes
    [InlineData("ff00")]     // 127 in two bytes
    [InlineData("808000")]
    public void ReadUvarint_NotMinimallyEncoded_ThrowsFormatException(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        var offset = 0;

        Assert.Throws<FormatException>(() => CarReader.ReadUvarint(bytes, ref offset));
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(127UL)]
    [InlineData(128UL)]
    [InlineData(16384UL)]
    [InlineData(2097151UL)]
    [InlineData(2097152UL)]
    [InlineData(1UL << 35)]
    [InlineData(long.MaxValue)]
    public void ReadUvarint_RoundTripsCarWriterEncoding(ulong value)
    {
        Span<byte> buffer = stackalloc byte[10];
        var length = CarWriter.EncodeUvarint(value, buffer);
        var offset = 0;

        Assert.Equal(value, CarReader.ReadUvarint(buffer[..length], ref offset));
        Assert.Equal(length, offset);
    }

    [Fact]
    public void FromBytes_BlockNeedingAThreeByteLengthPrefix_RoundTrips()
    {
        // 20,000 bytes of data plus the CID takes the section length past 2^14.
        var data = new byte[20_000];
        Random.Shared.NextBytes(data);
        var block = new CarBlock(CidComputation.ComputeBinaryForRaw(data), data);

        var reader = CarReader.FromBytes(CarWriter.Write(block.Cid, [block]), verifyBlockCids: true);

        Assert.Equal(data, Assert.Single(reader.Blocks).Data);
    }
}
