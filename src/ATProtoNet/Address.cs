using System.Numerics;
using System.Runtime.CompilerServices;
using static ATProtoNet.IdentifierSyntax;

namespace ATProtoNet;

// The one scanner for AT URIs and space addresses (SYN-75): the scheme, the marker segment and the segment
// layouts are stated here and nowhere else, so a change of the upstream space syntax (SYN-80) is a change
// to this file. A space address has the literal segment "space" where an AT URI has its collection, which
// can never be an NSID.
internal static class Address
{
    public const string Scheme = "at://", SpaceMarker = "space";
    public const int MaxLength = 8 * 1024;

    public enum Kind : byte { None, Public, Space, SpaceRecord }

    // Where each '/'-separated segment after the scheme ends in the text; -1 for the segments it lacks.
    [InlineArray(7)]
    public struct Ends
    {
        private int _end;
    }

    public static Kind Scan<TChar>(ReadOnlySpan<TChar> s, out Ends ends) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        ends = default;
        Span<int> end = ends;
        end.Fill(-1);
        if (s.Length > MaxLength || s.Length <= Scheme.Length || !Matches(s[..Scheme.Length], Scheme)) return Kind.None;

        var count = 0;
        for (var start = Scheme.Length; ;)
        {
            var slash = s[start..].IndexOf(TChar.CreateTruncating('/'));
            if (count == end.Length) return Kind.None;
            end[count++] = slash < 0 ? s.Length : start + slash;
            if (slash < 0) break;
            start += slash + 1;
        }

        var authority = At(s, end, 0);
        if (count < 2 || !Matches(At(s, end, 1), SpaceMarker))
            return count <= 3 && (StartsWithDid(authority) ? IsDid(authority) : IsHandle(authority, out _))
                && (count < 2 || IsNsid(At(s, end, 1))) && (count < 3 || IsRecordKey(At(s, end, 2))) ? Kind.Public : Kind.None;

        if (count is not (4 or 7) || !IsDid(authority) || !IsNsid(At(s, end, 2)) || !IsRecordKey(At(s, end, 3))) return Kind.None;
        if (count == 4) return Kind.Space;
        return IsDid(At(s, end, 4)) && IsNsid(At(s, end, 5)) && IsRecordKey(At(s, end, 6)) ? Kind.SpaceRecord : Kind.None;
    }

    private static ReadOnlySpan<TChar> At<TChar>(ReadOnlySpan<TChar> s, ReadOnlySpan<int> end, int i) =>
        s[(i == 0 ? Scheme.Length : end[i - 1] + 1)..end[i]];

    // The text of segment i of a scanned value.
    public static string Slice(string value, in Ends ends, int i) => value[(i == 0 ? Scheme.Length : ends[i - 1] + 1)..ends[i]];

    // The name of the address type text is a valid address of, if any.
    public static string? KindOf(string text) => Scan(text.AsSpan(), out _) switch
    {
        Kind.Public => nameof(AtUri),
        Kind.Space => nameof(SpaceUri),
        Kind.SpaceRecord => nameof(SpaceRecordUri),
        _ => null,
    };

    private static bool Matches<TChar>(ReadOnlySpan<TChar> s, string literal) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (s.Length != literal.Length) return false;
        for (var i = 0; i < s.Length; i++)
            if (Identifiers.Unit(s[i]) != literal[i]) return false;
        return true;
    }
}
