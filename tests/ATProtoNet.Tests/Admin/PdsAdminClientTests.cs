using ATProtoNet.Admin;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Server;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Admin;

public class PdsAdminClientTests : IDisposable
{
    private const string AdminPassword = "hunter2";

    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly PdsAdminClient _client;

    public PdsAdminClientTests()
    {
        _httpClient = new HttpClient(_stub)
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
    public async Task Constructor_SendsToTheOptionsUrl_NotASuppliedClientsBaseAddress()
    {
        using var stub = new HttpStub();
        using var httpClient = new HttpClient(stub)
        {
            BaseAddress = new Uri("http://pds:3000/"),
        };
        using var client = new PdsAdminClient(
            new PdsAdminOptions { Url = "https://pds.example.com", AdminPassword = AdminPassword },
            httpClient,
            null);
        stub.On("com.atproto.server.createInviteCode", """{"code":"pds-example-com-abc123"}""");

        await client.CreateInviteCodeAsync();

        // The Authorization header goes where the validated options URL points, and the
        // supplied client is left as it was.
        Assert.Equal(new Uri("https://pds.example.com/"), client.PdsUrl);
        Assert.Equal("pds.example.com", Assert.Single(stub.Requests).Uri.Host);
        Assert.Equal(new Uri("http://pds:3000/"), httpClient.BaseAddress);
    }

    // ──────────────────────────────────────────────────────────
    //  Invite codes
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateInviteCodeAsync_AuthenticatesWithAdminPassword()
    {
        _stub.On("com.atproto.server.createInviteCode", """{"code":"pds-example-com-abc123"}""");

        var code = await _client.CreateInviteCodeAsync();

        Assert.Equal("pds-example-com-abc123", code);

        var request = Assert.Single(_stub.Requests);
        var expected = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"admin:{AdminPassword}"));
        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
        Assert.Equal(expected, request.Headers.Authorization?.Parameter);
        Assert.Equal("com.atproto.server.createInviteCode", request.Nsid);
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
        _stub.On("com.atproto.server.createInviteCodes", """
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
        _stub.On("com.atproto.server.describeServer", """{"did":"did:web:pds.example.com","inviteCodeRequired":true}""");
        _stub.On("com.atproto.server.createInviteCode", """{"code":"minted-code"}""");
        _stub.On("com.atproto.server.createAccount", """{"did":"did:plc:alice","handle":"alice.example.com","accessJwt":"a","refreshJwt":"r"}""");

        var account = await _client.CreateAccountAsync(new CreateAccountRequest
        {
            Handle = Handle.Parse("alice.example.com"),
            Email = "alice@example.com",
            Password = "correct-horse",
        });

        Assert.Equal("did:plc:alice", account.Did);
        Assert.Equal("alice.example.com", account.Handle);

        Assert.Equal(3, _stub.Requests.Count);
        Assert.Equal("com.atproto.server.describeServer", _stub.Requests[0].Nsid);
        Assert.Equal("com.atproto.server.createInviteCode", _stub.Requests[1].Nsid);
        Assert.Equal("com.atproto.server.createAccount", _stub.Requests[2].Nsid);

        var body = _stub.Requests[2].JsonBody;
        Assert.Equal("minted-code", body.GetProperty("inviteCode").GetString());
        Assert.Equal("alice.example.com", body.GetProperty("handle").GetString());
        Assert.Equal("alice@example.com", body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task CreateAccountAsync_SendsSignupWithoutAdminCredentials()
    {
        _stub.On("com.atproto.server.describeServer", """{"did":"did:web:pds.example.com","inviteCodeRequired":false}""");
        _stub.On("com.atproto.server.createAccount", """{"did":"did:plc:alice","handle":"alice.example.com","accessJwt":"a","refreshJwt":"r"}""");

        await _client.CreateAccountAsync(new CreateAccountRequest
        {
            Handle = Handle.Parse("alice.example.com"),
            Password = "correct-horse",
        });

        // Signup is a public endpoint — leaking the admin password onto it would be a bug.
        var signup = Assert.Single(_stub.To("com.atproto.server.createAccount"));
        Assert.Null(signup.Headers.Authorization);
    }

    [Fact]
    public async Task CreateAccountAsync_WhenInviteNotRequired_DoesNotMintCode()
    {
        _stub.On("com.atproto.server.describeServer", """{"did":"did:web:pds.example.com","inviteCodeRequired":false}""");
        _stub.On("com.atproto.server.createAccount", """{"did":"did:plc:alice","handle":"alice.example.com","accessJwt":"a","refreshJwt":"r"}""");

        await _client.CreateAccountAsync(new CreateAccountRequest
        {
            Handle = Handle.Parse("alice.example.com"),
            Password = "correct-horse",
        });

        Assert.Empty(_stub.To("com.atproto.server.createInviteCode"));
    }

    [Fact]
    public async Task CreateAccountAsync_WithExplicitInviteCode_SkipsDescribeServer()
    {
        _stub.On("com.atproto.server.createAccount", """{"did":"did:plc:alice","handle":"alice.example.com","accessJwt":"a","refreshJwt":"r"}""");

        await _client.CreateAccountAsync(new CreateAccountRequest
        {
            Handle = Handle.Parse("alice.example.com"),
            Password = "correct-horse",
            InviteCode = "supplied-code",
        });

        var request = Assert.Single(_stub.Requests);
        Assert.Equal("com.atproto.server.createAccount", request.Nsid);

        var body = request.JsonBody;
        Assert.Equal("supplied-code", body.GetProperty("inviteCode").GetString());
    }

    [Fact]
    public async Task CreateAccountAsync_WithoutHandle_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _client.CreateAccountAsync(new CreateAccountRequest
            {
                Handle = Handle.Parse(""),
                Password = "correct-horse",
            }));
    }

    // ──────────────────────────────────────────────────────────
    //  Account administration
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAccountAsync_QueriesAdminEndpoint()
    {
        _stub.On("com.atproto.admin.getAccountInfo", """
            {"did":"did:plc:alice","handle":"alice.example.com","indexedAt":"2026-07-25T00:00:00.000Z"}
            """);

        var account = await _client.GetAccountAsync(Did.Parse("did:plc:alice"));

        Assert.Equal("alice.example.com", account.Handle);
        var request = Assert.Single(_stub.To("com.atproto.admin.getAccountInfo"));
        Assert.Contains("did=did%3Aplc%3Aalice", request.Query);
    }

    [Fact]
    public async Task SearchAccountsAsync_QueriesAdminEndpointWithAdminAuth()
    {
        _stub.On("com.atproto.admin.searchAccounts", """
            {"cursor":"c2","accounts":[{"did":"did:plc:alice","handle":"alice.example.com","indexedAt":"2026-07-25T00:00:00.000Z"}]}
            """);

        var page = await _client.SearchAccountsAsync(email: "alice@example.com", limit: 5);

        var request = Assert.Single(_stub.Requests);
        Assert.Equal("com.atproto.admin.searchAccounts", request.Nsid);
        Assert.Contains("email=alice%40example.com", request.Query);
        Assert.Contains("limit=5", request.Query);
        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
        Assert.Equal("c2", page.Cursor);
        Assert.Equal("alice.example.com", Assert.Single(page.Accounts).Handle);
    }

    [Fact]
    public async Task EnumerateSearchAccountsAsync_FetchesEveryPage()
    {
        _stub.On("com.atproto.admin.searchAccounts", """
            {"cursor":"c2","accounts":[{"did":"did:plc:alice","handle":"alice.example.com","indexedAt":"2026-07-25T00:00:00.000Z"}]}
            """);
        _stub.On("com.atproto.admin.searchAccounts", """
            {"accounts":[{"did":"did:plc:bob","handle":"bob.example.com","indexedAt":"2026-07-25T00:00:00.000Z"}]}
            """);

        var accounts = await _client.EnumerateSearchAccountsAsync(pageSize: 1).ToListAsync();

        Assert.Equal(["alice.example.com", "bob.example.com"], accounts.Select(a => a.Handle.Value));
        Assert.Contains("cursor=c2", _stub.To("com.atproto.admin.searchAccounts").Last().Query);
    }

    [Fact]
    public async Task TakedownAccountAsync_SendsRepoRefWithTakedownApplied()
    {
        _stub.On("com.atproto.admin.updateSubjectStatus", """{"subject":{"$type":"com.atproto.admin.defs#repoRef","did":"did:plc:alice"}}""");

        await _client.TakedownAccountAsync(Did.Parse("did:plc:alice"), reference: "report-42");

        var body = Assert.Single(_stub.Requests).JsonBody;
        Assert.Equal("com.atproto.admin.defs#repoRef", body.GetProperty("subject").GetProperty("$type").GetString());
        Assert.Equal("did:plc:alice", body.GetProperty("subject").GetProperty("did").GetString());
        Assert.True(body.GetProperty("takedown").GetProperty("applied").GetBoolean());
        Assert.Equal("report-42", body.GetProperty("takedown").GetProperty("ref").GetString());
    }

    [Fact]
    public async Task RestoreAccountAsync_SendsTakedownNotApplied()
    {
        _stub.On("com.atproto.admin.updateSubjectStatus", """{"subject":{"$type":"com.atproto.admin.defs#repoRef","did":"did:plc:alice"}}""");

        await _client.RestoreAccountAsync(Did.Parse("did:plc:alice"));

        var body = Assert.Single(_stub.Requests).JsonBody;
        Assert.False(body.GetProperty("takedown").GetProperty("applied").GetBoolean());
    }

    [Fact]
    public async Task UpdateAccountHandleAsync_PostsToAdminEndpoint()
    {
        _stub.On("com.atproto.admin.updateAccountHandle", "{}");

        await _client.UpdateAccountHandleAsync(Did.Parse("did:plc:alice"), Handle.Parse("alice2.example.com"));

        var request = Assert.Single(_stub.Requests);
        Assert.Equal("com.atproto.admin.updateAccountHandle", request.Nsid);

        var body = request.JsonBody;
        Assert.Equal("alice2.example.com", body.GetProperty("handle").GetString());
    }

    [Fact]
    public async Task DeleteAccountAsync_PostsToAdminEndpoint()
    {
        _stub.On("com.atproto.admin.deleteAccount", "{}");

        await _client.DeleteAccountAsync(Did.Parse("did:plc:alice"));

        var request = Assert.Single(_stub.Requests);
        Assert.Equal("com.atproto.admin.deleteAccount", request.Nsid);
        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task VoidAdminProcedures_TolerateAnEmptyResponseBody()
    {
        // The reference PDS answers all of these with 200 and no body. Asking for a
        // deserialized response throws JsonException on the empty payload, which broke
        // every one of them against a real server.
        (string Nsid, Func<Task> Call)[] calls =
        [
            ("com.atproto.admin.deleteAccount", () => _client.DeleteAccountAsync(Did.Parse("did:plc:alice"))),
            ("com.atproto.admin.updateAccountHandle", () => _client.UpdateAccountHandleAsync(Did.Parse("did:plc:alice"), Handle.Parse("alice2.example.com"))),
            ("com.atproto.admin.updateAccountEmail", () => _client.UpdateAccountEmailAsync(AtIdentifier.Parse("did:plc:alice"), "new@example.com")),
            ("com.atproto.admin.updateAccountPassword", () => _client.UpdateAccountPasswordAsync(Did.Parse("did:plc:alice"), "new-password")),
            ("com.atproto.admin.disableAccountInvites", () => _client.Admin.DisableAccountInvitesAsync(Did.Parse("did:plc:alice"))),
            ("com.atproto.admin.enableAccountInvites", () => _client.Admin.EnableAccountInvitesAsync(Did.Parse("did:plc:alice"))),
            ("com.atproto.admin.disableInviteCodes", () => _client.Admin.DisableInviteCodesAsync(["code-1"])),
        ];

        foreach (var (nsid, call) in calls)
        {
            _stub.On(nsid, "");
            await call();
        }

        Assert.Equal(calls.Length, _stub.Requests.Count);
    }

    [Fact]
    public void CreateClient_TargetsTheSamePds()
    {
        using var client = _client.CreateClient();

        Assert.Equal(new Uri("https://pds.example.com/"), client.ServiceUrl);
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _stub.Dispose();
        GC.SuppressFinalize(this);
    }
}
