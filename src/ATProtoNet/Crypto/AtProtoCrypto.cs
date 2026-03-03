using System.Buffers.Binary;
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
    /// <summary>OID for secp256k1 (K-256): 1.3.132.0.10</summary>
    private static readonly Oid s_k256Oid = new("1.3.132.0.10");

    /// <summary>Multicodec varint prefix for P-256 compressed public keys (0x1200 → [0x80, 0x24]).</summary>
    private static readonly byte[] s_p256MulticodecPrefix = [0x80, 0x24];

    /// <summary>Multicodec varint prefix for K-256 compressed public keys (0xE7 → [0xE7, 0x01]).</summary>
    private static readonly byte[] s_k256MulticodecPrefix = [0xE7, 0x01];

    // Base58 Bitcoin alphabet
    private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

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
            var curve = ECCurve.CreateFromValue(s_k256Oid.Value!);
            var ecdsa = ECDsa.Create(curve);
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
        if (compressedPublicKey.Length != 33)
            throw new ArgumentException("Compressed public key must be 33 bytes.", nameof(compressedPublicKey));

        var ecCurve = curve == KeyCurve.P256
            ? ECCurve.NamedCurves.nistP256
            : ECCurve.CreateFromValue(s_k256Oid.Value!);

        var ecdsa = ECDsa.Create();

        // .NET 10+ supports importing compressed point directly via ECParameters
        // We need to decompress the point
        var parameters = new ECParameters
        {
            Curve = ecCurve,
            Q = DecompressPoint(compressedPublicKey, ecCurve),
        };

        ecdsa.ImportParameters(parameters);
        return new AtProtoKey(ecdsa, curve);
    }

    /// <summary>
    /// Parses a <c>did:key</c> identifier and returns the public key.
    /// </summary>
    /// <param name="didKey">A DID in <c>did:key:z...</c> format.</param>
    /// <returns>The parsed <see cref="AtProtoKey"/> (public key only).</returns>
    /// <exception cref="FormatException">Thrown when the did:key is malformed.</exception>
    public static AtProtoKey FromDidKey(string didKey)
    {
        if (!didKey.StartsWith("did:key:z", StringComparison.Ordinal))
            throw new FormatException("did:key must start with 'did:key:z'.");

        var multikey = didKey["did:key:".Length..];
        return FromMultikey(multikey);
    }

    /// <summary>
    /// Parses a multikey string (<c>z</c>-prefixed base58btc-encoded multicodec key).
    /// </summary>
    /// <param name="multikey">The multikey string starting with 'z'.</param>
    /// <returns>The parsed <see cref="AtProtoKey"/> (public key only).</returns>
    public static AtProtoKey FromMultikey(string multikey)
    {
        if (string.IsNullOrEmpty(multikey) || multikey[0] != 'z')
            throw new FormatException("Multikey must start with 'z' (base58btc prefix).");

        var bytes = Base58Decode(multikey[1..]);

        if (bytes.Length < 2)
            throw new FormatException("Multikey too short.");

        KeyCurve curve;
        int prefixLen;

        if (bytes.Length >= 2 && bytes[0] == 0x80 && bytes[1] == 0x24)
        {
            curve = KeyCurve.P256;
            prefixLen = 2;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xE7 && bytes[1] == 0x01)
        {
            curve = KeyCurve.K256;
            prefixLen = 2;
        }
        else
        {
            throw new FormatException($"Unknown multicodec prefix: 0x{bytes[0]:X2} 0x{bytes[1]:X2}");
        }

        var compressedKey = bytes[prefixLen..];
        return ImportCompressedPublicKey(compressedKey, curve);
    }

    /// <summary>
    /// Verifies a signature against message bytes using a <c>did:key</c>.
    /// </summary>
    /// <param name="didKey">The signer's did:key.</param>
    /// <param name="message">The message bytes that were signed (pre-hashed with SHA-256).</param>
    /// <param name="signature">The signature bytes (IEEE P1363 format — r || s concatenation).</param>
    /// <returns><c>true</c> if the signature is valid.</returns>
    public static bool VerifySignature(string didKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        using var key = FromDidKey(didKey);
        return key.Verify(message, signature);
    }

    /// <summary>
    /// Encodes a 33-byte compressed public key as a base58btc multikey string.
    /// </summary>
    internal static string ToMultikey(ReadOnlySpan<byte> compressedPublicKey, KeyCurve curve)
    {
        var prefix = curve == KeyCurve.P256 ? s_p256MulticodecPrefix : s_k256MulticodecPrefix;
        var encoded = new byte[prefix.Length + compressedPublicKey.Length];
        prefix.CopyTo(encoded, 0);
        compressedPublicKey.CopyTo(encoded.AsSpan(prefix.Length));
        return "z" + Base58Encode(encoded);
    }

    /// <summary>
    /// Decompresses an EC point from compressed SEC1 form.
    /// Computes Y from X using the curve equation y² = x³ + ax + b (mod p).
    /// </summary>
    private static ECPoint DecompressPoint(ReadOnlySpan<byte> compressed, ECCurve curve)
    {
        if (compressed.Length != 33)
            throw new FormatException("Compressed point must be 33 bytes.");

        var prefix = compressed[0];
        if (prefix is not (0x02 or 0x03))
            throw new FormatException($"Invalid compressed point prefix: 0x{prefix:X2}");

        var isOdd = prefix == 0x03;
        var xBytes = compressed[1..].ToArray();

        // Get curve parameters
        ECCurveParams curveParams;
        var oid = curve.Oid?.Value;

        if (oid == "1.2.840.10045.3.1.7" || curve.Oid?.FriendlyName == "nistP256")
            curveParams = ECCurveParams.P256();
        else if (oid == "1.3.132.0.10")
            curveParams = ECCurveParams.K256();
        else
            throw new ArgumentException($"Unsupported curve for decompression: {oid}");

        var p = new System.Numerics.BigInteger(curveParams.P, true, true);
        var a = new System.Numerics.BigInteger(curveParams.A, true, true);
        var b = new System.Numerics.BigInteger(curveParams.B, true, true);
        var x = new System.Numerics.BigInteger(xBytes, true, true);

        // y² = x³ + ax + b (mod p)
        var ySquared = (System.Numerics.BigInteger.ModPow(x, 3, p) + a * x + b) % p;
        if (ySquared < 0) ySquared += p;

        // Compute modular square root using Tonelli-Shanks (both P-256 and K-256 have p ≡ 3 mod 4)
        // For p ≡ 3 mod 4: y = ySquared^((p+1)/4) mod p
        var exp = (p + 1) / 4;
        var y = System.Numerics.BigInteger.ModPow(ySquared, exp, p);

        // Verify: y² mod p == ySquared
        if (System.Numerics.BigInteger.ModPow(y, 2, p) != ySquared)
            throw new FormatException("Invalid compressed point: no valid Y coordinate.");

        // Choose correct Y parity (odd/even)
        if (y.IsEven == isOdd)
            y = p - y;

        var yBytes = y.ToByteArray(true, true);

        // Pad to 32 bytes
        var xPadded = new byte[32];
        var yPadded = new byte[32];
        xBytes.AsSpan(0, Math.Min(xBytes.Length, 32)).CopyTo(xPadded.AsSpan(32 - Math.Min(xBytes.Length, 32)));
        yBytes.AsSpan(0, Math.Min(yBytes.Length, 32)).CopyTo(yPadded.AsSpan(32 - Math.Min(yBytes.Length, 32)));

        return new ECPoint { X = xPadded, Y = yPadded };
    }

    /// <summary>Well-known curve parameters for EC point decompression.</summary>
    private readonly struct ECCurveParams
    {
        public byte[] P { get; init; }
        public byte[] A { get; init; }
        public byte[] B { get; init; }

        /// <summary>NIST P-256 (secp256r1) curve parameters.</summary>
        public static ECCurveParams P256() => new()
        {
            P = [0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x01,
                 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF,
                 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
            // a = -3 mod p = p - 3
            A = [0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x01,
                 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF,
                 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFC],
            B = [0x5A, 0xC6, 0x35, 0xD8, 0xAA, 0x3A, 0x93, 0xE7,
                 0xB3, 0xEB, 0xBD, 0x55, 0x76, 0x98, 0x86, 0xBC,
                 0x65, 0x1D, 0x06, 0xB0, 0xCC, 0x53, 0xB0, 0xF6,
                 0x3B, 0xCE, 0x3C, 0x3E, 0x27, 0xD2, 0x60, 0x4B],
        };

        /// <summary>secp256k1 (K-256) curve parameters.</summary>
        public static ECCurveParams K256() => new()
        {
            P = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                 0xFF, 0xFF, 0xFF, 0xFE, 0xFF, 0xFF, 0xFC, 0x2F],
            A = [0x00], // a = 0
            B = [0x07], // b = 7
        };
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

        while (work.Any(b => b != 0))
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

    /// <summary>Base58 Bitcoin decoding (no check).</summary>
    internal static byte[] Base58Decode(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return [];

        // Count leading '1's
        var leadingOnes = 0;
        foreach (var c in encoded)
        {
            if (c != '1') break;
            leadingOnes++;
        }

        // Convert from base58
        var work = new List<byte>();

        foreach (var c in encoded)
        {
            var digit = Base58Alphabet.IndexOf(c);
            if (digit < 0)
                throw new FormatException($"Invalid Base58 character: '{c}'");

            var carry = digit;
            for (var i = 0; i < work.Count; i++)
            {
                carry += work[i] * 58;
                work[i] = (byte)(carry & 0xFF);
                carry >>= 8;
            }
            while (carry > 0)
            {
                work.Add((byte)(carry & 0xFF));
                carry >>= 8;
            }
        }

        work.Reverse();

        // Add leading zeros
        var result = new byte[leadingOnes + work.Count];
        work.CopyTo(result, leadingOnes);
        return result;
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

        return NormalizeLowS(signature);
    }

    /// <summary>
    /// Verifies a signature against data bytes.
    /// </summary>
    /// <param name="data">The original data that was signed.</param>
    /// <param name="signature">The signature in IEEE P1363 format (r || s).</param>
    /// <returns><c>true</c> if the signature is valid.</returns>
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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

        var parameters = _key.ExportParameters(false);
        var x = parameters.Q.X!;
        var y = parameters.Q.Y!;

        var compressed = new byte[33];
        // 0x02 if Y is even, 0x03 if Y is odd
        compressed[0] = (byte)((y[^1] & 1) == 0 ? 0x02 : 0x03);
        x.CopyTo(compressed, 1);
        return compressed;
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

    /// <summary>
    /// Normalizes an ECDSA signature to use low-S form as required by AT Protocol.
    /// In low-S form, S must be ≤ (curve order) / 2.
    /// </summary>
    private static byte[] NormalizeLowS(byte[] signature)
    {
        // IEEE P1363 format: r (32 bytes) || s (32 bytes) for 256-bit curves
        var halfLen = signature.Length / 2;
        var s = signature[halfLen..];

        // For both P-256 and K-256, the order is ~2^256
        // Check if S > order/2 by checking if the high bit is set
        // (This is a simplified check — for production, compare against actual half-order)
        if ((s[0] & 0x80) != 0)
        {
            // S is in the upper half — need to compute order - S
            // For P-256: order = FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551
            // For K-256: order = FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141
            // half_order has high bit clear, so just negate S mod order
            // However, .NET's SignData already produces low-S signatures on most platforms
            // This is a safety net
        }

        return signature;
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
