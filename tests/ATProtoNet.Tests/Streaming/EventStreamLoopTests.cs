using System.Formats.Cbor;
using ATProtoNet.Lexicon.Chat.Bsky.Moderation;
using ATProtoNet.Lexicon.Com.AtProto.Label;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Streaming;
using static ATProtoNet.Tests.Streaming.EventStreamFrames;

namespace ATProtoNet.Tests.Streaming;

/// <summary>
/// The connect, read and reconnect loop every stream consumer shares (through the firehose
/// consumer, the simplest), the label and chat moderation streams, over scripted connections.
/// </summary>
public class EventStreamLoopTests
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

    // ── The loop ─────────────────────────────────────────────

    private const string Relay = "wss://relay.test";

    private static TypedFirehoseConsumer Firehose(
        ScriptedConnector connector,
        int? maxReconnects = 0,
        IStreamCursorStore? store = null,
        List<EventStreamError>? errors = null) => new(
        new TypedFirehoseConsumerOptions
        {
            ServiceUrl = Relay,
            CursorStore = store,
            Reconnect = StreamTestExtensions.Immediate(maxReconnects),
            OnStreamError = errors is null ? null : errors.Add,
        },
        connector.Connect);

    [Theory]
    [InlineData("break", 10L)]
    [InlineData("cancel", 11L)]
    [InlineData("throw", 12L)]
    public async Task ConsumeAsync_HoweverItEnds_SavesTheCursorOfTheLastHandledEvent(string end, long saved)
    {
        // The event being handled when the caller broke out is not recorded: at-least-once.
        var store = new InMemoryStreamCursorStore();
        var connector = new ScriptedConnector()
            .Connection(IdentityFrame(10), IdentityFrame(11), IdentityFrame(12))
            .Failing(new EventStreamException("refused", statusCode: 400));
        var consumer = Firehose(connector, maxReconnects: null, store: store);
        using var cts = new CancellationTokenSource();

        var ex = await Record.ExceptionAsync(async () =>
        {
            await foreach (var message in consumer.ConsumeAsync(cancellationToken: cts.Token))
            {
                var seq = ((IdentityEvent)message).Seq;
                if (end == "break" && seq == 11)
                    break;
                if (end == "cancel" && seq == 11)
                    await cts.CancelAsync();
            }
        });

        // Cancelling ends the enumeration normally; the refused reconnect throws.
        Assert.Equal(end == "throw", ex is EventStreamException);
        Assert.Equal(saved, await store.GetCursorAsync(Relay, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConsumeAsync_AttemptsExhausted_ThrowsWithTheLastFailureRatherThanEndingLikeTheStream(bool failing)
    {
        var failure = new System.Net.WebSockets.WebSocketException("reset");
        var connector = new ScriptedConnector().Connection(IdentityFrame(1));
        if (failing)
            connector.Failing(failure).Failing(failure);

        var messages = new List<FirehoseMessage>();
        var ex = await Assert.ThrowsAsync<EventStreamException>(async () =>
        {
            await foreach (var message in Firehose(connector, maxReconnects: 2).ConsumeAsync(cancellationToken: TestContext.Current.CancellationToken))
                messages.Add(message);
        });

        Assert.Single(messages);
        Assert.Equal(failing ? failure : null, ex.InnerException);
        Assert.Equal(3, connector.Endpoints.Count);
    }

    [Fact]
    public async Task ConsumeAsync_AConnectionThatDelivers_ResetsTheAttemptCount()
    {
        // Two drops in a row would exhaust one attempt, but each connection delivers first.
        var connector = new ScriptedConnector()
            .Connection(IdentityFrame(1))
            .Connection(IdentityFrame(2))
            .Connection(IdentityFrame(3));

        var messages = await Firehose(connector, maxReconnects: 1).ConsumeAsync().DrainAsync();

        Assert.Equal([1L, 2L, 3L], messages.Cast<IdentityEvent>().Select(e => e.Seq));
    }

    [Fact]
    public async Task ConsumeAsync_FailedConnection_IsRetried()
    {
        var connector = new ScriptedConnector()
            .Failing(new InvalidOperationException("connect refused"))
            .Connection(IdentityFrame(1));

        var messages = await Firehose(connector, maxReconnects: 1).ConsumeAsync().DrainAsync();

        Assert.Single(messages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(EventStreamErrors.FutureCursor)]
    public async Task ConsumeAsync_FailureThatCannotBeRetried_ThrowsAtOnce(string? errorFrame)
    {
        // A cursor ahead of the relay, or a subscription it refused, fails the same way forever.
        var errors = new List<EventStreamError>();
        var connector = new ScriptedConnector();
        if (errorFrame is null)
            connector.Failing(new EventStreamException("refused", statusCode: 400));
        else
            connector.Connection(Error(errorFrame, "cursor is ahead"));

        var ex = await Assert.ThrowsAsync<EventStreamException>(async () =>
        {
            await foreach (var _ in Firehose(connector, maxReconnects: null, errors: errors).ConsumeAsync(cursor: 999, TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.False(ex.IsRetryable);
        Assert.Equal(errorFrame, ex.Error);
        Assert.Equal(errorFrame is null ? [] : [new EventStreamError(errorFrame, "cursor is ahead")], errors);
        Assert.Single(connector.Endpoints);
    }

    [Fact]
    public async Task ConsumeAsync_RetryableErrorFrame_IsReportedAndReconnectsFromTheLastCursor()
    {
        var errors = new List<EventStreamError>();
        var connector = new ScriptedConnector()
            .Connection(IdentityFrame(5), Error("ConsumerTooSlow"))
            .Connection(IdentityFrame(6));

        var messages = await Firehose(connector, maxReconnects: 1, errors: errors).ConsumeAsync().DrainAsync();

        Assert.Equal([5L, 6L], messages.Cast<IdentityEvent>().Select(e => e.Seq));
        Assert.Equal("ConsumerTooSlow", Assert.Single(errors).Error);
        Assert.Equal("5", connector.Cursors.ElementAt(1));
    }

    // ── LabelStreamConsumer ──────────────────────────────────

    [Fact]
    public async Task LabelStreamConsumer_ParsesLabelsAndInfo()
    {
        var connector = new ScriptedConnector().Connection(Labels(7, "spam", "!hide"), Info("OutdatedCursor"));
        var consumer = new LabelStreamConsumer(
            new LabelStreamConsumerOptions { ServiceUrl = Labeler, Reconnect = StreamTestExtensions.Immediate(0) },
            connector.Connect);

        var messages = await consumer.ConsumeAsync(cancellationToken: TestContext.Current.CancellationToken).DrainAsync();

        var labels = Assert.IsType<LabelsEvent>(messages[0]);
        Assert.Equal(7, labels.Seq);
        Assert.Equal(["spam", "!hide"], labels.Labels.Select(l => l.Val));
        Assert.Equal([1, 2, 3, 4, 5], labels.Labels[0].Sig);
        Assert.Equal("OutdatedCursor", Assert.IsType<LabelInfoEvent>(messages[1]).Name);
        Assert.Equal("wss://labeler.test/xrpc/com.atproto.label.subscribeLabels", connector.Endpoints.Single().ToString());
    }

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
