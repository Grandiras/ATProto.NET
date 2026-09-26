# Firehose Streaming

ATProto.NET supports real-time event streaming via the AT Protocol firehose (WebSocket subscription). It provides three levels of API — from low-level raw frames to a high-level typed consumer with verification and cursor persistence.

## Basic Usage

```csharp
using ATProtoNet.Streaming;

var firehose = new FirehoseClient("wss://bsky.network");

await foreach (var frame in firehose.SubscribeAsync())
{
    if (FirehoseEventParser.Parse(frame) is CommitEvent commit)
        Console.WriteLine($"{commit.Seq}: {commit.Repo} at {commit.Time}");
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

## Constructing Streaming Clients

Streaming clients are independent of `AtProtoClient`: a relay subscription needs no session, and
one process often reads the firehose without signing anyone in. Construct them with the relay URL:

```csharp
// A low-level firehose client
using var firehoseClient = new FirehoseClient("wss://bsky.network", logger);

// A reconnecting firehose consumer
using var consumer = new FirehoseConsumer("wss://bsky.network", logger, reconnectDelay: TimeSpan.FromSeconds(5));
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
| `Verifier` | `FirehoseVerifier?` | `null` | Verifier instance; its cache is invalidated on every `#identity` event |
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
| `Repo` | `Did` | DID of the repository |
| `Commit` | `Cid` | CID of the commit block |
| `Rev` | `Tid` | Revision of the commit |
| `Since` | `Tid?` | Revision the diff is relative to, for a partial commit |
| `Seq` | `long` | Sequence number (from `FirehoseMessage`) |
| `Time` | `AtDatetime?` | When the upstream host emitted the event (from `FirehoseMessage`) |
| `Ops` | `IReadOnlyList<RepoOp>?` | Record operations |
| `Blocks` | `byte[]?` | CAR-encoded block data |
| `TooBig` | `bool` | Commit was too large to inline — fetch the repo separately |
| `PrevData` | `Cid?` | Previous data CID (Sync v1.1) |
| `Blobs` | `IReadOnlyList<Cid>?` | Referenced blobs (deprecated — soon always empty) |

`SyncEvent`, `IdentityEvent`, `AccountEvent` and the legacy `HandleEvent` and `TombstoneEvent` carry
their account as a `Did`, and a handle as a `Handle`.

A frame whose identifiers do not parse — a `repo` that is not a DID, a `commit` that is not a CID —
is dropped like any other malformed frame: `FirehoseEventParser.Parse` returns `null`.

### RepoOp

| Property | Type | Description |
|----------|------|-------------|
| `Action` | `RepoOpAction` | `Create`, `Update` or `Delete` — the same three as Jetstream's `JetstreamOperation` |
| `Path` | `string` | `collection/rkey` path |
| `Cid` | `Cid?` | CID of the record |
| `Prev` | `Cid?` | Previous CID for inductive verification |

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

Verify commit signatures against the signing key in the author's DID document:

```csharp
using var verifier = new FirehoseVerifier(); // its own CachingDidResolver

var result = await verifier.VerifySignatureAsync(commitEvent);

if (result.IsValid)
{
    Console.WriteLine("Signature verified against DID signing key");
}

// Or share a resolver (and its cache) with the rest of the application
using var verifier2 = new FirehoseVerifier(myCachingDidResolver);
```

A relay carries thousands of commits a second from far fewer accounts, so keys come from a
cached DID document: a cache hit costs no network at all, and the parsed key is cached as well.
Pass a `CachingDidResolver` (or any caching `IDidResolver`) to the second constructor — an
uncached resolver makes a directory request per commit.

Two rules keep the cache correct, as the sync spec requires:

- **`#identity` events invalidate.** Call `verifier.InvalidateIdentityAsync(did)` for each one, so
  the account's next commit is checked against a freshly resolved key. `TypedFirehoseConsumer`
  does this whenever it has a `Verifier`.
- **A failed signature refetches once.** A signature that does not verify against the cached key
  is checked again against a refreshed document before it is reported as bad. Refreshes of one
  DID are rate-limited (`DidCacheOptions.MinRefreshInterval`), so a stream of forged commits
  does not turn into a directory request each.

See [Identity Resolution](did-resolution.md) for the cache's lifetimes and the fetch policy.

## Firehose Endpoints

| Endpoint | Description |
|----------|-------------|
| `wss://bsky.network` | Bluesky relay (all events) |
| `wss://your-pds:3000` | Direct PDS subscription |

## Custom Relay URL

Pass any relay (or a PDS, for its own repositories) to the constructor:

```csharp
using var firehose = new FirehoseClient("wss://custom-relay.example.com");
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
- [Identity Resolution](did-resolution.md) — Required for signature verification
- [Low-Level Repo API](low-level-repo.md) — CAR file parsing for commit blocks
