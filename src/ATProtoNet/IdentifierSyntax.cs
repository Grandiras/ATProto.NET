using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using static ATProtoNet.Identifiers;

namespace ATProtoNet;

// The identifier grammars, checked by hand rather than by regular expression. Each method states the
// reference expression it is equivalent to; the conformance suite holds it to that (SYN-8). Lengths
// are checked before anything is scanned (SYN-9), and a character set is searched vectorized first, so
// each grammar is left only its structure.
internal static class IdentifierSyntax
{
    private const string Lower = "abcdefghijklmnopqrstuvwxyz", Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Alphanumeric = Lower + Upper + "0123456789";

    private static readonly CharSet LowerLetters = new(Lower);
    private static readonly CharSet UpperLetters = new(Upper);
    private static readonly CharSet DidIdChars = new(Alphanumeric + "._:%-");
    private static readonly CharSet DomainChars = new(Alphanumeric + ".-");
    private static readonly CharSet RecordKeyChars = new(Alphanumeric + "._:~-");
    private static readonly CharSet TidChars = new("234567" + Lower);

    // ^did:[a-z]+:[a-zA-Z0-9._:%-]*[a-zA-Z0-9._-]\z, 7 to 2048 characters.
    public static bool IsDid<TChar>(ReadOnlySpan<TChar> s) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (s.Length is < Did.MinLength or > Did.MaxLength || Unit(s[0]) != 'd' || Unit(s[1]) != 'i' || Unit(s[2]) != 'd' || Unit(s[3]) != ':') return false;
        var method = LowerLetters.IndexOfAnyExcept(s[4..]);
        if (method <= 0 || Unit(s[4 + method]) != ':') return false;
        var id = s[(5 + method)..];
        return id.Length > 0 && DidIdChars.ContainsAll(id) && Unit(id[^1]) is not (':' or '%');
    }

    // ^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\z, at most 253
    // characters. hasUpper reports whether the handle needs lower-casing.
    public static bool IsHandle<TChar>(ReadOnlySpan<TChar> s, out bool hasUpper) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        hasUpper = false;
        if (s.Length > Handle.MaxLength || !DomainChars.ContainsAll(s)) return false;
        var labels = 0;
        var rest = s;
        while (true)
        {
            var dot = rest.IndexOf(TChar.CreateTruncating('.'));
            var label = dot < 0 ? rest : rest[..dot];
            if (!IsLabel(label)) return false;
            labels++;
            if (dot >= 0)
            {
                rest = rest[(dot + 1)..];
                continue;
            }
            if (IsDigit(label[0])) return false;
            hasUpper = UpperLetters.ContainsAny(s);
            return labels >= 2;
        }
    }

    // ^[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+(\.[a-zA-Z]([a-zA-Z0-9]{0,62})?)\z,
    // at most 317 characters.
    public static bool IsNsid<TChar>(ReadOnlySpan<TChar> s) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (s.Length > Nsid.MaxLength || !DomainChars.ContainsAll(s)) return false;
        var segments = 0;
        var rest = s;
        while (true)
        {
            var dot = rest.IndexOf(TChar.CreateTruncating('.'));
            var segment = dot < 0 ? rest : rest[..dot];
            var isName = dot < 0;
            if (!IsLabel(segment) || ((segments == 0 || isName) && IsDigit(segment[0]))) return false;
            segments++;
            if (!isName)
            {
                rest = rest[(dot + 1)..];
                continue;
            }
            return segments >= 3 && segment.IndexOf(TChar.CreateTruncating('-')) < 0;
        }
    }

    // ^[A-Za-z0-9._:~-]{1,512}\z, other than "." and "..".
    public static bool IsRecordKey<TChar>(ReadOnlySpan<TChar> s) where TChar : unmanaged, IBinaryInteger<TChar> =>
        s.Length is >= 1 and <= RecordKey.MaxLength && RecordKeyChars.ContainsAll(s)
        && (s.Length > 2 || Unit(s[0]) != '.' || (s.Length == 2 && Unit(s[1]) != '.'));

    // ^[234567abcdefghij][234567abcdefghijklmnopqrstuvwxyz]{12}\z: the first character carries the high bit, which is zero.
    public static bool IsTid<TChar>(ReadOnlySpan<TChar> s) where TChar : unmanaged, IBinaryInteger<TChar> =>
        s.Length == Tid.Length && Unit(s[0]) <= 'j' && TidChars.ContainsAll(s);

    public static bool StartsWithDid<TChar>(ReadOnlySpan<TChar> s) where TChar : unmanaged, IBinaryInteger<TChar> =>
        s.Length >= 4 && Unit(s[0]) == 'd' && Unit(s[1]) == 'i' && Unit(s[2]) == 'd' && Unit(s[3]) == ':';

    // A label of a domain name whose characters are known to be letters, digits, hyphens and dots.
    private static bool IsLabel<TChar>(ReadOnlySpan<TChar> label) where TChar : unmanaged, IBinaryInteger<TChar> =>
        label.Length is >= 1 and <= Handle.MaxLabelLength && Unit(label[0]) != '-' && Unit(label[^1]) != '-';

    private static bool IsDigit<TChar>(TChar c) where TChar : unmanaged, IBinaryInteger<TChar> => Unit(c) - '0' <= 9;

    // A set of ASCII characters, searchable in UTF-16 text and in UTF-8 bytes alike.
    private sealed class CharSet(string chars)
    {
        private readonly SearchValues<char> _utf16 = SearchValues.Create(chars);
        private readonly SearchValues<byte> _utf8 = SearchValues.Create(Encoding.ASCII.GetBytes(chars));

        public bool ContainsAll<TChar>(ReadOnlySpan<TChar> s) where TChar : unmanaged, IBinaryInteger<TChar> => IndexOfAnyExcept(s) < 0;

        public bool ContainsAny<TChar>(ReadOnlySpan<TChar> s) where TChar : unmanaged, IBinaryInteger<TChar> =>
            typeof(TChar) == typeof(char) ? MemoryMarshal.Cast<TChar, char>(s).IndexOfAny(_utf16) >= 0 : MemoryMarshal.Cast<TChar, byte>(s).IndexOfAny(_utf8) >= 0;

        // The index of the first unit outside the set, or -1.
        public int IndexOfAnyExcept<TChar>(ReadOnlySpan<TChar> s) where TChar : unmanaged, IBinaryInteger<TChar> =>
            typeof(TChar) == typeof(char) ? MemoryMarshal.Cast<TChar, char>(s).IndexOfAnyExcept(_utf16) : MemoryMarshal.Cast<TChar, byte>(s).IndexOfAnyExcept(_utf8);
    }
}
