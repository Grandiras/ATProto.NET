using System.Text;
using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.TestKit;

namespace ATProtoNet.Tests.Crypto;

/// <summary><c>did:key</c>, multikeys and the point encodings behind them.</summary>
public sealed class DidKeyTests
{
    public static TheoryData<KeyCurve> Curves() => [KeyCurve.P256, KeyCurve.K256];

    [Theory]
    [InlineData(TestKeys.P256DidKey, KeyCurve.P256)]
    [InlineData(TestKeys.K256DidKey, KeyCurve.K256)]
    [Trait("Spec", "CRY-20"), Trait("Spec", "CRY-21")]
    public void Parse_TestKey_ExposesCurveAndMultikey(string text, KeyCurve curve)
    {
        var key = DidKey.Parse(text);

        Assert.Equal(curve, key.Curve);
        Assert.Equal(text, key.Value);
        Assert.Equal(text["did:key:".Length..], key.Multikey);
        Assert.Equal(key, DidKey.FromMultikey(key.Multikey));
        Assert.Equal(33, key.CompressedPublicKey.Length);
    }

    [Theory]
    [MemberData(nameof(Curves))]
    [Trait("Spec", "CRY-21")]
    public void Parse_GeneratedKeys_RoundTripForBothParities(KeyCurve curve)
    {
        var parities = new HashSet<byte>();
        for (var i = 0; i < 32 && parities.Count < 2; i++)
        {
            using var key = SigningKey.Generate(curve);
            var didKey = key.PublicKey.DidKey;
            parities.Add(didKey.CompressedPublicKey[0]);

            var parsed = DidKey.Parse(didKey.Value);
            Assert.Equal(didKey.CompressedPublicKey, parsed.CompressedPublicKey);
            Assert.Equal(key.PublicKey, parsed.ToVerificationKey());
            Assert.True(parsed.ToVerificationKey().Verify("m"u8, key.Sign("m"u8)));
        }
        Assert.Equal(2, parities.Count);
    }

    [Theory]
    [InlineData("did:web:example.com")]
    [InlineData("did:key:")]
    [InlineData("did:key:zInvalid0Base58")]
    [InlineData("did:key:mDnaeTiq1PdzvZXUaMdezchcMJQpBdH2VN4pgrrEhMCCbmwSb")]
    [InlineData("did:key:z6MkhaXgBZDvotDkL5257faiztiGiC2QtKLGpbnnEGta2doK")] // Ed25519: not an atproto curve
    [InlineData(TestKeys.P256DidKey + "\n")]
    [InlineData(" " + TestKeys.P256DidKey)]
    [Trait("Spec", "CRY-20"), Trait("Spec", "CRY-21")]
    public void Parse_Invalid_ThrowsFormatExceptionAndTryParseFails(string text)
    {
        Assert.Throws<FormatException>(() => DidKey.Parse(text));
        Assert.False(DidKey.TryParse(text, out _));
        Assert.False(DidKey.TryParse(text.AsSpan(), out _));
        Assert.False(DidKey.TryParse(Encoding.UTF8.GetBytes(text), out _));
    }

    [Fact]
    [Trait("Spec", "CRY-21")]
    public void TryParse_AllThreeForms_AcceptTheSameKey()
    {
        Assert.True(DidKey.TryParse(TestKeys.P256DidKey, out var fromString));
        Assert.True(DidKey.TryParse(TestKeys.P256DidKey.AsSpan(), out var fromChars));
        Assert.True(DidKey.TryParse(Encoding.UTF8.GetBytes(TestKeys.P256DidKey), out var fromUtf8));
        Assert.Equal(fromString, fromChars);
        Assert.Equal(fromString, fromUtf8);
        Assert.Throws<ArgumentNullException>(() => DidKey.Parse((string)null!));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(5)]
    [Trait("Spec", "CRY-20")]
    public void FromMultikey_WrongKeyLength_ReportsTheLength(int extraBytes)
    {
        byte[] valid = [0x80, 0x24, .. DidKey.Parse(TestKeys.P256DidKey).CompressedPublicKey];
        var bytes = extraBytes < 0 ? valid[..^1] : [.. valid, .. new byte[extraBytes]];

        var error = Assert.Throws<FormatException>(() => DidKey.FromMultikey("z" + Base58Btc.Encode(bytes)));
        Assert.Contains("33-byte", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("mAQID")]
    [InlineData("f0102")]
    [Trait("Spec", "CRY-20")]
    public void FromMultikey_OtherMultibase_ThrowsFormatException(string multikey) =>
        Assert.Throws<FormatException>(() => DidKey.FromMultikey(multikey));

    [Theory]
    [InlineData(KeyCurve.P256, 1)] // x³ - 3x + b is not a square mod p
    [InlineData(KeyCurve.K256, 5)] // x³ + 7 is not a square mod p
    [Trait("Spec", "CRY-5")]
    public void FromCompressedPoint_XWithNoPointOnTheCurve_ThrowsFormatException(KeyCurve curve, byte x)
    {
        var point = new byte[33];
        (point[0], point[32]) = (0x02, x);

        Assert.Throws<FormatException>(() => VerificationKey.FromCompressedPoint(curve, point));
    }

    [Fact]
    [Trait("Spec", "CRY-5")]
    public void FromCompressedPoint_XNotBelowTheFieldPrime_ThrowsFormatException()
    {
        var point = new byte[33];
        point[0] = 0x03;
        point.AsSpan(1).Fill(0xFF);

        Assert.Throws<FormatException>(() => VerificationKey.FromCompressedPoint(KeyCurve.P256, point));
    }

    [Theory]
    [InlineData(0x04, 33)]
    [InlineData(0x00, 33)]
    [InlineData(0x02, 32)]
    [InlineData(0x02, 34)]
    [Trait("Spec", "CRY-5")]
    public void FromCompressedPoint_BadPrefixOrLength_ThrowsFormatException(byte prefix, int length)
    {
        var point = new byte[length];
        DidKey.Parse(TestKeys.P256DidKey).CompressedPublicKey[..Math.Min(33, length)].CopyTo(point);
        point[0] = prefix;

        Assert.Throws<FormatException>(() => VerificationKey.FromCompressedPoint(KeyCurve.P256, point));
    }

    [Theory]
    [MemberData(nameof(Curves))]
    [Trait("Spec", "CRY-6")]
    public void FromUncompressedPoint_IsTheSameKeyAsItsCompressedForm(KeyCurve curve)
    {
        using var key = SigningKey.Generate(curve);
        var jwk = key.PublicKey.ToJsonWebKey();
        byte[] uncompressed = [0x04, .. Base64Url(jwk.X), .. Base64Url(jwk.Y)];

        var fromUncompressed = VerificationKey.FromUncompressedPoint(curve, uncompressed);

        Assert.Equal(key.PublicKey, fromUncompressed);
        Assert.Equal(key.PublicKey, VerificationKey.FromCompressedPoint(curve, key.PublicKey.DidKey.CompressedPublicKey));
        Assert.Equal(key.PublicKey.DidKey, fromUncompressed.DidKey);
    }

    [Fact]
    [Trait("Spec", "CRY-6")]
    public void FromUncompressedPoint_OtherEncodingOrOffCurve_ThrowsFormatException()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var jwk = key.PublicKey.ToJsonWebKey();
        byte[] uncompressed = [0x04, .. Base64Url(jwk.X), .. Base64Url(jwk.Y)];
        var offCurve = (byte[])uncompressed.Clone();
        offCurve[64] ^= 1;

        Assert.Throws<FormatException>(() => VerificationKey.FromUncompressedPoint(KeyCurve.P256, [0x06, .. uncompressed[1..]]));
        Assert.Throws<FormatException>(() => VerificationKey.FromUncompressedPoint(KeyCurve.P256, key.PublicKey.DidKey.CompressedPublicKey));
        Assert.Throws<FormatException>(() => VerificationKey.FromUncompressedPoint(KeyCurve.P256, offCurve));
    }

    [Fact]
    [Trait("Spec", "CRY-1")]
    public void Curves_AreExactlyTheTwoAtprotoCurves() =>
        Assert.Equal([KeyCurve.P256, KeyCurve.K256], Enum.GetValues<KeyCurve>());

    private static byte[] Base64Url(string text) => System.Buffers.Text.Base64Url.DecodeFromChars(text);
}
