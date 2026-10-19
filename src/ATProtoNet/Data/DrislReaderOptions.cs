namespace ATProtoNet.Data;

/// <summary>How strictly <see cref="DrislReader"/> applies the DRISL rules.</summary>
public enum DrislMode
{
    /// <summary>Every DRISL rule: the only mode for anything hashed, signed or compared by CID.</summary>
    Strict,

    /// <summary>Non-canonical but well-formed CBOR accepted (key order, lengths, unknown tags, invalid UTF-8), for display of foreign data.</summary>
    Lenient,
}

/// <summary>The kind of the next item a <see cref="DrislReader"/> reads.</summary>
public enum DrislValueKind
{
    /// <summary>The current map or array, or the top-level value, has no more items.</summary>
    End,

    /// <summary>A null.</summary>
    Null,

    /// <summary>A boolean.</summary>
    Boolean,

    /// <summary>A signed 64-bit integer.</summary>
    Integer,

    /// <summary>A text string (also every map key).</summary>
    String,

    /// <summary>A byte string.</summary>
    Bytes,

    /// <summary>A CID link (tag 42).</summary>
    Link,

    /// <summary>An array.</summary>
    Array,

    /// <summary>A map with string keys.</summary>
    Map,
}

/// <summary>The built-in limits of DRISL reading and of the data model's JSON form.</summary>
public static class DrislLimits
{
    /// <summary>The deepest nesting accepted: every map and array is a level, and so is the <c>$link</c> or <c>$bytes</c> wrapper of a link or byte string.</summary>
    public const int MaxDepth = 64;
}

/// <summary>Options of a <see cref="DrislReader"/>.</summary>
public sealed record DrislReaderOptions
{
    internal static readonly DrislReaderOptions Default = new();

    /// <summary>How strictly the DRISL rules apply; <see cref="DrislMode.Strict"/> unless the caller opts out.</summary>
    public DrislMode Mode { get; init; } = DrislMode.Strict;

    /// <summary>The deepest nesting accepted, between 1 and <see cref="DrislLimits.MaxDepth"/>.</summary>
    public int MaxDepth
    {
        get;
        init => field = value is > 0 and <= DrislLimits.MaxDepth ? value : throw new ArgumentOutOfRangeException(nameof(value), value, $"MaxDepth must be between 1 and {DrislLimits.MaxDepth}.");
    } = DrislLimits.MaxDepth;
}
