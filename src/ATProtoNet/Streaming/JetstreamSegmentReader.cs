using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Repo;
using DidValue = ATProtoNet.Identity.Did;

namespace ATProtoNet.Streaming;

/// <summary>
/// The kind discriminator stored in a segment row, as written on disk.
/// </summary>
/// <remarks>
/// Finer-grained than <see cref="JetstreamEventKind"/>: the three commit operations are separate
/// values, and <see cref="CreateResync"/> is a create written while replacing a repo's records
/// after a resync. All four project to <see cref="JetstreamCommitEvent"/>.
/// </remarks>
public enum JetstreamArchiveRowKind : byte
{
    /// <summary>A record was created (commit, <c>create</c>).</summary>
    Create = 1,

    /// <summary>A record was updated (commit, <c>update</c>).</summary>
    Update = 2,

    /// <summary>A record was deleted (commit, <c>delete</c>).</summary>
    Delete = 3,

    /// <summary>An identity change.</summary>
    Identity = 4,

    /// <summary>An account status change.</summary>
    Account = 5,

    /// <summary>A repo resynchronization marker.</summary>
    Sync = 6,

    /// <summary>A record materialized while replacing a repo after a resync; delivered as a create.</summary>
    CreateResync = 7,
}

/// <summary>
/// One decoded row of a segment block, before projection to a <see cref="JetstreamEvent"/>.
/// </summary>
/// <remarks>
/// This is the archive's own shape: the raw column values, including the untouched
/// <see cref="Payload"/> CBOR. It is exposed for mirrors and auditors that want the bytes the
/// network published rather than the JSON projection — the archive keeps records as CBOR
/// precisely so a mirror stays byte-auditable. Consumers that just want events can use
/// <see cref="ToEvent"/>, or the event-level readers on <see cref="JetstreamSegmentReader"/>.
/// </remarks>
public sealed class JetstreamArchiveRow
{
    /// <summary>Jetstream's monotonic sequence number for this event — the v2 cursor.</summary>
    public required long Seq { get; init; }

    /// <summary>When Jetstream first saw the event, unix microseconds. Monotonic with <see cref="Seq"/>.</summary>
    public required long WitnessedAt { get; init; }

    /// <summary>
    /// An operator-imported display timestamp in unix microseconds, or <c>0</c> when there is
    /// none. <see cref="TimeUs"/> applies the fallback.
    /// </summary>
    public required long IndexedAt { get; init; }

    /// <summary>The row's kind discriminator.</summary>
    public required JetstreamArchiveRowKind Kind { get; init; }

    /// <summary>The repo (account) DID this row concerns.</summary>
    public required string Did { get; init; }

    /// <summary>The collection NSID; empty for non-commit rows.</summary>
    public required string Collection { get; init; }

    /// <summary>The record key; empty for non-commit rows.</summary>
    public required string RKey { get; init; }

    /// <summary>The repo revision (TID); empty when the row carries none.</summary>
    public required string Rev { get; init; }

    /// <summary>
    /// The raw payload: DAG-CBOR record bytes for a create/update, the CBOR
    /// <c>com.atproto.sync.subscribeRepos</c> frame body for identity, account, and sync rows,
    /// and empty for a delete.
    /// </summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>
    /// The timestamp shown to subscribers as <c>time_us</c>: <see cref="IndexedAt"/> when an
    /// operator imported one, otherwise <see cref="WitnessedAt"/>.
    /// </summary>
    public long TimeUs => IndexedAt != 0 ? IndexedAt : WitnessedAt;

    /// <summary>
    /// Project this row to the same event model the live tail delivers.
    /// </summary>
    /// <returns>The event, or null when the row cannot be projected — an unknown kind, an
    /// unparseable DID, or a payload that is not the CBOR the kind requires. Malformed rows are
    /// skipped rather than thrown on, matching the live parser's forward tolerance.</returns>
    public JetstreamEvent? ToEvent()
    {
        DidValue did;
        try
        {
            did = DidValue.Parse(Did);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }

        return Kind switch
        {
            JetstreamArchiveRowKind.Create or JetstreamArchiveRowKind.CreateResync
                => Commit(did, JetstreamOperation.Create),
            JetstreamArchiveRowKind.Update => Commit(did, JetstreamOperation.Update),
            JetstreamArchiveRowKind.Delete => Commit(did, JetstreamOperation.Delete),
            JetstreamArchiveRowKind.Identity => Identity(did),
            JetstreamArchiveRowKind.Account => Account(did),
            JetstreamArchiveRowKind.Sync => Sync(did),
            _ => null,
        };
    }

    private JetstreamCommitEvent Commit(DidValue did, JetstreamOperation operation)
    {
        JsonElement? record = null;
        Cid? cid = null;

        // A delete carries no record; anything else materializes one, and its CID is the
        // DAG-CBOR hash of exactly these bytes — the archive stores no separate CID column.
        if (operation != JetstreamOperation.Delete && !Payload.IsEmpty)
        {
            try
            {
                record = DagCborDecoder.Decode(Payload);
                cid = CidComputation.ComputeForDagCbor(Payload.Span);
            }
            catch (Exception ex) when (ex is System.Formats.Cbor.CborContentException or InvalidOperationException
                                           or ArgumentException or FormatException or NotSupportedException)
            {
                record = null;
                cid = null;
            }
        }

        return new JetstreamCommitEvent
        {
            Did = did,
            TimeUs = TimeUs,
            Cursor = Seq,
            Collection = Collection,
            RKey = RKey,
            Operation = operation,
            Rev = string.IsNullOrEmpty(Rev) ? null : Rev,
            Cid = cid,
            Record = record,
        };
    }

    private JetstreamIdentityEvent Identity(DidValue did)
    {
        var frame = DecodePayload();
        return new JetstreamIdentityEvent
        {
            Did = did,
            TimeUs = TimeUs,
            Cursor = Seq,
            Handle = GetString(frame, "handle"),
            Seq = GetInt64(frame, "seq"),
            Time = GetString(frame, "time"),
        };
    }

    private JetstreamAccountEvent? Account(DidValue did)
    {
        var frame = DecodePayload();
        if (frame is not { } account
            || !account.TryGetProperty("active", out var active)
            || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return null;

        return new JetstreamAccountEvent
        {
            Did = did,
            TimeUs = TimeUs,
            Cursor = Seq,
            Active = active.GetBoolean(),
            Status = GetString(frame, "status"),
            Seq = GetInt64(frame, "seq"),
            Time = GetString(frame, "time"),
        };
    }

    private JetstreamSyncEvent Sync(DidValue did)
    {
        var frame = DecodePayload();

        byte[]? blocks = null;
        // DagCborDecoder renders a CBOR byte string as { "$bytes": "<base64>" }, matching the
        // JSON shape the live wire delivers the same field in.
        if (frame is { } sync
            && sync.TryGetProperty("blocks", out var blocksProp)
            && blocksProp.ValueKind == JsonValueKind.Object
            && GetString(blocksProp, "$bytes") is { } base64)
        {
            try
            {
                blocks = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                // Tolerate an undecodable CAR — the DID and rev are still actionable
            }
        }

        return new JetstreamSyncEvent
        {
            Did = did,
            TimeUs = TimeUs,
            Cursor = Seq,
            Rev = GetString(frame, "rev") ?? (string.IsNullOrEmpty(Rev) ? null : Rev),
            Blocks = blocks,
            Seq = GetInt64(frame, "seq"),
            Time = GetString(frame, "time"),
        };
    }

    /// <summary>Decode a non-commit payload — a CBOR <c>subscribeRepos</c> frame body — to JSON.</summary>
    private JsonElement? DecodePayload()
    {
        if (Payload.IsEmpty)
            return null;

        try
        {
            var element = DagCborDecoder.Decode(Payload);
            return element.ValueKind == JsonValueKind.Object ? element : null;
        }
        catch (Exception ex) when (ex is System.Formats.Cbor.CborContentException or InvalidOperationException
                                       or ArgumentException or FormatException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement? element, string name)
        => element is { } value && value.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static long? GetInt64(JsonElement? element, string name)
        => element is { } value && value.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var number)
            ? number
            : null;
}

/// <summary>
/// The 256-byte fixed header of a sealed Jetstream segment (<c>.jss</c>).
/// </summary>
public sealed class JetstreamSegmentHeader
{
    /// <summary>The size of the fixed header, in bytes. Blocks start immediately after it.</summary>
    public const int Size = 256;

    /// <summary>The four magic bytes a segment file starts with.</summary>
    public static ReadOnlySpan<byte> Magic => "jss0"u8;

    /// <summary>
    /// The segment's xxh3 metadata checksum, which is also its <c>getSegment</c> ETag and the
    /// <c>checksum</c> reported by <c>listSegments</c>. Zero marks an <i>active</i> (still
    /// appending) segment, which has no footer and cannot be read as sealed.
    /// </summary>
    public required ulong Checksum { get; init; }

    /// <summary>The segment format version.</summary>
    public required ushort Version { get; init; }

    /// <summary>Number of blocks in the segment.</summary>
    public required uint BlockCount { get; init; }

    /// <summary>Number of events in the segment.</summary>
    public required uint EventCount { get; init; }

    /// <summary>Number of distinct DIDs in the segment.</summary>
    public required uint UniqueDidCount { get; init; }

    /// <summary>Lowest sequence number in the segment.</summary>
    public required ulong MinSeq { get; init; }

    /// <summary>Highest sequence number in the segment.</summary>
    public required ulong MaxSeq { get; init; }

    /// <summary>Earliest witnessed-at in the segment, unix microseconds.</summary>
    public required long MinWitnessedAt { get; init; }

    /// <summary>Latest witnessed-at in the segment, unix microseconds.</summary>
    public required long MaxWitnessedAt { get; init; }

    /// <summary>Byte offset where the footer begins — that is, one past the last block.</summary>
    public required ulong FooterOffset { get; init; }

    /// <summary>Byte offset of the segment-wide DID bloom filter.</summary>
    public required ulong DidBloomOffset { get; init; }

    /// <summary>Byte offset of the per-block DID bloom filters.</summary>
    public required ulong BlockDidBloomOffset { get; init; }

    /// <summary>Byte offset of the collection index.</summary>
    public required ulong CollectionIndexOffset { get; init; }

    /// <summary>Byte offset of the block index — the start of the footer.</summary>
    public required ulong BlockIndexOffset { get; init; }
}

/// <summary>
/// Decoder for Jetstream sealed segments (<c>.jss</c>) — the columnar binary format the v2
/// archive is stored and served in.
/// </summary>
/// <remarks>
/// <para>A segment is a 256-byte header, a run of length-prefixed zstd frames (~4096 events each,
/// operator-configurable), and a variable-length footer holding the block index, DID bloom
/// filters, and the collection index. This reader walks the blocks in order, which needs neither
/// the footer nor a seekable stream — segments are hundreds of megabytes, so events are streamed
/// one block at a time rather than materialized whole.</para>
/// <para>zstd is not bundled with the SDK: supply an <see cref="IJetstreamBlockDecompressor"/>.
/// Block frames carry zstd content checksums, so a corrupted frame fails in the decompressor.
/// The header's own xxh3 checksum is exposed as
/// <see cref="JetstreamSegmentHeader.Checksum"/> for comparison against
/// <see cref="JetstreamSegmentInfo.Checksum"/> (they are the same value, and the ETag), but is not
/// recomputed here — the SDK ships no xxhash implementation either.</para>
/// <para>The archive is <b>folded, not filtered</b>: every matching event is delivered at least
/// once in sequence order, including creates a later delete supersedes. Fold the stream into
/// idempotent writes keyed on the record's <c>at://</c> URI.</para>
/// </remarks>
/// <example>
/// <code>
/// await using var file = File.OpenRead("seg_000000002a.jss");
/// await foreach (var evt in JetstreamSegmentReader.ReadEventsAsync(file, decompressor))
/// {
///     if (evt is JetstreamCommitEvent commit)
///         Console.WriteLine($"{commit.Cursor} {commit.Operation} {commit.Uri}");
/// }
/// </code>
/// </example>
public static class JetstreamSegmentReader
{
    /// <summary>Bytes of fixed-width column data per event: seq, timestamps, kind, lengths.</summary>
    private const int FixedColumnBytesPerEvent = 8 + 8 + 8 + 1 + 1 + 2 + 1 + 1 + 4;

    /// <summary>
    /// A sanity ceiling on a block's event count, well above the ~4096 events a writer emits.
    /// Guards a corrupt or hostile length prefix from driving a huge allocation.
    /// </summary>
    private const int MaxEventsPerBlock = 1 << 22;

    /// <summary>A sanity ceiling on one stored block frame (256 MiB), for the same reason.</summary>
    private const long MaxBlockFrameBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Parse the 256-byte fixed header of a sealed segment.
    /// </summary>
    /// <param name="header">At least <see cref="JetstreamSegmentHeader.Size"/> bytes from the
    /// start of the file.</param>
    /// <exception cref="JetstreamArchiveException">The bytes are not a sealed segment header.</exception>
    public static JetstreamSegmentHeader ReadHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < JetstreamSegmentHeader.Size)
            throw new JetstreamArchiveException(
                $"A segment header is {JetstreamSegmentHeader.Size} bytes; got {header.Length}.");

        if (!header[..4].SequenceEqual(JetstreamSegmentHeader.Magic))
            throw new JetstreamArchiveException(
                "Not a Jetstream segment: the file does not start with the 'jss0' magic.");

        var checksum = BinaryPrimitives.ReadUInt64LittleEndian(header[4..]);
        if (checksum == 0)
            throw new JetstreamArchiveException(
                "This segment is active, not sealed: its checksum is zero and it has no footer. " +
                "Only sealed segments are served by getSegment.");

        var parsed = new JetstreamSegmentHeader
        {
            Checksum = checksum,
            Version = BinaryPrimitives.ReadUInt16LittleEndian(header[12..]),
            BlockCount = BinaryPrimitives.ReadUInt32LittleEndian(header[14..]),
            EventCount = BinaryPrimitives.ReadUInt32LittleEndian(header[18..]),
            UniqueDidCount = BinaryPrimitives.ReadUInt32LittleEndian(header[22..]),
            MinSeq = BinaryPrimitives.ReadUInt64LittleEndian(header[26..]),
            MaxSeq = BinaryPrimitives.ReadUInt64LittleEndian(header[34..]),
            MinWitnessedAt = BinaryPrimitives.ReadInt64LittleEndian(header[42..]),
            MaxWitnessedAt = BinaryPrimitives.ReadInt64LittleEndian(header[50..]),
            FooterOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[58..]),
            DidBloomOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[66..]),
            BlockDidBloomOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[74..]),
            CollectionIndexOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[82..]),
            BlockIndexOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[90..]),
        };

        if (parsed.FooterOffset < JetstreamSegmentHeader.Size)
            throw new JetstreamArchiveException(
                $"Segment footer offset {parsed.FooterOffset} overlaps the fixed header.");

        return parsed;
    }

    /// <summary>
    /// Decode one stored block frame — the raw zstd bytes without the segment's 8-byte length
    /// prefix, exactly what <c>network.bsky.jetstream.getBlock</c> returns.
    /// </summary>
    /// <param name="frame">The compressed block frame.</param>
    /// <param name="decompressor">The zstd decompressor to inflate it with.</param>
    /// <exception cref="JetstreamArchiveException">The frame could not be decompressed or decoded.</exception>
    public static IReadOnlyList<JetstreamArchiveRow> DecodeBlockFrame(
        ReadOnlySpan<byte> frame,
        IJetstreamBlockDecompressor decompressor)
    {
        ArgumentNullException.ThrowIfNull(decompressor);

        byte[] block;
        try
        {
            block = decompressor.Decompress(frame);
        }
        catch (Exception ex) when (ex is not JetstreamArchiveException and not OperationCanceledException)
        {
            throw new JetstreamArchiveException(
                $"Could not decompress a {frame.Length}-byte segment block: {ex.Message}",
                innerException: ex);
        }

        return DecodeBlock(block);
    }

    /// <summary>
    /// Decode an already-decompressed block body: an event count followed by the fixed-width
    /// columns and then the concatenated variable-length ones.
    /// </summary>
    /// <param name="block">The decompressed block body.</param>
    /// <exception cref="JetstreamArchiveException">The block is truncated or its columns do not
    /// add up.</exception>
    public static IReadOnlyList<JetstreamArchiveRow> DecodeBlock(ReadOnlySpan<byte> block)
    {
        if (block.Length < 4)
            throw new JetstreamArchiveException(
                $"A segment block is at least 4 bytes; got {block.Length}.");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(block);
        if (count == 0)
            return [];
        if (count > MaxEventsPerBlock)
            throw new JetstreamArchiveException(
                $"Segment block claims {count} events, above the {MaxEventsPerBlock} ceiling.");

        var n = (int)count;
        var fixedBytes = 4L + (long)n * FixedColumnBytesPerEvent;
        if (block.Length < fixedBytes)
            throw new JetstreamArchiveException(
                $"Segment block is truncated: {n} events need {fixedBytes} bytes of columns, " +
                $"the block has {block.Length}.");

        // Column offsets, in the on-disk order of §3.2.
        var seq = 4;
        var witnessedAt = seq + (n * 8);
        var indexedAt = witnessedAt + (n * 8);
        var kind = indexedAt + (n * 8);
        var collectionLen = kind + n;
        var didLen = collectionLen + n;
        var rkeyLen = didLen + (n * 2);
        var revLen = rkeyLen + n;
        var eventLen = revLen + n;

        // Variable-length columns follow, concatenated in the same order as their length columns.
        long collectionsTotal = 0, didsTotal = 0, rkeysTotal = 0, revsTotal = 0, payloadsTotal = 0;
        for (var i = 0; i < n; i++)
        {
            collectionsTotal += block[collectionLen + i];
            didsTotal += BinaryPrimitives.ReadUInt16LittleEndian(block[(didLen + (i * 2))..]);
            rkeysTotal += block[rkeyLen + i];
            revsTotal += block[revLen + i];
            payloadsTotal += BinaryPrimitives.ReadUInt32LittleEndian(block[(eventLen + (i * 4))..]);
        }

        var collectionsStart = fixedBytes;
        var didsStart = collectionsStart + collectionsTotal;
        var rkeysStart = didsStart + didsTotal;
        var revsStart = rkeysStart + rkeysTotal;
        var payloadsStart = revsStart + revsTotal;
        var totalBytes = payloadsStart + payloadsTotal;

        if (block.Length < totalBytes)
            throw new JetstreamArchiveException(
                $"Segment block is truncated: its columns need {totalBytes} bytes, " +
                $"the block has {block.Length}.");

        var rows = new JetstreamArchiveRow[n];
        int collectionAt = (int)collectionsStart, didAt = (int)didsStart, rkeyAt = (int)rkeysStart,
            revAt = (int)revsStart, payloadAt = (int)payloadsStart;

        for (var i = 0; i < n; i++)
        {
            int collectionSize = block[collectionLen + i];
            int didSize = BinaryPrimitives.ReadUInt16LittleEndian(block[(didLen + (i * 2))..]);
            int rkeySize = block[rkeyLen + i];
            int revSize = block[revLen + i];
            var payloadSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[(eventLen + (i * 4))..]);

            rows[i] = new JetstreamArchiveRow
            {
                Seq = (long)BinaryPrimitives.ReadUInt64LittleEndian(block[(seq + (i * 8))..]),
                WitnessedAt = BinaryPrimitives.ReadInt64LittleEndian(block[(witnessedAt + (i * 8))..]),
                IndexedAt = BinaryPrimitives.ReadInt64LittleEndian(block[(indexedAt + (i * 8))..]),
                Kind = (JetstreamArchiveRowKind)block[kind + i],
                Collection = Utf8(block.Slice(collectionAt, collectionSize)),
                Did = Utf8(block.Slice(didAt, didSize)),
                RKey = Utf8(block.Slice(rkeyAt, rkeySize)),
                Rev = Utf8(block.Slice(revAt, revSize)),
                // Copied, not aliased: the caller outlives the decompression buffer.
                Payload = block.Slice(payloadAt, payloadSize).ToArray(),
            };

            collectionAt += collectionSize;
            didAt += didSize;
            rkeyAt += rkeySize;
            revAt += revSize;
            payloadAt += payloadSize;
        }

        return rows;

        static string Utf8(ReadOnlySpan<byte> bytes) => bytes.IsEmpty ? string.Empty : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Stream every row of a sealed segment, block by block, in sequence order.
    /// </summary>
    /// <param name="segment">The segment file, positioned at its start. Read sequentially; the
    /// stream need not be seekable.</param>
    /// <param name="decompressor">The zstd decompressor for the block frames.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="JetstreamArchiveException">The segment is not a sealed <c>.jss</c> file,
    /// or is truncated.</exception>
    public static async IAsyncEnumerable<JetstreamArchiveRow> ReadRowsAsync(
        Stream segment,
        IJetstreamBlockDecompressor decompressor,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(decompressor);

        var headerBytes = new byte[JetstreamSegmentHeader.Size];
        await ReadExactlyAsync(segment, headerBytes, "segment header", cancellationToken);
        var header = ReadHeader(headerBytes);

        var offset = (ulong)JetstreamSegmentHeader.Size;
        var lengthPrefix = new byte[8];

        for (uint blockIndex = 0; blockIndex < header.BlockCount; blockIndex++)
        {
            if (offset + 8 > header.FooterOffset)
                throw new JetstreamArchiveException(
                    $"Segment is truncated: block {blockIndex} starts at {offset}, " +
                    $"past the footer at {header.FooterOffset}.");

            await ReadExactlyAsync(segment, lengthPrefix, $"block {blockIndex} length", cancellationToken);
            var frameLength = BinaryPrimitives.ReadUInt64LittleEndian(lengthPrefix);

            if (frameLength > MaxBlockFrameBytes || offset + 8 + frameLength > header.FooterOffset)
                throw new JetstreamArchiveException(
                    $"Segment block {blockIndex} claims {frameLength} bytes, which does not fit " +
                    $"before the footer at {header.FooterOffset}.");

            var frame = new byte[(int)frameLength];
            await ReadExactlyAsync(segment, frame, $"block {blockIndex}", cancellationToken);
            offset += 8 + frameLength;

            foreach (var row in DecodeBlockFrame(frame, decompressor))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
            }
        }
    }

    /// <summary>
    /// Stream a sealed segment's rows already projected to the event model the live tail delivers.
    /// Rows that cannot be projected are skipped.
    /// </summary>
    /// <param name="segment">The segment file, positioned at its start.</param>
    /// <param name="decompressor">The zstd decompressor for the block frames.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async IAsyncEnumerable<JetstreamEvent> ReadEventsAsync(
        Stream segment,
        IJetstreamBlockDecompressor decompressor,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var row in ReadRowsAsync(segment, decompressor, cancellationToken))
        {
            if (row.ToEvent() is { } evt)
                yield return evt;
        }
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        string what,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var got = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (got == 0)
                throw new JetstreamArchiveException(
                    $"Segment ended while reading {what}: expected {buffer.Length} bytes, got {read}.");
            read += got;
        }
    }
}
