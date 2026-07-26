using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Admin;
using ATProtoNet.Http;

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

    private readonly MockHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly PdsAdminClient _client;

    public PdsAdminClientAccountAuthTests()
    {
        _httpClient = new HttpClient(_handler)
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
        Assert.Empty(_handler.Requests);
        Assert.Equal(PdsAdminAuthentication.AdminAccount, _client.Authentication);
    }

    // ──────────────────────────────────────────────────────────
    //  Session establishment
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminCall_SignsInFirstAndSendsABearerToken()
    {
        _handler.Enqueue(SessionJson);
        _handler.Enqueue("""{"code":"pds-example-com-abc123"}""");

        var code = await _client.CreateInviteCodeAsync();

        Assert.Equal("pds-example-com-abc123", code);
        Assert.Equal(2, _handler.Requests.Count);

        var login = _handler.Requests[0];
        Assert.Contains("com.atproto.server.createSession", login.Path);
        Assert.Null(login.AuthScheme);

        var body = JsonDocument.Parse(login.Body!).RootElement;
        Assert.Equal(AdminHandle, body.GetProperty("identifier").GetString());
        Assert.Equal(AdminPassword, body.GetProperty("password").GetString());

        var invite = _handler.Requests[1];
        Assert.Contains("com.atproto.server.createInviteCode", invite.Path);

        // Basic here would be the reference PDS's scheme, which Tranquil does not accept.
        Assert.Equal("Bearer", invite.AuthScheme);
        Assert.Equal("access-1", invite.AuthParameter);
    }

    [Fact]
    public async Task AdminCalls_ReuseOneSession()
    {
        _handler.Enqueue(SessionJson);
        _handler.Enqueue("{}");
        _handler.Enqueue("{}");

        await _client.DeleteAccountAsync("did:plc:alice");
        await _client.UpdateAccountHandleAsync("did:plc:bob", "bob2.example.com");

        Assert.Equal(1, _handler.Requests.Count(r => r.Path.Contains("createSession")));
    }

    [Fact]
    public async Task EnsureAdminSessionAsync_MakesTheRawClientsUsable()
    {
        _handler.Enqueue(SessionJson);
        _handler.Enqueue("""{"did":"did:plc:alice","handle":"alice.example.com","indexedAt":"2026-07-25T00:00:00.000Z"}""");

        await _client.EnsureAdminSessionAsync();
        await _client.Admin.GetAccountInfoAsync("did:plc:alice");

        Assert.Equal("Bearer", _handler.Requests[1].AuthScheme);
    }

    [Fact]
    public async Task ConcurrentAdminCalls_SignInOnlyOnce()
    {
        _handler.Enqueue(SessionJson);

        for (var i = 0; i < 8; i++)
        {
            _handler.Enqueue("{}");
        }

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => _client.DeleteAccountAsync("did:plc:alice")));

        Assert.Equal(1, _handler.Requests.Count(r => r.Path.Contains("createSession")));
    }

    // ──────────────────────────────────────────────────────────
    //  Session expiry
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminCall_WhenTheSessionIsRejected_SignsInAgainAndRetries()
    {
        _handler.Enqueue(SessionJson);
        _handler.Enqueue("""{"error":"ExpiredToken","message":"Token has expired"}""", HttpStatusCode.Unauthorized);
        _handler.Enqueue(SessionJson.Replace("access-1", "access-2"));
        _handler.Enqueue("{}");

        // The client is registered as a typed HttpClient and outlives its access tokens,
        // so an expired one has to be recoverable rather than fatal.
        await _client.DeleteAccountAsync("did:plc:alice");

        Assert.Equal(4, _handler.Requests.Count);
        Assert.Equal(2, _handler.Requests.Count(r => r.Path.Contains("createSession")));
        Assert.Equal("access-2", _handler.Requests[3].AuthParameter);
    }

    [Fact]
    public async Task AdminCall_WhenTheRetryIsAlsoRejected_Throws()
    {
        _handler.Enqueue(SessionJson);
        _handler.Enqueue("""{"error":"ExpiredToken"}""", HttpStatusCode.Unauthorized);
        _handler.Enqueue(SessionJson);
        _handler.Enqueue("""{"error":"ExpiredToken"}""", HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<AtProtoHttpException>(
            () => _client.DeleteAccountAsync("did:plc:alice"));

        // One retry, not a loop: a password that has stopped working must surface.
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal(4, _handler.Requests.Count);
    }

    [Fact]
    public async Task AdminCall_DoesNotRetryOtherErrors()
    {
        _handler.Enqueue(SessionJson);
        _handler.Enqueue("""{"error":"InvalidRequest","message":"nope"}""", HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<AtProtoHttpException>(
            () => _client.DeleteAccountAsync("did:plc:alice"));

        Assert.Equal(2, _handler.Requests.Count);
    }

    // ──────────────────────────────────────────────────────────
    //  Public endpoints
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAccountAsync_WhenInvitesAreOff_NeedsNoSession()
    {
        _handler.Enqueue("""{"did":"did:web:pds.example.com","inviteCodeRequired":false}""");
        _handler.Enqueue("""{"did":"did:plc:admin","handle":"pdsadmin.pds.example.com","accessJwt":"a","refreshJwt":"r"}""");

        // This is how the administrator account itself gets created: Tranquil flags the
        // first account on an empty instance as an administrator, and signup is public —
        // so it has to work before the client has any admin authority at all.
        var account = await _client.CreateAccountAsync(new CreatePdsAccountRequest
        {
            Handle = AdminHandle,
            Email = "admin@example.com",
            Password = AdminPassword,
        });

        Assert.Equal("did:plc:admin", account.Did);
        Assert.DoesNotContain(_handler.Requests, r => r.Path.Contains("createSession"));
        Assert.All(_handler.Requests, r => Assert.Null(r.AuthScheme));
    }

    [Fact]
    public async Task DescribeServerAsync_NeedsNoSession()
    {
        _handler.Enqueue("""{"did":"did:web:pds.example.com","inviteCodeRequired":false}""");

        await _client.DescribeServerAsync();

        var request = Assert.Single(_handler.Requests);
        Assert.Null(request.AuthScheme);
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
        private readonly Queue<(string Json, HttpStatusCode Status)> _responses = new();
        private readonly Lock _gate = new();

        public List<CapturedRequest> Requests { get; } = [];

        public void Enqueue(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            _responses.Enqueue((json, status));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var captured = new CapturedRequest(
                request.RequestUri!.PathAndQuery,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                body);

            lock (_gate)
            {
                Requests.Add(captured);

                var (json, status) = _responses.Count > 0
                    ? _responses.Dequeue()
                    : ("{}", HttpStatusCode.OK);

                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }
        }
    }
}
