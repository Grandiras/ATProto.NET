using System.Diagnostics.CodeAnalysis;

namespace ATProtoNet;

/// <summary>The contract every atproto identifier type implements, for code that is generic over them.</summary>
/// <typeparam name="TSelf">The identifier type.</typeparam>
/// <remarks>
/// An identifier validates on construction and cannot be invalid. <c>Parse</c> throws
/// <see cref="FormatException"/> for invalid input and <see cref="ArgumentNullException"/> for
/// <see langword="null"/>; <c>TryParse</c> never throws. The <see cref="string"/>,
/// <see cref="ReadOnlySpan{T}"/> of <see cref="char"/> and UTF-8 forms accept exactly the same inputs.
/// The <see cref="IParsable{TSelf}"/>, <see cref="ISpanParsable{TSelf}"/> and
/// <see cref="IUtf8SpanParsable{TSelf}"/> members delegate to them and ignore the format provider.
/// </remarks>
public interface IAtIdentifier<TSelf> : ISpanParsable<TSelf>, IUtf8SpanParsable<TSelf>, IEquatable<TSelf>, IComparable<TSelf>
    where TSelf : IAtIdentifier<TSelf>
{
    /// <summary>The canonical text of the identifier.</summary>
    string Value { get; }

    /// <summary>The longest text the type accepts, in characters.</summary>
    static abstract int MaxLength { get; }

    /// <summary>Parses <paramref name="s"/>.</summary>
    /// <param name="s">The text to parse.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="s"/> is not a valid value.</exception>
    static abstract TSelf Parse(string s);

    /// <summary>Parses <paramref name="s"/>.</summary>
    /// <param name="s">The text to parse.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="FormatException"><paramref name="s"/> is not a valid value.</exception>
    static abstract TSelf Parse(ReadOnlySpan<char> s);

    /// <summary>Parses UTF-8 <paramref name="utf8Text"/>.</summary>
    /// <param name="utf8Text">The UTF-8 text to parse.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="FormatException"><paramref name="utf8Text"/> is not a valid value.</exception>
    static abstract TSelf Parse(ReadOnlySpan<byte> utf8Text);

    /// <summary>Parses <paramref name="s"/> without throwing.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="result">The identifier, when the text is valid.</param>
    /// <returns>Whether <paramref name="s"/> is a valid value.</returns>
    static abstract bool TryParse([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out TSelf result);

    /// <summary>Parses <paramref name="s"/> without throwing.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="result">The identifier, when the text is valid.</param>
    /// <returns>Whether <paramref name="s"/> is a valid value.</returns>
    static abstract bool TryParse(ReadOnlySpan<char> s, [MaybeNullWhen(false)] out TSelf result);

    /// <summary>Parses UTF-8 <paramref name="utf8Text"/> without throwing.</summary>
    /// <param name="utf8Text">The UTF-8 text to parse.</param>
    /// <param name="result">The identifier, when the text is valid.</param>
    /// <returns>Whether <paramref name="utf8Text"/> is a valid value.</returns>
    static abstract bool TryParse(ReadOnlySpan<byte> utf8Text, [MaybeNullWhen(false)] out TSelf result);

    static TSelf IParsable<TSelf>.Parse(string s, IFormatProvider? provider) => TSelf.Parse(s);

    static bool IParsable<TSelf>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result) =>
        TSelf.TryParse(s, out result);

    static TSelf ISpanParsable<TSelf>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => TSelf.Parse(s);

    static bool ISpanParsable<TSelf>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result) =>
        TSelf.TryParse(s, out result);

    static TSelf IUtf8SpanParsable<TSelf>.Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => TSelf.Parse(utf8Text);

    static bool IUtf8SpanParsable<TSelf>.TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result) =>
        TSelf.TryParse(utf8Text, out result);
}
