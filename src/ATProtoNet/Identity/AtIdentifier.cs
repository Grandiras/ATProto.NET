using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Represents an AT Protocol identifier that can be either a DID or a Handle.
/// Used in scenarios where either identifier type is accepted (e.g., API parameters).
/// </summary>
[JsonConverter(typeof(AtIdentifierJsonConverter))]
public sealed class AtIdentifier : IEquatable<AtIdentifier>
{
    /// <summary>
    /// The DID value, if this identifier is a DID.
    /// </summary>
    public Did? Did { get; }

    /// <summary>
    /// The Handle value, if this identifier is a Handle.
    /// </summary>
    public Handle? Handle { get; }

    /// <summary>
    /// Whether this identifier is a DID.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Did))]
    [MemberNotNullWhen(false, nameof(Handle))]
    public bool IsDid => Did is not null;

    /// <summary>
    /// Whether this identifier is a Handle.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Handle))]
    [MemberNotNullWhen(false, nameof(Did))]
    public bool IsHandle => Handle is not null;

    /// <summary>
    /// The string value of the identifier.
    /// </summary>
    public string Value => Did?.Value ?? Handle!.Value;

    private AtIdentifier(Did did)
    {
        Did = did;
    }

    private AtIdentifier(Handle handle)
    {
        Handle = handle;
    }

    /// <summary>
    /// Creates an AtIdentifier from a DID.
    /// </summary>
    public static AtIdentifier FromDid(Did did) => new(did);

    /// <summary>
    /// Creates an AtIdentifier from a Handle.
    /// </summary>
    public static AtIdentifier FromHandle(Handle handle) => new(handle);

    /// <summary>
    /// Parses a string as an AT identifier (either DID or Handle).
    /// DIDs are unambiguous because they always start with "did:".
    /// </summary>
    public static AtIdentifier Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.StartsWith("did:", StringComparison.Ordinal))
            return new AtIdentifier(Identity.Did.Parse(value));

        return new AtIdentifier(Identity.Handle.Parse(value));
    }

    /// <summary>
    /// Attempts to parse a string as an AT identifier without throwing.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out AtIdentifier? identifier)
    {
        identifier = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.StartsWith("did:", StringComparison.Ordinal))
        {
            if (Identity.Did.TryParse(value, out var did))
            {
                identifier = new AtIdentifier(did);
                return true;
            }
            return false;
        }

        if (Identity.Handle.TryParse(value, out var handle))
        {
            identifier = new AtIdentifier(handle);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Implicitly converts a <see cref="Did"/> to its <see cref="AtIdentifier"/> representation.
    /// </summary>
    /// <param name="did">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static implicit operator AtIdentifier(Did did) => FromDid(did);

    /// <summary>
    /// Implicitly converts a <see cref="Handle"/> to its <see cref="AtIdentifier"/> representation.
    /// </summary>
    /// <param name="handle">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static implicit operator AtIdentifier(Handle handle) => FromHandle(handle);

    /// <summary>
    /// Implicitly converts a <see cref="AtIdentifier"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="id">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static implicit operator string(AtIdentifier id) => id.Value;

    /// <summary>
    /// Determines whether this instance and another <see cref="AtIdentifier"/> represent the same
    /// value.
    /// </summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(AtIdentifier? other) => other is not null && Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AtIdentifier other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Determines whether two <see cref="AtIdentifier"/> instances are equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(AtIdentifier? left, AtIdentifier? right) => Equals(left, right);

    /// <summary>
    /// Determines whether two <see cref="AtIdentifier"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(AtIdentifier? left, AtIdentifier? right) => !Equals(left, right);
}
