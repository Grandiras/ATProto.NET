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

`TypedFirehoseConsumer` is the highest-level API. It reconnects, parses each frame once, filters by collection, verifies CIDs and signatures (or the full Sync 1.1 chain, see [Verifying the firehose](#verifying-the-firehose-sync-11)), and persists its cursor.

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
| `SyncVerifier` | `RepoSyncVerifier?` | `null` | Verifies every `#commit` and `#sync` inductively (Sync 1.1) and delivers only events that chain; set this or `Verifier` |
| `Resync` | `RepoResyncOptions?` | `null` | With `SyncVerifier`, fetches repositories whose chain breaks and delivers them as `RepoResyncEvent`s |
| `VerifyCids` | `bool` | `false` | Without a `Verifier`, check commit and sync blocks against their CIDs (local only) |
| `CursorPersistInterval` | `int` | `100` | Events between cursor saves |
| `Reconnect` | `StreamReconnectPolicy` | 5 s → 30 s, 10 attempts | Reconnect backoff and when to give up |
| `OnStreamError` | `Action<EventStreamError>?` | `null` | Every error frame the relay sends |
| `OnEventDropped` | `Action<DroppedStreamEvent>?` | `null` | Every event skipped as unreadable, of an unknown type, failing verification, stale, or for a desynchronized repository |
| `Logger` | `ILogger?` | `null` | Logging |

### Filtering and parsing

A `CollectionFilter` is applied before a commit is parsed: the consumer reads the operation paths straight from the frame's CBOR, and a commit with no operation in the filtered collections is skipped without being deserialized. Most of a firehose consumer's CPU goes into commits it then drops, so a narrow filter makes the consumer far cheaper. A commit without operations, and every non-commit event, always passes. With a `SyncVerifier` every commit has to be verified to keep its repository's chain, so the filter then only decides which verified commits are delivered.

### Verification

Set a `Verifier` and every commit is verified: its signature against the account's signing key, and every block of its CAR against its CID, in a single pass over the CAR. `VerifyCids` is the local-only check for when there is no verifier. A commit that fails is dropped and reported to `OnEventDropped` with `StreamDropReason.VerificationFailed`. `#sync` events have their blocks checked against their CIDs when either is set.

A signature shows a commit is authentic, not that you have every commit. For that, set a `SyncVerifier` instead: see [Verifying the firehose](#verifying-the-firehose-sync-11).

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

With a `SyncVerifier`, two more reasons appear: `Stale`, for an event no newer than the last one verified for its repository (a replay), and `Desynchronized`, for an authentic event of a repository whose chain is broken. Their `Detail` starts with the repository's DID.

## Verifying the firehose (Sync 1.1)

A signature proves a commit is authentic. It does not prove you received *every* commit: a relay that drops one, or a reconnect that skips past it, leaves you with a silently stale copy of the repository. Sync 1.1 makes the firehose inductively verifiable. Each `#commit` carries `prevData`, the root of the repository's tree before the commit, and its blocks carry the part of the tree its operations touched. Replaying the operations backwards against that partial tree must land exactly on `prevData`, and `prevData` must be the tree of the last commit you verified for that repository. When it is not, the repository is *desynchronized* and must be fetched again. See the [sync spec](https://atproto.com/specs/sync).

`RepoSyncVerifier` runs the spec's validation checklist:

| Check | On failure |
|---|---|
| The event's `blocks` are at most 2,000,000 bytes, a `#commit` has at most 200 operations, and every block matches its CID | `Invalid` |
| The CAR's first root is the event's `commit`; the commit is version 3; its `did` and `rev` match the event | `Invalid` |
| The revision is not in the future (5 minutes of clock skew by default) | `Invalid` |
| The revision is newer than the last one seen for the repository | `Stale` |
| The signature verifies against the account's signing key, refetching the DID document once if it fails | `Invalid` |
| Operations are well formed (a create has a `cid` and no `prev`, an update both, a delete a `prev` and no `cid`; no path twice), and every created or updated record is in the blocks (DAG-CBOR, at most 1,000,000 bytes) | `Invalid` |
| Inverting the operations against the partial tree lands exactly on `prevData` | `Invalid` |
| `prevData` is the tree of the last verified commit; a commit without `prevData` (from a host older than Sync 1.1) cannot show its operations are complete, so it is accepted only as a repository's first | `Desynchronized` |
| A `#sync` event asserts the tree already verified | `Desynchronized` when it moves to another tree |

An `Invalid` event is rejected and leaves the repository's state alone. `Desynchronized` marks the repository desynchronized, and invokes `RepoSyncVerifierOptions.OnDesynchronized` once, with the reason. A repository the verifier has no state for starts its chain at its first valid event. Like the reference relay and Tap, `since` is not required to match when `prevData` does: an empty commit moves the revision and not the tree, so missing one loses no record. The MST inversion follows indigo's `atproto/repo/mst`, the code the reference relay and Tap invert commits with.

### In the typed consumer

```csharp
using ATProtoNet.Repo;
using ATProtoNet.Streaming;
using ATProtoNet.Lexicon.Com.AtProto.Sync;

using var verifier = new RepoSyncVerifier(new RepoSyncVerifierOptions
{
    StateStore = new InMemoryRepoSyncStateStore(),
    OnDesynchronized = result => logger.LogWarning("{Did} desynchronized: {Reason}", result.Did, result.Reason),
});

var consumer = new TypedFirehoseConsumer(new TypedFirehoseConsumerOptions
{
    ServiceUrl = "wss://bsky.network",
    SyncVerifier = verifier,
    Resync = new RepoResyncOptions(),   // fetch broken repositories again
    CursorStore = cursorStore,
});

await foreach (var message in consumer.ConsumeAsync(cancellationToken: stoppingToken))
{
    switch (message)
    {
        case CommitEvent commit:
            foreach (var change in commit.GetRecordEvents())
                await index.ApplyAsync(change);
            break;

        case RepoResyncEvent resync:
            // The repository's whole, verified contents: reconcile what you hold for it.
            await index.ReconcileAsync(resync.Did, resync.Snapshot.Records, resync.Snapshot.GetRecord);
            break;
    }
}
```

Only events that verify and chain are delivered. A repository's new state is recorded when you ask for the next message, like the cursor, so an event you were processing when the process stopped is verified and delivered again rather than skipped.

### Resynchronizing

Without `Resync`, a desynchronized repository stays that way: its events are dropped with `StreamDropReason.Desynchronized` until you fetch it yourself and record a new state with `verifier.StateStore.SetAsync`. With `Resync`, the consumer does it:

1. It downloads the repository's export (`com.atproto.sync.getRepo`) in the background, from the relay first, as the spec's guidance on "thundering herds" asks: the relay can serve a cached copy, or redirect to the PDS. It falls back to the account's own PDS, and skips a copy older than the event that broke the chain.
2. It verifies the export: every block's CID, the commit's repository, version and signature, and that the tree is complete and canonical (`RepoSnapshot`).
3. It delivers a `RepoResyncEvent`. Reconcile against `Snapshot.Records`: a record you lack was created, one whose CID differs was updated, one you hold that the snapshot lacks was deleted.
4. The repository's events that arrived during the fetch were verified and held; they follow the snapshot in order, so nothing between the snapshot and the live stream is lost. The cursor is not saved past a held event until it has been delivered, so a process that stops in between sees it again.

Everything is bounded (`RepoResyncOptions`): `MaxConcurrency` repositories fetched or fetched-and-waiting at once (4), each export at most `MaxRepoBytes` (256 MiB), so exports take at most about `(MaxConcurrency + 1) × MaxRepoBytes` of memory; `MaxPendingRepos` more wait for a turn (1,000), and `MaxHeldEventsPerRepo` (1,000) and `MaxHeldBytes` (64 MiB) bound the events held meanwhile. A repository over a limit is fetched again later, and one whose fetch fails waits `RetryDelay` before the next attempt. The default fetchers only connect to public addresses; set `AllowPrivateNetworks` for a development PDS, or pass your own `IRepoFetcher`s, such as a mirror, as `Fetchers`.

Repositories the store lists as not synchronized are picked up every `ScanInterval`: ones left over from a run that stopped mid-fetch, and ones you mark yourself. To backfill a repository you have not seen on the firehose, for example one found through `com.atproto.sync.listReposByCollection`, mark it:

```csharp
await verifier.StateStore.SetAsync(new RepoSyncState(did, Rev: null, Data: null, RepoSyncStatus.Desynchronized));
```

`RepoSnapshot.Verify(car, did, signingKey)` verifies an export you downloaded some other way.

### Keeping state

`IRepoSyncStateStore` keeps each repository's last revision, tree root and status. `InMemoryRepoSyncStateStore` is fast and forgets everything on restart, after which each repository starts a new chain at its next commit. `ATProtoNet.Server` has an EF Core store that survives restarts:

```csharp
builder.Services.AddDbContextFactory<RepoSyncStateDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddAtProtoEfCoreRepoSyncStateStore<RepoSyncStateDbContext>();

// later
var store = app.Services.GetRequiredService<IRepoSyncStateStore>();
using var verifier = new RepoSyncVerifier(new RepoSyncVerifierOptions { StateStore = store });
```

To keep the table in a context of your own, call `RepoSyncStateDbContext.ConfigureRepoSyncStateModel(modelBuilder)` from its `OnModelCreating`. The verifier reads a repository's state for every commit and writes it once the commit is processed, two round trips per event, which the full network outpaces; at that scale keep the state in memory and persist it in batches, or track fewer repositories.

### Cost

Verification is dominated by the signature check. On the captured frames in the test suite (one thread of a Ryzen 5 7600X, Release build):

| Commit | `FirehoseVerifier` (signature and CIDs) | `RepoSyncVerifier` (the whole checklist) |
|---|---|---|
| 1 operation, 3.3 KB of blocks | 234 µs, 9.5 KB allocated | 259 µs, 30 KB allocated |
| 3 operations, 10 KB of blocks | 224 µs, 20 KB allocated | 304 µs, 74 KB allocated |

### Using the verifier directly

`VerifyCommitAsync` and `VerifySyncAsync` return a `RepoSyncResult`: its `Outcome` (`Valid`, `Stale`, `Invalid` or `Desynchronized`), the `Reason`, and for a valid event the `State` it moves the repository to. A valid event's state is recorded only by `ApplyAsync`, which you call once the event is processed; a desynchronizing event is recorded at once. Verify and apply one repository's events in stream order, and pass every `#identity` event to `InvalidateIdentityAsync`.

The verifier does not track account hosting status (`#account` events) or check which host is authoritative for an account; a relay applies both before events reach you.

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
| `SyncEvent` | Sync 1.1 assertion of a repository's current commit, used to recover from a broken chain |
| `InfoEvent` | An informational notice such as `OutdatedCursor`; not sequenced |

All but `InfoEvent` derive from `FirehoseEvent`, which carries `Seq` and `Time`. `TypedFirehoseConsumer` adds one message of its own with `Resync` set, `RepoResyncEvent`, which is not part of the wire protocol and not sequenced. `SyncEvent`, `IdentityEvent` and `AccountEvent` carry their account as a `Did`, and a handle as a `Handle`.

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
| `PrevData` | `Cid?` | The repository's tree root before the commit (Sync 1.1) |

`TooBig`, `Rebase` and `Blobs` are deprecated upstream and marked `[Obsolete]`: producers always send `false` or an empty list.

### RepoOp

| Property | Type | Description |
|----------|------|-------------|
| `Action` | `RepoOpAction` | `Create`, `Update` or `Delete`, the same type Jetstream's `JetstreamCommitEvent.Operation` uses |
| `Path` | `string` | `collection/rkey` path |
| `Cid` | `Cid?` | CID of the record |
| `Prev` | `Cid?` | The record's CID before an update or delete, which inverting the commit needs |

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

The blocks are read, not verified: set a `Verifier` or a `SyncVerifier` on the consumer if the records must be authentic.

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

Set `Verifier = new LabelVerifier(resolver)` on the options to check every label's signature against its labeler's `#atproto_label` key. Each `LabelsEvent` then carries `Verification`, one result per label; labels that fail are still delivered, so filter on `result.IsValid`. See [Labeler Services](labeler.md#verifying-labels).

## Commit Verification

`FirehoseVerifier` verifies firehose commits: that every block of the CAR matches its CID, and that the commit is signed by the account's current key. It does not invert the operations against the previous MST root (`prevData`) or chain commits; `RepoSyncVerifier` does ([Verifying the firehose](#verifying-the-firehose-sync-11)).

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
- [Tap](tap.md) — let a Tap instance verify, backfill and filter the firehose, and read plain JSON events
- [Cryptography](crypto.md) — DAG-CBOR, CID computation used by the firehose
- [Identity Resolution](did-resolution.md) — Required for signature verification
- [Low-Level Repo API](low-level-repo.md) — CAR file parsing for commit blocks
