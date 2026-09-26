using System.Formats.Cbor;
using ATProtoNet.Lexicon.Chat.Bsky.Moderation;
using ATProtoNet.Lexicon.Com.AtProto.Label;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Streaming;
using static ATProtoNet.Tests.Streaming.EventStreamFrames;

namespace ATProtoNet.Tests.Streaming;

/// <summary>
/// The single-connection <see cref="FirehoseClient"/>, the label stream, and the chat moderation
/// stream, over scripted connections.
/// </summary>
public class EventStreamClientTests
{
    private const string Labeler = "wss://labeler.test";

    private static byte[] Labels(long seq, params string[] values) => Message("#labels", writer =>
    {
        writer.WriteStartMap(2);
        writer.WriteTextString("seq");
        writer.WriteInt64(seq);
        writer.WriteTextString("labels");
        writer.WriteStartArray(values.Length);
        foreach (var value in values)
        {
            writer.WriteStartMap(6);
            writer.WriteTextString("src");
            writer.WriteTextString("did:plc:ar7c4by46qjdydhdevvrndac");
            writer.WriteTextString("uri");
            writer.WriteTextString(RepoDid);
            writer.WriteTextString("val");
            writer.WriteTextString(value);
            writer.WriteTextString("cts");
            writer.WriteTextString("2024-01-15T12:00:00.000Z");
            writer.WriteTextString("ver");
            writer.WriteInt32(1);
            writer.WriteTextString("sig");
            writer.WriteByteString([1, 2, 3, 4, 5]);
            writer.WriteEndMap();
        }

        writer.WriteEndArray();
        writer.WriteEndMap();
    });

    // ── FirehoseClient ───────────────────────────────────────

    [Fact]
    public async Task SubscribeAsync_YieldsTypedMessagesAndEndsWhenTheServerCloses()
    {
        var connector = new ScriptedConnector().Connection(IdentityFrame(1), Unknown("#new", 2), IdentityFrame(3));
        await using var client = new FirehoseClient("wss://relay.test/", logger: null, connector.Connect);

        var messages = await client.SubscribeAsync(cursor: 0, TestContext.Current.CancellationToken).DrainAsync();

        Assert.Equal([1L, 3L], messages.Cast<IdentityEvent>().Select(e => e.Seq));
        Assert.Equal("wss://relay.test/xrpc/com.atproto.sync.subscribeRepos?cursor=0", connector.Endpoints.Single().ToString());
    }

    [Fact]
    public async Task SubscribeAsync_ErrorFrame_ThrowsTheTypedError()
    {
        var connector = new ScriptedConnector().Connection(IdentityFrame(1), Error("ConsumerTooSlow", "keep up"));
        await using var client = new FirehoseClient("wss://relay.test", logger: null, connector.Connect);

        var seen = new List<FirehoseMessage>();
        var ex = await Assert.ThrowsAsync<EventStreamException>(async () =>
        {
            await foreach (var message in client.SubscribeAsync(cancellationToken: TestContext.Current.CancellationToken))
                seen.Add(message);
        });

        Assert.Single(seen);
        Assert.Equal(EventStreamErrors.ConsumerTooSlow, ex.Error);
        Assert.True(ex.IsRetryable);
    }

    [Fact]
    public async Task SubscribeAsync_EachSubscriptionOwnsItsConnection()
    {
        var connector = new ScriptedConnector().Connection(IdentityFrame(1)).Connection(IdentityFrame(2));
        await using var client = new FirehoseClient("wss://relay.test", logger: null, connector.Connect);

        var first = await client.SubscribeAsync(cancellationToken: TestContext.Current.CancellationToken).DrainAsync();
        var second = await client.SubscribeAsync(cancellationToken: TestContext.Current.CancellationToken).DrainAsync();

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(2, connector.Endpoints.Count);
    }

    [Fact]
    public async Task DisposeAsync_EndsRunningSubscriptionsAndRefusesNewOnes()
    {
        var connector = new ScriptedConnector().Connection(IdentityFrame(1), IdentityFrame(2), IdentityFrame(3));
        var client = new FirehoseClient("wss://relay.test", logger: null, connector.Connect);

        var seen = new List<FirehoseMessage>();
        await foreach (var message in client.SubscribeAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            seen.Add(message);
            await client.DisposeAsync();
        }

        Assert.Single(seen);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.SubscribeAsync(cancellationToken: TestContext.Current.CancellationToken).DrainAsync());
    }

    [Fact]
    public async Task SubscribeLabelsAsync_ParsesLabelsAndInfo()
    {
        var connector = new ScriptedConnector().Connection(Labels(7, "spam", "!hide"), Info("OutdatedCursor"));
        await using var client = new FirehoseClient(Labeler, logger: null, connector.Connect);

        var messages = await client.SubscribeLabelsAsync(cancellationToken: TestContext.Current.CancellationToken).DrainAsync();

        var labels = Assert.IsType<LabelsEvent>(messages[0]);
        Assert.Equal(7, labels.Seq);
        Assert.Equal(["spam", "!hide"], labels.Labels.Select(l => l.Val));
        Assert.Equal([1, 2, 3, 4, 5], labels.Labels[0].Sig);
        Assert.Equal("OutdatedCursor", Assert.IsType<LabelInfoEvent>(messages[1]).Name);
        Assert.Equal("wss://labeler.test/xrpc/com.atproto.label.subscribeLabels", connector.Endpoints.Single().ToString());
    }

    // ── LabelStreamConsumer ──────────────────────────────────

    [Fact]
    public async Task LabelStreamConsumer_ResumesAndPersistsTheSequenceNumber()
    {
        var store = new InMemoryStreamCursorStore();
        await store.StoreCursorAsync(Labeler, 6, TestContext.Current.CancellationToken);
        var connector = new ScriptedConnector()
            .Connection(Labels(6, "replayed"), Labels(7, "spam"))
            .Connection(Labels(8, "porn"));
        var consumer = new LabelStreamConsumer(
            new LabelStreamConsumerOptions
            {
                ServiceUrl = Labeler,
                CursorStore = store,
                Reconnect = StreamTestExtensions.Immediate(1),
            },
            connector.Connect);

        var messages = await consumer.ConsumeAsync().DrainAsync();

        Assert.Equal([7L, 8L], messages.Cast<LabelsEvent>().Select(l => l.Seq));
        Assert.Equal(["6", "7"], connector.Cursors.Take(2));
        Assert.Equal(8, await store.GetCursorAsync(Labeler, TestContext.Current.CancellationToken));
        Assert.Equal(8, consumer.LastSeq);
    }

    // ── ChatModerationEventConsumer ──────────────────────────

    private static byte[] ModEvent(string type, params (string Key, object Value)[] fields) => Message(type, writer =>
    {
        writer.WriteStartMap(fields.Length);
        foreach (var (key, value) in fields)
        {
            writer.WriteTextString(key);
            switch (value)
            {
                case string text:
                    writer.WriteTextString(text);
                    break;
                case long number:
                    writer.WriteInt64(number);
                    break;
                case bool flag:
                    writer.WriteBoolean(flag);
                    break;
                case string[] items:
                    writer.WriteStartArray(items.Length);
                    foreach (var item in items)
                        writer.WriteTextString(item);
                    writer.WriteEndArray();
                    break;
            }
        }

        writer.WriteEndMap();
    });

    private static byte[] FirstMessage(string rev) => ModEvent("#eventConvoFirstMessage",
        ("rev", rev),
        ("createdAt", "2026-09-25T10:00:00.000Z"),
        ("convoId", "convo-1"),
        ("user", RepoDid),
        ("recipients", new[] { "did:plc:ar7c4by46qjdydhdevvrndac" }));

    [Fact]
    public async Task ChatModerationEventConsumer_ParsesTypedAndUnknownEvents()
    {
        var connector = new ScriptedConnector().Connection(
            FirstMessage("222222222222a"),
            ModEvent("#eventGroupChatMemberLeft",
                ("rev", "222222222222b"),
                ("createdAt", "2026-09-25T10:00:01.000Z"),
                ("convoId", "convo-2"),
                ("convoCreatedAt", "2026-09-01T00:00:00.000Z"),
                ("actorDid", RepoDid),
                ("ownerDid", RepoDid),
                ("subjectDid", "did:plc:ar7c4by46qjdydhdevvrndac"),
                ("groupName", "Readers"),
                ("groupMemberCount", 4L),
                ("leaveMethod", "kicked")),
            ModEvent("#eventSomethingNew", ("rev", "222222222222c"), ("createdAt", "2026-09-25T10:00:02.000Z")));
        var consumer = new ChatModerationEventConsumer(
            new ChatModerationEventConsumerOptions
            {
                ServiceUrl = "wss://chat.test",
                GetAccessTokenAsync = _ => ValueTask.FromResult("service-token"),
                Reconnect = StreamTestExtensions.Immediate(0),
            },
            connector.Connect);

        var events = await consumer.ConsumeAsync().DrainAsync();

        var first = Assert.IsType<ConvoFirstMessageEvent>(events[0]);
        Assert.Equal("convo-1", first.ConvoId);
        Assert.Equal(RepoDid, first.User.Value);
        var left = Assert.IsType<GroupChatMemberLeftEvent>(events[1]);
        Assert.Equal("kicked", left.LeaveMethod);
        Assert.Equal(4, left.GroupMemberCount);
        var unknown = Assert.IsType<UnknownChatModerationEvent>(events[2]);
        Assert.Equal("chat.bsky.moderation.subscribeModEvents#eventSomethingNew", unknown.Type);
        Assert.Equal("222222222222c", unknown.Rev);
        Assert.Equal("222222222222c", consumer.LastRev);
    }

    [Fact]
    public async Task ChatModerationEventConsumer_AuthenticatesEachConnectionAndResumesAfterTheLastRev()
    {
        var tokens = 0;
        var connector = new ScriptedConnector()
            .Connection(FirstMessage("222222222222a"))
            .Connection(FirstMessage("222222222222b"));
        var consumer = new ChatModerationEventConsumer(
            new ChatModerationEventConsumerOptions
            {
                ServiceUrl = "wss://chat.test/",
                GetAccessTokenAsync = _ => ValueTask.FromResult($"token-{++tokens}"),
                Reconnect = StreamTestExtensions.Immediate(1),
            },
            connector.Connect);

        var events = await consumer.ConsumeAsync(ChatModerationEventConsumer.BeginningCursor).DrainAsync();

        Assert.Equal(2, events.Count);
        Assert.Equal(["2222222222222", "222222222222a", "222222222222b"], connector.Cursors);
        Assert.Equal(["Bearer token-1", "Bearer token-2", "Bearer token-3"], connector.Options.Select(o => o.Authorization));
        Assert.Equal(
            "wss://chat.test/xrpc/chat.bsky.moderation.subscribeModEvents?cursor=2222222222222",
            connector.Endpoints[0].ToString());
    }

    [Fact]
    public async Task ChatModerationEventConsumer_FailingTokenProvider_IsRetriedThenThrows()
    {
        var connector = new ScriptedConnector();
        var consumer = new ChatModerationEventConsumer(
            new ChatModerationEventConsumerOptions
            {
                ServiceUrl = "wss://chat.test",
                GetAccessTokenAsync = _ => throw new InvalidOperationException("no key"),
                Reconnect = StreamTestExtensions.Immediate(1),
            },
            connector.Connect);

        var ex = await Assert.ThrowsAsync<EventStreamException>(
            () => consumer.ConsumeAsync(cancellationToken: TestContext.Current.CancellationToken).DrainAsync());

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Empty(connector.Endpoints);
    }

    // ── Reconnect policy and frames ──────────────────────────

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 30)]
    [InlineData(30, 30)]
    public void StreamReconnectPolicy_BacksOffExponentiallyUpToTheMaximum(int attempt, int expectedSeconds)
    {
        var delay = new StreamReconnectPolicy().DelayFor(attempt);

        // Up to 10 % jitter on top.
        Assert.InRange(delay.TotalSeconds, expectedSeconds, expectedSeconds * 1.1);
    }

    [Fact]
    public void EventStreamException_IsRetryable()
    {
        Assert.False(new EventStreamException("x", EventStreamErrors.FutureCursor).IsRetryable);
        Assert.True(new EventStreamException("x", EventStreamErrors.ConsumerTooSlow).IsRetryable);
        Assert.False(new EventStreamException("x", statusCode: 400).IsRetryable);
        Assert.True(new EventStreamException("x", statusCode: 503).IsRetryable);
        Assert.IsAssignableFrom<AtProtoException>(new JetstreamException("x"));
        Assert.IsAssignableFrom<EventStreamException>(new JetstreamException("x"));
    }

    [Fact]
    public void EventStreamFrame_ReadSeq_SkipsEverythingElse()
    {
        var frame = Commit(123_456_789_012, paths: "app.bsky.feed.post/3jzfcijpj2z2a");
        Assert.True(EventStreamFrame.TryReadHeader(frame, out var op, out var type, out var bodyOffset));

        Assert.Equal(1, op);
        Assert.Equal("#commit", type);
        Assert.Equal(123_456_789_012, EventStreamFrame.ReadSeq(frame.AsMemory(bodyOffset)));
        Assert.Null(EventStreamFrame.ReadSeq(ReadOnlyMemory<byte>.Empty));
    }
}
