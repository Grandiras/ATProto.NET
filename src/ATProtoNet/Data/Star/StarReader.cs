using System.Runtime.CompilerServices;
using System.Text;

namespace ATProtoNet.Data.Star;

/// <summary>Reads STAR archives, the serialization of space repositories, as a bounded stream.</summary>
/// <remarks>
/// STAR v1 framing (magic <c>2A 6C 01 00</c>, varint-prefixed DRISL metadata with a <c>$type</c>, then
/// varint-prefixed key and value pairs) with the atproto space repository profile: keys are record
/// paths in strictly increasing order, values strict DRISL maps. Record CIDs are computed, never read.
/// Every failure is a <see cref="FormatException"/>; an exceeded bound is a
/// <see cref="DataLimitExceededException"/> naming the option. See
/// <see href="https://next.tangled.org/microcosm.blue/star"/>.
/// </remarks>
public static class StarReader
{
    /// <summary>Reads the magic and the header metadata, leaving the stream at the first entry.</summary>
    /// <param name="star">The STAR stream; nothing past the header is read.</param>
    /// <param name="options">Bounds; the defaults when omitted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The header.</returns>
    /// <exception cref="FormatException">The magic or metadata is malformed, or the metadata is longer than <see cref="StarReaderOptions.MaxHeaderBytes"/>.</exception>
    public static async ValueTask<StarHeader> ReadHeaderAsync(Stream star, StarReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(star);
        options ??= StarReaderOptions.Default;
        using var frames = new FrameReader(star, StarFormat.Magic.Length + UnsignedVarint.MaxLength + options.MaxHeaderBytes, nameof(options.MaxHeaderBytes), readAhead: false);
        var magic = await frames.ReadExactlyAsync(StarFormat.Magic.Length, cancellationToken).ConfigureAwait(false);
        if (!magic.AsSpan().SequenceEqual(StarFormat.Magic)) throw new FormatException("The input is not a STAR v1 archive: wrong magic bytes.");
        var length = await frames.ReadVarintAsync(allowEnd: false, cancellationToken).ConfigureAwait(false);
        if (length is 0 or null) throw new FormatException("The STAR metadata is empty.");
        if (length > (ulong)options.MaxHeaderBytes)
            throw new DataLimitExceededException(nameof(options.MaxHeaderBytes), $"The STAR metadata is longer than MaxHeaderBytes ({options.MaxHeaderBytes} bytes).");
        var header = await frames.ReadExactlyAsync((int)length, cancellationToken).ConfigureAwait(false);
        StarFormat.CheckHeader(header);
        return new(header);
    }

    /// <summary>Streams the entries that follow the header, holding one entry and a bounded buffer at a time.</summary>
    /// <param name="star">The STAR stream, positioned after the header (see <see cref="ReadHeaderAsync"/>).</param>
    /// <param name="options">Bounds; the defaults when omitted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entries, in path order; each owns its bytes.</returns>
    /// <exception cref="FormatException">An entry is malformed, out of order, a duplicate, or exceeds a bound, or the input ends inside one.</exception>
    public static async IAsyncEnumerable<StarEntry> ReadEntriesAsync(Stream star, StarReaderOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(star);
        options ??= StarReaderOptions.Default;
        using var frames = new FrameReader(star, options.MaxTotalBytes, nameof(options.MaxTotalBytes), readAhead: true);
        string? previous = null;
        var count = 0;
        while (await frames.ReadVarintAsync(allowEnd: true, cancellationToken).ConfigureAwait(false) is { } pathLength)
        {
            if (count++ == options.MaxEntries)
                throw new DataLimitExceededException(nameof(options.MaxEntries), $"The STAR archive has more than MaxEntries ({options.MaxEntries}) entries.");
            if (pathLength > StarLimits.MaxPathBytes) throw new FormatException("A STAR key is longer than any record path.");
            var path = Path(await frames.ReadExactlyAsync((int)pathLength, cancellationToken).ConfigureAwait(false));
            if (previous is not null && string.CompareOrdinal(previous, path) >= 0)
                throw new FormatException($"The STAR entry {path} is not after {previous}: entries must be in strictly increasing path order.");
            var recordLength = await frames.ReadVarintAsync(allowEnd: false, cancellationToken).ConfigureAwait(false);
            if (recordLength > (ulong)options.MaxEntryBytes)
                throw new DataLimitExceededException(nameof(options.MaxEntryBytes), $"The STAR record {path} is longer than MaxEntryBytes ({options.MaxEntryBytes} bytes).");
            var record = await frames.ReadExactlyAsync((int)recordLength!.Value, cancellationToken).ConfigureAwait(false);
            StarFormat.CheckRecord(record);
            yield return new(path, Cid.Compute(CidCodec.DagCbor, record), record);
            previous = path;
        }
    }

    // Record paths are ASCII, so bytes that are not valid UTF-8 cannot pass the path check either.
    private static string Path(byte[] utf8)
    {
        var path = Encoding.UTF8.GetString(utf8);
        return StarFormat.IsRecordPath(path) ? path : throw new FormatException($"The STAR key \"{path}\" is not a record path ({{collection}}/{{rkey}}).");
    }
}
