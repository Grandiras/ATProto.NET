using System.Net.WebSockets;
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
/// <see cref="RequiresFactAttribute"/> (<see cref="IntegrationRequirement.Jetstream"/>) so CI
/// stays offline by default.
/// </remarks>
public class JetstreamV2Tests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>One connection, no reconnect: each test reads what it needs and leaves.</summary>
    private static readonly StreamReconnectPolicy NoReconnect = new() { MaxAttempts = 0 };

    private static JetstreamConsumerOptions V2Options(
        IReadOnlyList<JetstreamEventKind>? kinds = null,
        IReadOnlyList<string>? collections = null) => new()
        {
            ServiceUrl = TestConfig.JetstreamUrl,
            Protocol = JetstreamProtocol.V2,
            WantedKinds = kinds,
            WantedCollections = collections,
            Reconnect = NoReconnect,
        };

    [RequiresFact(IntegrationRequirement.Jetstream)]
    public async Task SubscribeAsync_V2_DeliversParsedEventsWithSequenceCursors()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var client = new JetstreamConsumer(V2Options(kinds: [JetstreamEventKind.Commit]));

        var events = new List<JetstreamEvent>();
        await foreach (var evt in client.ConsumeAsync(cancellationToken: cts.Token))
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

    [RequiresFact(IntegrationRequirement.Jetstream)]
    public async Task SubscribeAsync_V2_FiltersCommitsByCollection()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var client = new JetstreamConsumer(V2Options(
            kinds: [JetstreamEventKind.Commit],
            collections: ["app.bsky.feed.post"]));

        var commits = new List<JetstreamCommitEvent>();
        await foreach (var evt in client.ConsumeAsync(cancellationToken: cts.Token))
        {
            commits.Add(Assert.IsType<JetstreamCommitEvent>(evt));
            if (commits.Count == 3)
                break;
        }

        Assert.Equal(3, commits.Count);
        Assert.All(commits, commit => Assert.Equal("app.bsky.feed.post", commit.Collection));
    }

    [RequiresFact(IntegrationRequirement.Jetstream)]
    public async Task SubscribeAsync_V2_ReplaysTheCursorInclusively()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var options = V2Options(kinds: [JetstreamEventKind.Commit]);

        var first = await FirstEventAsync(new JetstreamConsumer(options), null, cts.Token);

        // A v2 cursor names an event the server replays rather than a position to resume
        // after — which is why the consumer reconnects at the last sequence it delivered and
        // skips it. The consumer hides the replay, so read the wire itself.
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("xrpc.v1.json");
        await socket.ConnectAsync(
            new Uri($"{TestConfig.JetstreamUrl.TrimEnd('/')}/xrpc/network.bsky.jetstream.subscribeEvents?kinds=commit&cursor={first.Cursor}"),
            cts.Token);
        var buffer = new byte[1 << 20];
        var length = 0;
        ValueWebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(buffer.AsMemory(length), cts.Token);
            length += received.Count;
        }
        while (!received.EndOfMessage);
        var replayed = JetstreamEventParser.ParseFrame(buffer.AsMemory(0, length), JetstreamProtocol.V2).Event;

        Assert.Equal(first.Cursor, replayed?.Cursor);
        Assert.Equal(first.Did, replayed?.Did);
    }

    [RequiresFact(IntegrationRequirement.Jetstream)]
    public async Task SubscribeAsync_V2_WithCursorBelowRetentionFloor_ThrowsConnectException()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var client = new JetstreamConsumer(V2Options());

        var ex = await Assert.ThrowsAsync<JetstreamException>(async () =>
            await FirstEventAsync(client, 1, cts.Token));

        // Validated before the upgrade, so it arrives as an HTTP status rather than a frame.
        Assert.Equal(400, ex.StatusCode);
        Assert.False(ex.IsRetryable);
    }

    [RequiresFact(IntegrationRequirement.Jetstream)]
    public async Task SubscribeAsync_V2_WithUnknownDictionaryId_ThrowsConnectException()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var options = new JetstreamConsumerOptions
        {
            ServiceUrl = TestConfig.JetstreamUrl,
            Protocol = JetstreamProtocol.V2,
            ZstdDictionaryId = 1, // Far below any dictionary the server has ever trained
            Decompressor = new UnusedDecompressor(),
            Reconnect = NoReconnect,
        };
        var client = new JetstreamConsumer(options);

        var ex = await Assert.ThrowsAsync<JetstreamException>(async () =>
            await FirstEventAsync(client, null, cts.Token));

        Assert.Equal(400, ex.StatusCode);
        Assert.False(ex.IsRetryable);
    }

    [RequiresFact(IntegrationRequirement.Jetstream)]
    public async Task GetZstdDictionaryAsync_ReturnsADictionaryCarryingItsOwnId()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var dictionaries = new JetstreamArchiveClient(TestConfig.JetstreamUrl);

        var current = await dictionaries.GetZstdDictionaryAsync(cancellationToken: cts.Token);

        Assert.True(current.Id > 0);
        Assert.NotEmpty(current.Data);
        // The ID read out of the RFC 8878 header is the one the server names the dictionary
        // by, so fetching it explicitly returns the same dictionary.
        var byId = await dictionaries.GetZstdDictionaryAsync(current.Id, cts.Token);
        Assert.Equal(current.Id, byId.Id);
        Assert.Equal(current.Data, byId.Data);
    }

    [RequiresFact(IntegrationRequirement.Jetstream)]
    public async Task GetZstdDictionaryAsync_WithUnknownId_ThrowsConnectException()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var dictionaries = new JetstreamArchiveClient(TestConfig.JetstreamUrl);

        var ex = await Assert.ThrowsAsync<JetstreamException>(async () =>
            await dictionaries.GetZstdDictionaryAsync(1, cts.Token));

        Assert.NotNull(ex.StatusCode);
        Assert.InRange(ex.StatusCode!.Value, 400, 499);
    }

    [RequiresFact(IntegrationRequirement.Jetstream)]
    public async Task SubscribeAsync_V2_TimestampCursor_SeeksByTime()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var since = DateTimeOffset.UtcNow.AddMinutes(-5);
        var client = new JetstreamConsumer(V2Options(kinds: [JetstreamEventKind.Commit]));

        // A cursor of 10^15 or more is a unix-microseconds seek, how a v1 cursor carries over.
        var evt = await FirstEventAsync(client, JetstreamCursor.FromTimestamp(since), cts.Token);

        Assert.NotNull(evt.Cursor);
        Assert.False(JetstreamCursor.IsTimestamp(evt.Cursor!.Value));
        Assert.True(evt.Timestamp >= since.AddMinutes(-1), $"{evt.Timestamp} is well before {since}");
        Assert.True(evt.Timestamp < DateTimeOffset.UtcNow.AddMinutes(-1), "The seek should start in the past");
    }

    [RequiresFact(IntegrationRequirement.Jetstream)]
    public async Task GetHealthAsync_ReportsAVersion()
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var jetstream = new JetstreamArchiveClient(TestConfig.JetstreamUrl);

        var health = await jetstream.GetHealthAsync(cts.Token);

        Assert.False(string.IsNullOrEmpty(health.Version));
    }

    [RequiresFact(IntegrationRequirement.Jetstream)]
    public async Task SubscribeAsync_V1_AgainstAV2Host_StillParsesAndCarriesACursor()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var client = new JetstreamConsumer(new JetstreamConsumerOptions
        {
            ServiceUrl = TestConfig.JetstreamUrl,
            WantedCollections = ["app.bsky.feed.post"],
            Reconnect = NoReconnect,
        });

        var evt = await FirstEventAsync(client, null, cts.Token);

        // The v2 hosts serve the frozen v1 wire too, with their sequence number added to it.
        var commit = Assert.IsType<JetstreamCommitEvent>(evt);
        Assert.Equal("app.bsky.feed.post", commit.Collection);
        Assert.True(commit.TimeUs > 0);
        Assert.NotNull(commit.Cursor);
    }

    private static async Task<JetstreamEvent> FirstEventAsync(
        JetstreamConsumer client, long? cursor, CancellationToken cancellationToken)
    {
        await foreach (var evt in client.ConsumeAsync(cursor, cancellationToken))
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
