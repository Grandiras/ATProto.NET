using System.Buffers;
using ATProtoNet.Data;
using ATProtoNet.Data.Car;

namespace ATProtoNet.Tests.Data;

public class CarTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CarBlock Record = Block(CidCodec.DagCbor, [0xa1, 0x61, 0x61, 0x01]);
    private static readonly CarBlock Blob = Block(CidCodec.Raw, "hello"u8.ToArray());

    [Fact]
    [Trait("Spec", "DAT-62")]
    public void Write_RootsAndBlocks_RoundTripsAndPassesVerification()
    {
        var car = Write([Record.Cid], [Record, Blob]);

        var file = CarReader.Read(car, new() { VerifyBlockCids = true });

        Assert.Equal([Record.Cid], file.Header.Roots);
        Assert.Equal([Record.Cid, Blob.Cid], file.Blocks.Select(b => b.Cid));
        Assert.Equal(Blob.Data.ToArray(), file.Blocks[1].Data.ToArray());
    }

    [Fact]
    [Trait("Spec", "DAT-62")]
    public void Write_NoRootsNoBlocks_IsAHeaderOnlyCar()
    {
        var car = Write([], []);

        Assert.Equal("11a265726f6f7473806776657273696f6e01", Convert.ToHexStringLower(car));
        Assert.Empty(CarReader.Read(car).Blocks);
    }

    [Fact]
    [Trait("Spec", "DAT-60")]
    public void Read_Buffer_BlocksSliceTheInput()
    {
        var car = Write([], [Blob]);

        var data = CarReader.Read(car).Blocks[0].Data;

        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(data, out var segment));
        Assert.Same(car, segment.Array);
    }

    [Fact]
    [Trait("Spec", "DAT-61")]
    public void TryGetBlock_DuplicateCid_ReturnsFirstOccurrenceAndKeepsBoth()
    {
        var first = new CarBlock(Blob.Cid, "first"u8.ToArray());
        var car = Write([], [first, Blob]);

        var file = CarReader.Read(car);

        Assert.Equal(2, file.Blocks.Count);
        Assert.True(file.TryGetBlock(Blob.Cid, out var data));
        Assert.Equal("first"u8.ToArray(), data.ToArray());
        Assert.False(file.TryGetBlock(Record.Cid, out _));
    }

    [Fact]
    [Trait("Spec", "DAT-58")]
    public void Read_VerifyingTamperedBlock_ThrowsFormatException()
    {
        var car = Write([], [new CarBlock(Blob.Cid, "jello"u8.ToArray())]);

        Assert.Single(CarReader.Read(car).Blocks);
        Assert.Throws<FormatException>(() => CarReader.Read(car, new() { VerifyBlockCids = true }));
    }

    [Theory]
    [Trait("Spec", "DAT-28")]
    [Trait("Spec", "DAT-55")]
    [Trait("Spec", "DAT-56")]
    [Trait("Spec", "DAT-57")]
    [InlineData("")] // no header
    [InlineData("00")] // zero header length
    [InlineData("8000")] // non-minimal header length
    [InlineData("0aa16776657273696f6e01")] // no roots
    [InlineData("11a265726f6f7473806776657273696f6e02")] // version 2
    [InlineData("12a265726f6f7473806776657273696f6e01f6")] // bytes after the header map
    [InlineData("11a26776657273696f6e0165726f6f747380")] // keys out of order
    [InlineData("11a265726f6f7473806776657273696f6e0100")] // zero block length
    [InlineData("11a265726f6f7473806776657273696f6e010301711220")] // CID past the end of its block
    [InlineData("11a265726f6f7473806776657273696f6e01ff01")] // block longer than the file
    public void Read_Malformed_ThrowsFormatException(string hex) =>
        Assert.Throws<FormatException>(() => CarReader.Read(Convert.FromHexString(hex)));

    [Fact]
    [Trait("Spec", "DAT-12")]
    [Trait("Spec", "DAT-57")]
    public void Read_CidV0Block_IsReportedAsCidV0()
    {
        var car = Convert.FromHexString("11a265726f6f7473806776657273696f6e01" + "22" + "1220" + new string('0', 64));

        Assert.Contains("CIDv0", Assert.Throws<FormatException>(() => CarReader.Read(car)).Message);
    }

    [Fact]
    [Trait("Spec", "DAT-31")]
    [Trait("Spec", "DAT-55")]
    public void Read_UnknownHeaderKeyNestedPastTheLimit_ThrowsDataLimitExceeded()
    {
        // {"roots": [], "version": 1, "zzzzzzzz": [[[…]]]}, the map being the first of 65 levels.
        var header = "a365726f6f7473806776657273696f6e0168" + "7a7a7a7a7a7a7a7a" + string.Concat(Enumerable.Repeat("81", 64)) + "01";
        var car = Convert.FromHexString(Convert.ToHexStringLower([(byte)(header.Length / 2)]) + header);

        Assert.Equal("MaxDepth", Assert.Throws<DataLimitExceededException>(() => CarReader.Read(car)).Limit);
    }

    [Theory]
    [Trait("Spec", "DAT-59")]
    [InlineData(nameof(CarReaderOptions.MaxTotalBytes))]
    [InlineData(nameof(CarReaderOptions.MaxBlocks))]
    [InlineData(nameof(CarReaderOptions.MaxBlockBytes))]
    [InlineData(nameof(CarReaderOptions.MaxHeaderBytes))]
    public async Task Read_LimitExceeded_ThrowsDataLimitExceededNamingIt(string limit)
    {
        var car = Write([Record.Cid], [Record, Blob]);
        var options = limit switch
        {
            nameof(CarReaderOptions.MaxTotalBytes) => new CarReaderOptions { MaxTotalBytes = car.Length - 1 },
            nameof(CarReaderOptions.MaxBlocks) => new CarReaderOptions { MaxBlocks = 1 },
            nameof(CarReaderOptions.MaxBlockBytes) => new CarReaderOptions { MaxBlockBytes = Cid.BinaryLength + 4 },
            _ => new CarReaderOptions { MaxHeaderBytes = 8 },
        };

        Assert.Equal(limit, Assert.Throws<DataLimitExceededException>(() => CarReader.Read(car, options)).Limit);
        var streamed = await Assert.ThrowsAsync<DataLimitExceededException>(async () => await ReadStream(car, options with { MaxTotalBytes = limit == nameof(CarReaderOptions.MaxTotalBytes) ? 50 : options.MaxTotalBytes }));
        Assert.Equal(limit, streamed.Limit);
    }

    [Fact]
    [Trait("Spec", "DAT-60")]
    [Trait("Spec", "DAT-62")]
    public async Task ReadBlocksAsync_AfterReadHeaderAsync_StreamsTheBlocksWrittenByWriteAsync()
    {
        using var stream = new MemoryStream();
        await CarWriter.WriteAsync(stream, [Record.Cid], Async([Record, Blob]), Ct);
        Assert.Equal(Write([Record.Cid], [Record, Blob]), stream.ToArray());
        stream.Position = 0;

        var header = await CarReader.ReadHeaderAsync(stream, cancellationToken: Ct);
        var blocks = await CarReader.ReadBlocksAsync(stream, new() { VerifyBlockCids = true }, Ct).ToListAsync(Ct);

        Assert.Equal([Record.Cid], header.Roots);
        Assert.Equal(new[] { Record, Blob }.Select(b => (b.Cid, Convert.ToHexString(b.Data.Span))), blocks.Select(b => (b.Cid, Convert.ToHexString(b.Data.Span))));
    }

    [Fact]
    [Trait("Spec", "DAT-33")]
    [Trait("Spec", "DAT-57")]
    public async Task ReadBlocksAsync_TruncatedBlock_ThrowsFormatException()
    {
        var car = Write([], [Blob]);
        using var stream = new MemoryStream(car[..^1]);

        await CarReader.ReadHeaderAsync(stream, cancellationToken: Ct);
        await Assert.ThrowsAsync<FormatException>(async () => await CarReader.ReadBlocksAsync(stream, cancellationToken: Ct).ToListAsync(Ct));
    }

    private static async Task<List<CarBlock>> ReadStream(byte[] car, CarReaderOptions options)
    {
        using var stream = new MemoryStream(car);
        await CarReader.ReadHeaderAsync(stream, options, Ct);
        return await CarReader.ReadBlocksAsync(stream, options, Ct).ToListAsync(Ct);
    }

    private static CarBlock Block(CidCodec codec, byte[] data) => new(Cid.Compute(codec, data), data);

    private static byte[] Write(Cid[] roots, CarBlock[] blocks)
    {
        var output = new ArrayBufferWriter<byte>();
        CarWriter.Write(output, roots, blocks);
        return output.WrittenSpan.ToArray();
    }

    private static async IAsyncEnumerable<T> Async<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
