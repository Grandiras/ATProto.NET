using System.Text;
using ATProtoNet.Data;
using ATProtoNet.Data.Star;

namespace ATProtoNet.Tests.Data;

public class StarTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] Header = Drisl("""{"$type":"atproto-space-repo","did":"did:plc:abc","rev":"3k2a","space":"did:plc:xyz/com.example.space/self"}""");
    private static readonly (string Path, ReadOnlyMemory<byte> Record)[] Entries =
    [
        ("app.bsky.feed.like/3k2a", Drisl("""{"$type":"app.bsky.feed.like","n":1}""")),
        ("app.bsky.feed.post/3k2a", Drisl("""{"text":"a"}""")),
        ("app.bsky.feed.post/3k2b", Drisl("""{"text":"b"}""")),
    ];

    [Fact]
    [Trait("Spec", "DAT-72")]
    [Trait("Spec", "DAT-75")]
    [Trait("Spec", "DAT-78")]
    [Trait("Spec", "DAT-76")]
    [Trait("Spec", "DAT-79")]
    public async Task WriteAsync_ThenRead_ReadsBackEveryEntryWithComputedCids()
    {
        var star = await Write(Entries);

        Assert.Equal("2a6c0100", Convert.ToHexStringLower(star[..4]));
        var (header, entries) = await Read(star);
        Assert.Equal(Header, header.Drisl.ToArray());
        Assert.Equal(Entries.Select(e => e.Path), entries.Select(e => e.Path));
        Assert.Equal(Entries.Select(e => Cid.Compute(CidCodec.DagCbor, e.Record.Span)), entries.Select(e => e.Cid));
        Assert.Equal(Entries.Select(e => Convert.ToHexString(e.Record.Span)), entries.Select(e => Convert.ToHexString(e.Record.Span)));
    }

    [Fact]
    [Trait("Spec", "DAT-76")]
    public async Task WriteAsync_SameInput_SameBytes() => Assert.Equal(await Write(Entries), await Write(Entries));

    [Fact]
    [Trait("Spec", "DAT-76")]
    public async Task WriteAsync_HeaderOverTheDefaultReaderLimit_ThrowsArgumentException()
    {
        var header = Drisl($$"""{"$type":"atproto-space-repo","pad":"{{new string('x', StarLimits.DefaultMaxHeaderBytes)}}"}""");
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(async () => await StarWriter.WriteAsync(stream, header, Entries.ToAsyncEnumerable(), Ct));
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [Trait("Spec", "DAT-74")]
    [InlineData(1, 0)] // out of order
    [InlineData(1, 1)] // duplicate
    public async Task ReadEntriesAsync_PathsNotIncreasing_ThrowsFormatException(int first, int second)
    {
        var star = Concat(await Write([]), Pair(Entries[first]), Pair(Entries[second]));

        await Assert.ThrowsAsync<FormatException>(() => Read(star));
        await Assert.ThrowsAsync<ArgumentException>(() => Write([Entries[first], Entries[second]]));
    }

    [Theory]
    [Trait("Spec", "DAT-74")]
    [Trait("Spec", "DAT-73")]
    [InlineData("17")] // a key length and nothing more
    [InlineData("1761")] // inside a key
    [InlineData("8000")] // a non-minimal length
    public async Task ReadEntriesAsync_TrailingOrTruncated_ThrowsFormatException(string tail)
    {
        var star = Concat(await Write(Entries), Convert.FromHexString(tail));

        await Assert.ThrowsAsync<FormatException>(() => Read(star));
    }

    [Theory]
    [Trait("Spec", "DAT-28")]
    [Trait("Spec", "DAT-75")]
    [InlineData("01")] // not a map
    [InlineData("a2616202616101")] // keys out of canonical order
    [InlineData("a10101")] // not a data-model map
    [InlineData("a1616101f6")] // bytes after the map
    public async Task ReadEntriesAsync_RecordNotStrictMap_ThrowsFormatException(string record)
    {
        var star = Concat(await Write([]), Pair(("app.bsky.feed.post/3k2a", Convert.FromHexString(record))));

        await Assert.ThrowsAsync<FormatException>(() => Read(star));
    }

    [Theory]
    [Trait("Spec", "DAT-77")]
    [InlineData("3k2a")]
    [InlineData("app.bsky.feed.post")]
    [InlineData("app.bsky.feed.post/")]
    [InlineData("app.bsky/3k2a")]
    [InlineData("app.bsky.feed.post/a/b")]
    [InlineData("app.bsky.feed.post/..")]
    [InlineData("did:plc:abc/app.bsky.feed.post/3k2a")]
    public async Task ReadEntriesAsync_KeyNotARecordPath_ThrowsFormatException(string path)
    {
        var star = Concat(await Write([]), Pair((path, Entries[0].Record)));

        await Assert.ThrowsAsync<FormatException>(() => Read(star));
        await Assert.ThrowsAsync<ArgumentException>(() => Write([(path, Entries[0].Record)]));
    }

    [Theory]
    [Trait("Spec", "DAT-73")]
    [InlineData(nameof(StarReaderOptions.MaxTotalBytes))]
    [InlineData(nameof(StarReaderOptions.MaxEntries))]
    [InlineData(nameof(StarReaderOptions.MaxEntryBytes))]
    [InlineData(nameof(StarReaderOptions.MaxHeaderBytes))]
    public async Task Read_LimitExceeded_ThrowsDataLimitExceededNamingIt(string limit)
    {
        var options = limit switch
        {
            nameof(StarReaderOptions.MaxTotalBytes) => new StarReaderOptions { MaxTotalBytes = 40 },
            nameof(StarReaderOptions.MaxEntries) => new StarReaderOptions { MaxEntries = 2 },
            nameof(StarReaderOptions.MaxEntryBytes) => new StarReaderOptions { MaxEntryBytes = 10 },
            _ => new StarReaderOptions { MaxHeaderBytes = 16 },
        };

        var error = await Assert.ThrowsAsync<DataLimitExceededException>(async () => await Read(await Write(Entries), options));
        Assert.Equal(limit, error.Limit);
    }

    [Theory]
    [Trait("Spec", "DAT-72")]
    [Trait("Spec", "DAT-73")]
    [Trait("Spec", "DAT-78")]
    [InlineData("2a6c0200")] // another version
    [InlineData("2a6c0100" + "00")] // empty metadata
    [InlineData("2a6c0100" + "0101")] // metadata not a map
    [InlineData("2a6c0100" + "03a10101")] // metadata not a data-model map
    [InlineData("2a6c0100" + "04a1616101")] // metadata without $type
    [InlineData("3aa265726f6f7473")] // a CAR
    public async Task ReadHeaderAsync_Malformed_ThrowsFormatException(string hex)
    {
        using var stream = new MemoryStream(Convert.FromHexString(hex));

        await Assert.ThrowsAsync<FormatException>(async () => await StarReader.ReadHeaderAsync(stream, cancellationToken: Ct));
    }

    private static async Task<byte[]> Write((string, ReadOnlyMemory<byte>)[] entries)
    {
        using var stream = new MemoryStream();
        await StarWriter.WriteAsync(stream, Header, entries.ToAsyncEnumerable(), Ct);
        return stream.ToArray();
    }

    private static async Task<(StarHeader, List<StarEntry>)> Read(byte[] star, StarReaderOptions? options = null)
    {
        using var stream = new MemoryStream(star);
        var header = await StarReader.ReadHeaderAsync(stream, options, Ct);
        return (header, await StarReader.ReadEntriesAsync(stream, options, Ct).ToListAsync(Ct));
    }

    private static byte[] Pair((string Path, ReadOnlyMemory<byte> Record) entry) =>
        [(byte)entry.Path.Length, .. Encoding.ASCII.GetBytes(entry.Path), (byte)entry.Record.Length, .. entry.Record.Span];

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    private static byte[] Drisl(string json) => DrislJson.ToDrisl(Encoding.UTF8.GetBytes(json));
}
