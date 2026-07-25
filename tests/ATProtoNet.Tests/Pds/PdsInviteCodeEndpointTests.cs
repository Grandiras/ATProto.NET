using ATProtoNet.Pds;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ATProtoNet.Tests.Pds;

public class PdsInviteCodeEndpointTests : IAsyncDisposable
{
    private const string AdminPassword = "hunter2";

    private readonly IHost _host;
    private readonly HttpClient _client;

    public PdsInviteCodeEndpointTests()
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
                        opts.OpenRegistration = false;
                        opts.AdminPassword = AdminPassword;
                        opts.AvailableUserDomains = ["test.local"];
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

    private static AuthenticationHeaderValue Basic(string user, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

    private async Task<JsonElement> SendAsync(HttpMethod method, string url, object? body = null,
        AuthenticationHeaderValue? auth = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Authorization = auth } };
        if (body is not null) request.Content = JsonContent.Create(body);

        var response = await _client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private Task<JsonElement> CreateCodeAsync(int useCount = 1, string? forAccount = null) =>
        SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createInviteCode",
            new { useCount, forAccount }, Basic("admin", AdminPassword));

    // ── Admin auth ──

    [Fact]
    public async Task CreateInviteCode_NoCredential_Returns401()
    {
        await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createInviteCode",
            new { useCount = 1 }, auth: null, expected: HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateInviteCode_WrongPassword_Returns401()
    {
        await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createInviteCode",
            new { useCount = 1 }, Basic("admin", "wrong"), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateInviteCode_WrongUsername_Returns401()
    {
        await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createInviteCode",
            new { useCount = 1 }, Basic("root", AdminPassword), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateInviteCode_BearerTokenInsteadOfBasic_Returns401()
    {
        await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createInviteCode",
            new { useCount = 1 }, new AuthenticationHeaderValue("Bearer", AdminPassword),
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateInviteCode_MalformedBasicHeader_Returns401()
    {
        await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createInviteCode",
            new { useCount = 1 }, new AuthenticationHeaderValue("Basic", "not-base64!!"),
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminEndpoints_WithNoAdminPasswordConfigured_Return401()
    {
        using var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddAtProtoPds(opts =>
                    {
                        opts.Hostname = "test.local";
                        opts.OpenRegistration = false;
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

        await host.StartAsync();
        using var client = host.GetTestServer().CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/xrpc/com.atproto.server.createInviteCode")
        {
            Headers = { Authorization = Basic("admin", "") },
            Content = JsonContent.Create(new { useCount = 1 }),
        };

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await host.StopAsync();
    }

    // ── createInviteCode / createInviteCodes ──

    [Fact]
    public async Task CreateInviteCode_ReturnsUsableCode()
    {
        var created = await CreateCodeAsync();
        var code = created.GetProperty("code").GetString();

        Assert.NotNull(code);
        Assert.StartsWith("test-local-", code);

        var account = await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createAccount",
            new { handle = "alice.test.local", email = "alice@test.local", password = "password123", inviteCode = code });

        Assert.Equal("alice.test.local", account.GetProperty("handle").GetString());
    }

    [Fact]
    public async Task CreateInviteCodes_ReturnsBatchPerAccount()
    {
        var result = await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createInviteCodes",
            new { codeCount = 2, useCount = 1, forAccounts = new[] { "did:plc:alice", "did:plc:bob" } },
            Basic("admin", AdminPassword));

        var batches = result.GetProperty("codes");
        Assert.Equal(2, batches.GetArrayLength());
        Assert.Equal("did:plc:alice", batches[0].GetProperty("account").GetString());
        Assert.Equal(2, batches[0].GetProperty("codes").GetArrayLength());
    }

    [Fact]
    public async Task CreateInviteCode_ZeroUseCount_Returns400()
    {
        await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createInviteCode",
            new { useCount = 0 }, Basic("admin", AdminPassword), HttpStatusCode.BadRequest);
    }

    // ── createAccount enforcement ──

    [Fact]
    public async Task CreateAccount_ArbitraryInviteCode_Returns400()
    {
        var error = await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createAccount",
            new { handle = "mallory.test.local", email = "m@test.local", password = "password123", inviteCode = "anything" },
            expected: HttpStatusCode.BadRequest);

        Assert.Equal("InvalidInviteCode", error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CreateAccount_NoInviteCode_Returns400()
    {
        var error = await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createAccount",
            new { handle = "mallory.test.local", email = "m@test.local", password = "password123" },
            expected: HttpStatusCode.BadRequest);

        Assert.Equal("InvalidInviteCode", error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CreateAccount_ReusingASingleUseCode_Returns400()
    {
        var code = (await CreateCodeAsync()).GetProperty("code").GetString();

        await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createAccount",
            new { handle = "first.test.local", password = "password123", inviteCode = code });

        var error = await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createAccount",
            new { handle = "second.test.local", password = "password123", inviteCode = code },
            expected: HttpStatusCode.BadRequest);

        Assert.Equal("InvalidInviteCode", error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task DescribeServer_ReportsInviteCodeRequired()
    {
        var description = await SendAsync(HttpMethod.Get, "/xrpc/com.atproto.server.describeServer");
        Assert.True(description.GetProperty("inviteCodeRequired").GetBoolean());
    }

    // ── admin.getInviteCodes ──

    [Fact]
    public async Task GetInviteCodes_ReturnsLexiconShapedViews()
    {
        var code = (await CreateCodeAsync(useCount: 3)).GetProperty("code").GetString();

        var result = await SendAsync(HttpMethod.Get, "/xrpc/com.atproto.admin.getInviteCodes",
            auth: Basic("admin", AdminPassword));

        var entry = result.GetProperty("codes").EnumerateArray()
            .Single(c => c.GetProperty("code").GetString() == code);

        Assert.Equal(3, entry.GetProperty("available").GetInt32());
        Assert.False(entry.GetProperty("disabled").GetBoolean());
        Assert.Equal("admin", entry.GetProperty("createdBy").GetString());
        Assert.Empty(entry.GetProperty("uses").EnumerateArray());
    }

    [Fact]
    public async Task GetInviteCodes_ReportsUsesAfterRedemption()
    {
        var code = (await CreateCodeAsync()).GetProperty("code").GetString();

        var account = await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createAccount",
            new { handle = "used.test.local", password = "password123", inviteCode = code });
        var did = account.GetProperty("did").GetString();

        var result = await SendAsync(HttpMethod.Get, "/xrpc/com.atproto.admin.getInviteCodes",
            auth: Basic("admin", AdminPassword));

        var entry = result.GetProperty("codes").EnumerateArray()
            .Single(c => c.GetProperty("code").GetString() == code);

        var use = Assert.Single(entry.GetProperty("uses").EnumerateArray().ToList());
        Assert.Equal(did, use.GetProperty("usedBy").GetString());
    }

    [Fact]
    public async Task GetInviteCodes_NoCredential_Returns401()
    {
        await SendAsync(HttpMethod.Get, "/xrpc/com.atproto.admin.getInviteCodes",
            expected: HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetInviteCodes_Paginates()
    {
        for (var i = 0; i < 3; i++) await CreateCodeAsync();

        var page1 = await SendAsync(HttpMethod.Get, "/xrpc/com.atproto.admin.getInviteCodes?limit=2",
            auth: Basic("admin", AdminPassword));
        Assert.Equal(2, page1.GetProperty("codes").GetArrayLength());

        var cursor = page1.GetProperty("cursor").GetString();
        Assert.NotNull(cursor);

        var page2 = await SendAsync(HttpMethod.Get,
            $"/xrpc/com.atproto.admin.getInviteCodes?limit=2&cursor={cursor}",
            auth: Basic("admin", AdminPassword));
        Assert.Equal(1, page2.GetProperty("codes").GetArrayLength());
    }

    // ── admin.disableInviteCodes ──

    [Fact]
    public async Task DisableInviteCodes_ByCode_MakesTheCodeUnusable()
    {
        var code = (await CreateCodeAsync()).GetProperty("code").GetString();

        await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.admin.disableInviteCodes",
            new { codes = new[] { code } }, Basic("admin", AdminPassword));

        var error = await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createAccount",
            new { handle = "blocked.test.local", password = "password123", inviteCode = code },
            expected: HttpStatusCode.BadRequest);

        Assert.Equal("InvalidInviteCode", error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task DisableInviteCodes_ByAccount_DisablesThatAccountsCodes()
    {
        var code = (await CreateCodeAsync(forAccount: "did:plc:alice")).GetProperty("code").GetString();

        await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.admin.disableInviteCodes",
            new { accounts = new[] { "did:plc:alice" } }, Basic("admin", AdminPassword));

        var result = await SendAsync(HttpMethod.Get, "/xrpc/com.atproto.admin.getInviteCodes",
            auth: Basic("admin", AdminPassword));

        var entry = result.GetProperty("codes").EnumerateArray()
            .Single(c => c.GetProperty("code").GetString() == code);
        Assert.True(entry.GetProperty("disabled").GetBoolean());
    }

    [Fact]
    public async Task DisableInviteCodes_NoCredential_Returns401()
    {
        await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.admin.disableInviteCodes",
            new { codes = new[] { "x" } }, auth: null, expected: HttpStatusCode.Unauthorized);
    }

    // ── server.getAccountInviteCodes ──

    [Fact]
    public async Task GetAccountInviteCodes_ReturnsTheCallersCodes()
    {
        var signupCode = (await CreateCodeAsync()).GetProperty("code").GetString();
        var account = await SendAsync(HttpMethod.Post, "/xrpc/com.atproto.server.createAccount",
            new { handle = "alice.test.local", password = "password123", inviteCode = signupCode });

        var did = account.GetProperty("did").GetString();
        var token = account.GetProperty("accessJwt").GetString();

        var ownCode = (await CreateCodeAsync(forAccount: did)).GetProperty("code").GetString();
        await CreateCodeAsync(forAccount: "did:plc:someoneelse");

        var result = await SendAsync(HttpMethod.Get, "/xrpc/com.atproto.server.getAccountInviteCodes",
            auth: new AuthenticationHeaderValue("Bearer", token));

        var entry = Assert.Single(result.GetProperty("codes").EnumerateArray().ToList());
        Assert.Equal(ownCode, entry.GetProperty("code").GetString());
        Assert.Equal(did, entry.GetProperty("forAccount").GetString());
    }

    [Fact]
    public async Task GetAccountInviteCodes_NoToken_Returns401()
    {
        await SendAsync(HttpMethod.Get, "/xrpc/com.atproto.server.getAccountInviteCodes",
            expected: HttpStatusCode.Unauthorized);
    }
}
