using ATProtoNet.Pds;

namespace ATProtoNet.Tests.Pds;

public class PdsSessionServiceTests
{
    private readonly PdsOptions _options = new()
    {
        Hostname = "test.local",
        PublicUrl = "https://test.local",
    };

    [Fact]
    public void IssueAccessToken_ReturnsValidJwt()
    {
        var service = new PdsSessionService(_options);
        var token = service.IssueAccessToken("did:plc:test123", "alice.test.local");

        Assert.NotEmpty(token);
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length); // JWT format: header.payload.signature
    }

    [Fact]
    public void IssueRefreshToken_ReturnsValidJwt()
    {
        var service = new PdsSessionService(_options);
        var token = service.IssueRefreshToken("did:plc:test123");

        Assert.NotEmpty(token);
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
    }

    [Fact]
    public void ValidateToken_ValidAccessToken_ReturnsResult()
    {
        var service = new PdsSessionService(_options);
        var token = service.IssueAccessToken("did:plc:test123", "alice.test.local");

        var result = service.ValidateToken(token);

        Assert.NotNull(result);
        Assert.True(result!.IsValid);
        Assert.Equal("did:plc:test123", result.Did);
        Assert.Equal("alice.test.local", result.Handle);
        Assert.Equal("atproto", result.Scope);
    }

    [Fact]
    public void ValidateToken_ValidRefreshToken_ReturnsResult()
    {
        var service = new PdsSessionService(_options);
        var token = service.IssueRefreshToken("did:plc:test123");

        var result = service.ValidateToken(token);

        Assert.NotNull(result);
        Assert.True(result!.IsValid);
        Assert.Equal("did:plc:test123", result.Did);
        Assert.Equal("com.atproto.refresh", result.Scope);
    }

    [Fact]
    public void ValidateToken_TamperedToken_ReturnsNull()
    {
        var service = new PdsSessionService(_options);
        var token = service.IssueAccessToken("did:plc:test123", "alice.test.local");

        // Tamper with the payload
        var parts = token.Split('.');
        var tampered = $"{parts[0]}.dGFtcGVyZWQ.{parts[2]}";

        var result = service.ValidateToken(tampered);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_GarbageString_ReturnsNull()
    {
        var service = new PdsSessionService(_options);
        var result = service.ValidateToken("not-a-token");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_ExpiredToken_ReturnsNull()
    {
        var service = new PdsSessionService(_options);
        // Issue token with zero lifetime — expired immediately
        var token = service.IssueAccessToken("did:plc:test123", "alice.test.local",
            expiration: TimeSpan.FromSeconds(-1));

        var result = service.ValidateToken(token);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_DifferentSigningKey_ReturnsNull()
    {
        var key1 = new byte[32];
        Array.Fill(key1, (byte)0x01);
        var key2 = new byte[32];
        Array.Fill(key2, (byte)0x02);

        var issuer = new PdsSessionService(_options, key1);
        var validator = new PdsSessionService(_options, key2);

        var token = issuer.IssueAccessToken("did:plc:test123", "alice.test.local");
        var result = validator.ValidateToken(token);

        Assert.Null(result);
    }

    [Fact]
    public void SameKey_ValidatesOwnTokens()
    {
        var key = new byte[32];
        Array.Fill(key, (byte)0xAB);

        var service = new PdsSessionService(_options, key);
        var token = service.IssueAccessToken("did:plc:test123", "alice.test.local");

        var result = service.ValidateToken(token);

        Assert.NotNull(result);
        Assert.True(result!.IsValid);
    }

    [Fact]
    public void GenerateSigningKey_ReturnsBase64KeyOfExpectedSize()
    {
        var key = PdsSessionService.GenerateSigningKey();

        Assert.Equal(PdsSessionService.SigningKeySize, Convert.FromBase64String(key).Length);
        Assert.NotEqual(key, PdsSessionService.GenerateSigningKey());
    }

    [Fact]
    public void ConfiguredSigningKey_SurvivesRestart()
    {
        var options = new PdsOptions
        {
            Hostname = "test.local",
            SessionSigningKey = PdsSessionService.GenerateSigningKey(),
        };

        // Two instances stand in for two process lifetimes sharing the same configuration.
        var before = new PdsSessionService(options);
        var after = new PdsSessionService(options);

        var token = before.IssueAccessToken("did:plc:test123", "alice.test.local");
        var result = after.ValidateToken(token);

        Assert.NotNull(result);
        Assert.Equal("did:plc:test123", result!.Did);
        Assert.False(before.UsesEphemeralSigningKey);
        Assert.False(after.UsesEphemeralSigningKey);
    }

    [Fact]
    public void NoConfiguredSigningKey_UsesEphemeralKey()
    {
        var before = new PdsSessionService(_options);
        var after = new PdsSessionService(_options);

        var token = before.IssueAccessToken("did:plc:test123", "alice.test.local");

        Assert.True(before.UsesEphemeralSigningKey);
        Assert.Null(after.ValidateToken(token));
    }

    [Fact]
    public void ExplicitSigningKey_TakesPrecedenceOverOptions()
    {
        var explicitKey = new byte[32];
        Array.Fill(explicitKey, (byte)0x7F);

        var options = new PdsOptions
        {
            Hostname = "test.local",
            SessionSigningKey = PdsSessionService.GenerateSigningKey(),
        };

        var service = new PdsSessionService(options, explicitKey);
        var token = service.IssueAccessToken("did:plc:test123", "alice.test.local");

        Assert.False(service.UsesEphemeralSigningKey);
        Assert.NotNull(new PdsSessionService(options, explicitKey).ValidateToken(token));
        Assert.Null(new PdsSessionService(options).ValidateToken(token));
    }

    [Fact]
    public void ResolveSigningKey_Unset_ReturnsNull()
    {
        Assert.Null(PdsSessionService.ResolveSigningKey(_options));

        // An unset environment variable binds to an empty string — treat it as unset too.
        Assert.Null(PdsSessionService.ResolveSigningKey(new PdsOptions { SessionSigningKey = "" }));
        Assert.Null(PdsSessionService.ResolveSigningKey(new PdsOptions { SessionSigningKey = "  " }));
    }

    [Fact]
    public void ResolveSigningKey_InvalidBase64_Throws()
    {
        var options = new PdsOptions { SessionSigningKey = "not base64!!" };

        var ex = Assert.Throws<InvalidOperationException>(() => PdsSessionService.ResolveSigningKey(options));
        Assert.Contains("SessionSigningKey", ex.Message);
    }

    [Fact]
    public void Constructor_InvalidConfiguredKey_Throws()
    {
        var options = new PdsOptions { SessionSigningKey = "%%%" };

        Assert.Throws<InvalidOperationException>(() => new PdsSessionService(options));
    }
}
