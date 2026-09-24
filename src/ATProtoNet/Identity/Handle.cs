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
/// <remarks>
/// Parsing strips one leading <c>@</c> (common user input) and lower-cases the handle, so
/// <see cref="Value"/> is always normalized and equality and ordering are ordinal on it.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<Handle>))]
public sealed partial record Handle : IIdentifier<Handle>
{
    private const int MaxLength = 253;

    // Handle must be a valid domain name
    // Each label: 1-63 chars, alphanumeric + hyphens, no leading/trailing hyphens
    // The top-level label may not start with a digit
    [GeneratedRegex(@"^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$")]
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
    /// <param name="value">The handle, optionally prefixed with <c>@</c>.</param>
    /// <returns>A validated, lower-cased handle.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid handle.</exception>
    public static Handle Parse(string value) =>
        TryParse(value, out var handle) ? handle : throw IIdentifier<Handle>.InvalidValue(value, "handle");

    /// <summary>
    /// Attempts to create a Handle from a string value without throwing.
    /// </summary>
    /// <param name="value">The handle, optionally prefixed with <c>@</c>.</param>
    /// <param name="handle">The parsed, lower-cased handle on success.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid handle.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out Handle? handle) =>
        TryCreate(value, value, out handle);

    static bool IIdentifier<Handle>.TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Handle? result) =>
        TryCreate(span, text, out result);

    /// <summary>
    /// Whether <paramref name="span"/> is a handle exactly as written: no <c>@</c> prefix is
    /// stripped. Case is not significant.
    /// </summary>
    internal static bool IsValidSyntax(ReadOnlySpan<char> span) =>
        span.Length <= MaxLength && HandlePattern().IsMatch(span);

    internal static bool TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Handle? result)
    {
        result = null;

        if (span.StartsWith('@'))
        {
            span = span[1..];
            text = null;
        }

        if (!IsValidSyntax(span))
            return false;

        // The pattern admits ASCII only, so invariant lower-casing is the whole normalization.
        // ToLowerInvariant returns the same instance when nothing changes.
        result = new Handle((text ?? span.ToString()).ToLowerInvariant());
        return true;
    }

    /// <summary>
    /// Implicitly converts a <see cref="Handle"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="handle">The value to convert.</param>
    /// <returns>The converted value, or <see langword="null"/> for a <see langword="null"/> handle.</returns>
    [return: NotNullIfNotNull(nameof(handle))]
    public static implicit operator string?(Handle? handle) => handle?.Value;

    /// <summary>
    /// Explicitly converts a <see cref="string"/> to its <see cref="Handle"/> representation.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid <see cref="Handle"/>.</exception>
    public static explicit operator Handle(string value) => Parse(value);

    /// <inheritdoc />
    public int CompareTo(Handle? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
