using System.Runtime.CompilerServices;
using System.Text;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

/// <summary><see cref="JetstreamClient"/> over scripted connections.</summary>
public class JetstreamClientTests
{
    private static byte[] V2Commit(long seq) => Encoding.UTF8.GetBytes(
        $$$"""{"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#commit","seq":{{{seq}}},"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time":"2024-09-09T19:46:02.329308Z","rev":"3l3qo2vutsw2b","operation":"create","collection":"app.bsky.feed.like","rkey":"3l3qo2vuowo2b"}}""");

    private static readonly byte[] Info = Encoding.UTF8.GetBytes(
        """{"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#info","name":"OutdatedCursor"}}""");

    private static byte[] ErrorFrame(string error) => Encoding.UTF8.GetBytes(
        $$"""{"$type":"error","error":"{{error}}","message":"stopping"}""");

    private static JetstreamConsumerOptions V2(
        List<JetstreamInfo>? infos = null,
        List<EventStreamError>? errors = null,
        List<DroppedStreamEvent>? dropped = null,
        IJetstreamDecompressor? decompressor = null) => new()
    {
        ServiceUrl = "https://jetstream.test",
        Protocol = JetstreamProtocol.V2,
        OnInfo = infos is null ? null : infos.Add,
        OnStreamError = errors is null ? null : errors.Add,
        OnEventDropped = dropped is null ? null : dropped.Add,
        Decompressor = decompressor,
        ZstdDictionaryId = decompressor is null ? null : 3,
    };

    [Fact]
    public async Task SubscribeAsync_ParsesFramesAndReportsInfoOutOfBand()
    {
        var infos = new List<JetstreamInfo>();
        var connector = new ScriptedConnector().Connection(V2Commit(1), Info, V2Commit(2));
        await using var client = new JetstreamClient(V2(infos: infos), connector.Connect);

        var events = await client.SubscribeAsync(cursor: 7, TestContext.Current.CancellationToken).DrainAsync();

        Assert.Equal([1L, 2L], events.Select(e => e.Cursor!.Value));
        Assert.Equal("OutdatedCursor", Assert.Single(infos).Name);
        Assert.Equal("wss://jetstream.test/xrpc/network.bsky.jetstream.subscribeEvents?cursor=7", connector.Endpoints[0].ToString());
        Assert.Equal("xrpc.v1.json", connector.Options[0].SubProtocol);
    }

    [Fact]
    public async Task SubscribeAsync_ReadsEachFrameStraightFromTheReceiveBuffer()
    {
        // The socket reuses its buffer for the next message, so an event must not keep a
        // reference to it.
        var buffer = V2Commit(5);
        var connector = new ScriptedConnector().Connection(buffer);
        await using var client = new JetstreamClient(V2(), connector.Connect);

        var events = await client.SubscribeAsync(cancellationToken: TestContext.Current.CancellationToken).DrainAsync();
        Array.Clear(buffer);

        var commit = Assert.IsType<JetstreamCommitEvent>(Assert.Single(events));
        Assert.Equal("app.bsky.feed.like", commit.Collection.Value);
    }

    [Fact]
    public async Task SubscribeAsync_ErrorFrame_ThrowsATypedJetstreamException()
    {
        var errors = new List<EventStreamError>();
        var connector = new ScriptedConnector().Connection(V2Commit(1), ErrorFrame("ConsumerTooSlow"));
        await using var client = new JetstreamClient(V2(errors: errors), connector.Connect);

        var ex = await Assert.ThrowsAsync<JetstreamException>(
            () => client.SubscribeAsync(cancellationToken: TestContext.Current.CancellationToken).DrainAsync());

        Assert.Equal(EventStreamErrors.ConsumerTooSlow, ex.Error);
        Assert.True(ex.IsRetryable);
        Assert.Equal([new EventStreamError("ConsumerTooSlow", "stopping")], errors);
    }

    [Fact]
    public async Task SubscribeAsync_RefusedUpgrade_ThrowsWithTheStatus()
    {
        var connector = new ScriptedConnector().Failing(new EventStreamException("refused", statusCode: 400));
        await using var client = new JetstreamClient(V2(), connector.Connect);

        var ex = await Assert.ThrowsAsync<JetstreamException>(
            () => client.SubscribeAsync(cursor: 1, TestContext.Current.CancellationToken).DrainAsync());

        Assert.Equal(400, ex.StatusCode);
        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public async Task SubscribeAsync_UnreadableFrame_IsSkippedAndReported()
    {
        var dropped = new List<DroppedStreamEvent>();
        var connector = new ScriptedConnector().Connection("{not json"u8.ToArray(), V2Commit(3));
        await using var client = new JetstreamClient(V2(dropped: dropped), connector.Connect);

        var events = await client.SubscribeAsync(cancellationToken: TestContext.Current.CancellationToken).DrainAsync();

        Assert.Single(events);
        Assert.Equal(StreamDropReason.Malformed, Assert.Single(dropped).Reason);
    }

    [Fact]
    public async Task SubscribeAsync_BinaryFrame_GoesThroughTheDecompressor()
    {
        var decompressor = new ReversingDecompressor();
        var compressed = V2Commit(9).Reverse().ToArray();
        var connector = new BinaryConnector(compressed);
        await using var client = new JetstreamClient(V2(decompressor: decompressor), connector.Connect);

        var events = await client.SubscribeAsync(cancellationToken: TestContext.Current.CancellationToken).DrainAsync();

        Assert.Equal(9, Assert.Single(events).Cursor);
        Assert.Equal(1, decompressor.Calls);
    }

    private sealed class ReversingDecompressor : IJetstreamDecompressor
    {
        public int Calls { get; private set; }

        public byte[] Decompress(ReadOnlySpan<byte> frame)
        {
            Calls++;
            var copy = frame.ToArray();
            Array.Reverse(copy);
            return copy;
        }
    }

    private sealed class BinaryConnector(byte[] frame)
    {
        public async IAsyncEnumerable<StreamSocketMessage> Connect(
            Uri endpoint, StreamSocketOptions options, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new StreamSocketMessage(frame, IsBinary: true);
        }
    }
}
