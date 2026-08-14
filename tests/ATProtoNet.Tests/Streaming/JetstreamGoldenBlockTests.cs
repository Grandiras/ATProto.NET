using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

/// <summary>
/// Conformance tests against Jetstream's own golden block fixture.
/// </summary>
/// <remarks>
/// <c>Streaming/TestData/jetstream-golden-block.bin</c> is
/// <a href="https://github.com/bluesky-social/jetstream/blob/main/segment/testdata/golden_block.bin">
/// <c>segment/testdata/golden_block.bin</c></a> from the Jetstream repository with its zstd frame
/// already inflated, so the columnar layout can be checked without pulling a zstd implementation
/// into the test project. The expectations below are the <c>goldenEvents()</c> list that produced
/// it — bytes written by the reference encoder, not by this SDK, so a shared misreading of the
/// format cannot pass. <c>jetstream-golden-header.bin</c> is the 256-byte fixed header of that
/// repository's <c>segment/testdata/legacy_fixed4096_blooms.jss</c>, a real sealed segment.
/// </remarks>
public class JetstreamGoldenBlockTests
{
    private static byte[] GoldenBlock()
        => File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Streaming", "TestData", "jetstream-golden-block.bin"));

    [Fact]
    public void DecodeBlock_ReadsTheReferenceEncodersGoldenBlock()
    {
        var rows = JetstreamSegmentReader.DecodeBlock(GoldenBlock());

        Assert.Equal(3, rows.Count);

        Assert.Equal(1, rows[0].Seq);
        Assert.Equal(1_700_000_000_000_000, rows[0].WitnessedAt);
        Assert.Equal(0, rows[0].IndexedAt);
        Assert.Equal(JetstreamArchiveRowKind.Create, rows[0].Kind);
        Assert.Equal("did:plc:abcdefghijklmnopqrstuvwx", rows[0].Did);
        Assert.Equal("app.bsky.feed.post", rows[0].Collection);
        Assert.Equal("3l3qo2vuowo2b", rows[0].RKey);
        Assert.Equal("3l3qo2vutsw2b", rows[0].Rev);
        // CBOR {"hello": 5}
        Assert.Equal([0xA1, 0x65, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x05], rows[0].Payload.ToArray());

        Assert.Equal(2, rows[1].Seq);
        Assert.Equal(JetstreamArchiveRowKind.Identity, rows[1].Kind);
        Assert.Equal("did:web:example.com", rows[1].Did);
        Assert.Equal(string.Empty, rows[1].Collection);
        Assert.True(rows[1].Payload.IsEmpty);
        // An imported indexed_at wins over witnessed_at as the displayed time_us.
        Assert.Equal(1_700_000_001_000_000, rows[1].WitnessedAt);
        Assert.Equal(1_700_000_000_500_000, rows[1].IndexedAt);
        Assert.Equal(1_700_000_000_500_000, rows[1].TimeUs);

        Assert.Equal(3, rows[2].Seq);
        Assert.Equal(JetstreamArchiveRowKind.Delete, rows[2].Kind);
        Assert.Equal("did:plc:zzzzzzzzzzzzzzzzzzzzzzzz", rows[2].Did);
        Assert.Equal("app.bsky.feed.like", rows[2].Collection);
        Assert.Equal("3l3qo2vuowo2c", rows[2].RKey);
        Assert.True(rows[2].Payload.IsEmpty);
    }

    [Fact]
    public void ReadHeader_ParsesARealSealedSegmentHeader()
    {
        // The first 256 bytes of segment/testdata/legacy_fixed4096_blooms.jss from the Jetstream
        // repository: a real sealed segment written by the reference implementation, which pins
        // every field offset in the fixed header.
        var header = JetstreamSegmentReader.ReadHeader(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Streaming", "TestData", "jetstream-golden-header.bin")));

        Assert.Equal(0x90990e50dd911597UL, header.Checksum);
        Assert.Equal(1, header.Version);
        Assert.Equal(2u, header.BlockCount);
        Assert.Equal(12u, header.EventCount);
        Assert.Equal(3u, header.UniqueDidCount);
        Assert.Equal(1ul, header.MinSeq);
        Assert.Equal(12ul, header.MaxSeq);
        Assert.Equal(1, header.MinWitnessedAt);
        Assert.Equal(12, header.MaxWitnessedAt);
        Assert.Equal(578ul, header.FooterOffset);
        Assert.Equal(578ul, header.BlockIndexOffset);
        Assert.Equal(682ul, header.DidBloomOffset);
        Assert.Equal(771ul, header.BlockDidBloomOffset);
        Assert.Equal(17597ul, header.CollectionIndexOffset);
    }

    [Fact]
    public void ToEvent_ProjectsTheGoldenBlockToTheLiveEventModel()
    {
        var events = JetstreamSegmentReader.DecodeBlock(GoldenBlock())
            .Select(row => row.ToEvent())
            .ToList();

        var create = Assert.IsType<JetstreamCommitEvent>(events[0]);
        Assert.Equal(JetstreamOperation.Create, create.Operation);
        Assert.Equal("at://did:plc:abcdefghijklmnopqrstuvwx/app.bsky.feed.post/3l3qo2vuowo2b",
            create.Uri.ToString());
        Assert.Equal(5, create.Record!.Value.GetProperty("hello").GetInt32());
        Assert.Equal(1_700_000_000_000_000, create.TimeUs);

        var identity = Assert.IsType<JetstreamIdentityEvent>(events[1]);
        Assert.Equal("did:web:example.com", identity.Did.ToString());
        // The row carried no payload, so there is no upstream handle to report.
        Assert.Null(identity.Handle);

        var delete = Assert.IsType<JetstreamCommitEvent>(events[2]);
        Assert.Equal(JetstreamOperation.Delete, delete.Operation);
        Assert.Null(delete.Cid);
    }
}
