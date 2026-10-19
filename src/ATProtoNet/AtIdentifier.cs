using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>An account identifier that is either a <see cref="ATProtoNet.Did"/> or a <see cref="ATProtoNet.Handle"/>.</summary>
/// <remarks>
/// Text starting with <c>did:</c> is a DID and anything else a handle, normalized as <see cref="ATProtoNet.Handle"/> does.
/// Exactly one of <see cref="Did"/> and <see cref="Handle"/> is set. See <see href="https://atproto.com/specs/lexicon#at-identifier"/>.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<AtIdentifier>))]
public sealed class AtIdentifier : IAtIdentifier<AtIdentifier>
{
    /// <summary>The longest identifier, in characters.</summary>
    public const int MaxLength = Did.MaxLength;

    internal AtIdentifier(Did did) => Did = did;

    internal AtIdentifier(Handle handle) => Handle = handle;

    /// <summary>The DID, when the identifier is one.</summary>
    public Did? Did { get; }

    /// <summary>The handle, when the identifier is one.</summary>
    public Handle? Handle { get; }

    /// <summary>Whether the identifier is a DID.</summary>
    [MemberNotNullWhen(true, nameof(Did))]
    [MemberNotNullWhen(false, nameof(Handle))]
    public bool IsDid => Did is not null;

    /// <summary>Whether the identifier is a handle.</summary>
    [MemberNotNullWhen(true, nameof(Handle))]
    [MemberNotNullWhen(false, nameof(Did))]
    public bool IsHandle => Handle is not null;

    /// <inheritdoc />
    public string Value => Did?.Value ?? Handle!.Value;

    internal static AtIdentifier FromValidated(string value) =>
        value.StartsWith("did:", StringComparison.Ordinal) ? new(ATProtoNet.Did.FromValidated(value)) : new(ATProtoNet.Handle.FromValidated(value));

    internal static bool TryRead<TChar>(ReadOnlySpan<TChar> text, string? value, [NotNullWhen(true)] out AtIdentifier? result) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = null;
        if (IdentifierSyntax.StartsWithDid(text)) result = ATProtoNet.Did.TryRead(text, value, out var did) ? new(did) : null;
        else result = ATProtoNet.Handle.TryRead(text, value, out var handle) ? new(handle) : null;
        return result is not null;
    }

    /// <inheritdoc />
    public static AtIdentifier Parse(string s) => Identifiers.Parse<AtIdentifier>(s);

    /// <inheritdoc />
    public static AtIdentifier Parse(ReadOnlySpan<char> s) => Identifiers.Parse<AtIdentifier>(s);

    /// <inheritdoc />
    public static AtIdentifier Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<AtIdentifier>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out AtIdentifier? result) => TryRead(s.AsSpan(), s, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out AtIdentifier? result) => TryRead(s, null, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out AtIdentifier? result) => TryRead(utf8Text, null, out result);

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] AtIdentifier? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as AtIdentifier);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(AtIdentifier? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The canonical text of <paramref name="value"/>.</summary>
    /// <param name="value">The identifier.</param>
    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator string?(AtIdentifier? value) => value?.Value;

    /// <summary>Whether two identifiers are the same.</summary>
    public static bool operator ==(AtIdentifier? left, AtIdentifier? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two identifiers differ.</summary>
    public static bool operator !=(AtIdentifier? left, AtIdentifier? right) => !(left == right);

    /// <inheritdoc />
    static int IAtIdentifier<AtIdentifier>.MaxLength => MaxLength;
}
