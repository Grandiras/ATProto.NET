using System.Diagnostics.CodeAnalysis;
using ATProtoNet.Identity;

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
/// </remarks>
/// <example>
/// <code>
/// var space = SpaceUri.Parse("at://did:plc:abc123/space/com.atmoboards.forum/default");
/// Console.WriteLine(space.SpaceType); // com.atmoboards.forum
/// </code>
/// </example>
public sealed class SpaceUri : IEquatable<SpaceUri>
{
    /// <summary>The fixed path segment marking an AT-URI as addressing permissioned space data.</summary>
    public const string Marker = "space";

    private const string Scheme = "at://";

    /// <summary>The full URI string.</summary>
    public string Value { get; }

    /// <summary>The space authority: the DID at the root of the space, and the issuer of its credentials.</summary>
    public string Authority { get; }

    /// <summary>The space type: an NSID naming the modality of the space.</summary>
    public string SpaceType { get; }

    /// <summary>
    /// The space key, distinguishing spaces of the same type under the same authority.
    /// Carries the same syntax requirements as a record key.
    /// </summary>
    public string Skey { get; }

    private SpaceUri(string value, string authority, string spaceType, string skey)
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
    /// <exception cref="ArgumentException">Thrown when any component is invalid.</exception>
    public static SpaceUri Create(string authority, string spaceType, string skey)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(spaceType);
        ArgumentNullException.ThrowIfNull(skey);

        if (!Did.TryParse(authority, out _))
            throw new ArgumentException($"Space URI authority must be a DID: '{authority}'.", nameof(authority));
        if (!Nsid.TryParse(spaceType, out _))
            throw new ArgumentException($"Invalid space type NSID: '{spaceType}'.", nameof(spaceType));
        if (!IsValidSkey(skey))
            throw new ArgumentException($"Invalid space key: '{skey}'.", nameof(skey));

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
    public static SpaceUri Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryParse(value, out var uri)
            ? uri
            : throw new ArgumentException($"Invalid space URI: '{value}'.", nameof(value));
    }

    /// <summary>
    /// Attempts to parse a space URI, returning <see langword="false"/> rather than throwing.
    /// </summary>
    /// <param name="value">The URI string.</param>
    /// <param name="spaceUri">The parsed URI on success.</param>
    public static bool TryParse(string? value, [NotNullWhen(true)] out SpaceUri? spaceUri)
    {
        spaceUri = null;

        if (!TrySplit(value, out var authority, out var spaceType, out var skey, out var rest))
            return false;
        if (rest is not null)
            return false;

        spaceUri = new SpaceUri(value!, authority, spaceType, skey);
        return true;
    }

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
    public SpaceRecordUri Record(string author, string collection, string rkey) =>
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
    public override bool Equals(object? obj) => obj is SpaceUri other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>Implicitly converts a space URI to its string form.</summary>
    /// <param name="spaceUri">The space URI.</param>
    public static implicit operator string(SpaceUri spaceUri) => spaceUri.Value;

    /// <summary>Compares two space URIs for equality.</summary>
    /// <param name="left">The first URI.</param>
    /// <param name="right">The second URI.</param>
    public static bool operator ==(SpaceUri? left, SpaceUri? right) => Equals(left, right);

    /// <summary>Compares two space URIs for inequality.</summary>
    /// <param name="left">The first URI.</param>
    /// <param name="right">The second URI.</param>
    public static bool operator !=(SpaceUri? left, SpaceUri? right) => !Equals(left, right);

    // A space key carries the same syntax requirements as a record key.
    internal static bool IsValidSkey(string? value) =>
        value is not null && value.Length <= 512 && RecordKey.TryParse(value, out _);

    /// <summary>
    /// Splits a space URI into its leading three components plus whatever follows, without
    /// validating the trailing part. Shared by both URI types so the space-ref grammar is
    /// stated once.
    /// </summary>
    internal static bool TrySplit(
        string? value,
        [NotNullWhen(true)] out string authority,
        [NotNullWhen(true)] out string spaceType,
        [NotNullWhen(true)] out string skey,
        out string? rest)
    {
        authority = spaceType = skey = string.Empty;
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

        if (!Did.TryParse(segments[0], out _))
            return false;
        if (!Nsid.TryParse(segments[2], out _))
            return false;
        if (!IsValidSkey(segments[3]))
            return false;

        authority = segments[0];
        spaceType = segments[2];
        skey = segments[3];

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
/// </remarks>
public sealed class SpaceRecordUri : IEquatable<SpaceRecordUri>
{
    /// <summary>The full URI string.</summary>
    public string Value { get; }

    /// <summary>The space this record lives in.</summary>
    public SpaceUri Space { get; }

    /// <summary>The DID of the account that authored the record.</summary>
    public string Author { get; }

    /// <summary>The record collection NSID.</summary>
    public string Collection { get; }

    /// <summary>The record key.</summary>
    public string Rkey { get; }

    private SpaceRecordUri(string value, SpaceUri space, string author, string collection, string rkey)
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
    /// <exception cref="ArgumentException">Thrown when any component is invalid.</exception>
    public static SpaceRecordUri Create(SpaceUri space, string author, string collection, string rkey)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(rkey);

        if (!Did.TryParse(author, out _))
            throw new ArgumentException($"Space record URI author must be a DID: '{author}'.", nameof(author));
        if (!Nsid.TryParse(collection, out _))
            throw new ArgumentException($"Invalid collection NSID: '{collection}'.", nameof(collection));
        if (!RecordKey.TryParse(rkey, out _))
            throw new ArgumentException($"Invalid record key: '{rkey}'.", nameof(rkey));

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
    public static SpaceRecordUri Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryParse(value, out var uri)
            ? uri
            : throw new ArgumentException($"Invalid space record URI: '{value}'.", nameof(value));
    }

    /// <summary>
    /// Attempts to parse a space record URI, returning <see langword="false"/> rather than throwing.
    /// </summary>
    /// <param name="value">The URI string.</param>
    /// <param name="recordUri">The parsed URI on success.</param>
    public static bool TryParse(string? value, [NotNullWhen(true)] out SpaceRecordUri? recordUri)
    {
        recordUri = null;

        if (!SpaceUri.TrySplit(value, out var authority, out var spaceType, out var skey, out var rest))
            return false;
        if (rest is null)
            return false;

        var tail = rest.Split('/');
        if (tail.Length != 3)
            return false;
        if (!Did.TryParse(tail[0], out _))
            return false;
        if (!Nsid.TryParse(tail[1], out _))
            return false;
        if (!RecordKey.TryParse(tail[2], out _))
            return false;

        var space = SpaceUri.Create(authority, spaceType, skey);
        recordUri = new SpaceRecordUri(value!, space, tail[0], tail[1], tail[2]);
        return true;
    }

    /// <summary>
    /// The record's path within its repo, <c>{collection}/{rkey}</c> — the key side of a
    /// permissioned repo's key/value mapping, and the prefix of its set-hash element.
    /// </summary>
    public string Path => $"{Collection}/{Rkey}";

    /// <inheritdoc/>
    public bool Equals(SpaceRecordUri? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SpaceRecordUri other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>Implicitly converts a space record URI to its string form.</summary>
    /// <param name="recordUri">The record URI.</param>
    public static implicit operator string(SpaceRecordUri recordUri) => recordUri.Value;

    /// <summary>Compares two record URIs for equality.</summary>
    /// <param name="left">The first URI.</param>
    /// <param name="right">The second URI.</param>
    public static bool operator ==(SpaceRecordUri? left, SpaceRecordUri? right) => Equals(left, right);

    /// <summary>Compares two record URIs for inequality.</summary>
    /// <param name="left">The first URI.</param>
    /// <param name="right">The second URI.</param>
    public static bool operator !=(SpaceRecordUri? left, SpaceRecordUri? right) => !Equals(left, right);
}
