using System.Numerics;
using System.Runtime.CompilerServices;
using ATProtoNet.Crypto;

namespace ATProtoNet.Tests.Crypto;

/// <summary>Verification: the signature form, low-S, scalar ranges, and the managed fallback for curves the platform lacks.</summary>
public sealed class SignatureTests
{
    internal const string P256OrderHex = "FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551";
    internal const string K256OrderHex = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141";
    internal static readonly BigInteger P256Order = new(Convert.FromHexString(P256OrderHex), isUnsigned: true, isBigEndian: true);
    internal static readonly BigInteger K256Order = new(Convert.FromHexString(K256OrderHex), isUnsigned: true, isBigEndian: true);

    private static readonly byte[] Message = "service auth"u8.ToArray();

    public static TheoryData<KeyCurve> Curves() => [KeyCurve.P256, KeyCurve.K256];

    public static TheoryData<KeyCurve, int, string> OutOfRangeScalars()
    {
        var data = new TheoryData<KeyCurve, int, string>();
        foreach (var (curve, order) in new[] { (KeyCurve.P256, P256OrderHex), (KeyCurve.K256, K256OrderHex) })
            foreach (var index in new[] { 0, 1 })
            {
                data.Add(curve, index, new string('0', 64));
                data.Add(curve, index, order);
                data.Add(curve, index, new string('F', 64));
            }
        return data;
    }

    public static TheoryData<string> Fixtures() => [.. Fixture("signature-fixtures.json"), .. Fixture("signature-scalar-range.json")];

    [Theory]
    [MemberData(nameof(Curves))]
    [Trait("Spec", "CRY-11")]
    public void Verify_TamperedMessageOrSignature_IsFalse(KeyCurve curve)
    {
        using var key = SigningKey.Generate(curve);
        var signature = key.Sign(Message);
        var tampered = (byte[])signature.Clone();
        tampered[10] ^= 1;

        Assert.False(key.PublicKey.Verify("another message"u8, signature));
        Assert.False(key.PublicKey.Verify(Message, tampered));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(72)]
    [Trait("Spec", "CRY-11"), Trait("Spec", "CRY-14")]
    public void Verify_SignatureOfTheWrongLength_ReturnsFalse(int length)
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var resized = new byte[length];
        key.Sign(Message).AsSpan(0, Math.Min(length, 64)).CopyTo(resized);

        Assert.False(key.PublicKey.Verify(Message, resized));
        Assert.False(key.PublicKey.Verify(Message, resized, SignaturePolicy.AcceptHighS));
    }

    [Theory]
    [MemberData(nameof(Curves))]
    [Trait("Spec", "CRY-11"), Trait("Spec", "CRY-12")]
    public void Verify_HighS_OnlyUnderAcceptHighS(KeyCurve curve)
    {
        using var key = SigningKey.Generate(curve);
        var highS = HighS(key.Sign(Message), curve);

        Assert.False(key.PublicKey.Verify(Message, highS));
        Assert.True(key.PublicKey.Verify(Message, highS, SignaturePolicy.AcceptHighS));
    }

    [Theory]
    [MemberData(nameof(OutOfRangeScalars))]
    [Trait("Spec", "CRY-11"), Trait("Spec", "CRY-12"), Trait("Spec", "CRY-13")]
    public void Verify_ScalarOutOfRange_IsFalseRatherThanThrowing(KeyCurve curve, int index, string scalar)
    {
        // Regression (0.8): an S of n or more made high-S normalisation compute n - S < 0 and throw OverflowException.
        using var key = SigningKey.Generate(curve);
        var signature = key.Sign(Message);
        Convert.FromHexString(scalar).CopyTo(signature, index * 32);

        Assert.False(key.PublicKey.Verify(Message, signature));
        Assert.False(key.PublicKey.Verify(Message, signature, SignaturePolicy.AcceptHighS));
        Assert.False(VerifyManaged(key.PublicKey, Message, signature, SignaturePolicy.AcceptHighS));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    [Trait("Spec", "CRY-15"), Trait("Spec", "CRY-16")]
    public void VerifyManaged_InteropFixture_MatchesExpectedValidity(string name)
    {
        var (file, id) = (name[..name.IndexOf('#')], name[(name.IndexOf('#') + 1)..]);
        var fixture = CryptoVectors.Json($"crypto/{file}").AsArray().Single(f => (string?)(f!["comment"] ?? f["name"]) == id)!;
        var key = DidKey.Parse((string)fixture["publicKeyDid"]!).ToVerificationKey();
        var (message, signature) = (CryptoVectors.Base64((string)fixture["messageBase64"]!), CryptoVectors.Base64((string)fixture["signatureBase64"]!));

        Assert.Equal((bool)fixture["validSignature"]!, VerifyManaged(key, message, signature, SignaturePolicy.LowSOnly));
        Assert.Equal((bool)fixture["validSignature"]!, key.Verify(message, signature));
    }

    [Theory]
    [MemberData(nameof(Curves))]
    [Trait("Spec", "CRY-16")]
    public void VerifyManaged_RandomSignatures_AgreesWithThePlatform(KeyCurve curve)
    {
        for (var i = 0; i < 8; i++)
        {
            using var key = SigningKey.Generate(curve);
            var message = BitConverter.GetBytes(i);
            var signature = key.Sign(message);
            var other = key.Sign("other"u8);

            Assert.True(VerifyManaged(key.PublicKey, message, signature, SignaturePolicy.LowSOnly));
            Assert.True(VerifyManaged(key.PublicKey, message, HighS(signature, curve), SignaturePolicy.AcceptHighS));
            Assert.False(VerifyManaged(key.PublicKey, message, HighS(signature, curve), SignaturePolicy.LowSOnly));
            Assert.False(VerifyManaged(key.PublicKey, message, other, SignaturePolicy.LowSOnly));
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "VerifyManaged")]
    private static extern bool VerifyManaged(VerificationKey key, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, SignaturePolicy policy);

    internal static byte[] HighS(byte[] signature, KeyCurve curve)
    {
        var order = curve == KeyCurve.P256 ? P256Order : K256Order;
        var high = (byte[])signature.Clone();
        var s = order - new BigInteger(signature.AsSpan(32), isUnsigned: true, isBigEndian: true);
        high.AsSpan(32).Clear();
        s.TryWriteBytes(high.AsSpan(64 - s.GetByteCount(isUnsigned: true)), out _, isUnsigned: true, isBigEndian: true);
        return high;
    }

    private static IEnumerable<string> Fixture(string file) =>
        CryptoVectors.Json($"crypto/{file}").AsArray().Select(f => $"{file}#{(string?)(f!["comment"] ?? f["name"])}");
}
