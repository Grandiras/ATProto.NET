using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace ATProtoNet;

// What the identifier types share: the Parse bodies, the text of a validated span, the error message.
// Every grammar is ASCII-only, so one scanner serves UTF-16 text (TChar = char) and UTF-8 bytes (TChar = byte).
internal static class Identifiers
{
    private const int ShownLength = 64;

    public static T Parse<T>(string s) where T : IAtIdentifier<T>
    {
        ArgumentNullException.ThrowIfNull(s);
        return T.TryParse(s, out var result) ? result : throw Invalid<T>(s);
    }

    public static T Parse<T>(ReadOnlySpan<char> s) where T : IAtIdentifier<T> =>
        T.TryParse(s, out var result) ? result : throw Invalid<T>(s.ToString());

    public static T Parse<T>(ReadOnlySpan<byte> utf8Text) where T : IAtIdentifier<T> =>
        T.TryParse(utf8Text, out var result) ? result : throw Invalid<T>(Encoding.UTF8.GetString(utf8Text));

    // The exception Parse and the JSON converters raise. An address of another kind is named, so
    // AtUri.Parse of a space address says what it was given.
    public static FormatException Invalid<T>(string text)
    {
        var shown = text.Length > ShownLength ? text[..ShownLength] + "…" : text;
        var message = $"'{shown}' is not a valid {typeof(T).Name}.";
        return new(Address.KindOf(text) is { } kind ? $"{message} It is a {kind}." : message);
    }

    // The string of text a scanner has validated.
    public static string Text<TChar>(ReadOnlySpan<TChar> text) where TChar : unmanaged, IBinaryInteger<TChar> =>
        typeof(TChar) == typeof(char)
            ? new(MemoryMarshal.Cast<TChar, char>(text))
            : string.Create(text.Length, text, static (chars, bytes) => Ascii.ToUtf16(MemoryMarshal.Cast<TChar, byte>(bytes), chars, out _));

    public static uint Unit<TChar>(TChar c) where TChar : unmanaged, IBinaryInteger<TChar> => uint.CreateTruncating(c);
}
