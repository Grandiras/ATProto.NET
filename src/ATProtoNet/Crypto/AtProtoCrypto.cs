using System.Numerics;
using System.Security.Cryptography;

namespace ATProtoNet.Crypto;

/// <summary>
/// AT Protocol cryptographic utilities for key generation, signing, verification,
/// and multikey/did:key encoding.
/// <para>
/// Supports P-256 (NIST secp256r1) and K-256 (secp256k1) as specified by the
/// AT Protocol Cryptography spec: https://atproto.com/specs/cryptography
/// </para>
/// </summary>
public static class AtProtoCrypto
{
    /// <summary>Length of a SEC1 compressed point on either supported curve.</summary>
    private const int CompressedKeyLength = 33;

    /// <summary>Length of an IEEE P1363 (<c>r || s</c>) signature on either supported curve.</summary>
    private const int SignatureLength = 64;

    /// <summary>A did:key multikey: a 2-byte multicodec prefix and a compressed point.</summary>
    private const int MultikeyLength = 2 + CompressedKeyLength;

    // Base58 Bitcoin alphabet
    private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>Base58 digit value of each ASCII character, or -1.</summary>
    private static readonly sbyte[] s_base58Digits = CreateBase58Digits();

    /// <summary>
    /// Generates a new P-256 (NIST secp256r1) key pair for signing.
    /// </summary>
    /// <returns>An <see cref="AtProtoKey"/> wrapping the ECDsa key pair.</returns>
    public static AtProtoKey GenerateP256Key()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new AtProtoKey(ecdsa, KeyCurve.P256);
    }

    /// <summary>
    /// Generates a new K-256 (secp256k1) key pair for signing.
    /// </summary>
    /// <returns>An <see cref="AtProtoKey"/> wrapping the ECDsa key pair.</returns>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown when the platform does not support secp256k1 (macOS without OpenSSL).
    /// </exception>
    public static AtProtoKey GenerateK256Key()
    {
        try
        {
            var ecdsa = ECDsa.Create(CurveInfo.K256.CreateCurve());
            return new AtProtoKey(ecdsa, KeyCurve.K256);
        }
        catch (PlatformNotSupportedException)
        {
            throw new PlatformNotSupportedException(
                "secp256k1 (K-256) is not supported on this platform. " +
                "Linux with OpenSSL 1.1+ is required. macOS and Windows may not support this curve.");
        }
    }

    /// <summary>
    /// Imports a private key from PKCS#8 format.
    /// </summary>
    /// <param name="pkcs8PrivateKey">The PKCS#8-encoded private key bytes.</param>
    /// <param name="curve">The curve the key belongs to.</param>
    /// <returns>An <see cref="AtProtoKey"/> wrapping the imported key pair.</returns>
    public static AtProtoKey ImportPrivateKey(ReadOnlySpan<byte> pkcs8PrivateKey, KeyCurve curve)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(pkcs8PrivateKey, out _);

        // Validate that the actual key curve matches the declared curve
        var actualOid = ecdsa.ExportParameters(false).Curve.Oid?.Value;
        var expectedOid = CurveInfo.For(curve).OidValue;
        if (actualOid != expectedOid)
        {
            ecdsa.Dispose();
            throw new ArgumentException(
                $"Key curve mismatch: expected {curve} (OID {expectedOid}), but key has OID {actualOid}.",
                nameof(curve));
        }

        return new AtProtoKey(ecdsa, curve);
    }

    /// <summary>
    /// Imports a public key from its compressed (SEC1) representation.
    /// </summary>
    /// <param name="compressedPublicKey">33-byte compressed public key (0x02/0x03 prefix).</param>
    /// <param name="curve">The curve the key belongs to.</param>
    /// <returns>An <see cref="AtProtoKey"/> for verification only (no private key).</returns>
    public static AtProtoKey ImportCompressedPublicKey(ReadOnlySpan<byte> compressedPublicKey, KeyCurve curve)
    {
        if (compressedPublicKey.Length != CompressedKeyLength)
            throw new ArgumentException("Compressed public key must be 33 bytes.", nameof(compressedPublicKey));

        return CreatePublicKey(PublicKeyParameters(compressedPublicKey, CurveInfo.For(curve)), curve);
    }

    /// <summary>
    /// Parses a <c>did:key</c> identifier and returns the public key.
    /// </summary>
    /// <param name="didKey">A DID in <c>did:key:z...</c> format.</param>
    /// <returns>The parsed <see cref="AtProtoKey"/> (public key only).</returns>
    /// <exception cref="FormatException">Thrown when the did:key is malformed.</exception>
    public static AtProtoKey FromDidKey(string didKey)
    {
        var parameters = ParseDidKey(didKey, out var curve);
        return CreatePublicKey(parameters, curve);
    }

    /// <summary>
    /// Parses a multikey string (<c>z</c>-prefixed base58btc-encoded multicodec key).
    /// </summary>
    /// <param name="multikey">The multikey string starting with 'z'.</param>
    /// <returns>The parsed <see cref="AtProtoKey"/> (public key only).</returns>
    public static AtProtoKey FromMultikey(string multikey)
    {
        var parameters = ParseMultikey(multikey, out var curve);
        return CreatePublicKey(parameters, curve);
    }

    /// <summary>
    /// Parses a <c>did:key</c> to its curve and decompressed public point. The point is
    /// validated to lie on the curve but not yet imported into a platform key.
    /// </summary>
    /// <exception cref="FormatException">Thrown when the did:key is malformed.</exception>
    internal static ECParameters ParseDidKey(string didKey, out KeyCurve curve)
    {
        ArgumentNullException.ThrowIfNull(didKey);
        if (!didKey.StartsWith("did:key:z", StringComparison.Ordinal))
            throw new FormatException("did:key must start with 'did:key:z'.");

        return ParseMultikey(didKey["did:key:".Length..], out curve);
    }

    private static ECParameters ParseMultikey(string multikey, out KeyCurve curve)
    {
        // Both supported keys are exactly MultikeyLength bytes; the spare room lets a slightly
        // over-long value decode far enough to be reported as the wrong length.
        Span<byte> bytes = stackalloc byte[MultikeyLength + 8];
        var length = Base58Decode(MultibasePayload(multikey), bytes);

        if (length < 2)
            throw new FormatException("Multikey too short.");

        var info = CurveInfo.FromMulticodec(bytes[0], bytes[1])
            ?? throw new FormatException($"Unknown multicodec prefix: 0x{bytes[0]:X2} 0x{bytes[1]:X2}");

        if (length != MultikeyLength)
        {
            throw new FormatException(
                $"A {info.Curve} multikey must carry a {CompressedKeyLength}-byte compressed key, got {length - 2} bytes.");
        }

        curve = info.Curve;
        return PublicKeyParameters(bytes[2..length], info);
    }

    /// <summary>Imports a public point as a verification-only key.</summary>
    internal static AtProtoKey CreatePublicKey(ECParameters parameters, KeyCurve curve)
        => new(ECDsa.Create(parameters), curve);

    private static ECParameters PublicKeyParameters(ReadOnlySpan<byte> compressedPublicKey, CurveInfo curve)
        => new()
        {
            Curve = curve.CreateCurve(),
            Q = DecompressPoint(compressedPublicKey, curve),
        };

    /// <summary>
    /// Formats a raw public key as a <c>did:key</c> identifier.
    /// </summary>
    /// <param name="publicKey">
    /// The public key in SEC1 form — either 33-byte compressed (<c>0x02</c>/<c>0x03</c> prefix)
    /// or 65-byte uncompressed (<c>0x04 || X || Y</c>), which is compressed first.
    /// </param>
    /// <param name="curve">The curve the key belongs to; selects the multicodec prefix.</param>
    /// <returns>The key as a <c>did:key:z...</c> string.</returns>
    /// <exception cref="FormatException">Thrown when the point encoding is not recognized.</exception>
    /// <remarks>
    /// This encodes only; it does not check that the point lies on <paramref name="curve"/>.
    /// Use <see cref="ImportCompressedPublicKey"/> (or <see cref="FromDidKey"/> on the result)
    /// when the key material comes from an untrusted source and must be validated.
    /// </remarks>
    public static string FormatDidKey(ReadOnlySpan<byte> publicKey, KeyCurve curve)
        => $"did:key:{ToMultikey(CompressPublicKey(publicKey), curve)}";

    /// <summary>
    /// Compresses a public key point to its 33-byte SEC1 compressed form.
    /// </summary>
    /// <param name="publicKey">
    /// A 65-byte uncompressed point (<c>0x04 || X || Y</c>), or an already-compressed 33-byte
    /// point, which is returned as-is.
    /// </param>
    /// <returns>The 33-byte compressed point.</returns>
    /// <exception cref="FormatException">Thrown when the point encoding is not recognized.</exception>
    /// <remarks>
    /// Both curves this SDK supports have 32-byte coordinates, so compression is the same
    /// operation for either: keep X, and record the parity of Y in the prefix byte.
    /// </remarks>
    public static byte[] CompressPublicKey(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length == CompressedKeyLength && publicKey[0] is 0x02 or 0x03)
            return publicKey.ToArray();

        if (publicKey.Length != 65 || publicKey[0] != 0x04)
        {
            throw new FormatException(
                $"Expected a 33-byte compressed or 65-byte uncompressed EC point, got {publicKey.Length} bytes" +
                (publicKey.Length > 0 ? $" with prefix 0x{publicKey[0]:X2}." : "."));
        }

        return CompressPoint(publicKey[1..33], yIsOdd: (publicKey[64] & 1) == 1);
    }

    /// <summary>SEC1 point compression: X, with the parity of Y in the prefix byte.</summary>
    internal static byte[] CompressPoint(ReadOnlySpan<byte> x, bool yIsOdd)
    {
        var compressed = new byte[CompressedKeyLength];
        compressed[0] = yIsOdd ? (byte)0x03 : (byte)0x02;
        x.CopyTo(compressed.AsSpan(1));
        return compressed;
    }

    /// <summary>
    /// Decodes a multibase string to its raw bytes.
    /// </summary>
    /// <remarks>
    /// Only <c>z</c> (base58btc) is supported — the encoding every AT Protocol DID document
    /// uses for <c>publicKeyMultibase</c>, in both the <c>Multikey</c> and the legacy
    /// <c>Ecdsa...VerificationKey2019</c> forms.
    /// </remarks>
    internal static byte[] MultibaseToBytes(string multibase)
        => Base58DecodeToArray(MultibasePayload(multibase));

    /// <summary>The base58btc payload of a <c>z</c>-prefixed multibase string.</summary>
    private static ReadOnlySpan<char> MultibasePayload(string multibase)
    {
        if (string.IsNullOrEmpty(multibase))
            throw new FormatException("Multibase value is empty.");

        if (multibase[0] != 'z')
            throw new FormatException($"Unsupported multibase prefix '{multibase[0]}'; expected 'z' (base58btc).");

        return multibase.AsSpan(1);
    }

    /// <summary>
    /// Verifies a signature against message bytes using a <c>did:key</c>.
    /// </summary>
    /// <param name="didKey">The signer's did:key.</param>
    /// <param name="message">The raw message bytes that were signed. Do NOT pre-hash; this method hashes with SHA-256 internally.</param>
    /// <param name="signature">The signature bytes (IEEE P1363 format — r || s concatenation).</param>
    /// <returns><c>true</c> if the signature is valid.</returns>
    /// <remarks>
    /// Parsed keys are cached, keyed by the did:key string and bounded in number, so repeated
    /// verifications against the same signer skip parsing and key import.
    /// </remarks>
    public static bool VerifySignature(string didKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
        => DidKeyCache.Shared.Verify(didKey, message, signature);

    /// <summary>
    /// Encodes a 33-byte compressed public key as a base58btc multikey string.
    /// </summary>
    internal static string ToMultikey(ReadOnlySpan<byte> compressedPublicKey, KeyCurve curve)
    {
        var info = CurveInfo.For(curve);
        var encoded = new byte[2 + compressedPublicKey.Length];
        encoded[0] = info.Multicodec0;
        encoded[1] = info.Multicodec1;
        compressedPublicKey.CopyTo(encoded.AsSpan(2));
        return "z" + Base58Encode(encoded);
    }

    /// <summary>
    /// Decompresses an EC point from compressed SEC1 form.
    /// Computes Y from X using the curve equation y² = x³ + ax + b (mod p).
    /// </summary>
    private static ECPoint DecompressPoint(ReadOnlySpan<byte> compressed, CurveInfo curve)
    {
        if (compressed.Length != CompressedKeyLength)
            throw new FormatException("Compressed point must be 33 bytes.");

        var prefix = compressed[0];
        if (prefix is not (0x02 or 0x03))
            throw new FormatException($"Invalid compressed point prefix: 0x{prefix:X2}");

        var p = curve.P;
        var x = new BigInteger(compressed[1..], isUnsigned: true, isBigEndian: true);
        if (x >= p)
            throw new FormatException("X coordinate out of range for curve.");

        var ySquared = (BigInteger.ModPow(x, 3, p) + (curve.A * x) + curve.B) % p;

        // Both curves have p ≡ 3 (mod 4), where a square root is ySquared^((p + 1) / 4).
        var y = BigInteger.ModPow(ySquared, curve.SqrtExponent, p);
        if (y * y % p != ySquared)
            throw new FormatException("Invalid compressed point: no valid Y coordinate.");

        if (y.IsEven == (prefix == 0x03))
            y = p - y;

        var yBytes = new byte[32];
        y.TryWriteBytes(yBytes.AsSpan(32 - y.GetByteCount(isUnsigned: true)), out _, isUnsigned: true, isBigEndian: true);

        return new ECPoint { X = compressed[1..].ToArray(), Y = yBytes };
    }

    /// <summary>
    /// Everything this SDK needs to know about one of its two curves, computed once. Both have
    /// 32-byte field elements and a prime modulus p ≡ 3 (mod 4).
    /// </summary>
    private sealed class CurveInfo
    {
        public static readonly CurveInfo P256 = new(
            KeyCurve.P256,
            oidValue: "1.2.840.10045.3.1.7",
            multicodec0: 0x80, multicodec1: 0x24, // varint of 0x1200
            p: "FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF",
            a: "FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFC", // -3 mod p
            b: "5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B",
            order: "FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551");

        public static readonly CurveInfo K256 = new(
            KeyCurve.K256,
            oidValue: "1.3.132.0.10",
            multicodec0: 0xE7, multicodec1: 0x01, // varint of 0xE7
            p: "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F",
            a: "00",
            b: "07",
            order: "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141");

        private CurveInfo(
            KeyCurve curve, string oidValue, byte multicodec0, byte multicodec1,
            string p, string a, string b, string order)
        {
            Curve = curve;
            OidValue = oidValue;
            Multicodec0 = multicodec0;
            Multicodec1 = multicodec1;
            P = FromHex(p);
            A = FromHex(a);
            B = FromHex(b);
            SqrtExponent = (P + 1) / 4;
            Order = FromHex(order);

            HalfOrder = new byte[32];
            (Order / 2).TryWriteBytes(HalfOrder, out _, isUnsigned: true, isBigEndian: true);
        }

        public KeyCurve Curve { get; }

        public string OidValue { get; }

        /// <summary>The two bytes of the curve's multicodec varint, which prefix a multikey.</summary>
        public byte Multicodec0 { get; }

        /// <inheritdoc cref="Multicodec0"/>
        public byte Multicodec1 { get; }

        public BigInteger P { get; }

        public BigInteger A { get; }

        public BigInteger B { get; }

        /// <summary>(p + 1) / 4, the exponent of a modular square root when p ≡ 3 (mod 4).</summary>
        public BigInteger SqrtExponent { get; }

        /// <summary>The group order n.</summary>
        public BigInteger Order { get; }

        /// <summary>n / 2 as 32 big-endian bytes: the largest S a low-S signature may carry.</summary>
        public byte[] HalfOrder { get; }

        public static CurveInfo For(KeyCurve curve) => curve switch
        {
            KeyCurve.P256 => P256,
            KeyCurve.K256 => K256,
            _ => throw new ArgumentOutOfRangeException(nameof(curve), curve, "Unsupported key curve."),
        };

        public static CurveInfo? FromMulticodec(byte first, byte second)
        {
            if (first == P256.Multicodec0 && second == P256.Multicodec1)
                return P256;
            if (first == K256.Multicodec0 && second == K256.Multicodec1)
                return K256;
            return null;
        }

        public ECCurve CreateCurve() => Curve == KeyCurve.P256
            ? ECCurve.NamedCurves.nistP256
            : ECCurve.CreateFromValue(OidValue);

        private static BigInteger FromHex(string hex)
            => new(Convert.FromHexString(hex), isUnsigned: true, isBigEndian: true);
    }

    /// <summary>Base58 Bitcoin encoding (no check).</summary>
    internal static string Base58Encode(ReadOnlySpan<byte> data)
    {
        // Count leading zeros
        var leadingZeros = 0;
        foreach (var b in data)
        {
            if (b != 0) break;
            leadingZeros++;
        }

        // Convert to base58
        var result = new List<char>();
        var work = data.ToArray();

        while (HasNonZeroByte(work))
        {
            var remainder = 0;
            for (var i = 0; i < work.Length; i++)
            {
                var digit = (remainder << 8) + work[i];
                work[i] = (byte)(digit / 58);
                remainder = digit % 58;
            }
            result.Add(Base58Alphabet[remainder]);
        }

        // Add leading '1's for leading zero bytes
        for (var i = 0; i < leadingZeros; i++)
            result.Add('1');

        result.Reverse();
        return new string(result.ToArray());
    }

    private static bool HasNonZeroByte(byte[] data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] != 0) return true;
        }
        return false;
    }

    /// <summary>Base58 Bitcoin decoding (no check).</summary>
    internal static byte[] Base58Decode(string encoded)
        => string.IsNullOrEmpty(encoded) ? [] : Base58DecodeToArray(encoded);

    private static byte[] Base58DecodeToArray(ReadOnlySpan<char> encoded)
    {
        // Every base58 digit carries less than a byte, so the input length bounds the output.
        Span<byte> buffer = encoded.Length <= 128 ? stackalloc byte[128] : new byte[encoded.Length];
        return buffer[..Base58Decode(encoded, buffer)].ToArray();
    }

    /// <summary>Base58 Bitcoin decoding (no check) into <paramref name="destination"/>.</summary>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="FormatException">
    /// Thrown for a character outside the alphabet, or when the value does not fit in
    /// <paramref name="destination"/>. The work is bounded by the destination size, however
    /// long the input.
    /// </exception>
    internal static int Base58Decode(ReadOnlySpan<char> encoded, Span<byte> destination)
    {
        // Each leading '1' is a leading zero byte.
        var leadingZeros = 0;
        while (leadingZeros < encoded.Length && encoded[leadingZeros] == '1')
            leadingZeros++;

        if (leadingZeros > destination.Length)
            throw new FormatException("Base58 value is too long.");

        // Accumulate the value little-endian at the front of the destination:
        // value = value * 58 + digit, one character at a time.
        var capacity = destination.Length - leadingZeros;
        var length = 0;
        foreach (var c in encoded)
        {
            var digit = c < s_base58Digits.Length ? s_base58Digits[c] : -1;
            if (digit < 0)
                throw new FormatException($"Invalid Base58 character: '{c}'");

            var carry = digit;
            for (var i = 0; i < length; i++)
            {
                carry += destination[i] * 58;
                destination[i] = (byte)carry;
                carry >>= 8;
            }

            while (carry > 0)
            {
                if (length == capacity)
                    throw new FormatException("Base58 value is too long.");

                destination[length++] = (byte)carry;
                carry >>= 8;
            }
        }

        // Big-endian, after the leading zeros.
        destination[..length].Reverse();
        destination[..length].CopyTo(destination[leadingZeros..]);
        destination[..leadingZeros].Clear();
        return leadingZeros + length;
    }

    private static sbyte[] CreateBase58Digits()
    {
        var digits = new sbyte[128];
        digits.AsSpan().Fill(-1);
        for (var i = 0; i < Base58Alphabet.Length; i++)
            digits[Base58Alphabet[i]] = (sbyte)i;
        return digits;
    }

    /// <summary>
    /// Returns <c>true</c> if the S component of an IEEE P1363 signature is in low-S form
    /// (S ≤ half-order), as required by the AT Protocol.
    /// </summary>
    internal static bool IsLowS(ReadOnlySpan<byte> signature, KeyCurve curve)
    {
        var halfLen = signature.Length / 2;
        return CompareBigEndianUnsigned(signature[halfLen..], CurveInfo.For(curve).HalfOrder) <= 0;
    }

    /// <summary>
    /// Whether <paramref name="signature"/> has the IEEE P1363 length of both supported curves.
    /// A DER-encoded signature, which atproto does not allow, never does.
    /// </summary>
    internal static bool HasSignatureLength(ReadOnlySpan<byte> signature) => signature.Length == SignatureLength;

    /// <summary>
    /// Normalizes an ECDSA signature to use low-S form as required by AT Protocol.
    /// In low-S form, S must be ≤ (curve order) / 2.
    /// If S > half-order, replaces S with (order - S).
    /// </summary>
    internal static byte[] NormalizeLowSSignature(byte[] signature, KeyCurve curve)
    {
        var halfLen = signature.Length / 2;
        var sSpan = signature.AsSpan(halfLen);
        var curveInfo = CurveInfo.For(curve);

        // Compare S > halfOrder (big-endian unsigned)
        if (CompareBigEndianUnsigned(sSpan, curveInfo.HalfOrder) > 0)
        {
            var s = new BigInteger(sSpan, true, true);
            var lowS = curveInfo.Order - s;
            var lowSBytes = lowS.ToByteArray(true, true);

            var result = (byte[])signature.Clone();
            // Clear S portion and write normalized value (right-aligned, zero-padded)
            Array.Clear(result, halfLen, halfLen);
            lowSBytes.CopyTo(result, halfLen + (halfLen - lowSBytes.Length));
            return result;
        }

        return signature;
    }

    /// <summary>
    /// Compares two big-endian unsigned byte sequences.
    /// Returns negative if a &lt; b, 0 if equal, positive if a &gt; b.
    /// </summary>
    private static int CompareBigEndianUnsigned(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        // Pad to same length by comparing from most significant byte
        var maxLen = Math.Max(a.Length, b.Length);
        for (var i = 0; i < maxLen; i++)
        {
            var aByte = i < maxLen - a.Length ? (byte)0 : a[i - (maxLen - a.Length)];
            var bByte = i < maxLen - b.Length ? (byte)0 : b[i - (maxLen - b.Length)];
            if (aByte != bByte)
                return aByte.CompareTo(bByte);
        }
        return 0;
    }
}

/// <summary>The elliptic curve used by an AT Protocol key.</summary>
public enum KeyCurve
{
    /// <summary>NIST P-256 (secp256r1) — used for OAuth DPoP proofs and newer AT Protocol keys.</summary>
    P256,

    /// <summary>secp256k1 — used for legacy/Bitcoin-derived AT Protocol keys.</summary>
    K256,
}

internal static class KeyCurveExtensions
{
    /// <summary>The JWS <c>alg</c> for signatures made with a key on this curve.</summary>
    internal static string JwsAlgorithm(this KeyCurve curve) => curve switch
    {
        KeyCurve.P256 => "ES256",
        KeyCurve.K256 => "ES256K",
        _ => throw new ArgumentOutOfRangeException(nameof(curve), curve, "Unsupported key curve."),
    };
}

/// <summary>
/// An AT Protocol signing key wrapping an ECDsa instance with curve metadata.
/// Supports signing, verification, and multikey/did:key encoding.
/// </summary>
public sealed class AtProtoKey : IDisposable
{
    private readonly ECDsa _key;
    private bool _disposed;

    /// <summary>The elliptic curve this key uses.</summary>
    public KeyCurve Curve { get; }

    internal AtProtoKey(ECDsa key, KeyCurve curve)
    {
        _key = key;
        Curve = curve;
    }

    /// <summary>
    /// Signs the given data bytes using SHA-256 + ECDSA.
    /// Returns the signature in IEEE P1363 format (r || s concatenation), with low-S normalization.
    /// </summary>
    /// <param name="data">The data to sign (will be SHA-256 hashed internally).</param>
    /// <returns>The signature bytes.</returns>
    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var signature = _key.SignData(
            data,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return AtProtoCrypto.NormalizeLowSSignature(signature, Curve);
    }

    /// <summary>
    /// Verifies a signature against data bytes.
    /// Rejects high-S signatures (signature malleability) per AT Protocol spec.
    /// </summary>
    /// <param name="data">The original data that was signed.</param>
    /// <param name="signature">The signature in IEEE P1363 format (r || s).</param>
    /// <returns><c>true</c> if the signature is valid and uses low-S form.</returns>
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Only the fixed-length r || s form, and only low-S: AT Protocol requires low-S
        // normalization to rule out signature malleability.
        if (!AtProtoCrypto.HasSignatureLength(signature) || !AtProtoCrypto.IsLowS(signature, Curve))
            return false;

        return _key.VerifyData(
            data,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>Returns the compressed (SEC1) public key bytes (33 bytes).</summary>
    public byte[] GetCompressedPublicKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var q = _key.ExportParameters(false).Q;
        return AtProtoCrypto.CompressPoint(q.X, yIsOdd: (q.Y![^1] & 1) == 1);
    }

    /// <summary>Returns the multikey string (z-prefixed base58btc with multicodec prefix).</summary>
    public string ToMultikey()
    {
        var compressed = GetCompressedPublicKey();
        return AtProtoCrypto.ToMultikey(compressed, Curve);
    }

    /// <summary>Returns the <c>did:key:z...</c> identifier for this key's public component.</summary>
    public string ToDidKey() => $"did:key:{ToMultikey()}";

    /// <summary>Exports the private key in PKCS#8 format.</summary>
    /// <remarks>
    /// <b>Security:</b> The exported key is unencrypted. Store securely.
    /// </remarks>
    public byte[] ExportPrivateKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _key.ExportPkcs8PrivateKey();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _key.Dispose();
        }
    }
}
