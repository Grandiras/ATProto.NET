using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>A namespaced identifier naming a Lexicon, such as <c>app.bsky.feed.post</c>.</summary>
/// <remarks>
/// NSIDs are not case-normalized: equality is ordinal on the text as written, because Lexicon resolution and <c>$type</c>
/// matching are ordinal upstream. See <see href="https://atproto.com/specs/nsid"/>.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<Nsid>))]
public sealed class Nsid : IAtIdentifier<Nsid>
{
    /// <summary>The longest NSID, in characters.</summary>
    public const int MaxLength = 317;

    /// <summary>The longest name segment, in characters.</summary>
    public const int MaxNameLength = 63;

    private string? _authority, _name;

    private Nsid(string value) => Value = value;

    /// <inheritdoc />
    public string Value { get; }

    /// <summary>The authority segments, a reversed domain name: <c>app.bsky.feed</c> in <c>app.bsky.feed.post</c>.</summary>
    public string Authority => _authority ??= Value[..Value.LastIndexOf('.')];

    /// <summary>The name segment: <c>post</c> in <c>app.bsky.feed.post</c>.</summary>
    public string Name => _name ??= Value[(Value.LastIndexOf('.') + 1)..];

    internal static Nsid FromValidated(string value) => new(value);

    internal static bool TryRead<TChar>(ReadOnlySpan<TChar> text, string? value, [NotNullWhen(true)] out Nsid? result) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = IdentifierSyntax.IsNsid(text) ? new(value ?? Identifiers.Text(text)) : null;
        return result is not null;
    }

    /// <inheritdoc />
    public static Nsid Parse(string s) => Identifiers.Parse<Nsid>(s);

    /// <inheritdoc />
    public static Nsid Parse(ReadOnlySpan<char> s) => Identifiers.Parse<Nsid>(s);

    /// <inheritdoc />
    public static Nsid Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<Nsid>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out Nsid? result) => TryRead(s.AsSpan(), s, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out Nsid? result) => TryRead(s, null, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out Nsid? result) => TryRead(utf8Text, null, out result);

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] Nsid? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as Nsid);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(Nsid? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The canonical text of <paramref name="value"/>.</summary>
    /// <param name="value">The identifier.</param>
    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator string?(Nsid? value) => value?.Value;

    /// <summary>Whether two identifiers are the same.</summary>
    public static bool operator ==(Nsid? left, Nsid? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two identifiers differ.</summary>
    public static bool operator !=(Nsid? left, Nsid? right) => !(left == right);

    /// <inheritdoc />
    static int IAtIdentifier<Nsid>.MaxLength => MaxLength;
}
