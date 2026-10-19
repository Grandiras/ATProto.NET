using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>A handle, the human-readable domain-name identifier of an account, such as <c>alice.bsky.social</c>.</summary>
/// <remarks>
/// Parsing strips one leading <c>@</c> and lower-cases the handle, so <see cref="Value"/> is always normalized. Reserved and
/// special-use top-level domains are syntactically valid; refusing to resolve them is identity's job.
/// See <see href="https://atproto.com/specs/handle"/>.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<Handle>))]
public sealed class Handle : IAtIdentifier<Handle>
{
    /// <summary>The longest handle, in characters.</summary>
    public const int MaxLength = 253;

    /// <summary>The longest label of a handle, in characters.</summary>
    public const int MaxLabelLength = 63;

    private const string InvalidText = "handle.invalid";

    private Handle(string value) => Value = value;

    /// <summary><c>handle.invalid</c>: what an account is shown as when its handle fails bidirectional verification.</summary>
    public static Handle Invalid { get; } = new(InvalidText);

    /// <inheritdoc />
    public string Value { get; }

    /// <summary>Whether this is <see cref="Invalid"/>.</summary>
    public bool IsInvalid => Value == InvalidText;

    /// <summary>Converts to the identifier that is either a DID or a handle.</summary>
    /// <param name="handle">The handle.</param>
    [return: NotNullIfNotNull(nameof(handle))]
    public static implicit operator AtIdentifier?(Handle? handle) => handle is null ? null : new(handle);

    internal static Handle FromValidated(string value) => new(value.ToLowerInvariant());

    internal static bool TryRead<TChar>(ReadOnlySpan<TChar> text, string? value, [NotNullWhen(true)] out Handle? result) where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = null;
        if (text.Length > 0 && Identifiers.Unit(text[0]) == '@')
        {
            text = text[1..];
            value = null;
        }
        if (!IdentifierSyntax.IsHandle(text, out var hasUpper)) return false;
        value ??= Identifiers.Text(text);
        result = new(hasUpper ? value.ToLowerInvariant() : value);
        return true;
    }

    /// <inheritdoc />
    public static Handle Parse(string s) => Identifiers.Parse<Handle>(s);

    /// <inheritdoc />
    public static Handle Parse(ReadOnlySpan<char> s) => Identifiers.Parse<Handle>(s);

    /// <inheritdoc />
    public static Handle Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<Handle>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out Handle? result) => TryRead(s.AsSpan(), s, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out Handle? result) => TryRead(s, null, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out Handle? result) => TryRead(utf8Text, null, out result);

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] Handle? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as Handle);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(Handle? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The canonical text of <paramref name="value"/>.</summary>
    /// <param name="value">The identifier.</param>
    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator string?(Handle? value) => value?.Value;

    /// <summary>Whether two identifiers are the same.</summary>
    public static bool operator ==(Handle? left, Handle? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two identifiers differ.</summary>
    public static bool operator !=(Handle? left, Handle? right) => !(left == right);

    /// <inheritdoc />
    static int IAtIdentifier<Handle>.MaxLength => MaxLength;
}
