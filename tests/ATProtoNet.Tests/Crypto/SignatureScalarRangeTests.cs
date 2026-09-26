using ATProtoNet.Crypto;

namespace ATProtoNet.Tests.Crypto;

/// <summary>
/// Signatures whose <c>r</c> or <c>s</c> lies outside [1, n − 1]: attacker-chosen bytes that no
/// signer produces, and that every verification path must answer with <see langword="false"/>.
/// </summary>
public sealed class SignatureScalarRangeTests
{
    private const string P256Order = "FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551";
    private const string K256Order = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141";

    private static readonly byte[] Message = "service auth"u8.ToArray();

    public static TheoryData<KeyCurve, int, string> OutOfRangeScalars()
    {
        var data = new TheoryData<KeyCurve, int, string>();
        foreach (var (curve, order) in new[] { (KeyCurve.P256, P256Order), (KeyCurve.K256, K256Order) })
        {
            foreach (var index in new[] { 0, 1 }) // r, then s
            {
                data.Add(curve, index, new string('0', 64));
                data.Add(curve, index, order);
                data.Add(curve, index, new string('F', 64));
            }
        }

        return data;
    }

    private static AtProtoKey Generate(KeyCurve curve) =>
        curve == KeyCurve.P256 ? AtProtoCrypto.GenerateP256Key() : AtProtoCrypto.GenerateK256Key();

    private static byte[] WithScalar(byte[] signature, int index, string hex)
    {
        var copy = (byte[])signature.Clone();
        Convert.FromHexString(hex).CopyTo(copy, index * 32);
        return copy;
    }

    [Theory]
    [MemberData(nameof(OutOfRangeScalars))]
    public void VerifyJwtSignature_ScalarOutOfRange_IsFalseRatherThanThrowing(KeyCurve curve, int index, string scalar)
    {
        // Regression: an S of n or more made the high-S normalization compute n - S < 0, and
        // BigInteger.ToByteArray(isUnsigned: true) threw OverflowException — a 500 for any
        // forged token naming a resolvable issuer.
        using var key = Generate(curve);
        var signature = WithScalar(key.Sign(Message), index, scalar);

        Assert.False(AtProtoCrypto.VerifyJwtSignature(key.ToDidKey(), curve.JwsAlgorithm(), Message, signature));
    }

    [Theory]
    [MemberData(nameof(OutOfRangeScalars))]
    public void VerifySignature_ScalarOutOfRange_IsFalseRatherThanThrowing(KeyCurve curve, int index, string scalar)
    {
        // The commit, label and space-token path: low-S only, so no normalization, but the
        // platform verifier is never handed a scalar out of range either.
        using var key = Generate(curve);
        var signature = WithScalar(key.Sign(Message), index, scalar);

        Assert.False(AtProtoCrypto.HasScalarsInRange(signature, curve));
        Assert.False(AtProtoCrypto.VerifySignature(key.ToDidKey(), Message, signature));
        Assert.False(key.Verify(Message, signature));
    }

    [Theory]
    [InlineData(KeyCurve.P256)]
    [InlineData(KeyCurve.K256)]
    public void HasScalarsInRange_ARealSignature_IsTrue(KeyCurve curve)
    {
        using var key = Generate(curve);

        Assert.True(AtProtoCrypto.HasScalarsInRange(key.Sign(Message), curve));
    }

    [Theory]
    [InlineData(KeyCurve.P256, P256Order)]
    [InlineData(KeyCurve.K256, K256Order)]
    [InlineData(KeyCurve.K256, "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")]
    public void NormalizeLowSSignature_SOfTheOrderOrMore_IsLeftForVerificationToRefuse(KeyCurve curve, string s)
    {
        var signature = WithScalar(new byte[64], 1, s);

        Assert.Equal(signature, AtProtoCrypto.NormalizeLowSSignature(signature, curve));
    }
}
