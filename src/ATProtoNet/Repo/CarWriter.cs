using System.Formats.Cbor;

namespace ATProtoNet.Repo;

/// <summary>
/// Writes CAR v1 (Content Addressable aRchive) files — the producer counterpart to
/// <see cref="CarReader"/>.
/// <para>
/// A CAR v1 file is an unsigned-varint-prefixed DAG-CBOR header followed by a sequence of
/// length-prefixed blocks, each block being its binary CID immediately followed by the block
/// bytes. AT Protocol uses this format for <c>com.atproto.sync.getRepo</c> responses and for
/// the <c>blocks</c> field of firehose <c>#commit</c> events.
/// </para>
/// </summary>
/// <remarks>
/// See: https://ipld.io/specs/transport/car/carv1/
/// </remarks>
public static class CarWriter
{
    /// <summary>
    /// Encodes a CAR v1 file with a single root.
    /// </summary>
    /// <param name="root">Binary CID of the root block (as produced by <see cref="CidComputation.ComputeBinaryForDagCbor"/>).</param>
    /// <param name="blocks">
    /// The blocks to include, keyed by their base32 CID string (<c>bafyrei…</c>) — the key
    /// format produced by <see cref="MerkleSearchTree.Serialize"/>.
    /// </param>
    /// <returns>The complete CAR file bytes.</returns>
    public static byte[] Write(byte[] root, IReadOnlyDictionary<string, byte[]> blocks)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(blocks);

        return Write([root], blocks.Select(kv => new CarBlock(CidComputation.DecodeCidString(kv.Key), kv.Value)));
    }

    /// <summary>
    /// Encodes a CAR v1 file with a single root from an explicit block sequence.
    /// </summary>
    /// <param name="root">Binary CID of the root block.</param>
    /// <param name="blocks">The blocks to include, in write order.</param>
    /// <returns>The complete CAR file bytes.</returns>
    public static byte[] Write(byte[] root, IEnumerable<CarBlock> blocks)
        => Write([root], blocks);

    /// <summary>
    /// Encodes a CAR v1 file.
    /// </summary>
    /// <param name="roots">Binary CIDs of the root blocks. May be empty.</param>
    /// <param name="blocks">The blocks to include, in write order.</param>
    /// <returns>The complete CAR file bytes.</returns>
    public static byte[] Write(IReadOnlyList<byte[]> roots, IEnumerable<CarBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(blocks);

        using var stream = new MemoryStream();
        WriteTo(stream, roots, blocks);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes a CAR v1 file to a stream without buffering the whole archive in memory.
    /// </summary>
    /// <param name="destination">The stream to write to.</param>
    /// <param name="roots">Binary CIDs of the root blocks.</param>
    /// <param name="blocks">The blocks to include, in write order.</param>
    public static void WriteTo(Stream destination, IReadOnlyList<byte[]> roots, IEnumerable<CarBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(blocks);

        var header = EncodeHeader(roots);
        WriteUvarint(destination, (ulong)header.Length);
        destination.Write(header);

        foreach (var block in blocks)
        {
            WriteUvarint(destination, (ulong)(block.Cid.Length + block.Data.Length));
            destination.Write(block.Cid);
            destination.Write(block.Data);
        }
    }

    /// <summary>
    /// Writes a CAR v1 file to a stream asynchronously.
    /// </summary>
    /// <param name="destination">The stream to write to.</param>
    /// <param name="roots">Binary CIDs of the root blocks.</param>
    /// <param name="blocks">The blocks to include, in write order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WriteToAsync(
        Stream destination,
        IReadOnlyList<byte[]> roots,
        IEnumerable<CarBlock> blocks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(blocks);

        var header = EncodeHeader(roots);
        await WriteUvarintAsync(destination, (ulong)header.Length, cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);

        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteUvarintAsync(destination, (ulong)(block.Cid.Length + block.Data.Length), cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(block.Cid, cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(block.Data, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Encodes the DAG-CBOR CAR v1 header: <c>{"roots": [&lt;tag 42 CID&gt;, …], "version": 1}</c>.
    /// </summary>
    internal static byte[] EncodeHeader(IReadOnlyList<byte[]> roots)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(2);

        // Canonical (length-first) key order puts "roots" before "version"; the writer
        // sorts on WriteEndMap regardless, but writing them in order keeps this readable.
        writer.WriteTextString("roots");
        writer.WriteStartArray(roots.Count);
        foreach (var root in roots)
            WriteCidLink(writer, root);
        writer.WriteEndArray();

        writer.WriteTextString("version");
        writer.WriteInt32(1);

        writer.WriteEndMap();
        return writer.Encode();
    }

    private static void WriteCidLink(CborWriter writer, byte[] cidBytes)
    {
        writer.WriteTag((CborTag)42);
        // Identity multibase prefix (0x00), as required for CID links in DAG-CBOR.
        var tagged = new byte[cidBytes.Length + 1];
        cidBytes.CopyTo(tagged.AsSpan(1));
        writer.WriteByteString(tagged);
    }

    private static void WriteUvarint(Stream destination, ulong value)
    {
        Span<byte> buffer = stackalloc byte[10];
        var length = EncodeUvarint(value, buffer);
        destination.Write(buffer[..length]);
    }

    private static ValueTask WriteUvarintAsync(Stream destination, ulong value, CancellationToken cancellationToken)
    {
        var buffer = new byte[10];
        var length = EncodeUvarint(value, buffer);
        return destination.WriteAsync(buffer.AsMemory(0, length), cancellationToken);
    }

    /// <summary>
    /// Encodes an unsigned LEB128 varint into <paramref name="destination"/>, returning the byte count.
    /// </summary>
    internal static int EncodeUvarint(ulong value, Span<byte> destination)
    {
        var index = 0;
        while (value >= 0x80)
        {
            destination[index++] = (byte)(value | 0x80);
            value >>= 7;
        }
        destination[index++] = (byte)value;
        return index;
    }
}
