using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>A Lexicon <c>datetime</c> value: an RFC 3339 timestamp that keeps the exact text it was read or created from, so a record written back re-serializes byte for byte and keeps its CID.</summary>
/// <remarks>
/// <para><b>Strict construction, lenient reading.</b> <see cref="Parse"/> and
/// <see cref="TryParse"/> accept only valid atproto datetimes (checked against the
/// <c>atproto-interop-tests</c> fixtures), and <see cref="Now"/>, <see cref="FromDateTimeOffset"/>
/// and <see cref="FromDateTime"/> always write the canonical form
/// <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>. The JSON converter, by contrast, never rejects a string:
/// records in the wild carry timestamps without a time zone, with a lower-case <c>z</c> and
/// worse, and one such value must not make a whole response unreadable. Such a value is kept
/// verbatim with <see cref="IsValid"/> <see langword="false"/>; <see cref="TryGetValue"/> still
/// recovers the instant from a near-miss ISO 8601 form (a missing time zone is read as UTC).</para>
/// <para><b>Equality</b> is ordinal on the text, like the identifier types: two spellings of one
/// instant are different values, because they produce different records. <b>Ordering</b> is by
/// instant, with the text breaking ties so that it agrees with equality; values without an
/// instant sort first.</para>
/// <para>The <see langword="default"/> value has no text. It is neither valid nor serializable;
/// initialize a datetime with <see cref="Now"/>, <see cref="Parse"/> or
/// <see cref="FromDateTimeOffset"/>.</para>
/// </remarks>
[JsonConverter(typeof(AtDatetimeJsonConverter))]
public readonly record struct AtDatetime : ISpanParsable<AtDatetime>, IComparable<AtDatetime>, IComparable
{
    // The spec's upper bound on the text; it keeps a hostile value from costing anything.
    private const int MaxLength = 64;

    // Instants are held as ticks since 0000-01-01T00:00:00Z in the proleptic Gregorian calendar,
    // because atproto allows years 0000-0009 that DateTime cannot represent. Year 0 is a leap
    // year, so DateTime's tick 0 (0001-01-01) is 366 days in.
    private const long Year1Ticks = 366 * TimeSpan.TicksPerDay;

    // A DateTimeOffset's offset is limited to ±14 hours; RFC 3339 allows more.
    private const int MaxClrOffsetMinutes = 14 * 60;

    private const string CanonicalFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    // The instant's ticks, below 2^62 for any year the syntax allows, share a word with the State in the
    // top two bits, so that a datetime field in a model is two words wide. The offset is not stored:
    // the text holds it.
    private const int StateShift = 62;
    private const long TicksMask = (1L << StateShift) - 1;

    private readonly string? _text;
    private readonly long _ticksAndState;

    private AtDatetime(string text, long ticks, State state)
    {
        _text = text;
        _ticksAndState = ticks | ((long)state << StateShift);
    }

    private enum State : byte
    {
        // default(AtDatetime): no text at all.
        None,

        // Text that no parser could read as a date.
        Unreadable,

        // Not a valid atproto datetime, but a near-miss ISO 8601 form with a known instant.
        Lenient,

        // A valid atproto datetime.
        Valid,
    }

    /// <summary>Whether the text is a valid atproto datetime. Always <see langword="true"/> for a value created from code; a value read from the wire may not be.</summary>
    public bool IsValid => CurrentState == State.Valid;

    private State CurrentState => (State)((ulong)_ticksAndState >> StateShift);

    private long Ticks => _ticksAndState & TicksMask;

    /// <summary>The instant the text denotes, in the offset it was written with.</summary>
    /// <remarks>
    /// Precision is 100 ns; further fractional digits are truncated. An offset beyond the
    /// ±14:00 a <see cref="DateTimeOffset"/> can hold is normalized to UTC.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The text does not denote an instant (see <see cref="TryGetValue"/>), or denotes one
    /// outside the <see cref="DateTimeOffset"/> range, such as a date in year 0000.
    /// </exception>
    public DateTimeOffset Value => TryGetValue(out var value)
        ? value
        : throw new InvalidOperationException(_text is null
            ? "An uninitialized AtDatetime has no value."
            : $"'{_text}' does not denote an instant a DateTimeOffset can represent.");

    /// <summary>Gets the instant the text denotes, when it denotes one <see cref="DateTimeOffset"/> can represent.</summary>
    /// <param name="value">The instant, in the offset it was written with.</param>
    /// <returns>
    /// <see langword="true"/> for every valid value in the <see cref="DateTimeOffset"/> range
    /// and for near-miss ISO 8601 text read from the wire; <see langword="false"/> otherwise.
    /// </returns>
    public bool TryGetValue(out DateTimeOffset value)
    {
        value = default;
        if (CurrentState < State.Lenient)
            return false;

        var utcTicks = Ticks - Year1Ticks;
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
            return false;

        var offsetMinutes = OffsetMinutes();
        var offsetTicks = offsetMinutes * TimeSpan.TicksPerMinute;
        var localTicks = utcTicks + offsetTicks;
        value = Math.Abs(offsetMinutes) <= MaxClrOffsetMinutes
                && localTicks >= DateTime.MinValue.Ticks && localTicks <= DateTime.MaxValue.Ticks
            ? new DateTimeOffset(localTicks, TimeSpan.FromTicks(offsetTicks))
            : new DateTimeOffset(utcTicks, TimeSpan.Zero);
        return true;
    }

    // The offset the text was written with, read back from it. A valid datetime ends in Z or ±HH:MM; a
    // lenient reading's offset is the one the framework's parser found in it on the way in.
    private int OffsetMinutes()
    {
        var text = _text!;
        if (CurrentState != State.Valid)
        {
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var lenient)
                ? (int)lenient.Offset.TotalMinutes
                : 0;
        }

        if (text[^1] == 'Z')
            return 0;

        var minutes = ((text[^5] - '0') * 10 + (text[^4] - '0')) * 60 + (text[^2] - '0') * 10 + (text[^1] - '0');
        return text[^6] == '-' ? -minutes : minutes;
    }

    /// <summary>The current time as a canonical atproto datetime (UTC, millisecond precision).</summary>
    /// <returns>The current time.</returns>
    public static AtDatetime Now() => FromDateTimeOffset(DateTimeOffset.UtcNow);

    /// <summary>Creates the canonical atproto datetime for an instant: UTC, millisecond precision, <c>yyyy-MM-ddTHH:mm:ss.fffZ</c> in the invariant culture.</summary>
    /// <param name="value">The instant. Sub-millisecond precision is truncated.</param>
    /// <returns>The datetime.</returns>
    /// <remarks>
    /// A <see cref="DateTime"/> argument converts implicitly with <see cref="DateTimeOffset"/>'s
    /// rules, which read <see cref="DateTimeKind.Unspecified"/> as local time. Call
    /// <see cref="FromDateTime"/> instead to read it as UTC.
    /// </remarks>
    public static AtDatetime FromDateTimeOffset(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        utc = utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMillisecond));

        // The invariant culture: under the current one, a Thai or Japanese-era locale would
        // write its own calendar's year, and some locales a different time separator.
        var text = utc.ToString(CanonicalFormat, CultureInfo.InvariantCulture);
        return new AtDatetime(text, utc.Ticks + Year1Ticks, State.Valid);
    }

    /// <summary>Creates the canonical atproto datetime for a <see cref="DateTime"/>, as <see cref="FromDateTimeOffset"/> does.</summary>
    /// <param name="value">
    /// The date and time. <see cref="DateTimeKind.Local"/> is converted to UTC;
    /// <see cref="DateTimeKind.Unspecified"/> is taken to be UTC already.
    /// </param>
    /// <returns>The datetime.</returns>
    public static AtDatetime FromDateTime(DateTime value) =>
        FromDateTimeOffset(value.Kind == DateTimeKind.Local
            ? new DateTimeOffset(value)
            : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    /// <summary>Parses a valid atproto datetime, keeping its text exactly as given.</summary>
    /// <param name="value">The datetime text.</param>
    /// <returns>The datetime.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a valid atproto datetime.</exception>
    public static AtDatetime Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryCreate(value, value, out var result)
            ? result
            : throw new ArgumentException($"Invalid datetime: '{value}'.", nameof(value));
    }

    /// <summary>Attempts to parse a valid atproto datetime, keeping its text exactly as given.</summary>
    /// <param name="value">The datetime text.</param>
    /// <param name="result">The datetime on success.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid atproto datetime.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out AtDatetime result)
    {
        result = default;
        return value is not null && TryCreate(value, value, out result);
    }

    static AtDatetime IParsable<AtDatetime>.Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return TryCreate(s, s, out var result) ? result : throw InvalidFormat(s);
    }

    static bool IParsable<AtDatetime>.TryParse(
        [NotNullWhen(true)] string? s, IFormatProvider? provider, out AtDatetime result) =>
        TryParse(s, out result);

    static AtDatetime ISpanParsable<AtDatetime>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        TryCreate(s, null, out var result) ? result : throw InvalidFormat(s.ToString());

    static bool ISpanParsable<AtDatetime>.TryParse(
        ReadOnlySpan<char> s, IFormatProvider? provider, out AtDatetime result) =>
        TryCreate(s, null, out result);

    // IParsable's contract is FormatException; Parse(string) keeps ArgumentException, as the
    // identifier types do.
    private static FormatException InvalidFormat(string value) =>
        new($"'{value}' is not a valid atproto datetime.");

    // Reads a datetime off the wire without ever rejecting it: invalid text is kept, with IsValid false.
    internal static AtDatetime FromWire(string text)
    {
        if (TryCreate(text, text, out var result))
            return result;

        // A best-effort reading of near-miss ISO 8601 forms. The leading "dddd-" keeps the
        // culture-shaped forms DateTimeOffset.TryParse would otherwise accept ("04/12/1985") out.
        if (text.Length <= MaxLength
            && text.Length > 5
            && char.IsAsciiDigit(text[0]) && char.IsAsciiDigit(text[1])
            && char.IsAsciiDigit(text[2]) && char.IsAsciiDigit(text[3]) && text[4] == '-'
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var lenient))
        {
            return new AtDatetime(text, lenient.UtcTicks + Year1Ticks, State.Lenient);
        }

        return new AtDatetime(text, 0, State.Unreadable);
    }

    // Reads UTF-8 text as DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
    // DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal) does, for the text on which the
    // two provably agree: a valid atproto datetime with at most seven fractional digits (the framework
    // rounds finer ones), an offset of at most 14 hours, and an instant a DateTimeOffset holds on both
    // sides of its offset. False means only "ask DateTimeOffset.TryParse", not that the text is invalid.
    internal static bool TryParseUtc(ReadOnlySpan<byte> utf8, out DateTime utc)
    {
        utc = default;
        if (!TryParseInstant(utf8, out var ticks, out var offsetMinutes) || Math.Abs(offsetMinutes) > MaxClrOffsetMinutes)
            return false;

        // A valid datetime has a fraction exactly when a '.' follows the seconds, and ends in Z or ±HH:MM.
        var fractionDigits = utf8[19] == '.' ? utf8.Length - 20 - (utf8[^1] == 'Z' ? 1 : 6) : 0;
        var utcTicks = ticks - Year1Ticks;
        var localTicks = utcTicks + offsetMinutes * TimeSpan.TicksPerMinute;
        if (fractionDigits > 7
            || utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks
            || localTicks < DateTime.MinValue.Ticks || localTicks > DateTime.MaxValue.Ticks)
            return false;

        utc = new DateTime(utcTicks, DateTimeKind.Utc);
        return true;
    }

    // Reads a valid atproto datetime from the unescaped UTF-8 bytes of a JSON string. A valid datetime is
    // plain ASCII, so the bytes are its text.
    internal static bool TryCreate(ReadOnlySpan<byte> utf8, out AtDatetime result)
    {
        result = TryParseInstant(utf8, out var ticks, out _)
            ? new AtDatetime(IdentifierSyntax.ToAsciiString(utf8), ticks, State.Valid)
            : default;
        return result.IsValid;
    }

    private static bool TryCreate(ReadOnlySpan<char> s, string? text, out AtDatetime result)
    {
        result = TryParseInstant(s, out var ticks, out _)
            ? new AtDatetime(text ?? s.ToString(), ticks, State.Valid)
            : default;
        return result.IsValid;
    }

    // Validates the atproto datetime syntax — RFC 3339 restricted to what ISO 8601 also allows: an
    // upper-case T, seconds precision or finer, and a mandatory time zone that is Z or ±HH:MM but not
    // -00:00 — and then the calendar. ticks counts from year zero, in UTC.
    private static bool TryParseInstant<TChar>(ReadOnlySpan<TChar> s, out long ticks, out int offsetMinutes)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        ticks = 0;
        offsetMinutes = 0;

        // Shortest form: yyyy-MM-ddTHH:mm:ssZ.
        if (s.Length < 20 || s.Length > MaxLength
            || Unit(s[4]) != '-' || Unit(s[7]) != '-' || Unit(s[10]) != 'T' || Unit(s[13]) != ':' || Unit(s[16]) != ':')
            return false;

        uint y0 = Digit(s[0]), y1 = Digit(s[1]), y2 = Digit(s[2]), y3 = Digit(s[3]);
        uint mo0 = Digit(s[5]), mo1 = Digit(s[6]), d0 = Digit(s[8]), d1 = Digit(s[9]);
        uint h0 = Digit(s[11]), h1 = Digit(s[12]), mi0 = Digit(s[14]), mi1 = Digit(s[15]);
        uint s0 = Digit(s[17]), s1 = Digit(s[18]);
        if (y0 > 9 | y1 > 9 | y2 > 9 | y3 > 9 | mo0 > 9 | mo1 > 9 | d0 > 9 | d1 > 9
            | h0 > 9 | h1 > 9 | mi0 > 9 | mi1 > 9 | s0 > 9 | s1 > 9)
            return false;

        var year = (int)(y0 * 1000 + y1 * 100 + y2 * 10 + y3);
        var month = (int)(mo0 * 10 + mo1);
        var day = (int)(d0 * 10 + d1);
        var hour = (int)(h0 * 10 + h1);
        var minute = (int)(mi0 * 10 + mi1);
        var second = (int)(s0 * 10 + s1);

        var at = 19;
        long fractionTicks = 0;
        if (Unit(s[at]) == '.')
        {
            var start = ++at;
            while (at < s.Length && Digit(s[at]) <= 9)
            {
                // Digits beyond the seventh are below a tick and truncated.
                if (at - start < 7)
                    fractionTicks = fractionTicks * 10 + Digit(s[at]);
                at++;
            }

            var digits = at - start;
            if (digits is < 1 or > 20)
                return false;

            for (var i = Math.Min(digits, 7); i < 7; i++)
                fractionTicks *= 10;
        }

        var zone = s[at..];
        if (zone.Length == 1 && Unit(zone[0]) == 'Z')
        {
            offsetMinutes = 0;
        }
        else if (zone.Length == 6 && Unit(zone[0]) is '+' or '-' && Unit(zone[3]) == ':'
                 && TryDigits(zone, 1, 2, out var offsetHours)
                 && TryDigits(zone, 4, 2, out var offsetMinute)
                 && offsetHours <= 23 && offsetMinute <= 59)
        {
            var negative = Unit(zone[0]) == '-';

            // -00:00 means "offset unknown" in RFC 3339.
            if (negative && offsetHours == 0 && offsetMinute == 0)
                return false;

            offsetMinutes = (offsetHours * 60 + offsetMinute) * (negative ? -1 : 1);
        }
        else
        {
            return false;
        }

        if (month is < 1 or > 12 || day < 1 || hour > 23 || minute > 59 || second > 59)
            return false;

        // Leap days count from March.
        var leapDay = month > 2 && IsLeapYear(year) ? 1 : 0;
        if (day > DaysInMonth(year, month))
            return false;

        var localTicks = (DaysBeforeYear(year) + CommonDaysBeforeMonth[month - 1] + leapDay + day - 1) * TimeSpan.TicksPerDay
            + hour * TimeSpan.TicksPerHour
            + minute * TimeSpan.TicksPerMinute
            + second * TimeSpan.TicksPerSecond
            + fractionTicks;
        ticks = localTicks - offsetMinutes * TimeSpan.TicksPerMinute;

        // "0000-01-01T00:00:00+01:00" is before year zero.
        return ticks >= 0;
    }

    private static uint Unit<TChar>(TChar c)
        where TChar : unmanaged, IBinaryInteger<TChar>
        => uint.CreateTruncating(c);

    // The value of a decimal digit, or more than 9 for any other character.
    private static uint Digit<TChar>(TChar c)
        where TChar : unmanaged, IBinaryInteger<TChar>
        => uint.CreateTruncating(c) - '0';

    private static bool TryDigits<TChar>(ReadOnlySpan<TChar> s, int start, int count, out int value)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        value = 0;
        foreach (var c in s.Slice(start, count))
        {
            var digit = Digit(c);
            if (digit > 9)
                return false;
            value = value * 10 + (int)digit;
        }

        return true;
    }

    private static bool IsLeapYear(int year) => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

    private static long DaysBeforeYear(int year) => year * 365L + (year + 3) / 4 - (year + 99) / 100 + (year + 399) / 400;

    private static int DaysInMonth(int year, int month) =>
        month == 2 && IsLeapYear(year) ? 29 : CommonDaysInMonth[month - 1];

    private static ReadOnlySpan<byte> CommonDaysInMonth => [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    // Days in the months of a common year before the first of each month.
    private static ReadOnlySpan<short> CommonDaysBeforeMonth => [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];

    /// <summary>Whether two values have the same text.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> if the texts are ordinally equal.</returns>
    public bool Equals(AtDatetime other) => string.Equals(_text, other._text, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => _text is null ? 0 : _text.GetHashCode(StringComparison.Ordinal);

    /// <summary>Compares by instant, then ordinally by text; values without an instant sort first.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>A negative number, zero or a positive number as this value sorts before, with or after <paramref name="other"/>.</returns>
    public int CompareTo(AtDatetime other)
    {
        var hasInstant = CurrentState >= State.Lenient;
        var otherHasInstant = other.CurrentState >= State.Lenient;
        if (hasInstant != otherHasInstant)
            return hasInstant ? 1 : -1;

        if (hasInstant && Ticks != other.Ticks)
            return Ticks < other.Ticks ? -1 : 1;

        return string.CompareOrdinal(_text, other._text);
    }

    /// <inheritdoc />
    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        AtDatetime other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(AtDatetime)}.", nameof(obj)),
    };

    /// <summary>Whether <paramref name="left"/> sorts before <paramref name="right"/>.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    public static bool operator <(AtDatetime left, AtDatetime right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> sorts after <paramref name="right"/>.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    public static bool operator >(AtDatetime left, AtDatetime right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> sorts before or with <paramref name="right"/>.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    public static bool operator <=(AtDatetime left, AtDatetime right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> sorts after or with <paramref name="right"/>.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    public static bool operator >=(AtDatetime left, AtDatetime right) => left.CompareTo(right) >= 0;

    /// <summary>The text exactly as it was read or created, or an empty string for the <see langword="default"/> value.</summary>
    /// <returns>The datetime text.</returns>
    public override string ToString() => _text ?? string.Empty;

    internal bool HasText => _text is not null;
}
