using System.Security.Cryptography;
using ATProtoNet.Crypto;
using ATProtoNet.TestKit;

namespace ATProtoNet.Tests.Crypto;

/// <summary>Private keys: generation, import, signing, ownership and secrecy.</summary>
public sealed class SigningKeyTests
{
    private static readonly byte[] Message = "atproto signing"u8.ToArray();

    public static TheoryData<KeyCurve> Curves() => [KeyCurve.P256, KeyCurve.K256];

    internal static SigningKey TestKey(KeyCurve curve) => SigningKey.ImportRawPrivateScalar(
        Convert.FromHexString(curve == KeyCurve.P256 ? TestKeys.P256PrivateKeyHex : TestKeys.K256PrivateKeyHex), curve);

    [Theory]
    [MemberData(nameof(Curves))]
    [Trait("Spec", "CRY-1"), Trait("Spec", "CRY-7"), Trait("Spec", "CRY-10")]
    public void Generate_EachCurve_SignsWhatItsPublicKeyVerifies(KeyCurve curve)
    {
        using var key = SigningKey.Generate(curve);
        using var other = SigningKey.Generate(curve);

        Assert.Equal(curve, key.Curve);
        Assert.NotEqual(key.PublicKey, other.PublicKey);
        Assert.True(key.PublicKey.Verify(Message, key.Sign(Message)));
        Assert.False(other.PublicKey.Verify(Message, key.Sign(Message)));
    }

    [Theory]
    [MemberData(nameof(Curves))]
    [Trait("Spec", "CRY-10")]
    public void Sign_ManyTimes_IsAlways64BytesLowS(KeyCurve curve)
    {
        using var key = SigningKey.Generate(curve);
        var halfOrder = (curve == KeyCurve.P256 ? SignatureTests.P256Order : SignatureTests.K256Order) / 2;

        for (var i = 0; i < 40; i++)
        {
            var signature = key.Sign(Message);
            Assert.Equal(VerificationKey.SignatureLength, signature.Length);
            Assert.True(new System.Numerics.BigInteger(signature.AsSpan(32), isUnsigned: true, isBigEndian: true) <= halfOrder);
        }
    }

    [Fact]
    [Trait("Spec", "CRY-10")]
    public void Sign_IntoSpan_WritesTheSignatureAndReturnsItsLength()
    {
        using var key = TestKey(KeyCurve.P256);
        var destination = new byte[80];

        Assert.Equal(64, key.Sign(Message, destination));
        Assert.True(key.PublicKey.Verify(Message, destination.AsSpan(0, 64)));
        Assert.Throws<ArgumentOutOfRangeException>(() => key.Sign(Message, new byte[63]));
    }

    [Theory]
    [MemberData(nameof(Curves))]
    [Trait("Spec", "CRY-3")]
    public async Task Sign_ConcurrentCallers_AllProduceValidSignatures(KeyCurve curve)
    {
        using var key = SigningKey.Generate(curve);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            Enumerable.Range(0, 25).All(_ => key.PublicKey.Verify(Message, key.Sign(Message))), TestContext.Current.CancellationToken)));

        Assert.All(results, Assert.True);
    }

    [Theory]
    [MemberData(nameof(Curves))]
    [Trait("Spec", "CRY-4")]
    public void ImportPkcs8_ExportedKey_IsTheSameKey(KeyCurve curve)
    {
        using var key = SigningKey.Generate(curve);

        using var imported = SigningKey.ImportPkcs8(key.ExportPkcs8(), curve);

        Assert.Equal(key.PublicKey, imported.PublicKey);
        Assert.True(key.PublicKey.Verify(Message, imported.Sign(Message)));
    }

    [Theory]
    [InlineData(KeyCurve.P256, KeyCurve.K256)]
    [InlineData(KeyCurve.K256, KeyCurve.P256)]
    [Trait("Spec", "CRY-4")]
    public void ImportPkcs8_OtherCurve_ThrowsFormatException(KeyCurve actual, KeyCurve declared)
    {
        using var key = SigningKey.Generate(actual);

        Assert.Throws<FormatException>(() => SigningKey.ImportPkcs8(key.ExportPkcs8(), declared));
    }

    [Fact]
    [Trait("Spec", "CRY-4")]
    public void ImportPkcs8_NotPkcs8_ThrowsFormatException()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        byte[] trailing = [.. key.ExportPkcs8(), 0];

        Assert.Throws<FormatException>(() => SigningKey.ImportPkcs8([1, 2, 3], KeyCurve.P256));
        Assert.Throws<FormatException>(() => SigningKey.ImportPkcs8(trailing, KeyCurve.P256));
    }

    [Theory]
    [InlineData("00")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData(SignatureTests.P256OrderHex)]
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")]
    public void ImportRawPrivateScalar_OutsideOneToOrder_ThrowsFormatException(string scalar) =>
        Assert.Throws<FormatException>(() => SigningKey.ImportRawPrivateScalar(Convert.FromHexString(scalar), KeyCurve.P256));

    [Fact]
    public void ImportRawPrivateScalar_TestKey_HasItsPublishedDidKey()
    {
        using var p256 = TestKey(KeyCurve.P256);
        using var k256 = TestKey(KeyCurve.K256);

        Assert.Equal(TestKeys.P256DidKey, p256.PublicKey.DidKey.Value);
        Assert.Equal(TestKeys.K256DidKey, k256.PublicKey.DidKey.Value);
    }

    [Fact]
    [Trait("Spec", "CRY-47")]
    public void Sign_Disposed_ThrowsObjectDisposedException()
    {
        var key = SigningKey.Generate(KeyCurve.P256);
        key.Dispose();

        Assert.Throws<ObjectDisposedException>(() => key.Sign(Message));
        Assert.Throws<ObjectDisposedException>(() => key.ExportPkcs8());
        Assert.Equal(KeyCurve.P256, key.PublicKey.Curve);
    }

    [Fact]
    [Trait("Spec", "CRY-46")]
    public void ToString_NamesCurveAndPublicKeyOnly()
    {
        using var key = TestKey(KeyCurve.P256);

        Assert.Equal($"SigningKey(P256, {TestKeys.P256DidKey})", key.ToString());
        Assert.DoesNotContain(TestKeys.P256PrivateKeyHex, key.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Spec", "CRY-2")]
    public void Types_PrivateAndPublicMaterial_AreSeparate()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(SigningKey)));
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(VerificationKey)));
        Assert.DoesNotContain(typeof(VerificationKey).GetMethods(), m => m.Name.StartsWith("Sign", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Spec", "CRY-23")]
    public void Apis_TakeTypedKeys_NotStrings()
    {
        Assert.Equal(typeof(DidKey), typeof(VerificationKeyCache).GetMethod(nameof(VerificationKeyCache.Verify))!.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(DidKey), typeof(VerificationKey).GetProperty(nameof(VerificationKey.DidKey))!.PropertyType);
        Assert.Equal(typeof(VerificationKey), typeof(SigningKey).GetProperty(nameof(SigningKey.PublicKey))!.PropertyType);
    }

    [Fact]
    [Trait("Spec", "CRY-7")]
    public void Generate_P256_UsesAFreshScalarEachTime()
    {
        using var first = SigningKey.Generate(KeyCurve.P256);
        using var second = SigningKey.Generate(KeyCurve.P256);

        Assert.False(CryptographicOperations.FixedTimeEquals(first.ExportPkcs8(), second.ExportPkcs8()));
    }
}
