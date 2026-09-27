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

    // What the server's validator checks of a generated proof (typ, alg, the thumbprint of the
    // embedded key, htm/htu, a fresh jti, ath, a resumed key's binding) is pinned end to end in
    // DPoPInteropTests; these pin what the validator does not.

    [Fact]
    public void DifferentInstances_GenerateDifferentKeys()
    {
        using var generator2 = new DPoPProofGenerator();
        Assert.NotEqual(_generator.KeyThumbprint, generator2.KeyThumbprint);
    }

    [Fact]
    public void GenerateProof_Nonce_IsSentOnlyWhenGiven()
    {
        var without = TestJws.DecodeJson(_generator.GenerateProof("POST", "https://example.com/token"), 1);
        var with = TestJws.DecodeJson(_generator.GenerateProof("POST", "https://example.com/token", "test-nonce-value"), 1);

        Assert.False(without.TryGetProperty("nonce", out _));
        Assert.Equal("test-nonce-value", with.GetProperty("nonce").GetString());
    }

    [Fact]
    public void GenerateProof_HttpMethodUppercased()
    {
        var proof = _generator.GenerateProof("get", "https://example.com/token");
        var htm = ExtractClaim(proof, "htm");
        Assert.Equal("GET", htm);
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
}
