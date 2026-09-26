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
/// <remarks>Record keys are case-sensitive; equality and ordering are ordinal on <see cref="Value"/>.</remarks>
[JsonConverter(typeof(IdentifierJsonConverter<RecordKey>))]
public sealed partial record RecordKey : IIdentifier<RecordKey>
{
    // 1-512 characters from A-Z a-z 0-9 . - _ : ~ (the URI "unreserved" set plus ':'),
    // excluding the relative-path segments "." and "..".
    [GeneratedRegex(@"^[A-Za-z0-9._:~-]{1,512}\z")]
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
    /// <param name="value">The record key string.</param>
    /// <returns>A validated record key.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid record key.</exception>
    public static RecordKey Parse(string value) =>
        TryParse(value, out var key) ? key : throw IIdentifier<RecordKey>.InvalidValue(value, "record key");

    /// <summary>
    /// Attempts to create a RecordKey from a string value without throwing.
    /// </summary>
    /// <param name="value">The record key string.</param>
    /// <param name="recordKey">The parsed record key on success.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid record key.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out RecordKey? recordKey) =>
        TryCreate(value, value, out recordKey);

    static bool IIdentifier<RecordKey>.TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out RecordKey? result) =>
        TryCreate(span, text, out result);

    internal static bool TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out RecordKey? result)
    {
        result = span is not "." and not ".." && RecordKeyPattern().IsMatch(span)
            ? new RecordKey(text ?? span.ToString())
            : null;
        return result is not null;
    }

    /// <summary>
    /// Creates a new TID-based record key from the process-wide <see cref="TidGenerator"/>.
    /// </summary>
    /// <returns>A TID record key; successive calls return increasing keys.</returns>
    public static RecordKey NewTid() => new(Tid.Next().Value);

    /// <summary>
    /// Implicitly converts a <see cref="RecordKey"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="key">The value to convert.</param>
    /// <returns>The converted value, or <see langword="null"/> for a <see langword="null"/> key.</returns>
    [return: NotNullIfNotNull(nameof(key))]
    public static implicit operator string?(RecordKey? key) => key?.Value;

    /// <summary>
    /// Explicitly converts a <see cref="string"/> to its <see cref="RecordKey"/> representation.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid <see cref="RecordKey"/>.</exception>
    public static explicit operator RecordKey(string value) => Parse(value);

    /// <inheritdoc />
    public int CompareTo(RecordKey? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
