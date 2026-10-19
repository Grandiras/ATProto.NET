using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using ATProtoNet.Crypto.Jose;

using ATProtoNet.Data;

namespace ATProtoNet.Crypto;

/// <summary>A P-256 or K-256 public key that verifies ECDSA signatures; immutable and safe for concurrent use.</summary>
/// <remarks>
/// A signature is the 64-byte IEEE P1363 <c>r || s</c> over the SHA-256 of the message; DER is refused.
/// See <see href="https://atproto.com/specs/cryptography"/>.
/// </remarks>
public sealed class VerificationKey : IEquatable<VerificationKey>
{
    /// <summary>The length of a signature, <c>r || s</c>.</summary>
    public const int SignatureLength = 64;

    /// <summary>The length of a SEC1 compressed point.</summary>
    public const int CompressedPointLength = 33;

    /// <summary>The length of a SEC1 uncompressed point, <c>0x04 || x || y</c>.</summary>
    public const int UncompressedPointLength = 65;

    // .NET does not document ECDsa as safe for concurrent use, so each verification rents an instance.
    private readonly Stack<ECDsa> _idle = new();

    internal VerificationKey(DidKey didKey) => DidKey = didKey;

    /// <summary>The key's curve.</summary>
    public KeyCurve Curve => DidKey.Curve;

    /// <summary>The key's <c>did:key</c>.</summary>
    public DidKey DidKey { get; }

    /// <summary>Reads a 33-byte SEC1 compressed point.</summary>
    /// <param name="curve">The point's curve.</param>
    /// <param name="point">The compressed point.</param>
    /// <returns>The key.</returns>
    /// <exception cref="FormatException">The bytes are not a compressed point on <paramref name="curve"/>.</exception>
    public static VerificationKey FromCompressedPoint(KeyCurve curve, ReadOnlySpan<byte> point)
    {
        var info = Crypto.Curve.Of(curve);
        return new(DidKey.FromPoint(info, info.Decompress(point)));
    }

    /// <summary>Reads a 65-byte SEC1 uncompressed point.</summary>
    /// <param name="curve">The point's curve.</param>
    /// <param name="point">The uncompressed point, <c>0x04 || x || y</c>.</param>
    /// <returns>The key, the same as from the point's compressed form.</returns>
    /// <exception cref="FormatException">The bytes are not an uncompressed point on <paramref name="curve"/>.</exception>
    public static VerificationKey FromUncompressedPoint(KeyCurve curve, ReadOnlySpan<byte> point)
    {
        if (point is not [0x04, ..] || point.Length != UncompressedPointLength)
            throw new FormatException($"An uncompressed point is {UncompressedPointLength} bytes starting with 0x04.");
        return FromPoint(Crypto.Curve.Of(curve), point[1..].ToArray());
    }

    /// <summary>Reads an EC JSON Web Key.</summary>
    /// <param name="jwk">The key; a private member is ignored.</param>
    /// <returns>The public key.</returns>
    /// <exception cref="FormatException">The key is not a P-256 or secp256k1 EC key whose point is on its curve.</exception>
    /// <remarks>See <see href="https://www.rfc-editor.org/rfc/rfc7518#section-6.2"/> and <see href="https://www.rfc-editor.org/rfc/rfc8812"/>.</remarks>
    public static VerificationKey FromJsonWebKey(JsonWebKey jwk)
    {
        ArgumentNullException.ThrowIfNull(jwk);
        if (jwk.Kty != "EC" || Crypto.Curve.FromJwkName(jwk.Crv) is not { } curve)
            throw new FormatException("The JWK is not a P-256 or secp256k1 EC key.");
        var point = new byte[2 * Crypto.Curve.FieldLength];
        if (!TryDecodeCoordinate(jwk.X, point.AsSpan(0, 32)) || !TryDecodeCoordinate(jwk.Y, point.AsSpan(32)))
            throw new FormatException("The JWK's x and y are not unpadded base64url of 32 bytes each.");
        return FromPoint(curve, point);
    }

    /// <summary>Whether <paramref name="signature"/> is this key's signature over <paramref name="message"/>.</summary>
    /// <param name="message">The signed bytes; they are hashed with SHA-256 here.</param>
    /// <param name="signature">The 64-byte <c>r || s</c>.</param>
    /// <param name="policy">Whether a high-S signature may verify.</param>
    /// <returns>
    /// Whether the signature is exactly 64 bytes, both scalars lie in <c>[1, n − 1]</c>, <c>s ≤ n/2</c> unless
    /// <paramref name="policy"/> accepts high-S, and the ECDSA check passes. Never throws for a malformed signature.
    /// </returns>
    public bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, SignaturePolicy policy = SignaturePolicy.LowSOnly) =>
        VerifyCore(message, signature, policy, managed: !DidKey.CurveInfo.HasPlatformEcdsa);

    /// <summary>The key as a public EC JSON Web Key.</summary>
    /// <returns>The JWK, without <c>d</c>.</returns>
    public JsonWebKey ToJsonWebKey() => new()
    {
        Kty = "EC",
        Crv = DidKey.CurveInfo.JwkName,
        X = Base64Url.EncodeToString(DidKey.Point.AsSpan(0, 32)),
        Y = Base64Url.EncodeToString(DidKey.Point.AsSpan(32)),
    };

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] VerificationKey? other) => other is not null && DidKey == other.DidKey;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as VerificationKey);

    /// <inheritdoc />
    public override int GetHashCode() => DidKey.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => DidKey.Value;

    internal static VerificationKey FromPoint(Curve curve, byte[] point)
    {
        curve.CheckOnCurve(point);
        return new(DidKey.FromPoint(curve, point));
    }

    // Verify through the managed implementation even where the platform has the curve, so tests can cross-check it.
    internal bool VerifyManaged(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, SignaturePolicy policy) =>
        VerifyCore(message, signature, policy, managed: true);

    private bool VerifyCore(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, SignaturePolicy policy, bool managed)
    {
        var curve = DidKey.CurveInfo;
        if (signature.Length != SignatureLength || !curve.InRange(signature[..32]) || !curve.InRange(signature[32..]))
            return false;
        Span<byte> normalized = stackalloc byte[SignatureLength];
        signature.CopyTo(normalized);
        if (!curve.IsLowS(normalized[32..]))
        {
            if (policy != SignaturePolicy.AcceptHighS) return false;
            curve.Negate(normalized[32..]);
        }
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(message, hash);
        if (managed) return ManagedEcdsa.Verify(curve, DidKey.Point, hash, normalized);
        var ecdsa = Rent();
        try
        {
            return ecdsa.VerifyHash(hash, normalized, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        finally
        {
            Return(ecdsa);
        }
    }

    private ECDsa Rent()
    {
        lock (_idle)
            if (_idle.TryPop(out var ecdsa)) return ecdsa;
        var point = DidKey.Point;
        return ECDsa.Create(new ECParameters { Curve = DidKey.CurveInfo.EcCurve, Q = new() { X = point[..32], Y = point[32..] } });
    }

    private void Return(ECDsa ecdsa)
    {
        lock (_idle)
        {
            if (_idle.Count < Environment.ProcessorCount)
            {
                _idle.Push(ecdsa);
                return;
            }
        }
        ecdsa.Dispose();
    }

    private static bool TryDecodeCoordinate(string? text, Span<byte> destination)
    {
        if (text is not { Length: 43 } || !StrictBase64.TryDecode(text.AsSpan(), url: true, allowPadding: false, out var bytes)) return false;
        bytes.CopyTo(destination);
        return true;
    }
}
