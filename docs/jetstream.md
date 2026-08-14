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

## Historical Replay (v2 Archive)

The live tail is only one of three ways to consume Jetstream v2. The v2 hosts also serve a **full-network archive** over HTTP, which the SDK consumes in two modes:

| Mode | Transport | Auth | Type |
|---|---|---|---|
| Live | WebSocket (`subscribeEvents`) | none | `JetstreamConsumer` |
| Replay | HTTP plan + download, then cut over to the live tail | API key on the HTTP calls | `JetstreamReplayConsumer` |
| Snapshot | HTTP only, no live tail | API key | `JetstreamReplayConsumer` with `SnapshotOnly = true` |

Replay is what an indexer actually needs: *the records that already exist* **and** *every new one*, with no gap at the seam.

```csharp
var consumer = new JetstreamReplayConsumer(new JetstreamConsumerOptions
{
    ServiceUrl = JetstreamEndpoints.UsEast,
    Protocol = JetstreamProtocol.V2,          // required: v1 has no archive
    WantedCollections = ["app.bsky.feed.post"],
    WantedKinds = [JetstreamEventKind.Commit],
    CursorStore = myCursorStore,              // same IFirehoseCursorStore, same v2 sequence number
    Archive = new JetstreamArchiveOptions
    {
        ApiKey = Environment.GetEnvironmentVariable("JETSTREAM_API_KEY"),
        BlockDecompressor = new ZstdBlockDecompressor(),   // see below
        DownloadParallelism = 4,
    },
});

await foreach (var evt in consumer.ReplayAsync(cancellationToken: stoppingToken))
{
    // History first, then the live tail — one loop, in sequence order.
    await IndexAsync(evt);
}
```

The filters are declared once and used by both phases. `consumer.IsBackfilling` says which phase you are in; `consumer.LastCursor` is the sequence number of the last event delivered.

### How it works

Replay is **stateless on the server** — no registered subscription, no per-consumer cursor:

1. **Plan.** `planSnapshot` is posted with the DID/collection/kind filter and the sequence window. The first response's `sealedTipSeq` is pinned as the ceiling `S` for the whole backfill; each page reports `plannedThroughSeq`, and while that is below `S` the consumer re-plans with `afterSeq = plannedThroughSeq`, `beforeSeq = S`, so the range never floats. Large plans truncate at a whole segment or block-range boundary and always admit at least one work unit, so paging always progresses. If a page ever *does* come back without advancing while `S` is still ahead — the tip reported before the segments carrying it became servable — the consumer waits (exponential from a second, capped by `MaxRetryDelay`) and re-plans, up to `MaxStalledPlanAttempts` times (default 5), then throws a `JetstreamArchiveException`. It does **not** stop below `S` and cut over: the cutover starts at `S`, so the skipped range would never be delivered by either phase.
2. **Download.** Each planned segment comes back as `mode: "segment"` (fetch the file with `getSegment`) or `mode: "blocks"` (fetch the listed ranges with `getBlock`). `DownloadParallelism` downloads run ahead of the decoder, which still delivers strictly in sequence order. Whole segments are spooled to a temporary file (`SpoolDirectory`, defaulting to the system temp directory) rather than buffered — they run to hundreds of megabytes.
3. **Cut over.** The live socket is connected *once* at `?cursor=S`. That cursor is inclusive, so events at or below the last one already delivered are dropped, and the server replays the window between the plan and the socket — nothing is lost at the handoff and there is no buffer to drain.

The planner works from bloom filters and per-block summaries: it has **no false negatives, but can return blocks with no matching rows**, so the exact filter is applied again to what was decoded.

`SnapshotOnly = true` stops when the archive is exhausted instead of cutting over. `BeforeSeq` bounds the snapshot above and requires `SnapshotOnly` — there is nothing to cut over into above a fixed ceiling. It caps the pinned ceiling too, so a snapshot stops at `BeforeSeq` even when `sealedTipSeq` runs far past it.

### Auth, metering, and resume

The HTTP endpoints are metered on the Bluesky-hosted instances (the live WebSocket stays unauthenticated and unmetered):

- `ApiKey` is sent as `Authorization: Bearer`. A missing, malformed, or revoked key is a `401` (`invalid bearer credential`), surfaced as a non-retryable `JetstreamArchiveException`.
- Metering is in **response bytes on the wire**, not requests. Over quota is `429` (`byte limit exceeded`) with a `Retry-After`; the quota refills continuously rather than resetting on a boundary, so the client waits out exactly what the server asked for, up to `MaxRetryAttempts` times.
- Running out mid-download closes the stream cleanly. `DownloadSegmentAsync` resumes with an HTTP `Range` request from the exact byte offset it stopped at — nothing already downloaded is re-fetched or re-charged.

If a backfill runs long enough that the pinned tip `S` ages out of the live socket's lookback window (36 hours on the Bluesky instances), the cutover connect is refused with a `JetstreamConnectException`. The consumer then re-enters the plan loop from the last sequence number it delivered rather than skipping the gap, up to `MaxCutoverAttempts` times (default 3) before rethrowing.

### Folding, not filtering

Replay delivers **every matching event at least once in sequence order**, including creates that a later delete supersedes. Consumers converge by folding, exactly as on the live tail:

- create adds, update replaces, delete removes — key writes on the record's `at://` URI so they stay idempotent;
- a `JetstreamAccountEvent` with `Active = false` (`Status = "deleted"`), or a `JetstreamSyncEvent` divergence marker, removes **all** of that account's records;
- account-level events carry no collection and are delivered even to a collection-filtered consumer.

The cursor persists through the same `IFirehoseCursorStore` the live tail uses, since a replay cursor *is* the v2 sequence number — a restart resumes the backfill where it left off.

### The zstd seam

Segment blocks are plain zstd frames with **no dictionary** — unlike the dictionary-compressed WebSocket frames, so `IJetstreamDecompressor` and `IJetstreamBlockDecompressor` are deliberately separate and a decompressor with the Jetstream dictionary loaded cannot read a block. Using [ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port):

```csharp
using ZstdSharp;

public sealed class ZstdBlockDecompressor : IJetstreamBlockDecompressor
{
    public byte[] Decompress(ReadOnlySpan<byte> frame)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(frame).ToArray();
    }
}
```

### Reading the archive directly

`JetstreamArchiveClient` wraps the four endpoints for mirrors and other tooling, and `JetstreamSegmentReader` decodes the `.jss` format on its own:

```csharp
using var archive = new JetstreamArchiveClient(JetstreamEndpoints.UsEast, apiKey);

await foreach (var segment in archive.ListAllSegmentsAsync())
{
    // name, index, sizeBytes, checksum, eventCount, minSeq/maxSeq, minWitnessedAt/maxWitnessedAt
    if (Mirror.HasCurrent(segment.Name, segment.Checksum))
        continue;

    await using var file = File.Create(segment.Name);
    await archive.DownloadSegmentAsync(segment.Name, file);
}

// Streaming decode: a segment is never held in memory whole.
await using var stored = File.OpenRead("seg_000000002a.jss");
await foreach (var row in JetstreamSegmentReader.ReadRowsAsync(stored, decompressor))
{
    // row.Payload is the untouched CBOR the network published — the archive keeps records as
    // CBOR precisely so a mirror stays byte-auditable. row.ToEvent() projects to the live model.
}
```

Two behaviours worth designing around:

- **Sealed segments are immutable only between compactions.** The server periodically rewrites them to physically drop deleted records, and a rewritten file gets a new checksum. A mirror re-lists and compares `Checksum` (which is also the `getSegment` ETag) rather than assuming a name never changes content.
- **`Checksum` is not recomputed locally.** It is an xxh3 metadata checksum, and the SDK ships no xxhash implementation any more than it ships zstd; block frames carry their own zstd content checksums, which the decompressor verifies. Compare the listed checksum against the ETag to detect a stale mirror.

A runnable end-to-end example — the ZstdSharp decompressor, snapshot mode, and the replay cutover — is in [`samples/JetstreamReplaySample`](../samples/JetstreamReplaySample).

## Jetstream vs. Binary Firehose

| | Jetstream (`JetstreamConsumer`) | Firehose (`TypedFirehoseConsumer`) |
|---|---|---|
| Wire format | JSON (optionally zstd) | DAG-CBOR / CAR |
| Filtering | **Server-side** (collections, DIDs) | Client-side only |
| Bandwidth for niche collections | Near zero | Entire network stream |
| Cryptographic verification | ✗ | ✓ (CIDs + signatures) |
| Cursor | `Cursor` (seq) on v2, `TimeUs` on v1 | Sequence number |
| Historical backfill | Full-network archive (`JetstreamReplayConsumer`, v2) | Relay retention window, then `com.atproto.sync.getRepo` per repo |
| Source | Jetstream instance (trusted) | Relay / PDS |
