using ATProtoNet.Pds;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ATProtoNet.Tests.Pds;

public class PdsEndpointTests : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;

    public PdsEndpointTests()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddAtProtoPds(opts =>
                    {
                        opts.Hostname = "test.local";
                        opts.PublicUrl = "https://test.local";
                        opts.OpenRegistration = true;
                        opts.AvailableUserDomains = [ "test.local" ];
                    });
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapAtProtoPds());
                });
            })
            .Build();

        _host.Start();
        _client = _host.GetTestServer().CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private async Task<(string accessJwt, string did)> CreateTestAccountAsync()
    {
        var response = await _client.PostAsJsonAsync("/xrpc/com.atproto.server.createAccount",
            new { handle = "alice.test.local", email = "alice@test.local", password = "password123" });
        response.EnsureSuccessStatusCode();

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return (doc.RootElement.GetProperty("accessJwt").GetString()!,
                doc.RootElement.GetProperty("did").GetString()!);
    }

    [Fact]
    public async Task DescribeServer_ReturnsDescription()
    {
        var response = await _client.GetAsync("/xrpc/com.atproto.server.describeServer");
        response.EnsureSuccessStatusCode();

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.False(doc.RootElement.GetProperty("inviteCodeRequired").GetBoolean());
    }

    [Fact]
    public async Task CreateAccount_ReturnsSession()
    {
        var response = await _client.PostAsJsonAsync("/xrpc/com.atproto.server.createAccount",
            new { handle = "bob.test.local", email = "bob@test.local", password = "password123" });
        response.EnsureSuccessStatusCode();

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("bob.test.local", doc.RootElement.GetProperty("handle").GetString());
        Assert.True(doc.RootElement.TryGetProperty("accessJwt", out _));
    }

    [Fact]
    public async Task CreateSession_ValidCredentials_ReturnsSession()
    {
        await _client.PostAsJsonAsync("/xrpc/com.atproto.server.createAccount",
            new { handle = "carol.test.local", email = "carol@test.local", password = "password123" });

        var response = await _client.PostAsJsonAsync("/xrpc/com.atproto.server.createSession",
            new { identifier = "carol.test.local", password = "password123" });
        response.EnsureSuccessStatusCode();

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("carol.test.local", doc.RootElement.GetProperty("handle").GetString());
    }

    [Fact]
    public async Task CreateSession_InvalidPassword_Returns401()
    {
        await _client.PostAsJsonAsync("/xrpc/com.atproto.server.createAccount",
            new { handle = "dave.test.local", email = "dave@test.local", password = "password123" });

        var response = await _client.PostAsJsonAsync("/xrpc/com.atproto.server.createSession",
            new { identifier = "dave.test.local", password = "wrongpassword" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSession_WithToken_ReturnsInfo()
    {
        var (token, did) = await CreateTestAccountAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/xrpc/com.atproto.server.getSession");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(did, doc.RootElement.GetProperty("did").GetString());
    }

    [Fact]
    public async Task GetSession_NoToken_Returns401()
    {
        var response = await _client.GetAsync("/xrpc/com.atproto.server.getSession");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateRecord_WithAuth_CreatesRecord()
    {
        var (token, did) = await CreateTestAccountAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/xrpc/com.atproto.repo.createRecord");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            repo = did,
            collection = "app.bsky.feed.post",
            record = new { text = "Hello from test!", createdAt = "2024-01-01T00:00:00Z" },
        });

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Contains(did, doc.RootElement.GetProperty("uri").GetString());
    }

    [Fact]
    public async Task CreateRecord_NoAuth_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/xrpc/com.atproto.repo.createRecord",
            new { repo = "did:plc:fake", collection = "app.bsky.feed.post", record = new { text = "hi" } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateRecord_WrongRepo_Returns403()
    {
        var (token, _) = await CreateTestAccountAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/xrpc/com.atproto.repo.createRecord");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            repo = "did:plc:someoneelse",
            collection = "app.bsky.feed.post",
            record = new { text = "hi" },
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRecord_ReturnsRecord()
    {
        var (token, did) = await CreateTestAccountAsync();

        // Create a record first
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/xrpc/com.atproto.repo.createRecord");
        createReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(new
        {
            repo = did,
            collection = "app.bsky.feed.post",
            record = new { text = "test" },
            rkey = "gettest",
        });
        await _client.SendAsync(createReq);

        // Get it
        var response = await _client.GetAsync(
            $"/xrpc/com.atproto.repo.getRecord?repo={did}&collection=app.bsky.feed.post&rkey=gettest");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetRecord_NotFound_Returns400()
    {
        var response = await _client.GetAsync(
            "/xrpc/com.atproto.repo.getRecord?repo=did:plc:test&collection=app.bsky.feed.post&rkey=notfound");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListRecords_ReturnsRecords()
    {
        var (token, did) = await CreateTestAccountAsync();

        for (int i = 0; i < 3; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/xrpc/com.atproto.repo.createRecord");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new
            {
                repo = did,
                collection = "app.bsky.feed.post",
                record = new { text = $"Post {i}" },
                rkey = $"list{i}",
            });
            await _client.SendAsync(req);
        }

        var response = await _client.GetAsync(
            $"/xrpc/com.atproto.repo.listRecords?repo={did}&collection=app.bsky.feed.post&limit=10");
        response.EnsureSuccessStatusCode();

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(3, doc.RootElement.GetProperty("records").GetArrayLength());
    }
}
