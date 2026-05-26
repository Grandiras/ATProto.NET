// Firehose consumer sample — demonstrates real-time event streaming with filtering and verification
// See docs/firehose.md for full documentation

using ATProtoNet.Streaming;
using ATProtoNet.Lexicon.Com.AtProto.Sync;

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

var options = new TypedFirehoseConsumerOptions
{
    ServiceUrl = "wss://bsky.network",
    CollectionFilter = new HashSet<string> { "app.bsky.feed.post" },
    CursorStore = new InMemoryFirehoseCursorStore(),
    VerifyCids = true,
    ReconnectDelay = TimeSpan.FromSeconds(5),
    MaxReconnectAttempts = -1, // Unlimited reconnections
    CursorPersistInterval = 100,
};

var consumer = new TypedFirehoseConsumer(options);
var count = 0;

try
{
    await foreach (var msg in consumer.ConsumeAsync(cancellationToken: cts.Token))
    {
        if (msg is CommitEvent commit)
        {
            foreach (var op in commit.Ops ?? [])
            {
                count++;
                var action = op.Action?.ToUpperInvariant() ?? "UNKNOWN";
                Console.WriteLine($"[{count}] {action} {op.Path} from {commit.Repo} (seq: {commit.Seq})");
            }
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine($"\nStopped. Processed {count} operations.");
}
