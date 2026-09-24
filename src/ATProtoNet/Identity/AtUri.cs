using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Represents an AT URI, the URI scheme for addressing records in the AT Protocol.
/// Format: at://&lt;authority&gt;/&lt;collection&gt;/&lt;rkey&gt;
/// Examples: at://did:plc:xxx/app.bsky.feed.post/3k2la, at://alice.bsky.social/app.bsky.actor.profile/self
/// </summary>
/// <remarks>
/// <para>Parsing follows the restricted AT URI syntax that Lexicon <c>at-uri</c> fields use:
/// <c>at://AUTHORITY[/COLLECTION[/RKEY]]</c>, where the authority is a DID or handle, the
/// collection an NSID and the rkey a record key. Query strings, fragments, trailing slashes and
/// empty or extra path segments are rejected.</para>
/// <para>Equality and ordering are ordinal on <see cref="Value"/>.</para>
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<AtUri>))]
public sealed record AtUri : IIdentifier<AtUri>
{
    private const string Scheme = "at://";
    private const int MaxLength = 8 * 1024;

    /// <summary>
    /// The full AT URI string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The authority part (DID or handle), as written in the URI.
    /// </summary>
    public string Authority { get; }

    /// <summary>
    /// The authority parsed as an <see cref="AtIdentifier"/>. A handle authority is lower-cased
    /// here, as <see cref="Handle"/> always is.
    /// </summary>
    public AtIdentifier Repo { get; }

    /// <summary>
    /// The collection NSID, if present.
    /// </summary>
    public Nsid? Collection { get; }

    /// <summary>
    /// The record key, if present.
    /// </summary>
    public RecordKey? RecordKey { get; }

    private AtUri(string value, string authority, AtIdentifier repo, Nsid? collection, RecordKey? recordKey)
    {
        Value = value;
        Authority = authority;
        Repo = repo;
        Collection = collection;
        RecordKey = recordKey;
    }

    /// <summary>
    /// Creates an AT URI from a string value with validation.
    /// </summary>
    /// <param name="value">The AT URI string.</param>
    /// <returns>A validated AT URI.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid AT URI.</exception>
    public static AtUri Parse(string value) =>
        TryParse(value, out var uri) ? uri : throw IIdentifier<AtUri>.InvalidValue(value, "AT URI");

    /// <summary>
    /// Attempts to create an AT URI from a string value without throwing.
    /// </summary>
    /// <param name="value">The AT URI string.</param>
    /// <param name="atUri">The parsed AT URI on success.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid AT URI.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out AtUri? atUri)
    {
        atUri = null;
        return value is not null && TryCreate(value, out atUri);
    }

    static bool IIdentifier<AtUri>.TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out AtUri? result)
    {
        result = null;
        return span.StartsWith(Scheme, StringComparison.Ordinal)
            && span.Length <= MaxLength
            && TryCreate(text ?? span.ToString(), out result);
    }

    private static bool TryCreate(string value, [NotNullWhen(true)] out AtUri? result)
    {
        result = null;
        if (value.Length > MaxLength || !value.StartsWith(Scheme, StringComparison.Ordinal))
            return false;

        // Each part's own syntax excludes '/', '?', '#' and whitespace, so splitting on '/' and
        // validating every part also rejects queries, fragments and empty segments.
        var path = value.AsSpan(Scheme.Length);
        var slash = path.IndexOf('/');
        var authoritySpan = slash < 0 ? path : path[..slash];
        if (!AtIdentifier.TryCreateStrict(authoritySpan, null, out var repo))
            return false;

        // A handle is lower-cased in Repo; Authority keeps the text as written.
        var authority = authoritySpan.SequenceEqual(repo.Value) ? repo.Value : authoritySpan.ToString();

        Nsid? collection = null;
        Identity.RecordKey? rkey = null;
        if (slash >= 0)
        {
            path = path[(slash + 1)..];
            slash = path.IndexOf('/');
            var collectionSpan = slash < 0 ? path : path[..slash];
            if (!Nsid.TryCreate(collectionSpan, null, out collection))
                return false;

            if (slash >= 0 && !Identity.RecordKey.TryCreate(path[(slash + 1)..], null, out rkey))
                return false;
        }

        result = new AtUri(value, authority, repo, collection, rkey);
        return true;
    }

    /// <summary>
    /// Creates a new AT URI from components.
    /// </summary>
    /// <param name="repo">The repository: a DID or handle.</param>
    /// <param name="collection">The collection NSID, if any.</param>
    /// <param name="rkey">The record key, if any. It requires a <paramref name="collection"/>.</param>
    /// <returns>The AT URI addressing the given repository, collection or record.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="rkey"/> is given without a <paramref name="collection"/>.
    /// </exception>
    public static AtUri Create(AtIdentifier repo, Nsid? collection = null, RecordKey? rkey = null)
    {
        ArgumentNullException.ThrowIfNull(repo);
        if (rkey is not null && collection is null)
            throw new ArgumentException("A record key requires a collection.", nameof(rkey));

        // Every part is already valid and their maximum lengths sum to well under 8 KiB, so the
        // result needs no re-parsing.
        var value = collection is null ? $"{Scheme}{repo.Value}"
            : rkey is null ? $"{Scheme}{repo.Value}/{collection.Value}"
            : $"{Scheme}{repo.Value}/{collection.Value}/{rkey.Value}";

        return new AtUri(value, repo.Value, repo, collection, rkey);
    }

    /// <summary>
    /// Implicitly converts a <see cref="AtUri"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="atUri">The value to convert.</param>
    /// <returns>The converted value, or <see langword="null"/> for a <see langword="null"/> URI.</returns>
    [return: NotNullIfNotNull(nameof(atUri))]
    public static implicit operator string?(AtUri? atUri) => atUri?.Value;

    /// <summary>
    /// Explicitly converts a <see cref="string"/> to its <see cref="AtUri"/> representation.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid <see cref="AtUri"/>.</exception>
    public static explicit operator AtUri(string value) => Parse(value);

    /// <inheritdoc />
    public bool Equals(AtUri? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public int CompareTo(AtUri? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
