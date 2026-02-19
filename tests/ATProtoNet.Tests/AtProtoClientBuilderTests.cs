namespace ATProtoNet.Tests;

public class AtProtoClientBuilderTests
{
    [Fact]
    public void Build_DefaultOptions_CreatesClient()
    {
        var client = new AtProtoClientBuilder().Build();

        Assert.NotNull(client);
        Assert.False(client.IsAuthenticated);
        Assert.Null(client.Session);
    }

    [Fact]
    public void WithInstanceUrl_SetsUrl()
    {
        var client = new AtProtoClientBuilder()
            .WithInstanceUrl("https://pds.example.com")
            .Build();

        Assert.NotNull(client);
    }

    [Fact]
    public void WithInstanceUrl_NullOrEmpty_Throws()
    {
        var builder = new AtProtoClientBuilder();

        Assert.ThrowsAny<ArgumentException>(() => builder.WithInstanceUrl(null!));
        Assert.ThrowsAny<ArgumentException>(() => builder.WithInstanceUrl(""));
        Assert.ThrowsAny<ArgumentException>(() => builder.WithInstanceUrl("  "));
    }

    [Fact]
    public void WithAutoRefreshSession_ConfiguresOption()
    {
        // Should not throw
        var client = new AtProtoClientBuilder()
            .WithAutoRefreshSession(false)
            .Build();

        Assert.NotNull(client);
    }

    [Fact]
    public void WithHttpClient_UsesProvidedClient()
    {
        using var httpClient = new HttpClient();
        var client = new AtProtoClientBuilder()
            .WithHttpClient(httpClient)
            .Build();

        Assert.NotNull(client);
    }

    [Fact]
    public void FluentChaining_Works()
    {
        using var httpClient = new HttpClient();

        var client = new AtProtoClientBuilder()
            .WithInstanceUrl("https://pds.example.com")
            .WithAutoRefreshSession(false)
            .WithHttpClient(httpClient)
            .Build();

        Assert.NotNull(client);
    }

    [Fact]
    public void Build_SubClients_AreAccessible()
    {
        var client = new AtProtoClientBuilder().Build();

        // All sub-client namespaces should be initialized
        Assert.NotNull(client.Server);
        Assert.NotNull(client.Repo);
        Assert.NotNull(client.Identity);
        Assert.NotNull(client.Sync);
        Assert.NotNull(client.Admin);
        Assert.NotNull(client.Label);
        Assert.NotNull(client.Moderation);
        Assert.NotNull(client.Bsky);
    }

    [Fact]
    public void Client_ImplementsIDisposable()
    {
        var client = new AtProtoClientBuilder().Build();

        // Should not throw
        client.Dispose();
    }

    [Fact]
    public async Task Client_ImplementsIAsyncDisposable()
    {
        var client = new AtProtoClientBuilder().Build();

        // Should not throw
        await client.DisposeAsync();
    }
}
