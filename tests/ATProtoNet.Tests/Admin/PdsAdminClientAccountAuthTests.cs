using System.Net;
using ATProtoNet.Admin;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Server;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Admin;

/// <summary>
/// <see cref="PdsAdminClient"/> under <see cref="PdsAdminAuthentication.AdminAccount"/> —
/// the scheme a Tranquil PDS uses, where there is no server-wide admin password and
/// administration goes through the session of an account flagged as an administrator.
/// </summary>
public class PdsAdminClientAccountAuthTests : IDisposable
{
    private const string AdminHandle = "pdsadmin.pds.example.com";
    private const string AdminPassword = "hunter2";

    private const string SessionJson = """
        {"did":"did:plc:admin","handle":"pdsadmin.pds.example.com",
         "accessJwt":"access-1","refreshJwt":"refresh-1"}
        """;

    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly PdsAdminClient _client;

    public PdsAdminClientAccountAuthTests()
    {
        _httpClient = new HttpClient(_stub)
        {
            BaseAddress = new Uri("https://pds.example.com/"),
        };

        _client = new PdsAdminClient(Options(), _httpClient, null);
    }

    private static PdsAdminOptions Options() => new()
    {
        Url = "https://pds.example.com",
        Authentication = PdsAdminAuthentication.AdminAccount,
        AdminIdentifier = AdminHandle,
        AdminPassword = AdminPassword,
    };

    // ──────────────────────────────────────────────────────────
    //  Construction
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithoutAnIdentifier_Throws()
    {
        var options = Options();
        options.AdminIdentifier = null;

        var ex = Assert.Throws<ArgumentException>(() => new PdsAdminClient(options, null, null));

        Assert.Contains("AdminIdentifier", ex.Message);
    }

    [Fact]
    public void Constructor_DoesNotSignInEagerly()
    {
        // The administrator account may not exist yet: on a fresh Tranquil instance the
        // application registers it, and the client is resolved before that happens.
        Assert.Empty(_stub.Requests);
        Assert.Equal(PdsAdminAuthentication.AdminAccount, _client.Authentication);
    }

    // ──────────────────────────────────────────────────────────
    //  Session establishment
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminCall_SignsInFirstAndSendsABearerToken()
    {
        _stub.On("com.atproto.server.createSession", SessionJson);
        _stub.On("com.atproto.server.createInviteCode", """{"code":"pds-example-com-abc123"}""");

        var code = await _client.CreateInviteCodeAsync();

        Assert.Equal("pds-example-com-abc123", code);
        Assert.Equal(2, _stub.Requests.Count);

        var login = Assert.Single(_stub.To("com.atproto.server.createSession"));
        Assert.Null(login.Headers.Authorization);

        var body = login.JsonBody;
        Assert.Equal(AdminHandle, body.GetProperty("identifier").GetString());
        Assert.Equal(AdminPassword, body.GetProperty("password").GetString());

        var invite = Assert.Single(_stub.To("com.atproto.server.createInviteCode"));

        // Basic here would be the reference PDS's scheme, which Tranquil does not accept.
        Assert.Equal("Bearer", invite.Headers.Authorization?.Scheme);
        Assert.Equal("access-1", invite.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task AdminCalls_ReuseOneSession()
    {
        _stub.On("com.atproto.server.createSession", SessionJson);
        _stub.On("com.atproto.admin.deleteAccount", "{}");
        _stub.On("com.atproto.admin.updateAccountHandle", "{}");

        await _client.DeleteAccountAsync(Did.Parse("did:plc:alice"));
        await _client.UpdateAccountHandleAsync(Did.Parse("did:plc:bob"), Handle.Parse("bob2.example.com"));

        Assert.Single(_stub.To("com.atproto.server.createSession"));
    }

    [Fact]
    public async Task EnsureAdminSessionAsync_MakesTheRawClientsUsable()
    {
        _stub.On("com.atproto.server.createSession", SessionJson);
        _stub.On("com.atproto.admin.getAccountInfo", """{"did":"did:plc:alice","handle":"alice.example.com","indexedAt":"2026-07-25T00:00:00.000Z"}""");

        await _client.EnsureAdminSessionAsync();
        await _client.Admin.GetAccountInfoAsync(Did.Parse("did:plc:alice"));

        Assert.Equal("Bearer", Assert.Single(_stub.To("com.atproto.admin.getAccountInfo")).Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task ConcurrentAdminCalls_SignInOnlyOnce()
    {
        _stub.On("com.atproto.server.createSession", SessionJson);
        _stub.On("com.atproto.admin.deleteAccount", "{}");

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => _client.DeleteAccountAsync(Did.Parse("did:plc:alice"))));

        Assert.Single(_stub.To("com.atproto.server.createSession"));
    }

    // ──────────────────────────────────────────────────────────
    //  Session expiry
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminCall_WhenTheSessionIsRejected_SignsInAgainAndRetries()
    {
        _stub.On("com.atproto.server.createSession", SessionJson);
        _stub.On("com.atproto.server.createSession", SessionJson.Replace("access-1", "access-2"));
        _stub.On("com.atproto.admin.deleteAccount", HttpStatusCode.Unauthorized, """{"error":"ExpiredToken","message":"Token has expired"}""");
        _stub.On("com.atproto.admin.deleteAccount", "{}");

        // The client is registered as a typed HttpClient and outlives its access tokens,
        // so an expired one has to be recoverable rather than fatal.
        await _client.DeleteAccountAsync(Did.Parse("did:plc:alice"));

        Assert.Equal(4, _stub.Requests.Count);
        Assert.Equal(2, _stub.To("com.atproto.server.createSession").Count());
        Assert.Equal("access-2", _stub.To("com.atproto.admin.deleteAccount").Last().Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task AdminCall_WhenTheRetryIsAlsoRejected_Throws()
    {
        _stub.On("com.atproto.server.createSession", SessionJson);
        _stub.On("com.atproto.server.createSession", SessionJson);
        _stub.On("com.atproto.admin.deleteAccount", HttpStatusCode.Unauthorized, """{"error":"ExpiredToken"}""");
        _stub.On("com.atproto.admin.deleteAccount", HttpStatusCode.Unauthorized, """{"error":"ExpiredToken"}""");

        var ex = await Assert.ThrowsAsync<XrpcAuthenticationException>(
            () => _client.DeleteAccountAsync(Did.Parse("did:plc:alice")));

        // One retry, not a loop: a password that has stopped working must surface.
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal(4, _stub.Requests.Count);
    }

    [Fact]
    public async Task SearchAccountsAsync_SignsInAndRetriesARejectedSession()
    {
        // Tranquil serves searchAccounts, so it goes through the same session handling.
        _stub.On("com.atproto.server.createSession", SessionJson);
        _stub.On("com.atproto.server.createSession", SessionJson.Replace("access-1", "access-2"));
        _stub.On("com.atproto.admin.searchAccounts", HttpStatusCode.Unauthorized, """{"error":"ExpiredToken"}""");
        _stub.On("com.atproto.admin.searchAccounts", """{"accounts":[]}""");

        var page = await _client.SearchAccountsAsync(email: "alice@example.com");

        Assert.Empty(page.Accounts);
        var retry = _stub.To("com.atproto.admin.searchAccounts").Last();
        Assert.Equal("Bearer", retry.Headers.Authorization?.Scheme);
        Assert.Equal("access-2", retry.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task AdminCall_DoesNotRetryOtherErrors()
    {
        _stub.On("com.atproto.server.createSession", SessionJson);
        _stub.On("com.atproto.admin.deleteAccount", HttpStatusCode.BadRequest, """{"error":"InvalidRequest","message":"nope"}""");

        await Assert.ThrowsAsync<XrpcException>(
            () => _client.DeleteAccountAsync(Did.Parse("did:plc:alice")));

        Assert.Equal(2, _stub.Requests.Count);
    }

    // ──────────────────────────────────────────────────────────
    //  Public endpoints
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAccountAsync_WhenInvitesAreOff_NeedsNoSession()
    {
        _stub.On("com.atproto.server.describeServer", """{"did":"did:web:pds.example.com","inviteCodeRequired":false}""");
        _stub.On("com.atproto.server.createAccount", """{"did":"did:plc:admin","handle":"pdsadmin.pds.example.com","accessJwt":"a","refreshJwt":"r"}""");

        // This is how the administrator account itself gets created: Tranquil flags the
        // first account on an empty instance as an administrator, and signup is public —
        // so it has to work before the client has any admin authority at all.
        var account = await _client.CreateAccountAsync(new CreateAccountRequest
        {
            Handle = Handle.Parse(AdminHandle),
            Email = "admin@example.com",
            Password = AdminPassword,
        });

        Assert.Equal("did:plc:admin", account.Did);
        Assert.Empty(_stub.To("com.atproto.server.createSession"));
        Assert.All(_stub.Requests, r => Assert.Null(r.Headers.Authorization));
    }

    [Fact]
    public async Task DescribeServerAsync_NeedsNoSession()
    {
        _stub.On("com.atproto.server.describeServer", """{"did":"did:web:pds.example.com","inviteCodeRequired":false}""");

        await _client.DescribeServerAsync();

        var request = Assert.Single(_stub.Requests);
        Assert.Null(request.Headers.Authorization);
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _stub.Dispose();
        GC.SuppressFinalize(this);
    }
}
