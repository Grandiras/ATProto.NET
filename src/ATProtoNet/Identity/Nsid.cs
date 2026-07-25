using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Represents a Namespaced Identifier (NSID) used to identify Lexicon schemas.
/// Format: segment.segment.name (e.g., com.atproto.repo.createRecord)
/// </summary>
[JsonConverter(typeof(NsidJsonConverter))]
public sealed partial class Nsid : IEquatable<Nsid>, IComparable<Nsid>
{
    // NSID: at least 3 segments separated by dots
    // Authority segments: lowercase alphanumeric + hyphens
    // Name segment: alphanumeric, starts with letter
    [GeneratedRegex(@"^[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+(\.[a-zA-Z][a-zA-Z0-9]*)$", RegexOptions.Compiled)]
    private static partial Regex NsidPattern();

    /// <summary>
    /// The full NSID string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The authority segments (reversed domain), e.g., "com.atproto.repo".
    /// </summary>
    public string Authority
    {
        get
        {
            var lastDot = Value.LastIndexOf('.');
            return Value[..lastDot];
        }
    }

    /// <summary>
    /// The name segment (last part), e.g., "createRecord".
    /// </summary>
    public string Name
    {
        get
        {
            var lastDot = Value.LastIndexOf('.');
            return Value[(lastDot + 1)..];
        }
    }

    /// <summary>
    /// The individual segments of the NSID.
    /// </summary>
    public string[] Segments => Value.Split('.');

    private Nsid(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates an NSID from a string value with validation.
    /// </summary>
    public static Nsid Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > 317) // max NSID length
            throw new ArgumentException("NSID must not exceed 317 characters", nameof(value));

        if (!NsidPattern().IsMatch(value))
            throw new ArgumentException($"Invalid NSID format: '{value}'", nameof(value));

        var segments = value.Split('.');
        if (segments.Length < 3)
            throw new ArgumentException("NSID must have at least 3 segments", nameof(value));

        return new Nsid(value);
    }

    /// <summary>
    /// Attempts to create an NSID from a string value without throwing.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out Nsid? nsid)
    {
        nsid = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 317 || !NsidPattern().IsMatch(value))
            return false;

        var segments = value.Split('.');
        if (segments.Length < 3)
            return false;

        nsid = new Nsid(value);
        return true;
    }

    /// <summary>
    /// Creates an NSID without validation.
    /// </summary>
    internal static Nsid UnsafeCreate(string value) => new(value);

    /// <summary>
    /// Implicitly converts a <see cref="Nsid"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="nsid">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static implicit operator string(Nsid nsid) => nsid.Value;

    /// <summary>
    /// Determines whether this instance and another <see cref="Nsid"/> represent the same value.
    /// </summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(Nsid? other) => other is not null && Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Nsid other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <inheritdoc />
    public int CompareTo(Nsid? other) => string.Compare(Value, other?.Value, StringComparison.Ordinal);

    /// <summary>Determines whether two <see cref="Nsid"/> instances are equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Nsid? left, Nsid? right) => Equals(left, right);

    /// <summary>Determines whether two <see cref="Nsid"/> instances are not equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Nsid? left, Nsid? right) => !Equals(left, right);
}
