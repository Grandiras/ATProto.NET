using System.Text.Json;
using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Tests.Auth.OAuth;

public class AtProtoTokenDataTests
{
    [Fact]
    public void Required_Properties_CanBeSet()
    {
        var privateKey = new byte[] { 1, 2, 3, 4, 5 };

        var data = new AtProtoTokenData
        {
            Did = "did:plc:abc123",
            Handle = "alice.bsky.social",
            AccessToken = "access-token",
            PdsUrl = "https://bsky.social",
            Issuer = "https://bsky.social",
            TokenEndpoint = "https://bsky.social/oauth/token",
            DPoPPrivateKey = privateKey,
        };

        Assert.Equal("did:plc:abc123", data.Did);
        Assert.Equal("alice.bsky.social", data.Handle);
        Assert.Equal("access-token", data.AccessToken);
        Assert.Equal("https://bsky.social", data.PdsUrl);
        Assert.Equal("https://bsky.social", data.Issuer);
        Assert.Equal("https://bsky.social/oauth/token", data.TokenEndpoint);
        Assert.Equal(privateKey, data.DPoPPrivateKey);
    }

    [Fact]
    public void Optional_Properties_DefaultToNull()
    {
        var data = new AtProtoTokenData
        {
            Did = "did:plc:abc123",
            Handle = "alice.bsky.social",
            AccessToken = "access-token",
            PdsUrl = "https://bsky.social",
            Issuer = "https://bsky.social",
            TokenEndpoint = "https://bsky.social/oauth/token",
            DPoPPrivateKey = [1, 2, 3],
        };

        Assert.Null(data.RefreshToken);
        Assert.Null(data.AuthServerDpopNonce);
        Assert.Null(data.ResourceServerDpopNonce);
        Assert.Null(data.ExpiresIn);
        Assert.Null(data.Scope);
    }

    [Fact]
    public void TokenObtainedAt_DefaultsToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;

        var data = new AtProtoTokenData
        {
            Did = "did:plc:abc123",
            Handle = "alice.bsky.social",
            AccessToken = "access-token",
            PdsUrl = "https://bsky.social",
            Issuer = "https://bsky.social",
            TokenEndpoint = "https://bsky.social/oauth/token",
            DPoPPrivateKey = [1, 2, 3],
        };

        var after = DateTimeOffset.UtcNow;

        Assert.InRange(data.TokenObtainedAt, before, after);
    }

    [Fact]
    public void Serialization_RoundTripsCorrectly()
    {
        var privateKey = new byte[] { 10, 20, 30, 40, 50 };
        var now = DateTimeOffset.UtcNow;

        var original = new AtProtoTokenData
        {
            Did = "did:plc:abc123",
            Handle = "alice.bsky.social",
            AccessToken = "access-token-value",
            RefreshToken = "refresh-token-value",
            PdsUrl = "https://bsky.social",
            Issuer = "https://bsky.social",
            TokenEndpoint = "https://bsky.social/oauth/token",
            DPoPPrivateKey = privateKey,
            AuthServerDpopNonce = "auth-nonce",
            ResourceServerDpopNonce = "resource-nonce",
            TokenObtainedAt = now,
            ExpiresIn = 3600,
            Scope = "atproto transition:generic",
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AtProtoTokenData>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Did, deserialized.Did);
        Assert.Equal(original.Handle, deserialized.Handle);
        Assert.Equal(original.AccessToken, deserialized.AccessToken);
        Assert.Equal(original.RefreshToken, deserialized.RefreshToken);
        Assert.Equal(original.PdsUrl, deserialized.PdsUrl);
        Assert.Equal(original.Issuer, deserialized.Issuer);
        Assert.Equal(original.TokenEndpoint, deserialized.TokenEndpoint);
        Assert.Equal(original.DPoPPrivateKey, deserialized.DPoPPrivateKey);
        Assert.Equal(original.AuthServerDpopNonce, deserialized.AuthServerDpopNonce);
        Assert.Equal(original.ResourceServerDpopNonce, deserialized.ResourceServerDpopNonce);
        Assert.Equal(original.ExpiresIn, deserialized.ExpiresIn);
        Assert.Equal(original.Scope, deserialized.Scope);
    }

    [Fact]
    public void Mutable_Properties_CanBeModified()
    {
        var data = new AtProtoTokenData
        {
            Did = "did:plc:abc123",
            Handle = "alice.bsky.social",
            AccessToken = "old-access",
            PdsUrl = "https://bsky.social",
            Issuer = "https://bsky.social",
            TokenEndpoint = "https://bsky.social/oauth/token",
            DPoPPrivateKey = [1, 2, 3],
        };

        data.AccessToken = "new-access";
        data.RefreshToken = "new-refresh";
        data.AuthServerDpopNonce = "new-nonce";
        data.ResourceServerDpopNonce = "resource-nonce";

        Assert.Equal("new-access", data.AccessToken);
        Assert.Equal("new-refresh", data.RefreshToken);
        Assert.Equal("new-nonce", data.AuthServerDpopNonce);
        Assert.Equal("resource-nonce", data.ResourceServerDpopNonce);
    }
}
