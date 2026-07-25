using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Represents a record key used to identify individual records within a collection.
/// Record keys have specific restrictions on allowed characters and patterns.
/// Common patterns: "self" (singleton), TID (timestamp-based), or custom strings.
/// </summary>
[JsonConverter(typeof(RecordKeyJsonConverter))]
public sealed partial class RecordKey : IEquatable<RecordKey>
{
    // Record key: 1-512 chars, alphanumeric + . - _ ~ : % (no slashes)
    // Must not be "." or ".."
    [GeneratedRegex(@"^[a-zA-Z0-9._~:@!$&')(*+,;=-]{1,512}$", RegexOptions.Compiled)]
    private static partial Regex RecordKeyPattern();

    /// <summary>
    /// A well-known record key for singleton records (e.g., profile records).
    /// </summary>
    public static readonly RecordKey Self = new("self");

    /// <summary>
    /// The record key string value.
    /// </summary>
    public string Value { get; }

    private RecordKey(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a RecordKey from a string value with validation.
    /// </summary>
    public static RecordKey Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value is "." or "..")
            throw new ArgumentException("Record key must not be '.' or '..'", nameof(value));

        if (!RecordKeyPattern().IsMatch(value))
            throw new ArgumentException($"Invalid record key format: '{value}'", nameof(value));

        return new RecordKey(value);
    }

    /// <summary>
    /// Attempts to create a RecordKey from a string value without throwing.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out RecordKey? recordKey)
    {
        recordKey = null;
        if (string.IsNullOrWhiteSpace(value) || value is "." or "..")
            return false;

        if (!RecordKeyPattern().IsMatch(value))
            return false;

        recordKey = new RecordKey(value);
        return true;
    }

    /// <summary>
    /// Creates a new TID-based record key.
    /// </summary>
    public static RecordKey NewTid() => new(Tid.NextString());

    /// <summary>
    /// Creates a RecordKey without validation.
    /// </summary>
    internal static RecordKey UnsafeCreate(string value) => new(value);

    /// <summary>
    /// Implicitly converts a <see cref="RecordKey"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="key">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static implicit operator string(RecordKey key) => key.Value;

    /// <summary>
    /// Determines whether this instance and another <see cref="RecordKey"/> represent the same
    /// value.
    /// </summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(RecordKey? other) => other is not null && Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RecordKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Determines whether two <see cref="RecordKey"/> instances are equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(RecordKey? left, RecordKey? right) => Equals(left, right);

    /// <summary>Determines whether two <see cref="RecordKey"/> instances are not equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(RecordKey? left, RecordKey? right) => !Equals(left, right);
}
