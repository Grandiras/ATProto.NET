using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>A Lexicon <c>language</c>: a BCP 47 language tag such as <c>en</c>, <c>pt-BR</c> or <c>zh-Hant</c>.</summary>
/// <remarks>
/// Accepts the tags of RFC 5646 §2.1, including grandfathered and private-use tags, and refuses the well-formed but invalid ones (a
/// repeated variant or extension subtag). Subtags are case-insensitive, except that the primary language subtag must be lower case.
/// <see cref="Value"/> keeps the text as given; equality is ordinal and case-insensitive. The tag is not checked against the IANA registry.
/// See <see href="https://atproto.com/specs/lexicon#language"/>.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<Language>))]
public sealed class Language : IAtIdentifier<Language>
{
    /// <summary>The longest tag, in characters. RFC 5646 sets no maximum; this bounds parsing.</summary>
    public const int MaxLength = 128;

    private Language(string value) => Value = value;

    /// <inheritdoc />
    public string Value { get; }

    internal static bool TryRead<TChar>(ReadOnlySpan<TChar> text, string? value, [NotNullWhen(true)] out Language? result) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = null;
        if (text.Length > MaxLength) return false;
        Span<char> chars = stackalloc char[text.Length];
        for (var i = 0; i < chars.Length; i++)
        {
            var unit = Identifiers.Unit(text[i]);
            if (unit > 127) return false;
            chars[i] = (char)unit;
        }
        if (LanguageSyntax.IsLanguage(chars)) result = new(value ?? new string(chars));
        return result is not null;
    }

    /// <inheritdoc />
    public static Language Parse(string s) => Identifiers.Parse<Language>(s);

    /// <inheritdoc />
    public static Language Parse(ReadOnlySpan<char> s) => Identifiers.Parse<Language>(s);

    /// <inheritdoc />
    public static Language Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<Language>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out Language? result) => TryRead(s.AsSpan(), s, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out Language? result) => TryRead(s, null, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out Language? result) => TryRead(utf8Text, null, out result);

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] Language? other) => other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as Language);

    /// <inheritdoc />
    public override int GetHashCode() => string.GetHashCode(Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public int CompareTo(Language? other) => string.Compare(Value, other?.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The canonical text of <paramref name="value"/>.</summary>
    /// <param name="value">The identifier.</param>
    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator string?(Language? value) => value?.Value;

    /// <summary>Whether two identifiers are the same.</summary>
    public static bool operator ==(Language? left, Language? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two identifiers differ.</summary>
    public static bool operator !=(Language? left, Language? right) => !(left == right);

    /// <inheritdoc />
    static int IAtIdentifier<Language>.MaxLength => MaxLength;
}
