// Firehose consumer sample — demonstrates real-time event streaming with filtering and verification
// See docs/firehose.md for full documentation

using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Streaming;

Console.WriteLine("ATProto.NET Firehose Consumer Sample");
Console.WriteLine("====================================");
Console.WriteLine("Connecting to wss://bsky.network...");
Console.WriteLine("Filtering: app.bsky.feed.post only");
Console.WriteLine("Press Ctrl+C to stop.\n");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var dropped = 0;
var options = new TypedFirehoseConsumerOptions
{
    ServiceUrl = "wss://bsky.network",
    CollectionFilter = new HashSet<Nsid> { Nsid.Parse("app.bsky.feed.post") },
    CursorStore = new InMemoryStreamCursorStore(),
    VerifyCids = true,
    Reconnect = new StreamReconnectPolicy { MaxAttempts = null }, // Reconnect forever
    CursorPersistInterval = 100,
    OnEventDropped = _ => Interlocked.Increment(ref dropped),
};

var consumer = new TypedFirehoseConsumer(options);
var count = 0;

try
{
    // Ctrl+C cancels the token, which ends the loop normally with the cursor saved.
    await foreach (var msg in consumer.ConsumeAsync(cancellationToken: cts.Token))
    {
        if (msg is CommitEvent commit)
        {
            foreach (var op in commit.Ops ?? [])
            {
                count++;
                var action = op.Action.ToString().ToUpperInvariant();
                Console.WriteLine($"[{count}] {action} {op.Path} from {commit.Repo} (seq: {commit.Seq})");
            }
        }
    }
}
catch (EventStreamException ex)
{
    // An error reconnecting cannot fix, such as FutureCursor.
    Console.Error.WriteLine($"The firehose failed: {ex.Message}");
}

Console.WriteLine($"\nStopped at sequence {consumer.LastSeq}. Processed {count} operations, dropped {dropped} event(s).");
