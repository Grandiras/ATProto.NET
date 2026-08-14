# Jetstream Streaming

ATProto.NET supports [Jetstream](https://github.com/bluesky-social/jetstream), the JSON alternative to the binary [firehose](firehose.md). Jetstream's key advantage is **server-side filtering**: the server only sends events for the collections and DIDs you ask for, so tailing a niche collection costs almost no bandwidth — where the binary firehose always delivers the whole network's commit stream.

> **⚠ Not cryptographically verifiable.** Jetstream events are plain JSON without MST proofs or commit signatures. Unlike `TypedFirehoseConsumer` (which offers `VerifyCids`/`VerifySignatures`), a Jetstream consumer must trust the Jetstream instance. For canonical state, re-fetch records via `com.atproto.repo.getRecord`. Use the binary firehose when verification matters.

## Basic Usage

```csharp
using ATProtoNet.Streaming;

var client = new JetstreamClient(new JetstreamConsumerOptions
{
    ServiceUrl = JetstreamEndpoints.UsEast,
    Protocol = JetstreamProtocol.V2,
    WantedCollections = ["app.bsky.feed.post"],
    WantedKinds = [JetstreamEventKind.Commit],
});

await foreach (var evt in client.SubscribeAsync())
{
    if (evt is JetstreamCommitEvent commit)
        Console.WriteLine($"{commit.Operation} {commit.Uri}");
}
```

`JetstreamClient` manages a single connection with no reconnect logic. For production use, prefer `JetstreamConsumer` (below).

## Protocol Versions

Jetstream has two wire protocols, and the SDK speaks both. `Protocol` selects; it defaults to `JetstreamProtocol.V1` so existing configurations keep working unchanged.

| | `JetstreamProtocol.V1` | `JetstreamProtocol.V2` |
|---|---|---|
| Endpoint path | `/subscribe` | `/xrpc/network.bsky.jetstream.subscribeEvents` |
| Framing | bare JSON object per frame | `{"$type":"message","payload":{…}}` envelope, `xrpc.v1.json` subprotocol |
| Filters | `WantedCollections`, `WantedDids` | plus `WantedKinds` |
| Commit shape | nested under `commit` | flat |
| Cursor | `TimeUs` (unix µs) | `Cursor` (sequence number) |
| `JetstreamSyncEvent` | never emitted | emitted |
| Stale cursor | silently clamped | rejected pre-upgrade (`JetstreamConnectException`) |
| Compression | `compress=true` | versioned zstd dictionary |

Prefer v2 for anything new: the sequence-number cursor is exact where a timestamp is approximate, resync markers are only delivered there, and a cursor the server can no longer honour is an error rather than a silent gap.

`JetstreamEndpoints` names the public Bluesky-operated instances:

```csharp
JetstreamEndpoints.UsEast        // wss://jetstream.us-east.bsky.network — v2 (also serves v1)
JetstreamEndpoints.UsWest        // wss://jetstream.us-west.bsky.network — v2 (also serves v1)
JetstreamEndpoints.LegacyUsEast1 // wss://jetstream1.us-east.bsky.network — v1 only
JetstreamEndpoints.LegacyUsEast2 // …and LegacyUsWest1 / LegacyUsWest2
```

Only the two v2 hosts serve v2; the legacy hosts answer `/xrpc/…` with a 404. Both host generations serve v1, so pointing an existing v1 configuration at `JetstreamEndpoints.UsEast` is safe.

## Event Types

| Type | Meaning |
|---|---|
| `JetstreamCommitEvent` | Record create/update/delete. Exposes `Collection`, `RKey`, `Operation`, `Rev`, `Cid`, raw `Record` JSON, computed `Uri`, and typed `GetRecord<T>()`. |
| `JetstreamIdentityEvent` | Identity change (e.g. handle update). |
| `JetstreamAccountEvent` | Account status change (`Active`, `Status` like `"takendown"`). |
| `JetstreamSyncEvent` | **v2 only.** The account's commit chain could not be followed — drop its derived state and re-read the repo. Carries `Rev` and the commit CAR in `Blocks`. |

All events carry:

- `Did` — the repo the event concerns.
- `TimeUs` — the event timestamp in unix **microseconds**, and the v1 cursor. On v2 it is derived from the frame's RFC 3339 `time`, whose six fractional digits convert exactly.
- `Timestamp` — the same instant as a `DateTimeOffset`.
- `Cursor` — Jetstream's monotonic sequence number, and the v2 cursor. Populated on every v2 event; on v1 it comes from the `cursor` field that only the v2 hosts emit, and is `null` against a legacy instance.

`JetstreamIdentityEvent`, `JetstreamAccountEvent`, and `JetstreamSyncEvent` also expose the *upstream* relay's `Seq` and `Time`, which are distinct from Jetstream's own `Cursor` and `TimeUs`.

Typed record access uses the SDK's serialization defaults, including types registered in `LexiconTypeRegistry`:

```csharp
if (evt is JetstreamCommitEvent { Operation: not JetstreamOperation.Delete } commit)
{
    var post = commit.GetRecord<PostRecord>();
}
```

The parser is forward-tolerant: unknown event kinds, operations, and fields are skipped rather than throwing, so consumers keep working as the Jetstream protocol evolves.

## Filtering

```csharp
var options = new JetstreamConsumerOptions
{
    ServiceUrl = JetstreamEndpoints.UsEast,
    Protocol = JetstreamProtocol.V2,
    // Full NSIDs or prefix wildcards; max 100 entries.
    WantedCollections = ["exchange.recipe.recipe", "app.bsky.graph.*"],
    // Optional DID filter; max 10,000 entries.
    WantedDids = ["did:plc:q6gjnaw2blty4crticxkmujt"],
    // v2 only. Omitted, every kind is delivered.
    WantedKinds = [JetstreamEventKind.Commit, JetstreamEventKind.Account],
    // Optional: drop records larger than this server-side.
    MaxMessageSizeBytes = 1_000_000,
};
```

The three filters are independent and ANDed. The one trap: **a collection filter constrains commit events only** — identity, account, and sync events arrive regardless, on both protocols. That is deliberate (an account deletion has no collection, and dropping it would leave you indexing records of a deleted account), so handle those kinds rather than assuming a collection filter excluded them. A commits-only stream needs `WantedKinds = [JetstreamEventKind.Commit]` as well; on v1 there is no `kinds` filter, so filter client-side.

Setting `WantedCollections` while `WantedKinds` excludes `Commit` is a filter that could never apply, and throws `ArgumentException` before the socket opens rather than getting an HTTP 400 back.

## Managed Consumer (Reconnect + Cursor Persistence)

`JetstreamConsumer` mirrors `TypedFirehoseConsumer` ergonomics: automatic reconnection with backoff, cursor persistence through the same `IFirehoseCursorStore` interface, and duplicate suppression across reconnects.

```csharp
var consumer = new JetstreamConsumer(new JetstreamConsumerOptions
{
    ServiceUrl = JetstreamEndpoints.UsEast,
    Protocol = JetstreamProtocol.V2,
    WantedCollections = ["exchange.recipe.recipe"],
    CursorStore = myCursorStore,   // any IFirehoseCursorStore
    StreamId = "jetstream-main",
    MaxReconnectAttempts = -1,     // reconnect forever
});

await foreach (var evt in consumer.ConsumeAsync())
{
    await IndexAsync(evt);
}
```

Details worth knowing:

- **What the cursor is depends on the protocol.** On v2 it is the sequence number (`evt.Cursor`, tracked as `consumer.LastCursor`); on v1 it is `time_us` (`consumer.LastTimeUs`). Either way it is persisted to the `CursorStore` every `CursorPersistInterval` events (default 100) and once more on shutdown. The two are not interchangeable — give each protocol its own `StreamId` if they share a store, or the stored value will be read as the wrong kind of position.
- **Resume order**: an explicit `ConsumeAsync(cursor)` argument wins over the stored cursor; with neither, consumption starts live.
- **Reconnect**: a v2 cursor is replayed *inclusively* by the server, so the consumer reconnects at exactly the last sequence number it delivered and drops the one replayed event. A v1 cursor is a timestamp, so it rewinds by `ReconnectRewind` (default 5 s) to cover events lost in flight and filters out what it already delivered (`time_us <= LastTimeUs`). `ReconnectRewind` is ignored on v2.
- **At-least-once across restarts**: events after the last persisted cursor are redelivered when a new process resumes from the store — make processing idempotent (e.g. upsert keyed by the record's `at://` URI).
- **A rejected subscription is not retried.** v2 validates before the WebSocket upgrade: a cursor below the retention floor (36 h on the Bluesky instances), a retired zstd dictionary, or a malformed filter comes back as an HTTP 400. `ConsumeAsync` throws `JetstreamConnectException` rather than reconnecting, because retrying the same request would loop forever and dropping the cursor would silently skip the gap. Catch it and re-read the missing range from the repos you care about.

```csharp
try
{
    await foreach (var evt in consumer.ConsumeAsync(cancellationToken: stoppingToken))
        await IndexAsync(evt);
}
catch (JetstreamConnectException ex) when (!ex.IsRetryable)
{
    logger.LogError(ex, "Jetstream refused the subscription; backfilling from {Cursor}", lastDurableCursor);
    await BackfillAsync(lastDurableCursor, stoppingToken);
}
```

### Advisory and error frames (v2)

v2 carries two frames that are not events. Neither is delivered through `ConsumeAsync`; both are logged, and `OnInfo` / `OnStreamError` observe them:

```csharp
var options = new JetstreamConsumerOptions
{
    ServiceUrl = JetstreamEndpoints.UsEast,
    Protocol = JetstreamProtocol.V2,
    // Advisory: e.g. OutdatedCursor, when a timestamp cursor was clamped up to the floor.
    OnInfo = info => logger.LogInformation("Jetstream {Name}: {Message}", info.Name, info.Message),
    // Terminal: e.g. ConsumerTooSlow. The server closes the stream; the consumer reconnects.
    OnStreamError = error => metrics.StreamError(error.Error),
};
```

## Ingestion Loop Pattern

A typical indexer backing a custom appview:

```csharp
await foreach (var evt in consumer.ConsumeAsync(cancellationToken: stoppingToken))
{
    switch (evt)
    {
        case JetstreamCommitEvent { Operation: JetstreamOperation.Delete } del:
            await index.RemoveAsync(del.Uri);
            break;
        case JetstreamCommitEvent commit:
            await index.UpsertAsync(commit.Uri, commit.Cid, commit.Record);
            break;
        case JetstreamIdentityEvent identity:
            await index.UpdateHandleAsync(identity.Did, identity.Handle);
            break;
        case JetstreamAccountEvent { Active: false } account:
            await index.HideAccountAsync(account.Did);
            break;
        // v2 only: the repo's commit chain broke, so derived state may be wrong.
        case JetstreamSyncEvent sync:
            await index.ResyncAsync(sync.Did);
            break;
    }
}
```

Creates, updates, and deletes all arrive in one stream in cursor order, so the loop **folds** toward network truth rather than seeing a settled view: a record you will later delete may show up transiently before its delete arrives. That is expected — key writes on the record's `at://` URI and they stay idempotent.

## zstd Compression

Jetstream supports zstd-compressed frames (with a custom dictionary), cutting bandwidth roughly in half. The SDK does not bundle a zstd implementation — the core package stays dependency-free — but exposes the `IJetstreamDecompressor` seam. When `Decompressor` is set, the client asks for compressed frames and routes the binary ones through it.

Using [ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port):

```csharp
using ZstdSharp;

public sealed class ZstdJetstreamDecompressor : IJetstreamDecompressor, IDisposable
{
    private readonly Decompressor _decompressor = new();

    public ZstdJetstreamDecompressor(byte[] dictionary)
        => _decompressor.LoadDictionary(dictionary);

    public byte[] Decompress(ReadOnlySpan<byte> frame)
        => _decompressor.Unwrap(frame).ToArray();

    public void Dispose() => _decompressor.Dispose();
}
```

**On v1** the dictionary is unversioned — use the [one checked into the Jetstream repository](https://github.com/bluesky-social/jetstream/blob/main/pkg/models/zstd_dictionary) — and the client sends `compress=true`.

**On v2** the dictionary is versioned, and the subscription names the version it was built against. `JetstreamDictionaryClient` fetches it; the ID is read out of the dictionary's own header, so one call configures the subscription:

```csharp
using var dictionaries = new JetstreamDictionaryClient(JetstreamEndpoints.UsEast);
var dictionary = await dictionaries.GetDictionaryAsync();   // .Id and .Data

var options = new JetstreamConsumerOptions
{
    ServiceUrl = JetstreamEndpoints.UsEast,
    Protocol = JetstreamProtocol.V2,
    ZstdDictionaryId = dictionary.Id,
    Decompressor = new ZstdJetstreamDecompressor(dictionary.Data),
};
```

Setting only one of the two throws `ArgumentException`: a dictionary ID without a decompressor asks the server for binary frames nothing can read, and a decompressor without an ID has no version to name. If the server retires the dictionary, connecting fails with a non-retryable `JetstreamConnectException` — fetch again with no ID to pick up the current one.

For low-volume filtered streams the bandwidth saving is negligible — skip compression unless you tail high-volume collections like `app.bsky.*`.

## Historical Replay

v2 hosts also serve a **replay** API — an archive of the whole network, paged over authenticated HTTP (`planSnapshot`, `listSegments`, `getSegment`, `getBlock`) in a columnar `.jss` segment format, with a seamless cutover to the live tail at the tip. **The SDK does not implement it**; only the live tail described on this page is supported. Consumers needing a full historical backfill should use Bluesky's own [Go or TypeScript SDK](https://bsky.network/docs/jetstream-sdk) for the archive portion, or replay from the PDSes directly with `com.atproto.sync.getRepo`.

## Jetstream vs. Binary Firehose

| | Jetstream (`JetstreamConsumer`) | Firehose (`TypedFirehoseConsumer`) |
|---|---|---|
| Wire format | JSON (optionally zstd) | DAG-CBOR / CAR |
| Filtering | **Server-side** (collections, DIDs) | Client-side only |
| Bandwidth for niche collections | Near zero | Entire network stream |
| Cryptographic verification | ✗ | ✓ (CIDs + signatures) |
| Cursor | `Cursor` (seq) on v2, `TimeUs` on v1 | Sequence number |
| Source | Jetstream instance (trusted) | Relay / PDS |
