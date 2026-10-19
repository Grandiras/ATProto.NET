namespace ATProtoNet.Data.Star;

/// <summary>The built-in and default limits of STAR reading.</summary>
public static class StarLimits
{
    /// <summary>The default bound on the entries after the header.</summary>
    public const long DefaultMaxTotalBytes = 256L * 1024 * 1024;

    /// <summary>The default bound on the number of entries.</summary>
    public const int DefaultMaxEntries = 1024 * 1024;

    /// <summary>The default bound on one record; atproto records are at most 1 MiB.</summary>
    public const int DefaultMaxEntryBytes = 1024 * 1024;

    /// <summary>The default bound on the header metadata, and the most <see cref="StarWriter"/> writes.</summary>
    public const int DefaultMaxHeaderBytes = 64 * 1024;

    /// <summary>The longest record path: a 317-character collection NSID, a slash and a 512-character record key.</summary>
    public const int MaxPathBytes = 317 + 1 + 512;
}

/// <summary>Bounds of a STAR read, each enforced before anything is buffered past it.</summary>
public sealed record StarReaderOptions
{
    internal static readonly StarReaderOptions Default = new();

    /// <summary>The most bytes read after the header.</summary>
    public long MaxTotalBytes { get; init => field = OptionLimits.Positive(value); } = StarLimits.DefaultMaxTotalBytes;

    /// <summary>The most entries read.</summary>
    public int MaxEntries { get; init => field = OptionLimits.Positive(value); } = StarLimits.DefaultMaxEntries;

    /// <summary>The longest record.</summary>
    public int MaxEntryBytes { get; init => field = OptionLimits.Positive(value); } = StarLimits.DefaultMaxEntryBytes;

    /// <summary>The longest header metadata.</summary>
    public int MaxHeaderBytes { get; init => field = OptionLimits.Positive(value); } = StarLimits.DefaultMaxHeaderBytes;
}

/// <summary>A STAR header: its DRISL metadata map, which its consumer (the space repository) interprets.</summary>
public sealed class StarHeader
{
    internal StarHeader(ReadOnlyMemory<byte> drisl) => Drisl = drisl;

    /// <summary>The metadata, strict DRISL: a map with a string <c>$type</c>.</summary>
    public ReadOnlyMemory<byte> Drisl { get; }
}

/// <summary>A STAR entry: a record path, the record's CID (computed from its bytes, never read) and the record.</summary>
public sealed class StarEntry
{
    internal StarEntry(string path, Cid cid, ReadOnlyMemory<byte> record) => (Path, Cid, Record) = (path, cid, record);

    /// <summary>The record path, <c>{collection}/{rkey}</c>.</summary>
    public string Path { get; }

    /// <summary>The DRISL CID of <see cref="Record"/>.</summary>
    public Cid Cid { get; }

    /// <summary>The record: a strict DRISL map.</summary>
    public ReadOnlyMemory<byte> Record { get; }
}
