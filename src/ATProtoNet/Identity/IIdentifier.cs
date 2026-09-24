using System.Diagnostics.CodeAnalysis;

namespace ATProtoNet.Identity;

/// <summary>
/// The parsing contract shared by the string-backed identifier types. Each type states its
/// validation once, in <see cref="TryCreate"/>; this interface derives the
/// <see cref="IParsable{TSelf}"/> and <see cref="ISpanParsable{TSelf}"/> members from it, and
/// the one JSON converter for all identifiers binds to it.
/// </summary>
/// <typeparam name="TSelf">The identifier type.</typeparam>
internal interface IIdentifier<TSelf> : ISpanParsable<TSelf>, IEquatable<TSelf>, IComparable<TSelf>
    where TSelf : class, IIdentifier<TSelf>
{
    /// <summary>
    /// Validates <paramref name="span"/> and, when it is valid, creates the identifier.
    /// </summary>
    /// <param name="span">The candidate text.</param>
    /// <param name="text">
    /// The same text as a string when the caller already holds one, so a valid value can be
    /// stored without copying it; <see langword="null"/> when only the span is available.
    /// </param>
    /// <param name="result">The identifier on success.</param>
    static abstract bool TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out TSelf? result);

    /// <summary>
    /// The exception a public <c>Parse(string)</c> throws for <paramref name="value"/>:
    /// <see cref="ArgumentNullException"/> for <see langword="null"/>, otherwise
    /// <see cref="ArgumentException"/> naming the identifier <paramref name="kind"/>.
    /// </summary>
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
