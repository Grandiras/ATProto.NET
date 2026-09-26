# Firehose Streaming

ATProto.NET reads the AT Protocol event streams over WebSocket: the repository stream (`com.atproto.sync.subscribeRepos`, the firehose) of a relay or PDS, and a labeler's label stream (`com.atproto.label.subscribeLabels`). There are two levels of API: a single-connection client, and reconnecting consumers with filtering, verification and cursor persistence.

## Basic Usage

`FirehoseClient` reads one connection and parses each frame into a typed `FirehoseMessage`:

```csharp
using ATProtoNet.Streaming;
using ATProtoNet.Lexicon.Com.AtProto.Sync;   // CommitEvent, SyncEvent, IdentityEvent, AccountEvent, InfoEvent

await using var firehose = new FirehoseClient("wss://bsky.network");

await foreach (var message in firehose.SubscribeAsync())
{
    if (message is CommitEvent commit)
        Console.WriteLine($"{commit.Seq}: {commit.Repo} at {commit.Time}");
}
```

The enumeration ends when the relay closes the connection or the token is cancelled, and throws an `EventStreamException` when the relay sends an error frame. It does not reconnect: use `TypedFirehoseConsumer` for that. Each call to `SubscribeAsync` opens its own connection, and disposing the client ends them all.

## With Cursor (Resume)

Every message except `#info` is a `FirehoseEvent`, whose `Seq` is the stream position. Pass the last one you handled back as the cursor to resume after it:

```csharp
long? lastSeq = LoadLastSequence();

await foreach (var message in firehose.SubscribeAsync(cursor: lastSeq))
{
    ProcessMessage(message);

    if (message is FirehoseEvent sequenced)
        SaveLastSequence(sequenced.Seq);
}
```

`TypedFirehoseConsumer` does this for you, through an `IStreamCursorStore`.

## Cancellation

Cancelling the token ends the enumeration normally: no `OperationCanceledException` is thrown, and the consumers save their cursor on the way out. This holds for every stream client and consumer in `ATProtoNet.Streaming`.

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

await foreach (var message in firehose.SubscribeAsync(cancellationToken: cts.Token))
{
    ProcessMessage(message);
}

Console.WriteLine("Streaming stopped");
```

## Constructing Streaming Clients

Streaming clients are independent of `AtProtoClient`: a relay subscription needs no session, and one process often reads the firehose without signing anyone in. Construct them with the service URL:

```csharp
// A single connection
await using var firehoseClient = new FirehoseClient("wss://bsky.network", logger);

// A reconnecting, typed consumer
var consumer = new TypedFirehoseConsumer(new TypedFirehoseConsumerOptions
{
    ServiceUrl = "wss://bsky.network",
    Logger = logger,
});
```

## Typed Firehose Consumer

`TypedFirehoseConsumer` is the highest-level API. It reconnects, parses each frame once, filters by collection, verifies CIDs and signatures, and persists its cursor.

```csharp
using ATProtoNet.Identity;
using ATProtoNet.Streaming;
using ATProtoNet.Lexicon.Com.AtProto.Sync;

var consumer = new TypedFirehoseConsumer(new TypedFirehoseConsumerOptions
{
    ServiceUrl = "wss://bsky.network",
    CollectionFilter = new HashSet<Nsid> { Nsid.Parse("app.bsky.feed.post") },
    CursorStore = new InMemoryStreamCursorStore(),
    VerifyCids = true,
    Reconnect = new StreamReconnectPolicy { MaxAttempts = null },   // reconnect forever
    CursorPersistInterval = 100,
});

await foreach (var msg in consumer.ConsumeAsync())
{
    if (msg is CommitEvent commit)
    {
        Console.WriteLine($"Commit from {commit.Repo}");
        foreach (var op in commit.Ops ?? [])
            Console.WriteLine($"  {op.Action} {op.Path}");
    }
}
```

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ServiceUrl` | `string` | (required) | Relay/PDS WebSocket URL |
| `CollectionFilter` | `IReadOnlySet<Nsid>?` | `null` | Only deliver commits with an operation in these collections |
| `CursorStore` | `IStreamCursorStore?` | `null` | Persistent cursor storage |
| `StreamId` | `string?` | Service URL | Key for cursor storage |
| `Verifier` | `FirehoseVerifier?` | `null` | Verifies every commit's signature and blocks when set; its cache is invalidated on every `#identity` event |
| `VerifyCids` | `bool` | `false` | Without a `Verifier`, check commit and sync blocks against their CIDs (local only) |
| `CursorPersistInterval` | `int` | `100` | Events between cursor saves |
| `Reconnect` | `StreamReconnectPolicy` | 5 s → 30 s, 10 attempts | Reconnect backoff and when to give up |
| `OnStreamError` | `Action<EventStreamError>?` | `null` | Every error frame the relay sends |
| `OnEventDropped` | `Action<DroppedStreamEvent>?` | `null` | Every event skipped as unreadable, of an unknown type, or failing verification |
| `Logger` | `ILogger?` | `null` | Logging |

### Filtering and parsing

A `CollectionFilter` is applied before a commit is parsed: the consumer reads the operation paths straight from the frame's CBOR, and a commit with no operation in the filtered collections is skipped without being deserialized. Most of a firehose consumer's CPU goes into commits it then drops, so a narrow filter makes the consumer far cheaper. A commit without operations, and every non-commit event, always passes.

### Verification

Set a `Verifier` and every commit is verified: its signature against the account's signing key, and every block of its CAR against its CID, in a single pass over the CAR. `VerifyCids` is the local-only check for when there is no verifier. A commit that fails is dropped and reported to `OnEventDropped` with `StreamDropReason.VerificationFailed`. `#sync` events have their blocks checked against their CIDs when either is set.

### Cursor Persistence

The cursor moves past every event the stream carries, including the ones a filter or a failed verification drops, so a rarely matching filter does not replay everything since its last match after a restart. An event you receive is recorded once you ask for the next one, so delivery is **at-least-once**: the event being handled when the process stops may be delivered again.

Saves happen every `CursorPersistInterval` events in the background, one at a time and off the read loop, so a slow store never stalls the socket (which a relay would drop as too slow). The final position is saved when the enumeration ends, whether by cancellation, a `break`, or an exception. `consumer.LastSeq` is the last position the stream passed.

Implement `IStreamCursorStore` for resumable consumption across restarts. The same interface serves the firehose, label and Jetstream consumers:

```csharp
public interface IStreamCursorStore
{
    ValueTask<long?> GetCursorAsync(string streamId, CancellationToken ct = default);
    ValueTask StoreCursorAsync(string streamId, long cursor, CancellationToken ct = default);
}
```

A built-in `InMemoryStreamCursorStore` is provided for development. For production, persist it (e.g. in a database or a file):

```csharp
public class FileStreamCursorStore(string directory) : IStreamCursorStore
{
    public async ValueTask<long?> GetCursorAsync(string streamId, CancellationToken ct)
    {
        var path = Path.Combine(directory, $"{Uri.EscapeDataString(streamId)}.cursor");
        if (!File.Exists(path)) return null;
        var text = await File.ReadAllTextAsync(path, ct);
        return long.TryParse(text, out var cursor) ? cursor : null;
    }

    public async ValueTask StoreCursorAsync(string streamId, long cursor, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Uri.EscapeDataString(streamId)}.cursor");
        await File.WriteAllTextAsync(path, cursor.ToString(), ct);
    }
}
```

### Reconnecting and errors

`StreamReconnectPolicy` is shared by every consumer:

| Property | Default | Meaning |
|---|---|---|
| `InitialDelay` | 5 s | Delay before the first reconnect, doubled on each further one |
| `MaxDelay` | 30 s | The longest delay between attempts |
| `MaxAttempts` | `10` | Consecutive failed attempts before giving up; `null` reconnects forever, `0` never |

The count resets whenever a connection delivers a frame. When the attempts run out, `ConsumeAsync` **throws** an `EventStreamException` whose `InnerException` is the last failure, rather than ending as though the stream had finished.

Relays end a stream with an error frame (`op = -1`) such as `ConsumerTooSlow` or `FutureCursor`. Each is reported to `OnStreamError`; the consumer then reconnects from its cursor when the error is retryable (`ConsumerTooSlow`), and throws when it is not (`FutureCursor`, whose cursor is ahead of the relay and would fail the same way forever):

```csharp
try
{
    await foreach (var msg in consumer.ConsumeAsync(cancellationToken: stoppingToken))
        await HandleAsync(msg);
}
catch (EventStreamException ex) when (ex.Error == EventStreamErrors.FutureCursor)
{
    logger.LogError("The stored cursor is ahead of the relay; was it pointed at another relay?");
}
catch (EventStreamException ex)
{
    logger.LogError(ex, "The firehose failed ({Error}, HTTP {Status})", ex.Error, ex.StatusCode);
}
```

### Dropped events

A frame that cannot be read — malformed CBOR, a missing required field, or an identifier that does not parse (a `repo` that is not a DID, a `commit` that is not a CID) — is skipped, and so is a message type this SDK version does not model. The cursor still moves past it. `OnEventDropped` reports each one, with its `Reason`, its `Cursor` when it could be read, and a `Detail`:

```csharp
OnEventDropped = drop => metrics.Dropped(drop.Reason.ToString()),
```

## Event Parsing

`FirehoseEventParser` decodes a raw CBOR firehose frame read some other way:

```csharp
FirehoseMessage? message = FirehoseEventParser.Parse(frameBytes);
```

It returns `null` for a malformed frame or an unknown message type, and throws an `EventStreamException` for an error frame.

### Message Types

The message types live in `ATProtoNet.Lexicon.Com.AtProto.Sync`:

| Type | Description |
|------|-------------|
| `CommitEvent` | Repository commit with record operations |
| `IdentityEvent` | Identity/handle changes |
| `AccountEvent` | Account status changes |
| `SyncEvent` | Sync v1.1 messages for repo state recovery |
| `InfoEvent` | An informational notice such as `OutdatedCursor`; not sequenced |

All but `InfoEvent` derive from `FirehoseEvent`, which carries `Seq` and `Time`. `SyncEvent`, `IdentityEvent` and `AccountEvent` carry their account as a `Did`, and a handle as a `Handle`.

### CommitEvent Properties

| Property | Type | Description |
|----------|------|-------------|
| `Repo` | `Did` | DID of the repository |
| `Commit` | `Cid` | CID of the commit block |
| `Rev` | `Tid` | Revision of the commit |
| `Since` | `Tid?` | Revision the diff is relative to, for a partial commit |
| `Seq` | `long` | Sequence number (from `FirehoseEvent`) |
| `Time` | `AtDatetime?` | When the upstream host emitted the event (from `FirehoseEvent`) |
| `Ops` | `IReadOnlyList<RepoOp>?` | Record operations |
| `Blocks` | `byte[]?` | CAR-encoded block data |
| `PrevData` | `Cid?` | Previous data CID (Sync v1.1) |

`TooBig`, `Rebase` and `Blobs` are deprecated upstream and marked `[Obsolete]`: producers always send `false` or an empty list.

### RepoOp

| Property | Type | Description |
|----------|------|-------------|
| `Action` | `RepoOpAction` | `Create`, `Update` or `Delete`, the same type Jetstream's `JetstreamCommitEvent.Operation` uses |
| `Path` | `string` | `collection/rkey` path |
| `Cid` | `Cid?` | CID of the record |
| `Prev` | `Cid?` | Previous CID for inductive verification |

### Record events

`commit.GetRecordEvents()` splits a commit into one `FirehoseRecordEvent` per operation, with the collection and record key parsed and the record decoded from the commit's blocks into the JSON data model. It implements `IRecordEvent`, as `JetstreamCommitEvent` does, so indexing code can take either stream:

```csharp
async Task IndexAsync(IRecordEvent change)
{
    if (change.Operation == RepoOpAction.Delete)
        await index.RemoveAsync(change.Uri);
    else
        await index.UpsertAsync(change.Uri, change.Cid, change.GetRecord<PostRecord>());
}

foreach (var change in commit.GetRecordEvents())
    await IndexAsync(change);
```

The blocks are read, not verified: set a `Verifier` on the consumer if the records must be authentic.

## Label Streams

A labeler publishes its labels on `com.atproto.label.subscribeLabels`. `FirehoseClient.SubscribeLabelsAsync` reads one connection; `LabelStreamConsumer` reconnects and persists the cursor, with the same options, policy and error handling as the firehose consumer:

```csharp
using ATProtoNet.Lexicon.Com.AtProto.Label;

var labels = new LabelStreamConsumer(new LabelStreamConsumerOptions
{
    ServiceUrl = "wss://mod.bsky.app",
    CursorStore = cursorStore,
});

await foreach (var message in labels.ConsumeAsync(cancellationToken: stoppingToken))
{
    if (message is LabelsEvent batch)
    {
        foreach (var label in batch.Labels)
            Console.WriteLine($"{label.Src} {(label.Neg == true ? "removed" : "applied")} {label.Val} on {label.Uri}");
    }
}
```

Messages are a `LabelsEvent` (`Seq`, the cursor, and `Labels`) or a `LabelInfoEvent` notice such as `OutdatedCursor`.

## Commit Verification

`FirehoseVerifier` verifies firehose commits: that every block of the CAR matches its CID, and that the commit is signed by the account's current key. It does not invert the operations against the previous MST root (`prevData`).

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

Verify commit signatures against the signing key in the author's DID document; this checks every block's CID as well:

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
await using var firehose = new FirehoseClient("wss://custom-relay.example.com");
```

## Use Cases

- **Feed generators** — process posts in real-time to build custom feeds
- **Moderation tools** — monitor content in real-time
- **Analytics** — track network activity
- **Data indexing** — build searchable indexes of AT Protocol data
- **Notifications** — trigger actions on specific events
- **Backup** — replicate repository data with verification

## Next Steps

- [Jetstream Streaming](jetstream.md) — server-side filtered JSON streams, and the v2 archive
- [Cryptography](crypto.md) — DAG-CBOR, CID computation used by the firehose
- [Identity Resolution](did-resolution.md) — Required for signature verification
- [Low-Level Repo API](low-level-repo.md) — CAR file parsing for commit blocks
