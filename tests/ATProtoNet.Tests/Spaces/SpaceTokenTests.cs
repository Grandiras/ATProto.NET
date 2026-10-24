using System.Text;
using System.Text.Json.Nodes;
using ATProtoNet.Crypto;
using ATProtoNet.Crypto.Jose;
using ATProtoNet.Spaces;
using ATProtoNet.TestKit;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Spaces;

/// <summary>
/// The three space JWTs: issuing credentials and attestations, and parsing all three. Delegation tokens are minted by
/// the user's PDS, never the SDK, so the tests sign them by hand; so too every token with a claim the issuers refuse.
/// </summary>
public sealed partial class SpaceTokenTests
{
    private const string User = "did:example:alice";
    private const string Authority = "did:example:space";
    private const string Space = "at://did:example:space/space/app.bsky.group/test";
    private const string Host = $"{Authority}#atproto_pds";
    private const string ClientId = "https://app.example.com/client-metadata.json";
    private const string Jti = "0123456789abcdef0123456789abcdef";

    private static readonly DateTimeOffset Now = TestIds.Now;

    public static TheoryData<KeyCurve> Curves() => [KeyCurve.P256, KeyCurve.K256];

    [Fact]
    [Trait("Spec", "SPC-18"), Trait("Spec", "SPC-19"), Trait("Spec", "SPC-20")]
    public void Constants_AreTheProtocolValues()
    {
        Assert.Equal("atproto-space-delegation+jwt", SpaceToken.TypeName(SpaceTokenType.Delegation));
        Assert.Equal("atproto-space-credential+jwt", SpaceToken.TypeName(SpaceTokenType.Credential));
        Assert.Equal("atproto-client-attestation+jwt", SpaceToken.TypeName(SpaceTokenType.ClientAttestation));
        Assert.Equal("#atproto", SpaceToken.SigningKeyId);
        Assert.Equal(TimeSpan.FromMinutes(10), SpaceToken.DefaultSpaceCredentialLifetime);
        Assert.Equal(TimeSpan.FromSeconds(60), SpaceToken.DefaultShortTokenLifetime);
        Assert.Equal(TimeSpan.FromMinutes(60), SpaceToken.MaxSpaceCredentialLifetime);
        Assert.Equal(TimeSpan.FromSeconds(5), SpaceToken.SpaceTokenClockSkew);
    }

    [Theory]
    [MemberData(nameof(Curves))]
    [Trait("Spec", "SPC-18"), Trait("Spec", "SPC-19"), Trait("Spec", "SPC-20"), Trait("Spec", "SPC-21")]
    public void CreateCredential_ThenParse_RoundTripsWithTenMinutesAndNoAudience(KeyCurve curve)
    {
        using var authority = SigningKey.Generate(curve);
        var time = new FakeTimeProvider(Now);

        var jwt = SpaceToken.CreateCredential(CredentialClaims(time.GetUtcNow()), authority);
        var token = SpaceToken.Parse(jwt, SpaceTokenType.Credential, time);

        Assert.True(Jws.TryDecode(jwt, out var jws, out var error), error);
        Assert.Equal(new JwsHeader { Algorithm = Jws.AlgorithmFor(curve), Type = "atproto-space-credential+jwt", KeyId = "#atproto" }, jws.Header);
        // The reference's payload order; a credential names no audience, since every host of the space accepts it.
        Assert.Equal(["iss", "sub", "cnf", "iat", "exp", "jti"], jws.Payload.EnumerateObject().Select(p => p.Name));
        Assert.Equal((SpaceTokenType.Credential, Authority, Space, null), (token.Type, token.Issuer, token.Subject, token.Audience));
        Assert.Equal(TestKeys.P256DidKey, token.ConfirmationKey?.Value);
        Assert.Equal((Now, Now.AddMinutes(10)), (token.IssuedAt, token.ExpiresAt));
        Assert.Equal("#atproto", token.KeyId);
        Assert.Matches("^[0-9a-f]{32}$", token.Jti);
        Assert.True(token.VerifySignature(authority.PublicKey));
    }

    [Theory]
    [Trait("Spec", "SPC-19"), Trait("Spec", "SPC-20")]
    [InlineData(1)]
    [InlineData(3600)]
    public void CreateCredential_LifetimeUpToSixtyMinutes_IsIssuedAndParses(int seconds)
    {
        using var authority = SigningKey.Generate(KeyCurve.P256);
        var time = new FakeTimeProvider(Now);

        var token = SpaceToken.Parse(SpaceToken.CreateCredential(CredentialClaims(Now) with { Lifetime = TimeSpan.FromSeconds(seconds) }, authority), SpaceTokenType.Credential, time);

        Assert.Equal(TimeSpan.FromSeconds(seconds), token.ExpiresAt - token.IssuedAt);
    }

    [Theory]
    [Trait("Spec", "SPC-19"), Trait("Spec", "SPC-20")]
    [InlineData(3601_000)] // 61 minutes and a second
    [InlineData(3660_000)] // 61 minutes
    [InlineData(3600_500)] // over the cap by half a second
    [InlineData(0)]
    [InlineData(-1_000)]
    [InlineData(500)] // under a second: it would be written as exp = iat, which no parser accepts
    public void CreateCredential_LifetimeOutsideTheBound_ThrowsArgumentException(int milliseconds)
    {
        using var authority = SigningKey.Generate(KeyCurve.P256);

        Assert.Throws<ArgumentException>(() => SpaceToken.CreateCredential(CredentialClaims(Now) with { Lifetime = TimeSpan.FromMilliseconds(milliseconds) }, authority));
    }

    [Fact]
    [Trait("Spec", "SPC-19")]
    public void CreateCredential_K256BoundKey_ThrowsArgumentException()
    {
        using var authority = SigningKey.Generate(KeyCurve.P256);

        Assert.Throws<ArgumentException>(() => SpaceToken.CreateCredential(CredentialClaims(Now) with { BoundKey = DidKey.Parse(TestKeys.K256DidKey) }, authority));
    }

    [Fact]
    [Trait("Spec", "SPC-19")]
    public void CreateCredential_EveryToken_CarriesAFresh128BitHexJti()
    {
        using var authority = SigningKey.Generate(KeyCurve.P256);

        var jtis = Enumerable.Range(0, 3).Select(_ => SpaceToken.Parse(SpaceToken.CreateCredential(CredentialClaims(Now), authority), SpaceTokenType.Credential).Jti).ToList();

        Assert.All(jtis, jti => Assert.Matches("^[0-9a-f]{32}$", jti));
        Assert.Equal(3, jtis.Distinct().Count());
    }

    [Fact]
    public void CreateCredential_NullArgument_ThrowsArgumentNullException()
    {
        using var authority = SigningKey.Generate(KeyCurve.P256);

        Assert.Throws<ArgumentNullException>(() => SpaceToken.CreateCredential(null!, authority));
        Assert.Throws<ArgumentNullException>(() => SpaceToken.CreateCredential(CredentialClaims(Now), null!));
    }

    [Theory]
    [Trait("Spec", "SPC-19"), Trait("Spec", "SPC-20")]
    [InlineData("-iat", SpaceTokenError.BadJwt)]
    [InlineData("iat=\"now\"", SpaceTokenError.BadJwt)]
    [InlineData("-jti", SpaceTokenError.BadJwt)]
    [InlineData("jti=\"\"", SpaceTokenError.BadJwt)]
    [InlineData("jti=123", SpaceTokenError.BadJwt)]
    [InlineData("-exp", SpaceTokenError.BadJwt)]
    [InlineData("exp=1.5", SpaceTokenError.BadJwt)]
    [InlineData("-iss", SpaceTokenError.BadJwtIss)]
    [InlineData("iss=\"\"", SpaceTokenError.BadJwtIss)]
    [InlineData("-sub", SpaceTokenError.BadJwtSub)]
    [InlineData("sub=7", SpaceTokenError.BadJwtSub)]
    [InlineData("-cnf", SpaceTokenError.BadJwtCnf)]
    [InlineData("cnf=\"" + TestKeys.P256DidKey + "\"", SpaceTokenError.BadJwtCnf)] // not an object
    [InlineData("cnf={}", SpaceTokenError.BadJwtCnf)]
    [InlineData("cnf={\"jkt\":\"" + TestKeys.P256DidKey + "\"}", SpaceTokenError.BadJwtCnf)] // the DPoP binding names no key
    [InlineData("cnf={\"kid\":\"not a did\"}", SpaceTokenError.BadJwtCnf)]
    [InlineData("cnf={\"kid\":\"did:plc:ewvi7nxzyoun6zhxrhs64oiz\"}", SpaceTokenError.BadJwtCnf)] // a DID, but not a key
    [InlineData("cnf={\"kid\":\"" + TestKeys.K256DidKey + "\"}", SpaceTokenError.BadJwtCnf)] // HTTP signatures are P-256 only
    public void Parse_CredentialWithABadClaim_IsRefusedWithItsError(string change, SpaceTokenError expected)
    {
        using var authority = SigningKey.Generate(KeyCurve.K256);

        AssertRefused(expected, Signed(authority, SpaceTokenType.Credential, Changed(CredentialJson(Now), change)), SpaceTokenType.Credential);
    }

    [Theory]
    [Trait("Spec", "SPC-19"), Trait("Spec", "SPC-20")]
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void Parse_SignedCredentialLifetime_IsBoundedByAnHour(int seconds, bool accepted)
    {
        using var authority = SigningKey.Generate(KeyCurve.P256);
        var jwt = Signed(authority, SpaceTokenType.Credential, Changed(CredentialJson(Now), $"exp={Now.ToUnixTimeSeconds() + seconds}"));

        if (accepted)
            Assert.Equal(TimeSpan.FromSeconds(seconds), SpaceToken.Parse(jwt, SpaceTokenType.Credential).ExpiresAt - Now);
        else
            AssertRefused(SpaceTokenError.BadJwt, jwt, SpaceTokenType.Credential);
    }

    [Theory]
    [Trait("Spec", "SPC-19")]
    [InlineData(5, true)] // inside the skew
    [InlineData(6, false)]
    [InlineData(60, false)]
    public void Parse_CredentialIssuedInTheFuture_IsRefusedBeyondTheSkew(int secondsAhead, bool accepted)
    {
        using var authority = SigningKey.Generate(KeyCurve.P256);
        var jwt = SpaceToken.CreateCredential(CredentialClaims(Now.AddSeconds(secondsAhead)), authority);
        var time = new FakeTimeProvider(Now);

        if (accepted)
            Assert.Equal(Now.AddSeconds(secondsAhead), SpaceToken.Parse(jwt, SpaceTokenType.Credential, time).IssuedAt);
        else
            AssertRefused(SpaceTokenError.BadJwt, jwt, SpaceTokenType.Credential, time);
        // Without a clock, times are not checked.
        Assert.NotNull(SpaceToken.Parse(jwt, SpaceTokenType.Credential));
    }

    [Theory]
    [Trait("Spec", "SPC-19"), Trait("Spec", "SPC-20")]
    [InlineData(-1, true)]
    [InlineData(4, true)] // past exp, inside the skew
    [InlineData(5, false)]
    [InlineData(3600, false)]
    public void Parse_CredentialPastItsExpiry_IsExpiredBeyondTheSkew(int secondsPastExpiry, bool accepted)
    {
        using var authority = SigningKey.Generate(KeyCurve.P256);
        var time = new FakeTimeProvider(Now);
        var jwt = SpaceToken.CreateCredential(CredentialClaims(time.GetUtcNow()), authority);

        time.Advance(SpaceToken.DefaultSpaceCredentialLifetime + TimeSpan.FromSeconds(secondsPastExpiry));

        if (accepted)
            Assert.NotNull(SpaceToken.Parse(jwt, SpaceTokenType.Credential, time));
        else
            AssertRefused(SpaceTokenError.JwtExpired, jwt, SpaceTokenType.Credential, time);
    }

    [Fact]
    [Trait("Spec", "SPC-18"), Trait("Spec", "SPC-19"), Trait("Spec", "SPC-21")]
    public void CreateClientAttestation_ThenParse_RoundTripsWithIssuerAndSubjectTheClientId()
    {
        using var client = SigningKey.Generate(KeyCurve.P256);
        var time = new FakeTimeProvider(Now);

        var jwt = SpaceToken.CreateClientAttestation(AttestationClaims() with { KeyId = "key-1" }, client);
        var token = SpaceToken.Parse(jwt, SpaceTokenType.ClientAttestation, time);

        Assert.True(Jws.TryDecode(jwt, out var jws, out var error), error);
        Assert.Equal(new JwsHeader { Algorithm = "ES256", Type = "atproto-client-attestation+jwt", KeyId = "key-1" }, jws.Header);
        Assert.Equal((SpaceTokenType.ClientAttestation, ClientId, ClientId, Host), (token.Type, token.Issuer, token.Subject, token.Audience));
        Assert.Equal((Now, Now.AddSeconds(60), "key-1"), (token.IssuedAt, token.ExpiresAt, token.KeyId));
        Assert.Null(token.ConfirmationKey);
        Assert.False(jws.Payload.TryGetProperty("cnf", out _));
        Assert.True(token.VerifySignature(client.PublicKey));
    }

    [Fact]
    [Trait("Spec", "SPC-19")]
    public void CreateClientAttestation_WithoutAKeyId_WritesNoKid()
    {
        using var client = SigningKey.Generate(KeyCurve.P256);

        var jwt = SpaceToken.CreateClientAttestation(AttestationClaims(), client);

        Assert.True(Jws.TryDecode(jwt, out var jws, out _));
        Assert.False(jws.HeaderJson.TryGetProperty("kid", out _));
        Assert.Null(SpaceToken.Parse(jwt, SpaceTokenType.ClientAttestation).KeyId);
    }

    [Theory]
    [Trait("Spec", "SPC-20")]
    [InlineData(0)]
    [InlineData(-1_000)]
    [InlineData(999)]
    public void CreateClientAttestation_LifetimeUnderASecond_ThrowsArgumentException(int milliseconds)
    {
        using var client = SigningKey.Generate(KeyCurve.P256);

        Assert.Throws<ArgumentException>(() => SpaceToken.CreateClientAttestation(AttestationClaims() with { Lifetime = TimeSpan.FromMilliseconds(milliseconds) }, client));
    }

    private static SpaceCredentialClaims CredentialClaims(DateTimeOffset issuedAt) => new()
    {
        Authority = Did.Parse(Authority),
        Space = SpaceUri.Parse(Space),
        BoundKey = DidKey.Parse(TestKeys.P256DidKey),
        IssuedAt = issuedAt,
    };

    private static ClientAttestationClaims AttestationClaims() => new() { ClientId = ClientId, Audience = Host, IssuedAt = Now };

    private static JsonObject CredentialJson(DateTimeOffset iat) => new()
    {
        ["iss"] = Authority,
        ["sub"] = Space,
        ["cnf"] = new JsonObject { ["kid"] = TestKeys.P256DidKey },
        ["iat"] = iat.ToUnixTimeSeconds(),
        ["exp"] = iat.AddMinutes(10).ToUnixTimeSeconds(),
        ["jti"] = Jti,
    };

    private static JsonObject ShortTokenJson(string issuer, string subject) => new()
    {
        ["iss"] = issuer,
        ["sub"] = subject,
        ["aud"] = Host,
        ["iat"] = Now.ToUnixTimeSeconds(),
        ["exp"] = Now.AddSeconds(60).ToUnixTimeSeconds(),
        ["jti"] = Jti,
    };

    // "-claim" removes a claim; "claim=json" sets it.
    private static JsonObject Changed(JsonObject claims, string change)
    {
        if (change.StartsWith('-'))
        {
            claims.Remove(change[1..]);
            return claims;
        }
        var at = change.IndexOf('=');
        claims[change[..at]] = JsonNode.Parse(change[(at + 1)..]);
        return claims;
    }

    // A token signed by hand: the type's typ and, but for an attestation, the #atproto kid, unless a test says otherwise.
    private static string Signed(SigningKey key, SpaceTokenType type, JsonObject claims, string? typ = "", string? kid = "") =>
        Jws.Sign(new JwsHeader
        {
            Algorithm = Jws.AlgorithmFor(key.Curve),
            Type = typ == "" ? SpaceToken.TypeName(type) : typ,
            KeyId = kid == "" ? (type is SpaceTokenType.ClientAttestation ? null : SpaceToken.SigningKeyId) : kid,
        }, Encoding.UTF8.GetBytes(claims.ToJsonString()), key);

    private static void AssertRefused(SpaceTokenError expected, string jwt, SpaceTokenType type, TimeProvider? time = null) =>
        Assert.Equal(expected, Assert.Throws<SpaceTokenException>(() => SpaceToken.Parse(jwt, type, time)).Error);
}
