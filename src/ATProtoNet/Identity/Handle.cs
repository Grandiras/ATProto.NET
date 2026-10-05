using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>Represents an AT Protocol Handle (domain name identifier). Handles are human-readable identifiers that map to DIDs. Examples: alice.bsky.social, bob.example.com</summary>
/// <remarks>
/// Parsing strips one leading <c>@</c> (common user input) and lower-cases the handle, so
/// <see cref="Value"/> is always normalized and equality and ordering are ordinal on it.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<Handle>))]
public sealed record Handle : IIdentifier<Handle>
{
    /// <summary><c>handle.invalid</c>: what an account is shown as when its handle does not verify bidirectionally. The <c>.invalid</c> TLD never resolves.</summary>
    public static Handle Invalid { get; } = new("handle.invalid");

    /// <summary>The handle string value (normalized to lowercase).</summary>
    public string Value { get; }

    private Handle(string value)
    {
        Value = value;
    }

    /// <summary>Creates a Handle from a string value with validation.</summary>
    /// <param name="value">The handle, optionally prefixed with <c>@</c>.</param>
    /// <returns>A validated, lower-cased handle.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid handle.</exception>
    public static Handle Parse(string value) =>
        TryParse(value, out var handle) ? handle : throw IIdentifier<Handle>.InvalidValue(value, "handle");

    /// <summary>Attempts to create a Handle from a string value without throwing.</summary>
    /// <param name="value">The handle, optionally prefixed with <c>@</c>.</param>
    /// <param name="handle">The parsed, lower-cased handle on success.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid handle.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out Handle? handle) =>
        TryCreate(value, value, out handle);

    static bool IIdentifier<Handle>.TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Handle? result) =>
        TryCreate(span, text, out result);

    // Creates the handle of text already known to be valid, lower-cased.
    internal static Handle FromValidated(string text) => new(text.ToLowerInvariant());

    internal static bool TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Handle? result)
    {
        result = null;

        if (span.StartsWith('@'))
        {
            span = span[1..];
            text = null;
        }

        if (!IdentifierSyntax.IsHandle(span, out var hasUpper))
            return false;

        // The syntax admits ASCII only, so invariant lower-casing is the whole normalization.
        text ??= span.ToString();
        result = new Handle(hasUpper ? text.ToLowerInvariant() : text);
        return true;
    }

    static bool IIdentifier<Handle>.TryCreate(ReadOnlySpan<byte> utf8, [NotNullWhen(true)] out Handle? result) =>
        TryCreate(utf8, out result);

    internal static bool TryCreate(ReadOnlySpan<byte> utf8, [NotNullWhen(true)] out Handle? result)
    {
        // An @-prefixed handle takes the UTF-16 path, which strips the prefix.
        if (!IdentifierSyntax.IsHandle(utf8, out var hasUpper))
        {
            result = null;
            return false;
        }

        var text = IdentifierSyntax.ToAsciiString(utf8);
        result = new Handle(hasUpper ? text.ToLowerInvariant() : text);
        return true;
    }

    /// <summary>Implicitly converts a <see cref="Handle"/> to its <see cref="string"/> representation.</summary>
    /// <param name="handle">The value to convert.</param>
    /// <returns>The converted value, or <see langword="null"/> for a <see langword="null"/> handle.</returns>
    [return: NotNullIfNotNull(nameof(handle))]
    public static implicit operator string?(Handle? handle) => handle?.Value;

    /// <summary>Explicitly converts a <see cref="string"/> to its <see cref="Handle"/> representation.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid <see cref="Handle"/>.</exception>
    public static explicit operator Handle(string value) => Parse(value);

    /// <inheritdoc />
    public int CompareTo(Handle? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
