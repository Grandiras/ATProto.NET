using System.Text;

namespace ATProtoNet.Data.Star;

/// <summary>Writes STAR archives of space repositories, deterministically: the same header and records give the same bytes.</summary>
/// <remarks>See <see cref="StarReader"/> for the format; the writer refuses input its reader would refuse.</remarks>
public static class StarWriter
{
    /// <summary>Writes the magic, the header metadata and the entries.</summary>
    /// <param name="output">Receives the archive.</param>
    /// <param name="header">The metadata: a strict DRISL map with a string <c>$type</c>.</param>
    /// <param name="entries">Record paths and strict DRISL records, in strictly increasing path order.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when every entry is written.</returns>
    /// <exception cref="ArgumentException">The header, a path or a record is invalid, the header is over <see cref="StarLimits.DefaultMaxHeaderBytes"/> or a record over <see cref="StarLimits.DefaultMaxEntryBytes"/>, or the paths are out of order.</exception>
    public static async ValueTask WriteAsync(Stream output, ReadOnlyMemory<byte> header, IAsyncEnumerable<(string Path, ReadOnlyMemory<byte> Record)> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(entries);
        if (header.Length > StarLimits.DefaultMaxHeaderBytes)
            throw new ArgumentException($"The metadata is longer than {StarLimits.DefaultMaxHeaderBytes} bytes, which readers refuse by default.", nameof(header));
        Check(() => StarFormat.CheckHeader(header), nameof(header));
        var prefix = new byte[StarFormat.Magic.Length + UnsignedVarint.MaxLength + StarLimits.MaxPathBytes + UnsignedVarint.MaxLength];
        StarFormat.Magic.CopyTo(prefix);
        var length = StarFormat.Magic.Length + UnsignedVarint.Write((ulong)header.Length, prefix.AsSpan(StarFormat.Magic.Length));
        await output.WriteAsync(prefix.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);

        string? previous = null;
        await foreach (var (path, record) in entries.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (path is null || !StarFormat.IsRecordPath(path)) throw new ArgumentException($"\"{path}\" is not a record path ({{collection}}/{{rkey}}).", nameof(entries));
            if (previous is not null && string.CompareOrdinal(previous, path) >= 0)
                throw new ArgumentException($"The entry {path} is not after {previous}: entries must be in strictly increasing path order.", nameof(entries));
            if (record.Length > StarLimits.DefaultMaxEntryBytes) throw new ArgumentException($"The record {path} is longer than {StarLimits.DefaultMaxEntryBytes} bytes.", nameof(entries));
            Check(() => StarFormat.CheckRecord(record), nameof(entries));
            length = UnsignedVarint.Write((ulong)path.Length, prefix);
            length += Encoding.ASCII.GetBytes(path, prefix.AsSpan(length));
            length += UnsignedVarint.Write((ulong)record.Length, prefix.AsSpan(length));
            await output.WriteAsync(prefix.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(record, cancellationToken).ConfigureAwait(false);
            previous = path;
        }
    }

    private static void Check(Action check, string parameter)
    {
        try
        {
            check();
        }
        catch (FormatException e)
        {
            throw new ArgumentException(e.Message, parameter, e);
        }
    }
}
