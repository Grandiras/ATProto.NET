using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Represents a Decentralized Identifier (DID) as specified in the AT Protocol.
/// DIDs are the permanent, long-term identifiers for accounts.
/// Examples: did:plc:z72i7hdynmk6r22z27h6tvur, did:web:example.com
/// </summary>
[JsonConverter(typeof(DidJsonConverter))]
public sealed partial class Did : IEquatable<Did>, IComparable<Did>
{
    // DID syntax: did:<method>:<method-specific-id>
    // Method: lowercase letters and digits
    // Method-specific-id: allowed chars per W3C DID spec
    [GeneratedRegex(@"^did:[a-z]+:[a-zA-Z0-9._:%-]*[a-zA-Z0-9._%-]$", RegexOptions.Compiled)]
    private static partial Regex DidPattern();

    /// <summary>
    /// The full DID string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The DID method (e.g., "plc", "web").
    /// </summary>
    /// <remarks>
    /// Sliced rather than split: <c>Split(':')</c> allocated an array plus a string per
    /// segment (and a <c>did:web</c> identifier can carry several colons) to return one of them.
    /// </remarks>
    public string Method => Value[4..Value.IndexOf(':', 4)];

    /// <summary>
    /// The method-specific identifier portion of the DID.
    /// </summary>
    public string MethodSpecificId => Value[(Value.IndexOf(':', 4) + 1)..];

    private Did(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a DID from a string value with validation.
    /// </summary>
    /// <param name="value">The DID string.</param>
    /// <returns>A validated DID instance.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid DID.</exception>
    public static Did Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!DidPattern().IsMatch(value))
            throw new ArgumentException($"Invalid DID format: '{value}'", nameof(value));

        if (value.Length > 2048)
            throw new ArgumentException("DID must not exceed 2048 characters", nameof(value));

        return new Did(value);
    }

    /// <summary>
    /// Attempts to create a DID from a string value without throwing.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out Did? did)
    {
        did = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || !DidPattern().IsMatch(value))
            return false;

        did = new Did(value);
        return true;
    }

    /// <summary>
    /// Creates a DID without validation. Use only when the input is known to be valid.
    /// </summary>
    internal static Did UnsafeCreate(string value) => new(value);

    /// <summary>
    /// Implicitly converts a <see cref="Did"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="did">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static implicit operator string(Did did) => did.Value;

    /// <summary>
    /// Explicitly converts a <see cref="string"/> to its <see cref="Did"/> representation.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid <see cref="Did"/>.</exception>
    public static explicit operator Did(string value) => Parse(value);

    /// <summary>
    /// Determines whether this instance and another <see cref="Did"/> represent the same value.
    /// </summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(Did? other) => other is not null && Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Did other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <inheritdoc />
    public int CompareTo(Did? other) => string.Compare(Value, other?.Value, StringComparison.Ordinal);

    /// <summary>Determines whether two <see cref="Did"/> instances are equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Did? left, Did? right) => Equals(left, right);

    /// <summary>Determines whether two <see cref="Did"/> instances are not equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Did? left, Did? right) => !Equals(left, right);
}
