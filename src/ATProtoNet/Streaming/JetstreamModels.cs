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
    /// The Jetstream WebSocket URL, without the <c>/subscribe</c> path
    /// (e.g., "wss://jetstream2.us-east.bsky.network").
    /// <c>http(s)</c> schemes are converted to <c>ws(s)</c> automatically.
    /// </summary>
    public required string ServiceUrl { get; init; }

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
    /// The SDK does not ship a zstd implementation to keep the core package dependency-free.
    /// Jetstream compression uses a custom zstd dictionary; see the Jetstream documentation
    /// page for a ready-to-use implementation based on <c>ZstdSharp.Port</c>.
    /// </remarks>
    public IJetstreamDecompressor? Decompressor { get; init; }

    /// <summary>Optional cursor store for persistent resume across restarts.
    /// The stored cursor is the event's <c>time_us</c> (unix microseconds), not a sequence number.</summary>
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
    public TimeSpan ReconnectRewind { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Base delay between reconnection attempts. Default: 5 seconds.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Max reconnection attempts. Default: 10. Use -1 for unlimited.</summary>
    public int MaxReconnectAttempts { get; init; } = 10;

    /// <summary>Optional logger.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>The resolved stream identifier for cursor storage.</summary>
    internal string ResolvedStreamId => StreamId ?? ServiceUrl;
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
    /// Event timestamp in unix microseconds. This is the value to use as a cursor
    /// when resuming a subscription.
    /// </summary>
    public required long TimeUs { get; init; }
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
