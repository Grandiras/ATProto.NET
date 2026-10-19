using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>The address of a repository, a collection or a record: <c>at://AUTHORITY[/COLLECTION[/RKEY]]</c>.</summary>
/// <remarks>
/// The restricted syntax Lexicon <c>at-uri</c> fields use: the authority is a DID or handle, the collection an NSID and the record key a
/// record key; no query, fragment, trailing slash or empty segment, at most 8 KiB. <see cref="Value"/> keeps the text as given, so a
/// handle authority is lower-cased only in <see cref="Authority"/>, and equality is ordinal on the text. Space addresses are
/// <see cref="SpaceUri"/> and <see cref="SpaceRecordUri"/>. See <see href="https://atproto.com/specs/at-uri-scheme"/>.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<AtUri>))]
public sealed class AtUri : IAtIdentifier<AtUri>
{
    /// <summary>The longest AT URI, in characters.</summary>
    public const int MaxLength = Address.MaxLength;

    private readonly int _authorityEnd, _collectionEnd;
    private AtIdentifier? _authority;
    private Nsid? _collection;
    private RecordKey? _recordKey;

    private AtUri(string value, int authorityEnd, int collectionEnd)
    {
        Value = value;
        _authorityEnd = authorityEnd;
        _collectionEnd = collectionEnd;
    }

    /// <inheritdoc />
    public string Value { get; }

    // The parts are built on first use and cached without locking: a race builds one twice, to equal values.

    /// <summary>The DID or handle the URI is rooted at.</summary>
    public AtIdentifier Authority => _authority ??= AtIdentifier.FromValidated(Value[Address.Scheme.Length.._authorityEnd]);

    /// <summary>The collection, when the URI has one.</summary>
    public Nsid? Collection => _collectionEnd < 0 ? null : _collection ??= Nsid.FromValidated(Value[(_authorityEnd + 1).._collectionEnd]);

    /// <summary>The record key, when the URI has one.</summary>
    public RecordKey? RecordKey => _collectionEnd < 0 || _collectionEnd == Value.Length ? null : _recordKey ??= ATProtoNet.RecordKey.FromValidated(Value[(_collectionEnd + 1)..]);

    /// <summary>Builds the AT URI of the given parts.</summary>
    /// <param name="authority">The DID or handle.</param>
    /// <param name="collection">The collection, if any.</param>
    /// <param name="recordKey">The record key, if any; it needs a <paramref name="collection"/>.</param>
    /// <returns>The URI that <see cref="Parse(string)"/> reads back.</returns>
    /// <exception cref="ArgumentException"><paramref name="recordKey"/> is given without a <paramref name="collection"/>.</exception>
    public static AtUri Create(AtIdentifier authority, Nsid? collection = null, RecordKey? recordKey = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (recordKey is not null && collection is null) throw new ArgumentException("A record key needs a collection.", nameof(recordKey));
        var value = $"{Address.Scheme}{authority}{(collection is null ? "" : $"/{collection}")}{(recordKey is null ? "" : $"/{recordKey}")}";
        var authorityEnd = Address.Scheme.Length + authority.Value.Length;
        return new(value, authorityEnd, collection is null ? -1 : authorityEnd + 1 + collection.Value.Length)
        {
            _authority = authority,
            _collection = collection,
            _recordKey = recordKey,
        };
    }

    internal static AtUri Of(string value, in Address.Ends ends) => new(value, ends[0], ends[1]);

    internal static bool TryRead<TChar>(ReadOnlySpan<TChar> text, string? value, [NotNullWhen(true)] out AtUri? result) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = Address.Scan(text, out var ends) == Address.Kind.Public ? Of(value ?? Identifiers.Text(text), ends) : null;
        return result is not null;
    }

    /// <inheritdoc />
    public static AtUri Parse(string s) => Identifiers.Parse<AtUri>(s);

    /// <inheritdoc />
    public static AtUri Parse(ReadOnlySpan<char> s) => Identifiers.Parse<AtUri>(s);

    /// <inheritdoc />
    public static AtUri Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<AtUri>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out AtUri? result) => TryRead(s.AsSpan(), s, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out AtUri? result) => TryRead(s, null, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out AtUri? result) => TryRead(utf8Text, null, out result);

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] AtUri? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as AtUri);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(AtUri? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The canonical text of <paramref name="value"/>.</summary>
    /// <param name="value">The identifier.</param>
    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator string?(AtUri? value) => value?.Value;

    /// <summary>Whether two identifiers are the same.</summary>
    public static bool operator ==(AtUri? left, AtUri? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two identifiers differ.</summary>
    public static bool operator !=(AtUri? left, AtUri? right) => !(left == right);

    /// <inheritdoc />
    static int IAtIdentifier<AtUri>.MaxLength => MaxLength;
}
