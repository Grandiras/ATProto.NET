namespace ATProtoNet.Tests;

/// <summary>
/// The one construction path: <c>new AtProtoClient(options, httpClient, sessionStore, logger)</c>,
/// every argument optional.
/// </summary>
public class AtProtoClientConstructionTests
{
    [Fact]
    public void Constructor_NoArguments_AddressesBlueskyUnauthenticated()
    {
        using var client = new AtProtoClient();

        Assert.Equal(new Uri("https://bsky.social/"), client.ServiceUrl);
        Assert.False(client.IsAuthenticated);
        Assert.Null(client.Session);
    }

    [Fact]
    public void Constructor_WithOptions_AddressesTheInstance()
    {
        using var client = new AtProtoClient(new AtProtoClientOptions { InstanceUrl = "https://pds.example.com" });

        Assert.Equal(new Uri("https://pds.example.com/"), client.ServiceUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not a url")]
    public void Constructor_InvalidInstanceUrl_Throws(string url)
    {
        Assert.ThrowsAny<ArgumentException>(() => new AtProtoClient(new AtProtoClientOptions { InstanceUrl = url }));
    }

    [Fact]
    public async Task Dispose_LeavesASuppliedHttpClientUsable()
    {
        using var handler = new NoContentHandler();
        using var httpClient = new HttpClient(handler);

        await new AtProtoClient(httpClient: httpClient).DisposeAsync();

        using var response = await httpClient.GetAsync(new Uri("https://example.com/"));
        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public void Constructor_SubClients_AreAccessible()
    {
        using var client = new AtProtoClient();

        Assert.NotNull(client.Server);
        Assert.NotNull(client.Repo);
        Assert.NotNull(client.Identity);
        Assert.NotNull(client.Sync);
        Assert.NotNull(client.Admin);
        Assert.NotNull(client.Label);
        Assert.NotNull(client.Moderation);
        Assert.NotNull(client.Space);
        Assert.NotNull(client.SimpleSpace);
        Assert.NotNull(client.Bsky);
        Assert.NotNull(client.Chat);
        Assert.NotNull(client.Ozone);
        Assert.NotNull(client.Site);
        Assert.NotNull(client.Transport);
    }

    [Fact]
    public async Task Client_DisposesTwice_WithoutThrowing()
    {
        var client = new AtProtoClient();

        await client.DisposeAsync();
        client.Dispose();
    }

    private sealed class NoContentHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
    }
}
