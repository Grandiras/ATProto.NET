using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Represents a Timestamp Identifier (TID) used as record keys in AT Protocol.
/// TIDs are base32-sortable, monotonically increasing identifiers based on timestamps.
/// Format: 13 characters of base32-sortable encoding.
/// </summary>
[JsonConverter(typeof(TidJsonConverter))]
public sealed partial class Tid : IEquatable<Tid>, IComparable<Tid>
{
    // TID is a 13-character base32-sortable string
    private const string Base32SortableChars = "234567abcdefghijklmnopqrstuvwxyz";
    private const int TidLength = 13;

    [GeneratedRegex(@"^[2-7a-z]{13}$", RegexOptions.Compiled)]
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
    public static Tid Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!TidPattern().IsMatch(value))
            throw new ArgumentException($"Invalid TID format: '{value}'", nameof(value));

        return new Tid(value);
    }

    /// <summary>
    /// Attempts to create a TID from a string value without throwing.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out Tid? tid)
    {
        tid = null;
        if (string.IsNullOrWhiteSpace(value) || !TidPattern().IsMatch(value))
            return false;

        tid = new Tid(value);
        return true;
    }

    /// <summary>
    /// Generates a new TID based on the current timestamp and a random clock ID.
    /// </summary>
    public static Tid Next()
    {
        // TID is a 64-bit integer encoded as base32-sortable
        // Top 53 bits: microseconds since UNIX epoch
        // Bottom 10 bits: clock ID (random)
        var microseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
        var clockId = Random.Shared.Next(0, 1024);
        var tidValue = (microseconds << 10) | (long)clockId;

        return new Tid(Encode(tidValue));
    }

    /// <summary>
    /// Gets the string value of the next TID, useful for record keys.
    /// </summary>
    public static string NextString() => Next().Value;

    /// <summary>
    /// Creates a TID from its raw 64-bit value: microseconds since the UNIX epoch in the top
    /// 53 bits, a clock identifier in the bottom 10.
    /// </summary>
    /// <param name="value">The raw TID value. The high bit must be clear.</param>
    /// <remarks>
    /// <see cref="Next"/> resolves only to the millisecond and picks a random clock id, so two
    /// TIDs minted in the same millisecond are ordered arbitrarily. Callers that need a strictly
    /// increasing sequence — a repository's commit <c>rev</c>, for instance — should track the
    /// last value themselves and mint the next one through this method.
    /// </remarks>
    public static Tid FromInt64(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return new Tid(Encode(value));
    }

    /// <summary>
    /// Gets the raw 64-bit value this TID encodes. Ordinal string comparison of two TIDs and
    /// numeric comparison of their values always agree, since the encoding is base32-sortable.
    /// </summary>
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
    /// Creates a TID without validation.
    /// </summary>
    internal static Tid UnsafeCreate(string value) => new(value);

    public static implicit operator string(Tid tid) => tid.Value;

    public bool Equals(Tid? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => obj is Tid other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Value;
    public int CompareTo(Tid? other) => string.Compare(Value, other?.Value, StringComparison.Ordinal);

    public static bool operator ==(Tid? left, Tid? right) => Equals(left, right);
    public static bool operator !=(Tid? left, Tid? right) => !Equals(left, right);
}
