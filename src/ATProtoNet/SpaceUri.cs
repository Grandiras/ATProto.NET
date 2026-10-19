using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>The address of a space: <c>at://{authority}/space/{spaceType}/{skey}</c>.</summary>
/// <remarks>
/// The authority is a DID, never a handle: a space's identity and membership are keyed on DIDs. The same scanner as <see cref="AtUri"/>
/// reads it, with the same restrictions and maximum length; the literal segment <c>space</c> cannot be an NSID, so space addresses
/// never collide with public AT URIs. <see cref="Value"/> keeps the text as given and equality is ordinal on it. The syntax is pinned
/// to the Atproto Spaces proposal (design/specs/spaces.md).
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<SpaceUri>))]
public sealed class SpaceUri : IAtIdentifier<SpaceUri>
{
    /// <summary>The longest space address, in characters.</summary>
    public const int MaxLength = Address.MaxLength;

    private SpaceUri(string value, Did authority, Nsid spaceType, RecordKey skey)
    {
        Value = value;
        Authority = authority;
        SpaceType = spaceType;
        Skey = skey;
    }

    /// <inheritdoc />
    public string Value { get; }

    /// <summary>The DID at the root of the space.</summary>
    public Did Authority { get; }

    /// <summary>The NSID naming the kind of space.</summary>
    public Nsid SpaceType { get; }

    /// <summary>The key distinguishing spaces of one type under one authority.</summary>
    public RecordKey Skey { get; }

    /// <summary>Builds the address of the given parts.</summary>
    /// <param name="authority">The space authority.</param>
    /// <param name="spaceType">The space type.</param>
    /// <param name="skey">The space key.</param>
    /// <returns>The address that <see cref="Parse(string)"/> reads back.</returns>
    public static SpaceUri Create(Did authority, Nsid spaceType, RecordKey skey)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(spaceType);
        ArgumentNullException.ThrowIfNull(skey);
        return new($"{Address.Scheme}{authority}/{Address.SpaceMarker}/{spaceType}/{skey}", authority, spaceType, skey);
    }

    /// <summary>The address of a record of this space.</summary>
    /// <param name="author">The DID of the account that wrote the record.</param>
    /// <param name="collection">The collection.</param>
    /// <param name="rkey">The record key.</param>
    /// <returns>The record address, whose <see cref="SpaceRecordUri.Space"/> is this space.</returns>
    public SpaceRecordUri Record(Did author, Nsid collection, RecordKey rkey) => SpaceRecordUri.Create(this, author, collection, rkey);

    internal static SpaceUri Of(string value, in Address.Ends ends) => new(value,
        Did.FromValidated(Address.Slice(value, ends, 0)), Nsid.FromValidated(Address.Slice(value, ends, 2)), RecordKey.FromValidated(Address.Slice(value, ends, 3)));

    internal static bool TryRead<TChar>(ReadOnlySpan<TChar> text, string? value, [NotNullWhen(true)] out SpaceUri? result) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = Address.Scan(text, out var ends) == Address.Kind.Space ? Of(value ?? Identifiers.Text(text), ends) : null;
        return result is not null;
    }

    /// <inheritdoc />
    public static SpaceUri Parse(string s) => Identifiers.Parse<SpaceUri>(s);

    /// <inheritdoc />
    public static SpaceUri Parse(ReadOnlySpan<char> s) => Identifiers.Parse<SpaceUri>(s);

    /// <inheritdoc />
    public static SpaceUri Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<SpaceUri>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out SpaceUri? result) => TryRead(s.AsSpan(), s, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out SpaceUri? result) => TryRead(s, null, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out SpaceUri? result) => TryRead(utf8Text, null, out result);

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] SpaceUri? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as SpaceUri);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(SpaceUri? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The canonical text of <paramref name="value"/>.</summary>
    /// <param name="value">The identifier.</param>
    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator string?(SpaceUri? value) => value?.Value;

    /// <summary>Whether two identifiers are the same.</summary>
    public static bool operator ==(SpaceUri? left, SpaceUri? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two identifiers differ.</summary>
    public static bool operator !=(SpaceUri? left, SpaceUri? right) => !(left == right);

    /// <inheritdoc />
    static int IAtIdentifier<SpaceUri>.MaxLength => MaxLength;
}
