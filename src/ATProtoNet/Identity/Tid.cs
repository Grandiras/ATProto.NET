using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Represents a Timestamp Identifier (TID) used as record keys and repository revisions in
/// AT Protocol. A TID encodes a 64-bit integer — microseconds since the UNIX epoch in the top
/// 53 bits (below a zero high bit) and a clock identifier in the bottom 10 — as 13
/// base32-sortable characters.
/// </summary>
/// <remarks>
/// Equality and ordering are ordinal on <see cref="Value"/>, which agrees with the numeric
/// order of <see cref="ToInt64"/>.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<Tid>))]
public sealed partial record Tid : IIdentifier<Tid>
{
    private const string Base32SortableChars = "234567abcdefghijklmnopqrstuvwxyz";
    private const int TidLength = 13;

    // The first character carries the high bit, which must be zero, so it is limited to the
    // lower half of the alphabet.
    [GeneratedRegex("^[234567abcdefghij][234567abcdefghijklmnopqrstuvwxyz]{12}$")]
    private static partial Regex TidPattern();

    /// <summary>
    /// The TID string value.
    /// </summary>
    public string Value { get; }

    private Tid(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a TID from a string value with validation.
    /// </summary>
    /// <param name="value">The TID string.</param>
    /// <returns>A validated TID.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid TID.</exception>
    public static Tid Parse(string value) =>
        TryParse(value, out var tid) ? tid : throw IIdentifier<Tid>.InvalidValue(value, "TID");

    /// <summary>
    /// Attempts to create a TID from a string value without throwing.
    /// </summary>
    /// <param name="value">The TID string.</param>
    /// <param name="tid">The parsed TID on success.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid TID.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out Tid? tid) =>
        TryCreate(value, value, out tid);

    static bool IIdentifier<Tid>.TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Tid? result) =>
        TryCreate(span, text, out result);

    internal static bool TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Tid? result)
    {
        result = TidPattern().IsMatch(span) ? new Tid(text ?? span.ToString()) : null;
        return result is not null;
    }

    /// <summary>
    /// Generates a new TID from the process-wide <see cref="TidGenerator"/>. Successive calls
    /// return strictly increasing values, including across threads.
    /// </summary>
    /// <returns>A new TID.</returns>
    public static Tid Next() => TidGenerator.Shared.Next();

    /// <summary>
    /// Gets the string value of the next TID, useful for record keys.
    /// </summary>
    /// <returns>The value of <see cref="Next"/>.</returns>
    public static string NextString() => Next().Value;

    /// <summary>
    /// Creates a TID from its raw 64-bit value: microseconds since the UNIX epoch in the top
    /// 53 bits, a clock identifier in the bottom 10.
    /// </summary>
    /// <param name="value">The raw TID value. The high bit must be clear.</param>
    /// <returns>The TID encoding <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static Tid FromInt64(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return new Tid(Encode(value));
    }

    /// <summary>
    /// Gets the raw 64-bit value this TID encodes. Ordinal string comparison of two TIDs and
    /// numeric comparison of their values always agree, since the encoding is base32-sortable.
    /// </summary>
    /// <returns>The raw TID value.</returns>
    public long ToInt64()
    {
        long value = 0;
        foreach (var c in Value)
            value = (value << 5) | (uint)Base32SortableChars.IndexOf(c);

        return value;
    }

    private static string Encode(long value)
    {
        Span<char> chars = stackalloc char[TidLength];
        for (var i = TidLength - 1; i >= 0; i--)
        {
            chars[i] = Base32SortableChars[(int)(value & 0x1F)];
            value >>= 5;
        }
        return new string(chars);
    }

    /// <summary>
    /// Implicitly converts a <see cref="Tid"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="tid">The value to convert.</param>
    /// <returns>The converted value, or <see langword="null"/> for a <see langword="null"/> TID.</returns>
    [return: NotNullIfNotNull(nameof(tid))]
    public static implicit operator string?(Tid? tid) => tid?.Value;

    /// <summary>
    /// Explicitly converts a <see cref="string"/> to its <see cref="Tid"/> representation.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid <see cref="Tid"/>.</exception>
    public static explicit operator Tid(string value) => Parse(value);

    /// <inheritdoc />
    public int CompareTo(Tid? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
