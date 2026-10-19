using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>The key of a record within its collection, such as <c>self</c> or a <see cref="Tid"/>.</summary>
/// <remarks>Record keys are case-sensitive. See <see href="https://atproto.com/specs/record-key"/>.</remarks>
[JsonConverter(typeof(IdentifierJsonConverter<RecordKey>))]
public sealed class RecordKey : IAtIdentifier<RecordKey>
{
    /// <summary>The longest record key, in characters.</summary>
    public const int MaxLength = 512;

    private RecordKey(string value) => Value = value;

    /// <summary>The key <c>self</c>, which singleton records such as a profile use.</summary>
    public static RecordKey Self { get; } = new("self");

    /// <inheritdoc />
    public string Value { get; }

    /// <summary>The record key whose text is <paramref name="tid"/>.</summary>
    /// <param name="tid">The TID.</param>
    /// <returns>The record key.</returns>
    public static RecordKey From(Tid tid) => new(tid.Value);

    internal static RecordKey FromValidated(string value) => new(value);

    internal static bool TryRead<TChar>(ReadOnlySpan<TChar> text, string? value, [NotNullWhen(true)] out RecordKey? result) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = IdentifierSyntax.IsRecordKey(text) ? new(value ?? Identifiers.Text(text)) : null;
        return result is not null;
    }

    /// <inheritdoc />
    public static RecordKey Parse(string s) => Identifiers.Parse<RecordKey>(s);

    /// <inheritdoc />
    public static RecordKey Parse(ReadOnlySpan<char> s) => Identifiers.Parse<RecordKey>(s);

    /// <inheritdoc />
    public static RecordKey Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<RecordKey>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out RecordKey? result) => TryRead(s.AsSpan(), s, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out RecordKey? result) => TryRead(s, null, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out RecordKey? result) => TryRead(utf8Text, null, out result);

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] RecordKey? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as RecordKey);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(RecordKey? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The canonical text of <paramref name="value"/>.</summary>
    /// <param name="value">The identifier.</param>
    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator string?(RecordKey? value) => value?.Value;

    /// <summary>Whether two identifiers are the same.</summary>
    public static bool operator ==(RecordKey? left, RecordKey? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two identifiers differ.</summary>
    public static bool operator !=(RecordKey? left, RecordKey? right) => !(left == right);

    /// <inheritdoc />
    static int IAtIdentifier<RecordKey>.MaxLength => MaxLength;
}
