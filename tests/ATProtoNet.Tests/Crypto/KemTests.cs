using System.Security.Cryptography;
using ATProtoNet.Crypto;
using ATProtoNet.TestKit;

namespace ATProtoNet.Tests.Crypto;

/// <summary>Key encapsulation: DHKEM(P-256, HKDF-SHA256) of RFC 9180, kept apart from signing keys.</summary>
public sealed class KemTests
{
    // RFC 9180 appendix A.3.1: DHKEM(P-256, HKDF-SHA256), base mode.
    private const string RfcSkRm = "f3ce7fdae57e1a310d87f1ebbde6f328be0a99cdbcadf4d6589cf29de4b8ffd2";
    private const string RfcPkRm = "04fe8c19ce0905191ebc298a9245792531f26f0cece2460639e8bc39cb7f706a826a779b4cf969b8a0e539c7f62fb3d30ad6aa8f80e30f1d128aafd68a2ce72ea0";
    private const string RfcEnc = "04a92719c6195d5085104f469a8b9814d5838ff72b60501e2c4466e5e67b325ac98536d7b61a1af4b78e5b7f951c0900be863c403ce65c9bfcb9382657222d18c4";
    private const string RfcSharedSecret = "c0d26aeab536609a572b07695d933b589dcf363ff9d93c93adea537aeabb8cb8";

    [Fact]
    [Trait("Spec", "CRY-51")]
    public void Decapsulate_Rfc9180Vector_RecoversTheSharedSecret()
    {
        using var key = KemPrivateKey.ImportPkcs8(Pkcs8(RfcSkRm), KemAlgorithm.Default);

        using var secret = key.Decapsulate(Convert.FromHexString(RfcEnc));

        Assert.Equal(VerificationKey.FromUncompressedPoint(KeyCurve.P256, Convert.FromHexString(RfcPkRm)).DidKey.Multikey, key.PublicKey.Multikey);
        // A secret is only ever an HMAC key, so compare the MACs both key.
        Assert.Equal(HMACSHA256.HashData(Convert.FromHexString(RfcSharedSecret), Array.Empty<byte>()), Expand(secret));
    }

    [Fact]
    [Trait("Spec", "CRY-51")]
    public void Encapsulate_ToAPublicKey_IsRecoveredByItsHolderOnly()
    {
        using var holder = KemPrivateKey.Generate(KemAlgorithm.Default);
        using var other = KemPrivateKey.Generate(new KemAlgorithm.EcdhP256());

        var encapsulation = KemPublicKey.FromMultikey(holder.PublicKey.Multikey).Encapsulate();
        using var sent = encapsulation.Secret;
        using var received = holder.Decapsulate(encapsulation.Ciphertext.Span);
        using var wrong = other.Decapsulate(encapsulation.Ciphertext.Span);

        Assert.Equal(65, encapsulation.Ciphertext.Length);
        Assert.Equal(Expand(sent), Expand(received));
        Assert.NotEqual(Expand(sent), Expand(wrong));
        Assert.NotEqual(encapsulation.Ciphertext.ToArray(), holder.PublicKey.Encapsulate().Ciphertext.ToArray());
    }

    [Fact]
    [Trait("Spec", "CRY-52")]
    public void ComputeMac_IsHmacSha256KeyedByTheSecret()
    {
        using var key = KemPrivateKey.Generate(KemAlgorithm.Default);
        var (sent, ciphertext) = key.PublicKey.Encapsulate();
        using var secret = sent;
        using var received = key.Decapsulate(ciphertext.Span);
        var (first, second, other) = (new byte[SharedSecret.MacLength], new byte[SharedSecret.MacLength], new byte[SharedSecret.MacLength]);

        secret.ComputeMac("context and message"u8, first);
        received.ComputeMac("context and message"u8, second);
        secret.ComputeMac("another message"u8, other);

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.Throws<ArgumentOutOfRangeException>(() => secret.ComputeMac([], new byte[31]));
    }

    [Fact]
    [Trait("Spec", "CRY-52")]
    public void SharedSecret_Disposed_ThrowsAndIsRedacted()
    {
        using var key = KemPrivateKey.Generate(KemAlgorithm.Default);
        var secret = key.PublicKey.Encapsulate().Secret;

        Assert.Equal("SharedSecret([redacted])", secret.ToString());
        secret.Dispose();
        Assert.Throws<ObjectDisposedException>(() => secret.ComputeMac([], new byte[32]));
    }

    [Fact]
    [Trait("Spec", "CRY-52")]
    public void KemPrivateKey_ToString_ShowsThePublicKeyOnly()
    {
        using var key = KemPrivateKey.ImportPkcs8(Pkcs8(RfcSkRm), KemAlgorithm.Default);

        Assert.Equal($"KemPrivateKey({key.PublicKey.Multikey})", key.ToString());
        Assert.DoesNotContain(RfcSkRm, key.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Spec", "CRY-53")]
    public void FromMultikey_RoundTripsExactly()
    {
        using var key = KemPrivateKey.Generate(KemAlgorithm.Default);

        var parsed = KemPublicKey.FromMultikey(key.PublicKey.Multikey);

        Assert.Equal(key.PublicKey, parsed);
        Assert.Equal(key.PublicKey.Multikey, parsed.Multikey);
        Assert.IsType<KemAlgorithm.EcdhP256>(parsed.Algorithm);
    }

    [Theory]
    [InlineData(TestKeys.K256Multibase)]
    [InlineData("z6MkhaXgBZDvotDkL5257faiztiGiC2QtKLGpbnnEGta2doK")]
    [InlineData("not a multikey")]
    [Trait("Spec", "CRY-53")]
    public void FromMultikey_AlgorithmOutsideTheSet_ThrowsFormatException(string multikey) =>
        Assert.Throws<FormatException>(() => KemPublicKey.FromMultikey(multikey));

    [Fact]
    [Trait("Spec", "CRY-51")]
    public void Decapsulate_MalformedCiphertext_ThrowsFormatException()
    {
        using var key = KemPrivateKey.Generate(KemAlgorithm.Default);
        var ciphertext = key.PublicKey.Encapsulate().Ciphertext.ToArray();
        var offCurve = (byte[])ciphertext.Clone();
        offCurve[64] ^= 1;

        Assert.Throws<FormatException>(() => key.Decapsulate(ciphertext[..64]));
        Assert.Throws<FormatException>(() => key.Decapsulate([0x02, .. ciphertext[1..]]));
        Assert.Throws<FormatException>(() => key.Decapsulate(offCurve));
    }

    [Fact]
    [Trait("Spec", "CRY-51")]
    public void ImportPkcs8_ExportedOrForeignKey_RoundTripsOrThrows()
    {
        using var key = KemPrivateKey.Generate(KemAlgorithm.Default);
        using var k256 = ECDiffieHellman.Create(ECCurve.CreateFromValue("1.3.132.0.10"));

        using var imported = KemPrivateKey.ImportPkcs8(key.ExportPkcs8(), KemAlgorithm.Default);

        Assert.Equal(key.PublicKey, imported.PublicKey);
        Assert.Throws<FormatException>(() => KemPrivateKey.ImportPkcs8(k256.ExportPkcs8PrivateKey(), KemAlgorithm.Default));
        Assert.Throws<FormatException>(() => KemPrivateKey.ImportPkcs8([1, 2, 3], KemAlgorithm.Default));
    }

    [Fact]
    [Trait("Spec", "CRY-50")]
    public void KemTypes_NeverTakeOrReturnSigningMaterial()
    {
        Type[] signing = [typeof(SigningKey), typeof(VerificationKey), typeof(DidKey)];
        var members = new[] { typeof(KemPublicKey), typeof(KemPrivateKey), typeof(SharedSecret), typeof(KemAlgorithm) }
            .SelectMany(t => t.GetMethods().Where(m => m.DeclaringType == t));

        Assert.DoesNotContain(members, m => signing.Contains(m.ReturnType) || m.GetParameters().Any(p => signing.Contains(p.ParameterType)));
    }

    [Fact]
    [Trait("Spec", "CRY-51")]
    public void Values_OverBytes_AreNotRecordsWithReferenceEquality()
    {
        // Regression (#238 review): record equality over ReadOnlyMemory<byte> compares references, not bytes.
        Assert.Null(typeof(KemEncapsulation).GetMethod("<Clone>$"));
        Assert.False(typeof(IEquatable<ATProtoNet.Crypto.Jose.DecodedJws>).IsAssignableFrom(typeof(ATProtoNet.Crypto.Jose.DecodedJws)));
    }

    private static byte[] Expand(SharedSecret secret)
    {
        var mac = new byte[SharedSecret.MacLength];
        secret.ComputeMac([], mac);
        return mac;
    }

    private static byte[] Pkcs8(string scalarHex)
    {
        using var key = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = Convert.FromHexString(scalarHex) });
        return key.ExportPkcs8PrivateKey();
    }
}
