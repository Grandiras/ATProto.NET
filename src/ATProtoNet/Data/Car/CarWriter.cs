using System.Buffers;

namespace ATProtoNet.Data.Car;

/// <summary>Writes CARv1 files: a canonical DRISL header <c>{roots, version: 1}</c>, then each block as a varint length, its binary CID and its data.</summary>
/// <remarks>
/// Blocks are written as given; their CIDs are not recomputed. See
/// <see href="https://atproto.com/specs/repository#car-file-serialization"/>.
/// </remarks>
public static class CarWriter
{
    private const int MaxPrefixLength = UnsignedVarint.MaxLength + Cid.BinaryLength;

    /// <summary>Writes a CAR to a buffer.</summary>
    /// <param name="output">Receives the CAR.</param>
    /// <param name="roots">The roots, possibly none.</param>
    /// <param name="blocks">The blocks, in order, possibly none.</param>
    public static void Write(IBufferWriter<byte> output, IEnumerable<Cid> roots, IEnumerable<CarBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(blocks);
        output.Write(Header(roots));
        foreach (var block in blocks)
        {
            output.Advance(Prefix(output.GetSpan(MaxPrefixLength), block));
            output.Write(block.Data.Span);
        }
    }

    /// <summary>Writes a CAR to a stream, one block at a time.</summary>
    /// <param name="output">Receives the CAR.</param>
    /// <param name="roots">The roots, possibly none.</param>
    /// <param name="blocks">The blocks, in order, possibly none.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when every block is written.</returns>
    public static async ValueTask WriteAsync(Stream output, IEnumerable<Cid> roots, IAsyncEnumerable<CarBlock> blocks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(blocks);
        await output.WriteAsync(Header(roots), cancellationToken).ConfigureAwait(false);
        var prefix = new byte[MaxPrefixLength];
        await foreach (var block in blocks.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await output.WriteAsync(prefix.AsMemory(0, Prefix(prefix, block)), cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(block.Data, cancellationToken).ConfigureAwait(false);
        }
    }

    // The length-prefixed header section.
    private static byte[] Header(IEnumerable<Cid> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var list = roots.ToList();
        var writer = new DrislWriter();
        writer.WriteStartMap(2);
        writer.WriteKey("roots");
        writer.WriteStartArray(list.Count);
        foreach (var root in list) writer.WriteLink(root);
        writer.WriteEndArray();
        writer.WriteKey("version");
        writer.WriteInt64(1);
        writer.WriteEndMap();
        var header = writer.ToArray();
        var section = new byte[UnsignedVarint.GetLength((ulong)header.Length) + header.Length];
        header.CopyTo(section, UnsignedVarint.Write((ulong)header.Length, section));
        return section;
    }

    private static int Prefix(Span<byte> prefix, CarBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var length = UnsignedVarint.Write((ulong)(Cid.BinaryLength + block.Data.Length), prefix);
        block.Cid.WriteBytes(prefix[length..]);
        return length + Cid.BinaryLength;
    }
}
