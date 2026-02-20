using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Tests.Auth.OAuth;

public class DynamicPdsTests
{
    [Fact]
    public void SetPdsUrl_ChangesBaseUrl()
    {
        var client = new AtProtoClient(new AtProtoClientOptions
        {
            InstanceUrl = "https://bsky.social"
        });

        Assert.Contains("bsky.social", client.PdsUrl);

        client.SetPdsUrl("https://pds.example.com");

        Assert.Contains("pds.example.com", client.PdsUrl);
    }

    [Fact]
    public void SetPdsUrl_ThrowsOnNull()
    {
        var client = new AtProtoClient(new AtProtoClientOptions());

        Assert.ThrowsAny<ArgumentException>(() =>
            client.SetPdsUrl(null!));
    }

    [Fact]
    public void SetPdsUrl_ThrowsOnEmpty()
    {
        var client = new AtProtoClient(new AtProtoClientOptions());

        Assert.Throws<ArgumentException>(() =>
            client.SetPdsUrl(string.Empty));
    }

    [Fact]
    public void PdsUrl_DefaultValue()
    {
        var client = new AtProtoClient(new AtProtoClientOptions
        {
            InstanceUrl = "https://bsky.social"
        });

        Assert.Contains("bsky.social", client.PdsUrl);
    }

    [Fact]
    public void AtProtoClientOptions_OAuthProperty()
    {
        var options = new AtProtoClientOptions();

        Assert.Null(options.OAuth);

        options.OAuth = new OAuthOptions
        {
            ClientMetadata = new OAuthClientMetadata
            {
                ClientId = "https://myapp.example.com/client-metadata.json",
            },
        };

        Assert.NotNull(options.OAuth);
        Assert.Equal("https://myapp.example.com/client-metadata.json",
            options.OAuth.ClientMetadata.ClientId);
    }

    [Fact]
    public void SetPdsUrl_CanChangePdsMultipleTimes()
    {
        var client = new AtProtoClient(new AtProtoClientOptions());

        client.SetPdsUrl("https://pds1.example.com");
        Assert.Contains("pds1.example.com", client.PdsUrl);

        client.SetPdsUrl("https://pds2.example.com");
        Assert.Contains("pds2.example.com", client.PdsUrl);

        client.SetPdsUrl("https://bsky.social");
        Assert.Contains("bsky.social", client.PdsUrl);
    }

    [Fact]
    public void OAuthSession_NullByDefault()
    {
        var client = new AtProtoClient(new AtProtoClientOptions());

        Assert.Null(client.OAuthSession);
    }
}
