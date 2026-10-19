using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using ATProtoNet.Data;

namespace ATProtoNet.Crypto;

/// <summary>A <c>did:key</c> naming a P-256 or K-256 public key: <c>did:key:</c> followed by its multikey.</summary>
/// <remarks>
/// The multikey is the multibase <c>z</c> (base58btc) encoding of the curve's multicodec varint (<c>0x1200</c>
/// for P-256, <c>0xe7</c> for K-256) and the 33-byte compressed point, which is checked to lie on the curve.
/// See <see href="https://atproto.com/specs/cryptography"/> and <see href="https://w3c-ccg.github.io/did-method-key/"/>.
/// </remarks>
public sealed class DidKey : IAtIdentifier<DidKey>
{
    private const string Prefix = "did:key:";
    private const int Longest = 57;
    private const int MultikeyBytes = 2 + VerificationKey.CompressedPointLength;

    private readonly byte[] _compressed;

    private DidKey(Curve curve, byte[] compressed, byte[] point, string? multikey)
    {
        (CurveInfo, _compressed, Point) = (curve, compressed, point);
        Multikey = multikey ?? "z" + Base58Btc.Encode([.. curve.MulticodecPrefix, .. compressed]);
        Value = Prefix + Multikey;
    }

    /// <inheritdoc />
    public static int MaxLength => Longest;

    /// <summary>The canonical text, <c>did:key:z…</c>.</summary>
    public string Value { get; }

    /// <summary>The multikey, <c>z…</c>, as a DID document's <c>publicKeyMultibase</c> carries it.</summary>
    public string Multikey { get; }

    /// <summary>The key's curve.</summary>
    public KeyCurve Curve => CurveInfo.Id;

    /// <summary>The 33-byte SEC1 compressed public point.</summary>
    public ReadOnlySpan<byte> CompressedPublicKey => _compressed;

    internal Curve CurveInfo { get; }

    // The uncompressed point x || y.
    internal byte[] Point { get; }

    /// <summary>Parses a multikey (<c>z…</c>).</summary>
    /// <param name="multikey">The multikey.</param>
    /// <returns>The <c>did:key</c> of the key.</returns>
    /// <exception cref="FormatException">The multikey is malformed, of another key type or not a point on its curve.</exception>
    public static DidKey FromMultikey(string multikey)
    {
        ArgumentNullException.ThrowIfNull(multikey);
        return Read(multikey, out var error) ?? throw new FormatException(error);
    }

    /// <summary>A verification key for this public key.</summary>
    /// <returns>The key.</returns>
    public VerificationKey ToVerificationKey() => new(this);

    internal static DidKey FromPoint(Curve curve, byte[] point) => new(curve, Crypto.Curve.Compress(point), point, null);

    /// <inheritdoc />
    public static DidKey Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.StartsWith(Prefix, StringComparison.Ordinal) && s.Length <= MaxLength
            ? Read(s[Prefix.Length..], out var error) ?? throw new FormatException(error)
            : throw new FormatException("The value is not a did:key of at most 57 characters.");
    }

    /// <inheritdoc />
    public static DidKey Parse(ReadOnlySpan<char> s) => Identifiers.Parse<DidKey>(s);

    /// <inheritdoc />
    public static DidKey Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<DidKey>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out DidKey? result)
    {
        result = s is { Length: <= Longest } && s.StartsWith(Prefix, StringComparison.Ordinal) ? Read(s[Prefix.Length..], out _) : null;
        return result is not null;
    }

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out DidKey? result)
    {
        result = null;
        return s.Length <= MaxLength && TryParse(s.ToString(), out result);
    }

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out DidKey? result)
    {
        result = null;
        // Every valid did:key is ASCII, so a non-ASCII byte fails parsing either way.
        return utf8Text.Length <= MaxLength && Ascii.IsValid(utf8Text) && TryParse(Encoding.ASCII.GetString(utf8Text), out result);
    }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] DidKey? other) => other is not null && Value == other.Value;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as DidKey);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public int CompareTo(DidKey? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The canonical text of <paramref name="key"/>.</summary>
    /// <param name="key">The <c>did:key</c>.</param>
    [return: NotNullIfNotNull(nameof(key))]
    public static implicit operator string?(DidKey? key) => key?.Value;

    /// <summary>Whether two <c>did:key</c>s are the same.</summary>
    public static bool operator ==(DidKey? left, DidKey? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two <c>did:key</c>s differ.</summary>
    public static bool operator !=(DidKey? left, DidKey? right) => !(left == right);

    private static DidKey? Read(string multikey, out string error)
    {
        if (multikey is not ['z', ..])
        {
            error = "A multikey is multibase base58btc: it starts with 'z'.";
            return null;
        }
        // Spare room lets an over-long key decode far enough to be reported as the wrong length.
        Span<byte> bytes = stackalloc byte[MultikeyBytes + 8];
        if (!Base58Btc.TryDecode(multikey.AsSpan(1), bytes, out var length))
        {
            error = "The multikey is not base58btc, or is far too long.";
            return null;
        }
        if (UnsignedVarint.TryRead(bytes[..length], out var codec, out var read) != OperationStatus.Done || Crypto.Curve.FromMulticodec(codec) is not { } curve)
        {
            error = "The multikey is not a P-256 or K-256 public key.";
            return null;
        }
        if (length - read != VerificationKey.CompressedPointLength)
        {
            error = $"A {curve.Id} multikey carries a {VerificationKey.CompressedPointLength}-byte compressed key, not {length - read} bytes.";
            return null;
        }
        try
        {
            var compressed = bytes[read..length].ToArray();
            error = "";
            return new(curve, compressed, curve.Decompress(compressed), multikey);
        }
        catch (FormatException e)
        {
            error = e.Message;
            return null;
        }
    }
}
