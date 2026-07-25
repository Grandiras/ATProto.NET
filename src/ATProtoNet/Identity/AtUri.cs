using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Represents an AT URI, the URI scheme for addressing records in the AT Protocol.
/// Format: at://&lt;authority&gt;/&lt;collection&gt;/&lt;rkey&gt;
/// Examples: at://did:plc:xxx/app.bsky.feed.post/3k2la, at://alice.bsky.social/app.bsky.actor.profile/self
/// </summary>
[JsonConverter(typeof(AtUriJsonConverter))]
public sealed partial class AtUri : IEquatable<AtUri>
{
    [GeneratedRegex(@"^at://([^/]+)(/([^/]+)(/([^/]+))?)?$", RegexOptions.Compiled)]
    private static partial Regex AtUriPattern();

    /// <summary>
    /// The full AT URI string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The authority part (DID or handle).
    /// </summary>
    public string Authority { get; }

    /// <summary>
    /// The collection (NSID), if present.
    /// </summary>
    public string? Collection { get; }

    /// <summary>
    /// The record key, if present.
    /// </summary>
    public string? RecordKey { get; }

    /// <summary>
    /// The authority parsed as an AtIdentifier.
    /// </summary>
    public AtIdentifier Repo => AtIdentifier.Parse(Authority);

    private AtUri(string value, string authority, string? collection, string? recordKey)
    {
        Value = value;
        Authority = authority;
        Collection = collection;
        RecordKey = recordKey;
    }

    /// <summary>
    /// Creates an AT URI from a string value with validation.
    /// </summary>
    public static AtUri Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > 8192)
            throw new ArgumentException("AT URI must not exceed 8 KBytes", nameof(value));

        var match = AtUriPattern().Match(value);
        if (!match.Success)
            throw new ArgumentException($"Invalid AT URI format: '{value}'", nameof(value));

        var authority = match.Groups[1].Value;
        var collection = match.Groups[3].Success ? match.Groups[3].Value : null;
        var rkey = match.Groups[5].Success ? match.Groups[5].Value : null;

        return new AtUri(value, authority, collection, rkey);
    }

    /// <summary>
    /// Attempts to create an AT URI from a string value without throwing.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out AtUri? atUri)
    {
        atUri = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8192)
            return false;

        var match = AtUriPattern().Match(value);
        if (!match.Success)
            return false;

        var authority = match.Groups[1].Value;
        var collection = match.Groups[3].Success ? match.Groups[3].Value : null;
        var rkey = match.Groups[5].Success ? match.Groups[5].Value : null;

        atUri = new AtUri(value, authority, collection, rkey);
        return true;
    }

    /// <summary>
    /// Creates a new AT URI from components.
    /// </summary>
    public static AtUri Create(AtIdentifier repo, string? collection = null, string? rkey = null)
    {
        var uri = $"at://{repo.Value}";
        if (collection is not null)
        {
            uri += $"/{collection}";
            if (rkey is not null)
                uri += $"/{rkey}";
        }
        return Parse(uri);
    }

    /// <summary>
    /// Creates an AT URI without validation.
    /// </summary>
    internal static AtUri UnsafeCreate(string value, string authority, string? collection, string? rkey) =>
        new(value, authority, collection, rkey);

    /// <summary>
    /// Implicitly converts a <see cref="AtUri"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="atUri">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static implicit operator string(AtUri atUri) => atUri.Value;

    /// <summary>
    /// Determines whether this instance and another <see cref="AtUri"/> represent the same value.
    /// </summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(AtUri? other) => other is not null && Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AtUri other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Determines whether two <see cref="AtUri"/> instances are equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(AtUri? left, AtUri? right) => Equals(left, right);

    /// <summary>Determines whether two <see cref="AtUri"/> instances are not equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(AtUri? left, AtUri? right) => !Equals(left, right);
}
