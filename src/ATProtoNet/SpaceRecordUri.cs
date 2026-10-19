using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>The address of a record in a space: <c>at://{authority}/space/{spaceType}/{skey}/{author}/{collection}/{rkey}</c>.</summary>
/// <remarks>
/// The authority and the author are DIDs, never handles. The authority is the space's; the author wrote and signed the record. The syntax,
/// restrictions and equality are those of <see cref="SpaceUri"/>.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<SpaceRecordUri>))]
public sealed class SpaceRecordUri : IAtIdentifier<SpaceRecordUri>
{
    /// <summary>The longest record address, in characters.</summary>
    public const int MaxLength = Address.MaxLength;

    private SpaceRecordUri(string value, SpaceUri space, Did author, Nsid collection, RecordKey rkey)
    {
        Value = value;
        Space = space;
        Author = author;
        Collection = collection;
        Rkey = rkey;
    }

    /// <inheritdoc />
    public string Value { get; }

    /// <summary>The space the record lives in.</summary>
    public SpaceUri Space { get; }

    /// <summary>The DID of the account that wrote the record.</summary>
    public Did Author { get; }

    /// <summary>The collection.</summary>
    public Nsid Collection { get; }

    /// <summary>The record key.</summary>
    public RecordKey Rkey { get; }

    /// <summary>The record's path in the space, <c>{collection}/{rkey}</c>.</summary>
    public string Path => $"{Collection}/{Rkey}";

    internal static SpaceRecordUri Create(SpaceUri space, Did author, Nsid collection, RecordKey rkey)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(rkey);
        return new($"{space}/{author}/{collection}/{rkey}", space, author, collection, rkey);
    }

    internal static SpaceRecordUri Of(string value, in Address.Ends ends) => new(value, SpaceUri.Of(value[..ends[3]], ends),
        Did.FromValidated(Address.Slice(value, ends, 4)), Nsid.FromValidated(Address.Slice(value, ends, 5)), RecordKey.FromValidated(Address.Slice(value, ends, 6)));

    internal static bool TryRead<TChar>(ReadOnlySpan<TChar> text, string? value, [NotNullWhen(true)] out SpaceRecordUri? result) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = Address.Scan(text, out var ends) == Address.Kind.SpaceRecord ? Of(value ?? Identifiers.Text(text), ends) : null;
        return result is not null;
    }

    /// <inheritdoc />
    public static SpaceRecordUri Parse(string s) => Identifiers.Parse<SpaceRecordUri>(s);

    /// <inheritdoc />
    public static SpaceRecordUri Parse(ReadOnlySpan<char> s) => Identifiers.Parse<SpaceRecordUri>(s);

    /// <inheritdoc />
    public static SpaceRecordUri Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<SpaceRecordUri>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out SpaceRecordUri? result) => TryRead(s.AsSpan(), s, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out SpaceRecordUri? result) => TryRead(s, null, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out SpaceRecordUri? result) => TryRead(utf8Text, null, out result);

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] SpaceRecordUri? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as SpaceRecordUri);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(SpaceRecordUri? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The canonical text of <paramref name="value"/>.</summary>
    /// <param name="value">The identifier.</param>
    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator string?(SpaceRecordUri? value) => value?.Value;

    /// <summary>Whether two identifiers are the same.</summary>
    public static bool operator ==(SpaceRecordUri? left, SpaceRecordUri? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two identifiers differ.</summary>
    public static bool operator !=(SpaceRecordUri? left, SpaceRecordUri? right) => !(left == right);

    /// <inheritdoc />
    static int IAtIdentifier<SpaceRecordUri>.MaxLength => MaxLength;
}
