using System.Text.Json.Serialization;

namespace ATProtoNet.Streaming;

/// <summary>
/// Decompresses a zstd frame stored inside a Jetstream sealed segment (<c>.jss</c>).
/// </summary>
/// <remarks>
/// <para>Segment blocks — and the segment's collection index — are stored as plain zstd frames
/// with content checksums enabled and <b>no dictionary</b>, unlike the dictionary-compressed
/// WebSocket frames <see cref="IJetstreamDecompressor"/> handles. The two are not
/// interchangeable: a decompressor with the Jetstream dictionary loaded cannot read a segment
/// block.</para>
/// <para>The SDK ships no zstd implementation, so archive reading requires supplying one —
/// see the Jetstream documentation page for a <c>ZstdSharp.Port</c> implementation.</para>
/// </remarks>
public interface IJetstreamBlockDecompressor
{
    /// <summary>Decompress one stored zstd frame.</summary>
    /// <param name="frame">The compressed frame, without the segment's 8-byte length prefix —
    /// exactly what <c>network.bsky.jetstream.getBlock</c> returns.</param>
    /// <returns>The decompressed block body.</returns>
    byte[] Decompress(ReadOnlySpan<byte> frame);
}

/// <summary>
/// Configuration for the Jetstream v2 archive: the HTTP replay endpoints and the
/// <c>.jss</c> segment decoder behind <see cref="JetstreamReplayConsumer"/>.
/// </summary>
/// <remarks>
/// Set on <see cref="JetstreamConsumerOptions.Archive"/>, so one options object configures both
/// the backfill and the live tail it cuts over into — the filters
/// (<see cref="JetstreamConsumerOptions.WantedCollections"/>,
/// <see cref="JetstreamConsumerOptions.WantedDids"/>,
/// <see cref="JetstreamConsumerOptions.WantedKinds"/>) are shared by both phases rather than
/// declared twice.
/// </remarks>
public sealed class JetstreamArchiveOptions
{
    /// <summary>
    /// The API key sent as <c>Authorization: Bearer</c> on every archive HTTP request.
    /// Required on the Bluesky-hosted instances, where the HTTP endpoints are metered;
    /// a self-hosted instance may serve them unauthenticated.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Decompressor for the zstd frames inside a segment. Required — the SDK bundles no zstd.
    /// </summary>
    public required IJetstreamBlockDecompressor BlockDecompressor { get; init; }

    /// <summary>
    /// Start the backfill after this sequence number; events at or below it are not delivered.
    /// When null, a cursor from <see cref="JetstreamConsumerOptions.CursorStore"/> is used,
    /// and failing that the replay starts at the beginning of the archive.
    /// </summary>
    public long? AfterSeq { get; init; }

    /// <summary>
    /// Stop the backfill at this sequence number; events above it are not delivered.
    /// Implies <see cref="SnapshotOnly"/> semantics for the upper bound — there is nothing to
    /// cut over into above a fixed ceiling, so setting it with
    /// <see cref="SnapshotOnly"/> <c>= false</c> is rejected.
    /// </summary>
    public long? BeforeSeq { get; init; }

    /// <summary>
    /// Stop when the archive is exhausted instead of cutting over to the live tail.
    /// Default: false (replay mode).
    /// </summary>
    public bool SnapshotOnly { get; init; }

    /// <summary>
    /// How many segment/block downloads to run concurrently. Downloads are prefetched ahead of
    /// the decoder but decoded and delivered strictly in sequence order. Default: 4.
    /// </summary>
    public int DownloadParallelism { get; init; } = 4;

    /// <summary>
    /// Directory for the temporary files whole-segment downloads are spooled to. Segments are
    /// large (hundreds of MB), so they are never held in memory. Defaults to the system
    /// temporary directory. Spool files are deleted when closed, including on a crash-free abort.
    /// </summary>
    public string? SpoolDirectory { get; init; }

    /// <summary>
    /// How many times a metered or transient HTTP failure is retried before giving up.
    /// A <c>429</c> waits out the response's <c>Retry-After</c>; other transient failures back
    /// off exponentially. Default: 5.
    /// </summary>
    public int MaxRetryAttempts { get; init; } = 5;

    /// <summary>Ceiling on a single retry delay, including one asked for by <c>Retry-After</c>.
    /// Default: 5 minutes.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How many times the replay may re-enter the plan loop because the pinned tip aged out of
    /// the live socket's lookback window before the cutover completed. Default: 3.
    /// </summary>
    public int MaxCutoverAttempts { get; init; } = 3;

    /// <summary>
    /// How many times a plan page that does not advance past the previous one — the sealed tip
    /// reported ahead of the segments that carry it — is re-planned before the backfill fails with
    /// a <see cref="JetstreamArchiveException"/>. Each attempt backs off exponentially from a
    /// second, capped by <see cref="MaxRetryDelay"/>. Failing is deliberate: giving up quietly
    /// below the pinned tip would leave a permanent gap, because the cutover reconnects at that tip
    /// and never redelivers the skipped range. Default: 5.
    /// </summary>
    public int MaxStalledPlanAttempts { get; init; } = 5;

    /// <summary>
    /// The archive host, when it differs from <see cref="JetstreamConsumerOptions.ServiceUrl"/>
    /// (e.g. a CDN mirror in front of the segment files). <c>ws(s)</c> schemes are converted to
    /// <c>http(s)</c> automatically.
    /// </summary>
    public string? ServiceUrl { get; init; }

    /// <summary>
    /// An <see cref="System.Net.Http.HttpClient"/> to send archive requests with. When null, one
    /// is created and disposed with the consumer. Supply a pooled client for large backfills.
    /// </summary>
    public HttpClient? HttpClient { get; init; }
}

/// <summary>How a planned segment should be downloaded.</summary>
public enum JetstreamSegmentDownloadMode
{
    /// <summary>Fetch the whole segment file with <c>getSegment</c>.</summary>
    Segment,

    /// <summary>Fetch only the listed block ranges with <c>getBlock</c>.</summary>
    Blocks,
}

/// <summary>An inclusive range of block indices within a segment.</summary>
/// <param name="First">Index of the first block in the range.</param>
/// <param name="Last">Index of the last block in the range, inclusive.</param>
public sealed record JetstreamBlockRange(
    [property: JsonPropertyName("first")] int First,
    [property: JsonPropertyName("last")] int Last);

/// <summary>A segment the planner selected, and how to download it.</summary>
public sealed class JetstreamPlannedSegment
{
    /// <summary>Segment filename, to pass to <c>getSegment</c> or <c>getBlock</c>.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Zero-based segment index.</summary>
    [JsonPropertyName("index")]
    public int Index { get; init; }

    /// <summary>The segment's xxh3 metadata checksum as 16 hex characters — also its
    /// <c>getSegment</c> ETag. Changes when the server rewrites the segment during compaction.</summary>
    [JsonPropertyName("checksum")]
    public required string Checksum { get; init; }

    /// <summary>Lowest sequence number in the segment.</summary>
    [JsonPropertyName("minSeq")]
    public long MinSeq { get; init; }

    /// <summary>Highest sequence number in the segment.</summary>
    [JsonPropertyName("maxSeq")]
    public long MaxSeq { get; init; }

    /// <summary>The wire value of <c>mode</c>, preserved verbatim for forward compatibility.</summary>
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    /// <summary>Block ranges to download. Present only when <see cref="Mode"/> is <c>blocks</c>.</summary>
    [JsonPropertyName("blocks")]
    public IReadOnlyList<JetstreamBlockRange>? Blocks { get; init; }

    /// <summary>
    /// <see cref="Mode"/> as an enum. An unrecognized mode reads as
    /// <see cref="JetstreamSegmentDownloadMode.Segment"/>, which is always a correct — if less
    /// economical — way to fetch the data.
    /// </summary>
    [JsonIgnore]
    public JetstreamSegmentDownloadMode DownloadMode
        => string.Equals(Mode, "blocks", StringComparison.Ordinal) && Blocks is { Count: > 0 }
            ? JetstreamSegmentDownloadMode.Blocks
            : JetstreamSegmentDownloadMode.Segment;
}

/// <summary>Planner statistics for one <c>planSnapshot</c> page.</summary>
public sealed class JetstreamPlanStats
{
    /// <summary>Segments the planner looked at.</summary>
    [JsonPropertyName("segmentsExamined")]
    public long SegmentsExamined { get; init; }

    /// <summary>Segments that matched the filter.</summary>
    [JsonPropertyName("segmentsMatched")]
    public long SegmentsMatched { get; init; }

    /// <summary>Blocks that matched the filter.</summary>
    [JsonPropertyName("blocksMatched")]
    public long BlocksMatched { get; init; }

    /// <summary>Items counted toward the server's per-page plan limit.</summary>
    [JsonPropertyName("entries")]
    public long Entries { get; init; }
}

/// <summary>One page of a <c>network.bsky.jetstream.planSnapshot</c> response.</summary>
public sealed class JetstreamSnapshotPlan
{
    /// <summary>
    /// The last sealed sequence number this page covers. Pass it as
    /// <see cref="JetstreamSnapshotRequest.AfterSeq"/> to page on; planning is complete once it
    /// reaches <see cref="SealedTipSeq"/>.
    /// </summary>
    [JsonPropertyName("plannedThroughSeq")]
    public long PlannedThroughSeq { get; init; }

    /// <summary>
    /// The end of the sealed archive for this snapshot, capped by
    /// <see cref="JetstreamSnapshotRequest.BeforeSeq"/> when one was given. Pin it and pass it
    /// as <c>beforeSeq</c> on later pages so the window does not float while it is downloaded.
    /// </summary>
    [JsonPropertyName("sealedTipSeq")]
    public long SealedTipSeq { get; init; }

    /// <summary>The segments to download, in ascending sequence order.</summary>
    [JsonPropertyName("segments")]
    public IReadOnlyList<JetstreamPlannedSegment> Segments { get; init; } = [];

    /// <summary>Planner statistics for this page.</summary>
    [JsonPropertyName("stats")]
    public JetstreamPlanStats? Stats { get; init; }
}

/// <summary>A <c>network.bsky.jetstream.planSnapshot</c> request body.</summary>
public sealed class JetstreamSnapshotRequest
{
    /// <summary>Event kinds to include. Null or empty includes every kind.</summary>
    [JsonPropertyName("kinds")]
    public IReadOnlyList<string>? Kinds { get; init; }

    /// <summary>DIDs to include. Null or empty includes every DID. Maximum 10,000 entries.</summary>
    [JsonPropertyName("dids")]
    public IReadOnlyList<string>? Dids { get; init; }

    /// <summary>
    /// Collection NSIDs or namespace wildcards such as <c>app.bsky.feed.*</c>. Constrains commit
    /// events only. Null or empty includes every collection. Maximum 100 entries.
    /// </summary>
    [JsonPropertyName("collections")]
    public IReadOnlyList<string>? Collections { get; init; }

    /// <summary>Start after this sequence number; events at or below it are excluded.</summary>
    [JsonPropertyName("afterSeq")]
    public long? AfterSeq { get; init; }

    /// <summary>Stop at this sequence number; events above it are excluded.</summary>
    [JsonPropertyName("beforeSeq")]
    public long? BeforeSeq { get; init; }
}

/// <summary>A sealed segment file as reported by <c>network.bsky.jetstream.listSegments</c>.</summary>
public sealed class JetstreamSegmentInfo
{
    /// <summary>Segment filename, to pass to <c>getSegment</c>.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Zero-based segment index.</summary>
    [JsonPropertyName("index")]
    public int Index { get; init; }

    /// <summary>File size in bytes.</summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    /// <summary>
    /// The segment-format xxh3 metadata checksum as 16 hex characters; equals the
    /// <c>getSegment</c> ETag. A mirror compares this to detect a segment the server rewrote
    /// during compaction — sealed segments are immutable only <i>between</i> compactions.
    /// </summary>
    [JsonPropertyName("checksum")]
    public required string Checksum { get; init; }

    /// <summary>Number of events in the segment.</summary>
    [JsonPropertyName("eventCount")]
    public long EventCount { get; init; }

    /// <summary>Lowest sequence number in the segment.</summary>
    [JsonPropertyName("minSeq")]
    public long MinSeq { get; init; }

    /// <summary>Highest sequence number in the segment.</summary>
    [JsonPropertyName("maxSeq")]
    public long MaxSeq { get; init; }

    /// <summary>Earliest witnessed-at in the segment, unix microseconds.</summary>
    [JsonPropertyName("minWitnessedAt")]
    public long MinWitnessedAt { get; init; }

    /// <summary>Latest witnessed-at in the segment, unix microseconds.</summary>
    [JsonPropertyName("maxWitnessedAt")]
    public long MaxWitnessedAt { get; init; }
}

/// <summary>One page of a <c>network.bsky.jetstream.listSegments</c> response.</summary>
public sealed class JetstreamSegmentPage
{
    /// <summary>The pagination cursor for the next page, or null at the end of the list.</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The segments in this page, in ascending index order.</summary>
    [JsonPropertyName("segments")]
    public IReadOnlyList<JetstreamSegmentInfo> Segments { get; init; } = [];
}

/// <summary>
/// Thrown when a Jetstream archive HTTP request fails, or a segment cannot be decoded.
/// </summary>
/// <remarks>
/// The metered endpoints answer a missing or revoked key with <c>401</c>
/// (<c>invalid bearer credential</c>) and an exhausted byte quota with <c>429</c>
/// (<c>byte limit exceeded</c>) plus a <c>Retry-After</c> header. Both surface here;
/// <see cref="IsRetryable"/> distinguishes them, and <see cref="JetstreamArchiveClient"/>
/// already waits out a <c>429</c> before it gives up.
/// </remarks>
public sealed class JetstreamArchiveException : Exception
{
    /// <summary>Create an archive exception.</summary>
    /// <param name="message">The error description.</param>
    /// <param name="statusCode">The HTTP status the server answered with, if any.</param>
    /// <param name="error">The XRPC error name from the response body, if any.</param>
    /// <param name="retryAfter">The <c>Retry-After</c> the response asked for, if any.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public JetstreamArchiveException(
        string message,
        int? statusCode = null,
        string? error = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Error = error;
        RetryAfter = retryAfter;
    }

    /// <summary>The HTTP status code, if the failure came from a response.</summary>
    public int? StatusCode { get; }

    /// <summary>The XRPC error name (e.g. <c>SegmentNotFound</c>), if the body carried one.</summary>
    public string? Error { get; }

    /// <summary>How long the server asked the client to wait, if it sent <c>Retry-After</c>.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Whether retrying the same request could succeed: true for a transport fault, a 5xx, and
    /// the metered <c>429</c>; false for the other 4xx statuses, which reject the request itself.
    /// </summary>
    public bool IsRetryable => StatusCode is not (>= 400 and < 500) || StatusCode == 429;
}
