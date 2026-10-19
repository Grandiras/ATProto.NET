using ATProtoNet.Crypto;
using ATProtoNet.Data;

namespace ATProtoNet.Tests.Crypto;

public class SignedObjectTests
{
    private static readonly DataModelMap Commit = (DataModelMap)DataModelValue.FromJson("""{"did":"did:plc:abc","rev":"3k2a","version":3}"""u8);

    [Theory]
    [Trait("Spec", "CRY-40")]
    [Trait("Spec", "CRY-41")]
    [Trait("Spec", "CRY-42")]
    [InlineData(KeyCurve.P256, SignatureEncoding.Bytes)]
    [InlineData(KeyCurve.K256, SignatureEncoding.Bytes)]
    [InlineData(KeyCurve.P256, SignatureEncoding.Base64Url)]
    public void Sign_ThenVerify_IsValidAndCoversTheMapWithoutSig(KeyCurve curve, SignatureEncoding encoding)
    {
        using var key = SigningKey.Generate(curve);

        var signed = SignedObject.Sign(Commit, key, encoding);

        Assert.True(SignedObject.Verify(signed, key.PublicKey, encoding));
        Assert.Equal(Commit.ToDrisl(), SignedDrisl.GetUnsignedBytes(signed, out var signature));
        Assert.IsType(encoding is SignatureEncoding.Bytes ? typeof(DataModelBytes) : typeof(DataModelString), signature);
    }

    [Fact]
    [Trait("Spec", "CRY-41")]
    public void Verify_OtherKeyTamperedOrWrongEncoding_IsFalse()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        using var other = SigningKey.Generate(KeyCurve.P256);
        var signed = SignedObject.Sign(Commit, key);
        var tampered = SignedObject.Sign((DataModelMap)DataModelValue.FromJson("""{"did":"did:plc:abd","rev":"3k2a","version":3}"""u8), key);
        SignedDrisl.GetUnsignedBytes(tampered, out var foreign);

        Assert.False(SignedObject.Verify(signed, other.PublicKey));
        Assert.False(SignedObject.Verify(signed, key.PublicKey, SignatureEncoding.Base64Url));
        Assert.False(SignedObject.Verify(SignedDrisl.AddSignature(Commit.ToDrisl(), foreign!), key.PublicKey));
    }

    [Theory]
    [Trait("Spec", "CRY-41")]
    [InlineData("""{"did":"did:plc:abc"}""")]
    [InlineData("""{"did":"did:plc:abc","sig":{"$bytes":""}}""")]
    [InlineData("""{"did":"did:plc:abc","sig":"AQID"}""")]
    [InlineData("""{"did":"did:plc:abc","sig":null}""")]
    public void Verify_MissingEmptyOrWronglyTypedSignature_IsFalse(string json)
    {
        using var key = SigningKey.Generate(KeyCurve.P256);

        Assert.False(SignedObject.Verify(DataModelValue.FromJson(System.Text.Encoding.UTF8.GetBytes(json)).ToDrisl(), key.PublicKey));
    }

    [Fact]
    [Trait("Spec", "CRY-41")]
    public void Verify_NotStrictDrisl_ThrowsFormatException()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);

        Assert.Throws<FormatException>(() => SignedObject.Verify(Convert.FromHexString("a2617401617301"), key.PublicKey));
    }

    [Fact]
    [Trait("Spec", "CRY-40")]
    public void Sign_MapAlreadySigned_ThrowsFormatException()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var signed = (DataModelMap)DataModelValue.FromDrisl(SignedObject.Sign(Commit, key));

        Assert.Throws<FormatException>(() => SignedObject.Sign(signed, key));
    }

    [Theory]
    [Trait("Spec", "CRY-41")]
    [InlineData("==")]
    [InlineData(" ")]
    [InlineData("\n")]
    [InlineData("*")]
    public void Verify_Base64UrlSignatureSpelledDifferently_IsFalse(string suffix)
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var unsigned = SignedDrisl.GetUnsignedBytes(SignedObject.Sign(Commit, key, SignatureEncoding.Base64Url), out var signature);
        var respelled = SignedDrisl.AddSignature(unsigned, new DataModelString(((DataModelString)signature!).Value + suffix));

        Assert.False(SignedObject.Verify(respelled, key.PublicKey, SignatureEncoding.Base64Url));
    }
}
