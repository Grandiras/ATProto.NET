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
/// <remarks>
/// Equality and ordering are ordinal on <see cref="Value"/>. Validation follows the atproto
/// DID syntax, which is stricter than W3C DID syntax: it is checked against the
/// <c>atproto-interop-tests</c> syntax fixtures.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<Did>))]
public sealed partial record Did : IIdentifier<Did>
{
    private const int MaxLength = 2048;

    // did:<method>:<method-specific-id>. The method is lowercase letters only; the id may
    // contain '%' (percent-encoding) and ':', but may not end with either.
    [GeneratedRegex(@"^did:[a-z]+:[a-zA-Z0-9._:%-]*[a-zA-Z0-9._-]$")]
    private static partial Regex DidPattern();

    /// <summary>
    /// The full DID string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The DID method (e.g., "plc", "web").
    /// </summary>
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
    public static Did Parse(string value) =>
        TryParse(value, out var did) ? did : throw IIdentifier<Did>.InvalidValue(value, "DID");

    /// <summary>
    /// Attempts to create a DID from a string value without throwing.
    /// </summary>
    /// <param name="value">The DID string.</param>
    /// <param name="did">The parsed DID on success.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid DID.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out Did? did) =>
        TryCreate(value, value, out did);

    static bool IIdentifier<Did>.TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Did? result) =>
        TryCreate(span, text, out result);

    internal static bool TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Did? result)
    {
        result = span.Length <= MaxLength && DidPattern().IsMatch(span)
            ? new Did(text ?? span.ToString())
            : null;
        return result is not null;
    }

    /// <summary>
    /// Implicitly converts a <see cref="Did"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="did">The value to convert.</param>
    /// <returns>The converted value, or <see langword="null"/> for a <see langword="null"/> DID.</returns>
    [return: NotNullIfNotNull(nameof(did))]
    public static implicit operator string?(Did? did) => did?.Value;

    /// <summary>
    /// Explicitly converts a <see cref="string"/> to its <see cref="Did"/> representation.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid <see cref="Did"/>.</exception>
    public static explicit operator Did(string value) => Parse(value);

    /// <inheritdoc />
    public int CompareTo(Did? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
