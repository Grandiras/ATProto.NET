using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Represents a Content Identifier (CID) used to reference content-addressed data.
/// CIDs are self-describing content-addressed identifiers used in IPLD.
/// </summary>
[JsonConverter(typeof(CidJsonConverter))]
public sealed class Cid : IEquatable<Cid>
{
    /// <summary>
    /// The CID string value.
    /// </summary>
    public string Value { get; }

    private Cid(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Wraps a CID string. Only rejects null/blank input — the multibase prefix,
    /// codec, and digest length are not checked, so a value that round-trips here
    /// is not necessarily a well-formed CID.
    /// </summary>
    public static Cid Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new Cid(value);
    }

    /// <summary>
    /// Non-throwing counterpart to <see cref="Parse"/>, with the same lenient rules:
    /// returns <see langword="false"/> only for null or blank input.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out Cid? cid)
    {
        cid = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        cid = new Cid(value);
        return true;
    }

    /// <summary>
    /// Creates a CID skipping even the blank check, for values already known good.
    /// </summary>
    internal static Cid UnsafeCreate(string value) => new(value);

    /// <summary>
    /// Implicitly converts a <see cref="Cid"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="cid">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static implicit operator string(Cid cid) => cid.Value;

    /// <summary>
    /// Determines whether this instance and another <see cref="Cid"/> represent the same value.
    /// </summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(Cid? other) => other is not null && Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Cid other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Determines whether two <see cref="Cid"/> instances are equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Cid? left, Cid? right) => Equals(left, right);

    /// <summary>Determines whether two <see cref="Cid"/> instances are not equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Cid? left, Cid? right) => !Equals(left, right);
}
