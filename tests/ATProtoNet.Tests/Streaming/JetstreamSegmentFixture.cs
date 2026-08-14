using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using ATProtoNet.Repo;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

/// <summary>
/// Builds <c>.jss</c> segment bytes for tests: the 256-byte fixed header, length-prefixed block
/// frames, and a stand-in footer. Blocks are written through a pluggable "compressor" so the tests
/// can pair the fixture with <see cref="PassThroughDecompressor"/> and exercise the decoder without
/// pulling a zstd implementation into the test project.
/// </summary>
internal static class JetstreamSegmentFixture
{
    /// <summary>A row to encode into a block, mirroring the on-disk columns.</summary>
    internal sealed record Row(
        long Seq,
        long WitnessedAt,
        long IndexedAt,
        JetstreamArchiveRowKind Kind,
        string Did,
        string Collection,
        string RKey,
        string Rev,
        byte[] Payload)
    {
        public static Row Commit(
            long seq,
            string did = "did:plc:eygmaihciaxprqvxpfvl6flk",
            string collection = "app.bsky.feed.post",
            string rkey = "3l3qo2vuowo2b",
            JetstreamArchiveRowKind kind = JetstreamArchiveRowKind.Create,
            byte[]? record = null)
            => new(seq, 1_725_911_162_000_000 + seq, 0, kind, did, collection, rkey, "3l3qo2vuowo2c",
                record ?? (kind == JetstreamArchiveRowKind.Delete ? [] : PostRecord()));

        public static Row NonCommit(long seq, JetstreamArchiveRowKind kind, byte[] payload,
            string did = "did:plc:eygmaihciaxprqvxpfvl6flk")
            => new(seq, 1_725_911_162_000_000 + seq, 0, kind, did, string.Empty, string.Empty,
                string.Empty, payload);
    }

    /// <summary>A DAG-CBOR encoded <c>app.bsky.feed.post</c> record.</summary>
    internal static byte[] PostRecord(string text = "hello archive")
        => DagCborEncoder.Encode(JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["$type"] = "app.bsky.feed.post",
            ["text"] = text,
            ["createdAt"] = "2024-09-09T20:26:02.000Z",
        }));

    /// <summary>A DAG-CBOR encoded <c>com.atproto.sync.subscribeRepos</c> frame body.</summary>
    internal static byte[] Frame(Dictionary<string, object> fields)
        => DagCborEncoder.Encode(JsonSerializer.SerializeToElement(fields));

    /// <summary>Encode one uncompressed columnar block body (§3.2).</summary>
    internal static byte[] EncodeBlock(IReadOnlyList<Row> rows)
    {
        var body = new MemoryStream();
        var scratch = new byte[8];

        void U16(int value) { BinaryPrimitives.WriteUInt16LittleEndian(scratch, (ushort)value); body.Write(scratch, 0, 2); }
        void U32(long value) { BinaryPrimitives.WriteUInt32LittleEndian(scratch, (uint)value); body.Write(scratch, 0, 4); }
        void U64(long value) { BinaryPrimitives.WriteUInt64LittleEndian(scratch, (ulong)value); body.Write(scratch, 0, 8); }

        U32(rows.Count);

        foreach (var row in rows) U64(row.Seq);
        foreach (var row in rows) U64(row.WitnessedAt);
        foreach (var row in rows) U64(row.IndexedAt);
        foreach (var row in rows) body.WriteByte((byte)row.Kind);
        foreach (var row in rows) body.WriteByte((byte)Encoding.UTF8.GetByteCount(row.Collection));
        foreach (var row in rows) U16(Encoding.UTF8.GetByteCount(row.Did));
        foreach (var row in rows) body.WriteByte((byte)Encoding.UTF8.GetByteCount(row.RKey));
        foreach (var row in rows) body.WriteByte((byte)Encoding.UTF8.GetByteCount(row.Rev));
        foreach (var row in rows) U32(row.Payload.Length);

        foreach (var row in rows) body.Write(Encoding.UTF8.GetBytes(row.Collection));
        foreach (var row in rows) body.Write(Encoding.UTF8.GetBytes(row.Did));
        foreach (var row in rows) body.Write(Encoding.UTF8.GetBytes(row.RKey));
        foreach (var row in rows) body.Write(Encoding.UTF8.GetBytes(row.Rev));
        foreach (var row in rows) body.Write(row.Payload);

        return body.ToArray();
    }

    /// <summary>
    /// Build a whole sealed segment out of one block per element of <paramref name="blocks"/>.
    /// </summary>
    /// <param name="blocks">The rows of each block, in order.</param>
    /// <param name="compress">Stand-in for zstd. Defaults to storing the block verbatim.</param>
    /// <param name="checksum">The header checksum; zero marks the segment as active (unsealed).</param>
    internal static byte[] Segment(
        IReadOnlyList<IReadOnlyList<Row>> blocks,
        Func<byte[], byte[]>? compress = null,
        ulong checksum = 0x0123456789abcdefUL)
    {
        compress ??= body => body;

        var file = new MemoryStream();
        file.Write(new byte[JetstreamSegmentHeader.Size]);

        var frames = blocks.Select(rows => compress(EncodeBlock(rows))).ToList();
        var prefix = new byte[8];
        foreach (var frame in frames)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(prefix, (ulong)frame.Length);
            file.Write(prefix);
            file.Write(frame);
        }

        var footerOffset = (ulong)file.Length;
        // A stand-in footer: the reader walks blocks sequentially and only needs the offset to
        // know where they stop.
        file.Write(new byte[52 * blocks.Count]);

        var rowsAll = blocks.SelectMany(b => b).ToList();
        var header = new byte[JetstreamSegmentHeader.Size];
        "jss0"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(4), checksum);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(14), (uint)blocks.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(18), (uint)rowsAll.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(22), (uint)rowsAll.Select(r => r.Did).Distinct().Count());
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(26), (ulong)(rowsAll.Count == 0 ? 0 : rowsAll.Min(r => r.Seq)));
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(34), (ulong)(rowsAll.Count == 0 ? 0 : rowsAll.Max(r => r.Seq)));
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(42), rowsAll.Count == 0 ? 0 : rowsAll.Min(r => r.WitnessedAt));
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(50), rowsAll.Count == 0 ? 0 : rowsAll.Max(r => r.WitnessedAt));
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(58), footerOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(66), footerOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(74), footerOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(82), footerOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(90), footerOffset);

        var bytes = file.ToArray();
        header.CopyTo(bytes.AsSpan());
        return bytes;
    }

    /// <summary>One stored block frame, as <c>getBlock</c> returns it (no length prefix).</summary>
    internal static byte[] BlockFrame(IReadOnlyList<Row> rows) => EncodeBlock(rows);
}

/// <summary>
/// Stands in for zstd: the fixtures store block bodies verbatim, so decoding them needs no real
/// decompressor. The block framing and columnar layout under test are independent of the codec.
/// </summary>
internal sealed class PassThroughDecompressor : IJetstreamBlockDecompressor
{
    public byte[] Decompress(ReadOnlySpan<byte> frame) => frame.ToArray();
}
