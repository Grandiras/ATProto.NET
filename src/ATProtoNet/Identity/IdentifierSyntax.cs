using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace ATProtoNet.Identity;

// The identifier grammars, checked by hand rather than by regular expression.
//
// Every grammar is ASCII-only, so each check runs unchanged on UTF-16 text (TChar = char) and on the raw
// UTF-8 bytes of a JSON string (TChar = byte): a non-ASCII unit is outside every character set. Each
// method states the regular expression it is equivalent to; IdentifierSyntaxTests holds it to that on
// generated input as well as on the interop fixtures. The character sets are checked with vectorized
// searches first, which leaves each grammar only its structure to check.
internal static class IdentifierSyntax
{
    private const int MaxDidLength = 2048;
    private const int MaxHandleLength = 253;
    private const int MaxNsidLength = 317;
    private const int MaxRecordKeyLength = 512;
    private const int MaxLabelLength = 63;
    private const int TidLength = 13;

    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Alphanumeric = Lower + Upper + "0123456789";

    private static readonly CharSet LowerLetters = new(Lower);
    private static readonly CharSet UpperLetters = new(Upper);

    // [a-zA-Z0-9._:%-], the characters of a DID's method-specific identifier.
    private static readonly CharSet DidIdChars = new(Alphanumeric + "._:%-");

    // [a-zA-Z0-9.-], the characters of a domain name, and so of a handle and an NSID.
    private static readonly CharSet DomainChars = new(Alphanumeric + ".-");

    // [A-Za-z0-9._:~-]
    private static readonly CharSet RecordKeyChars = new(Alphanumeric + "._:~-");

    // The base32-sortable alphabet of a TID.
    private static readonly CharSet TidChars = new("234567" + Lower);

    // ^did:[a-z]+:[a-zA-Z0-9._:%-]*[a-zA-Z0-9._-]\z, at most 2048 characters.
    public static bool IsDid<TChar>(ReadOnlySpan<TChar> s)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (s.Length is < 7 or > MaxDidLength
            || Unit(s[0]) != 'd' || Unit(s[1]) != 'i' || Unit(s[2]) != 'd' || Unit(s[3]) != ':')
            return false;

        // The method is followed by a ':', which LowerLetters stops at; -1 means it never ended.
        var method = LowerLetters.IndexOfAnyExcept(s[4..]);
        if (method <= 0 || Unit(s[4 + method]) != ':')
            return false;

        var id = s[(5 + method)..];
        return id.Length > 0 && DidIdChars.ContainsAll(id) && Unit(id[^1]) is not (':' or '%');
    }

    // ^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\z, at most
    // 253 characters: two or more dot-separated labels of 1-63 letters, digits and hyphens that neither
    // start nor end with a hyphen, the last one starting with a letter. hasUpper reports whether the
    // handle holds an upper-case letter, and so needs lower-casing.
    public static bool IsHandle<TChar>(ReadOnlySpan<TChar> s, out bool hasUpper)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        hasUpper = false;
        if (s.Length > MaxHandleLength || !DomainChars.ContainsAll(s))
            return false;

        var labels = 0;
        var rest = s;
        while (true)
        {
            var dot = rest.IndexOf(TChar.CreateTruncating('.'));
            var label = dot < 0 ? rest : rest[..dot];
            if (!IsLabel(label))
                return false;

            labels++;
            if (dot < 0)
            {
                // The top-level label starts with a letter.
                if (IsDigit(label[0]))
                    return false;
                break;
            }

            rest = rest[(dot + 1)..];
        }

        hasUpper = UpperLetters.ContainsAny(s);
        return labels >= 2;
    }

    // ^[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+(\.[a-zA-Z]([a-zA-Z0-9]{0,62})?)\z,
    // at most 317 characters: a domain authority of two or more labels (the first starting with a
    // letter), then a name of 1-63 letters and digits that starts with a letter.
    public static bool IsNsid<TChar>(ReadOnlySpan<TChar> s)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (s.Length > MaxNsidLength || !DomainChars.ContainsAll(s))
            return false;

        var segments = 0;
        var rest = s;
        while (true)
        {
            var dot = rest.IndexOf(TChar.CreateTruncating('.'));
            var segment = dot < 0 ? rest : rest[..dot];
            if (!IsLabel(segment))
                return false;

            // The first authority label and the name start with a letter.
            var name = dot < 0;
            if ((segments == 0 || name) && IsDigit(segment[0]))
                return false;

            segments++;
            if (name)
                return segments >= 3 && segment.IndexOf(TChar.CreateTruncating('-')) < 0;

            rest = rest[(dot + 1)..];
        }
    }

    // ^[A-Za-z0-9._:~-]{1,512}\z, other than "." and "..".
    public static bool IsRecordKey<TChar>(ReadOnlySpan<TChar> s)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (s.Length is < 1 or > MaxRecordKeyLength || !RecordKeyChars.ContainsAll(s))
            return false;

        // "." and ".." are relative path segments.
        return s.Length > 2 || Unit(s[0]) != '.' || (s.Length == 2 && Unit(s[1]) != '.');
    }

    // ^[234567abcdefghij][234567abcdefghijklmnopqrstuvwxyz]{12}\z: the first character carries the high
    // bit, which must be zero, so it is limited to the lower half of the alphabet.
    public static bool IsTid<TChar>(ReadOnlySpan<TChar> s)
        where TChar : unmanaged, IBinaryInteger<TChar>
        => s.Length == TidLength && Unit(s[0]) <= 'j' && TidChars.ContainsAll(s);

    // Creates the string of text this class has validated: ASCII, so every byte is one character.
    public static string ToAsciiString(ReadOnlySpan<byte> ascii) =>
        string.Create(ascii.Length, ascii, static (chars, bytes) => Ascii.ToUtf16(bytes, chars, out _));

    // A label of a domain name, whose characters are already known to be letters, digits, hyphens and
    // dots: 1-63 characters, neither starting nor ending with a hyphen.
    private static bool IsLabel<TChar>(ReadOnlySpan<TChar> label)
        where TChar : unmanaged, IBinaryInteger<TChar>
        => label.Length is >= 1 and <= MaxLabelLength && Unit(label[0]) != '-' && Unit(label[^1]) != '-';

    private static bool IsDigit<TChar>(TChar c)
        where TChar : unmanaged, IBinaryInteger<TChar>
        => Unit(c) - '0' <= 9;

    private static uint Unit<TChar>(TChar c)
        where TChar : unmanaged, IBinaryInteger<TChar>
        => uint.CreateTruncating(c);

    // A set of ASCII characters, searchable in UTF-16 text and in UTF-8 bytes alike.
    private sealed class CharSet(string chars)
    {
        private readonly SearchValues<char> _utf16 = SearchValues.Create(chars);
        private readonly SearchValues<byte> _utf8 = SearchValues.Create(Encoding.ASCII.GetBytes(chars));

        public bool ContainsAll<TChar>(ReadOnlySpan<TChar> s)
            where TChar : unmanaged, IBinaryInteger<TChar>
            => IndexOfAnyExcept(s) < 0;

        public bool ContainsAny<TChar>(ReadOnlySpan<TChar> s)
            where TChar : unmanaged, IBinaryInteger<TChar>
            => typeof(TChar) == typeof(char)
                ? MemoryMarshal.Cast<TChar, char>(s).IndexOfAny(_utf16) >= 0
                : MemoryMarshal.Cast<TChar, byte>(s).IndexOfAny(_utf8) >= 0;

        // The index of the first unit of s outside the set, or -1 when there is none.
        public int IndexOfAnyExcept<TChar>(ReadOnlySpan<TChar> s)
            where TChar : unmanaged, IBinaryInteger<TChar>
            => typeof(TChar) == typeof(char)
                ? MemoryMarshal.Cast<TChar, char>(s).IndexOfAnyExcept(_utf16)
                : MemoryMarshal.Cast<TChar, byte>(s).IndexOfAnyExcept(_utf8);
    }
}
