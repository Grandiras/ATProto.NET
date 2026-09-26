using System.Net;
using ATProtoNet.Tests.TestSupport;

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
        using var stub = new HttpStub().On("/", _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(stub);

        await new AtProtoClient(httpClient: httpClient).DisposeAsync();

        using var response = await httpClient.GetAsync(new Uri("https://example.com/"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Client_DisposesTwice_WithoutThrowing()
    {
        var client = new AtProtoClient();

        await client.DisposeAsync();
        client.Dispose();
    }
}
