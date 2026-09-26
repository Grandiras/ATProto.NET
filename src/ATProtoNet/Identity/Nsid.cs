using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Represents a Namespaced Identifier (NSID) used to identify Lexicon schemas.
/// Format: segment.segment.name (e.g., com.atproto.repo.createRecord)
/// </summary>
/// <remarks>Equality and ordering are ordinal on <see cref="Value"/>.</remarks>
[JsonConverter(typeof(IdentifierJsonConverter<Nsid>))]
public sealed partial record Nsid : IIdentifier<Nsid>
{
    private const int MaxLength = 317;

    // The spec's reference pattern: a reversed-domain authority of at least two segments
    // (1-63 chars each, no leading/trailing hyphen, the first not starting with a digit),
    // then a name segment of 1-63 ASCII letters and digits starting with a letter.
    [GeneratedRegex(@"^[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+(\.[a-zA-Z]([a-zA-Z0-9]{0,62})?)\z")]
    private static partial Regex NsidPattern();

    /// <summary>
    /// The full NSID string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The authority segments (reversed domain), e.g., "com.atproto.repo".
    /// </summary>
    public string Authority => Value[..Value.LastIndexOf('.')];

    /// <summary>
    /// The name segment (last part), e.g., "createRecord".
    /// </summary>
    public string Name => Value[(Value.LastIndexOf('.') + 1)..];

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
    /// <param name="value">The NSID string.</param>
    /// <returns>A validated NSID.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid NSID.</exception>
    public static Nsid Parse(string value) =>
        TryParse(value, out var nsid) ? nsid : throw IIdentifier<Nsid>.InvalidValue(value, "NSID");

    /// <summary>
    /// Attempts to create an NSID from a string value without throwing.
    /// </summary>
    /// <param name="value">The NSID string.</param>
    /// <param name="nsid">The parsed NSID on success.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid NSID.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out Nsid? nsid) =>
        TryCreate(value, value, out nsid);

    static bool IIdentifier<Nsid>.TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Nsid? result) =>
        TryCreate(span, text, out result);

    internal static bool TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Nsid? result)
    {
        result = span.Length <= MaxLength && NsidPattern().IsMatch(span)
            ? new Nsid(text ?? span.ToString())
            : null;
        return result is not null;
    }

    /// <summary>
    /// Implicitly converts a <see cref="Nsid"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="nsid">The value to convert.</param>
    /// <returns>The converted value, or <see langword="null"/> for a <see langword="null"/> NSID.</returns>
    [return: NotNullIfNotNull(nameof(nsid))]
    public static implicit operator string?(Nsid? nsid) => nsid?.Value;

    /// <summary>
    /// Explicitly converts a <see cref="string"/> to its <see cref="Nsid"/> representation.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid <see cref="Nsid"/>.</exception>
    public static explicit operator Nsid(string value) => Parse(value);

    /// <inheritdoc />
    public int CompareTo(Nsid? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
