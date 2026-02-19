# Firehose Streaming

ATProto.NET supports real-time event streaming via the AT Protocol firehose (WebSocket subscription).

## Basic Usage

```csharp
using ATProtoNet.Streaming;

var firehose = new FirehoseClient("wss://bsky.network");

await foreach (var message in firehose.SubscribeAsync())
{
    Console.WriteLine($"Seq: {message.Seq}");
    Console.WriteLine($"Repo: {message.Repo}");
    Console.WriteLine($"Time: {message.Time}");
}
```

## With Cursor (Resume)

Resume from a specific sequence number:

```csharp
long lastSeq = LoadLastSequence();

await foreach (var message in firehose.SubscribeAsync(cursor: lastSeq))
{
    ProcessMessage(message);
    SaveLastSequence(message.Seq);
}
```

## Cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

try
{
    await foreach (var message in firehose.SubscribeAsync(cancellationToken: cts.Token))
    {
        ProcessMessage(message);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Streaming stopped");
}
```

## Firehose Endpoints

| Endpoint | Description |
|----------|-------------|
| `wss://bsky.network` | Bluesky relay (all events) |
| `wss://your-pds:3000` | Direct PDS subscription |

## Use Cases

- **Feed generators** — process posts in real-time to build custom feeds
- **Moderation tools** — monitor content in real-time
- **Analytics** — track network activity
- **Data indexing** — build searchable indexes of AT Protocol data
- **Notifications** — trigger actions on specific events
