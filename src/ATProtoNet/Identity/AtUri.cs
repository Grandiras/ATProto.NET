using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>Represents an AT URI, the URI scheme for addressing records in the AT Protocol. Format: at://&lt;authority&gt;/&lt;collection&gt;/&lt;rkey&gt; Examples: at://did:plc:xxx/app.bsky.feed.post/3k2la, at://alice.bsky.social/app.bsky.actor.profile/self</summary>
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

    // Where the parts end in Value: just past the authority, and just past the collection (-1 without
    // one). Parsing validates every part but builds each only on first use, since most URIs read off
    // the wire are only ever compared or written back.
    private readonly int _authorityEnd;
    private readonly int _collectionEnd;

    // The parts built so far. A race builds a part twice, to equal values.
    private Parts? _parts;

    /// <summary>The full AT URI string value.</summary>
    public string Value { get; }

    /// <summary>The authority part (DID or handle), as written in the URI.</summary>
    public string Authority => (_parts ??= new()).Authority ??= Value[Scheme.Length.._authorityEnd];

    /// <summary>The authority parsed as an <see cref="AtIdentifier"/>. A handle authority is lower-cased here, as <see cref="Handle"/> always is.</summary>
    public AtIdentifier Repo => (_parts ??= new()).Repo ??= AtIdentifier.FromValidated(Authority);

    /// <summary>The collection NSID, if present.</summary>
    public Nsid? Collection => _collectionEnd < 0
        ? null
        : (_parts ??= new()).Collection ??= Nsid.FromValidated(Value[(_authorityEnd + 1).._collectionEnd]);

    /// <summary>The record key, if present.</summary>
    public RecordKey? RecordKey => _collectionEnd < 0 || _collectionEnd == Value.Length
        ? null
        : (_parts ??= new()).RecordKey ??= Identity.RecordKey.FromValidated(Value[(_collectionEnd + 1)..]);

    private AtUri(string value, int authorityEnd, int collectionEnd)
    {
        Value = value;
        _authorityEnd = authorityEnd;
        _collectionEnd = collectionEnd;
    }

    /// <summary>Creates an AT URI from a string value with validation.</summary>
    /// <param name="value">The AT URI string.</param>
    /// <returns>A validated AT URI.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid AT URI.</exception>
    public static AtUri Parse(string value) =>
        TryParse(value, out var uri) ? uri : throw IIdentifier<AtUri>.InvalidValue(value, "AT URI");

    /// <summary>Attempts to create an AT URI from a string value without throwing.</summary>
    /// <param name="value">The AT URI string.</param>
    /// <param name="atUri">The parsed AT URI on success.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid AT URI.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out AtUri? atUri)
    {
        atUri = null;
        return value is not null && TryCreate(value, value, out atUri);
    }

    static bool IIdentifier<AtUri>.TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out AtUri? result) =>
        TryCreate(span, text, out result);

    static bool IIdentifier<AtUri>.TryCreate(ReadOnlySpan<byte> utf8, [NotNullWhen(true)] out AtUri? result)
    {
        result = TryParseSyntax(utf8, out var authorityEnd, out var collectionEnd)
            ? new AtUri(IdentifierSyntax.ToAsciiString(utf8), authorityEnd, collectionEnd)
            : null;
        return result is not null;
    }

    private static bool TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out AtUri? result)
    {
        result = TryParseSyntax(span, out var authorityEnd, out var collectionEnd)
            ? new AtUri(text ?? span.ToString(), authorityEnd, collectionEnd)
            : null;
        return result is not null;
    }

    // Validates at://AUTHORITY[/COLLECTION[/RKEY]] and finds where the authority and the collection end.
    // Each part's own syntax excludes '/', '?', '#' and whitespace, so splitting on '/' and validating
    // every part also rejects queries, fragments and empty segments.
    private static bool TryParseSyntax<TChar>(ReadOnlySpan<TChar> s, out int authorityEnd, out int collectionEnd)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        authorityEnd = collectionEnd = -1;
        if (s.Length > MaxLength || s.Length < Scheme.Length)
            return false;

        for (var i = 0; i < Scheme.Length; i++)
        {
            if (uint.CreateTruncating(s[i]) != Scheme[i])
                return false;
        }

        var slash = TChar.CreateTruncating('/');
        var next = s[Scheme.Length..].IndexOf(slash);
        authorityEnd = next < 0 ? s.Length : Scheme.Length + next;

        // The authority is a DID or a handle exactly as written: the @ prefix Handle.Parse tolerates in
        // user input is not a handle character.
        var authority = s[Scheme.Length..authorityEnd];
        if (!(StartsWithDid(authority) ? IdentifierSyntax.IsDid(authority) : IdentifierSyntax.IsHandle(authority, out _)))
            return false;

        if (next < 0)
            return true;

        next = s[(authorityEnd + 1)..].IndexOf(slash);
        collectionEnd = next < 0 ? s.Length : authorityEnd + 1 + next;
        return IdentifierSyntax.IsNsid(s[(authorityEnd + 1)..collectionEnd])
            && (next < 0 || IdentifierSyntax.IsRecordKey(s[(collectionEnd + 1)..]));
    }

    private static bool StartsWithDid<TChar>(ReadOnlySpan<TChar> s)
        where TChar : unmanaged, IBinaryInteger<TChar>
        => s.Length >= 4
            && uint.CreateTruncating(s[0]) == 'd' && uint.CreateTruncating(s[1]) == 'i'
            && uint.CreateTruncating(s[2]) == 'd' && uint.CreateTruncating(s[3]) == ':';

    /// <summary>Creates a new AT URI from components.</summary>
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

        var authorityEnd = Scheme.Length + repo.Value.Length;
        return new AtUri(value, authorityEnd, collection is null ? -1 : authorityEnd + 1 + collection.Value.Length)
        {
            _parts = new() { Authority = repo.Value, Repo = repo, Collection = collection, RecordKey = rkey },
        };
    }

    /// <summary>Implicitly converts a <see cref="AtUri"/> to its <see cref="string"/> representation.</summary>
    /// <param name="atUri">The value to convert.</param>
    /// <returns>The converted value, or <see langword="null"/> for a <see langword="null"/> URI.</returns>
    [return: NotNullIfNotNull(nameof(atUri))]
    public static implicit operator string?(AtUri? atUri) => atUri?.Value;

    /// <summary>Explicitly converts a <see cref="string"/> to its <see cref="AtUri"/> representation.</summary>
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

    private sealed class Parts
    {
        public string? Authority;
        public AtIdentifier? Repo;
        public Nsid? Collection;
        public RecordKey? RecordKey;
    }
}
