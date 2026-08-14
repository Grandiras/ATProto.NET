using System.Text.Json;
using ATProtoNet.Repo;
using ATProtoNet.Streaming;
using static ATProtoNet.Tests.Streaming.JetstreamSegmentFixture;

namespace ATProtoNet.Tests.Streaming;

public class JetstreamSegmentReaderTests
{
    private static readonly PassThroughDecompressor Decompressor = new();

    [Fact]
    public void ReadHeader_ParsesEveryFixedField()
    {
        var segment = Segment([[Row.Commit(10), Row.Commit(11)]]);

        var header = JetstreamSegmentReader.ReadHeader(segment);

        Assert.Equal(0x0123456789abcdefUL, header.Checksum);
        Assert.Equal(1, header.Version);
        Assert.Equal(1u, header.BlockCount);
        Assert.Equal(2u, header.EventCount);
        Assert.Equal(1u, header.UniqueDidCount);
        Assert.Equal(10ul, header.MinSeq);
        Assert.Equal(11ul, header.MaxSeq);
        Assert.True(header.FooterOffset > JetstreamSegmentHeader.Size);
        Assert.Equal(header.FooterOffset, header.BlockIndexOffset);
    }

    [Fact]
    public void ReadHeader_RejectsBytesWithoutTheMagic()
    {
        var segment = Segment([[Row.Commit(1)]]);
        segment[0] = (byte)'x';

        var ex = Assert.Throws<JetstreamArchiveException>(() => JetstreamSegmentReader.ReadHeader(segment));
        Assert.Contains("jss0", ex.Message);
    }

    [Fact]
    public void ReadHeader_RejectsAnActiveSegment()
    {
        // A zero checksum at offset 4 is how an active (still appending) segment is detected: it
        // has no footer, so walking its blocks would run off the end of the written data.
        var segment = Segment([[Row.Commit(1)]], checksum: 0);

        var ex = Assert.Throws<JetstreamArchiveException>(() => JetstreamSegmentReader.ReadHeader(segment));
        Assert.Contains("active", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecodeBlock_ReadsColumnsBackInRowOrder()
    {
        var rows = JetstreamSegmentReader.DecodeBlock(BlockFrame([
            Row.Commit(7, collection: "app.bsky.feed.post", rkey: "aaa"),
            Row.Commit(8, collection: "app.bsky.feed.like", rkey: "bbbb",
                kind: JetstreamArchiveRowKind.Delete),
        ]));

        Assert.Equal(2, rows.Count);
        Assert.Equal(7, rows[0].Seq);
        Assert.Equal("app.bsky.feed.post", rows[0].Collection);
        Assert.Equal("aaa", rows[0].RKey);
        Assert.False(rows[0].Payload.IsEmpty);
        Assert.Equal(8, rows[1].Seq);
        Assert.Equal("app.bsky.feed.like", rows[1].Collection);
        Assert.Equal("bbbb", rows[1].RKey);
        Assert.True(rows[1].Payload.IsEmpty);
    }

    [Fact]
    public void DecodeBlock_EmptyBlockYieldsNoRows()
        => Assert.Empty(JetstreamSegmentReader.DecodeBlock(BlockFrame([])));

    [Fact]
    public void DecodeBlock_TruncatedColumnsThrowInsteadOfReadingPastTheEnd()
    {
        var block = BlockFrame([Row.Commit(1)]);

        var ex = Assert.Throws<JetstreamArchiveException>(
            () => JetstreamSegmentReader.DecodeBlock(block.AsSpan(0, block.Length - 8)));

        Assert.Contains("truncated", ex.Message);
    }

    [Fact]
    public void DecodeBlock_RejectsAnImplausibleEventCount()
    {
        // A corrupt count column must not drive a huge allocation before the length check.
        var block = new byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(block, uint.MaxValue);

        Assert.Throws<JetstreamArchiveException>(() => JetstreamSegmentReader.DecodeBlock(block));
    }

    [Fact]
    public void TimeUs_FallsBackToWitnessedAtWhenNoTimestampWasImported()
    {
        var rows = JetstreamSegmentReader.DecodeBlock(BlockFrame([
            Row.Commit(1) with { WitnessedAt = 111, IndexedAt = 0 },
            Row.Commit(2) with { WitnessedAt = 222, IndexedAt = 999 },
        ]));

        Assert.Equal(111, rows[0].TimeUs);
        Assert.Equal(999, rows[1].TimeUs);
    }

    [Fact]
    public void ToEvent_ProjectsACommitWithTheRecordAndItsComputedCid()
    {
        var record = PostRecord("hello archive");
        var row = JetstreamSegmentReader.DecodeBlock(BlockFrame([Row.Commit(42, record: record)]))[0];

        var evt = Assert.IsType<JetstreamCommitEvent>(row.ToEvent());

        Assert.Equal(42, evt.Cursor);
        Assert.Equal(JetstreamOperation.Create, evt.Operation);
        Assert.Equal("app.bsky.feed.post", evt.Collection);
        Assert.Equal("hello archive", evt.Record!.Value.GetProperty("text").GetString());
        // The archive stores no CID column: it is the DAG-CBOR hash of the stored payload.
        Assert.Equal(CidComputation.ComputeForDagCbor(record), evt.Cid);
    }

    [Fact]
    public void ToEvent_ProjectsACreateResyncRowAsACreate()
    {
        var row = JetstreamSegmentReader.DecodeBlock(BlockFrame([
            Row.Commit(5, kind: JetstreamArchiveRowKind.CreateResync)])) [0];

        var evt = Assert.IsType<JetstreamCommitEvent>(row.ToEvent());
        Assert.Equal(JetstreamOperation.Create, evt.Operation);
    }

    [Fact]
    public void ToEvent_ADeleteCarriesNeitherRecordNorCid()
    {
        var row = JetstreamSegmentReader.DecodeBlock(BlockFrame([
            Row.Commit(6, kind: JetstreamArchiveRowKind.Delete)])) [0];

        var evt = Assert.IsType<JetstreamCommitEvent>(row.ToEvent());
        Assert.Equal(JetstreamOperation.Delete, evt.Operation);
        Assert.Null(evt.Record);
        Assert.Null(evt.Cid);
    }

    [Fact]
    public void ToEvent_ProjectsIdentityAccountAndSyncPayloads()
    {
        var block = BlockFrame([
            Row.NonCommit(1, JetstreamArchiveRowKind.Identity, Frame(new()
            {
                ["seq"] = 900,
                ["did"] = "did:plc:eygmaihciaxprqvxpfvl6flk",
                ["handle"] = "alice.example.com",
                ["time"] = "2024-09-09T20:26:02.000Z",
            })),
            Row.NonCommit(2, JetstreamArchiveRowKind.Account, Frame(new()
            {
                ["seq"] = 901,
                ["active"] = false,
                ["status"] = "deleted",
            })),
            Row.NonCommit(3, JetstreamArchiveRowKind.Sync, Frame(new()
            {
                ["seq"] = 902,
                ["rev"] = "3l3qo2vuowo2c",
            })),
        ]);

        var events = JetstreamSegmentReader.DecodeBlock(block).Select(r => r.ToEvent()).ToList();

        var identity = Assert.IsType<JetstreamIdentityEvent>(events[0]);
        Assert.Equal("alice.example.com", identity.Handle);
        Assert.Equal(900, identity.Seq);
        Assert.Equal(1, identity.Cursor);

        var account = Assert.IsType<JetstreamAccountEvent>(events[1]);
        Assert.False(account.Active);
        Assert.Equal("deleted", account.Status);

        var sync = Assert.IsType<JetstreamSyncEvent>(events[2]);
        Assert.Equal("3l3qo2vuowo2c", sync.Rev);
        Assert.Equal(902, sync.Seq);
    }

    [Fact]
    public void ToEvent_SkipsARowWithAnUnknownKindOrUnparseableDid()
    {
        var block = BlockFrame([
            Row.Commit(1) with { Kind = (JetstreamArchiveRowKind)99 },
            Row.Commit(2, did: "not-a-did"),
        ]);

        Assert.All(JetstreamSegmentReader.DecodeBlock(block), row => Assert.Null(row.ToEvent()));
    }

    [Fact]
    public async Task ReadEventsAsync_StreamsEveryBlockInSequenceOrder()
    {
        var segment = Segment([
            [Row.Commit(1), Row.Commit(2)],
            [Row.Commit(3)],
            [Row.Commit(4), Row.Commit(5)],
        ]);

        using var stream = new MemoryStream(segment);
        var events = new List<JetstreamEvent>();
        await foreach (var evt in JetstreamSegmentReader.ReadEventsAsync(stream, Decompressor))
            events.Add(evt);

        Assert.Equal([1L, 2, 3, 4, 5], events.Select(e => e.Cursor!.Value));
    }

    [Fact]
    public async Task ReadRowsAsync_StopsAtTheFooterRatherThanDecodingIt()
    {
        // The footer follows the last block; a reader that kept going would decode index entries
        // as if they were a block frame.
        var segment = Segment([[Row.Commit(1)]]);

        using var stream = new MemoryStream(segment);
        var rows = new List<JetstreamArchiveRow>();
        await foreach (var row in JetstreamSegmentReader.ReadRowsAsync(stream, Decompressor))
            rows.Add(row);

        Assert.Single(rows);
    }

    [Fact]
    public async Task ReadRowsAsync_ThrowsOnATruncatedSegment()
    {
        var segment = Segment([[Row.Commit(1), Row.Commit(2)]]);
        using var stream = new MemoryStream(segment[..(segment.Length / 2)]);

        await Assert.ThrowsAsync<JetstreamArchiveException>(async () =>
        {
            await foreach (var _ in JetstreamSegmentReader.ReadRowsAsync(stream, Decompressor)) { }
        });
    }

    [Fact]
    public void DecodeBlockFrame_WrapsADecompressorFailure()
    {
        var ex = Assert.Throws<JetstreamArchiveException>(
            () => JetstreamSegmentReader.DecodeBlockFrame([1, 2, 3], new ThrowingDecompressor()));

        Assert.Contains("decompress", ex.Message);
        Assert.IsType<InvalidDataException>(ex.InnerException);
    }

    [Fact]
    public void DecodeBlockFrame_RunsTheFrameThroughTheSuppliedDecompressor()
    {
        // getBlock hands back the stored frame without the 8-byte length prefix.
        var frame = BlockFrame([Row.Commit(3)]);

        var rows = JetstreamSegmentReader.DecodeBlockFrame(frame, Decompressor);

        Assert.Equal(3, Assert.Single(rows).Seq);
    }

    [Fact]
    public void Row_PayloadIsCopiedOutOfTheDecompressionBuffer()
    {
        // Rows outlive the buffer they were decoded from, so aliasing it would hand callers bytes
        // that another block later overwrites.
        var block = BlockFrame([Row.Commit(1)]);
        var rows = JetstreamSegmentReader.DecodeBlock(block);
        var payload = rows[0].Payload.ToArray();

        Array.Clear(block);

        Assert.Equal(payload, rows[0].Payload.ToArray());
        Assert.NotEmpty(payload);
    }

    private sealed class ThrowingDecompressor : IJetstreamBlockDecompressor
    {
        public byte[] Decompress(ReadOnlySpan<byte> frame) => throw new InvalidDataException("bad frame");
    }
}
