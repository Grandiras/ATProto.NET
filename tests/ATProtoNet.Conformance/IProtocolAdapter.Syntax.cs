namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>
    /// Parses <paramref name="input"/> as an identifier of <paramref name="kind"/>, strictly
    /// (no trimming, no trailing line breaks).
    /// </summary>
    SyntaxVerdict ParseIdentifier(IdentifierKind kind, string input);

    /// <summary>
    /// Runs a TID generator with clock identifier <paramref name="clockId"/> over a scripted
    /// clock: its <c>i</c>th call reads <paramref name="clockMicros"/>[i], in microseconds since
    /// the Unix epoch, and returns that call's TID.
    /// </summary>
    IReadOnlyList<string> GenerateTids(int clockId, IReadOnlyList<long> clockMicros);
}

/// <summary>The identifier and string-format kinds of the syntax area.</summary>
public enum IdentifierKind
{
    /// <summary>A DID.</summary>
    Did,
    /// <summary>A handle.</summary>
    Handle,
    /// <summary>A DID or a handle.</summary>
    AtIdentifier,
    /// <summary>An NSID.</summary>
    Nsid,
    /// <summary>An AT-URI.</summary>
    AtUri,
    /// <summary>A record key.</summary>
    RecordKey,
    /// <summary>A TID.</summary>
    Tid,
    /// <summary>A Lexicon <c>datetime</c>.</summary>
    Datetime,
    /// <summary>A CID in its string form (the atproto-blessed CIDv1 subset).</summary>
    Cid,
    /// <summary>A Lexicon <c>language</c> (BCP 47 tag).</summary>
    Language,
    /// <summary>A Lexicon <c>uri</c>.</summary>
    Uri,
}

/// <summary>The outcome of parsing one identifier.</summary>
/// <param name="Valid">Whether the input was accepted.</param>
/// <param name="Normalized">The identifier's canonical text when accepted (handles are lower-cased).</param>
public sealed record SyntaxVerdict(bool Valid, string? Normalized = null)
{
    /// <summary>A rejection.</summary>
    public static SyntaxVerdict Invalid { get; } = new(false);
}
