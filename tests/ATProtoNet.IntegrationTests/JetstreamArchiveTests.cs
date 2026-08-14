using ATProtoNet.Streaming;
using ZstdSharp;

namespace ATProtoNet.IntegrationTests;

/// <summary>
/// Tests the Jetstream v2 archive — the metered HTTP replay endpoints and the <c>.jss</c> segment
/// format — against a live Bluesky-operated instance.
/// </summary>
/// <remarks>
/// These need an API key on top of outbound internet, so they are gated by
/// <see cref="RequiresJetstreamArchiveFactAttribute"/> and skip by default. Every one of them is
/// deliberately small: the endpoints are metered in response bytes, so a test that downloaded a
/// whole 256 MB segment would spend real quota. The unit tests cover the decoder itself against
/// Jetstream's own golden fixtures.
/// </remarks>
public class JetstreamArchiveTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>Segment blocks are plain zstd frames with no dictionary.</summary>
    private sealed class ZstdBlockDecompressor : IJetstreamBlockDecompressor
    {
        public byte[] Decompress(ReadOnlySpan<byte> frame)
        {
            using var decompressor = new Decompressor();
            return decompressor.Unwrap(frame).ToArray();
        }
    }

    private static JetstreamArchiveClient Archive()
        => new(TestConfig.JetstreamUrl, TestConfig.JetstreamApiKey);

    [RequiresJetstreamArchiveFact]
    public async Task ListSegmentsAsync_ReportsSealedSegmentsInIndexOrder()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var archive = Archive();

        var page = await archive.ListSegmentsAsync(limit: 5, cancellationToken: cts.Token);

        Assert.NotEmpty(page.Segments);
        Assert.All(page.Segments, segment =>
        {
            Assert.EndsWith(".jss", segment.Name, StringComparison.Ordinal);
            // The checksum doubles as the getSegment ETag: 16 hex characters of xxh3.
            Assert.Equal(16, segment.Checksum.Length);
            Assert.True(segment.MinSeq <= segment.MaxSeq);
            Assert.True(segment.SizeBytes > 0);
            Assert.True(segment.EventCount > 0);
        });

        var indices = page.Segments.Select(s => s.Index).ToList();
        Assert.Equal(indices.OrderBy(i => i), indices);
    }

    [RequiresJetstreamArchiveFact]
    public async Task PlanSnapshotAsync_PlansAWindowAndPinsTheSealedTip()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var archive = Archive();

        var first = await archive.ListSegmentsAsync(limit: 1, cancellationToken: cts.Token);
        var segment = first.Segments[0];

        var plan = await archive.PlanSnapshotAsync(
            new JetstreamSnapshotRequest
            {
                Kinds = ["commit"],
                Collections = ["app.bsky.feed.post"],
                AfterSeq = segment.MinSeq == 0 ? 0 : segment.MinSeq - 1,
                BeforeSeq = segment.MaxSeq,
            },
            cts.Token);

        Assert.True(plan.SealedTipSeq > 0);
        // sealedTipSeq is capped by beforeSeq, which is what makes it safe to pin as the ceiling.
        Assert.True(plan.SealedTipSeq <= segment.MaxSeq);
        Assert.All(plan.Segments, planned =>
        {
            Assert.Contains(planned.Mode, new[] { "segment", "blocks" });
            if (planned.DownloadMode == JetstreamSegmentDownloadMode.Blocks)
                Assert.All(planned.Blocks!, range => Assert.True(range.First <= range.Last));
        });
    }

    [RequiresJetstreamArchiveFact]
    public async Task GetBlockAsync_ReturnsAFrameTheSegmentReaderCanDecode()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var archive = Archive();

        var page = await archive.ListSegmentsAsync(limit: 1, cancellationToken: cts.Token);
        var segment = page.Segments[0];

        // getBlock returns the stored zstd frame without the segment's 8-byte length prefix.
        var frame = await archive.GetBlockAsync(segment.Name, 0, cts.Token);
        var rows = JetstreamSegmentReader.DecodeBlockFrame(frame, new ZstdBlockDecompressor());

        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
        {
            Assert.StartsWith("did:", row.Did, StringComparison.Ordinal);
            Assert.InRange(row.Seq, (long)segment.MinSeq, segment.MaxSeq);
            Assert.True(row.TimeUs > 0);
        });

        var sequences = rows.Select(r => r.Seq).ToList();
        Assert.Equal(sequences.OrderBy(s => s), sequences);

        // Every row projects to the same event model the live tail delivers.
        Assert.All(rows, row => Assert.NotNull(row.ToEvent()));
    }

    [RequiresJetstreamArchiveFact]
    public async Task GetSegmentAsync_HonoursARangeRequest()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var archive = Archive();

        var page = await archive.ListSegmentsAsync(limit: 1, cancellationToken: cts.Token);
        var segment = page.Segments[0];

        // Range support is what makes a metered download resumable at its exact byte offset.
        using var download = await archive.GetSegmentAsync(segment.Name, rangeStart: 0, cancellationToken: cts.Token);
        var header = new byte[JetstreamSegmentHeader.Size];
        await download.Content.ReadExactlyAsync(header, cts.Token);

        var parsed = JetstreamSegmentReader.ReadHeader(header);
        Assert.Equal(segment.MinSeq, (long)parsed.MinSeq);
        Assert.Equal(segment.MaxSeq, (long)parsed.MaxSeq);
        Assert.Equal(segment.EventCount, parsed.EventCount);
        Assert.Equal(segment.Checksum, parsed.Checksum.ToString("x16"));
    }

    [RequiresJetstreamArchiveFact]
    public async Task AnInvalidApiKeyIsRefusedWithoutRetrying()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var archive = new JetstreamArchiveClient(TestConfig.JetstreamUrl, "not-a-real-key");

        var ex = await Assert.ThrowsAsync<JetstreamArchiveException>(
            () => archive.ListSegmentsAsync(limit: 1, cancellationToken: cts.Token));

        Assert.Equal(401, ex.StatusCode);
        Assert.False(ex.IsRetryable);
    }

    [RequiresJetstreamArchiveFact]
    public async Task SnapshotMode_DeliversArchivedEventsInSequenceOrder()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var archive = Archive();

        var page = await archive.ListSegmentsAsync(limit: 1, cancellationToken: cts.Token);
        var segment = page.Segments[0];

        var consumer = new JetstreamReplayConsumer(new JetstreamConsumerOptions
        {
            ServiceUrl = TestConfig.JetstreamUrl,
            Protocol = JetstreamProtocol.V2,
            WantedCollections = ["app.bsky.feed.post"],
            WantedKinds = [JetstreamEventKind.Commit],
            Archive = new JetstreamArchiveOptions
            {
                ApiKey = TestConfig.JetstreamApiKey,
                BlockDecompressor = new ZstdBlockDecompressor(),
                // One segment's worth of history, so the test spends a bounded number of bytes.
                AfterSeq = segment.MinSeq == 0 ? 0 : segment.MinSeq - 1,
                BeforeSeq = segment.MaxSeq,
                SnapshotOnly = true,
            },
        });

        using (consumer)
        {
            var events = new List<JetstreamEvent>();
            await foreach (var evt in consumer.ReplayAsync(cancellationToken: cts.Token))
            {
                events.Add(evt);
                if (events.Count == 25)
                    break;
            }

            Assert.NotEmpty(events);
            Assert.All(events, evt => Assert.IsType<JetstreamCommitEvent>(evt));
            Assert.All(events, evt => Assert.Equal("app.bsky.feed.post",
                ((JetstreamCommitEvent)evt).Collection));

            var cursors = events.Select(e => e.Cursor!.Value).ToList();
            Assert.Equal(cursors.OrderBy(c => c), cursors);
            Assert.Equal(cursors.Distinct(), cursors);
        }
    }
}
