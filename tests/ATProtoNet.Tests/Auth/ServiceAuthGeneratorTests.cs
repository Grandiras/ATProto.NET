using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Auth;

/// <summary>Tests for <see cref="ServiceAuthGenerator"/>.</summary>
public sealed class ServiceAuthGeneratorTests
{
    private static readonly Nsid GetRecord = Nsid.Parse("com.atproto.repo.getRecord");

    [Fact]
    public void CreateToken_ProducesThreePartJwt()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test123"), key);

        var token = gen.CreateToken("did:web:bsky.social", GetRecord);

        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
    }

    [Fact]
    public void CreateToken_IncludesCorrectIssuer()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:myservice"), key);

        var token = gen.CreateToken("did:web:target.example", GetRecord);
        var payload = DecodePayload(token);

        Assert.Contains("\"iss\":\"did:plc:myservice\"", payload);
    }

    [Fact]
    public void CreateToken_IncludesAudience()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        var token = gen.CreateToken("did:web:audience.example", GetRecord);
        var payload = DecodePayload(token);

        Assert.Contains("\"aud\":\"did:web:audience.example\"", payload);
    }

    [Fact]
    public void CreateToken_IncludesLxm()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        var token = gen.CreateToken("did:web:target", GetRecord);
        var payload = DecodePayload(token);

        Assert.Contains("\"lxm\":\"com.atproto.repo.getRecord\"", payload);
    }

    [Fact]
    public void CreateToken_IncludesExpClaim()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        var token = gen.CreateToken("did:web:target", GetRecord);
        var payload = DecodePayload(token);

        Assert.Contains("\"exp\":", payload);
    }

    [Fact]
    public void CreateToken_IncludesIatClaim()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        var token = gen.CreateToken("did:web:target", GetRecord);
        var payload = DecodePayload(token);

        Assert.Contains("\"iat\":", payload);
    }

    [Fact]
    public void CreateToken_IncludesJtiClaim()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        var token = gen.CreateToken("did:web:target", GetRecord);

        Assert.Matches("^[0-9a-f]{32}$", TestJws.DecodeJson(token, 1).GetProperty("jti").GetString());
    }

    [Fact]
    public void CreateToken_K256Key_SignsAnEs256KTokenTheDidKeyVerifies()
    {
        using var key = AtProtoCrypto.GenerateK256Key();
        var didKey = key.ToDidKey();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        var token = gen.CreateToken("did:web:target", GetRecord);
        var parts = token.Split('.');

        Assert.Equal("ES256K", TestJws.DecodeJson(token, 0).GetProperty("alg").GetString());
        Assert.True(AtProtoCrypto.VerifySignature(
            didKey, System.Text.Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), TestJws.Decode(parts[2])));
    }

    [Fact]
    public void CreateToken_UniqueJtiEachTime()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        var token1 = gen.CreateToken("did:web:target", GetRecord);
        var token2 = gen.CreateToken("did:web:target", GetRecord);

        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void CreateToken_HeaderUsesES256ForP256()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        var token = gen.CreateToken("did:web:target", GetRecord);
        var header = DecodeHeader(token);

        Assert.Contains("\"alg\":\"ES256\"", header);
    }

    [Fact]
    public void CreateToken_RejectsExpiryOver5Minutes()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            gen.CreateToken("did:web:target", GetRecord, expiresIn: TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void CreateToken_AudienceWithAServiceFragment_IsWrittenVerbatim()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        var token = gen.CreateToken("did:web:feed.example.com#bsky_fg", GetRecord);
        var payload = System.Text.Encoding.UTF8.GetString(Base64UrlDecode(token.Split('.')[1]));

        Assert.Contains("\"aud\":\"did:web:feed.example.com#bsky_fg\"", payload);
    }

    [Theory]
    [InlineData("https://feed.example.com")]
    [InlineData("feed.example.com")]
    [InlineData("did:web:feed.example.com#")]
    [InlineData("#bsky_fg")]
    [InlineData("did:web:feed.example.com#bsky_fg#again")]
    [InlineData("did:web:feed.example.com#bsky fg")]
    public void CreateToken_AudienceThatIsNotAServiceIdentifier_Throws(string audience)
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        Assert.Throws<ArgumentException>(() => gen.CreateToken(audience, GetRecord));
    }

    [Fact]
    public void CreateToken_ThrowsWhenDisposed()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);
        gen.Dispose();

        Assert.Throws<ObjectDisposedException>(() => gen.CreateToken("did:web:target", GetRecord));
    }

    [Fact]
    public void ServiceDid_ReturnsConfiguredDid()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:myservice"), key);

        Assert.Equal("did:plc:myservice", gen.ServiceDid);
    }

    [Fact]
    public void CreateToken_SignatureIsVerifiable()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var didKey = key.ToDidKey();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        var token = gen.CreateToken("did:web:target", GetRecord);
        var parts = token.Split('.');

        // Verify signature
        var signingInput = System.Text.Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64UrlDecode(parts[2]);

        using var verifyKey = AtProtoCrypto.FromDidKey(didKey);
        Assert.True(verifyKey.Verify(signingInput, signature));
    }

    [Fact]
    public void CreateToken_WritesExactlyTheClaimsTheReferenceImplementationDoes()
    {
        // @atproto/xrpc-server createServiceJwt: a {typ, alg} header, and iat, iss, aud, exp,
        // lxm and jti in the payload — nothing a receiver does not expect, nothing it requires
        // missing.
        using var key = AtProtoCrypto.GenerateK256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        var token = gen.CreateToken("did:web:api.bsky.app#bsky_appview", GetRecord);
        var header = TestJws.DecodeJson(token, 0);
        var payload = TestJws.DecodeJson(token, 1);

        Assert.Equal(["alg", "typ"], header.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("JWT", header.GetProperty("typ").GetString());
        Assert.Equal("ES256K", header.GetProperty("alg").GetString());
        Assert.Equal(
            ["aud", "exp", "iat", "iss", "jti", "lxm"],
            payload.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(60, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64());
    }

    [Fact]
    public void CreateToken_NullLxm_Throws()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        Assert.Throws<ArgumentNullException>(() => gen.CreateToken("did:web:target", null!));
    }

    [Fact]
    public void Constructor_WithAKeyId_SendsItAsTheKidHeader()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:labeler"), key, "#atproto_label");

        var token = gen.CreateToken("did:web:pds.example.com#atproto_pds", GetRecord);

        Assert.Equal("#atproto_label", gen.KeyId);
        Assert.Equal("#atproto_label", TestJws.DecodeJson(token, 0).GetProperty("kid").GetString());
        Assert.Equal("did:plc:labeler", TestJws.DecodeJson(token, 1).GetProperty("iss").GetString());
    }

    [Fact]
    public void Constructor_WithoutAKeyId_SendsNoKidSoTheDefaultAtprotoKeyApplies()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator(Did.Parse("did:plc:test"), key);

        var token = gen.CreateToken("did:web:target", GetRecord);

        Assert.Null(gen.KeyId);
        Assert.False(TestJws.DecodeJson(token, 0).TryGetProperty("kid", out _));
    }

    [Theory]
    [InlineData("atproto")] // the spec's form includes the '#'
    [InlineData("did:plc:test#atproto")] // and not the DID
    [InlineData("#")]
    [InlineData("#atproto#label")]
    [InlineData("#atproto label")]
    [InlineData("")]
    public void Constructor_KeyIdThatIsNotAFragment_Throws(string keyId)
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<ArgumentException>(() => new ServiceAuthGenerator(Did.Parse("did:plc:test"), key, keyId));
    }

    [Fact]
    public void Constructor_IssuerIsADidAndSoNeverCarriesAFragment()
    {
        // The 2026 revision removed the service fragment from "iss"; a Did cannot hold one.
        Assert.False(Did.TryParse("did:plc:test#atproto_labeler", out _));
    }

    // ── Helpers ──────────────────────────────────────────────

    private static string DecodePayload(string jwt)
    {
        var parts = jwt.Split('.');
        var bytes = Base64UrlDecode(parts[1]);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static string DecodeHeader(string jwt)
    {
        var parts = jwt.Split('.');
        var bytes = Base64UrlDecode(parts[0]);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
