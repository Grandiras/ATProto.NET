using System.Buffers;
using System.Collections.Frozen;

namespace ATProtoNet;

// RFC 5646 §2.1 language tags: langtag, privateuse and the grandfathered tags the grammar does not cover. Subtags are
// case-insensitive, except that the primary language subtag is lower case (the interop vectors reject "JA"); the four-letter
// primary subtags RFC 5646 reserves are refused ("jaja"), as are repeated variant and extension subtags (§2.2.5-2.2.6).
internal static class LanguageSyntax
{
    private static readonly FrozenSet<string> Grandfathered = FrozenSet.ToFrozenSet(
    [
        "art-lojban", "cel-gaulish", "en-GB-oed", "i-ami", "i-bnn", "i-default", "i-enochian", "i-hak", "i-klingon", "i-lux", "i-mingo",
        "i-navajo", "i-pwn", "i-tao", "i-tay", "i-tsu", "no-bok", "no-nyn", "sgn-BE-FR", "sgn-BE-NL", "sgn-CH-DE", "zh-guoyu", "zh-hakka",
        "zh-min", "zh-min-nan", "zh-xiang",
    ], StringComparer.OrdinalIgnoreCase);

    private enum Stage { Language, Extlang, Script, Region, Variant, Extension, PrivateUse }

    public static bool IsLanguage(ReadOnlySpan<char> tag)
    {
        if (tag.Length is 0 or > Language.MaxLength) return false;
        if (Grandfathered.GetAlternateLookup<ReadOnlySpan<char>>().Contains(tag)) return true;

        var stage = Stage.Language;
        var canHaveExtlang = false;
        var first = true;
        int extlangs = 0, extensionSubtags = 0, seenCount = 0;
        var needsPrivateUse = false;
        Span<Range> seen = stackalloc Range[Language.MaxLength / 2];

        foreach (var range in tag.Split('-'))
        {
            var sub = tag[range];
            if (first)
            {
                first = false;
                if (sub is ['x' or 'X']) (stage, needsPrivateUse) = (Stage.PrivateUse, true);
                else if (IsLowerLetters(sub) && sub.Length is 2 or 3) canHaveExtlang = true;
                else if (!(IsLowerLetters(sub) && sub.Length is >= 5 and <= 8)) return false;
                continue;
            }

            if (stage == Stage.PrivateUse)
            {
                if (sub.Length is < 1 or > 8 || !IsAlphanumeric(sub)) return false;
                needsPrivateUse = false;
            }
            else if (stage == Stage.Extension && sub.Length >= 2)
            {
                if (sub.Length > 8 || !IsAlphanumeric(sub)) return false;
                extensionSubtags++;
            }
            else if (sub.Length == 1)
            {
                if (stage == Stage.Extension && extensionSubtags == 0) return false;
                if (sub[0] is 'x' or 'X') (stage, needsPrivateUse) = (Stage.PrivateUse, true);
                else if (IsAlphanumeric(sub) && !Seen(tag, seen[..seenCount], sub)) (stage, extensionSubtags, seen[seenCount++]) = (Stage.Extension, 0, range);
                else return false;
            }
            else if (stage <= Stage.Extlang && canHaveExtlang && extlangs < 3 && sub.Length == 3 && IsLetters(sub)) (stage, extlangs) = (Stage.Extlang, extlangs + 1);
            else if (stage <= Stage.Extlang && sub.Length == 4 && IsLetters(sub)) stage = Stage.Script;
            else if (stage <= Stage.Script && (sub.Length == 2 && IsLetters(sub) || sub.Length == 3 && IsDigits(sub))) stage = Stage.Region;
            else if (stage <= Stage.Variant && IsVariant(sub) && !Seen(tag, seen[..seenCount], sub)) (stage, seen[seenCount++]) = (Stage.Variant, range);
            else return false;
        }

        return !needsPrivateUse && (stage != Stage.Extension || extensionSubtags > 0);
    }

    // 5*8alphanum / (DIGIT 3alphanum)
    private static bool IsVariant(ReadOnlySpan<char> sub) =>
        IsAlphanumeric(sub) && (sub.Length is >= 5 and <= 8 || sub.Length == 4 && char.IsAsciiDigit(sub[0]));

    private static bool Seen(ReadOnlySpan<char> tag, ReadOnlySpan<Range> seen, ReadOnlySpan<char> sub)
    {
        foreach (var range in seen)
            if (tag[range].Equals(sub, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsLowerLetters(ReadOnlySpan<char> s) => !s.IsEmpty && !s.ContainsAnyExceptInRange('a', 'z');

    private static bool IsLetters(ReadOnlySpan<char> s) => !s.IsEmpty && !s.ContainsAnyExcept(Letters);

    private static bool IsDigits(ReadOnlySpan<char> s) => !s.IsEmpty && !s.ContainsAnyExceptInRange('0', '9');

    private static bool IsAlphanumeric(ReadOnlySpan<char> s) => !s.IsEmpty && !s.ContainsAnyExcept(Alphanumeric);

    private static readonly SearchValues<char> Letters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");
    private static readonly SearchValues<char> Alphanumeric = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
}
