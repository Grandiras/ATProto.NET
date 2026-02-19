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

    public static implicit operator string(RecordKey key) => key.Value;

    public bool Equals(RecordKey? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => obj is RecordKey other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Value;

    public static bool operator ==(RecordKey? left, RecordKey? right) => Equals(left, right);
    public static bool operator !=(RecordKey? left, RecordKey? right) => !Equals(left, right);
}
