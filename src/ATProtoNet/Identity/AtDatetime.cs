using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// A Lexicon <c>datetime</c> value: an RFC 3339 timestamp that keeps the exact text it was
/// read or created from, so a record written back re-serializes byte for byte and keeps its
/// CID.
/// </summary>
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

    private readonly string? _text;
    private readonly long _ticks;
    private readonly short _offsetMinutes;
    private readonly State _state;

    private AtDatetime(string text, long ticks, int offsetMinutes, State state)
    {
        _text = text;
        _ticks = ticks;
        _offsetMinutes = (short)offsetMinutes;
        _state = state;
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

    /// <summary>
    /// Whether the text is a valid atproto datetime. Always <see langword="true"/> for a value
    /// created from code; a value read from the wire may not be.
    /// </summary>
    public bool IsValid => _state == State.Valid;

    /// <summary>
    /// The instant the text denotes, in the offset it was written with.
    /// </summary>
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

    /// <summary>
    /// Gets the instant the text denotes, when it denotes one <see cref="DateTimeOffset"/> can
    /// represent.
    /// </summary>
    /// <param name="value">The instant, in the offset it was written with.</param>
    /// <returns>
    /// <see langword="true"/> for every valid value in the <see cref="DateTimeOffset"/> range
    /// and for near-miss ISO 8601 text read from the wire; <see langword="false"/> otherwise.
    /// </returns>
    public bool TryGetValue(out DateTimeOffset value)
    {
        value = default;
        if (_state < State.Lenient)
            return false;

        var utcTicks = _ticks - Year1Ticks;
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
            return false;

        var offsetTicks = _offsetMinutes * TimeSpan.TicksPerMinute;
        var localTicks = utcTicks + offsetTicks;
        value = Math.Abs(_offsetMinutes) <= MaxClrOffsetMinutes
                && localTicks >= DateTime.MinValue.Ticks && localTicks <= DateTime.MaxValue.Ticks
            ? new DateTimeOffset(localTicks, TimeSpan.FromTicks(offsetTicks))
            : new DateTimeOffset(utcTicks, TimeSpan.Zero);
        return true;
    }

    /// <summary>
    /// The current time as a canonical atproto datetime (UTC, millisecond precision).
    /// </summary>
    /// <returns>The current time.</returns>
    public static AtDatetime Now() => FromDateTimeOffset(DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates the canonical atproto datetime for an instant: UTC, millisecond precision,
    /// <c>yyyy-MM-ddTHH:mm:ss.fffZ</c> in the invariant culture.
    /// </summary>
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
        return new AtDatetime(text, utc.Ticks + Year1Ticks, 0, State.Valid);
    }

    /// <summary>
    /// Creates the canonical atproto datetime for a <see cref="DateTime"/>, as
    /// <see cref="FromDateTimeOffset"/> does.
    /// </summary>
    /// <param name="value">
    /// The date and time. <see cref="DateTimeKind.Local"/> is converted to UTC;
    /// <see cref="DateTimeKind.Unspecified"/> is taken to be UTC already.
    /// </param>
    /// <returns>The datetime.</returns>
    public static AtDatetime FromDateTime(DateTime value) =>
        FromDateTimeOffset(value.Kind == DateTimeKind.Local
            ? new DateTimeOffset(value)
            : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    /// <summary>
    /// Parses a valid atproto datetime, keeping its text exactly as given.
    /// </summary>
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

    /// <summary>
    /// Attempts to parse a valid atproto datetime, keeping its text exactly as given.
    /// </summary>
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

    /// <summary>
    /// Reads a datetime off the wire without ever rejecting it: invalid text is kept, with
    /// <see cref="IsValid"/> <see langword="false"/>.
    /// </summary>
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
            return new AtDatetime(text, lenient.UtcTicks + Year1Ticks, (int)lenient.Offset.TotalMinutes, State.Lenient);
        }

        return new AtDatetime(text, 0, 0, State.Unreadable);
    }

    /// <summary>
    /// Validates the atproto datetime syntax — RFC 3339 restricted to what ISO 8601 also
    /// allows: an upper-case <c>T</c>, seconds precision or finer, and a mandatory time zone
    /// that is <c>Z</c> or <c>±HH:MM</c> but not <c>-00:00</c> — and then the calendar.
    /// </summary>
    private static bool TryCreate(ReadOnlySpan<char> s, string? text, out AtDatetime result)
    {
        result = default;

        // Shortest form: yyyy-MM-ddTHH:mm:ssZ.
        if (s.Length < 20 || s.Length > MaxLength
            || s[4] != '-' || s[7] != '-' || s[10] != 'T' || s[13] != ':' || s[16] != ':'
            || !TryDigits(s, 0, 4, out var year)
            || !TryDigits(s, 5, 2, out var month)
            || !TryDigits(s, 8, 2, out var day)
            || !TryDigits(s, 11, 2, out var hour)
            || !TryDigits(s, 14, 2, out var minute)
            || !TryDigits(s, 17, 2, out var second))
            return false;

        var at = 19;
        long fractionTicks = 0;
        if (s[at] == '.')
        {
            var start = ++at;
            while (at < s.Length && char.IsAsciiDigit(s[at]))
            {
                // Digits beyond the seventh are below a tick and truncated.
                if (at - start < 7)
                    fractionTicks = fractionTicks * 10 + (s[at] - '0');
                at++;
            }

            var digits = at - start;
            if (digits is < 1 or > 20)
                return false;

            for (var i = Math.Min(digits, 7); i < 7; i++)
                fractionTicks *= 10;
        }

        int offsetMinutes;
        var zone = s[at..];
        if (zone is "Z")
        {
            offsetMinutes = 0;
        }
        else if (zone.Length == 6 && zone[0] is '+' or '-' && zone[3] == ':'
                 && TryDigits(zone, 1, 2, out var offsetHours)
                 && TryDigits(zone, 4, 2, out var offsetMinute)
                 && offsetHours <= 23 && offsetMinute <= 59
                 && zone is not "-00:00")
        {
            offsetMinutes = (offsetHours * 60 + offsetMinute) * (zone[0] == '-' ? -1 : 1);
        }
        else
        {
            return false;
        }

        if (month is < 1 or > 12 || day < 1 || day > DaysInMonth(year, month)
            || hour > 23 || minute > 59 || second > 59)
            return false;

        var localTicks = (DaysBeforeYear(year) + DaysBeforeMonth(year, month) + day - 1) * TimeSpan.TicksPerDay
            + hour * TimeSpan.TicksPerHour
            + minute * TimeSpan.TicksPerMinute
            + second * TimeSpan.TicksPerSecond
            + fractionTicks;
        var ticks = localTicks - offsetMinutes * TimeSpan.TicksPerMinute;

        // "0000-01-01T00:00:00+01:00" is before year zero.
        if (ticks < 0)
            return false;

        result = new AtDatetime(text ?? s.ToString(), ticks, offsetMinutes, State.Valid);
        return true;
    }

    private static bool TryDigits(ReadOnlySpan<char> s, int start, int count, out int value)
    {
        value = 0;
        foreach (var c in s.Slice(start, count))
        {
            if (!char.IsAsciiDigit(c))
                return false;
            value = value * 10 + (c - '0');
        }

        return true;
    }

    private static bool IsLeapYear(int year) => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

    private static long DaysBeforeYear(int year) => year * 365L + (year + 3) / 4 - (year + 99) / 100 + (year + 399) / 400;

    private static int DaysInMonth(int year, int month) => month switch
    {
        2 => IsLeapYear(year) ? 29 : 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    private static int DaysBeforeMonth(int year, int month)
    {
        var days = 0;
        for (var m = 1; m < month; m++)
            days += DaysInMonth(year, m);
        return days;
    }

    /// <summary>
    /// Whether two values have the same text.
    /// </summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> if the texts are ordinally equal.</returns>
    public bool Equals(AtDatetime other) => string.Equals(_text, other._text, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => _text is null ? 0 : _text.GetHashCode(StringComparison.Ordinal);

    /// <summary>
    /// Compares by instant, then ordinally by text; values without an instant sort first.
    /// </summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>A negative number, zero or a positive number as this value sorts before, with or after <paramref name="other"/>.</returns>
    public int CompareTo(AtDatetime other)
    {
        var hasInstant = _state >= State.Lenient;
        var otherHasInstant = other._state >= State.Lenient;
        if (hasInstant != otherHasInstant)
            return hasInstant ? 1 : -1;

        if (hasInstant && _ticks != other._ticks)
            return _ticks < other._ticks ? -1 : 1;

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

    /// <summary>
    /// The text exactly as it was read or created, or an empty string for the
    /// <see langword="default"/> value.
    /// </summary>
    /// <returns>The datetime text.</returns>
    public override string ToString() => _text ?? string.Empty;

    internal bool HasText => _text is not null;
}
