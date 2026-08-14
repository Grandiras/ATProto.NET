using ATProtoNet.Streaming;

namespace ATProtoNet.IntegrationTests;

/// <summary>
/// Tests the Jetstream v2 wire protocol against a live Bluesky-operated instance.
/// </summary>
/// <remarks>
/// These are the tests behind the "verified against the live instances" claim: the endpoint
/// path and subprotocol, the envelope and event shapes, the <c>kinds</c> filter, inclusive
/// sequence-number resume, the pre-upgrade rejections, and the dictionary fetch. They need no
/// PDS and no credentials — only outbound internet — and are gated by
/// <see cref="RequiresJetstreamFactAttribute"/> so CI stays offline by default.
/// </remarks>
public class JetstreamV2Tests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static JetstreamConsumerOptions V2Options(
        IReadOnlyList<JetstreamEventKind>? kinds = null,
        IReadOnlyList<string>? collections = null) => new()
        {
            ServiceUrl = TestConfig.JetstreamUrl,
            Protocol = JetstreamProtocol.V2,
            WantedKinds = kinds,
            WantedCollections = collections,
        };

    [RequiresJetstreamFact]
    public async Task SubscribeAsync_V2_DeliversParsedEventsWithSequenceCursors()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var client = new JetstreamClient(V2Options(kinds: [JetstreamEventKind.Commit]));

        var events = new List<JetstreamEvent>();
        await foreach (var evt in client.SubscribeAsync(cancellationToken: cts.Token))
        {
            events.Add(evt);
            if (events.Count == 5)
                break;
        }

        Assert.Equal(5, events.Count);
        Assert.All(events, evt =>
        {
            // Every v2 frame carries a sequence number, and the timestamp is derived from
            // the frame's RFC 3339 "time" rather than a v1 "time_us" field.
            Assert.NotNull(evt.Cursor);
            Assert.True(evt.Cursor > 0);
            Assert.True(evt.Timestamp > DateTimeOffset.UtcNow.AddHours(-1));
            Assert.StartsWith("did:", evt.Did.ToString(), StringComparison.Ordinal);
        });

        // The kinds filter is server-side, so nothing but commits should have arrived.
        Assert.All(events, evt => Assert.IsType<JetstreamCommitEvent>(evt));

        // Sequence numbers are monotonic, which is what makes them a resume position.
        var cursors = events.Select(e => e.Cursor!.Value).ToList();
        Assert.Equal(cursors.OrderBy(c => c), cursors);
    }

    [RequiresJetstreamFact]
    public async Task SubscribeAsync_V2_FiltersCommitsByCollection()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var client = new JetstreamClient(V2Options(
            kinds: [JetstreamEventKind.Commit],
            collections: ["app.bsky.feed.post"]));

        var commits = new List<JetstreamCommitEvent>();
        await foreach (var evt in client.SubscribeAsync(cancellationToken: cts.Token))
        {
            commits.Add(Assert.IsType<JetstreamCommitEvent>(evt));
            if (commits.Count == 3)
                break;
        }

        Assert.Equal(3, commits.Count);
        Assert.All(commits, commit => Assert.Equal("app.bsky.feed.post", commit.Collection));
    }

    [RequiresJetstreamFact]
    public async Task SubscribeAsync_V2_ReplaysTheCursorInclusively()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var options = V2Options(kinds: [JetstreamEventKind.Commit]);

        long cursor;
        string did;
        using (var live = new JetstreamClient(options))
        {
            var first = await FirstEventAsync(live, null, cts.Token);
            cursor = first.Cursor!.Value;
            did = first.Did.ToString();
        }

        // A v2 cursor names an event the server replays rather than a position to resume
        // after — which is why JetstreamConsumer reconnects at the last sequence it delivered
        // and ignores ReconnectRewind.
        using var replay = new JetstreamClient(options);
        var replayed = await FirstEventAsync(replay, cursor, cts.Token);

        Assert.Equal(cursor, replayed.Cursor);
        Assert.Equal(did, replayed.Did.ToString());
    }

    [RequiresJetstreamFact]
    public async Task SubscribeAsync_V2_WithCursorBelowRetentionFloor_ThrowsConnectException()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var client = new JetstreamClient(V2Options());

        var ex = await Assert.ThrowsAsync<JetstreamConnectException>(async () =>
            await FirstEventAsync(client, 1, cts.Token));

        // Validated before the upgrade, so it arrives as an HTTP status rather than a frame.
        Assert.Equal(400, ex.StatusCode);
        Assert.False(ex.IsRetryable);
    }

    [RequiresJetstreamFact]
    public async Task SubscribeAsync_V2_WithUnknownDictionaryId_ThrowsConnectException()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var options = new JetstreamConsumerOptions
        {
            ServiceUrl = TestConfig.JetstreamUrl,
            Protocol = JetstreamProtocol.V2,
            ZstdDictionaryId = 1, // Far below any dictionary the server has ever trained
            Decompressor = new UnusedDecompressor(),
        };
        using var client = new JetstreamClient(options);

        var ex = await Assert.ThrowsAsync<JetstreamConnectException>(async () =>
            await FirstEventAsync(client, null, cts.Token));

        Assert.Equal(400, ex.StatusCode);
        Assert.False(ex.IsRetryable);
    }

    [RequiresJetstreamFact]
    public async Task GetDictionaryAsync_ReturnsADictionaryCarryingItsOwnId()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var dictionaries = new JetstreamDictionaryClient(TestConfig.JetstreamUrl);

        var current = await dictionaries.GetDictionaryAsync(cancellationToken: cts.Token);

        Assert.True(current.Id > 0);
        Assert.NotEmpty(current.Data);
        // The ID read out of the RFC 8878 header is the one the server names the dictionary
        // by, so fetching it explicitly returns the same dictionary.
        var byId = await dictionaries.GetDictionaryAsync(current.Id, cts.Token);
        Assert.Equal(current.Id, byId.Id);
        Assert.Equal(current.Data, byId.Data);
    }

    [RequiresJetstreamFact]
    public async Task GetDictionaryAsync_WithUnknownId_ThrowsConnectException()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var dictionaries = new JetstreamDictionaryClient(TestConfig.JetstreamUrl);

        var ex = await Assert.ThrowsAsync<JetstreamConnectException>(async () =>
            await dictionaries.GetDictionaryAsync(1, cts.Token));

        Assert.NotNull(ex.StatusCode);
        Assert.InRange(ex.StatusCode!.Value, 400, 499);
    }

    [RequiresJetstreamFact]
    public async Task SubscribeAsync_V1_AgainstAV2Host_StillParsesAndCarriesACursor()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var client = new JetstreamClient(new JetstreamConsumerOptions
        {
            ServiceUrl = TestConfig.JetstreamUrl,
            WantedCollections = ["app.bsky.feed.post"],
        });

        var evt = await FirstEventAsync(client, null, cts.Token);

        // The v2 hosts serve the frozen v1 wire too, with their sequence number added to it.
        var commit = Assert.IsType<JetstreamCommitEvent>(evt);
        Assert.Equal("app.bsky.feed.post", commit.Collection);
        Assert.True(commit.TimeUs > 0);
        Assert.NotNull(commit.Cursor);
    }

    private static async Task<JetstreamEvent> FirstEventAsync(
        JetstreamClient client, long? cursor, CancellationToken cancellationToken)
    {
        await foreach (var evt in client.SubscribeAsync(cursor, cancellationToken))
            return evt;

        throw new InvalidOperationException("Jetstream closed the stream without delivering an event.");
    }

    /// <summary>A decompressor the tests never reach — the subscription is rejected first.</summary>
    private sealed class UnusedDecompressor : IJetstreamDecompressor
    {
        public byte[] Decompress(ReadOnlySpan<byte> frame)
            => throw new NotSupportedException();
    }
}
