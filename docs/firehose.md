# Firehose Streaming

ATProto.NET supports real-time event streaming via the AT Protocol firehose (WebSocket subscription). It provides three levels of API — from low-level raw frames to a high-level typed consumer with verification and cursor persistence.

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

## Convenience Methods

Create firehose clients directly from `AtProtoClient`:

```csharp
var client = new AtProtoClientBuilder()
    .WithInstanceUrl("https://bsky.social")
    .WithRelayUrl("wss://bsky.network")  // Default
    .Build();

// Create a low-level firehose client
var firehoseClient = client.CreateFirehoseClient();

// Create a reconnecting firehose consumer
var consumer = client.CreateFirehoseConsumer();
```

## Typed Firehose Consumer

The `TypedFirehoseConsumer` is the highest-level API. It parses CBOR frames into typed `FirehoseMessage` objects, supports collection filtering, CID/signature verification, and persistent cursor storage.

```csharp
using ATProtoNet.Streaming;
using ATProtoNet.Lexicon.Com.AtProto.Sync;   // CommitEvent, SyncEvent, IdentityEvent, AccountEvent

var options = new TypedFirehoseConsumerOptions
{
    ServiceUrl = "wss://bsky.network",
    CollectionFilter = new HashSet<string> { "app.bsky.feed.post" },
    CursorStore = new InMemoryFirehoseCursorStore(),
    VerifyCids = true,
    ReconnectDelay = TimeSpan.FromSeconds(5),
    MaxReconnectAttempts = 10,
    CursorPersistInterval = 100,
};

var consumer = new TypedFirehoseConsumer(options);

await foreach (var msg in consumer.ConsumeAsync())
{
    if (msg is CommitEvent commit)
    {
        Console.WriteLine($"Commit from {commit.Repo}");
        foreach (var op in commit.Ops ?? [])
        {
            Console.WriteLine($"  {op.Action} {op.Path}");
        }
    }
}
```

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ServiceUrl` | `string` | (required) | Relay/PDS WebSocket URL |
| `CollectionFilter` | `IReadOnlySet<string>?` | `null` | Only emit events for these collections |
| `CursorStore` | `IFirehoseCursorStore?` | `null` | Persistent cursor storage |
| `StreamId` | `string?` | Service URL | Key for cursor storage |
| `Verifier` | `FirehoseVerifier?` | `null` | Verifier instance |
| `VerifyCids` | `bool` | `false` | Verify CID integrity on commits |
| `VerifySignatures` | `bool` | `false` | Verify commit signatures (needs Verifier) |
| `CursorPersistInterval` | `int` | `100` | Events between cursor saves |
| `ReconnectDelay` | `TimeSpan` | 5 seconds | Delay between reconnections |
| `MaxReconnectAttempts` | `int` | `10` | Max reconnections (-1 = unlimited) |

### Cursor Persistence

Implement `IFirehoseCursorStore` for resumable consumption across restarts:

```csharp
public interface IFirehoseCursorStore
{
    Task<long?> GetCursorAsync(string streamId, CancellationToken ct = default);
    Task StoreCursorAsync(string streamId, long cursor, CancellationToken ct = default);
}
```

A built-in `InMemoryFirehoseCursorStore` is provided for development:

```csharp
var cursorStore = new InMemoryFirehoseCursorStore();
```

For production, implement persistent storage (e.g., backed by a database or file):

```csharp
public class FileFirehoseCursorStore : IFirehoseCursorStore
{
    private readonly string _directory;

    public FileFirehoseCursorStore(string directory) => _directory = directory;

    public async Task<long?> GetCursorAsync(string streamId, CancellationToken ct)
    {
        var path = Path.Combine(_directory, $"{streamId}.cursor");
        if (!File.Exists(path)) return null;
        var text = await File.ReadAllTextAsync(path, ct);
        return long.TryParse(text, out var cursor) ? cursor : null;
    }

    public async Task StoreCursorAsync(string streamId, long cursor, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{streamId}.cursor");
        await File.WriteAllTextAsync(path, cursor.ToString(), ct);
    }
}
```

## Event Parsing

`FirehoseEventParser` decodes raw CBOR firehose frames into typed objects:

```csharp
using ATProtoNet.Streaming;

// Parse a raw frame
FirehoseMessage? message = FirehoseEventParser.Parse(rawFrame);

// Or from raw bytes
FirehoseMessage? message = FirehoseEventParser.Parse(cborBytes);
```

### Message Types

The message types live in `ATProtoNet.Lexicon.Com.AtProto.Sync`:

| Type | Description |
|------|-------------|
| `CommitEvent` | Repository commit with record operations |
| `IdentityEvent` | Identity/handle changes |
| `AccountEvent` | Account status changes |
| `SyncEvent` | Sync v1.1 messages for repo state recovery |

### CommitEvent Properties

| Property | Type | Description |
|----------|------|-------------|
| `Repo` | `string` | DID of the repository |
| `Commit` | `string` | CID of the commit block |
| `Rev` | `string` | Revision string (a TID) |
| `Since` | `string?` | Revision the diff is relative to, for a partial commit |
| `Seq` | `long` | Sequence number (from `FirehoseMessage`) |
| `Time` | `string?` | ISO 8601 timestamp (from `FirehoseMessage`) |
| `Ops` | `List<RepoOp>?` | Record operations |
| `Blocks` | `byte[]?` | CAR-encoded block data |
| `TooBig` | `bool` | Commit was too large to inline — fetch the repo separately |
| `PrevData` | `string?` | Previous data CID (Sync v1.1) |
| `Blobs` | `List<string>?` | Referenced blobs (deprecated — soon always empty) |

### RepoOp

| Property | Type | Description |
|----------|------|-------------|
| `Action` | `string` | `"create"`, `"update"`, `"delete"` |
| `Path` | `string` | `collection/rkey` path |
| `Cid` | `string?` | CID of the record |
| `Prev` | `string?` | Previous CID for inductive verification |

## Commit Verification

`FirehoseVerifier` verifies the authenticity of firehose events:

### CID Verification

Verify that block CIDs match their content (local-only, no network access):

```csharp
using ATProtoNet.Streaming;

var result = FirehoseVerifier.VerifyCid(commitEvent);

if (result.IsValid)
{
    Console.WriteLine("CID integrity verified");
}
else
{
    Console.WriteLine($"Verification failed: {result.Error}");
}
```

### Signature Verification

Verify commit signatures against the signer's DID document (requires network access):

```csharp
var verifier = new FirehoseVerifier();  // Uses default DidResolver

var result = await verifier.VerifySignatureAsync(commitEvent);

if (result.IsValid)
{
    Console.WriteLine("Signature verified against DID signing key");
}

// Or with a custom DID resolver
var resolver = new DidResolver();
using var verifier = new FirehoseVerifier(resolver);
```

## Firehose Endpoints

| Endpoint | Description |
|----------|-------------|
| `wss://bsky.network` | Bluesky relay (all events) |
| `wss://your-pds:3000` | Direct PDS subscription |

## Custom Relay URL

Configure a custom relay URL:

```csharp
var client = new AtProtoClientBuilder()
    .WithInstanceUrl("https://bsky.social")
    .WithRelayUrl("wss://custom-relay.example.com")
    .Build();
```

## Use Cases

- **Feed generators** — process posts in real-time to build custom feeds
- **Moderation tools** — monitor content in real-time
- **Analytics** — track network activity
- **Data indexing** — build searchable indexes of AT Protocol data
- **Notifications** — trigger actions on specific events
- **Backup** — replicate repository data with verification

## Next Steps

- [Cryptography](crypto.md) — DAG-CBOR, CID computation used by the firehose
- [DID Resolution](did-resolution.md) — Required for signature verification
- [Low-Level Repo API](low-level-repo.md) — CAR file parsing for commit blocks
