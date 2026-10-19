using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using static ATProtoNet.Identifiers;

namespace ATProtoNet.Data;

/// <summary>A number carried as text in a Lexicon <c>string</c> field, such as <c>"52.520"</c>.</summary>
/// <remarks>
/// <para>The data model has no floating-point type, so Lexicons that need fractions declare a string. This type reads and writes that
/// text unchanged (a record read and written back keeps its CID) and parses it the same way on every machine. Valid text matches the JSON
/// number grammar <c>-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?</c>.</para>
/// <para>Construction is strict, reading is lenient, as for <see cref="AtDatetime"/>: JSON reads keep any string with
/// <see cref="IsValid"/> false, and a JSON number token is an error. Equality is ordinal on the text, so <c>"52.52"</c> and
/// <c>"52.520"</c> differ; <see cref="CompareTo"/> orders the text, not the numbers, which callers compare after
/// <see cref="TryGetDecimal"/>.</para>
/// </remarks>
[JsonConverter(typeof(DecimalStringJsonConverter))]
public sealed class DecimalString : IAtIdentifier<DecimalString>
{
    /// <summary>The longest text, in characters. It bounds parsing.</summary>
    public const int MaxLength = 64;

    private DecimalString(string value, bool isValid)
    {
        Value = value;
        IsValid = isValid;
    }

    /// <summary>The text exactly as written or read.</summary>
    public string Value { get; }

    /// <summary>Whether the text is a valid number; false only for a value read leniently off the wire.</summary>
    public bool IsValid { get; }

    /// <summary>The number as a <see cref="double"/>, in the invariant culture.</summary>
    /// <param name="value">The number.</param>
    /// <returns>False for invalid text, a result outside the range of <see cref="double"/> or a non-finite one.</returns>
    public bool TryGetDouble(out double value)
    {
        value = 0;
        if (!IsValid || !double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed)) return false;
        value = parsed;
        return true;
    }

    /// <summary>The number as a <see cref="decimal"/>, in the invariant culture.</summary>
    /// <param name="value">The number.</param>
    /// <returns>False for invalid text or a result outside the range of <see cref="decimal"/>.</returns>
    public bool TryGetDecimal(out decimal value)
    {
        value = 0;
        return IsValid && decimal.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>The shortest text that reads back as <paramref name="value"/>, in the invariant culture.</summary>
    /// <param name="value">A finite number.</param>
    /// <returns>The number as text.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is <see cref="double.NaN"/> or an infinity.</exception>
    public static DecimalString From(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "A decimal string cannot hold NaN or an infinity.");
        return new(value.ToString(CultureInfo.InvariantCulture), true);
    }

    /// <summary>The text of <paramref name="value"/> with its scale, in the invariant culture: <c>52.520m</c> is <c>"52.520"</c>.</summary>
    /// <param name="value">A number.</param>
    /// <returns>The number as text.</returns>
    public static DecimalString From(decimal value) => new(value.ToString(CultureInfo.InvariantCulture), true);

    // Reads text off the wire without ever rejecting it (SYN-85).
    internal static DecimalString FromWire(string text) => TryRead(text.AsSpan(), text, out var valid) ? valid : new(text, false);

    internal static bool TryRead<TChar>(ReadOnlySpan<TChar> text, string? value, [NotNullWhen(true)] out DecimalString? result) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = IsNumber(text) ? new(value ?? Text(text), true) : null;
        return result is not null;
    }

    // -?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?
    private static bool IsNumber<TChar>(ReadOnlySpan<TChar> s) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (s.Length is 0 or > MaxLength) return false;
        var i = Unit(s[0]) == '-' ? 1 : 0;
        if (i < s.Length && Unit(s[i]) == '0') i++;
        else if (i < s.Length && Unit(s[i]) - '1' <= 8) i = SkipDigits(s, i + 1);
        else return false;
        if (i < s.Length && Unit(s[i]) == '.')
        {
            var end = SkipDigits(s, i + 1);
            if (end == i + 1) return false;
            i = end;
        }
        if (i < s.Length && Unit(s[i]) is 'e' or 'E')
        {
            var start = i + 1 < s.Length && (Unit(s[i + 1]) is '+' or '-') ? i + 2 : i + 1;
            var end = SkipDigits(s, start);
            if (end == start) return false;
            i = end;
        }
        return i == s.Length;
    }

    private static int SkipDigits<TChar>(ReadOnlySpan<TChar> s, int at) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        while (at < s.Length && Unit(s[at]) - '0' <= 9) at++;
        return at;
    }

    /// <inheritdoc />
    public static DecimalString Parse(string s) => Identifiers.Parse<DecimalString>(s);

    /// <inheritdoc />
    public static DecimalString Parse(ReadOnlySpan<char> s) => Identifiers.Parse<DecimalString>(s);

    /// <inheritdoc />
    public static DecimalString Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<DecimalString>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out DecimalString? result) => TryRead(s.AsSpan(), s, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out DecimalString? result) => TryRead(s, null, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out DecimalString? result) => TryRead(utf8Text, null, out result);

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] DecimalString? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as DecimalString);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Orders ordinally by text; it is not a numeric order.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>A negative number, zero or a positive number as the text sorts before, with or after that of <paramref name="other"/>.</returns>
    public int CompareTo(DecimalString? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The text of <paramref name="value"/>.</summary>
    /// <param name="value">The number.</param>
    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator string?(DecimalString? value) => value?.Value;

    /// <summary>Whether two values have the same text.</summary>
    public static bool operator ==(DecimalString? left, DecimalString? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two values have different text.</summary>
    public static bool operator !=(DecimalString? left, DecimalString? right) => !(left == right);

    /// <inheritdoc />
    static int IAtIdentifier<DecimalString>.MaxLength => MaxLength;
}
