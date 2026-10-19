using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>A timestamp identifier: 53 bits of microseconds since the Unix epoch and a 10-bit clock identifier, as 13 base32-sortable characters.</summary>
/// <remarks>
/// A struct because <see langword="default"/> is a valid TID (value 0, <c>2222222222222</c>). Ordinal order of the text equals numeric
/// order of <see cref="ToInt64"/>. See <see href="https://atproto.com/specs/tid"/>.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<Tid>))]
public readonly struct Tid : IAtIdentifier<Tid>
{
    /// <summary>The length of a TID's text.</summary>
    public const int Length = 13;

    private const string Alphabet = "234567abcdefghijklmnopqrstuvwxyz";
    private const int ClockIdBits = 10;

    private readonly ulong _value;

    private Tid(ulong value) => _value = value;

    /// <summary>The microseconds since the Unix epoch.</summary>
    public long TimestampMicroseconds => (long)(_value >> ClockIdBits);

    /// <summary>The clock identifier, 0 to 1023.</summary>
    public int ClockId => (int)(_value & TidGenerator.MaxClockId);

    /// <inheritdoc />
    public string Value => string.Create(Length, _value, static (chars, value) =>
    {
        for (var i = Length - 1; i >= 0; i--, value >>= 5) chars[i] = Alphabet[(int)(value & 0x1F)];
    });

    /// <summary>The next TID of <see cref="TidGenerator.Default"/>, greater than every TID it returned before.</summary>
    /// <returns>A new TID.</returns>
    public static Tid Next() => TidGenerator.Default.Next();

    /// <summary>The TID with the raw 64-bit value <paramref name="value"/>.</summary>
    /// <param name="value">Microseconds in the top 53 bits and the clock identifier in the low 10.</param>
    /// <returns>The TID.</returns>
    public static Tid FromInt64(long value) => new((ulong)value);

    /// <summary>The raw 64-bit value: <see cref="TimestampMicroseconds"/> followed by <see cref="ClockId"/>.</summary>
    /// <returns>The value.</returns>
    public long ToInt64() => (long)_value;

    internal static bool TryRead<TChar>(ReadOnlySpan<TChar> text, out Tid result) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = default;
        if (!IdentifierSyntax.IsTid(text)) return false;
        ulong decoded = 0;
        foreach (var c in text) decoded = (decoded << 5) | (uint)Alphabet.IndexOf((char)Identifiers.Unit(c));
        result = new(decoded);
        return true;
    }

    /// <inheritdoc />
    public static Tid Parse(string s) => Identifiers.Parse<Tid>(s);

    /// <inheritdoc />
    public static Tid Parse(ReadOnlySpan<char> s) => Identifiers.Parse<Tid>(s);

    /// <inheritdoc />
    public static Tid Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<Tid>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, out Tid result) => TryRead(s.AsSpan(), out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, out Tid result) => TryRead(s, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out Tid result) => TryRead(utf8Text, out result);

    /// <inheritdoc />
    public bool Equals(Tid other) => _value == other._value;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Tid other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(Tid other) => _value.CompareTo(other._value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The canonical text of <paramref name="value"/>.</summary>
    /// <param name="value">The TID.</param>
    public static implicit operator string(Tid value) => value.Value;

    /// <summary>Whether two TIDs are the same.</summary>
    public static bool operator ==(Tid left, Tid right) => left.Equals(right);

    /// <summary>Whether two TIDs differ.</summary>
    public static bool operator !=(Tid left, Tid right) => !left.Equals(right);

    /// <inheritdoc />
    static int IAtIdentifier<Tid>.MaxLength => Length;
}
