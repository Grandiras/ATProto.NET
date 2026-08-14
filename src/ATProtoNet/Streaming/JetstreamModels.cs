using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Streaming;

/// <summary>
/// Configuration options for <see cref="JetstreamClient"/> and <see cref="JetstreamConsumer"/>.
/// </summary>
public sealed class JetstreamConsumerOptions
{
    /// <summary>
    /// The Jetstream host URL, without the endpoint path
    /// (e.g., "wss://jetstream.us-east.bsky.network").
    /// <c>http(s)</c> schemes are converted to <c>ws(s)</c> automatically.
    /// See <see cref="JetstreamEndpoints"/> for the public Bluesky-operated instances.
    /// </summary>
    public required string ServiceUrl { get; init; }

    /// <summary>
    /// Which Jetstream wire protocol to speak. Defaults to
    /// <see cref="JetstreamProtocol.V1"/> for backwards compatibility;
    /// <see cref="JetstreamProtocol.V2"/> is the current protocol and should be
    /// preferred for new consumers.
    /// </summary>
    /// <remarks>
    /// The two protocols differ in endpoint path, filter parameter names, event shape,
    /// and cursor semantics — see <see cref="JetstreamProtocol"/>. Only the v2 hosts
    /// (<see cref="JetstreamEndpoints.UsEast"/>, <see cref="JetstreamEndpoints.UsWest"/>)
    /// serve <see cref="JetstreamProtocol.V2"/>; both v1 and v2 hosts serve
    /// <see cref="JetstreamProtocol.V1"/>.
    /// </remarks>
    public JetstreamProtocol Protocol { get; init; } = JetstreamProtocol.V1;

    /// <summary>
    /// Collections to receive commit events for. Supports full NSIDs
    /// (e.g., "app.bsky.feed.post") and prefix wildcards (e.g., "app.bsky.graph.*").
    /// Maximum 100 entries. If null or empty, commit events for all collections are delivered.
    /// </summary>
    public IReadOnlyList<string>? WantedCollections { get; init; }

    /// <summary>
    /// DIDs to receive events for. Maximum 10,000 entries.
    /// If null or empty, events for all repos are delivered.
    /// </summary>
    public IReadOnlyList<string>? WantedDids { get; init; }

    /// <summary>
    /// Event kinds to receive. <see cref="JetstreamProtocol.V2"/> only — v1 has no
    /// <c>kinds</c> filter and delivers every kind. If null or empty, all kinds are delivered.
    /// </summary>
    /// <remarks>
    /// <see cref="WantedCollections"/> constrains commit events only; identity, account, and
    /// sync events flow regardless. A commits-only stream therefore needs
    /// <c>WantedKinds = [JetstreamEventKind.Commit]</c> as well. Setting
    /// <see cref="WantedCollections"/> while this list excludes
    /// <see cref="JetstreamEventKind.Commit"/> is rejected by the server, and by
    /// <see cref="JetstreamClient"/> before it connects.
    /// </remarks>
    public IReadOnlyList<JetstreamEventKind>? WantedKinds { get; init; }

    /// <summary>
    /// Maximum size (in bytes) of a payload the server will deliver.
    /// Records larger than the limit are dropped server-side. If null, no limit is requested.
    /// </summary>
    public long? MaxMessageSizeBytes { get; init; }

    /// <summary>
    /// Optional decompressor for zstd-compressed frames. When set, <c>compress=true</c>
    /// is requested from the server and binary frames are passed through
    /// <see cref="IJetstreamDecompressor.Decompress"/>. When null (default),
    /// uncompressed JSON text frames are requested.
    /// </summary>
    /// <remarks>
    /// <para>The SDK does not ship a zstd implementation to keep the core package
    /// dependency-free. Jetstream compression uses a custom zstd dictionary; see the
    /// Jetstream documentation page for a ready-to-use implementation based on
    /// <c>ZstdSharp.Port</c>.</para>
    /// <para>On <see cref="JetstreamProtocol.V2"/> the dictionary is versioned and
    /// <see cref="ZstdDictionaryId"/> must be set alongside this — fetch both with
    /// <see cref="JetstreamDictionaryClient"/>.</para>
    /// </remarks>
    public IJetstreamDecompressor? Decompressor { get; init; }

    /// <summary>
    /// The zstd dictionary ID to request compressed frames with.
    /// <see cref="JetstreamProtocol.V2"/> only, and required whenever
    /// <see cref="Decompressor"/> is set on v2 (v1 negotiates compression with a bare
    /// <c>compress=true</c> and an unversioned dictionary).
    /// </summary>
    /// <remarks>
    /// Obtain the ID and the matching dictionary bytes from
    /// <see cref="JetstreamDictionaryClient.GetDictionaryAsync"/>. A retired ID is rejected
    /// before the WebSocket upgrade with an HTTP 400 (<c>UnknownZstdDictionary</c>) naming
    /// the current one.
    /// </remarks>
    public int? ZstdDictionaryId { get; init; }

    /// <summary>
    /// Configuration for the v2 archive — the HTTP replay endpoints behind
    /// <see cref="JetstreamReplayConsumer"/>. Required by that consumer and ignored by the
    /// live-only <see cref="JetstreamClient"/> and <see cref="JetstreamConsumer"/>, so one options
    /// object configures a backfill and the live tail it cuts over into with a single set of
    /// filters.
    /// </summary>
    /// <remarks>
    /// <see cref="JetstreamProtocol.V2"/> only: v1 has no archive.
    /// </remarks>
    public JetstreamArchiveOptions? Archive { get; init; }

    /// <summary>Optional cursor store for persistent resume across restarts.
    /// The stored value is whatever the selected <see cref="Protocol"/> uses as its cursor:
    /// the event's <c>time_us</c> (unix microseconds) on <see cref="JetstreamProtocol.V1"/>,
    /// and its <c>seq</c> (<see cref="JetstreamEvent.Cursor"/>) on
    /// <see cref="JetstreamProtocol.V2"/>. The two are not interchangeable — give each
    /// protocol its own <see cref="StreamId"/> if both share a store.</summary>
    public IFirehoseCursorStore? CursorStore { get; init; }

    /// <summary>Stream identifier used as the key for cursor storage. Defaults to the service URL.</summary>
    public string? StreamId { get; init; }

    /// <summary>Interval (in number of events) between cursor persistence. Default: 100.</summary>
    public int CursorPersistInterval { get; init; } = 100;

    /// <summary>
    /// How far to rewind the cursor when reconnecting, to compensate for events that may
    /// have been in flight when the connection dropped. Replayed events already delivered
    /// are filtered out by <see cref="JetstreamConsumer"/>. Default: 5 seconds.
    /// </summary>
    /// <remarks>
    /// <see cref="JetstreamProtocol.V1"/> only. A v2 cursor is a sequence number the server
    /// replays inclusively, so there is nothing to rewind past — the consumer reconnects at
    /// the last sequence number it delivered.
    /// </remarks>
    public TimeSpan ReconnectRewind { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Base delay between reconnection attempts. Default: 5 seconds.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Max reconnection attempts. Default: 10. Use -1 for unlimited.</summary>
    public int MaxReconnectAttempts { get; init; } = 10;

    /// <summary>Optional logger.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// Invoked for each advisory <c>#info</c> frame. <see cref="JetstreamProtocol.V2"/> only.
    /// Info frames carry no sequence number and do not advance the cursor, so they are not
    /// delivered as events.
    /// </summary>
    public Action<JetstreamInfo>? OnInfo { get; init; }

    /// <summary>
    /// Invoked when the server sends a terminal error frame (e.g. <c>ConsumerTooSlow</c>)
    /// before closing the stream. <see cref="JetstreamProtocol.V2"/> only.
    /// <see cref="JetstreamConsumer"/> reconnects afterwards as it would after any drop.
    /// </summary>
    public Action<JetstreamStreamError>? OnStreamError { get; init; }

    /// <summary>The resolved stream identifier for cursor storage.</summary>
    internal string ResolvedStreamId => StreamId ?? ServiceUrl;
}

/// <summary>The Jetstream wire protocol a client speaks.</summary>
public enum JetstreamProtocol
{
    /// <summary>
    /// The original Jetstream wire, served at <c>/subscribe</c>. Filters are named
    /// <c>wantedCollections</c> / <c>wantedDids</c>, commit fields are nested under a
    /// <c>commit</c> object, events are identified by a <c>time_us</c> timestamp, and there
    /// is no <c>kinds</c> filter and no <c>sync</c> event. Frozen; served by every public
    /// instance, including the v2 hosts.
    /// </summary>
    V1,

    /// <summary>
    /// The current wire, served at <c>/xrpc/network.bsky.jetstream.subscribeEvents</c> under
    /// the <c>xrpc.v1.json</c> subprotocol. Every frame is a self-describing JSON envelope,
    /// filters are named <c>collections</c> / <c>dids</c> / <c>kinds</c>, commit fields are
    /// flat, <c>sync</c> events are delivered, and the cursor is a monotonic sequence number
    /// (<see cref="JetstreamEvent.Cursor"/>) rather than a timestamp. Served only by the v2
    /// hosts — see <see cref="JetstreamEndpoints"/>.
    /// </summary>
    V2,
}

/// <summary>
/// A Jetstream event kind, used by the <see cref="JetstreamConsumerOptions.WantedKinds"/>
/// filter. These are the <c>$type</c> fragment names of the v2 message union.
/// </summary>
public enum JetstreamEventKind
{
    /// <summary>A record create, update, or delete — <see cref="JetstreamCommitEvent"/>.</summary>
    Commit,

    /// <summary>An identity change — <see cref="JetstreamIdentityEvent"/>.</summary>
    Identity,

    /// <summary>An account status change — <see cref="JetstreamAccountEvent"/>.</summary>
    Account,

    /// <summary>A repo resynchronization marker — <see cref="JetstreamSyncEvent"/>.</summary>
    Sync,
}

/// <summary>
/// The public Jetstream instances operated by Bluesky.
/// </summary>
public static class JetstreamEndpoints
{
    /// <summary>US East v2 instance. Serves both <see cref="JetstreamProtocol.V2"/> and <see cref="JetstreamProtocol.V1"/>.</summary>
    public const string UsEast = "wss://jetstream.us-east.bsky.network";

    /// <summary>US West v2 instance. Serves both <see cref="JetstreamProtocol.V2"/> and <see cref="JetstreamProtocol.V1"/>.</summary>
    public const string UsWest = "wss://jetstream.us-west.bsky.network";

    /// <summary>Legacy US East instance. <see cref="JetstreamProtocol.V1"/> only.</summary>
    public const string LegacyUsEast1 = "wss://jetstream1.us-east.bsky.network";

    /// <summary>Legacy US East instance. <see cref="JetstreamProtocol.V1"/> only.</summary>
    public const string LegacyUsEast2 = "wss://jetstream2.us-east.bsky.network";

    /// <summary>Legacy US West instance. <see cref="JetstreamProtocol.V1"/> only.</summary>
    public const string LegacyUsWest1 = "wss://jetstream1.us-west.bsky.network";

    /// <summary>Legacy US West instance. <see cref="JetstreamProtocol.V1"/> only.</summary>
    public const string LegacyUsWest2 = "wss://jetstream2.us-west.bsky.network";
}

/// <summary>
/// An advisory, non-fatal notice about the stream (a v2 <c>#info</c> frame).
/// Carries no sequence number and does not advance the cursor.
/// </summary>
public sealed class JetstreamInfo
{
    /// <summary>The notice name. Currently only <c>OutdatedCursor</c>, sent as the first
    /// frame when a timestamp cursor below the retention floor was clamped up to it.</summary>
    public required string Name { get; init; }

    /// <summary>A human-readable description, if the server provided one.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// A terminal error frame sent by the server immediately before it closes the stream
/// (a v2 <c>error</c> envelope).
/// </summary>
public sealed class JetstreamStreamError
{
    /// <summary>The error name, with no namespace or <c>#</c> prefix — e.g. <c>ConsumerTooSlow</c>.</summary>
    public required string Error { get; init; }

    /// <summary>A human-readable description, if the server provided one.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// The outcome of parsing one Jetstream frame: at most one of an event, an advisory notice,
/// or a terminal error. All three are null for a frame that could not be understood, which
/// callers should skip.
/// </summary>
/// <param name="Event">The parsed event, if the frame carried one.</param>
/// <param name="Info">The advisory notice, if the frame was an <c>#info</c>.</param>
/// <param name="Error">The terminal error, if the frame was an error envelope.</param>
public readonly record struct JetstreamFrame(
    JetstreamEvent? Event,
    JetstreamInfo? Info,
    JetstreamStreamError? Error);

/// <summary>
/// Thrown when a Jetstream subscription is rejected before the WebSocket upgrade completes.
/// </summary>
/// <remarks>
/// The v2 endpoint validates the subscription up front and answers with an XRPC error
/// envelope: <c>CursorTooOld</c> when the requested sequence number is below the retention
/// floor, <c>UnknownZstdDictionary</c> for a retired dictionary ID, and <c>InvalidRequest</c>
/// for a malformed filter. None of those become valid by retrying the same request, so
/// <see cref="JetstreamConsumer"/> rethrows rather than reconnecting — a backfilling consumer
/// is meant to re-enter its backfill from the last sequence number it durably processed.
/// </remarks>
public sealed class JetstreamConnectException : Exception
{
    /// <summary>Create a connect exception.</summary>
    /// <param name="message">The error description.</param>
    /// <param name="statusCode">The HTTP status the server answered the upgrade with, if known.</param>
    /// <param name="innerException">The underlying transport exception, if any.</param>
    public JetstreamConnectException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
        => StatusCode = statusCode;

    /// <summary>The HTTP status code the upgrade request was answered with, if the transport reported one.</summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Whether reconnecting with the same request could succeed. False for 4xx statuses other
    /// than 429, which reject the subscription itself rather than reporting a transient fault.
    /// </summary>
    public bool IsRetryable => StatusCode is not (>= 400 and < 500) || StatusCode == 429;
}

/// <summary>
/// Decompresses zstd-compressed Jetstream frames.
/// Implementations must use the Jetstream custom zstd dictionary.
/// </summary>
public interface IJetstreamDecompressor
{
    /// <summary>Decompress a binary WebSocket frame into UTF-8 JSON bytes.</summary>
    /// <param name="frame">The raw compressed frame.</param>
    /// <returns>The decompressed UTF-8 JSON payload.</returns>
    byte[] Decompress(ReadOnlySpan<byte> frame);
}

/// <summary>The repository operation carried by a Jetstream commit event.</summary>
public enum JetstreamOperation
{
    /// <summary>A record was created.</summary>
    Create,

    /// <summary>A record was updated.</summary>
    Update,

    /// <summary>A record was deleted.</summary>
    Delete,
}

/// <summary>
/// Base type for events received from a Jetstream instance.
/// </summary>
/// <remarks>
/// Jetstream events are plain JSON without MST proofs or commit signatures —
/// unlike the binary firehose, they <b>cannot be cryptographically verified</b>.
/// For canonical state, re-fetch records via <c>com.atproto.repo.getRecord</c>.
/// </remarks>
public abstract class JetstreamEvent
{
    /// <summary>The repo (account) DID this event concerns.</summary>
    public required Did Did { get; init; }

    /// <summary>
    /// Event timestamp in unix microseconds. On <see cref="JetstreamProtocol.V1"/> this is
    /// also the value to use as a cursor when resuming a subscription; on
    /// <see cref="JetstreamProtocol.V2"/> it is derived from the frame's RFC 3339 <c>time</c>
    /// and <see cref="Cursor"/> is the resume position.
    /// </summary>
    public required long TimeUs { get; init; }

    /// <summary>
    /// Jetstream's monotonic per-event sequence number — the value to pass back as
    /// <c>?cursor=</c> to resume after this event.
    /// </summary>
    /// <remarks>
    /// Present on every <see cref="JetstreamProtocol.V2"/> event. On
    /// <see cref="JetstreamProtocol.V1"/> it is populated from the frame's <c>cursor</c>
    /// field, which only the v2 hosts emit, and is null when reading a legacy instance.
    /// Unlike <see cref="TimeUs"/>, a sequence number is unaffected by an operator timestamp
    /// import, so it is always a faithful resume position.
    /// </remarks>
    public long? Cursor { get; init; }

    /// <summary>The event timestamp as a UTC <see cref="DateTimeOffset"/>, derived from <see cref="TimeUs"/>.</summary>
    public DateTimeOffset Timestamp => DateTimeOffset.UnixEpoch.AddTicks(TimeUs * 10);
}

/// <summary>A record create/update/delete in a repository.</summary>
public sealed class JetstreamCommitEvent : JetstreamEvent
{
    private AtUri? _uri;

    /// <summary>The collection NSID (e.g., "app.bsky.feed.post").</summary>
    public required string Collection { get; init; }

    /// <summary>The record key within the collection.</summary>
    public required string RKey { get; init; }

    /// <summary>The repository operation.</summary>
    public required JetstreamOperation Operation { get; init; }

    /// <summary>The repo revision (TID) of the commit, if present.</summary>
    public string? Rev { get; init; }

    /// <summary>The record CID. Null for delete operations.</summary>
    public Cid? Cid { get; init; }

    /// <summary>
    /// The record body as raw JSON. Null for delete operations.
    /// Use <see cref="GetRecord{T}"/> for typed deserialization.
    /// </summary>
    public JsonElement? Record { get; init; }

    /// <summary>The <c>at://</c> URI of the affected record.</summary>
    public AtUri Uri => _uri ??= AtUri.Parse($"at://{Did}/{Collection}/{RKey}");

    /// <summary>
    /// Deserialize the record body as <typeparamref name="T"/> using the SDK's
    /// serialization defaults, including types registered in <see cref="LexiconTypeRegistry"/>.
    /// Returns null for delete operations.
    /// </summary>
    public T? GetRecord<T>() where T : class
        => Record is { } record
            ? record.Deserialize<T>(LexiconTypeRegistry.Instance.CreateOptions())
            : null;
}

/// <summary>An identity change (e.g., handle update) for a repo.</summary>
public sealed class JetstreamIdentityEvent : JetstreamEvent
{
    /// <summary>The new handle, if provided.</summary>
    public string? Handle { get; init; }

    /// <summary>The firehose sequence number of the underlying identity event, if present.</summary>
    public long? Seq { get; init; }

    /// <summary>The identity event timestamp (ISO 8601), if present.</summary>
    public string? Time { get; init; }
}

/// <summary>An account status change (activation, takedown, deactivation) for a repo.</summary>
public sealed class JetstreamAccountEvent : JetstreamEvent
{
    /// <summary>Whether the account is active.</summary>
    public required bool Active { get; init; }

    /// <summary>Status detail when inactive (e.g., "takendown", "deactivated"), if present.</summary>
    public string? Status { get; init; }

    /// <summary>The firehose sequence number of the underlying account event, if present.</summary>
    public long? Seq { get; init; }

    /// <summary>The account event timestamp (ISO 8601), if present.</summary>
    public string? Time { get; init; }
}

/// <summary>
/// A repo resynchronization marker: the account's commit chain could not be followed, and a
/// consumer holding derived state should re-read the repository rather than trust what it has.
/// </summary>
/// <remarks>
/// <see cref="JetstreamProtocol.V2"/> only — the frozen v1 wire never emits these. When
/// folding a stream into a store, treat one of these the way you would treat an account
/// deletion for that DID: drop the account's records and re-read them from its PDS.
/// </remarks>
public sealed class JetstreamSyncEvent : JetstreamEvent
{
    /// <summary>The repo revision (TID) the account is being resynchronized to, if present.</summary>
    public string? Rev { get; init; }

    /// <summary>
    /// The CAR file carrying the commit block, if present. Decoded from the frame's
    /// <c>$bytes</c> wrapper; the CAR header's first root is the commit block CID.
    /// </summary>
    public byte[]? Blocks { get; init; }

    /// <summary>The firehose sequence number of the underlying sync event, if present.</summary>
    public long? Seq { get; init; }

    /// <summary>The sync event timestamp (ISO 8601), if present.</summary>
    public string? Time { get; init; }
}
