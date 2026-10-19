using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Crypto.Jose;

namespace ATProtoNet.Tests.Crypto.Jose;

/// <summary>EC JSON Web Keys: conversion, RFC 7638 thumbprints, and keeping <c>d</c> private.</summary>
public sealed class JsonWebKeyTests
{
    // RFC 9449 §4.1 (figure 2) embeds this key; §6.1 gives its thumbprint.
    private static JsonWebKey RfcKey() => new()
    {
        Kty = "EC",
        Crv = "P-256",
        X = "l8tFrhx-34tV3hRICRDY9zCkDlpBhF42UQUfWVAWBFs",
        Y = "9VE4jf_Ok_o64zbTTlcuNJajHmt6v9TDVrU0CdvGRDA",
    };

    [Fact]
    [Trait("Spec", "CRY-36")]
    public void Thumbprint_Rfc9449Key_MatchesTheRfc() =>
        Assert.Equal("0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I", RfcKey().Thumbprint());

    [Fact]
    [Trait("Spec", "CRY-36")]
    public void Thumbprint_IgnoresMembersRfc7638DoesNotRequire() =>
        Assert.Equal(RfcKey().Thumbprint(), (RfcKey() with { Kid = "key-1", Use = "sig", Alg = "ES256", D = "c2VjcmV0" }).Thumbprint());

    [Theory]
    [InlineData(KeyCurve.P256, "P-256")]
    [InlineData(KeyCurve.K256, "secp256k1")]
    [Trait("Spec", "CRY-35")]
    public void ToJsonWebKey_RoundTripsThroughFromJsonWebKey(KeyCurve curve, string crv)
    {
        using var key = SigningKey.Generate(curve);

        var jwk = key.PublicKey.ToJsonWebKey();

        Assert.Equal(("EC", crv, 43, 43, null), (jwk.Kty, jwk.Crv, jwk.X.Length, jwk.Y.Length, jwk.D));
        Assert.Equal(key.PublicKey, VerificationKey.FromJsonWebKey(jwk));
    }

    public static TheoryData<JsonWebKey> InvalidKeys() =>
    [
        RfcKey() with { Kty = "RSA" },
        RfcKey() with { Crv = "P-384" },
        RfcKey() with { Crv = "Ed25519" },
        RfcKey() with { X = RfcKey().X + "=" },                  // padded
        RfcKey() with { X = RfcKey().X[..^1] },                  // 31 bytes
        RfcKey() with { Y = RfcKey().Y.Replace('_', '/') },      // the standard alphabet
        RfcKey() with { Y = RfcKey().X },                        // not on the curve
        RfcKey() with { Crv = "secp256k1" },                     // a P-256 point on the wrong curve
        RfcKey() with { Kty = null!, Crv = null! },              // explicit JSON nulls satisfy required
        RfcKey() with { X = null!, Y = null! },
    ];

    [Theory]
    [MemberData(nameof(InvalidKeys))]
    [Trait("Spec", "CRY-35")]
    public void FromJsonWebKey_NotAValidEcKey_ThrowsFormatException(JsonWebKey jwk) =>
        Assert.Throws<FormatException>(() => VerificationKey.FromJsonWebKey(jwk));

    [Fact]
    [Trait("Spec", "CRY-37")]
    public void PrivateMember_DroppedByToPublicAndNeverShown()
    {
        var jwk = RfcKey() with { D = "c2VjcmV0LXNjYWxhcg" };

        Assert.Null(jwk.ToPublic().D);
        Assert.DoesNotContain(jwk.D, jwk.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"d\"", JsonSerializer.Serialize(jwk.ToPublic()), StringComparison.Ordinal);
        Assert.Contains("\"d\"", JsonSerializer.Serialize(jwk), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "CRY-37")]
    public void JsonWebKeySet_HoldsPublicKeysOnly()
    {
        var set = new JsonWebKeySet([RfcKey() with { D = "c2VjcmV0", Kid = "k" }]);

        var json = JsonSerializer.Serialize(set);

        Assert.Null(Assert.Single(set.Keys).D);
        Assert.Equal("""{"keys":[{"kty":"EC","crv":"P-256","x":"l8tFrhx-34tV3hRICRDY9zCkDlpBhF42UQUfWVAWBFs","y":"9VE4jf_Ok_o64zbTTlcuNJajHmt6v9TDVrU0CdvGRDA","kid":"k"}]}""", json);
        Assert.Equal(set.Keys, JsonSerializer.Deserialize<JsonWebKeySet>(json)!.Keys);
    }

    [Fact]
    [Trait("Spec", "CRY-38")]
    public void JoseTypes_LiveInTheFoundationNamespace()
    {
        Assert.Equal("ATProtoNet.Crypto.Jose", typeof(JsonWebKey).Namespace);
        Assert.Equal("ATProtoNet.Crypto.Jose", typeof(Jws).Namespace);
    }
}
