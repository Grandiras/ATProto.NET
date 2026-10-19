namespace ATProtoNet.Data.Car;

/// <summary>The built-in and default limits of CAR reading.</summary>
public static class CarLimits
{
    /// <summary>The default bound on a whole CAR, or on the blocks after its header when streaming.</summary>
    public const long DefaultMaxTotalBytes = 256L * 1024 * 1024;

    /// <summary>The default bound on the number of blocks.</summary>
    public const int DefaultMaxBlocks = 4 * 1024 * 1024;

    /// <summary>The default bound on one block, CID included; records are at most 1 MB under the sync specification.</summary>
    public const int DefaultMaxBlockBytes = 2 * 1024 * 1024;

    /// <summary>The default bound on the header.</summary>
    public const int DefaultMaxHeaderBytes = 64 * 1024;
}

/// <summary>Bounds and checks of a CAR read, each enforced before anything is buffered past it.</summary>
public sealed record CarReaderOptions
{
    internal static readonly CarReaderOptions Default = new();

    /// <summary>The most bytes read: the whole CAR, or the blocks after the header when streaming.</summary>
    public long MaxTotalBytes { get; init => field = OptionLimits.Positive(value); } = CarLimits.DefaultMaxTotalBytes;

    /// <summary>The most blocks read.</summary>
    public int MaxBlocks { get; init => field = OptionLimits.Positive(value); } = CarLimits.DefaultMaxBlocks;

    /// <summary>The longest block, CID included.</summary>
    public int MaxBlockBytes { get; init => field = OptionLimits.Positive(value); } = CarLimits.DefaultMaxBlockBytes;

    /// <summary>The longest header.</summary>
    public int MaxHeaderBytes { get; init => field = OptionLimits.Positive(value); } = CarLimits.DefaultMaxHeaderBytes;

    /// <summary>Whether each block's CID is recomputed from its data; a mismatch fails the read.</summary>
    public bool VerifyBlockCids { get; init; }
}
