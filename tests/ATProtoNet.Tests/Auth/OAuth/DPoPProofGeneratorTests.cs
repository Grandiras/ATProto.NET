using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Crypto;

namespace ATProtoNet.Tests.Auth.OAuth;

public class DPoPProofGeneratorTests : IDisposable
{
    private DPoPProofGenerator _generator = new();

    public void Dispose()
    {
        _generator.Dispose();
    }

    [Fact]
    public void Constructor_GeneratesValidKeyThumbprint()
    {
        Assert.NotNull(_generator.KeyThumbprint);
        Assert.NotEmpty(_generator.KeyThumbprint);
        // Base64url encoded SHA-256 hash = 43 characters (32 bytes)
        Assert.Equal(43, _generator.KeyThumbprint.Length);
    }

    [Fact]
    public void KeyThumbprint_IsConsistent()
    {
        var thumbprint1 = _generator.KeyThumbprint;
        var thumbprint2 = _generator.KeyThumbprint;
        Assert.Equal(thumbprint1, thumbprint2);
    }

    [Fact]
    public void DifferentInstances_GenerateDifferentKeys()
    {
        using var generator2 = new DPoPProofGenerator();
        Assert.NotEqual(_generator.KeyThumbprint, generator2.KeyThumbprint);
    }

    [Fact]
    public void GenerateProof_ReturnsValidJwtFormat()
    {
        var proof = _generator.GenerateProof("POST", "https://example.com/token");

        Assert.NotNull(proof);
        var parts = proof.Split('.');
        Assert.Equal(3, parts.Length); // header.payload.signature
    }

    [Fact]
    public void GenerateProof_HasCorrectHeader()
    {
        var proof = _generator.GenerateProof("POST", "https://example.com/token");
        var parts = proof.Split('.');

        var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
        var header = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(headerJson)!;

        Assert.Equal("dpop+jwt", header["typ"].GetString());
        Assert.Equal("ES256", header["alg"].GetString());
        Assert.True(header.ContainsKey("jwk"));

        var jwk = header["jwk"];
        Assert.Equal("EC", jwk.GetProperty("kty").GetString());
        Assert.Equal("P-256", jwk.GetProperty("crv").GetString());
        Assert.True(jwk.TryGetProperty("x", out _));
        Assert.True(jwk.TryGetProperty("y", out _));
    }

    [Fact]
    public void GenerateProof_HasCorrectPayload()
    {
        var proof = _generator.GenerateProof("POST", "https://example.com/token");
        var parts = proof.Split('.');

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)!;

        Assert.Equal("POST", payload["htm"].GetString());
        Assert.Equal("https://example.com/token", payload["htu"].GetString());
        Assert.True(payload.ContainsKey("jti"));
        Assert.True(payload.ContainsKey("iat"));
        Assert.False(payload.ContainsKey("nonce")); // Not provided
        Assert.False(payload.ContainsKey("ath")); // No access token
    }

    [Fact]
    public void GenerateProof_WithNonce_IncludesNonce()
    {
        var proof = _generator.GenerateProof("POST", "https://example.com/token", "test-nonce-value");
        var parts = proof.Split('.');

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)!;

        Assert.Equal("test-nonce-value", payload["nonce"].GetString());
    }

    [Fact]
    public void GenerateProofWithAccessToken_IncludesAthClaim()
    {
        var proof = _generator.GenerateProofWithAccessToken(
            "GET", "https://example.com/api", "nonce-val", "my-access-token");
        var parts = proof.Split('.');

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)!;

        Assert.True(payload.ContainsKey("ath"));
        Assert.NotEmpty(payload["ath"].GetString()!);
    }

    [Fact]
    public void GenerateProof_UniqueJti()
    {
        var proof1 = _generator.GenerateProof("POST", "https://example.com/token");
        var proof2 = _generator.GenerateProof("POST", "https://example.com/token");

        var jti1 = ExtractClaim(proof1, "jti");
        var jti2 = ExtractClaim(proof2, "jti");

        Assert.NotEqual(jti1, jti2);
    }

    [Fact]
    public void GenerateProof_HttpMethodUppercased()
    {
        var proof = _generator.GenerateProof("get", "https://example.com/token");
        var htm = ExtractClaim(proof, "htm");
        Assert.Equal("GET", htm);
    }

    [Fact]
    public void ExportAndImportKey_ProduceSameThumbprint()
    {
        var exported = _generator.ExportPrivateKey();

        using var imported = new DPoPProofGenerator(exported);
        Assert.Equal(_generator.KeyThumbprint, imported.KeyThumbprint);
    }

    [Fact]
    public void ExportAndImportKey_CanGenerateValidProofs()
    {
        var exported = _generator.ExportPrivateKey();
        using var imported = new DPoPProofGenerator(exported);

        var proof = imported.GenerateProof("POST", "https://example.com");
        var parts = proof.Split('.');
        Assert.Equal(3, parts.Length);
    }

    [Fact]
    public void Dispose_PreventsProofGeneration()
    {
        var gen = new DPoPProofGenerator();
        gen.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            gen.GenerateProof("POST", "https://example.com"));
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var gen = new DPoPProofGenerator();
        gen.Dispose();
        gen.Dispose(); // Should not throw
    }

    [Fact]
    public void Constructor_WithAK256Key_Throws()
    {
        // A stored secp256k1 key used to sign proofs whose header still claimed ES256 and P-256,
        // which every verifier rejects. AT Protocol DPoP is ES256 only.
        using var k256 = AtProtoCrypto.GenerateK256Key();

        var ex = Assert.Throws<ArgumentException>(() => new DPoPProofGenerator(k256.ExportPrivateKey()));

        Assert.Contains("P-256", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_WithBytesThatAreNotAKey_Throws()
    {
        Assert.ThrowsAny<CryptographicException>(() => new DPoPProofGenerator([1, 2, 3]));
    }

    [Fact]
    public void Constructor_WithAnImportedP256Key_UsesThatKey()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var generator = new DPoPProofGenerator(key.ExportPrivateKey());

        var parts = generator.GenerateProof("POST", "https://example.com/token").Split('.');

        Assert.True(key.Verify(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), TestJws.Decode(parts[2])));
    }

    [Fact]
    public void GenerateProof_HeaderJwk_IsExactlyThePublicKeyAndHashesToKeyThumbprint()
    {
        var header = TestJws.DecodeJson(_generator.GenerateProof("POST", "https://example.com/token"), 0);
        var jwk = header.GetProperty("jwk");

        Assert.Equal(new[] { "kty", "crv", "x", "y" }, jwk.EnumerateObject().Select(p => p.Name));
        Assert.Equal(_generator.KeyThumbprint, DPoP.Thumbprint(jwk.Deserialize<JsonWebKey>()!));
    }

    [Theory]
    [InlineData("https://user:pw@pds.example.com/xrpc/a", "https://pds.example.com/xrpc/a")]
    [InlineData("https://user@pds.example.com:8443/xrpc/a?b=c", "https://pds.example.com:8443/xrpc/a")]
    [InlineData("https://pds.example.com/xrpc/a?b=c#d", "https://pds.example.com/xrpc/a")]
    [InlineData("HTTPS://PDS.Example.COM:443/xrpc/a", "https://pds.example.com/xrpc/a")]
    public void GenerateProof_Htu_IsTheTargetUriWithoutUserinfoQueryOrFragment(string url, string htu)
    {
        // RFC 9449 section 4.2: htu is the target URI without query and fragment, and RFC 9110
        // section 4.2.4 keeps userinfo out of an http(s) target URI altogether.
        Assert.Equal(htu, ExtractClaim(_generator.GenerateProof("GET", url), "htu"));
    }

    [Theory]
    [InlineData("/xrpc/a")]
    [InlineData("pds.example.com/xrpc/a")]
    [InlineData("file:///xrpc/a")]
    public void GenerateProof_UrlThatIsNotAbsoluteWithAHost_Throws(string url)
    {
        Assert.Throws<ArgumentException>(() => _generator.GenerateProof("GET", url));
    }

    [Fact]
    public void GenerateProof_Jti_Is128BitsOfHex()
    {
        var jti = ExtractClaim(_generator.GenerateProof("POST", "https://example.com/token"), "jti");

        Assert.Matches("^[0-9a-f]{32}$", jti);
    }

    [Fact]
    public void GenerateProofWithAccessToken_Ath_IsTheRfc9449Hash()
    {
        // RFC 9449 section 7.1: this access token's ath.
        var proof = _generator.GenerateProofWithAccessToken(
            "GET", "https://resource.example.org/protectedresource", null, "Kz~8mXK1EalYznwH-LC-1fBAo.4Ljp~zsPE_NeO.gxU");

        Assert.Equal("fUHyO2r2Z3DZ53EsNrWBb0xWXoaNy59IiKCAqksmQEo", ExtractClaim(proof, "ath"));
    }

    [Fact]
    public void GenerateProofWithAccessToken_AfterTheTokenChanges_HashesTheNewToken()
    {
        const string url = "https://example.com/api";

        var first = ExtractClaim(_generator.GenerateProofWithAccessToken("GET", url, null, "token-one"), "ath");
        var second = ExtractClaim(_generator.GenerateProofWithAccessToken("GET", url, null, "token-two"), "ath");
        var again = ExtractClaim(_generator.GenerateProofWithAccessToken("GET", url, null, "token-one"), "ath");

        Assert.Equal(DPoP.AccessTokenHash("token-one"), first);
        Assert.Equal(DPoP.AccessTokenHash("token-two"), second);
        Assert.Equal(first, again);
    }

    private static string ExtractClaim(string jwt, string claimName) =>
        TestJws.DecodeJson(jwt, 1).GetProperty(claimName).GetString()!;

    private static byte[] Base64UrlDecode(string input) => TestJws.Decode(input);
}
