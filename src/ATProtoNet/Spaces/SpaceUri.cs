using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;

namespace ATProtoNet.Spaces;

/// <summary>
/// A reference to a permissioned space: <c>at://{authority}/space/{spaceType}/{skey}</c>.
/// </summary>
/// <remarks>
/// <para>Permissioned data reuses the <c>at://</c> scheme rather than defining its own. The
/// literal <c>space</c> marker sits where a collection NSID appears in a public AT-URI, and the
/// two can never be confused: a collection NSID always contains at least two dots, and
/// <c>space</c> contains none.</para>
/// <para>A space is identified by three values — the <see cref="Authority"/> DID at the root of
/// the space, the <see cref="SpaceType"/> NSID naming its modality, and the
/// <see cref="Skey"/> distinguishing spaces of the same type under the same authority. Unlike a
/// public AT-URI, neither the authority nor a record's author may be a handle: a space's
/// identity and membership are keyed on DIDs.</para>
/// <para>This is the space-ref form only. Use <see cref="SpaceRecordUri"/> for a URI naming a
/// record within a space.</para>
/// <para>Equality and ordering are ordinal on <see cref="Value"/>.</para>
/// </remarks>
/// <example>
/// <code>
/// var space = SpaceUri.Parse("at://did:plc:abc123/space/com.atmoboards.forum/default");
/// Console.WriteLine(space.SpaceType); // com.atmoboards.forum
/// </code>
/// </example>
[JsonConverter(typeof(IdentifierJsonConverter<SpaceUri>))]
public sealed record SpaceUri : IIdentifier<SpaceUri>
{
    /// <summary>The fixed path segment marking an AT-URI as addressing permissioned space data.</summary>
    public const string Marker = "space";

    private const string Scheme = "at://";

    /// <summary>The full URI string.</summary>
    public string Value { get; }

    /// <summary>The space authority: the DID at the root of the space, and the issuer of its credentials.</summary>
    public Did Authority { get; }

    /// <summary>The space type: an NSID naming the modality of the space.</summary>
    public Nsid SpaceType { get; }

    /// <summary>
    /// The space key, distinguishing spaces of the same type under the same authority.
    /// Carries the same syntax requirements as a record key.
    /// </summary>
    public RecordKey Skey { get; }

    private SpaceUri(string value, Did authority, Nsid spaceType, RecordKey skey)
    {
        Value = value;
        Authority = authority;
        SpaceType = spaceType;
        Skey = skey;
    }

    /// <summary>
    /// Builds a space URI from its three components.
    /// </summary>
    /// <param name="authority">The space authority DID.</param>
    /// <param name="spaceType">The space type NSID.</param>
    /// <param name="skey">The space key.</param>
    public static SpaceUri Create(Did authority, Nsid spaceType, RecordKey skey)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(spaceType);
        ArgumentNullException.ThrowIfNull(skey);

        return new SpaceUri($"{Scheme}{authority}/{Marker}/{spaceType}/{skey}", authority, spaceType, skey);
    }

    /// <summary>
    /// Parses a space URI.
    /// </summary>
    /// <param name="value">The URI string.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is not a valid space URI. A URI naming a record
    /// within a space is rejected — parse it with <see cref="SpaceRecordUri"/>.
    /// </exception>
    public static SpaceUri Parse(string value) =>
        TryParse(value, out var uri) ? uri : throw IIdentifier<SpaceUri>.InvalidValue(value, "space URI");

    /// <summary>
    /// Attempts to parse a space URI, returning <see langword="false"/> rather than throwing.
    /// </summary>
    /// <param name="value">The URI string.</param>
    /// <param name="spaceUri">The parsed URI on success.</param>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out SpaceUri? spaceUri)
    {
        spaceUri = null;

        if (!TrySplit(value, out var authority, out var spaceType, out var skey, out var rest))
            return false;
        if (rest is not null)
            return false;

        spaceUri = new SpaceUri(value, authority, spaceType, skey);
        return true;
    }

    static bool IIdentifier<SpaceUri>.TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out SpaceUri? result) =>
        TryParse(text ?? span.ToString(), out result);

    /// <summary>
    /// Whether a string carries the <c>space</c> marker, and so addresses permissioned data
    /// rather than a public repository record. Checks only for the marker — use
    /// <see cref="TryParse"/> to validate the URI itself.
    /// </summary>
    /// <param name="value">The candidate URI string.</param>
    public static bool IsSpaceUri([NotNullWhen(true)] string? value)
    {
        if (value is null || !value.StartsWith(Scheme, StringComparison.Ordinal))
            return false;

        var pathStart = value.IndexOf('/', Scheme.Length);
        if (pathStart < 0)
            return false;

        var rest = value.AsSpan(pathStart + 1);
        return rest.SequenceEqual(Marker) || rest.StartsWith($"{Marker}/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the URI of a record within this space.
    /// </summary>
    /// <param name="author">The DID of the record's author.</param>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    public SpaceRecordUri Record(Did author, Nsid collection, RecordKey rkey) =>
        SpaceRecordUri.Create(this, author, collection, rkey);

    /// <summary>
    /// The service identifier a delegation token or client attestation names as its audience
    /// when addressing this space's authority as the space host.
    /// </summary>
    /// <remarks>
    /// This is the <em>audience</em>, not necessarily where requests are sent. An authority that
    /// publishes no <c>#atproto_space_host</c> service entry is still reached at its
    /// <c>#atproto_pds</c> endpoint — see <see cref="SpaceAuthority"/>.
    /// </remarks>
    public string HostAudience => SpaceAuthority.HostAudience(Authority);

    /// <inheritdoc/>
    public bool Equals(SpaceUri? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc/>
    public int CompareTo(SpaceUri? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>Implicitly converts a space URI to its string form.</summary>
    /// <param name="spaceUri">The space URI.</param>
    /// <returns>The URI string, or <see langword="null"/> for a <see langword="null"/> space URI.</returns>
    [return: NotNullIfNotNull(nameof(spaceUri))]
    public static implicit operator string?(SpaceUri? spaceUri) => spaceUri?.Value;

    /// <summary>Explicitly converts a string to a space URI.</summary>
    /// <param name="value">The URI string.</param>
    /// <returns>The parsed space URI.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is not a valid space URI.</exception>
    public static explicit operator SpaceUri(string value) => Parse(value);

    /// <summary>
    /// Splits a space URI into its leading three components plus whatever follows, without
    /// validating the trailing part. Shared by both URI types so the space-ref grammar is
    /// stated once.
    /// </summary>
    internal static bool TrySplit(
        [NotNullWhen(true)] string? value,
        [NotNullWhen(true)] out Did? authority,
        [NotNullWhen(true)] out Nsid? spaceType,
        [NotNullWhen(true)] out RecordKey? skey,
        out string? rest)
    {
        authority = null;
        spaceType = null;
        skey = null;
        rest = null;

        if (value is null || value.Length > 8192)
            return false;
        if (!value.StartsWith(Scheme, StringComparison.Ordinal))
            return false;
        // A space URI carries no query or fragment; both would be ambiguous against an rkey.
        if (value.AsSpan().ContainsAny('?', '#', ' '))
            return false;

        var segments = value[Scheme.Length..].Split('/');
        if (segments.Length < 4)
            return false;
        if (!string.Equals(segments[1], Marker, StringComparison.Ordinal))
            return false;

        // A space key carries the same syntax requirements as a record key.
        if (!Did.TryParse(segments[0], out authority) ||
            !Nsid.TryParse(segments[2], out spaceType) ||
            !RecordKey.TryParse(segments[3], out skey))
        {
            authority = null;
            spaceType = null;
            skey = null;
            return false;
        }

        if (segments.Length > 4)
            rest = string.Join('/', segments[4..]);

        return true;
    }
}

/// <summary>
/// The URI of a record within a permissioned space:
/// <c>at://{authority}/space/{spaceType}/{skey}/{author}/{collection}/{rkey}</c>.
/// </summary>
/// <remarks>
/// Authority in permissioned data splits in two. The URI's authority is the space authority —
/// the DID that gates access — while the record's authority remains the
/// <see cref="Author"/> DID that wrote and signed it. That is the one structural difference
/// from a public AT-URI, where the two are the same DID.
/// <para>Equality and ordering are ordinal on <see cref="Value"/>.</para>
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<SpaceRecordUri>))]
public sealed record SpaceRecordUri : IIdentifier<SpaceRecordUri>
{
    /// <summary>The full URI string.</summary>
    public string Value { get; }

    /// <summary>The space this record lives in.</summary>
    public SpaceUri Space { get; }

    /// <summary>The DID of the account that authored the record.</summary>
    public Did Author { get; }

    /// <summary>The record collection NSID.</summary>
    public Nsid Collection { get; }

    /// <summary>The record key.</summary>
    public RecordKey Rkey { get; }

    private SpaceRecordUri(string value, SpaceUri space, Did author, Nsid collection, RecordKey rkey)
    {
        Value = value;
        Space = space;
        Author = author;
        Collection = collection;
        Rkey = rkey;
    }

    /// <summary>
    /// Builds a record URI from a space and the record's location within it.
    /// </summary>
    /// <param name="space">The space the record lives in.</param>
    /// <param name="author">The DID of the record's author.</param>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    public static SpaceRecordUri Create(SpaceUri space, Did author, Nsid collection, RecordKey rkey)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(rkey);

        return new SpaceRecordUri($"{space.Value}/{author}/{collection}/{rkey}", space, author, collection, rkey);
    }

    /// <summary>
    /// Parses a space record URI.
    /// </summary>
    /// <param name="value">The URI string.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is not a valid space record URI. A bare space ref is
    /// rejected — parse it with <see cref="SpaceUri"/>.
    /// </exception>
    public static SpaceRecordUri Parse(string value) =>
        TryParse(value, out var uri) ? uri : throw IIdentifier<SpaceRecordUri>.InvalidValue(value, "space record URI");

    /// <summary>
    /// Attempts to parse a space record URI, returning <see langword="false"/> rather than throwing.
    /// </summary>
    /// <param name="value">The URI string.</param>
    /// <param name="recordUri">The parsed URI on success.</param>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out SpaceRecordUri? recordUri)
    {
        recordUri = null;

        if (!SpaceUri.TrySplit(value, out var authority, out var spaceType, out var skey, out var rest))
            return false;
        if (rest is null)
            return false;

        var tail = rest.Split('/');
        if (tail.Length != 3 ||
            !Did.TryParse(tail[0], out var author) ||
            !Nsid.TryParse(tail[1], out var collection) ||
            !RecordKey.TryParse(tail[2], out var rkey))
        {
            return false;
        }

        var space = SpaceUri.Create(authority, spaceType, skey);
        recordUri = new SpaceRecordUri(value, space, author, collection, rkey);
        return true;
    }

    static bool IIdentifier<SpaceRecordUri>.TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out SpaceRecordUri? result) =>
        TryParse(text ?? span.ToString(), out result);

    /// <summary>
    /// The record's path within its repo, <c>{collection}/{rkey}</c> — the key side of a
    /// permissioned repo's key/value mapping, and the prefix of its set-hash element.
    /// </summary>
    public string Path => $"{Collection}/{Rkey}";

    /// <inheritdoc/>
    public bool Equals(SpaceRecordUri? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc/>
    public int CompareTo(SpaceRecordUri? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>Implicitly converts a space record URI to its string form.</summary>
    /// <param name="recordUri">The record URI.</param>
    /// <returns>The URI string, or <see langword="null"/> for a <see langword="null"/> record URI.</returns>
    [return: NotNullIfNotNull(nameof(recordUri))]
    public static implicit operator string?(SpaceRecordUri? recordUri) => recordUri?.Value;

    /// <summary>Explicitly converts a string to a space record URI.</summary>
    /// <param name="value">The URI string.</param>
    /// <returns>The parsed space record URI.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is not a valid space record URI.</exception>
    public static explicit operator SpaceRecordUri(string value) => Parse(value);
}
