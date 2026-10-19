using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Crypto.Jose;

namespace ATProtoNet.Tests.Crypto.Jose;

/// <summary>Compact JWS: strict decoding, the algorithm bound to the key, and claim-agnostic signing.</summary>
public sealed class JwsTests
{
    private static readonly byte[] Claims = """{"iss":"did:plc:abc","exp":1}"""u8.ToArray();

    public static TheoryData<KeyCurve, string> Algorithms() => new() { { KeyCurve.P256, "ES256" }, { KeyCurve.K256, "ES256K" } };

    [Theory]
    [MemberData(nameof(Algorithms))]
    [Trait("Spec", "CRY-31")]
    public void AlgorithmFor_EachCurve_IsItsJwsAlgorithm(KeyCurve curve, string algorithm)
    {
        Assert.Equal(algorithm, Jws.AlgorithmFor(curve));
        Assert.Throws<ArgumentOutOfRangeException>(() => Jws.AlgorithmFor((KeyCurve)42));
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    [Trait("Spec", "CRY-30"), Trait("Spec", "CRY-31"), Trait("Spec", "CRY-32"), Trait("Spec", "CRY-45")]
    public void Sign_ThenDecode_VerifiesWithTheSignersKey(KeyCurve curve, string algorithm)
    {
        using var key = SigningKey.Generate(curve);

        var token = Jws.Sign(new JwsHeader { Algorithm = algorithm, Type = "test+jwt", KeyId = "k1" }, Claims, key);

        Assert.DoesNotContain('=', token);
        Assert.True(Jws.TryDecode(token, out var jws, out var error), error);
        Assert.Equal(new JwsHeader { Algorithm = algorithm, Type = "test+jwt", KeyId = "k1" }, jws.Header);
        Assert.Equal("did:plc:abc", jws.Payload.GetProperty("iss").GetString());
        Assert.Equal(Encoding.ASCII.GetBytes(token[..token.LastIndexOf('.')]), jws.SigningInput.ToArray());
        Assert.True(Jws.Verify(jws, key.PublicKey));
        // Signing never disposes the caller's key.
        Assert.NotEmpty(key.Sign(Claims));
    }

    [Fact]
    [Trait("Spec", "CRY-32")]
    public void Sign_HeaderMembers_OnlyThoseTheCallerSets()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);

        var token = Jws.Sign(new JwsHeader { Algorithm = "ES256" }, Claims, key);

        Assert.Equal("""{"alg":"ES256"}""", Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token.AsSpan(0, token.IndexOf('.')))));
    }

    [Fact]
    [Trait("Spec", "CRY-32"), Trait("Spec", "CRY-37")]
    public void Sign_EmbeddedJwk_IsWrittenPublic()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var jwk = key.PublicKey.ToJsonWebKey() with { D = "c2VjcmV0" };

        var token = Jws.Sign(new JwsHeader { Algorithm = "ES256", Type = "dpop+jwt", Jwk = jwk }, Claims, key);

        Assert.True(Jws.TryDecode(token, out var jws, out _));
        Assert.Equal(key.PublicKey.ToJsonWebKey(), jws.Header.Jwk);
        Assert.False(jws.HeaderJson.GetProperty("jwk").TryGetProperty("d", out _));
    }

    [Fact]
    [Trait("Spec", "CRY-31"), Trait("Spec", "CRY-32")]
    public void Sign_AlgorithmOfAnotherCurve_Throws()
    {
        // A K-256 key cannot sign a token that claims ES256.
        using var key = SigningKey.Generate(KeyCurve.K256);

        Assert.Throws<ArgumentException>(() => Jws.Sign(new JwsHeader { Algorithm = "ES256" }, Claims, key));
    }

    [Theory]
    [InlineData("ES256K")]
    [InlineData("none")]
    [InlineData("HS256")]
    [InlineData("RS256")]
    [Trait("Spec", "CRY-31")]
    public void Verify_AlgorithmNotTheKeys_IsFalse(string algorithm)
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var token = Forge(algorithm, key);

        Assert.True(Jws.TryDecode(token, out var jws, out _));
        Assert.False(Jws.Verify(jws, key.PublicKey));
    }

    [Fact]
    [Trait("Spec", "CRY-31"), Trait("Spec", "CRY-12")]
    public void Verify_HighSSignature_IsAccepted()
    {
        using var key = SigningKey.Generate(KeyCurve.K256);
        var token = Jws.Sign(new JwsHeader { Algorithm = "ES256K" }, Claims, key);
        var signature = Base64Url.DecodeFromChars(token.AsSpan(token.LastIndexOf('.') + 1));
        var highS = token[..(token.LastIndexOf('.') + 1)] + Base64Url.EncodeToString(Crypto.SignatureTests.HighS(signature, KeyCurve.K256));

        Assert.True(Jws.TryDecode(highS, out var jws, out _));
        Assert.True(Jws.Verify(jws, key.PublicKey));
    }

    public static TheoryData<string> MalformedTokens()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var token = Jws.Sign(new JwsHeader { Algorithm = "ES256" }, Claims, key);
        var (header, payload) = (token[..token.IndexOf('.')], token.Split('.')[1]);
        string Encode(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
        return
        [
            "", "a.b", "a.b.c.d", token + ".", "." + token,
            header + " ." + token[(header.Length + 1)..],               // whitespace
            token.Replace('-', '+').Replace('_', '/') + "+/",          // the standard alphabet
            Encode("[]") + "." + payload + "." + token.Split('.')[2],   // header not an object
            header + "." + Encode("\"claims\"") + "." + token.Split('.')[2],
            Encode("""{"typ":"jwt"}""") + "." + payload + "." + token.Split('.')[2],          // no alg
            Encode("""{"alg":1}""") + "." + payload + "." + token.Split('.')[2],
            Encode("""{"alg":"ES256","alg":"none"}""") + "." + payload + "." + token.Split('.')[2],
            Encode("""{"alg":null}""") + "." + payload + "." + token.Split('.')[2],
            Encode("""{"alg":"ES256","crit":["exp"],"exp":1}""") + "." + payload + "." + token.Split('.')[2],
            Encode("""{"alg":"ES256","jwk":{"kty":"EC","crv":"P-256","x":null,"y":null}}""") + "." + payload + "." + token.Split('.')[2],
            Encode("""{"alg":"ES256","jwk":{"kty":null,"crv":null,"x":"AA","y":"AA"}}""") + "." + payload + "." + token.Split('.')[2],
            header + "." + payload + "." + Base64Url.EncodeToString(new byte[72]),          // DER-sized
            header + "." + payload + "." + Base64Url.EncodeToString(new byte[63]),
            header + "." + payload + "." + new string('A', Jws.MaxTokenLength),
        ];
    }

    [Theory]
    [MemberData(nameof(MalformedTokens))]
    [Trait("Spec", "CRY-30")]
    public void TryDecode_Malformed_IsFalseWithAReason(string token)
    {
        Assert.False(Jws.TryDecode(token, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    [Trait("Spec", "CRY-30")]
    public void TryDecode_TrailingPadding_IsTolerated()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var parts = Jws.Sign(new JwsHeader { Algorithm = "ES256" }, Claims, key).Split('.');
        string Pad(string part) => part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');

        Assert.True(Jws.TryDecode(string.Join('.', parts.Select(Pad)), out var jws, out var error), error);
        Assert.Equal("did:plc:abc", jws.Payload.GetProperty("iss").GetString());
    }

    [Fact]
    [Trait("Spec", "CRY-33")]
    public void TryDecode_AnyClaims_AreLeftToTheTokenType()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var token = Jws.Sign(new JwsHeader { Algorithm = "ES256" }, """{"exp":-1,"anything":[1,2]}"""u8, key);

        Assert.True(Jws.TryDecode(token, out var jws, out _));
        Assert.Equal(JsonValueKind.Array, jws.Payload.GetProperty("anything").ValueKind);
    }

    // A token claiming algorithm, signed with ES256 over its own header and payload.
    private static string Forge(string algorithm, SigningKey key)
    {
        var signingInput = Base64Url.EncodeToString(Encoding.UTF8.GetBytes($$"""{"alg":"{{algorithm}}"}""")) + "." + Base64Url.EncodeToString(Claims);
        return signingInput + "." + Base64Url.EncodeToString(key.Sign(Encoding.ASCII.GetBytes(signingInput)));
    }
}
