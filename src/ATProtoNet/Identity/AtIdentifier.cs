using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Represents an AT Protocol identifier that can be either a DID or a Handle.
/// Used in scenarios where either identifier type is accepted (e.g., API parameters).
/// </summary>
/// <remarks>
/// A DID always starts with <c>did:</c> and a handle cannot contain a colon, so the two never
/// overlap. Equality and ordering are ordinal on <see cref="Value"/>.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<AtIdentifier>))]
public sealed record AtIdentifier : IIdentifier<AtIdentifier>
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
    /// <param name="did">The DID.</param>
    /// <returns>An identifier wrapping <paramref name="did"/>.</returns>
    public static AtIdentifier FromDid(Did did)
    {
        ArgumentNullException.ThrowIfNull(did);
        return new AtIdentifier(did);
    }

    /// <summary>
    /// Creates an AtIdentifier from a Handle.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <returns>An identifier wrapping <paramref name="handle"/>.</returns>
    public static AtIdentifier FromHandle(Handle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return new AtIdentifier(handle);
    }

    /// <summary>
    /// Parses a string as an AT identifier (either DID or Handle).
    /// A handle is normalized as by <see cref="Identity.Handle.Parse"/>.
    /// </summary>
    /// <param name="value">The DID or handle.</param>
    /// <returns>The parsed identifier.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is neither a valid DID nor a valid handle.</exception>
    public static AtIdentifier Parse(string value) =>
        TryParse(value, out var identifier)
            ? identifier
            : throw IIdentifier<AtIdentifier>.InvalidValue(value, "AT identifier");

    /// <summary>
    /// Attempts to parse a string as an AT identifier without throwing.
    /// </summary>
    /// <param name="value">The DID or handle.</param>
    /// <param name="identifier">The parsed identifier on success.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid DID or handle.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out AtIdentifier? identifier) =>
        TryCreate(value, value, out identifier);

    static bool IIdentifier<AtIdentifier>.TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out AtIdentifier? result) =>
        TryCreate(span, text, out result);

    /// <summary>
    /// Parses an identifier exactly as written, as the authority of an AT URI requires: the
    /// <c>@</c> prefix <see cref="Identity.Handle.Parse"/> tolerates in user input is rejected.
    /// </summary>
    internal static bool TryCreateStrict(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out AtIdentifier? result)
    {
        if (span.StartsWith('@'))
        {
            result = null;
            return false;
        }

        return TryCreate(span, text, out result);
    }

    internal static bool TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out AtIdentifier? result)
    {
        result = null;
        if (span.StartsWith("did:", StringComparison.Ordinal))
        {
            if (Identity.Did.TryCreate(span, text, out var did))
                result = new AtIdentifier(did);
        }
        else if (Identity.Handle.TryCreate(span, text, out var handle))
        {
            result = new AtIdentifier(handle);
        }

        return result is not null;
    }

    /// <summary>
    /// Implicitly converts a <see cref="Did"/> to its <see cref="AtIdentifier"/> representation.
    /// </summary>
    /// <param name="did">The value to convert.</param>
    /// <returns>The converted value, or <see langword="null"/> for a <see langword="null"/> DID.</returns>
    [return: NotNullIfNotNull(nameof(did))]
    public static implicit operator AtIdentifier?(Did? did) => did is null ? null : new(did);

    /// <summary>
    /// Implicitly converts a <see cref="Handle"/> to its <see cref="AtIdentifier"/> representation.
    /// </summary>
    /// <param name="handle">The value to convert.</param>
    /// <returns>The converted value, or <see langword="null"/> for a <see langword="null"/> handle.</returns>
    [return: NotNullIfNotNull(nameof(handle))]
    public static implicit operator AtIdentifier?(Handle? handle) => handle is null ? null : new(handle);

    /// <summary>
    /// Implicitly converts a <see cref="AtIdentifier"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="id">The value to convert.</param>
    /// <returns>The converted value, or <see langword="null"/> for a <see langword="null"/> identifier.</returns>
    [return: NotNullIfNotNull(nameof(id))]
    public static implicit operator string?(AtIdentifier? id) => id?.Value;

    /// <summary>
    /// Explicitly converts a <see cref="string"/> to its <see cref="AtIdentifier"/> representation.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is neither a valid DID nor a valid handle.</exception>
    public static explicit operator AtIdentifier(string value) => Parse(value);

    /// <inheritdoc />
    public int CompareTo(AtIdentifier? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
