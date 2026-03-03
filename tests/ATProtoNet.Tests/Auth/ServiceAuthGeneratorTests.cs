using ATProtoNet.Auth;
using ATProtoNet.Crypto;

namespace ATProtoNet.Tests.Auth;

/// <summary>Tests for <see cref="ServiceAuthGenerator"/>.</summary>
public sealed class ServiceAuthGeneratorTests
{
    [Fact]
    public void CreateToken_ProducesThreePartJwt()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator("did:plc:test123", key);

        var token = gen.CreateToken("did:web:bsky.social");

        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
    }

    [Fact]
    public void CreateToken_IncludesCorrectIssuer()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator("did:plc:myservice", key);

        var token = gen.CreateToken("did:web:target.example");
        var payload = DecodePayload(token);

        Assert.Contains("\"iss\":\"did:plc:myservice\"", payload);
    }

    [Fact]
    public void CreateToken_IncludesAudience()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator("did:plc:test", key);

        var token = gen.CreateToken("did:web:audience.example");
        var payload = DecodePayload(token);

        Assert.Contains("\"aud\":\"did:web:audience.example\"", payload);
    }

    [Fact]
    public void CreateToken_IncludesLxm_WhenProvided()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator("did:plc:test", key);

        var token = gen.CreateToken("did:web:target", lxm: "com.atproto.repo.getRecord");
        var payload = DecodePayload(token);

        Assert.Contains("\"lxm\":\"com.atproto.repo.getRecord\"", payload);
    }

    [Fact]
    public void CreateToken_OmitsLxm_WhenNull()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator("did:plc:test", key);

        var token = gen.CreateToken("did:web:target");
        var payload = DecodePayload(token);

        Assert.DoesNotContain("lxm", payload);
    }

    [Fact]
    public void CreateToken_IncludesExpClaim()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator("did:plc:test", key);

        var token = gen.CreateToken("did:web:target");
        var payload = DecodePayload(token);

        Assert.Contains("\"exp\":", payload);
    }

    [Fact]
    public void CreateToken_IncludesIatClaim()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator("did:plc:test", key);

        var token = gen.CreateToken("did:web:target");
        var payload = DecodePayload(token);

        Assert.Contains("\"iat\":", payload);
    }

    [Fact]
    public void CreateToken_IncludesJtiClaim()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator("did:plc:test", key);

        var token = gen.CreateToken("did:web:target");
        var payload = DecodePayload(token);

        Assert.Contains("\"jti\":", payload);
    }

    [Fact]
    public void CreateToken_UniqueJtiEachTime()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator("did:plc:test", key);

        var token1 = gen.CreateToken("did:web:target");
        var token2 = gen.CreateToken("did:web:target");

        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void CreateToken_HeaderUsesES256ForP256()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator("did:plc:test", key);

        var token = gen.CreateToken("did:web:target");
        var header = DecodeHeader(token);

        Assert.Contains("\"alg\":\"ES256\"", header);
    }

    [Fact]
    public void CreateToken_RejectsExpiryOver5Minutes()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator("did:plc:test", key);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            gen.CreateToken("did:web:target", expiresIn: TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void CreateToken_ThrowsWhenDisposed()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        var gen = new ServiceAuthGenerator("did:plc:test", key);
        gen.Dispose();

        Assert.Throws<ObjectDisposedException>(() => gen.CreateToken("did:web:target"));
    }

    [Fact]
    public void ServiceDid_ReturnsConfiguredDid()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var gen = new ServiceAuthGenerator("did:plc:myservice", key);

        Assert.Equal("did:plc:myservice", gen.ServiceDid);
    }

    [Fact]
    public void CreateToken_SignatureIsVerifiable()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var didKey = key.ToDidKey();
        using var gen = new ServiceAuthGenerator("did:plc:test", key);

        var token = gen.CreateToken("did:web:target");
        var parts = token.Split('.');

        // Verify signature
        var signingInput = System.Text.Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64UrlDecode(parts[2]);

        using var verifyKey = AtProtoCrypto.FromDidKey(didKey);
        Assert.True(verifyKey.Verify(signingInput, signature));
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
