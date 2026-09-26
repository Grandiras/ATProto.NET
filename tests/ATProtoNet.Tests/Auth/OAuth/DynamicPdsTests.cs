namespace ATProtoNet.Tests.Auth.OAuth;

public class DynamicPdsTests
{
    [Fact]
    public void SetServiceUrl_ChangesServiceUrl()
    {
        using var client = new AtProtoClient(new AtProtoClientOptions
        {
            InstanceUrl = "https://bsky.social"
        });

        Assert.Equal(new Uri("https://bsky.social/"), client.ServiceUrl);

        client.SetServiceUrl(new Uri("https://pds.example.com"));

        Assert.Equal(new Uri("https://pds.example.com/"), client.ServiceUrl);
    }

    [Fact]
    public void SetServiceUrl_ThrowsOnNull()
    {
        using var client = new AtProtoClient(new AtProtoClientOptions());

        Assert.Throws<ArgumentNullException>(() => client.SetServiceUrl(null!));
    }

    [Fact]
    public void SetServiceUrl_RejectsPlainHttpToAPublicHost()
    {
        using var client = new AtProtoClient(new AtProtoClientOptions());

        Assert.Throws<ArgumentException>(() => client.SetServiceUrl(new Uri("http://pds.example.com")));
        Assert.Equal(new Uri("https://bsky.social/"), client.ServiceUrl);
    }

    [Fact]
    public void ServiceUrl_DefaultValue()
    {
        using var client = new AtProtoClient(new AtProtoClientOptions());

        Assert.Equal(new Uri("https://bsky.social/"), client.ServiceUrl);
    }

    [Fact]
    public void SetServiceUrl_CanChangeServiceMultipleTimes()
    {
        using var client = new AtProtoClient(new AtProtoClientOptions());

        client.SetServiceUrl(new Uri("https://pds1.example.com"));
        Assert.Equal("pds1.example.com", client.ServiceUrl.Host);

        client.SetServiceUrl(new Uri("https://pds2.example.com"));
        Assert.Equal("pds2.example.com", client.ServiceUrl.Host);

        client.SetServiceUrl(new Uri("https://bsky.social"));
        Assert.Equal("bsky.social", client.ServiceUrl.Host);
    }

    [Fact]
    public void Session_NullByDefault()
    {
        using var client = new AtProtoClient(new AtProtoClientOptions());

        Assert.Null(client.Session);
    }
}
