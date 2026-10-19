using System.Buffers;

namespace ATProtoNet;

/// <summary>The Lexicon <c>uri</c> string format: a URI with a scheme, whatever the scheme.</summary>
/// <remarks>
/// RFC 3986: a scheme (a letter, then letters, digits, <c>+</c>, <c>-</c> and <c>.</c>), a colon and a non-empty remainder of printable
/// ASCII, so no whitespace. See <see href="https://atproto.com/specs/lexicon#uri"/>.
/// </remarks>
public static class UriFormat
{
    /// <summary>The longest URI, in characters.</summary>
    public const int MaxLength = 8192;

    private static readonly SearchValues<char> SchemeChars = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+-.");

    /// <summary>Whether <paramref name="value"/> is a valid Lexicon <c>uri</c>.</summary>
    /// <param name="value">The text to check.</param>
    /// <returns>Whether the text is a URI with a scheme.</returns>
    public static bool IsValid(ReadOnlySpan<char> value)
    {
        if (value.Length > MaxLength) return false;
        var colon = value.IndexOf(':');
        return colon > 0 && char.IsAsciiLetter(value[0]) && !value[..colon].ContainsAnyExcept(SchemeChars)
            && colon + 1 < value.Length && !value[(colon + 1)..].ContainsAnyExceptInRange('!', '~');
    }
}
