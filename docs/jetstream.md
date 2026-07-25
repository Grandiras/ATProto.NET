# Jetstream Streaming

ATProto.NET supports [Jetstream](https://github.com/bluesky-social/jetstream), the JSON alternative to the binary [firehose](firehose.md). Jetstream's key advantage is **server-side filtering**: the server only sends events for the collections and DIDs you ask for, so tailing a niche collection costs almost no bandwidth — where the binary firehose always delivers the whole network's commit stream.

> **⚠ Not cryptographically verifiable.** Jetstream events are plain JSON without MST proofs or commit signatures. Unlike `TypedFirehoseConsumer` (which offers `VerifyCids`/`VerifySignatures`), a Jetstream consumer must trust the Jetstream instance. For canonical state, re-fetch records via `com.atproto.repo.getRecord`. Use the binary firehose when verification matters.

## Basic Usage

```csharp
using ATProtoNet.Streaming;

var client = new JetstreamClient(new JetstreamConsumerOptions
{
    ServiceUrl = "wss://jetstream2.us-east.bsky.network",
    WantedCollections = ["app.bsky.feed.post"],
});

await foreach (var evt in client.SubscribeAsync())
{
    if (evt is JetstreamCommitEvent commit)
        Console.WriteLine($"{commit.Operation} {commit.Uri}");
}
```

`JetstreamClient` manages a single connection with no reconnect logic. For production use, prefer `JetstreamConsumer` (below).

Public Bluesky-operated instances: `jetstream1.us-east.bsky.network`, `jetstream2.us-east.bsky.network`, `jetstream1.us-west.bsky.network`, `jetstream2.us-west.bsky.network`.

## Event Types

| Type | Meaning |
|---|---|
| `JetstreamCommitEvent` | Record create/update/delete. Exposes `Collection`, `RKey`, `Operation`, `Cid`, raw `Record` JSON, computed `Uri`, and typed `GetRecord<T>()`. |
| `JetstreamIdentityEvent` | Identity change (e.g. handle update). |
| `JetstreamAccountEvent` | Account status change (`Active`, `Status` like `"takendown"`). |

All events carry `Did` and `TimeUs` — the event timestamp in unix **microseconds**, which doubles as the subscription cursor.

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
    ServiceUrl = "wss://jetstream2.us-east.bsky.network",
    // Full NSIDs or prefix wildcards; max 100 entries.
    WantedCollections = ["exchange.recipe.recipe", "app.bsky.graph.*"],
    // Optional DID filter; max 10,000 entries.
    WantedDids = ["did:plc:q6gjnaw2blty4crticxkmujt"],
    // Optional: drop records larger than this server-side.
    MaxMessageSizeBytes = 1_000_000,
};
```

## Managed Consumer (Reconnect + Cursor Persistence)

`JetstreamConsumer` mirrors `TypedFirehoseConsumer` ergonomics: automatic reconnection with backoff, cursor persistence through the same `IFirehoseCursorStore` interface, and duplicate suppression across reconnects.

```csharp
var consumer = new JetstreamConsumer(new JetstreamConsumerOptions
{
    ServiceUrl = "wss://jetstream2.us-east.bsky.network",
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

- **The cursor is `time_us`** (unix microseconds), not a firehose sequence number. It is persisted to the `CursorStore` every `CursorPersistInterval` events (default 100) and once more on shutdown.
- **Resume order**: an explicit `ConsumeAsync(cursor)` argument wins over the stored cursor; with neither, consumption starts live.
- **Reconnect rewind**: on reconnect the consumer rewinds by `ReconnectRewind` (default 5 s) to cover events lost in flight, then filters out events it already delivered (`time_us <= LastTimeUs`).
- **At-least-once across restarts**: events after the last persisted cursor are redelivered when a new process resumes from the store — make processing idempotent (e.g. upsert keyed by the record's `at://` URI).

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
    }
}
```

## zstd Compression

Jetstream supports zstd-compressed frames (with a custom dictionary), cutting bandwidth roughly in half. The SDK does not bundle a zstd implementation — the core package stays dependency-free — but exposes the `IJetstreamDecompressor` seam. When `Decompressor` is set, the client requests `compress=true` and routes binary frames through it.

Using [ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port) and the [official dictionary](https://github.com/bluesky-social/jetstream/blob/main/pkg/models/zstd_dictionary):

```csharp
using ZstdSharp;

public sealed class ZstdJetstreamDecompressor : IJetstreamDecompressor, IDisposable
{
    private readonly Decompressor _decompressor = new();

    public ZstdJetstreamDecompressor(string dictionaryPath)
        => _decompressor.LoadDictionary(File.ReadAllBytes(dictionaryPath));

    public byte[] Decompress(ReadOnlySpan<byte> frame)
        => _decompressor.Unwrap(frame).ToArray();

    public void Dispose() => _decompressor.Dispose();
}
```

For low-volume filtered streams the bandwidth saving is negligible — skip compression unless you tail high-volume collections like `app.bsky.*`.

## Jetstream vs. Binary Firehose

| | Jetstream (`JetstreamConsumer`) | Firehose (`TypedFirehoseConsumer`) |
|---|---|---|
| Wire format | JSON (optionally zstd) | DAG-CBOR / CAR |
| Filtering | **Server-side** (collections, DIDs) | Client-side only |
| Bandwidth for niche collections | Near zero | Entire network stream |
| Cryptographic verification | ✗ | ✓ (CIDs + signatures) |
| Cursor | `time_us` (unix µs) | Sequence number |
| Source | Jetstream instance (trusted) | Relay / PDS |
