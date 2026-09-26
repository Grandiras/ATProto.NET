using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
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
/// precisely so a mirror stays byte-auditable. The identifier columns are therefore kept as the
/// text the archive stored, unvalidated; <see cref="ToEvent()"/> parses them into the typed event
/// the live tail delivers, and consumers that just want events can use it or the event-level
/// readers on <see cref="JetstreamSegmentReader"/>.
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
    public required string Rkey { get; init; }

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
    /// unparseable DID, collection or record key, or a payload that is not the CBOR the kind
    /// requires. Malformed rows are skipped rather than thrown on, matching the live parser's
    /// forward tolerance.</returns>
    public JetstreamEvent? ToEvent()
        => DidValue.TryParse(Did, out var did) ? ToEvent(did) : null;

    /// <summary>Projects the row with its DID already parsed.</summary>
    internal JetstreamEvent? ToEvent(DidValue did) => Kind switch
    {
        JetstreamArchiveRowKind.Create or JetstreamArchiveRowKind.CreateResync => Commit(did, RepoOpAction.Create),
        JetstreamArchiveRowKind.Update => Commit(did, RepoOpAction.Update),
        JetstreamArchiveRowKind.Delete => Commit(did, RepoOpAction.Delete),
        JetstreamArchiveRowKind.Identity => JetstreamEvents.Identity(DecodePayload(), did, TimeUs, Seq),
        JetstreamArchiveRowKind.Account => DecodePayload() is { } account
            ? JetstreamEvents.Account(account, did, TimeUs, Seq)
            : null,
        JetstreamArchiveRowKind.Sync => JetstreamEvents.Sync(DecodePayload(), did, TimeUs, Seq, fallbackRev: Rev),
        _ => null,
    };

    private JetstreamCommitEvent? Commit(DidValue did, RepoOpAction operation)
    {
        if (!Nsid.TryParse(Collection, out var collection) || !RecordKey.TryParse(Rkey, out var rkey))
            return null;

        JsonElement? record = null;
        Cid? cid = null;

        // A delete carries no record; anything else materializes one, and its CID is the
        // DAG-CBOR hash of exactly these bytes — the archive stores no separate CID column.
        if (operation != RepoOpAction.Delete && !Payload.IsEmpty)
        {
            try
            {
                record = DagCborDecoder.Decode(Payload);
                cid = CidComputation.ComputeForDagCbor(Payload.Span);
            }
            catch (Exception ex) when (EventStreamFrame.IsMalformed(ex) || ex is NotSupportedException)
            {
                record = null;
                cid = null;
            }
        }

        return JetstreamEvents.Commit(
            did, TimeUs, Seq, collection, rkey, operation, Tid.TryParse(Rev, out var rev) ? rev : null, cid, record);
    }

    /// <summary>
    /// Decode a non-commit payload — a CBOR <c>subscribeRepos</c> frame body — to JSON. It has
    /// the same fields the live wire nests under <c>identity</c>, <c>account</c> or <c>sync</c>.
    /// </summary>
    private JsonElement? DecodePayload()
    {
        if (Payload.IsEmpty)
            return null;

        try
        {
            var element = DagCborDecoder.Decode(Payload);
            return element.ValueKind == JsonValueKind.Object ? element : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
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
    /// <exception cref="JetstreamException">The bytes are not a sealed segment header.</exception>
    public static JetstreamSegmentHeader ReadHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < JetstreamSegmentHeader.Size)
            throw new JetstreamException(
                $"A segment header is {JetstreamSegmentHeader.Size} bytes; got {header.Length}.");

        if (!header[..4].SequenceEqual(JetstreamSegmentHeader.Magic))
            throw new JetstreamException(
                "Not a Jetstream segment: the file does not start with the 'jss0' magic.");

        var checksum = BinaryPrimitives.ReadUInt64LittleEndian(header[4..]);
        if (checksum == 0)
            throw new JetstreamException(
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
            throw new JetstreamException(
                $"Segment footer offset {parsed.FooterOffset} overlaps the fixed header.");

        return parsed;
    }

    /// <summary>
    /// Decode one stored block frame — the raw zstd bytes without the segment's 8-byte length
    /// prefix, exactly what <c>network.bsky.jetstream.getBlock</c> returns.
    /// </summary>
    /// <param name="frame">The compressed block frame.</param>
    /// <param name="decompressor">The zstd decompressor to inflate it with.</param>
    /// <exception cref="JetstreamException">The frame could not be decompressed or decoded.</exception>
    public static IReadOnlyList<JetstreamArchiveRow> DecodeBlockFrame(
        ReadOnlySpan<byte> frame,
        IJetstreamBlockDecompressor decompressor)
    {
        var block = Decompress(frame, decompressor);

        // Copied, not aliased: rows handed to a caller may outlive a decompressor that reuses its
        // output buffer.
        return DecodeRows(block, filter: null, copyPayloads: true);
    }

    /// <summary>
    /// Decode an already-decompressed block body: an event count followed by the fixed-width
    /// columns and then the concatenated variable-length ones.
    /// </summary>
    /// <exception cref="JetstreamException">The block is truncated or its columns do not add up.</exception>
    internal static IReadOnlyList<JetstreamArchiveRow> DecodeBlock(ReadOnlySpan<byte> block)
        => DecodeRows(block.ToArray(), filter: null, copyPayloads: true);

    /// <summary>
    /// Decode a block frame straight to events, applying <paramref name="filter"/> to the row
    /// columns first: only rows that match are projected, which is where the cost is (DAG-CBOR to
    /// JSON, and hashing the record for its CID).
    /// </summary>
    internal static List<JetstreamEvent> DecodeEvents(
        ReadOnlySpan<byte> frame, IJetstreamBlockDecompressor decompressor, JetstreamArchiveRowFilter filter)
    {
        // The decompressed block is this call's alone, so matching rows alias it rather than copy.
        var rows = DecodeRows(Decompress(frame, decompressor), filter, copyPayloads: false);
        var events = new List<JetstreamEvent>(rows.Count);

        // One DID parse per account per block: the rows of a busy account repeat it.
        Dictionary<string, DidValue?>? dids = null;
        foreach (var row in rows)
        {
            dids ??= new Dictionary<string, DidValue?>(StringComparer.Ordinal);
            if (!dids.TryGetValue(row.Did, out var did))
                dids[row.Did] = did = DidValue.TryParse(row.Did, out var parsed) ? parsed : null;

            if (did is not null && row.ToEvent(did) is { } evt)
                events.Add(evt);
        }

        return events;
    }

    private static byte[] Decompress(ReadOnlySpan<byte> frame, IJetstreamBlockDecompressor decompressor)
    {
        ArgumentNullException.ThrowIfNull(decompressor);

        try
        {
            return decompressor.Decompress(frame);
        }
        catch (Exception ex) when (ex is not JetstreamException and not OperationCanceledException)
        {
            throw new JetstreamException(
                $"Could not decompress a {frame.Length}-byte segment block: {ex.Message}",
                innerException: ex);
        }
    }

    private static List<JetstreamArchiveRow> DecodeRows(byte[] blockArray, JetstreamArchiveRowFilter? filter, bool copyPayloads)
    {
        ReadOnlySpan<byte> block = blockArray;
        if (block.Length < 4)
            throw new JetstreamException(
                $"A segment block is at least 4 bytes; got {block.Length}.");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(block);
        if (count == 0)
            return [];
        if (count > MaxEventsPerBlock)
            throw new JetstreamException(
                $"Segment block claims {count} events, above the {MaxEventsPerBlock} ceiling.");

        var n = (int)count;
        var fixedBytes = 4L + (long)n * FixedColumnBytesPerEvent;
        if (block.Length < fixedBytes)
            throw new JetstreamException(
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
            throw new JetstreamException(
                $"Segment block is truncated: its columns need {totalBytes} bytes, " +
                $"the block has {block.Length}.");

        var rows = new List<JetstreamArchiveRow>(filter is null ? n : 0);
        var strings = new StringInterner();
        int collectionAt = (int)collectionsStart, didAt = (int)didsStart, rkeyAt = (int)rkeysStart,
            revAt = (int)revsStart, payloadAt = (int)payloadsStart;

        for (var i = 0; i < n; i++)
        {
            int collectionSize = block[collectionLen + i];
            int didSize = BinaryPrimitives.ReadUInt16LittleEndian(block[(didLen + (i * 2))..]);
            int rkeySize = block[rkeyLen + i];
            int revSize = block[revLen + i];
            var payloadSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[(eventLen + (i * 4))..]);

            var rowSeq = (long)BinaryPrimitives.ReadUInt64LittleEndian(block[(seq + (i * 8))..]);
            var rowKind = (JetstreamArchiveRowKind)block[kind + i];

            // An account and a collection repeat across a block's rows, so each is one string.
            var did = strings.Get(block.Slice(didAt, didSize));
            var collection = strings.Get(block.Slice(collectionAt, collectionSize));

            if (filter is null || filter.Matches(rowSeq, rowKind, did, collection))
            {
                rows.Add(new JetstreamArchiveRow
                {
                    Seq = rowSeq,
                    WitnessedAt = BinaryPrimitives.ReadInt64LittleEndian(block[(witnessedAt + (i * 8))..]),
                    IndexedAt = BinaryPrimitives.ReadInt64LittleEndian(block[(indexedAt + (i * 8))..]),
                    Kind = rowKind,
                    Collection = collection,
                    Did = did,
                    Rkey = Utf8(block.Slice(rkeyAt, rkeySize)),
                    Rev = strings.Get(block.Slice(revAt, revSize)),
                    Payload = copyPayloads
                        ? block.Slice(payloadAt, payloadSize).ToArray()
                        : blockArray.AsMemory(payloadAt, payloadSize),
                });
            }

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
    /// <exception cref="JetstreamException">The segment is not a sealed <c>.jss</c> file,
    /// or is truncated.</exception>
    public static async IAsyncEnumerable<JetstreamArchiveRow> ReadRowsAsync(
        Stream segment,
        IJetstreamBlockDecompressor decompressor,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decompressor);

        await foreach (var frame in ReadBlockFramesAsync(segment, cancellationToken).ConfigureAwait(false))
        {
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
        await foreach (var row in ReadRowsAsync(segment, decompressor, cancellationToken).ConfigureAwait(false))
        {
            if (row.ToEvent() is { } evt)
                yield return evt;
        }
    }

    /// <summary>
    /// Read a sealed segment's header, then yield each stored block frame (still compressed) in
    /// order.
    /// </summary>
    /// <exception cref="JetstreamException">The segment is not a sealed <c>.jss</c> file, or is
    /// truncated.</exception>
    internal static async IAsyncEnumerable<byte[]> ReadBlockFramesAsync(
        Stream segment,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var headerBytes = new byte[JetstreamSegmentHeader.Size];
        await ReadExactlyAsync(segment, headerBytes, "segment header", cancellationToken).ConfigureAwait(false);
        var header = ReadHeader(headerBytes);

        var offset = (ulong)JetstreamSegmentHeader.Size;
        var lengthPrefix = new byte[8];

        for (uint blockIndex = 0; blockIndex < header.BlockCount; blockIndex++)
        {
            if (offset + 8 > header.FooterOffset)
                throw new JetstreamException(
                    $"Segment is truncated: block {blockIndex} starts at {offset}, " +
                    $"past the footer at {header.FooterOffset}.");

            await ReadExactlyAsync(segment, lengthPrefix, $"block {blockIndex} length", cancellationToken).ConfigureAwait(false);
            var frameLength = BinaryPrimitives.ReadUInt64LittleEndian(lengthPrefix);

            if (frameLength > MaxBlockFrameBytes || offset + 8 + frameLength > header.FooterOffset)
                throw new JetstreamException(
                    $"Segment block {blockIndex} claims {frameLength} bytes, which does not fit " +
                    $"before the footer at {header.FooterOffset}.");

            var frame = new byte[(int)frameLength];
            await ReadExactlyAsync(segment, frame, $"block {blockIndex}", cancellationToken).ConfigureAwait(false);
            offset += 8 + frameLength;

            yield return frame;
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, string what, CancellationToken cancellationToken)
    {
        try
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException ex)
        {
            throw new JetstreamException($"Segment ended while reading {what}: expected {buffer.Length} bytes.", innerException: ex);
        }
    }

    /// <summary>
    /// Hands out one string per distinct UTF-8 value within a block, so the rows of a busy account
    /// or collection share it instead of each decoding their own.
    /// </summary>
    private sealed class StringInterner
    {
        private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _lookup;

        public StringInterner() => _lookup = _strings.GetAlternateLookup<ReadOnlySpan<char>>();

        public string Get(ReadOnlySpan<byte> utf8)
        {
            if (utf8.IsEmpty)
                return string.Empty;

            // Longer values are rare (a did:web on a long domain) and decode directly.
            if (utf8.Length > 512)
                return Encoding.UTF8.GetString(utf8);

            Span<char> chars = stackalloc char[utf8.Length];
            var length = Encoding.UTF8.GetChars(utf8, chars);
            var text = chars[..length];

            if (_lookup.TryGetValue(text, out var existing))
                return existing;

            var created = new string(text);
            _strings[created] = created;
            return created;
        }
    }
}

/// <summary>
/// The exact client-side filter the archive replay applies, mirroring the live tail's server-side
/// semantics: DIDs and kinds constrain everything, collections constrain commit events only. It
/// works on a row's columns, so rows are dropped before they are projected to events.
/// </summary>
internal sealed class JetstreamArchiveRowFilter
{
    private readonly HashSet<string>? _dids;
    private readonly HashSet<string>? _collections;
    private readonly string[] _collectionPrefixes;
    private readonly HashSet<JetstreamEventKind>? _kinds;

    public JetstreamArchiveRowFilter(JetstreamConsumerOptions options, long afterSeq, long ceiling)
    {
        AfterSeq = afterSeq;
        Ceiling = ceiling;

        if (options.WantedDids is { Count: > 0 } dids)
            _dids = new HashSet<string>(dids.Select(did => did.Value), StringComparer.Ordinal);

        if (options.WantedKinds is { Count: > 0 } kinds)
            _kinds = [.. kinds];

        if (options.WantedCollections is { Count: > 0 } collections)
        {
            _collections = new HashSet<string>(collections.Where(c => !c.EndsWith('*')), StringComparer.Ordinal);
            _collectionPrefixes = [.. collections.Where(c => c.EndsWith('*')).Select(c => c[..^1])];
        }
        else
        {
            _collectionPrefixes = [];
        }
    }

    /// <summary>Rows at or below this sequence number were delivered already.</summary>
    public long AfterSeq { get; }

    /// <summary>Rows above this sequence number belong to the live tail.</summary>
    public long Ceiling { get; }

    /// <summary>Whether a row's columns pass the sequence window and the filter.</summary>
    public bool Matches(long seq, JetstreamArchiveRowKind kind, string did, string collection)
    {
        if (seq <= AfterSeq || seq > Ceiling)
            return false;

        if (_dids is not null && !_dids.Contains(did))
            return false;

        var eventKind = kind switch
        {
            JetstreamArchiveRowKind.Create or JetstreamArchiveRowKind.Update or JetstreamArchiveRowKind.Delete
                or JetstreamArchiveRowKind.CreateResync => JetstreamEventKind.Commit,
            JetstreamArchiveRowKind.Identity => JetstreamEventKind.Identity,
            JetstreamArchiveRowKind.Account => JetstreamEventKind.Account,
            JetstreamArchiveRowKind.Sync => JetstreamEventKind.Sync,
            // A kind this version does not model cannot be projected; ToEvent skips it anyway.
            _ => (JetstreamEventKind?)null,
        };

        if (eventKind is null || (_kinds is not null && !_kinds.Contains(eventKind.Value)))
            return false;

        // A collection filter constrains commit events only: identity, account, and sync events
        // carry no collection and flow regardless, exactly as on the live tail.
        return eventKind != JetstreamEventKind.Commit || MatchesCollection(collection);
    }

    private bool MatchesCollection(string collection)
    {
        if (_collections is null)
            return true;

        if (_collections.Contains(collection))
            return true;

        foreach (var prefix in _collectionPrefixes)
        {
            if (collection.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
