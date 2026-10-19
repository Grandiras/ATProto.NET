using System.Numerics;
using static ATProtoNet.Identifiers;

namespace ATProtoNet;

// The Lexicon datetime grammar (SYN-41): RFC 3339 restricted to what ISO 8601 and WHATWG HTML also allow. Ticks count from
// 0000-01-01T00:00:00Z in the proleptic Gregorian calendar, because years 0000 to 0009 are valid and DateTime cannot hold them.
internal static class AtDatetimeSyntax
{
    private const string Shortest = "yyyy-MM-ddTHH:mm:ssZ";

    // Validates the grammar, then the calendar. offsetMinutes is the written offset.
    public static bool TryParse<TChar>(ReadOnlySpan<TChar> s, out long ticks, out int offsetMinutes) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        ticks = 0;
        offsetMinutes = 0;
        if (s.Length < Shortest.Length || s.Length > AtDatetime.MaxLength
            || Unit(s[4]) != '-' || Unit(s[7]) != '-' || Unit(s[10]) != 'T' || Unit(s[13]) != ':' || Unit(s[16]) != ':')
            return false;

        if (!Digits(s, 0, 4, out var year) | !Digits(s, 5, 2, out var month) | !Digits(s, 8, 2, out var day)
            | !Digits(s, 11, 2, out var hour) | !Digits(s, 14, 2, out var minute) | !Digits(s, 17, 2, out var second))
            return false;

        var at = 19;
        long fractionTicks = 0;
        if (Unit(s[at]) == '.')
        {
            var start = ++at;
            while (at < s.Length && Digit(s[at]) <= 9)
            {
                if (at - start < 7) fractionTicks = fractionTicks * 10 + Digit(s[at]);
                at++;
            }
            var digits = at - start;
            if (digits is < 1 or > AtDatetime.MaxFractionDigits) return false;
            for (var i = Math.Min(digits, 7); i < 7; i++) fractionTicks *= 10;
        }

        if (!TryOffset(s[at..], out offsetMinutes)) return false;
        if (month is < 1 or > 12 || day < 1 || hour > 23 || minute > 59 || second > 59 || day > DaysInMonth(year, month)) return false;

        var leapDay = month > 2 && IsLeapYear(year) ? 1 : 0;
        var localTicks = (DaysBeforeYear(year) + CommonDaysBeforeMonth[month - 1] + leapDay + day - 1) * TimeSpan.TicksPerDay
            + hour * TimeSpan.TicksPerHour + minute * TimeSpan.TicksPerMinute + second * TimeSpan.TicksPerSecond + fractionTicks;
        ticks = localTicks - offsetMinutes * TimeSpan.TicksPerMinute;

        // "0000-01-01T00:00:00+01:00" is before year zero.
        return ticks >= 0;
    }

    // Z or ±HH:MM, but not -00:00, which RFC 3339 reserves for "offset unknown".
    private static bool TryOffset<TChar>(ReadOnlySpan<TChar> zone, out int minutes) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        minutes = 0;
        if (zone.Length == 1) return Unit(zone[0]) == 'Z';
        if (zone.Length != 6 || Unit(zone[0]) is not ('+' or '-') || Unit(zone[3]) != ':'
            || !Digits(zone, 1, 2, out var hours) || !Digits(zone, 4, 2, out var mins) || hours > 23 || mins > 59)
            return false;
        var negative = Unit(zone[0]) == '-';
        if (negative && hours == 0 && mins == 0) return false;
        minutes = (hours * 60 + mins) * (negative ? -1 : 1);
        return true;
    }

    private static uint Digit<TChar>(TChar c) where TChar : unmanaged, IBinaryInteger<TChar> => Unit(c) - '0';

    private static bool Digits<TChar>(ReadOnlySpan<TChar> s, int start, int count, out int value) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        value = 0;
        foreach (var c in s.Slice(start, count))
        {
            var digit = Digit(c);
            if (digit > 9) return false;
            value = value * 10 + (int)digit;
        }
        return true;
    }

    private static bool IsLeapYear(int year) => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

    private static long DaysBeforeYear(int year) => year * 365L + (year + 3) / 4 - (year + 99) / 100 + (year + 399) / 400;

    private static int DaysInMonth(int year, int month) => month == 2 && IsLeapYear(year) ? 29 : CommonDaysInMonth[month - 1];

    private static ReadOnlySpan<byte> CommonDaysInMonth => [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    private static ReadOnlySpan<short> CommonDaysBeforeMonth => [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];
}
