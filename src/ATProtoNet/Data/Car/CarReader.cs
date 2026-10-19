using System.Runtime.CompilerServices;

namespace ATProtoNet.Data.Car;

/// <summary>Reads CARv1 files, the serialization of public repositories, within bounds.</summary>
/// <remarks>
/// The header is strict DRISL: <c>{"roots": [links], "version": 1}</c>, unknown keys skipped. Each block
/// is a non-zero varint length, a blessed binary CID and the data. Every failure is a
/// <see cref="FormatException"/>; an exceeded bound is a <see cref="DataLimitExceededException"/> naming
/// the option. See <see href="https://atproto.com/specs/repository#car-file-serialization"/> and
/// <see href="https://ipld.io/specs/transport/car/carv1/"/>.
/// </remarks>
public static class CarReader
{
    /// <summary>Reads a whole CAR from memory; the blocks slice <paramref name="car"/> rather than copy it.</summary>
    /// <param name="car">The CAR bytes.</param>
    /// <param name="options">Bounds and CID verification; the defaults when omitted.</param>
    /// <returns>The header and the blocks in file order.</returns>
    /// <exception cref="FormatException">The CAR is malformed, exceeds a bound, or (when verifying) a block does not match its CID.</exception>
    public static CarFile Read(ReadOnlyMemory<byte> car, CarReaderOptions? options = null)
    {
        options ??= CarReaderOptions.Default;
        if (car.Length > options.MaxTotalBytes)
            throw new DataLimitExceededException(nameof(options.MaxTotalBytes), $"The CAR is longer than MaxTotalBytes ({options.MaxTotalBytes} bytes).");
        var span = car.Span;
        var at = 0;
        var headerLength = Length(span, ref at, options.MaxHeaderBytes, nameof(options.MaxHeaderBytes), "header");
        var header = Header(car.Slice(at, headerLength));
        at += headerLength;
        var blocks = new List<CarBlock>();
        while (at < span.Length)
        {
            if (blocks.Count == options.MaxBlocks) throw TooManyBlocks(options);
            var length = Length(span, ref at, options.MaxBlockBytes, nameof(options.MaxBlockBytes), "block");
            blocks.Add(Block(car.Slice(at, length), options.VerifyBlockCids));
            at += length;
        }
        return new(header, blocks);
    }

    /// <summary>Reads the header from the start of a stream, leaving the stream at the first block.</summary>
    /// <param name="car">The CAR stream; nothing past the header is read.</param>
    /// <param name="options">Bounds; the defaults when omitted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The header.</returns>
    /// <exception cref="FormatException">The header is malformed or longer than <see cref="CarReaderOptions.MaxHeaderBytes"/>.</exception>
    public static async ValueTask<CarHeader> ReadHeaderAsync(Stream car, CarReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(car);
        options ??= CarReaderOptions.Default;
        using var frames = new FrameReader(car, options.MaxHeaderBytes + UnsignedVarint.MaxLength, nameof(options.MaxHeaderBytes), readAhead: false);
        var length = await frames.ReadVarintAsync(allowEnd: false, cancellationToken).ConfigureAwait(false);
        var header = await frames.ReadExactlyAsync(Length(length!.Value, options.MaxHeaderBytes, nameof(options.MaxHeaderBytes), "header"), cancellationToken).ConfigureAwait(false);
        return Header(header);
    }

    /// <summary>Streams the blocks that follow the header, holding one block and a bounded buffer at a time.</summary>
    /// <param name="car">The CAR stream, positioned after the header (see <see cref="ReadHeaderAsync"/>).</param>
    /// <param name="options">Bounds and CID verification; the defaults when omitted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The blocks, in file order; each owns its bytes.</returns>
    /// <exception cref="FormatException">A block is malformed, exceeds a bound, or (when verifying) does not match its CID.</exception>
    public static async IAsyncEnumerable<CarBlock> ReadBlocksAsync(Stream car, CarReaderOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(car);
        options ??= CarReaderOptions.Default;
        using var frames = new FrameReader(car, options.MaxTotalBytes, nameof(options.MaxTotalBytes), readAhead: true);
        var count = 0;
        while (await frames.ReadVarintAsync(allowEnd: true, cancellationToken).ConfigureAwait(false) is { } length)
        {
            if (count++ == options.MaxBlocks) throw TooManyBlocks(options);
            var block = await frames.ReadExactlyAsync(Length(length, options.MaxBlockBytes, nameof(options.MaxBlockBytes), "block"), cancellationToken).ConfigureAwait(false);
            yield return Block(block, options.VerifyBlockCids);
        }
    }

    // A varint section length, which must also fit in what remains of the input.
    private static int Length(ReadOnlySpan<byte> car, ref int at, int max, string limit, string section)
    {
        var length = Length(UnsignedVarint.Read(car[at..], out var read), max, limit, section);
        at += read;
        return length <= car.Length - at ? length : throw new FormatException($"A CAR {section} runs past the end of the input.");
    }

    private static int Length(ulong length, int max, string limit, string section) =>
        length == 0 ? throw new FormatException($"A CAR {section} has length zero.")
        : length > (ulong)max ? throw new DataLimitExceededException(limit, $"A CAR {section} is longer than {limit} ({max} bytes).")
        : (int)length;

    private static CarHeader Header(ReadOnlyMemory<byte> header)
    {
        var reader = new DrislReader(header);
        if (reader.PeekKind() != DrislValueKind.Map) throw new FormatException("The CAR header is not a map.");
        reader.ReadStartMap();
        long? version = null;
        List<Cid>? roots = null;
        while (reader.PeekKind() != DrislValueKind.End)
            switch (reader.ReadKey())
            {
                case "roots": roots = Roots(reader); break;
                case "version": version = reader.ReadInt64(); break;
                default: reader.SkipValue(); break;
            }
        reader.ReadEndMap();
        reader.ReadEnd();
        if (version != 1) throw new FormatException(version is null ? "The CAR header has no version." : $"CAR version {version} is not supported; only version 1 is.");
        return new(roots ?? throw new FormatException("The CAR header has no roots."));
    }

    private static List<Cid> Roots(DrislReader reader)
    {
        reader.ReadStartArray();
        var roots = new List<Cid>();
        while (reader.PeekKind() != DrislValueKind.End) roots.Add(reader.ReadLink());
        reader.ReadEndArray();
        return roots;
    }

    private static CarBlock Block(ReadOnlyMemory<byte> section, bool verify)
    {
        var span = section.Span;
        // A block shorter than a CID fails as not being one; a CIDv0 is reported as such.
        var cid = Cid.FromBytes(span.Length < Cid.BinaryLength ? span : span[..Cid.BinaryLength]);
        var data = section[Cid.BinaryLength..];
        return !verify || cid.Matches(data.Span) ? new(cid, data) : throw new FormatException($"The block {cid} does not match its CID.");
    }

    private static DataLimitExceededException TooManyBlocks(CarReaderOptions options) =>
        new(nameof(options.MaxBlocks), $"The CAR has more than MaxBlocks ({options.MaxBlocks}) blocks.");
}
