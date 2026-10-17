# Event streams

| | |
|---|---|
| **Status** | Draft |
| **Packages** | `ATProtoNet.Streaming` (namespaces `ATProtoNet.Streaming`, `.Firehose`, `.Labels`, `.Jetstream`, `.Tap`, `.Plc`, `.Advanced`). The chat moderation stream lives in `ATProtoNet.Bluesky`, the `StreamWorker<TItem>` hosted-service adapter in `ATProtoNet.Hosting` ([hosting](hosting.md)), the Tap webhook receiver in `ATProtoNet.AspNetCore` ([server](server.md)). |
| **Phase** | 7 |
| **Depends on** | [data-model](data-model.md) (`DrislReader`, `CarReader`, `Cid`), [repo](repo.md) (`SignedCommit`, `MerkleSearchTree` inversion, `RepoSnapshot`), [identity](identity.md) (`IIdentityResolver`, `UntrustedFetchPolicy`), [xrpc-client](xrpc-client.md) (`XrpcInvoker`, `IXrpcHandler`), [labels](labels.md) (`LabelVerifier`, `LabelFrameCodec`), [stores](stores.md) (durable `ICursorStore`, `IRepoSyncStateStore`) |
| **Normative references** | [Event Stream](https://atproto.com/specs/event-stream) · [Sync](https://atproto.com/specs/sync) (firehose, Sync 1.1, validation checklist, record-level synchronization) · [Repository](https://atproto.com/specs/repository) · `com.atproto.sync.subscribeRepos`, `com.atproto.label.subscribeLabels`, `chat.bsky.moderation.subscribeModEvents` Lexicons · [Jetstream](https://github.com/bluesky-social/jetstream) v1 and the v2 `network.bsky.jetstream.*` Lexicons · [Tap](https://github.com/bluesky-social/indigo/tree/main/cmd/tap) and `@atproto/tap` · PLC directory `/export` and `/export/stream` |

## Scope

**In:** one stream runtime (`StreamLoop`) shared by every stream: transport, frame codec,
reconnect and backoff, cursor resume and persistence, in-band notices, one exception type. On
top of it: `FirehoseStream` (with the optional Sync 1.1 verification and resync stages),
`LabelStream`, `JetstreamStream` (v1 and v2 codecs), `JetstreamArchive` (archive client and
segment reader), the generic backfill-then-live cutover combinator (`StreamCutover`) and the
Jetstream replay built on it, `TapStream` and `TapClient` (admin API), `PlcExportStream`, and
the chat moderation stream (in `ATProtoNet.Bluesky`, on the same runtime). The
`ICursorStore` and `IRepoSyncStateStore` interfaces with their in-memory implementations.

**Out:** serving subscriptions (`subscribeLabels`, user subscriptions) ([server](server.md));
durable store implementations ([stores](stores.md)); label signing and verification rules
([labels](labels.md)); MST, commit and snapshot verification ([repo](repo.md), which this spec
*uses*); Spaces sync, which is pull-based ([spaces](spaces.md)); hosted-service adapters
([hosting](hosting.md)).

## Public API sketch

```csharp
namespace ATProtoNet.Streaming;

// ── Items: protocol events and in-band notices (D-23) ──────────────────────
public interface IStreamItem;                                   // marker for everything a stream yields

public abstract record StreamNotice : IStreamItem;             // unsequenced, never moves the cursor
public sealed record StreamInfo(string Name, string? Message) : StreamNotice,
    IFirehoseItem, ILabelStreamItem, IJetstreamItem, IPlcExportItem
{
    public const string OutdatedCursor = "OutdatedCursor";
    public bool IsOutdatedCursor => Name == OutdatedCursor;
}
public sealed record EventDropped(DropReason Reason, string? Cursor, string? Detail) : StreamNotice,
    IFirehoseItem, ILabelStreamItem, IJetstreamItem, ITapItem, IPlcExportItem;   // opt-in
public sealed record RepoDesynchronized(Did Did, Tid? Rev, string Reason) : StreamNotice, IFirehoseItem;
public sealed record RepoResynced(Did Did, RepoSnapshot Snapshot) : StreamNotice, IFirehoseItem;

// Open set (AG-9): Malformed, UnknownType, Stale, VerificationFailed, Unreadable, ResyncOverflow
public readonly record struct DropReason(string Value);

// ── Common options ─────────────────────────────────────────────────────────
public abstract record StreamOptions
{
    public string? StreamId { get; init; }                      // cursor-store key; defaults to the service URL
    public ICursorStore? CursorStore { get; init; }
    public StreamReconnectPolicy Reconnect { get; init; } = new();
    public int CursorPersistInterval { get; init; } = 100;      // events between background saves
    public bool IncludeDroppedEvents { get; init; }             // yield EventDropped notices
    public StreamTransportOptions Transport { get; init; } = new();
}
public sealed record StreamReconnectPolicy
{
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);
    public int? MaxAttempts { get; init; } = 10;                // null: forever; 0: never reconnect
}
public sealed record StreamTransportOptions(HttpMessageInvoker? Invoker = null,   // public seam (0.8's was internal)
    IReadOnlyDictionary<string, string>? Headers = null, string? UserAgent = null);

// Where to start. null on ReadAsync means "stored cursor, else live".
public abstract record StreamStart { public static StreamStart Live { get; } public static StreamStart Stored { get; }
    public static StreamStart At(string cursor); }              // cursor is opaque, stream-specific

public sealed class StreamException : AtProtoException
{
    public string StreamName { get; }
    public string? Error { get; }                               // error-frame name or close reason
    public int? StatusCode { get; }                             // refused upgrade / HTTP failure
    public TimeSpan? RetryAfter { get; }
    public bool IsRetryable { get; }                            // STR rules on classification
}

// ── Firehose ───────────────────────────────────────────────────────────────
namespace ATProtoNet.Streaming.Firehose;

public interface IFirehoseItem : IStreamItem;
public abstract record FirehoseEvent(long Seq, DateTimeOffset Time) : IFirehoseItem;
public sealed record CommitEvent : FirehoseEvent { /* repo, commit, rev, since, blocks, ops, prevData, … */
    public IEnumerable<FirehoseRecordEvent> GetRecordEvents(); }
public sealed record SyncEvent : FirehoseEvent;
public sealed record IdentityEvent : FirehoseEvent;
public sealed record AccountEvent : FirehoseEvent;
public readonly record struct RepoOpAction(string Value);       // knownValues: create, update, delete

public sealed record FirehoseOptions : StreamOptions
{
    public required Uri ServiceUrl { get; init; }               // wss://relay…, or a PDS
    public IReadOnlySet<Nsid>? Collections { get; init; }
    public RepoSyncVerifier? Verifier { get; init; }             // Sync 1.1 stage
    public RepoResync? Resync { get; init; }                     // requires Verifier
}
public sealed class FirehoseStream(FirehoseOptions options, ILoggerFactory? loggerFactory = null)
{
    public IAsyncEnumerable<IFirehoseItem> ReadAsync(StreamStart? start = null, CancellationToken cancellationToken = default);
}

public sealed class RepoSyncVerifier(IIdentityResolver identity, IRepoSyncStateStore state,
    RepoSyncVerifierOptions? options = null, TimeProvider? time = null);
public sealed class RepoResync(RepoResyncOptions? options = null, HttpClient? httpClient = null);

// Shared record-level view (firehose, Jetstream, Tap): Did, Collection, Rkey, Action, Cid?, Rev?, Live,
// and T? As<T>() decoding CBOR or JSON through the one generated mapping (D-10).
public interface IRecordEvent;

// ── Labels, Jetstream, Tap, PLC ────────────────────────────────────────────
public sealed class LabelStream(LabelStreamOptions options, ILoggerFactory? loggerFactory = null);      // IAsyncEnumerable<ILabelStreamItem>
public sealed class JetstreamStream(JetstreamOptions options, ILoggerFactory? loggerFactory = null);    // IAsyncEnumerable<IJetstreamItem>
public sealed class JetstreamArchive(JetstreamArchiveOptions options, HttpClient? httpClient = null);  // plan, list, download, read segments
public static class JetstreamReplay                                                                    // backfill then live
{
    public static IAsyncEnumerable<IJetstreamItem> ReadAsync(JetstreamArchive archive, JetstreamOptions live,
        JetstreamReplayOptions? options = null, StreamStart? start = null, CancellationToken cancellationToken = default);
}
public sealed class TapStream(TapStreamOptions options, ILoggerFactory? loggerFactory = null)           // IAsyncEnumerable<ITapItem>
{
    public ValueTask AckAsync(TapEvent evt, CancellationToken cancellationToken = default);            // TapAckMode.Manual only
}
public sealed class TapClient(Uri tapUrl, string? adminPassword, HttpClient? httpClient = null);       // admin endpoints
public sealed class PlcExportStream(PlcExportOptions options, ILoggerFactory? loggerFactory = null);   // IAsyncEnumerable<IPlcExportItem>

public interface IZstdDecompressor                                // one seam for live frames and archive blocks
{
    int Decompress(ReadOnlySpan<byte> source, Span<byte> destination, ReadOnlySpan<byte> dictionary);
    int GetDecompressedSize(ReadOnlySpan<byte> source);           // frame content size, or -1 if absent
}

// ── Stores (durable implementations: stores.md) ───────────────────────────
public interface ICursorStore
{
    ValueTask<string?> GetAsync(string streamId, CancellationToken cancellationToken = default);
    ValueTask SetAsync(string streamId, string cursor, CancellationToken cancellationToken = default);
}
public interface IRepoSyncStateStore
{
    ValueTask<RepoSyncState?> GetAsync(Did did, CancellationToken cancellationToken = default);
    ValueTask SetAsync(RepoSyncState state, CancellationToken cancellationToken = default);
    IAsyncEnumerable<RepoSyncState> ListUnsynchronizedAsync(CancellationToken cancellationToken = default);
}
public sealed record RepoSyncState(Did Did, Tid Rev, Cid Data, RepoSyncStatus Status);
public enum RepoSyncStatus { Synchronized, Desynchronized, Resynchronizing }   // closed: the SDK's own state machine

// ── The runtime, for streams defined in other packages ─────────────────────
namespace ATProtoNet.Streaming.Advanced;

public interface IStreamCodec<TItem> where TItem : class
{
    ValueTask<Uri> GetEndpointAsync(string? resumeCursor, CancellationToken cancellationToken);   // may fetch credentials
    ValueTask<StreamFrameResult<TItem>> DecodeAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken);
    ICursorCodec Cursor { get; }                                // inclusive-replay, dedupe and reconnect rules
    TItem WrapNotice(StreamNotice notice);                      // for item types outside this package
}
public sealed class StreamLoop<TItem>(IStreamCodec<TItem> codec, StreamOptions options, ILogger logger, TimeProvider? time = null)
    where TItem : class
{
    public IAsyncEnumerable<TItem> RunAsync(StreamStart? start, CancellationToken cancellationToken);
}
public sealed class StreamCutover<TItem> where TItem : class;   // backfill-then-live combinator (STR rules below)
```

Usage:

```csharp
var firehose = new FirehoseStream(new FirehoseOptions
{
    ServiceUrl = new Uri("wss://bsky.network"),
    CursorStore = cursors,
    Collections = new HashSet<Nsid> { Nsid.Parse("app.bsky.feed.post") },
    Verifier = new RepoSyncVerifier(identity, syncState),
    Resync = new RepoResync(),
});

await foreach (var item in firehose.ReadAsync(cancellationToken: ct))
{
    switch (item)
    {
        case CommitEvent commit:
            foreach (var record in commit.GetRecordEvents())
                await IndexAsync(record.Did, record.Collection, record.Rkey, record.As<Post>(), ct);
            break;
        case RepoResynced resynced:
            await ReindexAsync(resynced.Snapshot, ct);
            break;
        case StreamInfo { IsOutdatedCursor: true }:
            logger.LogWarning("Cursor predates the relay's window; a gap exists.");
            break;
    }
}
```

## Requirements

### Stream runtime

- **STR-1** (MUST) Every stream in this package, and the chat moderation stream in
  `ATProtoNet.Bluesky`, runs on `StreamLoop<TItem>`. No stream implements its own connect,
  reconnect, backoff or cursor loop. A stream contributes only a codec (endpoint, frame decoding,
  cursor rules) and optional stages.
  *Source:* **Decision** [D-07](../06-decisions.md#d-07-one-consumption-model-for-every-event-stream) ·
  0.8 has `EventStreamLoop` plus separate loops in `JetstreamReplayConsumer`, `TapChannel` and
  `PlcClient.StreamExportAsync`
- **STR-2** (MUST) Every stream is consumed through a method returning
  `IAsyncEnumerable<TItem>` named `ReadAsync(StreamStart? start = null, CancellationToken
  cancellationToken = default)`. No stream exposes .NET events, callbacks or a public
  `Channel<T>`. Side information is yielded in-band (STR-26).
  *Source:* **Decision** D-07, D-23, [AG-18](../03-api-guidelines.md#pagination-and-streams)
- **STR-3** (MUST) A stream object holds configuration only. All per-enumeration state
  (position, connection, backoff, verifier pending state) lives in the enumeration, so one stream
  object can be enumerated more than once, including concurrently. Concurrent enumerations
  sharing one `StreamId` and cursor store are the caller's responsibility.
  *Source:* audit: 0.8 Jetstream keeps `LastTimeUs`/`LastCursor` on the consumer instance
- **STR-4** (MUST) Constructing a stream does no I/O and starts no background work. The
  connection opens when the first item is requested and closes when the enumeration ends.
  *Source:* [AG-27](../03-api-guidelines.md#resources-and-lifetimes)
- **STR-5** (MUST) The WebSocket transport is public and configurable through
  `StreamTransportOptions`: an `HttpMessageInvoker` (so `IHttpClientFactory` handlers, proxies
  and test servers apply), extra request headers and the User-Agent. The SDK's User-Agent is sent
  when none is given.
  *Source:* audit (0.8 connector seam is internal) · 0.8 `StreamSocket.cs`
- **STR-6** (SHOULD) A configured `ServiceUrl` is trusted as given (`ws://` to a
  development host included). A URL the SDK derives at runtime (a PDS endpoint from a DID
  document, the relay fetch URL for resync) MUST be `wss://`/`https://` or loopback.
  *Source:* event-stream spec, *Security*: only `wss://` over the internet · mirrors
  [xrpc-client](xrpc-client.md) service-URL rules
- **STR-7** (MUST) `StreamLoop<TItem>`, `IStreamCodec<TItem>` and
  `ICursorCodec` are public (`ATProtoNet.Streaming.Advanced`), so streams in other packages use
  the runtime without `InternalsVisibleTo`. A codec for an item type outside this package maps
  notices into its item type through `WrapNotice`.
  *Source:* **Decision** architecture rule 3 (no `InternalsVisibleTo` between production assemblies)

### Event-stream framing (DRISL-CBOR streams)

Applies to the firehose, label stream and chat moderation stream.

- **STR-8** (MUST) A WebSocket binary message is one frame: a DRISL-CBOR header map
  `{op, t}` followed by a DRISL-CBOR body, both decoded with the strict `DrislReader` and the
  limits in [Limits](#limits). Header and envelope bodies are bound **directly from CBOR** by
  hand-written binders, not transcoded through JSON.
  *Source:* event-stream spec, *Framing* · **Decision**
  [D-10](../06-decisions.md#d-10-serialization-systemtextjson-with-source-generation-generated-union-converters) ·
  0.8 `EventStreamFrame.cs`, `CborEventStreamHandler`
- **STR-9** (MUST) `op = 1` is a message whose `t` names its type relative to the
  subscription (`#commit`, `#sync`, `#identity`, `#account`, `#info` on the firehose; `#labels`,
  `#info` on the label stream). A codec may prefix `t` with the subscription NSID to form a
  Lexicon `$type` (the chat moderation stream does).
  *Source:* event-stream spec · 0.8 `ChatModerationEventConsumer.cs:121-124`
- **STR-10** (MUST) `op = -1` is an error frame whose body is `{error, message?}`. A
  missing `error` reads as `"Unknown"`. An error frame ends the connection: the loop logs it and
  reconnects if the error is retryable (STR-31), else throws `StreamException` with
  `Error` set.
  *Source:* event-stream spec, *Error Handling* · 0.8 `EventStreamLoop.cs:143-149`
- **STR-11** (MUST) Frames that cannot be delivered are dropped, never thrown:
  - an unreadable header: `Malformed`, cursor unchanged (there is no sequence to read);
  - any other `op`, or an empty `t`: `Malformed`; the cursor still advances to the body's
    top-level `seq` if one can be read;
  - an unknown `t`: `UnknownType`, cursor advances;
  - a known type whose body does not bind (missing field, invalid identifier, depth or size
    limit exceeded): `Malformed`, cursor advances.
  *Source:* 0.8 `TypedFirehoseConsumer.cs:224-244`, `EventStreamFrame.ReadSeq` · tests
  `FirehoseEventParserTests.Parse_DeeplyNestedBody_ReturnsNullRatherThanOverflowingTheStack`,
  `TypedFirehoseConsumerTests.ConsumeAsync_UnparseableIdentifier_IsReportedAndStillAdvancesTheCursor`
- **STR-12** (MUST) A WebSocket message larger than the stream's message limit fails the
  connection (it is not truncated). Empty messages are skipped. An abnormal close carrying a
  reason throws or reconnects as `StreamException` with `Error` = the reason. A refused upgrade
  produces `StreamException` with `StatusCode` set.
  *Source:* 0.8 `StreamSocket.cs:53,122-127` · tests `StreamSocketTests`
- **STR-13** (MUST) Envelope binders ignore unknown fields and accept the
  deprecated `subscribeRepos` fields (`rebase`, `tooBig`, `blobs`) without requiring them. A
  `#commit` op's `action` is an open string (`RepoOpAction`), not a closed enum; an unknown action
  makes the op, and so the commit, `Malformed`.
  *Source:* Sync spec, *Future Work* (fields may be removed) · audit (0.8 `RepoOpAction` is a
  closed C# enum, `SyncModels.cs:295`)

### Delivery and cursor

- **STR-14** (MUST) Delivery is at least once. The position of item *n* is recorded
  only when the consumer requests item *n + 1*. When the enumeration ends for any reason
  (`break`, cancellation, an exception in the consumer's loop body, an exception from the
  stream), the recorded position is that of the last item whose successor was requested; the
  item being handled when the enumeration ended is not recorded.
  *Source:* event-stream spec, *Sequence Numbers* · 0.8 `EventStreamLoop.cs:158-159` · test
  `EventStreamLoopTests.ConsumeAsync_HoweverItEnds_SavesTheCursorOfTheLastHandledEvent`
- **STR-15** (MUST) The start position is, in order: the explicit `StreamStart.At(cursor)`;
  else the cursor stored under `StreamId` (`StreamStart.Stored`, the default); else live.
  `StreamStart.Live` ignores the store for the start but still records positions.
  *Source:* tests `…ResumesFromTheStoredCursor_AndAnExplicitCursorWins` (firehose, Jetstream,
  label stream)
- **STR-16** (MUST) `StreamId` defaults to the service URL's canonical string. Two
  streams sharing a store but reading different services or filters SHOULD set distinct IDs.
  *Source:* 0.8 `StreamConsumerOptions`
- **STR-17** (MUST) After a reconnect the stream resumes from the last position
  it *passed* (delivered or dropped), not the last persisted one.
  *Source:* 0.8 `CursorTracker`
- **STR-18** (MUST) Every sequenced event advances the position: delivered, filtered
  out, dropped as malformed or unknown, stale, or failed verification. Only notices and
  unsequenced messages leave it unchanged.
  *Source:* #126 · tests `TypedFirehoseConsumerTests.ConsumeAsync_CollectionFilter_SkippedCommitsStillAdvanceTheStoredCursor`,
  `…UnparseableIdentifier_IsReportedAndStillAdvancesTheCursor`
- **STR-19** (MUST) After a reconnect, events at or before the current position are not
  delivered again (servers replay inclusively or rewind). Each cursor codec defines "at or
  before" for its cursor kind.
  *Source:* 0.8 `TypedFirehoseConsumer.cs:246`, `LabelStreamConsumer.cs:132`
- **STR-20** (MUST) Positions are saved in the background every
  `CursorPersistInterval` advances (default 100): at most one save in flight, later values
  coalesced into the newest, saves never block delivery, the stored value never moves backwards,
  and nothing is written when the position has not changed since the last save.
  *Source:* 0.8 `CursorTracker.cs` · tests `StreamCursorStoreTests.Advance_SlowStore_DoesNotBlockAndSavesTheNewestAfterward`
- **STR-21** (MUST) The recorded position is flushed when the enumeration ends,
  however it ends, with a non-cancelled token and before the enumeration completes or rethrows.
  *Source:* test `EventStreamLoopTests.ConsumeAsync_HoweverItEnds_SavesTheCursorOfTheLastHandledEvent`
- **STR-22** (MUST) A cursor store failure is logged and counted, never thrown into
  the enumeration. The next save retries with the newest value.
  *Source:* test `StreamCursorStoreTests.FlushAsync_FailingStore_DoesNotThrow`
- **STR-23** (MUST) A stage that holds events back (resync) caps the persisted
  position at one before the lowest held sequence, so a restart replays the held events.
  *Source:* 0.8 `RepoResync.cs:238-248` · test
  `TypedFirehoseConsumerSyncTests.ConsumeAsync_StopBetweenSnapshotAndHeldEvents_ReplaysTheHeldEvents`
- **STR-24** (MUST) Cancelling the token ends the enumeration normally: no
  `OperationCanceledException` escapes `ReadAsync`, and the position is flushed per
  STR-21.
  *Source:* 0.8 behaviour (`EventStreamLoop.cs:123-125,168-169`) · test
  `EventStreamLoopTests.ConsumeAsync_HoweverItEnds_SavesTheCursorOfTheLastHandledEvent` (`cancel` row)
- **STR-25** (MUST) `ICursorStore` stores opaque strings. Each codec encodes its
  position (firehose, label stream and Jetstream v2 `seq`; Jetstream v1 `time_us`; chat
  moderation `rev`; PLC export `seq`) and rejects a stored value it cannot parse by starting live
  and logging a warning. Tap has no cursor: Tap tracks acknowledgements server-side.
  *Source:* **Decision** (0.8 stores `long` only, so chat moderation has no store) ·
  [stores](stores.md)

### Notices

- **STR-26** (MUST) Side information is yielded as `StreamNotice` items in the
  same enumeration: `StreamInfo` (protocol `#info` messages, including `OutdatedCursor`),
  `RepoDesynchronized`, `RepoResynced` and, when `IncludeDroppedEvents` is set, `EventDropped`.
  There are no callbacks for any of these.
  *Source:* **Decision** [D-23](../06-decisions.md#d-23-stream-side-information-is-delivered-in-band) ·
  0.8 `OnStreamError`, `OnEventDropped`, `OnInfo`, `OnDesynchronized`
- **STR-27** (MUST) Notices never move the position and are never persisted.
  Requesting the item after a notice records the position of the last sequenced item, as usual.
  *Source:* **Decision** D-23
- **STR-28** (MUST) An `OutdatedCursor` signal is a `StreamInfo` notice on every
  stream that can continue after it (firehose, label stream, Jetstream). The stream keeps going
  from where the server put it; the gap is the caller's to handle. The SDK never silently resets
  a cursor.
  *Source:* event-stream spec, *Sequence Numbers* · audit (0.8 treats it three different ways)
- **STR-29** (MUST) Dropped events are counted by stream and reason in the
  `ATProtoNet.Streaming` meter and logged at `Debug` (malformed and unknown at `Warning`, rate
  limited) whether or not `EventDropped` notices are requested.
  *Source:* **Decision** D-23

### Errors and reconnect

- **STR-30** (MUST) Every failure that ends a stream, in every stream client of this
  spec (firehose, labels, Jetstream live and archive, Tap, PLC export, chat moderation), is a
  `StreamException`. There is no `JetstreamException`, `TapException` or `RepoFetchException`.
  Cancellation stays `OperationCanceledException` where it escapes at all.
  *Source:* **Decision** D-23 · audit (0.8 has `EventStreamException`, `JetstreamException`,
  `TapException`, `RepoFetchException`)
- **STR-31** (MUST) `IsRetryable` is `false` when the error name is `FutureCursor`, or
  the HTTP status is 4xx other than 408 and 429; otherwise `true` (network failures, 5xx, closes
  without a status, `ConsumerTooSlow`, other error frames). A non-retryable failure is thrown at
  once; a retryable one reconnects per policy.
  *Source:* 0.8 `EventStreamException.cs:53-55`, `EventStreamLoop.cs:171`
- **STR-32** (MUST) Reconnect delays start at `InitialDelay` (5 s) and double per attempt
  up to `MaxDelay` (30 s), plus 0–10% random jitter, waiting on the injected `TimeProvider`. The
  attempt count resets when any frame arrives.
  *Source:* 0.8 `StreamReconnectPolicy.cs:18-28`, `EventStreamLoop.cs:153`
- **STR-33** (MUST) `MaxAttempts` defaults to 10; `null` reconnects forever; `0` never
  reconnects, and then even a clean end of stream throws `StreamException`. Exhausting the
  attempts throws `StreamException` whose `InnerException` is the last failure.
  *Source:* 0.8 `StreamReconnectPolicy.cs:31-48` · test
  `EventStreamLoopTests.ConsumeAsync_AttemptsExhausted_ThrowsWithTheLastFailureRatherThanEndingLikeTheStream`
- **STR-34** (MUST) Before a stream throws, the position is flushed per
  STR-21.
  *Source:* test `JetstreamV2ConsumerTests.ConsumeAsync_RejectedSubscription_PersistsProgressBeforeThrowing`

### Firehose

- **STR-35** (MUST) `FirehoseStream` subscribes to
  `{ServiceUrl}/xrpc/com.atproto.sync.subscribeRepos?cursor={seq}`. `#commit`, `#sync`,
  `#identity` and `#account` are sequenced `FirehoseEvent`s; `#info` is a `StreamInfo` notice.
  *Source:* `com.atproto.sync.subscribeRepos` Lexicon · Sync spec
- **STR-36** (MUST) With `Collections` set, a `#commit` is delivered if any of its ops'
  collections is in the set. A commit with no ops, and every non-commit event, always passes.
  Without a verifier, the filter scans op paths from the raw CBOR before binding the full event.
  *Source:* 0.8 `TypedFirehoseConsumer.cs:224-233`, `CollectionMatcher.TryScan`
- **STR-37** (MUST) With a verifier, every `#commit` is verified whether or not
  the filter selects it, and a valid commit the filter excludes still advances its repository's
  chain (its state is recorded immediately).
  *Source:* 0.8 `TypedFirehoseConsumer.cs:331-337` · test
  `TypedFirehoseConsumerSyncTests.ConsumeAsync_CollectionFilter_StillChainsTheCommitsItSkips`
- **STR-38** (MUST) `CommitEvent.GetRecordEvents()` yields one `FirehoseRecordEvent`
  (`IRecordEvent`) per op, with the record block taken from the commit's CAR. `As<T>()` decodes
  the block through the data-model bridge into the generated or user type and returns `null` for
  deletes; a block missing from the CAR or of another `$type` throws `FormatException`.
  *Source:* 0.8 `RecordEvents.cs` · tests `RecordEventTests`
- **STR-39** (MUST) With a verifier, an `#identity` event invalidates the account's
  cached DID document before the event is delivered, so its next commit is checked against
  fresh keys.
  *Source:* 0.8 `TypedFirehoseConsumer.cs:304-309`
- **STR-40** (MUST) The new sync state of a delivered valid `#commit` or `#sync`
  is recorded when the consumer requests the next item (with a non-cancelled token), not before
  delivery. A consumer that fails while handling it gets the event again after restart.
  *Source:* 0.8 `TypedFirehoseConsumer.cs:258-268` · test
  `TypedFirehoseConsumerSyncTests.ConsumeAsync_RecordsTheStateOnlyOnceTheNextMessageIsAskedFor`

### Sync 1.1 verification (`RepoSyncVerifier`)

The verifier implements the Sync spec's inductive firehose: each `#commit` carries the MST nodes
needed to invert its ops and land on `prevData`, the root of the previous commit. It is a stage
of `FirehoseStream`, also usable on its own.

- **STR-41** (MUST) A `#commit` is checked in this order, and the first failure decides
  the outcome:
  1. at most `MaxCommitOps` ops;
  2. `blocks` present and at most `MaxCommitBlocksBytes`;
  3. `blocks` is a well-formed CAR (`CarReader`, strict) and every block matches its CID;
  4. the first CAR root is the commit block, equals the event's `commit` CID, and the block reads
     as a version-3 `SignedCommit`;
  5. the commit block's `did` and `rev` equal the event's `repo` and `rev`;
  6. `rev`'s timestamp is not more than `MaxClockSkew` ahead of now;
  7. **stale** if `rev` is not after the stored revision (checked before the signature, so
     replays cost no key lookup);
  8. the signature verifies against the account's `#atproto` key, with one DID-document refetch
     on failure (STR-47);
  9. op checks: each path is a valid `collection/rkey` MST key, no path appears twice; `create`
     has `cid` and no `prev`; `update` has `cid`, has `prev` when the commit is inductive, and
     `prev ≠ cid`; `delete` has no `cid` and has `prev` when inductive; every created or updated
     record block is in the CAR, is a DRISL-CBOR map and is at most `MaxRecordBytes`;
  10. ops are normalised: deletes first, then ordinal by path.
  *Source:* Sync spec, *Message Validation Checklist* · 0.8 `RepoSyncVerifier.cs:107-158`
- **STR-42** (MUST) When the commit carries `prevData`, the ops are inverted on a
  partial tree loaded from the commit's blocks (create: removing the key must return its `cid`;
  update: inserting `prev` must return the old `cid`; delete: inserting `prev` must find no
  entry), and the resulting root must equal `prevData`. A proof node missing from the blocks
  makes the commit `Invalid`.
  *Source:* Sync spec · [repo](repo.md) REP inversion requirements · tests
  `RepoSyncVerifierTests.VerifyCommitAsync_LiveCommitMissingAnOperation_FailsInversion`,
  `…LiveCommitWithAForgedPrevData_FailsInversion`, `…ManyRecords_ChainsOverTheCoveringProof`
- **STR-43** (MUST) Without `prevData`, the new tree (from the blocks) must show
  each op's outcome: created and updated keys map to their `cid`, deleted keys are absent.
  *Source:* 0.8 `RepoSyncVerifier.cs` · Tampered rows "operation not in the tree", "proof missing"
- **STR-44** (MUST) Chain rules against the stored `RepoSyncState`:
  - no stored state: the commit starts the chain (`Valid`), with or without `prevData`;
  - stored status is not `Synchronized`: `Desynchronized`;
  - `prevData` differs from the stored `Data`: `Desynchronized`;
  - a chained repository receives a commit without `prevData`: `Desynchronized`;
  - `since` is not compared (a `since` mismatch with matching `prevData` is `Valid`).
  *Source:* Sync spec · tests `RepoSyncVerifierTests.VerifyCommitAsync_UnknownRepository_StartsItsChain`,
  `…FirstCommitWithoutPrevData_StartsTheChain`, `…MissedCommit_DesynchronizesOnceAndHoldsTheRepository`,
  `…CommitWithoutPrevDataAfterAChain_Desynchronizes`, `…SinceMismatchButPrevDataMatches_IsValid`,
  `…PrevDataStrippedAndAnOperationDropped_Desynchronizes`
- **STR-45** (MUST) A `#sync` event gets checks 3–8 without ops. Its commit's `data`
  equal to the stored `Data` is `Valid` (only the revision moves); a different `data` is
  `Desynchronized`. A signed commit whose `data` is not a CID is `Invalid`, not an exception.
  *Source:* Sync spec (`#sync`) · tests `VerifySyncAsync_SameTree_MovesTheRevisionOn`,
  `VerifySyncAsync_OtherTree_Desynchronizes`, `VerifySyncAsync_ForgedSignature_Invalid`,
  `VerifySyncAsync_SignedCommitWhoseDataIsNotACid_IsInvalidRatherThanThrowing` (#180)
- **STR-46** (MUST) Outcomes and their effect on the stream:
  `Valid` → delivered, state recorded per STR-40; `Stale` → dropped (`Stale`);
  `Invalid` → dropped (`VerificationFailed`), state unchanged; `Desynchronized` → dropped, the
  state `{rev, data, Desynchronized}` is written immediately so older events become stale, and a
  `RepoDesynchronized` notice is yielded once per desynchronisation.
  *Source:* 0.8 `RepoSyncOutcome` · **Decision** D-23 replaces `OnDesynchronized`
- **STR-47** (MUST) The verification key is the `#atproto` verification method of the
  account's DID document, resolved through `IIdentityResolver`. On a signature failure, or when
  the document publishes no usable key, the document is refetched once and verification retried
  only if the key changed. An unresolvable account is `Invalid`.
  *Source:* Sync spec · 0.8 `DidResolverExtensions.VerifyWithRefreshAsync` · tests
  `…LiveCommitUnderAnotherKey_InvalidAfterOneRefresh`, `…KeyRotated_VerifiesAfterOneRefresh`,
  `…AccountPublishingNoUsableKey_IsInvalidAfterOneRefresh`, `…UnresolvableAccount_IsInvalid`
- **STR-48** (MUST) Each row of the 0.8 `Tampered` table yields `Invalid` with the
  rule identified in the result's reason: version 2; signed by another key; signed for another
  DID; signed with another revision; commit is not the root; record block missing; operation
  missing (inversion fails); operation not in the tree; two operations on one path; create with
  prev; update without prev; delete without prev; delete with cid; proof missing; invalid path.
  *Source:* test `RepoSyncVerifierTests.VerifyCommitAsync_BrokenRule_IsInvalid` (`Tampered`) —
  port the table as conformance data
- **STR-49** (MUST) `MaxCommitOps` + 1 ops, or `MaxCommitBlocksBytes` + 1 bytes of
  blocks, is `Invalid`; exactly the maximum is accepted. A `rev` more than `MaxClockSkew` in the
  future is `Invalid`.
  *Source:* tests `…TooManyOperations_IsInvalid`, `…MaximumOperations_IsValid`,
  `…BlocksOverTheLimit_IsInvalid`, `…RevisionInTheFuture_IsInvalid`
- **STR-50** (MUST) The six live `subscribeRepos` frames (`chain-first`,
  `chain-second`, `update`, `delete`, `multi-op`, `sync`) verify as `Valid`, consecutive commits
  chain, and a replayed frame is `Stale`.
  *Source:* vector `firehose-sync-frames.json` · tests `VerifyAsync_LiveFrame_IsValid`,
  `VerifyCommitAsync_LiveConsecutiveCommits_Chain`, `…LiveCommitReplayed_IsStale`
- **STR-51** (MUST) A `RepoSyncVerifier` assumes it is the only writer of its
  `IRepoSyncStateStore` for the DIDs it sees. Running several verifiers against one store (for
  example sharded by DID) is supported only if the shards are disjoint; the docs say so.
  *Source:* **Decision** (the chain rules are not safe under concurrent writers for one DID)

### Resync (`RepoResync`)

- **STR-52** (MUST) With `Resync` set, a repository that becomes `Desynchronized` is
  fetched in full (`com.atproto.sync.getRepo`) and its records are delivered as a
  `RepoResynced` notice carrying the verified `RepoSnapshot`. `Resync` without `Verifier` is an
  options-validation error.
  *Source:* Sync spec, *Record-Level Synchronization* · 0.8 `RepoResync.cs`
- **STR-53** (MUST) All resync state changes happen on the enumeration's loop;
  fetches run in the background and their completions are taken by the loop. No lock-free
  shared state between the two.
  *Source:* 0.8 `RepoResyncCoordinator` design
- **STR-54** (MUST) Fetchers are tried in order: the relay (the `ServiceUrl` with
  `wss`→`https`, `ws`→`http`), then the account's PDS from its DID document. A 400 or 404 means
  "not here, try the next fetcher". Each fetch goes through `XrpcInvoker` under the untrusted-fetch
  policy (public addresses, `https` only; `http` only with a caller-supplied `HttpClient`), with at
  most `MaxResyncRedirects` redirects each re-validated by the policy, a `ResyncFetchTimeout` per
  fetch and at most `MaxResyncRepoBytes` read (a larger declared length is not trusted or
  reserved).
  *Source:* 0.8 `RepoFetchers.cs:137-139`, `RepoResync.cs:45-47` · test
  `RepoFetcherTests.HostRepoFetcher_HugeDeclaredLength_DoesNotReserveIt` ·
  [identity](identity.md) untrusted-fetch rules
- **STR-55** (MUST) A fetched snapshot is read with `RepoSnapshot.VerifyAsync` against
  the account's `#atproto` key and must have `rev` ≥ the revision of the event that broke the
  chain (`MinRev`); otherwise the next fetcher is tried. A verified snapshot is recorded as
  `{rev, data, Synchronized}` when the `RepoResynced` notice's successor is requested.
  *Source:* 0.8 `RepoResync.cs` · [repo](repo.md) `RepoSnapshot`
- **STR-56** (MUST) Events for a repository that arrive while it is being fetched are held
  (at most `MaxHeldEventsPerRepo` per repository, `MaxHeldBytes` in total), then re-verified
  against the snapshot's state and delivered after the `RepoResynced` notice, in sequence order.
  *Source:* 0.8 `RepoResync.cs` · test
  `TypedFirehoseConsumerSyncTests.ConsumeAsync_StopBetweenSnapshotAndHeldEvents_ReplaysTheHeldEvents`
- **STR-57** (MUST) At most `MaxResyncConcurrency` fetches run at once (a slot is held
  until the loop takes the completion) and at most `MaxPendingResyncRepos` repositories wait.
  Beyond that, a desynchronised repository stays `Desynchronized` in the store and is picked up
  by the scan (STR-60).
  *Source:* 0.8 `RepoResync.cs:41-43`
- **STR-58** (MUST) When a repository's held events overflow, they are dropped
  (`ResyncOverflow`), the snapshot (when it arrives) is stored as `Desynchronized`, and the
  repository is fetched again at the latest revision seen.
  *Source:* 0.8 `RepoResync.cs`
- **STR-59** (MUST) When every fetcher fails, the repository is marked
  `Desynchronized`, retried after `ResyncRetryDelay`, and its held events are reported dropped.
  *Source:* 0.8 `RepoResync.cs:53`
- **STR-60** (MUST) Every `ResyncScanInterval` the loop lists unsynchronised repositories
  from the state store (`ListUnsynchronizedAsync`), including ones a previous process left
  `Resynchronizing` and ones the caller marked `Desynchronized` to request a refetch, and queues
  them within the bounds above.
  *Source:* 0.8 `RepoResync.cs:55`, `RepoSyncState.cs:23,35`

### Label stream

- **STR-61** (MUST) `LabelStream` subscribes to
  `{ServiceUrl}/xrpc/com.atproto.label.subscribeLabels?cursor={seq}`. `#labels` frames are
  decoded with `LabelFrameCodec` into a sequenced `LabelsEvent`; `#info` is a `StreamInfo` notice.
  *Source:* `com.atproto.label.subscribeLabels` Lexicon · [labels](labels.md)
- **STR-62** (MUST) With a `LabelVerifier` configured, every label in a `LabelsEvent`
  is annotated with its verification result. Labels that fail verification are delivered with
  their failed result, not dropped, so the caller decides.
  *Source:* 0.8 `LabelStreamConsumer.cs` (annotate, not drop) · [labels](labels.md) LBL rules
- **STR-63** (MUST) Inclusive replay is suppressed by `seq` per STR-19.
  *Source:* 0.8 `LabelStreamConsumer.cs:132`

### Jetstream live (`JetstreamStream`)

- **STR-64** (MUST) Both wire protocols are supported as codecs of one stream
  (`JetstreamProtocol.V1`, `.V2`; default `V2`):
  - **v1:** `/subscribe`, filters `wantedCollections` and `wantedDids`, commit fields nested under
    `commit`, cursor `time_us`, no kinds filter, no `#sync` events;
  - **v2:** `/xrpc/network.bsky.jetstream.subscribeEvents` with WebSocket subprotocol
    `xrpc.v1.json`; every frame is an envelope `{"$type":"message","payload":…}` or
    `{"$type":"error",…}`; the payload `$type` is fully qualified or a `#fragment` of
    `network.bsky.jetstream.subscribeEvents`; commit fields are flat; filters `collections`,
    `dids`, `kinds`; cursor `seq`.
  *Source:* Jetstream v1 README · v2 Lexicons · 0.8 `JetstreamConsumer.cs:50-53`,
  `JetstreamEventParser.cs:21` · **Decision** Q-12 default (keep v1)
- **STR-65** (MUST) On v1, a reconnect resumes at the last `time_us` minus
  `ReconnectRewind` (5 s) and drops events whose `time_us` is not after the last delivered one.
  *Source:* 0.8 `JetstreamModels.cs:124`
- **STR-66** (MUST) On v2, the server replays inclusively from `seq`: a reconnect
  resumes at the last `seq` and drops events whose `seq` is not after it. A v2 event without a
  `seq` is delivered but does not move the position.
  *Source:* 0.8 `JetstreamConsumer.cs`
- **STR-67** (MUST) A start cursor ≥ `JetstreamTimestampThreshold` (10¹⁵) is a
  timestamp seek, not a sequence number: it is sent as the cursor but is not a dedupe floor, and
  the position resets to the first `seq` received. A stored v1 cursor therefore migrates to v2
  without loss.
  *Source:* 0.8 `JetstreamModels.cs:253`, `JetstreamConsumer.cs:166-169` · tests
  `…TimestampCursor_IsNotADedupFloorForSequenceNumbers`, `…StoredV1Cursor_MigratesToSequenceNumbers`
- **STR-68** (MUST) Options are validated at construction: at most
  `MaxJetstreamCollections` collections and `MaxJetstreamDids` DIDs; kinds and a dictionary ID
  are v2-only; collections combined with kinds that exclude `commit` are rejected; on v2 a
  decompressor and a dictionary ID are set together or not at all; `MaxMessageSizeBytes` is at
  most `MaxJetstreamMessageSizeBytes`.
  *Source:* 0.8 `JetstreamModels.cs:19-21` and `Validate`
- **STR-69** (MUST) Compression goes through one `IZstdDecompressor` seam (the SDK ships no
  zstd implementation). v1 sends `compress=true` and uses the unversioned dictionary; v2 sends
  `zstdDictionary={id}`, where the ID is read from the dictionary's own header (magic
  `0xEC30A437` little-endian at bytes 0–3, ID at bytes 4–7, little-endian). The dictionary is
  fetched with `JetstreamArchive.GetZstdDictionaryAsync` without the API key.
  *Source:* 0.8 `JetstreamArchiveClient.cs:123,411-416` · 0.8 has two identically shaped
  interfaces (`IJetstreamDecompressor`, `IJetstreamBlockDecompressor`)
- **STR-70** (MUST) Frames are decompressed at the connection level: a frame that
  fails to decompress fails the connection (and reconnects), it is not skipped as one event. A
  retired dictionary answers `400 UnknownZstdDictionary`, which is not retryable.
  *Source:* 0.8 `JetstreamConsumer.cs:81-86`
- **STR-71** (MUST) Unknown event kinds and malformed frames are dropped with a
  reason. An unparseable `cid`, `handle` or `rev` inside an otherwise valid event is set to
  `null` on that event rather than dropping it. A non-numeric `time_us` or `seq` drops the frame.
  v2 `#info` messages are `StreamInfo` notices.
  *Source:* #126 · 0.8 `JetstreamEventParser.cs` · tests `JetstreamEventParserTests`,
  `JetstreamV2EventParserTests`
- **STR-72** (MUST) `JetstreamCommitEvent` implements `IRecordEvent`; `As<T>()` binds
  the JSON record through the same generated contracts as XRPC responses.
  *Source:* 0.8 `JetstreamCommitEvent : IRecordEvent`

### Jetstream archive (`JetstreamArchive`, experimental `ATPROTO003`)

- **STR-73** (MUST) The archive client calls `network.bsky.jetstream.planSnapshot`,
  `listSegments`, `getSegment`, `getBlock`, `getZstdDictionary` and `/xrpc/_health` through
  `XrpcInvoker`. The API key is sent as `Authorization: Bearer` on authenticated endpoints only.
  All archive types carry `[Experimental("ATPROTO003")]`.
  *Source:* 0.8 `JetstreamArchiveClient.cs:115-120` · **Decision** [D-16](../06-decisions.md#d-16-experimental-diagnostic-ids)
- **STR-74** (MUST) The archive client retries its own idempotent `GET`s on top of the
  invoker (which retries nothing but 429): transport errors and 5xx back off `min(30, 2ⁿ)` s for
  at most `MaxArchiveRetryAttempts`; 429 honours `Retry-After` up to `MaxArchiveRetryDelay`;
  401 and other 4xx are not retried and throw `StreamException`.
  *Source:* 0.8 `JetstreamArchiveClient.cs:138-141`, `JetstreamArchiveModels.cs:82-86` ·
  [xrpc-client](xrpc-client.md) "no hidden retries" rule (this retry is explicit and GET-only)
- **STR-75** (MUST) Segment downloads resume with a `Range` request after a failure and
  restart from zero if the server ignores `Range`.
  *Source:* 0.8 `JetstreamArchiveClient` · tests `JetstreamArchiveClientTests`
- **STR-76** (MUST) The segment reader decodes `.jss` columns with
  `JetstreamSegmentHeader.Size` (256) byte headers, rejects a block declaring more than
  `MaxSegmentEventsPerBlock` events or a frame over `MaxSegmentBlockFrameBytes`, computes each
  record's CID from its payload, and delivers `CreateResync` rows as creates.
  *Source:* 0.8 `JetstreamSegmentReader.cs:167,252-259` · tests
  `JetstreamSegmentReaderTests.DecodeBlock_RejectsAnImplausibleEventCount`,
  `JetstreamGoldenBlockTests` (vectors `jetstream-golden-{header,block}.bin`)

### Backfill then live (`StreamCutover`, `JetstreamReplay`)

- **STR-77** (MUST) `StreamCutover<TItem>` combines a backfill source (pages up to
  a pinned tip) with a live stream started at that tip. It owns the **only** cursor tracker for
  the combined enumeration: the live stream runs without its own cursor store, and deduplication
  across the boundary (and across overlapping backfill pages) happens once, by the cutover's
  position.
  *Source:* **Decision** · audit (0.8 `JetstreamReplayConsumer` opens a second `CursorTracker` on
  the same store and `StreamId`, lines 126/188, so two writers share one key and deduplicate twice)
- **STR-78** (MUST) `JetstreamReplay` requires v2 and archive options; a
  timestamp resume cursor is rejected; `BeforeSeq` requires `SnapshotOnly`; a start position at
  or after `BeforeSeq` yields nothing.
  *Source:* 0.8 `JetstreamReplayConsumer` preconditions
- **STR-79** (MUST) The tip is pinned from the first plan page as
  `min(BeforeSeq, SealedTipSeq)` and sent as `BeforeSeq` on every later page.
  *Source:* 0.8 `JetstreamReplayConsumer.cs:287-291` · test
  `JetstreamReplayConsumerTests.BeforeSeq_CapsTheCeilingOnEveryPageNotJustTheFirst`
- **STR-80** (MUST) A plan page that does not advance while still below the tip is never
  skipped: it is re-planned after 1, 2, 4… s (capped at 64 s) up to `MaxStalledPlanAttempts`,
  then the replay throws `StreamException`.
  *Source:* 0.8 `JetstreamReplayConsumer.cs:298-317,345`
- **STR-81** (MUST) Archive rows are filtered by their columns before decoding: the
  window is `(after, ceiling]`; DIDs and kinds constrain every row; collections constrain commit
  rows only, so account, identity and sync rows reach a collection-filtered consumer.
  *Source:* 0.8 `JetstreamReplayConsumer`
- **STR-82** (MUST) Work units download with `DownloadParallelism` (4) in parallel
  and are delivered strictly in plan order. Segments are spooled to temporary files opened with
  `DeleteOnClose`. A download failure that exhausts its retries throws.
  *Source:* 0.8 `JetstreamArchiveModels.cs:68`
- **STR-83** (MUST) After the last backfill row, one live stream connects at the pinned
  tip. If it fails with a non-retryable `StreamException` carrying a status (the tip aged out of
  the live lookback window), the replay re-plans from the last delivered event, up to
  `MaxCutoverAttempts`.
  *Source:* 0.8 `JetstreamArchiveModels.cs:92`

### Tap

- **STR-84** (MUST) `TapStream` connects to Tap's `/channel` WebSocket with HTTP Basic
  `admin:{password}` when a password is set, and yields `TapEvent`s (record, identity and
  account events). `TapRecordEvent` implements `IRecordEvent`; `Live` distinguishes firehose
  changes from backfill and resync.
  *Source:* `@atproto/tap` wire protocol · 0.8 `TapClient.cs`, `TapModels.cs`
- **STR-85** (MUST) Acknowledgement follows the runtime's at-least-once rule by
  default (`TapAckMode.AfterNextPull`): event *n* is acknowledged (`{"type":"ack","id":N}`) when
  the consumer requests event *n + 1*, and an event being handled when the enumeration ends is
  not acknowledged, so Tap redelivers it. `TapAckMode.Manual` leaves acknowledgement to
  `TapStream.AckAsync`. Acknowledgements made while disconnected are queued and sent on
  reconnect; acknowledging twice is harmless.
  *Source:* **Decision** (same model as cursors) · 0.8 `TapChannel` (manual only)
- **STR-86** (MUST) **Tap unreadable-event rule.** A Tap message that is not a
  readable event (malformed, or of a type this SDK version does not model) is **acknowledged**,
  logged at `Warning`, counted, and yielded as `EventDropped` (`Unreadable`) when dropped events
  are requested. The webhook receiver ([server](server.md)) applies the same rule: it answers
  `200` for an unreadable body. A handler failure is different: the channel leaves the event
  unacknowledged (the enumeration ended or the caller did not ack) and the webhook answers `500`,
  so Tap retries.
  *Reasoning:* Tap delivers each repository's events in order and holds back later events until
  the in-flight one is acknowledged. An unacknowledged unreadable event is redelivered forever and
  stalls its repository permanently; a newer SDK version that could read it is not coming to the
  running process. Losing one unreadable event (visible in logs and metrics) is the lesser harm,
  and both delivery modes must agree.
  *Source:* **Decision** · resolves the 0.8 contradiction between
  `TapClientTests.ReadAllAsync_InvalidMessage_IsReportedAndSkipped` (not acknowledged, `TapClient.cs:272-275`)
  and `TapWebhookTests.Post_NotATapEvent_IsAcknowledgedWithAWarning` (acknowledged,
  `TapWebhookExtensions.cs:169-176`) · `docs/tap.md:60`
- **STR-87** (MUST) A `TapStream` enumeration is the only reader of its
  connection. Several streams (in one or many processes) may be open at once; Tap shares events
  among them.
  *Source:* 0.8 `TapChannel` (one reader per channel)
- **STR-88** (MUST) `TapClient` exposes Tap's admin endpoints (`/repos/add`,
  `/repos/remove`, `/info/{did}`, …) with HTTP Basic `admin:{password}` (passwords containing `:`
  work), bounded response reads and failures as `StreamException` with `StatusCode`.
  *Source:* 0.8 `TapClient.cs` · `docs/tap.md` admin table · **Decision** STR-30

### PLC export (`PlcExportStream`)

- **STR-89** (MUST) `PlcExportStream` reads the PLC directory's `/export/stream`
  WebSocket on `StreamLoop` with `seq` cursors, through the untrusted-fetch policy for a
  directory URL the SDK did not get from configuration, with messages capped at
  `MaxPlcExportMessageBytes`. A dropped connection reconnects and resumes from the last `seq`;
  0.8 ended the enumeration silently.
  *Source:* 0.8 `PlcClient.cs:282-320,33`
- **STR-90** (MUST) A close with reason `FutureCursor` throws a non-retryable
  `StreamException`; `ConsumerTooSlow` reconnects from the last `seq`; `OutdatedCursor` yields a
  `StreamInfo` notice and then catches up through the paged `/export` endpoint from the last
  `seq` (a `StreamCutover`) before reconnecting the stream.
  *Source:* PLC directory export semantics · **Decision** (see open point 3) · 0.8 throws in all
  three cases

### Chat moderation stream (`ATProtoNet.Bluesky`)

- **STR-91** (MUST) The `chat.bsky.moderation.subscribeModEvents` stream is a
  `StreamLoop` codec in `ATProtoNet.Bluesky`. It obtains a fresh `Authorization` header from its
  `IXrpcHandler` (or a supplied token source) on every connection, prefixes frame types with the
  NSID to bind the generated union, and uses opaque `rev` cursors starting at `"2222222222222"`
  (the beginning) when asked. It supports a cursor store, which 0.8 did not.
  *Source:* 0.8 `ChatModerationEventConsumer.cs:62,98-124` · **Decision** STR-25

### Stores

- **STR-92** (MUST) `ICursorStore` and `IRepoSyncStateStore` are defined in this
  package with in-memory implementations (`InMemoryCursorStore`, `InMemoryRepoSyncStateStore`).
  Durable implementations (file, EF Core) live in other packages and pass the contract suites in
  [stores](stores.md). A firehose worker needs no ASP.NET Core reference to get a durable store.
  *Source:* **Decision** [D-15](../06-decisions.md#d-15-stores-are-interfaces-in-the-package-that-uses-them-implementations-live-elsewhere) ·
  audit (0.8 ships no durable cursor store; the durable sync-state store is in the ASP.NET-bound EF package)
- **STR-93** (MUST) The verifier writes a `Desynchronized` state immediately and
  a `Synchronized` state only through the delivery rule (STR-40); writes after
  delivery use a non-cancelled token.
  *Source:* 0.8 `TypedFirehoseConsumer.cs:267`, `RepoSyncVerifier.ApplyAsync`

### Diagnostics and hosting

- **STR-94** (MUST) The `ATProtoNet.Streaming` `ActivitySource` emits one activity per
  connection (stream name, service host, start cursor) and the meter records events delivered,
  dropped (by reason), errors, reconnects, cursor saves and save failures, verification outcomes,
  resync fetches and held events, and lag (now minus event time where the event has one).
  *Source:* [02 — Architecture](../02-architecture.md#diagnostics), AG-32
- **STR-95** (MUST) Running a stream as a hosted service is `StreamWorker<TItem>` in
  `ATProtoNet.Hosting` ([hosting](hosting.md)). This package has no dependency on
  `Microsoft.Extensions.Hosting`.
  *Source:* **Decision** [D-03](../06-decisions.md#d-03-package-map)

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `MaxStreamMessageBytes` | 64 MiB | one WebSocket message, firehose/labels/Jetstream/Tap |
| `MaxPlcExportMessageBytes` | 1 MiB | one PLC export message |
| `StreamCloseTimeout` | 2 s | graceful close handshake |
| `DefaultCursorPersistInterval` | 100 events | background cursor saves |
| `DefaultReconnectInitialDelay` / `DefaultReconnectMaxDelay` | 5 s / 30 s | backoff (+0–10% jitter) |
| `DefaultReconnectMaxAttempts` | 10 | `null` = forever, `0` = never |
| `MaxCommitOps` | 200 | ops in one `#commit` |
| `MaxCommitBlocksBytes` | 2,000,000 | `blocks` of one `#commit` |
| `MaxRecordBytes` | 1,000,000 | one record block in a commit |
| `DefaultMaxClockSkew` | 5 min | future `rev` tolerance |
| `MaxResyncConcurrency` | 4 | concurrent repo fetches |
| `MaxPendingResyncRepos` | 1,000 | queued repositories |
| `MaxResyncRepoBytes` | 256 MiB | one fetched repository |
| `MaxHeldEventsPerRepo` | 1,000 | events held during one fetch |
| `MaxHeldBytes` | 64 MiB | all held events |
| `ResyncFetchTimeout` | 5 min | one fetch |
| `MaxResyncRedirects` | 3 | per fetch |
| `ResyncRetryDelay` / `ResyncScanInterval` | 1 min / 1 min | failed fetch retry, store scan |
| `MaxJetstreamCollections` / `MaxJetstreamDids` | 100 / 10,000 | Jetstream filters |
| `MaxJetstreamMessageSizeBytes` | 4,294,967,295 | Jetstream `maxMessageSizeBytes` option |
| `JetstreamReconnectRewind` | 5 s | v1 reconnect |
| `JetstreamTimestampThreshold` | 10¹⁵ | cursor ≥ this is a timestamp seek |
| `DefaultDownloadParallelism` | 4 | archive work units |
| `MaxArchiveRetryAttempts` / `MaxArchiveRetryDelay` | 5 / 5 min | archive GET retries |
| `MaxCutoverAttempts` / `MaxStalledPlanAttempts` | 3 / 5 | replay |
| `JetstreamSegmentHeader.Size` | 256 bytes | `.jss` header |
| `MaxSegmentEventsPerBlock` | 2²² | `.jss` block |
| `MaxSegmentBlockFrameBytes` | 256 MiB | `.jss` block frame |

DRISL nesting depth, string and collection limits for frame bodies come from
[data-model](data-model.md); CAR bounds from the same spec.

## Errors

| Failure | Result |
|---|---|
| Connection refused, upgrade rejected, network failure, error frame, abnormal close, attempts exhausted, non-retryable status, archive or Tap admin HTTP failure | `StreamException` (with `Error`, `StatusCode`, `RetryAfter`, `IsRetryable`, `InnerException` as applicable) |
| Undeliverable frame, stale or invalid commit, unreadable Tap message, resync overflow | dropped; `EventDropped` notice if requested; metric always |
| Chain broken | `RepoDesynchronized` notice; event dropped |
| `#info`, `OutdatedCursor` | `StreamInfo` notice |
| Cursor store or sync-state store failure | logged and counted; enumeration continues (sync-state read failures for a commit drop it as `VerificationFailed`) |
| Invalid options | `ArgumentException` at construction |
| Caller cancellation | enumeration ends normally |

## Conformance and tests

**Conformance vectors** ([04 — Testing](../04-testing.md#conformance-areas)): firehose and Sync 1.1
(`firehose-sync-frames.json` plus the `Tampered` table as data), Jetstream
(`jetstream-golden-{header,block}.bin`, with provenance recorded), and the hostile-input corpus
entries for deep nesting, huge declared lengths and implausible block event counts.

**0.8 test classes whose scenarios must be ported:** `EventStreamLoopTests`,
`StreamCursorStoreTests`, `StreamSocketTests`, `FirehoseEventParserTests`,
`TypedFirehoseConsumerTests`, `TypedFirehoseConsumerSyncTests`, `RepoSyncVerifierTests`,
`RepoFetcherTests`, `RecordEventTests`, `JetstreamConsumerTests`, `JetstreamV2ConsumerTests`,
`JetstreamEventParserTests`, `JetstreamV2EventParserTests`, `JetstreamArchiveClientTests`,
`JetstreamReplayConsumerTests`, `JetstreamSegmentReaderTests`, `JetstreamGoldenBlockTests`,
`TapClientTests` (with the unreadable-event case inverted per STR-86),
`InMemoryRepoSyncStateStoreTests`. Merge `JetstreamArchiveClientPublicEndpointTests` into
`JetstreamArchiveClientTests` (duplicate coverage).

**New tests:** one `StreamLoop` contract suite run against every codec (start precedence,
at-least-once on every exit, dedupe, persistence, backoff, notices); the cutover single-writer
property (one cursor writer, monotonic stored values); PLC export reconnect and catch-up;
cursor-store opacity (chat moderation `rev` cursors round-trip).

**Integration (nightly):** live Jetstream v2 (`Requirement=Jetstream`), archive
(`Requirement=JetstreamArchive`, small and metered), and a relay firehose smoke test with Sync 1.1
verification.

## Changes from 0.8

| Change | Reason |
|---|---|
| One `StreamLoop` for every stream, including Jetstream replay, Tap and PLC export | 0.8 has four loops with different reconnect and cursor behaviour |
| Callbacks (`OnStreamError`, `OnEventDropped`, `OnInfo`, `OnDesynchronized`) replaced by in-band notices | D-23; one consumption model |
| `RepoResyncEvent` is a `RepoResynced` notice, no longer part of the Lexicon `FirehoseMessage` union | synthetic events do not belong in a wire union |
| Firehose envelopes bound directly from CBOR | D-10; 0.8 base64-encodes up to 2 MB of blocks per commit through JSON |
| One `StreamException`; `JetstreamException`, `TapException`, `RepoFetchException` removed | D-23 |
| `ICursorStore` stores opaque strings | chat moderation (`rev`) and future cursor kinds fit; 0.8 stores `long` only |
| `VerifyCids` option removed | integrity without authenticity; CID checks are part of Sync 1.1 verification |
| Tap unreadable events are acknowledged on the channel too (STR-86) | the 0.8 channel stalls the repository forever |
| Tap auto-acknowledgement mode (default) | same at-least-once model as cursors; manual mode kept |
| Replay owns a single cursor tracker | 0.8 runs two trackers on one key |
| PLC export reconnects and catches up on `OutdatedCursor` | 0.8 ends silently on a drop and throws on every close reason |
| One `IZstdDecompressor` | 0.8 has two identical interfaces |
| Jetstream options split into live (`JetstreamOptions`) and archive (`JetstreamArchiveOptions`) | 0.8 carries 22 properties with cross-field validation in one object |
| Stream objects hold no per-enumeration state | 0.8 Jetstream cannot be enumerated twice concurrently |
| Transport seam public | users could not set proxies or headers |
| Jetstream health check moves to `ATProtoNet.Hosting` and honours its failure status | 0.8's always reports `Unhealthy` and lives under `Server/Aspire` |

## Open points

1. **Account hosting status.** The Sync spec says consumers should ignore `#commit`/`#sync` for
   accounts whose status is not `active`, and for accounts whose authoritative PDS is not the
   connected host (when subscribing to a PDS directly). 0.8 does neither. *Proposed default:* the
   verifier records `#account` status in `RepoSyncState` (new `Active` field) and drops
   `#commit`/`#sync` for inactive accounts as `VerificationFailed`; host binding is deferred until
   someone subscribes to PDSes directly.
2. **zstd in the BCL.** If a future .NET ships Zstandard in `System.IO.Compression`, provide a
   default `IZstdDecompressor` over it. *Proposed default:* keep the seam, ship no default in 1.0.
3. **PLC catch-up.** Automatic catch-up through paged `/export` on `OutdatedCursor`
   (STR-90) assumes the directory's paged export accepts the stream's `seq` as `after`.
   *Proposed default:* verify against plc.directory in phase 7; if unsupported, yield the notice
   and throw a non-retryable `StreamException`.
4. **Tap admin error shape.** Tap answers admin failures with a status and a small body. *Proposed
   default:* `StreamException` with `StatusCode` and the body (bounded) as `Message`; revisit if
   Tap adopts the XRPC `{error,message}` envelope.
5. **Notice interfaces.** Notice records implement each in-package stream's item interface they
   can occur on; out-of-package codecs wrap them. *Proposed default:* keep; revisit if C# gains
   union types.
