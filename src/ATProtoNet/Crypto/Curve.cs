using System.Numerics;
using System.Security.Cryptography;
using ATProtoNet.Data;

namespace ATProtoNet.Crypto;

// Everything the SDK knows about one of its two curves. Both have 32-byte field elements and scalars and a
// prime modulus p ≡ 3 (mod 4), so one implementation serves both.
internal sealed class Curve
{
    public const int FieldLength = 32;

    public static readonly Curve P256 = new(KeyCurve.P256, "1.2.840.10045.3.1.7", 0x1200, "P-256", "ES256",
        p: "FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF",
        a: "FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFC",
        b: "5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B",
        n: "FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
        gx: "6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296",
        gy: "4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5");

    public static readonly Curve K256 = new(KeyCurve.K256, "1.3.132.0.10", 0xE7, "secp256k1", "ES256K",
        p: "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F",
        a: "00",
        b: "07",
        n: "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141",
        gx: "79BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798",
        gy: "483ADA7726A3C4655DA4FBFC0E1108A8FD17B448A68554199C47D08FFB10D4B8");

    private readonly byte[] _order, _halfOrder;

    private Curve(KeyCurve id, string oid, ulong multicodec, string jwkName, string jwsAlgorithm,
        string p, string a, string b, string n, string gx, string gy)
    {
        (Id, Oid, Multicodec, JwkName, JwsAlgorithm) = (id, oid, multicodec, jwkName, jwsAlgorithm);
        (P, A, B, N, Gx, Gy) = (Hex(p), Hex(a), Hex(b), Hex(n), Hex(gx), Hex(gy));
        _order = Convert.FromHexString(n);
        _halfOrder = (N / 2).ToByteArray(isUnsigned: true, isBigEndian: true);
        MulticodecPrefix = new byte[UnsignedVarint.GetLength(multicodec)];
        UnsignedVarint.Write(multicodec, MulticodecPrefix);
        HasPlatformEcdsa = id == KeyCurve.P256 || Probe(EcCurve);
    }

    public KeyCurve Id { get; }
    public string Oid { get; }
    public ulong Multicodec { get; }

    // The multicodec's varint, which prefixes a multikey.
    public byte[] MulticodecPrefix { get; }
    public string JwkName { get; }
    public string JwsAlgorithm { get; }
    public BigInteger P { get; }
    public BigInteger A { get; }
    public BigInteger B { get; }
    public BigInteger N { get; }
    public BigInteger Gx { get; }
    public BigInteger Gy { get; }

    // Whether the platform's ECDsa knows the curve: secp256k1 is missing where OpenSSL is (macOS).
    public bool HasPlatformEcdsa { get; }

    public ECCurve EcCurve => Id == KeyCurve.P256 ? ECCurve.NamedCurves.nistP256 : ECCurve.CreateFromValue(Oid);

    public static Curve Of(KeyCurve curve) => curve switch
    {
        KeyCurve.P256 => P256,
        KeyCurve.K256 => K256,
        _ => throw new ArgumentOutOfRangeException(nameof(curve), curve, "Not an atproto key curve."),
    };

    public static Curve? FromMulticodec(ulong codec) => codec == P256.Multicodec ? P256 : codec == K256.Multicodec ? K256 : null;

    public static Curve? FromJwkName(string name) => name == P256.JwkName ? P256 : name == K256.JwkName ? K256 : null;

    // The uncompressed point (x || y) of a 33-byte SEC1 compressed point, checked to lie on the curve.
    public byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        if (compressed.Length != VerificationKey.CompressedPointLength)
            throw new FormatException($"A compressed point is {VerificationKey.CompressedPointLength} bytes, not {compressed.Length}.");
        if (compressed[0] is not (0x02 or 0x03))
            throw new FormatException($"A compressed point starts with 0x02 or 0x03, not 0x{compressed[0]:X2}.");
        var x = Unsigned(compressed[1..]);
        if (x >= P) throw new FormatException("The point's x coordinate is not below the field prime.");
        var ySquared = Mod(BigInteger.ModPow(x, 3, P) + A * x + B);
        // With p ≡ 3 (mod 4) a square root is ySquared^((p + 1) / 4), if one exists.
        var y = BigInteger.ModPow(ySquared, (P + 1) / 4, P);
        if (y * y % P != ySquared) throw new FormatException("The x coordinate has no point on the curve.");
        if (y.IsEven == (compressed[0] == 0x03)) y = P - y;
        var point = new byte[2 * FieldLength];
        compressed[1..].CopyTo(point);
        Write(y, point.AsSpan(FieldLength));
        return point;
    }

    // Throws unless x || y is a point on the curve.
    public void CheckOnCurve(ReadOnlySpan<byte> point)
    {
        var (x, y) = (Unsigned(point[..FieldLength]), Unsigned(point[FieldLength..]));
        if (x >= P || y >= P || Mod(y * y) != Mod(BigInteger.ModPow(x, 3, P) + A * x + B))
            throw new FormatException("The point is not on the curve.");
    }

    // The SEC1 compression of x || y: x, with the parity of y in the prefix byte.
    public static byte[] Compress(ReadOnlySpan<byte> point) =>
        [(byte)((point[^1] & 1) == 1 ? 0x03 : 0x02), .. point[..FieldLength]];

    // Whether a 32-byte big-endian scalar lies in [1, n - 1].
    public bool InRange(ReadOnlySpan<byte> scalar) => scalar.ContainsAnyExcept((byte)0) && scalar.SequenceCompareTo(_order) < 0;

    public bool IsLowS(ReadOnlySpan<byte> s) => s.SequenceCompareTo(_halfOrder) <= 0;

    // s := n - s, for an s in range.
    public void Negate(Span<byte> s) => Write(N - Unsigned(s), s);

    public BigInteger Mod(BigInteger value) => (value % P + P) % P;

    public static BigInteger Unsigned(ReadOnlySpan<byte> bigEndian) => new(bigEndian, isUnsigned: true, isBigEndian: true);

    public static void Write(BigInteger value, Span<byte> destination)
    {
        destination.Clear();
        value.TryWriteBytes(destination[(destination.Length - value.GetByteCount(isUnsigned: true))..], out _, isUnsigned: true, isBigEndian: true);
    }

    private static BigInteger Hex(string hex) => Unsigned(Convert.FromHexString(hex));

    private static bool Probe(ECCurve curve)
    {
        try
        {
            using var _ = ECDsa.Create(curve);
            return true;
        }
        catch (Exception e) when (e is PlatformNotSupportedException or CryptographicException)
        {
            return false;
        }
    }
}
