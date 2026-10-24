using System.Buffers.Text;
using System.Text;
using ATProtoNet.Crypto;
using ATProtoNet.Spaces;
using ATProtoNet.TestKit;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Spaces;

// Delegation tokens, the type and kid checks every token shares, malformed tokens, time ranges and signatures.
public sealed partial class SpaceTokenTests
{
    [Theory]
    [MemberData(nameof(Curves))]
    [Trait("Spec", "SPC-18"), Trait("Spec", "SPC-19"), Trait("Spec", "SPC-21")]
    public void Parse_Delegation_ReadsItsClaimsAndVerifiesAgainstTheUsersKey(KeyCurve curve)
    {
        using var user = SigningKey.Generate(curve);

        var token = SpaceToken.Parse(Signed(user, SpaceTokenType.Delegation, ShortTokenJson(User, Space)), SpaceTokenType.Delegation, new FakeTimeProvider(Now));

        Assert.Equal((SpaceTokenType.Delegation, User, Space, Host, "#atproto"), (token.Type, token.Issuer, token.Subject, token.Audience, token.KeyId));
        Assert.Equal((Now, Now.AddSeconds(60), Jti), (token.IssuedAt, token.ExpiresAt, token.Jti));
        Assert.Null(token.ConfirmationKey);
        Assert.True(token.VerifySignature(user.PublicKey));
    }

    [Theory]
    [Trait("Spec", "SPC-19"), Trait("Spec", "SPC-20")]
    [InlineData("-aud", SpaceTokenError.BadJwtAudience)]
    [InlineData("aud=\"\"", SpaceTokenError.BadJwtAudience)]
    [InlineData("aud=[\"" + Host + "\"]", SpaceTokenError.BadJwtAudience)]
    [InlineData("-iss", SpaceTokenError.BadJwtIss)]
    [InlineData("-sub", SpaceTokenError.BadJwtSub)]
    [InlineData("-exp", SpaceTokenError.BadJwt)]
    [InlineData("-jti", SpaceTokenError.BadJwt)]
    [InlineData("exp=253402300800", SpaceTokenError.BadJwt)] // 10000-01-01, a second past what DateTimeOffset holds
    [InlineData("exp=9223372036854775807", SpaceTokenError.BadJwt)]
    [InlineData("exp=-1", SpaceTokenError.BadJwt)]
    [InlineData("exp=1e30", SpaceTokenError.BadJwt)]
    [InlineData("iat=253402300800", SpaceTokenError.BadJwt)]
    [InlineData("iat=-62135596801", SpaceTokenError.BadJwt)] // a second before 0001-01-01
    [InlineData("iat=\"now\"", SpaceTokenError.BadJwt)]
    public void Parse_DelegationWithABadClaim_IsRefusedWithItsError(string change, SpaceTokenError expected)
    {
        // Regression (0.8 #214): an out-of-range time threw ArgumentOutOfRangeException, a 500 instead of a refusal.
        using var user = SigningKey.Generate(KeyCurve.P256);

        AssertRefused(expected, Signed(user, SpaceTokenType.Delegation, Changed(ShortTokenJson(User, Space), change)), SpaceTokenType.Delegation);
    }

    [Theory]
    [Trait("Spec", "SPC-19")]
    [InlineData("jti=\" \"")]
    [InlineData("jti=\"\\t\\t\"")]
    [InlineData("jti=\"a\\u0000b\"")]
    [InlineData("jti=\"" + Jti + Jti + Jti + Jti + Jti + Jti + Jti + Jti + "\"")] // 256 characters
    public void Parse_JtiThatCannotBeSpent_ThrowsBadJwt(string change)
    {
        // A replay store cannot key on it, so it must be refused here rather than surface there.
        using var user = SigningKey.Generate(KeyCurve.P256);

        AssertRefused(SpaceTokenError.BadJwt, Signed(user, SpaceTokenType.Delegation, Changed(ShortTokenJson(User, Space), change)), SpaceTokenType.Delegation);
    }

    [Fact]
    [Trait("Spec", "SPC-20")]
    public void Parse_TheLastRepresentableSecond_IsAccepted()
    {
        using var user = SigningKey.Generate(KeyCurve.P256);

        var token = SpaceToken.Parse(Signed(user, SpaceTokenType.Delegation, Changed(ShortTokenJson(User, Space), "exp=253402300799")), SpaceTokenType.Delegation);

        Assert.Equal(DateTimeOffset.MaxValue.ToUnixTimeSeconds(), token.ExpiresAt.ToUnixTimeSeconds());
    }

    [Theory]
    [Trait("Spec", "SPC-19")]
    [InlineData(-1, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void Parse_DelegationPastItsExpiry_IsExpiredBeyondTheSkew(int secondsPastExpiry, bool accepted)
    {
        using var user = SigningKey.Generate(KeyCurve.P256);
        var jwt = Signed(user, SpaceTokenType.Delegation, ShortTokenJson(User, Space));
        var time = new FakeTimeProvider(Now.AddSeconds(60 + secondsPastExpiry));

        if (accepted)
            Assert.NotNull(SpaceToken.Parse(jwt, SpaceTokenType.Delegation, time));
        else
            AssertRefused(SpaceTokenError.JwtExpired, jwt, SpaceTokenType.Delegation, time);
    }

    [Theory]
    [Trait("Spec", "SPC-19")]
    [InlineData("sub=\"https://other.example/x\"", SpaceTokenError.BadJwtIss)]
    [InlineData("-iss", SpaceTokenError.BadJwtIss)]
    [InlineData("-sub", SpaceTokenError.BadJwtSub)]
    [InlineData("-aud", SpaceTokenError.BadJwtAudience)]
    public void Parse_AttestationWithABadClaim_IsRefusedWithItsError(string change, SpaceTokenError expected)
    {
        using var client = SigningKey.Generate(KeyCurve.P256);

        AssertRefused(expected, Signed(client, SpaceTokenType.ClientAttestation, Changed(ShortTokenJson(ClientId, ClientId), change)), SpaceTokenType.ClientAttestation);
    }

    [Theory]
    [Trait("Spec", "SPC-18")]
    [InlineData(SpaceTokenType.Delegation, SpaceTokenType.Credential)]
    [InlineData(SpaceTokenType.Credential, SpaceTokenType.Delegation)]
    [InlineData(SpaceTokenType.ClientAttestation, SpaceTokenType.Delegation)]
    [InlineData(SpaceTokenType.Delegation, SpaceTokenType.ClientAttestation)]
    public void Parse_TokenOfAnotherType_ThrowsBadJwtType(SpaceTokenType actual, SpaceTokenType expected)
    {
        // The types share a wire shape, so typ alone keeps a credential from being presented as a delegation token.
        using var key = SigningKey.Generate(KeyCurve.P256);
        var claims = actual is SpaceTokenType.Credential ? CredentialJson(Now) : ShortTokenJson(ClientId, ClientId);

        AssertRefused(SpaceTokenError.BadJwtType, Signed(key, actual, claims), expected);
    }

    [Theory]
    [Trait("Spec", "SPC-18")]
    [InlineData(null)]
    [InlineData("JWT")]
    [InlineData("atproto-space-credential+JWT")]
    public void Parse_CredentialWithAnotherTyp_ThrowsBadJwtType(string? typ)
    {
        using var key = SigningKey.Generate(KeyCurve.P256);

        AssertRefused(SpaceTokenError.BadJwtType, Signed(key, SpaceTokenType.Credential, CredentialJson(Now), typ: typ), SpaceTokenType.Credential);
    }

    [Theory]
    [Trait("Spec", "SPC-19")]
    [InlineData(SpaceTokenType.Delegation, null)]
    [InlineData(SpaceTokenType.Delegation, "#other_key")]
    [InlineData(SpaceTokenType.Delegation, "atproto")] // 0.8 accepted it without the '#'
    [InlineData(SpaceTokenType.Delegation, "#atproto_space")]
    [InlineData(SpaceTokenType.Credential, null)]
    [InlineData(SpaceTokenType.Credential, "did:example:space#atproto")] // DID-qualified
    [InlineData(SpaceTokenType.Credential, "#atproto_label")]
    public void Parse_KidOtherThanTheSigningKey_ThrowsBadJwt(SpaceTokenType type, string? kid)
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var claims = type is SpaceTokenType.Credential ? CredentialJson(Now) : ShortTokenJson(User, Space);

        AssertRefused(SpaceTokenError.BadJwt, Signed(key, type, claims, kid: kid), type);
    }

    [Theory]
    [Trait("Spec", "SPC-21")]
    [InlineData("nope")]
    [InlineData("aaa.bbb")]
    [InlineData("a.b.c.d")]
    [InlineData("!!!.e30.c2ln")]
    [InlineData("e30.WzFd.AAAA")] // a payload that is not an object
    [InlineData("")]
    public void Parse_MalformedToken_ThrowsBadJwt(string jwt) => AssertRefused(SpaceTokenError.BadJwt, jwt, SpaceTokenType.Credential);

    [Fact]
    [Trait("Spec", "SPC-21")]
    public void Parse_UnsignedOrShortSignature_ThrowsBadJwt()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var jwt = Signed(key, SpaceTokenType.Credential, CredentialJson(Now));

        AssertRefused(SpaceTokenError.BadJwt, jwt[..(jwt.LastIndexOf('.') + 1)], SpaceTokenType.Credential);
        AssertRefused(SpaceTokenError.BadJwt, $"{jwt[..jwt.LastIndexOf('.')]}.c2ln", SpaceTokenType.Credential);
    }

    [Fact]
    public void Parse_NullToken_ThrowsArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => SpaceToken.Parse(null!, SpaceTokenType.Credential));

    [Fact]
    [Trait("Spec", "SPC-21")]
    public void VerifySignature_AnotherKey_IsFalse()
    {
        using var authority = SigningKey.ImportRawPrivateScalar(Convert.FromHexString(TestKeys.K256PrivateKeyHex), KeyCurve.K256);
        using var other = SigningKey.Generate(KeyCurve.K256);
        using var p256 = SigningKey.ImportRawPrivateScalar(Convert.FromHexString(TestKeys.P256PrivateKeyHex), KeyCurve.P256);

        var token = SpaceToken.Parse(SpaceToken.CreateCredential(CredentialClaims(Now), authority), SpaceTokenType.Credential);

        Assert.True(token.VerifySignature(DidKey.Parse(TestKeys.K256DidKey).ToVerificationKey()));
        Assert.False(token.VerifySignature(other.PublicKey));
        // alg is bound to the key's curve: an ES256K token never verifies against a P-256 key.
        Assert.False(token.VerifySignature(p256.PublicKey));
    }

    [Fact]
    [Trait("Spec", "SPC-21")]
    public void VerifySignature_TamperedPayload_IsFalse()
    {
        using var user = SigningKey.Generate(KeyCurve.P256);
        var parts = Signed(user, SpaceTokenType.Delegation, ShortTokenJson(User, Space)).Split('.');
        var forged = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(ShortTokenJson("did:example:mallory", Space).ToJsonString()));

        var token = SpaceToken.Parse($"{parts[0]}.{forged}.{parts[2]}", SpaceTokenType.Delegation);

        Assert.Equal("did:example:mallory", token.Issuer);
        Assert.False(token.VerifySignature(user.PublicKey));
    }

    [Fact]
    [Trait("Spec", "SPC-21")]
    public void VerifySignature_AttestationSignedByTheClient_IsTrueOnlyForTheClientKey()
    {
        using var client = SigningKey.Generate(KeyCurve.K256);
        using var other = SigningKey.Generate(KeyCurve.K256);

        var token = SpaceToken.Parse(SpaceToken.CreateClientAttestation(AttestationClaims(), client), SpaceTokenType.ClientAttestation);

        Assert.True(token.VerifySignature(client.PublicKey));
        Assert.False(token.VerifySignature(other.PublicKey));
    }
}
