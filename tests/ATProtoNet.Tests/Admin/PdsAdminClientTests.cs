using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Admin;

namespace ATProtoNet.Tests.Admin;

public class PdsAdminClientTests : IDisposable
{
    private const string AdminPassword = "hunter2";

    private readonly MockHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly PdsAdminClient _client;

    public PdsAdminClientTests()
    {
        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://pds.example.com/")
        };

        _client = new PdsAdminClient(
            new PdsAdminOptions { Url = "https://pds.example.com", AdminPassword = AdminPassword },
            _httpClient,
            null);
    }

    // ──────────────────────────────────────────────────────────
    //  Construction
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithPlaintextHttpUrl_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new PdsAdminClient("http://pds.example.com", AdminPassword));

        Assert.Contains("HTTPS", ex.Message);
    }

    [Fact]
    public void Constructor_WithLoopbackHttpUrl_IsAllowed()
    {
        using var client = new PdsAdminClient("http://localhost:3000", AdminPassword);

        Assert.Equal("http://localhost:3000/", client.PdsUrl.ToString());
    }

    [Fact]
    public void Constructor_WithEmptyAdminPassword_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new PdsAdminClient("https://pds.example.com", ""));
    }

    [Fact]
    public void Constructor_WithAllowInsecureHttp_AcceptsPlaintextHost()
    {
        // The Aspire container network shape: a containerized consumer reaches the PDS
        // over plaintext HTTP at a non-loopback hostname.
        using var client = new PdsAdminClient(
            new PdsAdminOptions
            {
                Url = "http://pds:3000",
                AdminPassword = AdminPassword,
                AllowInsecureHttp = true,
            },
            null,
            null);

        Assert.Equal("http://pds:3000/", client.PdsUrl.ToString());
    }

    [Fact]
    public void Constructor_PlaintextHostError_NamesTheOptOut()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new PdsAdminClient("http://pds:3000", AdminPassword));

        Assert.Contains("AllowInsecureHttp", ex.Message);
        Assert.Contains("AtProto:Pds:AllowInsecureHttp", ex.Message);
    }

    [Fact]
    public void Constructor_ValidatesSuppliedHttpClientBaseAddress_NotJustTheOptionsUrl()
    {
        using var handler = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            // Where the Authorization header would actually be sent.
            BaseAddress = new Uri("http://pds:3000/"),
        };

        var ex = Assert.Throws<ArgumentException>(() => new PdsAdminClient(
            new PdsAdminOptions { Url = "https://pds.example.com", AdminPassword = AdminPassword },
            httpClient,
            null));

        Assert.Contains("http://pds:3000/", ex.Message);
    }

    // ──────────────────────────────────────────────────────────
    //  Invite codes
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateInviteCodeAsync_AuthenticatesWithAdminPassword()
    {
        _handler.Enqueue("""{"code":"pds-example-com-abc123"}""");

        var code = await _client.CreateInviteCodeAsync();

        Assert.Equal("pds-example-com-abc123", code);

        var request = Assert.Single(_handler.Requests);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"admin:{AdminPassword}"));
        Assert.Equal("Basic", request.AuthScheme);
        Assert.Equal(expected, request.AuthParameter);
        Assert.Contains("com.atproto.server.createInviteCode", request.Path);
    }

    [Fact]
    public async Task CreateInviteCodeAsync_WithZeroUses_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _client.CreateInviteCodeAsync(useCount: 0));
    }

    [Fact]
    public async Task CreateInviteCodesAsync_FlattensCodesAcrossAccounts()
    {
        _handler.Enqueue("""
            {"codes":[{"account":"admin","codes":["code-1","code-2"]},
                      {"account":"other","codes":["code-3"]}]}
            """);

        var codes = await _client.CreateInviteCodesAsync(codeCount: 3);

        Assert.Equal(["code-1", "code-2", "code-3"], codes);
    }

    // ──────────────────────────────────────────────────────────
    //  Account creation
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAccountAsync_WhenInviteRequired_MintsCodeThenCreatesAccount()
    {
        _handler.Enqueue("""{"did":"did:web:pds.example.com","inviteCodeRequired":true}""");
        _handler.Enqueue("""{"code":"minted-code"}""");
        _handler.Enqueue("""{"did":"did:plc:alice","handle":"alice.example.com","accessJwt":"a","refreshJwt":"r"}""");

        var account = await _client.CreateAccountAsync(new CreatePdsAccountRequest
        {
            Handle = "alice.example.com",
            Email = "alice@example.com",
            Password = "correct-horse",
        });

        Assert.Equal("did:plc:alice", account.Did);
        Assert.Equal("alice.example.com", account.Handle);

        Assert.Equal(3, _handler.Requests.Count);
        Assert.Contains("com.atproto.server.describeServer", _handler.Requests[0].Path);
        Assert.Contains("com.atproto.server.createInviteCode", _handler.Requests[1].Path);
        Assert.Contains("com.atproto.server.createAccount", _handler.Requests[2].Path);

        var body = JsonDocument.Parse(_handler.Requests[2].Body!).RootElement;
        Assert.Equal("minted-code", body.GetProperty("inviteCode").GetString());
        Assert.Equal("alice.example.com", body.GetProperty("handle").GetString());
        Assert.Equal("alice@example.com", body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task CreateAccountAsync_SendsSignupWithoutAdminCredentials()
    {
        _handler.Enqueue("""{"did":"did:web:pds.example.com","inviteCodeRequired":false}""");
        _handler.Enqueue("""{"did":"did:plc:alice","handle":"alice.example.com","accessJwt":"a","refreshJwt":"r"}""");

        await _client.CreateAccountAsync(new CreatePdsAccountRequest
        {
            Handle = "alice.example.com",
            Password = "correct-horse",
        });

        // Signup is a public endpoint — leaking the admin password onto it would be a bug.
        var signup = _handler.Requests.Single(r => r.Path.Contains("createAccount"));
        Assert.Null(signup.AuthScheme);
    }

    [Fact]
    public async Task CreateAccountAsync_WhenInviteNotRequired_DoesNotMintCode()
    {
        _handler.Enqueue("""{"did":"did:web:pds.example.com","inviteCodeRequired":false}""");
        _handler.Enqueue("""{"did":"did:plc:alice","handle":"alice.example.com","accessJwt":"a","refreshJwt":"r"}""");

        await _client.CreateAccountAsync(new CreatePdsAccountRequest
        {
            Handle = "alice.example.com",
            Password = "correct-horse",
        });

        Assert.DoesNotContain(_handler.Requests, r => r.Path.Contains("createInviteCode"));
    }

    [Fact]
    public async Task CreateAccountAsync_WithExplicitInviteCode_SkipsDescribeServer()
    {
        _handler.Enqueue("""{"did":"did:plc:alice","handle":"alice.example.com","accessJwt":"a","refreshJwt":"r"}""");

        await _client.CreateAccountAsync(new CreatePdsAccountRequest
        {
            Handle = "alice.example.com",
            Password = "correct-horse",
            InviteCode = "supplied-code",
        });

        var request = Assert.Single(_handler.Requests);
        Assert.Contains("com.atproto.server.createAccount", request.Path);

        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal("supplied-code", body.GetProperty("inviteCode").GetString());
    }

    [Fact]
    public async Task CreateAccountAsync_WithoutHandle_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _client.CreateAccountAsync(new CreatePdsAccountRequest
            {
                Handle = "",
                Password = "correct-horse",
            }));
    }

    // ──────────────────────────────────────────────────────────
    //  Account administration
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAccountAsync_QueriesAdminEndpoint()
    {
        _handler.Enqueue("""
            {"did":"did:plc:alice","handle":"alice.example.com","indexedAt":"2026-07-25T00:00:00.000Z"}
            """);

        var account = await _client.GetAccountAsync("did:plc:alice");

        Assert.Equal("alice.example.com", account.Handle);
        Assert.Contains("com.atproto.admin.getAccountInfo", _handler.Requests[0].Path);
        Assert.Contains("did=did%3Aplc%3Aalice", _handler.Requests[0].Path);
    }

    [Fact]
    public async Task TakedownAccountAsync_SendsRepoRefWithTakedownApplied()
    {
        _handler.Enqueue("""{"subject":{"did":"did:plc:alice"}}""");

        await _client.TakedownAccountAsync("did:plc:alice", reference: "report-42");

        var body = JsonDocument.Parse(_handler.Requests[0].Body!).RootElement;
        Assert.Equal("com.atproto.admin.defs#repoRef", body.GetProperty("subject").GetProperty("$type").GetString());
        Assert.Equal("did:plc:alice", body.GetProperty("subject").GetProperty("did").GetString());
        Assert.True(body.GetProperty("takedown").GetProperty("applied").GetBoolean());
        Assert.Equal("report-42", body.GetProperty("takedown").GetProperty("ref").GetString());
    }

    [Fact]
    public async Task RestoreAccountAsync_SendsTakedownNotApplied()
    {
        _handler.Enqueue("""{"subject":{"did":"did:plc:alice"}}""");

        await _client.RestoreAccountAsync("did:plc:alice");

        var body = JsonDocument.Parse(_handler.Requests[0].Body!).RootElement;
        Assert.False(body.GetProperty("takedown").GetProperty("applied").GetBoolean());
    }

    [Fact]
    public async Task UpdateAccountHandleAsync_PostsToAdminEndpoint()
    {
        _handler.Enqueue("{}");

        await _client.UpdateAccountHandleAsync("did:plc:alice", "alice2.example.com");

        var request = Assert.Single(_handler.Requests);
        Assert.Contains("com.atproto.admin.updateAccountHandle", request.Path);

        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal("alice2.example.com", body.GetProperty("handle").GetString());
    }

    [Fact]
    public async Task DeleteAccountAsync_PostsToAdminEndpoint()
    {
        _handler.Enqueue("{}");

        await _client.DeleteAccountAsync("did:plc:alice");

        var request = Assert.Single(_handler.Requests);
        Assert.Contains("com.atproto.admin.deleteAccount", request.Path);
        Assert.Equal("Basic", request.AuthScheme);
    }

    [Fact]
    public async Task VoidAdminProcedures_TolerateAnEmptyResponseBody()
    {
        // The reference PDS answers all of these with 200 and no body. Asking for a
        // deserialized response throws JsonException on the empty payload, which broke
        // every one of them against a real server.
        var calls = new List<Func<Task>>
        {
            () => _client.DeleteAccountAsync("did:plc:alice"),
            () => _client.UpdateAccountHandleAsync("did:plc:alice", "alice2.example.com"),
            () => _client.UpdateAccountEmailAsync("did:plc:alice", "new@example.com"),
            () => _client.UpdateAccountPasswordAsync("did:plc:alice", "new-password"),
            () => _client.Admin.DisableAccountInvitesAsync("did:plc:alice"),
            () => _client.Admin.EnableAccountInvitesAsync("did:plc:alice"),
            () => _client.Admin.DisableInviteCodesAsync(["code-1"]),
        };

        foreach (var call in calls)
        {
            _handler.Enqueue("");
            await call();
        }

        Assert.Equal(calls.Count, _handler.Requests.Count);
    }

    [Fact]
    public void CreateClient_TargetsTheSamePds()
    {
        using var client = _client.CreateClient();

        Assert.Equal("https://pds.example.com", client.PdsUrl);
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record CapturedRequest(
        string Path, string? AuthScheme, string? AuthParameter, string? Body);

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new();

        public List<CapturedRequest> Requests { get; } = [];

        public void Enqueue(string json) => _responses.Enqueue(json);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.RequestUri!.PathAndQuery,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                body));

            var json = _responses.Count > 0 ? _responses.Dequeue() : "{}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
