using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>A Lexicon <c>datetime</c>: an RFC 3339 timestamp that keeps the exact text it was read or made from, so a record written back keeps its CID.</summary>
/// <remarks>
/// <para>Construction is strict, reading is lenient. <c>Parse</c>, <c>TryParse</c>, <see cref="Now"/> and <see cref="From(DateTimeOffset)"/>
/// only produce valid values; the JSON converter never rejects a string, so one malformed timestamp does not fail a whole response. Such a
/// value keeps its text with <see cref="IsValid"/> false, and <see cref="TryGetInstant"/> still recovers the instant of a near-miss ISO 8601
/// form (a missing time zone reads as UTC).</para>
/// <para>Equality is ordinal on the text: two spellings of one instant are different values, because they make different records. Ordering
/// is by instant, then by text; values without an instant sort first.</para>
/// See <see href="https://atproto.com/specs/lexicon#datetime"/>.
/// </remarks>
[JsonConverter(typeof(AtDatetimeJsonConverter))]
public sealed class AtDatetime : IAtIdentifier<AtDatetime>
{
    /// <summary>The longest datetime, in characters.</summary>
    public const int MaxLength = 64;

    /// <summary>The most fractional-second digits a datetime may have.</summary>
    public const int MaxFractionDigits = 20;

    // Ticks since 0000-01-01T00:00:00Z. Year 0 is a leap year, so DateTime's tick 0 (0001-01-01) is 366 days in.
    private const long Year1Ticks = 366 * TimeSpan.TicksPerDay;
    private const int MaxClrOffsetMinutes = 14 * 60;
    private const string CanonicalFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private readonly long _ticks;
    private readonly int _offsetMinutes;
    private readonly State _state;

    private AtDatetime(string value, long ticks, int offsetMinutes, State state)
    {
        Value = value;
        _ticks = ticks;
        _offsetMinutes = offsetMinutes;
        _state = state;
    }

    private enum State : byte { Unreadable, Lenient, Valid }

    /// <summary>The text exactly as written or read.</summary>
    public string Value { get; }

    /// <summary>Whether the text is a valid datetime; false only for a value read leniently off the wire.</summary>
    public bool IsValid => _state == State.Valid;

    /// <summary>The instant the text denotes, in the offset it was written with.</summary>
    /// <param name="instant">The instant, at 100 ns precision. An offset beyond ±14:00 is normalized to UTC.</param>
    /// <returns>False when the text denotes no instant a <see cref="DateTimeOffset"/> can hold, such as a date in year 0000.</returns>
    public bool TryGetInstant(out DateTimeOffset instant)
    {
        instant = default;
        var utcTicks = _ticks - Year1Ticks;
        if (_state == State.Unreadable || utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks) return false;
        var offsetTicks = _offsetMinutes * TimeSpan.TicksPerMinute;
        var localTicks = utcTicks + offsetTicks;
        instant = Math.Abs(_offsetMinutes) <= MaxClrOffsetMinutes && localTicks >= DateTime.MinValue.Ticks && localTicks <= DateTime.MaxValue.Ticks
            ? new(localTicks, TimeSpan.FromTicks(offsetTicks))
            : new(utcTicks, TimeSpan.Zero);
        return true;
    }

    /// <summary>The current time in the canonical form, UTC to the millisecond.</summary>
    /// <param name="timeProvider">The clock to read; the system clock when omitted.</param>
    /// <returns>The datetime.</returns>
    public static AtDatetime Now(TimeProvider? timeProvider = null) => From((timeProvider ?? SystemClock.Instance).GetUtcNow());

    /// <summary>The canonical datetime of an instant: <c>yyyy-MM-ddTHH:mm:ss.fffZ</c> in UTC, truncated to milliseconds, whatever the culture.</summary>
    /// <param name="value">The instant.</param>
    /// <returns>The datetime.</returns>
    public static AtDatetime From(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        utc = utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMillisecond));
        return new(utc.ToString(CanonicalFormat, CultureInfo.InvariantCulture), utc.Ticks + Year1Ticks, 0, State.Valid);
    }

    /// <summary>The canonical datetime of a <see cref="DateTime"/>, as <see cref="From(DateTimeOffset)"/>.</summary>
    /// <param name="value">The date and time; <see cref="DateTimeKind.Local"/> is converted to UTC, <see cref="DateTimeKind.Unspecified"/> is taken as UTC.</param>
    /// <returns>The datetime.</returns>
    public static AtDatetime From(DateTime value) =>
        From(value.Kind == DateTimeKind.Local ? new DateTimeOffset(value) : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    // Reads a string off the wire without ever rejecting it (SYN-46). The leading "dddd-" keeps culture-shaped forms such as
    // "04/12/1985", which DateTimeOffset.TryParse would accept, out.
    internal static AtDatetime FromWire(string text)
    {
        if (TryRead(text.AsSpan(), text, out var valid)) return valid;
        if (text.Length is > 5 and <= MaxLength && char.IsAsciiDigit(text[0]) && char.IsAsciiDigit(text[1]) && char.IsAsciiDigit(text[2])
            && char.IsAsciiDigit(text[3]) && text[4] == '-' && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var lenient))
            return new(text, lenient.UtcTicks + Year1Ticks, (int)lenient.Offset.TotalMinutes, State.Lenient);
        return new(text, 0, 0, State.Unreadable);
    }

    internal static bool TryRead<TChar>(ReadOnlySpan<TChar> text, string? value, [NotNullWhen(true)] out AtDatetime? result) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = AtDatetimeSyntax.TryParse(text, out var ticks, out var offsetMinutes) ? new(value ?? Identifiers.Text(text), ticks, offsetMinutes, State.Valid) : null;
        return result is not null;
    }

    /// <inheritdoc />
    public static AtDatetime Parse(string s) => Identifiers.Parse<AtDatetime>(s);

    /// <inheritdoc />
    public static AtDatetime Parse(ReadOnlySpan<char> s) => Identifiers.Parse<AtDatetime>(s);

    /// <inheritdoc />
    public static AtDatetime Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<AtDatetime>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out AtDatetime? result) => TryRead(s.AsSpan(), s, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out AtDatetime? result) => TryRead(s, null, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out AtDatetime? result) => TryRead(utf8Text, null, out result);

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] AtDatetime? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as AtDatetime);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Compares by instant, then ordinally by text; values without an instant sort first.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>A negative number, zero or a positive number as this value sorts before, with or after <paramref name="other"/>.</returns>
    public int CompareTo(AtDatetime? other)
    {
        if (other is null) return 1;
        var hasInstant = _state != State.Unreadable;
        if (hasInstant != (other._state != State.Unreadable)) return hasInstant ? 1 : -1;
        return hasInstant && _ticks != other._ticks ? _ticks.CompareTo(other._ticks) : string.CompareOrdinal(Value, other.Value);
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The text of <paramref name="value"/>.</summary>
    /// <param name="value">The datetime.</param>
    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator string?(AtDatetime? value) => value?.Value;

    /// <summary>Whether two datetimes have the same text.</summary>
    public static bool operator ==(AtDatetime? left, AtDatetime? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two datetimes have different text.</summary>
    public static bool operator !=(AtDatetime? left, AtDatetime? right) => !(left == right);

    /// <inheritdoc />
    static int IAtIdentifier<AtDatetime>.MaxLength => MaxLength;
}
