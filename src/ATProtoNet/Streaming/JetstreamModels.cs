using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Serialization;

namespace ATProtoNet.Streaming;

/// <summary>
/// Configuration options for <see cref="JetstreamClient"/>, <see cref="JetstreamConsumer"/> and
/// <see cref="JetstreamReplayConsumer"/>.
/// </summary>
/// <remarks>
/// <see cref="StreamConsumerOptions.ServiceUrl"/> is the Jetstream host URL without the endpoint
/// path (e.g., <c>wss://jetstream.us-east.bsky.network</c>); <c>http(s)</c> schemes are converted
/// to <c>ws(s)</c> automatically. See <see cref="JetstreamEndpoints"/> for the public
/// Bluesky-operated instances.
/// </remarks>
public sealed class JetstreamConsumerOptions : StreamConsumerOptions
{
    private const int MaxWantedCollections = 100;
    private const int MaxWantedDids = 10_000;
    private const long MaxMaxMessageSizeBytes = 4_294_967_295;

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
    /// (e.g., "app.bsky.feed.post") and prefix wildcards (e.g., "app.bsky.graph.*"), which is
    /// why the entries are strings rather than <see cref="Nsid"/>s.
    /// Maximum 100 entries. If null or empty, commit events for all collections are delivered.
    /// </summary>
    public IReadOnlyList<string>? WantedCollections { get; init; }

    /// <summary>
    /// DIDs to receive events for. Maximum 10,000 entries.
    /// If null or empty, events for all repos are delivered.
    /// </summary>
    public IReadOnlyList<Did>? WantedDids { get; init; }

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
    /// Optional decompressor for zstd-compressed frames. When set, compressed frames are
    /// requested from the server and binary frames are passed through
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
    /// <see cref="JetstreamArchiveClient.GetZstdDictionaryAsync"/>.</para>
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
    /// <see cref="JetstreamArchiveClient.GetZstdDictionaryAsync"/>. A retired ID is rejected
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

    /// <summary>
    /// Invoked for each advisory <c>#info</c> frame. <see cref="JetstreamProtocol.V2"/> only.
    /// Info frames carry no sequence number and do not advance the cursor, so they are not
    /// delivered as events.
    /// </summary>
    public Action<JetstreamInfo>? OnInfo { get; init; }

    /// <inheritdoc/>
    internal override void Validate()
    {
        base.Validate();

        if (WantedCollections is { Count: > MaxWantedCollections })
            throw new ArgumentException(
                $"Jetstream accepts at most {MaxWantedCollections} wantedCollections entries " +
                $"(got {WantedCollections.Count}).");
        if (WantedDids is { Count: > MaxWantedDids })
            throw new ArgumentException(
                $"Jetstream accepts at most {MaxWantedDids} wantedDids entries (got {WantedDids.Count}).");
        if (MaxMessageSizeBytes is < 0 or > MaxMaxMessageSizeBytes)
            throw new ArgumentException(
                $"MaxMessageSizeBytes must be between 0 and {MaxMaxMessageSizeBytes} (got {MaxMessageSizeBytes}).");

        if (Protocol != JetstreamProtocol.V2)
        {
            if (WantedKinds is { Count: > 0 })
                throw new ArgumentException(
                    "WantedKinds requires JetstreamProtocol.V2; the v1 wire has no kinds filter.");
            if (ZstdDictionaryId is not null)
                throw new ArgumentException(
                    "ZstdDictionaryId requires JetstreamProtocol.V2; the v1 wire negotiates " +
                    "compression with compress=true and an unversioned dictionary.");
            return;
        }

        // The server rejects this pre-upgrade, since a collection filter that can never match a
        // delivered kind is always a mistake. Fail before opening the socket.
        if (WantedCollections is { Count: > 0 }
            && WantedKinds is { Count: > 0 } kinds
            && !kinds.Contains(JetstreamEventKind.Commit))
            throw new ArgumentException(
                "WantedCollections only constrains commit events, so it cannot be combined " +
                "with a WantedKinds list that excludes JetstreamEventKind.Commit.");
        if (Decompressor is not null && ZstdDictionaryId is null)
            throw new ArgumentException(
                "JetstreamProtocol.V2 compression is dictionary-versioned: set ZstdDictionaryId " +
                "to the id of the dictionary the decompressor was built with " +
                $"(see {nameof(JetstreamArchiveClient)}.{nameof(JetstreamArchiveClient.GetZstdDictionaryAsync)}).");
        if (ZstdDictionaryId is not null && Decompressor is null)
            throw new ArgumentException(
                "ZstdDictionaryId makes the server send binary zstd frames, so it requires " +
                "a Decompressor to read them.");
    }
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

/// <summary>The wire names of <see cref="JetstreamEventKind"/>, shared by the live and archive filters.</summary>
internal static class JetstreamKinds
{
    /// <summary>The wire name of an event kind — the <c>$type</c> fragment the server filters on.</summary>
    public static string Name(JetstreamEventKind kind) => kind switch
    {
        JetstreamEventKind.Commit => "commit",
        JetstreamEventKind.Identity => "identity",
        JetstreamEventKind.Account => "account",
        JetstreamEventKind.Sync => "sync",
        _ => throw new ArgumentException($"Unknown Jetstream event kind: {kind}", nameof(kind)),
    };
}

/// <summary>
/// Jetstream cursors: sequence numbers, and the unix-microseconds timestamps v1 used, which v2
/// still accepts as a seek position.
/// </summary>
/// <remarks>
/// <para>Jetstream splits the two at 10^15: a cursor below it is a sequence number, a cursor at or
/// above it a unix-microseconds timestamp (any time after September 2001). On
/// <see cref="JetstreamProtocol.V2"/> a timestamp cursor seeks to the first retained event
/// witnessed at or after that time; the consumer then resumes by sequence number, so a stored v1
/// cursor migrates to v2 by itself.</para>
/// <para>A timestamp seek is at-least-once: it may redeliver events around the boundary, and they
/// cannot be deduplicated by <see cref="JetstreamEvent.TimeUs"/>, which can be an imported display
/// time rather than the time the server seeks by. Never build a cursor near the boundary by hand.</para>
/// </remarks>
public static class JetstreamCursor
{
    /// <summary>The smallest timestamp cursor: 10^15 unix microseconds. Smaller values are sequence numbers.</summary>
    public const long TimestampThreshold = 1_000_000_000_000_000;

    /// <summary>
    /// A cursor that seeks to <paramref name="time"/>: on <see cref="JetstreamProtocol.V2"/> the
    /// first retained event witnessed at or after it, on <see cref="JetstreamProtocol.V1"/> its
    /// native cursor.
    /// </summary>
    /// <param name="time">The time to start from.</param>
    /// <returns>The time in unix microseconds.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="time"/> is before 2001-09-09,
    /// whose microseconds would read as a sequence number.</exception>
    public static long FromTimestamp(DateTimeOffset time)
    {
        var micros = (time.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10;
        if (micros < TimestampThreshold)
            throw new ArgumentOutOfRangeException(nameof(time), time,
                "A timestamp cursor must be at or after 2001-09-09T01:46:40Z; earlier ones read as sequence numbers.");
        return micros;
    }

    /// <summary>Whether <paramref name="cursor"/> is a unix-microseconds timestamp rather than a sequence number.</summary>
    /// <param name="cursor">The cursor.</param>
    public static bool IsTimestamp(long cursor) => cursor >= TimestampThreshold;
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
/// The outcome of parsing one Jetstream frame: at most one of an event, an advisory notice,
/// or a terminal error. All three are null for a frame that could not be understood, which
/// callers should skip.
/// </summary>
/// <param name="Event">The parsed event, if the frame carried one.</param>
/// <param name="Info">The advisory notice, if the frame was an <c>#info</c>.</param>
/// <param name="Error">The terminal error, if the frame was a v2 error envelope.</param>
public readonly record struct JetstreamFrame(
    JetstreamEvent? Event,
    JetstreamInfo? Info,
    EventStreamError? Error);

/// <summary>
/// Thrown when Jetstream refuses a subscription, an archive or dictionary request fails, a
/// segment cannot be decoded, or the live stream ends with an error frame.
/// </summary>
/// <remarks>
/// <para>The v2 endpoint validates a subscription before the WebSocket upgrade and answers with
/// an XRPC error: <c>CursorTooOld</c> when the requested sequence number is below the retention
/// floor, <c>UnknownZstdDictionary</c> for a retired dictionary ID, and <c>InvalidRequest</c>
/// for a malformed filter. None of those become valid by retrying the same request, so
/// <see cref="JetstreamConsumer"/> rethrows rather than reconnecting — a backfilling consumer
/// is meant to re-enter its backfill from the last sequence number it durably processed.</para>
/// <para>The metered archive endpoints answer a missing or revoked key with <c>401</c>
/// (<c>invalid bearer credential</c>) and an exhausted byte quota with <c>429</c>
/// (<c>byte limit exceeded</c>) plus a <c>Retry-After</c> header;
/// <see cref="JetstreamArchiveClient"/> waits out a <c>429</c> before it gives up.</para>
/// </remarks>
public sealed class JetstreamException : EventStreamException
{
    /// <summary>Create a Jetstream exception.</summary>
    /// <param name="message">The error description.</param>
    /// <param name="statusCode">The HTTP status the server answered with, if any.</param>
    /// <param name="error">The XRPC error name from the response body or error frame, if any.</param>
    /// <param name="retryAfter">The <c>Retry-After</c> the response asked for, if any.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public JetstreamException(
        string message,
        int? statusCode = null,
        string? error = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, error, statusCode, innerException)
        => RetryAfter = retryAfter;

    /// <summary>How long the server asked the client to wait, if it sent <c>Retry-After</c>.</summary>
    public TimeSpan? RetryAfter { get; }
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
public sealed class JetstreamCommitEvent : JetstreamEvent, IRecordEvent
{
    private AtUri? _uri;

    /// <summary>The collection NSID (e.g., "app.bsky.feed.post").</summary>
    public required Nsid Collection { get; init; }

    /// <summary>The record key within the collection.</summary>
    public required RecordKey Rkey { get; init; }

    /// <summary>The repository operation.</summary>
    public required RepoOpAction Operation { get; init; }

    /// <summary>The repo revision of the commit, if present.</summary>
    public Tid? Rev { get; init; }

    /// <summary>The record CID. Null for delete operations.</summary>
    public Cid? Cid { get; init; }

    /// <summary>
    /// The record body as raw JSON. Null for delete operations.
    /// Use <see cref="GetRecord{T}"/> for typed deserialization.
    /// </summary>
    public JsonElement? Record { get; init; }

    /// <summary>The <c>at://</c> URI of the affected record.</summary>
    public AtUri Uri => _uri ??= AtUri.Create(AtIdentifier.FromDid(Did), Collection, Rkey);

    /// <summary>
    /// Deserialize the record body as <typeparamref name="T"/> using
    /// <see cref="AtProtoJsonDefaults.Options"/>, including union variants registered in
    /// <see cref="LexiconTypeRegistry"/>. Returns null for delete operations.
    /// </summary>
    public T? GetRecord<T>() where T : class
        => Record is { } record
            ? record.Deserialize<T>(AtProtoJsonDefaults.Options)
            : null;
}

/// <summary>An identity change (e.g., handle update) for a repo.</summary>
public sealed class JetstreamIdentityEvent : JetstreamEvent
{
    /// <summary>The new handle, if provided.</summary>
    public Handle? Handle { get; init; }

    /// <summary>The firehose sequence number of the underlying identity event, if present.</summary>
    public long? Seq { get; init; }

    /// <summary>The identity event timestamp, if present.</summary>
    public AtDatetime? Time { get; init; }
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

    /// <summary>The account event timestamp, if present.</summary>
    public AtDatetime? Time { get; init; }
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
    /// <summary>The repo revision the account is being resynchronized to, if present.</summary>
    public Tid? Rev { get; init; }

    /// <summary>
    /// The CAR file carrying the commit block, if present. Decoded from the frame's
    /// <c>$bytes</c> wrapper; the CAR header's first root is the commit block CID.
    /// </summary>
    public byte[]? Blocks { get; init; }

    /// <summary>The firehose sequence number of the underlying sync event, if present.</summary>
    public long? Seq { get; init; }

    /// <summary>The sync event timestamp, if present.</summary>
    public AtDatetime? Time { get; init; }
}
