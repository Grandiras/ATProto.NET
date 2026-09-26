using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Streaming;
using static ATProtoNet.Tests.Streaming.JetstreamSegmentFixture;

namespace ATProtoNet.Tests.Streaming;

public class JetstreamReplayConsumerTests
{
    /// <summary>
    /// A stand-in archive: <c>planSnapshot</c> answers from a scripted queue of pages, and
    /// <c>getSegment</c> / <c>getBlock</c> serve bytes built by <see cref="JetstreamSegmentFixture"/>.
    /// </summary>
    private sealed class FakeArchive : HttpMessageHandler
    {
        private readonly Queue<object> _plans = new();

        public Dictionary<string, byte[]> Segments { get; } = [];
        public Dictionary<(string Segment, int Block), byte[]> Blocks { get; } = [];
        public List<string> PlanRequests { get; } = [];
        public List<string> Downloads { get; } = [];

        public FakeArchive Plan(long plannedThroughSeq, long sealedTipSeq, params object[] segments)
        {
            _plans.Enqueue(new { plannedThroughSeq, sealedTipSeq, segments });
            return this;
        }

        public static object WholeSegment(string name, int index, long minSeq, long maxSeq) => new
        {
            name,
            index,
            checksum = "0123456789abcdef",
            minSeq,
            maxSeq,
            mode = "segment",
        };

        public static object BlockRanges(string name, int index, long minSeq, long maxSeq, params (int First, int Last)[] ranges) => new
        {
            name,
            index,
            checksum = "0123456789abcdef",
            minSeq,
            maxSeq,
            mode = "blocks",
            blocks = ranges.Select(r => new { first = r.First, last = r.Last }).ToArray(),
        };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            if (uri.AbsolutePath.EndsWith("planSnapshot", StringComparison.Ordinal))
            {
                PlanRequests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                var plan = _plans.Count > 0
                    ? _plans.Dequeue()
                    : new { plannedThroughSeq = 0L, sealedTipSeq = 0L, segments = Array.Empty<object>() };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(plan), Encoding.UTF8, "application/json"),
                };
            }

            if (uri.AbsolutePath.EndsWith("getSegment", StringComparison.Ordinal))
            {
                var name = query["name"]!;
                Downloads.Add(name);
                return Segments.TryGetValue(name, out var bytes)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                    : NotFound("SegmentNotFound");
            }

            if (uri.AbsolutePath.EndsWith("getBlock", StringComparison.Ordinal))
            {
                var key = (query["segment"]!, int.Parse(query["blockIndex"]!));
                Downloads.Add($"{key.Item1}#{key.Item2}");
                return Blocks.TryGetValue(key, out var frame)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(frame) }
                    : NotFound("BlockNotFound");
            }

            return NotFound("MethodNotImplemented");

            static HttpResponseMessage NotFound(string error) => new(HttpStatusCode.BadRequest)
            {
                Content = new StringContent($$"""{"error":"{{error}}"}""", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static JetstreamConsumerOptions Options(
        FakeArchive archive,
        IReadOnlyList<string>? collections = null,
        IReadOnlyList<string>? dids = null,
        IReadOnlyList<JetstreamEventKind>? kinds = null,
        IStreamCursorStore? cursorStore = null,
        long? afterSeq = null,
        long? beforeSeq = null,
        bool snapshotOnly = true,
        int parallelism = 4,
        int stalledPlanAttempts = 5) => new()
        {
            ServiceUrl = JetstreamEndpoints.UsEast,
            Protocol = JetstreamProtocol.V2,
            WantedCollections = collections,
            WantedDids = dids?.Select(Did.Parse).ToList(),
            WantedKinds = kinds,
            CursorStore = cursorStore,
            CursorPersistInterval = 1,
            Archive = new JetstreamArchiveOptions
            {
                ApiKey = "test-key",
                BlockDecompressor = new PassThroughDecompressor(),
                AfterSeq = afterSeq,
                BeforeSeq = beforeSeq,
                SnapshotOnly = snapshotOnly,
                DownloadParallelism = parallelism,
                HttpClient = new HttpClient(archive),
                MaxRetryAttempts = 1,
                MaxRetryDelay = TimeSpan.Zero,
                MaxStalledPlanAttempts = stalledPlanAttempts,
            },
        };

    private static async Task<List<JetstreamEvent>> ReplayAsync(
        JetstreamConsumerOptions options, long? afterSeq = null)
    {
        using var consumer = new JetstreamReplayConsumer(options);
        var events = new List<JetstreamEvent>();
        await foreach (var evt in consumer.ReplayAsync(afterSeq))
            events.Add(evt);
        return events;
    }

    [Fact]
    public async Task SnapshotMode_DeliversEveryPlannedSegmentInSequenceOrder()
    {
        var archive = new FakeArchive().Plan(20, 20,
            FakeArchive.WholeSegment("seg_0.jss", 0, 1, 10),
            FakeArchive.WholeSegment("seg_1.jss", 1, 11, 20));
        archive.Segments["seg_0.jss"] = Segment([[Row.Commit(1), Row.Commit(2)], [Row.Commit(3)]]);
        archive.Segments["seg_1.jss"] = Segment([[Row.Commit(11), Row.Commit(12)]]);

        var events = await ReplayAsync(Options(archive));

        Assert.Equal([1L, 2, 3, 11, 12], events.Select(e => e.Cursor!.Value));
        Assert.Equal(["seg_0.jss", "seg_1.jss"], archive.Downloads);
    }

    [Fact]
    public async Task SnapshotMode_PagesThePlanUntilItReachesThePinnedTip()
    {
        // The first page pins sealedTipSeq; later pages resume at plannedThroughSeq and carry the
        // pinned tip as beforeSeq so the window cannot float while it is being downloaded.
        var archive = new FakeArchive()
            .Plan(10, 20, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 10))
            .Plan(20, 20, FakeArchive.WholeSegment("seg_1.jss", 1, 11, 20));
        archive.Segments["seg_0.jss"] = Segment([[Row.Commit(1)]]);
        archive.Segments["seg_1.jss"] = Segment([[Row.Commit(11)]]);

        var events = await ReplayAsync(Options(archive));

        Assert.Equal([1L, 11], events.Select(e => e.Cursor!.Value));
        Assert.Equal(2, archive.PlanRequests.Count);

        var second = JsonDocument.Parse(archive.PlanRequests[1]).RootElement;
        Assert.Equal(10, second.GetProperty("afterSeq").GetInt64());
        Assert.Equal(20, second.GetProperty("beforeSeq").GetInt64());
    }

    [Fact]
    public async Task SnapshotMode_DownloadsOnlyThePlannedBlocksInBlocksMode()
    {
        var archive = new FakeArchive().Plan(30, 30,
            FakeArchive.BlockRanges("seg_2.jss", 2, 21, 30, (1, 2)));
        archive.Blocks[("seg_2.jss", 1)] = BlockFrame([Row.Commit(21)]);
        archive.Blocks[("seg_2.jss", 2)] = BlockFrame([Row.Commit(22)]);

        var events = await ReplayAsync(Options(archive));

        Assert.Equal([21L, 22], events.Select(e => e.Cursor!.Value));
        Assert.Equal(["seg_2.jss#1", "seg_2.jss#2"], archive.Downloads);
    }

    [Fact]
    public async Task PlannerOverreach_IsTrimmedByTheExactClientSideFilter()
    {
        // The planner works from bloom filters and per-block summaries: no false negatives, but it
        // can hand back blocks whose rows do not match, so the filter is applied again here.
        var archive = new FakeArchive().Plan(5, 5, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 5));
        archive.Segments["seg_0.jss"] = Segment([[
            Row.Commit(1, collection: "app.bsky.feed.post"),
            Row.Commit(2, collection: "app.bsky.graph.follow"),
            Row.Commit(3, collection: "app.bsky.feed.like"),
            Row.Commit(4, did: "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", collection: "app.bsky.feed.post"),
        ]]);

        var events = await ReplayAsync(Options(
            archive,
            collections: ["app.bsky.feed.*"],
            dids: ["did:plc:eygmaihciaxprqvxpfvl6flk"]));

        Assert.Equal([1L, 3], events.Select(e => e.Cursor!.Value));
    }

    [Fact]
    public async Task AccountEvents_ReachACollectionFilteredConsumer()
    {
        // An account deletion carries no collection; dropping it would leave the consumer indexing
        // the records of a deleted account. Same rule the live tail follows.
        var archive = new FakeArchive().Plan(3, 3, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 3));
        archive.Segments["seg_0.jss"] = Segment([[
            Row.Commit(1, collection: "app.bsky.feed.post"),
            Row.NonCommit(2, JetstreamArchiveRowKind.Account, Frame(new()
            {
                ["seq"] = 90,
                ["active"] = false,
                ["status"] = "deleted",
            })),
        ]]);

        var events = await ReplayAsync(Options(archive, collections: ["app.bsky.feed.post"]));

        Assert.Equal([1L, 2], events.Select(e => e.Cursor!.Value));
        Assert.IsType<JetstreamAccountEvent>(events[1]);
    }

    [Fact]
    public async Task WantedKinds_FiltersRowsAndIsSentToThePlanner()
    {
        var archive = new FakeArchive().Plan(3, 3, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 3));
        archive.Segments["seg_0.jss"] = Segment([[
            Row.Commit(1),
            Row.NonCommit(2, JetstreamArchiveRowKind.Identity, Frame(new() { ["handle"] = "a.example" })),
        ]]);

        var events = await ReplayAsync(Options(archive, kinds: [JetstreamEventKind.Commit]));

        Assert.Equal([1L], events.Select(e => e.Cursor!.Value));
        Assert.Equal("commit",
            JsonDocument.Parse(archive.PlanRequests[0]).RootElement.GetProperty("kinds")[0].GetString());
    }

    [Fact]
    public async Task AfterSeq_SkipsEventsAtOrBelowTheResumePosition()
    {
        // A segment straddles the resume point: only the rows above it are new.
        var archive = new FakeArchive().Plan(5, 5, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 5));
        archive.Segments["seg_0.jss"] = Segment([[Row.Commit(1), Row.Commit(2), Row.Commit(3)]]);

        var events = await ReplayAsync(Options(archive), afterSeq: 2);

        Assert.Equal([3L], events.Select(e => e.Cursor!.Value));
    }

    [Fact]
    public async Task AResumePositionIsReadFromTheCursorStoreAndWrittenBack()
    {
        var store = new InMemoryStreamCursorStore();
        await store.StoreCursorAsync(JetstreamEndpoints.UsEast, 1);

        var archive = new FakeArchive().Plan(3, 3, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 3));
        archive.Segments["seg_0.jss"] = Segment([[Row.Commit(1), Row.Commit(2), Row.Commit(3)]]);

        var events = await ReplayAsync(Options(archive, cursorStore: store));

        Assert.Equal([2L, 3], events.Select(e => e.Cursor!.Value));
        Assert.Equal(3, await store.GetCursorAsync(JetstreamEndpoints.UsEast));

        var request = JsonDocument.Parse(archive.PlanRequests[0]).RootElement;
        Assert.Equal(1, request.GetProperty("afterSeq").GetInt64());
    }

    [Fact]
    public async Task OverlappingPlanPages_DoNotRedeliverEvents()
    {
        // Re-planning from plannedThroughSeq can hand back a segment that was already partly
        // delivered; delivery stays monotonic in sequence order.
        var archive = new FakeArchive()
            .Plan(2, 4, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 4))
            .Plan(4, 4, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 4));
        archive.Segments["seg_0.jss"] = Segment([[
            Row.Commit(1), Row.Commit(2), Row.Commit(3), Row.Commit(4)]]);

        var events = await ReplayAsync(Options(archive));

        Assert.Equal([1L, 2, 3, 4], events.Select(e => e.Cursor!.Value));
    }

    [Fact]
    public async Task ParallelDownloads_StillDeliverInPlanOrder()
    {
        var archive = new FakeArchive().Plan(60, 60,
            FakeArchive.WholeSegment("seg_0.jss", 0, 1, 20),
            FakeArchive.WholeSegment("seg_1.jss", 1, 21, 40),
            FakeArchive.WholeSegment("seg_2.jss", 2, 41, 60));
        archive.Segments["seg_0.jss"] = Segment([[Row.Commit(1), Row.Commit(2)]]);
        archive.Segments["seg_1.jss"] = Segment([[Row.Commit(21)]]);
        archive.Segments["seg_2.jss"] = Segment([[Row.Commit(41), Row.Commit(42)]]);

        var events = await ReplayAsync(Options(archive, parallelism: 3));

        Assert.Equal([1L, 2, 21, 41, 42], events.Select(e => e.Cursor!.Value));
    }

    [Fact]
    public async Task APlanThatStopsAdvancingBelowTheTip_IsRetriedAndThenFails()
    {
        // Truncating here would be a silent gap: the cutover reconnects at the pinned tip (100) and
        // never redelivers 6..100. The backfill re-plans a bounded number of times, then throws.
        var archive = new FakeArchive().Plan(5, 100, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 5));
        for (var i = 0; i < 10; i++)
            archive.Plan(5, 100, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 5));
        archive.Segments["seg_0.jss"] = Segment([[Row.Commit(1)]]);

        var ex = await Assert.ThrowsAsync<JetstreamException>(
            () => ReplayAsync(Options(archive, stalledPlanAttempts: 3)));

        Assert.Contains("does not advance", ex.Message);
        Assert.Contains("100", ex.Message);

        // One page that advanced, then the stalled page plus its three re-plans.
        Assert.Equal(5, archive.PlanRequests.Count);

        // Only the page that advanced was downloaded — a stall re-plans, it does not re-fetch.
        Assert.Equal(["seg_0.jss"], archive.Downloads);
    }

    [Fact]
    public async Task APlanThatStallsAndThenRecovers_DeliversTheWholeRange()
    {
        // The sealed tip is reported ahead of the segments carrying it; the wait is what closes the
        // gap between the two.
        var archive = new FakeArchive()
            .Plan(5, 20, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 5))
            .Plan(5, 20)
            .Plan(20, 20, FakeArchive.WholeSegment("seg_1.jss", 1, 6, 20));
        archive.Segments["seg_0.jss"] = Segment([[Row.Commit(1)]]);
        archive.Segments["seg_1.jss"] = Segment([[Row.Commit(11)]]);

        var events = await ReplayAsync(Options(archive));

        Assert.Equal([1L, 11], events.Select(e => e.Cursor!.Value));
        Assert.Equal(3, archive.PlanRequests.Count);
    }

    [Fact]
    public async Task AResumePositionAtTheSealedTip_EndsTheBackfillWithoutStalling()
    {
        // Nothing new is sealed yet: the plan cannot advance, but there is no range below the tip
        // to lose either, so this is a clean finish rather than a stall.
        var archive = new FakeArchive().Plan(7, 7);

        var events = await ReplayAsync(Options(archive), afterSeq: 7);

        Assert.Empty(events);
        Assert.Single(archive.PlanRequests);
    }

    [Fact]
    public async Task BeforeSeq_CapsTheCeilingOnEveryPageNotJustTheFirst()
    {
        // sealedTipSeq runs far past the requested bound; the snapshot must still stop at it rather
        // than adopt the pinned tip as its ceiling from the second page on.
        var archive = new FakeArchive()
            .Plan(5, 1000, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 5))
            .Plan(10, 1000, FakeArchive.WholeSegment("seg_1.jss", 1, 6, 12));
        archive.Segments["seg_0.jss"] = Segment([[Row.Commit(1), Row.Commit(5)]]);
        archive.Segments["seg_1.jss"] = Segment([[Row.Commit(6), Row.Commit(10), Row.Commit(12)]]);

        var events = await ReplayAsync(Options(archive, beforeSeq: 10));

        Assert.Equal([1L, 5, 6, 10], events.Select(e => e.Cursor!.Value));
        Assert.Equal(2, archive.PlanRequests.Count);

        var second = JsonDocument.Parse(archive.PlanRequests[1]).RootElement;
        Assert.Equal(10, second.GetProperty("beforeSeq").GetInt64());
    }

    [Fact]
    public async Task ADownloadFailure_SurfacesRatherThanSilentlySkippingData()
    {
        var archive = new FakeArchive().Plan(10, 10, FakeArchive.WholeSegment("missing.jss", 0, 1, 10));

        var ex = await Assert.ThrowsAsync<JetstreamException>(() => ReplayAsync(Options(archive)));

        Assert.Equal("SegmentNotFound", ex.Error);
    }

    [Fact]
    public void V1_IsRejectedBecauseItHasNoArchive()
    {
        var options = new JetstreamConsumerOptions
        {
            ServiceUrl = JetstreamEndpoints.UsEast,
            Archive = new JetstreamArchiveOptions { BlockDecompressor = new PassThroughDecompressor() },
        };

        var ex = Assert.Throws<ArgumentException>(() => new JetstreamReplayConsumer(options));
        Assert.Contains("V2", ex.Message);
    }

    [Fact]
    public void ArchiveOptionsAreRequired()
    {
        var options = new JetstreamConsumerOptions
        {
            ServiceUrl = JetstreamEndpoints.UsEast,
            Protocol = JetstreamProtocol.V2,
        };

        Assert.Throws<ArgumentException>(() => new JetstreamReplayConsumer(options));
    }

    [Fact]
    public void BeforeSeq_RequiresSnapshotMode()
    {
        var options = Options(new FakeArchive(), beforeSeq: 100, snapshotOnly: false);

        var ex = Assert.Throws<ArgumentException>(() => new JetstreamReplayConsumer(options));
        Assert.Contains("SnapshotOnly", ex.Message);
    }

    [Fact]
    public void ACollectionFilterThatExcludesCommitsIsRejected()
    {
        var options = Options(new FakeArchive(),
            collections: ["app.bsky.feed.post"],
            kinds: [JetstreamEventKind.Identity]);

        Assert.Throws<ArgumentException>(() => new JetstreamReplayConsumer(options));
    }

    // ── Cutover into the live tail ───────────────────────────

    private static JetstreamCommitEvent LiveCommit(long seq) => new()
    {
        Did = Did.Parse("did:plc:eygmaihciaxprqvxpfvl6flk"),
        TimeUs = 1_725_911_162_000_000 + seq,
        Cursor = seq,
        Collection = Nsid.Parse("app.bsky.feed.post"),
        Rkey = RecordKey.Parse("3l3qo2vuowo2b"),
        Operation = ATProtoNet.Lexicon.Com.AtProto.Sync.RepoOpAction.Create,
    };

    /// <summary>
    /// A replay consumer whose live tail is scripted: each connection the live consumer opens
    /// takes the next script, and the cursor it asked for is recorded.
    /// </summary>
    private static JetstreamReplayConsumer Replay(
        JetstreamConsumerOptions options, List<long?> liveCursors, params Func<IAsyncEnumerable<JetstreamEvent>>[] connections)
    {
        var queue = new Queue<Func<IAsyncEnumerable<JetstreamEvent>>>(connections);
        return new JetstreamReplayConsumer(options, archiveClient: null, live => new JetstreamConsumer(live, (cursor, _) =>
        {
            liveCursors.Add(cursor);
            return queue.Count > 0 ? queue.Dequeue()() : Events();
        }));
    }

    private static async IAsyncEnumerable<JetstreamEvent> Events(params JetstreamEvent[] events)
    {
        foreach (var evt in events)
        {
            await Task.Yield();
            yield return evt;
        }
    }

    private static async IAsyncEnumerable<JetstreamEvent> Refused()
    {
        await Task.Yield();
        throw new JetstreamException("cursor too old", statusCode: 400, error: "CursorTooOld");
#pragma warning disable CS0162 // Unreachable, but required to make this an iterator.
        yield break;
#pragma warning restore CS0162
    }

    private static JetstreamConsumerOptions ReplayOptions(FakeArchive archive, IStreamCursorStore? store = null)
    {
        var options = Options(archive, cursorStore: store, snapshotOnly: false);
        return new JetstreamConsumerOptions
        {
            ServiceUrl = options.ServiceUrl,
            Protocol = options.Protocol,
            CursorStore = options.CursorStore,
            CursorPersistInterval = options.CursorPersistInterval,
            Archive = options.Archive,
            Reconnect = JetstreamConsumerTests.Reconnect(0),
        };
    }

    [Fact]
    public async Task ReplayMode_CutsOverAtThePinnedTipWithoutRedeliveringTheSeam()
    {
        var archive = new FakeArchive().Plan(20, 20, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 20));
        archive.Segments["seg_0.jss"] = Segment([[Row.Commit(19), Row.Commit(20)]]);
        var liveCursors = new List<long?>();
        using var consumer = Replay(ReplayOptions(archive), liveCursors,
            () => Events(LiveCommit(20), LiveCommit(21), LiveCommit(22)));

        var events = new List<JetstreamEvent>();
        var phases = new List<bool>();
        try
        {
            await foreach (var evt in consumer.ReplayAsync(cancellationToken: TestContext.Current.CancellationToken))
            {
                events.Add(evt);
                phases.Add(consumer.IsBackfilling);
            }
        }
        catch (EventStreamException ex) when (ex is not JetstreamException)
        {
            // The scripted live tail ran out and its reconnect policy gave up.
        }

        // The tip is replayed inclusively by the socket; the one already delivered is dropped.
        Assert.Equal([19L, 20L, 21L, 22L], events.Select(e => e.Cursor!.Value));
        Assert.Equal([true, true, false, false], phases);
        Assert.Equal(20L, liveCursors[0]);
        Assert.Equal(22L, consumer.LastCursor);
    }

    [Fact]
    public async Task ReplayMode_ARefusedCutoverReplansFromTheLastDeliveredEvent()
    {
        var archive = new FakeArchive()
            .Plan(20, 20, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 20))
            .Plan(30, 30, FakeArchive.WholeSegment("seg_1.jss", 1, 21, 30));
        archive.Segments["seg_0.jss"] = Segment([[Row.Commit(20)]]);
        archive.Segments["seg_1.jss"] = Segment([[Row.Commit(25)]]);
        var liveCursors = new List<long?>();
        using var consumer = Replay(ReplayOptions(archive), liveCursors,
            Refused,
            () => Events(LiveCommit(31)));

        var events = new List<JetstreamEvent>();
        try
        {
            await foreach (var evt in consumer.ReplayAsync(cancellationToken: TestContext.Current.CancellationToken))
                events.Add(evt);
        }
        catch (EventStreamException ex) when (ex is not JetstreamException)
        {
        }

        Assert.Equal([20L, 25L, 31L], events.Select(e => e.Cursor!.Value));
        Assert.Equal([20L, 30L], liveCursors.Take(2));
        using var second = JsonDocument.Parse(archive.PlanRequests[1]);
        Assert.Equal(20, second.RootElement.GetProperty("afterSeq").GetInt64());
    }

    [Fact]
    public async Task ReplayAsync_TimestampCursor_IsRejected()
    {
        // The archive is addressed by sequence number; a stored v1 time_us must not be read as one.
        var timestamp = JetstreamCursor.FromTimestamp(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        using var consumer = new JetstreamReplayConsumer(Options(new FakeArchive()));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in consumer.ReplayAsync(timestamp, TestContext.Current.CancellationToken))
            {
            }
        });
    }

    [Fact]
    public async Task ReplayAsync_Cancellation_EndsNormallyAndPersistsTheCursor()
    {
        var store = new InMemoryStreamCursorStore();
        var archive = new FakeArchive().Plan(20, 20, FakeArchive.WholeSegment("seg_0.jss", 0, 1, 20));
        archive.Segments["seg_0.jss"] = Segment([[Row.Commit(1), Row.Commit(2), Row.Commit(3)]]);
        using var consumer = new JetstreamReplayConsumer(Options(archive, cursorStore: store));
        using var cts = new CancellationTokenSource();

        var seen = new List<long>();
        await foreach (var evt in consumer.ReplayAsync(cancellationToken: cts.Token))
        {
            seen.Add(evt.Cursor!.Value);
            if (seen.Count == 2)
                cts.Cancel();
        }

        Assert.Equal([1L, 2L], seen);
        Assert.Equal(2, await store.GetCursorAsync(JetstreamEndpoints.UsEast, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DecodeEvents_AppliesTheFilterAndWindowToTheRowColumns()
    {
        var options = new JetstreamConsumerOptions
        {
            ServiceUrl = JetstreamEndpoints.UsEast,
            Protocol = JetstreamProtocol.V2,
            WantedCollections = ["app.bsky.feed.*"],
            WantedDids = [Did.Parse("did:plc:eygmaihciaxprqvxpfvl6flk")],
        };
        var filter = new JetstreamArchiveRowFilter(options, afterSeq: 1, ceiling: 5);
        var frame = BlockFrame([
            Row.Commit(1),                                            // at the resume position
            Row.Commit(2),                                            // matches
            Row.Commit(3, collection: "app.bsky.graph.follow"),       // wrong collection
            Row.Commit(4, did: "did:plc:ar7c4by46qjdydhdevvrndac"),   // wrong account
            Row.Commit(5, collection: "app.bsky.feed.like"),          // matches the wildcard
            Row.Commit(6),                                            // above the ceiling
        ]);

        var events = JetstreamSegmentReader.DecodeEvents(frame, new PassThroughDecompressor(), filter);

        Assert.Equal([2L, 5L], events.Select(e => e.Cursor!.Value));
        Assert.Equal("hello archive", ((JetstreamCommitEvent)events[0]).Record!.Value.GetProperty("text").GetString());
    }
}
