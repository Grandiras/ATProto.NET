using System.Diagnostics.CodeAnalysis;

namespace ATProtoNet.Identity;

// The parsing contract shared by the string-backed identifier types. Each type states its validation
// once, in TryCreate; this interface derives the IParsable and ISpanParsable members from it, and the
// one JSON converter for all identifiers binds to it.
//
// TSelf: The identifier type.
internal interface IIdentifier<TSelf> : ISpanParsable<TSelf>, IEquatable<TSelf>, IComparable<TSelf>
    where TSelf : class, IIdentifier<TSelf>
{
    // Validates span and, when it is valid, creates the identifier.
    //
    // span: The candidate text.
    //
    // text: The same text as a string when the caller already holds one, so a valid value can be stored
    // without copying it; null when only the span is available.
    //
    // result: The identifier on success.
    static abstract bool TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out TSelf? result);

    // Creates the identifier straight from the unescaped UTF-8 bytes of a JSON string, without the UTF-16
    // string the reader would otherwise decode first. False only means this path produced nothing — the
    // bytes are not a valid identifier, or the type has no such path — and the caller falls back to
    // TryCreate on the decoded string, which reports the error.
    static virtual bool TryCreate(ReadOnlySpan<byte> utf8, [NotNullWhen(true)] out TSelf? result)
    {
        result = null;
        return false;
    }

    // The exception a public Parse(string) throws for value: ArgumentNullException for null, otherwise
    // ArgumentException naming the identifier kind.
    static ArgumentException InvalidValue(string? value, string kind) => value is null
        ? new ArgumentNullException(nameof(value))
        : new ArgumentException($"Invalid {kind}: '{value}'.", nameof(value));

    static TSelf IParsable<TSelf>.Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return TSelf.TryCreate(s, s, out var result) ? result : throw InvalidFormat(s);
    }

    static bool IParsable<TSelf>.TryParse(
        [NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
    {
        if (s is null)
        {
            result = null;
            return false;
        }

        return TSelf.TryCreate(s, s, out result);
    }

    static TSelf ISpanParsable<TSelf>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        TSelf.TryCreate(s, null, out var result) ? result : throw InvalidFormat(s.ToString());

    static bool ISpanParsable<TSelf>.TryParse(
        ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result) =>
        TSelf.TryCreate(s, null, out result);

    // IParsable's contract is FormatException; the types' own Parse methods keep ArgumentException.
    private static FormatException InvalidFormat(string value) =>
        new($"'{value}' is not a valid {typeof(TSelf).Name}.");
}
