using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Represents an AT Protocol Handle (domain name identifier).
/// Handles are human-readable identifiers that map to DIDs.
/// Examples: alice.bsky.social, bob.example.com
/// </summary>
[JsonConverter(typeof(HandleJsonConverter))]
public sealed partial class Handle : IEquatable<Handle>, IComparable<Handle>
{
    // Handle must be a valid domain name
    // Each label: 1-63 chars, alphanumeric + hyphens, no leading/trailing hyphens
    // Total max: 253 chars
    [GeneratedRegex(@"^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$", RegexOptions.Compiled)]
    private static partial Regex HandlePattern();

    /// <summary>
    /// The handle string value (normalized to lowercase).
    /// </summary>
    public string Value { get; }

    private Handle(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a Handle from a string value with validation.
    /// </summary>
    public static Handle Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        // Strip leading @ if present (common user input)
        if (value.StartsWith('@'))
            value = value[1..];

        var normalized = value.ToLowerInvariant();

        if (normalized.Length > 253)
            throw new ArgumentException("Handle must not exceed 253 characters", nameof(value));

        if (!HandlePattern().IsMatch(normalized))
            throw new ArgumentException($"Invalid handle format: '{value}'", nameof(value));

        return new Handle(normalized);
    }

    /// <summary>
    /// Attempts to create a Handle from a string value without throwing.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out Handle? handle)
    {
        handle = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.StartsWith('@'))
            value = value[1..];

        var normalized = value.ToLowerInvariant();

        if (normalized.Length > 253 || !HandlePattern().IsMatch(normalized))
            return false;

        handle = new Handle(normalized);
        return true;
    }

    /// <summary>
    /// Creates a Handle without validation.
    /// </summary>
    internal static Handle UnsafeCreate(string value) => new(value);

    /// <summary>
    /// Implicitly converts a <see cref="Handle"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="handle">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static implicit operator string(Handle handle) => handle.Value;

    /// <summary>
    /// Explicitly converts a <see cref="string"/> to its <see cref="Handle"/> representation.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid <see cref="Handle"/>.</exception>
    public static explicit operator Handle(string value) => Parse(value);

    /// <summary>
    /// Determines whether this instance and another <see cref="Handle"/> represent the same value.
    /// </summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(Handle? other) => other is not null
        && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Handle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode(StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <inheritdoc />
    public int CompareTo(Handle? other) =>
        string.Compare(Value, other?.Value, StringComparison.OrdinalIgnoreCase);

    /// <summary>Determines whether two <see cref="Handle"/> instances are equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Handle? left, Handle? right) => Equals(left, right);

    /// <summary>Determines whether two <see cref="Handle"/> instances are not equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Handle? left, Handle? right) => !Equals(left, right);
}
