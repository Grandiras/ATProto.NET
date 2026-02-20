using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;

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
    public void Base64UrlEncode_ProducesValidOutput()
    {
        var data = new byte[] { 0xFF, 0xFE, 0xFD };
        var result = DPoPProofGenerator.Base64UrlEncode(data);

        Assert.DoesNotContain("+", result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("=", result);
    }

    [Fact]
    public void ComputeS256Hash_IsConsistent()
    {
        var hash1 = DPoPProofGenerator.ComputeS256Hash("test-value");
        var hash2 = DPoPProofGenerator.ComputeS256Hash("test-value");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeS256Hash_DifferentInputsDifferentHashes()
    {
        var hash1 = DPoPProofGenerator.ComputeS256Hash("value1");
        var hash2 = DPoPProofGenerator.ComputeS256Hash("value2");
        Assert.NotEqual(hash1, hash2);
    }

    private static string ExtractClaim(string jwt, string claimName)
    {
        var parts = jwt.Split('.');
        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)!;
        return payload[claimName].GetString()!;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input
            .Replace('-', '+')
            .Replace('_', '/');

        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }
}
