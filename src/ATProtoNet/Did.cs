using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>A decentralized identifier, the permanent identifier of an account, such as <c>did:plc:z72i7hdynmk6r22z27h6tvur</c>.</summary>
/// <remarks>
/// Only the generic syntax is checked: method-specific rules (<c>did:plc</c>, <c>did:web</c>) belong to resolution, so a DID of an
/// unsupported method parses. DIDs are case-sensitive and never normalized. See <see href="https://atproto.com/specs/did"/>.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<Did>))]
public sealed class Did : IAtIdentifier<Did>
{
    /// <summary>The shortest DID, in characters.</summary>
    public const int MinLength = 7;

    /// <summary>The longest DID, in characters.</summary>
    public const int MaxLength = 2048;

    private string? _method;

    private Did(string value) => Value = value;

    /// <inheritdoc />
    public string Value { get; }

    /// <summary>The DID method, such as <c>plc</c> or <c>web</c>.</summary>
    public string Method => _method ??= Value[4..Value.IndexOf(':', 4)];

    /// <summary>Everything after the method and its colon.</summary>
    public ReadOnlySpan<char> MethodSpecificId => Value.AsSpan(Value.IndexOf(':', 4) + 1);

    /// <summary>Converts to the identifier that is either a DID or a handle.</summary>
    /// <param name="did">The DID.</param>
    [return: NotNullIfNotNull(nameof(did))]
    public static implicit operator AtIdentifier?(Did? did) => did is null ? null : new(did);

    internal static Did FromValidated(string value) => new(value);

    internal static bool TryRead<TChar>(ReadOnlySpan<TChar> text, string? value, [NotNullWhen(true)] out Did? result) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = IdentifierSyntax.IsDid(text) ? new(value ?? Identifiers.Text(text)) : null;
        return result is not null;
    }

    /// <inheritdoc />
    public static Did Parse(string s) => Identifiers.Parse<Did>(s);

    /// <inheritdoc />
    public static Did Parse(ReadOnlySpan<char> s) => Identifiers.Parse<Did>(s);

    /// <inheritdoc />
    public static Did Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<Did>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out Did? result) => TryRead(s.AsSpan(), s, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out Did? result) => TryRead(s, null, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out Did? result) => TryRead(utf8Text, null, out result);

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] Did? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as Did);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(Did? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The canonical text of <paramref name="value"/>.</summary>
    /// <param name="value">The identifier.</param>
    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator string?(Did? value) => value?.Value;

    /// <summary>Whether two identifiers are the same.</summary>
    public static bool operator ==(Did? left, Did? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two identifiers differ.</summary>
    public static bool operator !=(Did? left, Did? right) => !(left == right);

    /// <inheritdoc />
    static int IAtIdentifier<Did>.MaxLength => MaxLength;
}
