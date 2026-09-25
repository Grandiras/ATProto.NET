using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Server;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Auth;

/// <summary>
/// The lifecycle of a password session against a scripted PDS: sign-in, resume, refresh
/// (proactive, reactive and single-flight), sign-out and disposal.
/// </summary>
public sealed class PasswordSessionLifecycleTests : IDisposable
{
    private static readonly Nsid Ping = Nsid.Parse("com.example.ping");

    private readonly StubServer _server = new();
    private readonly HttpClient _http;
    private readonly List<AtProtoSessionChangedEventArgs> _changes = [];

    public PasswordSessionLifecycleTests()
    {
        _http = new HttpClient(_server);
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    private AtProtoClient NewClient(
        IAtProtoSessionStore? store = null, Action<AtProtoClientOptions>? configure = null,
        TimeProvider? time = null, string instanceUrl = "https://pds.example.com")
    {
        var client = Client(_http, store, configure, time, instanceUrl);
        client.SessionChanged += (_, change) =>
        {
            lock (_changes)
                _changes.Add(change);
        };
        return client;
    }

    private AtProtoSessionChange[] Changes()
    {
        lock (_changes)
            return [.. _changes.Select(c => c.Change)];
    }

    // ──────────────────────────────────────────────────────────
    //  Sign-in
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_InstallsAPasswordSessionExpiringWhenItsAccessJwtDoes()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(2);
        var access = AccessJwt("a1", expiresAt);
        _server.Respond = _ => SessionResponse(access, "r1");
        var store = new InMemoryAtProtoSessionStore();
        await using var client = NewClient(store);

        var session = await client.LoginAsync("alice.test", "password");

        Assert.Equal(Alice, session.Did);
        Assert.Equal(access, session.AccessJwt);
        Assert.Equal("r1", session.RefreshJwt);
        Assert.Equal(expiresAt.ToUnixTimeSeconds(), session.ExpiresAt?.ToUnixTimeSeconds());
        Assert.Same(session, client.Session);
        Assert.Same(session, await store.GetAsync(Alice));
        Assert.Equal([AtProtoSessionChange.Created], Changes());

        // Sign-in carries no credentials of its own.
        Assert.Null(Assert.Single(_server.Requests).Authorization);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, true)]
    public async Task LoginAsync_AllowTakendown_IsSentOnlyWhenAskedFor(bool allowTakendown, bool? sent)
    {
        _server.Respond = _ => SessionResponse(AccessJwt("a1"), "r1");
        await using var client = NewClient();

        await client.LoginAsync("alice.test", "password", allowTakendown: allowTakendown);

        var body = JsonDocument.Parse(Assert.Single(_server.Requests).Body!).RootElement;
        Assert.Equal(sent, body.TryGetProperty("allowTakendown", out var value) ? value.GetBoolean() : null);
        Assert.Equal("alice.test", body.GetProperty("identifier").GetString());
    }

    [Fact]
    public async Task LoginAsync_ThroughAnEntryway_MovesToThePdsTheDidDocumentNames()
    {
        _server.Respond = r => r.Nsid == "com.atproto.server.createSession"
            ? SessionResponse(AccessJwt("a1"), "r1", pdsInDidDoc: "https://pds.example.net")
            : JsonResponse("{}");
        await using var client = NewClient(instanceUrl: "https://entryway.example.com");

        var session = await client.LoginAsync("alice.test", "password");
        await client.QueryAsync<JsonElement>(Ping);

        Assert.Equal(new Uri("https://pds.example.net/"), session.ServiceEndpoint);
        Assert.Equal(new Uri("https://pds.example.net/"), client.ServiceUrl);
        Assert.Equal("pds.example.net", _server.To("com.example.ping").Single().Uri.Host);
    }

    [Theory]
    [InlineData("https://entryway.example.com", "did:plc:someoneelse", "https://pds.example.net")] // another account's document
    [InlineData("http://localhost:2583", null, "http://localhost:3000")]                          // a development PDS over HTTP
    [InlineData("https://entryway.example.com", null, "http://pds.example.net")]                   // a downgrade to HTTP
    public async Task LoginAsync_DidDocumentThatCannotBeFollowed_StaysOnTheConfiguredService(
        string instanceUrl, string? didDocId, string pdsInDidDoc)
    {
        _server.Respond = _ => SessionResponse(AccessJwt("a1"), "r1", pdsInDidDoc: pdsInDidDoc, didDocId: didDocId);
        await using var client = NewClient(instanceUrl: instanceUrl);

        var session = await client.LoginAsync("alice.test", "password");

        Assert.Equal(new Uri(instanceUrl + "/"), session.ServiceEndpoint);
        Assert.Equal(new Uri(instanceUrl + "/"), client.ServiceUrl);
    }

    [Fact]
    public async Task ARefreshWhoseDidDocumentNamesANewPds_MovesTheSessionThere()
    {
        // The account migrated: the DID document the PDS returns now names another one.
        _server.Respond = r => r.Nsid switch
        {
            "com.atproto.server.createSession" => SessionResponse(AccessJwt("a1"), "r1", pdsInDidDoc: "https://pds.example.com"),
            "com.atproto.server.refreshSession" => SessionResponse(AccessJwt("a2"), "r2", pdsInDidDoc: "https://pds2.example.com"),
            _ => JsonResponse("{}"),
        };
        await using var client = NewClient();
        await client.LoginAsync("alice.test", "password");

        await client.RefreshSessionAsync();
        await client.QueryAsync<JsonElement>(Ping);

        Assert.Equal(new Uri("https://pds2.example.com/"), client.Session!.ServiceEndpoint);
        Assert.Equal("pds2.example.com", _server.To("com.example.ping").Single().Uri.Host);
    }

    [Fact]
    public async Task CreateAccountAndLoginAsync_InstallsTheNewAccountsSession()
    {
        _server.Respond = _ => SessionResponse(AccessJwt("a1"), "r1");
        await using var client = NewClient();

        var session = await client.CreateAccountAndLoginAsync(new CreateAccountRequest
        {
            Handle = AliceHandle,
            Email = "alice@example.com",
            Password = "password",
        });

        Assert.Same(session, client.Session);
        Assert.Equal("alice@example.com", session.Email);
        Assert.Equal("com.atproto.server.createAccount", Assert.Single(_server.Requests).Nsid);
        Assert.Equal([AtProtoSessionChange.Created], Changes());
    }

    // ──────────────────────────────────────────────────────────
    //  Stateless sub-clients
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ServerCreateSessionAsync_ReturnsTokensWithoutInstallingThem()
    {
        _server.Respond = r => r.Nsid == "com.atproto.server.createSession" ? SessionResponse("a1", "r1") : JsonResponse("{}");
        await using var client = NewClient();

        await client.Server.CreateSessionAsync("alice.test", "password");
        await client.QueryAsync<JsonElement>(Ping);

        Assert.False(client.IsAuthenticated);
        Assert.Null(_server.To("com.example.ping").Single().Authorization);
    }

    [Fact]
    public async Task ServerCreateAccountAsync_LeavesTheSignedInSessionInPlace()
    {
        _server.Respond = r => r.Nsid switch
        {
            "com.atproto.server.createSession" => SessionResponse("alice-access", "alice-refresh"),
            "com.atproto.server.createAccount" => SessionResponse("bob-access", "bob-refresh", handle: "bob.test"),
            _ => JsonResponse("{}"),
        };
        await using var client = NewClient();
        var alice = await client.LoginAsync("alice.test", "password");

        await client.Server.CreateAccountAsync(new CreateAccountRequest { Handle = Handle.Parse("bob.test") });
        await client.QueryAsync<JsonElement>(Ping);

        Assert.Same(alice, client.Session);
        Assert.Null(_server.To("com.atproto.server.createAccount").Single().Authorization);
        Assert.Equal("Bearer alice-access", _server.To("com.example.ping").Single().Authorization);
    }

    [Fact]
    public async Task ServerDeleteSessionAsync_SendsTheRefreshJwtAndKeepsTheClientSession()
    {
        _server.Respond = r => r.Nsid == "com.atproto.server.createSession" ? SessionResponse("a1", "r1") : JsonResponse("{}");
        await using var client = NewClient();
        await client.LoginAsync("alice.test", "password");

        await client.Server.DeleteSessionAsync("r1");

        Assert.Equal("Bearer r1", _server.To("com.atproto.server.deleteSession").Single().Authorization);
        Assert.True(client.IsAuthenticated);
    }

    // ──────────────────────────────────────────────────────────
    //  Resume
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResumeSessionAsync_OnAFreshClientWithAnExpiredToken_RefreshesAndRetries()
    {
        var refreshed = AccessJwt("a2");
        _server.Respond = r => r.Nsid switch
        {
            "com.atproto.server.getSession" when r.Authorization == $"Bearer {refreshed}" => GetSessionResponse(),
            "com.atproto.server.getSession" => XrpcError(XrpcErrors.ExpiredToken),
            "com.atproto.server.refreshSession" => SessionResponse(refreshed, "r2"),
            _ => throw new InvalidOperationException(r.Nsid),
        };
        var store = new InMemoryAtProtoSessionStore();
        await using var client = NewClient(store);

        // A saved session whose access token the service now reports expired.
        var resumed = await client.ResumeSessionAsync(PasswordSession(AccessJwt("a1"), "r1"));

        var session = Assert.IsType<PasswordSession>(resumed);
        Assert.Equal(refreshed, session.AccessJwt);
        Assert.Equal("r2", session.RefreshJwt);
        Assert.Same(session, client.Session);
        Assert.Equal(session, await store.GetAsync(Alice));
        Assert.Equal("Bearer r1", _server.To("com.atproto.server.refreshSession").Single().Authorization);
        Assert.Equal([AtProtoSessionChange.Created, AtProtoSessionChange.Refreshed], Changes());
    }

    [Fact]
    public async Task ResumeSessionAsync_TakesTheAccountDetailsTheServiceReports()
    {
        _server.Respond = _ => GetSessionResponse(handle: "alice2.test", email: "alice@example.com");
        await using var client = NewClient();

        var resumed = await client.ResumeSessionAsync(PasswordSession(AccessJwt("a1"), "r1"));

        var session = Assert.IsType<PasswordSession>(resumed);
        Assert.Equal("alice2.test", session.Handle.Value);
        Assert.Equal("alice@example.com", session.Email);
        Assert.Equal(Handle.Parse("alice2.test"), client.Handle);
    }

    [Fact]
    public async Task TryRestoreSessionAsync_InstallsTheStoredSessionWithoutARequest()
    {
        var store = new InMemoryAtProtoSessionStore();
        var saved = PasswordSession(AccessJwt("a1"), "r1");
        await store.SetAsync(saved);
        await using var client = NewClient(store);

        await using var other = NewClient(store);

        Assert.True(await client.TryRestoreSessionAsync(Alice));
        Assert.False(await other.TryRestoreSessionAsync(Did.Parse("did:plc:nobody")));

        Assert.Same(saved, client.Session);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task TryRestoreSessionAsync_WithoutAStore_Throws()
    {
        await using var client = NewClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.TryRestoreSessionAsync(Alice));
    }

    // ──────────────────────────────────────────────────────────
    //  Refresh
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ARequestRejectedWithExpiredToken_IsRetriedOnceAfterARefresh()
    {
        var refreshed = AccessJwt("a2");
        _server.Respond = r => r.Nsid switch
        {
            "com.atproto.server.createSession" => SessionResponse(AccessJwt("a1"), "r1"),
            "com.atproto.server.refreshSession" => SessionResponse(refreshed, "r2"),
            "com.example.ping" when r.Authorization == $"Bearer {refreshed}" => JsonResponse("""{"ok":true}"""),
            "com.example.ping" => XrpcError(XrpcErrors.ExpiredToken),
            _ => throw new InvalidOperationException(r.Nsid),
        };
        await using var client = NewClient();
        await client.LoginAsync("alice.test", "password");

        var result = await client.QueryAsync<JsonElement>(Ping);

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(2, _server.To("com.example.ping").Count);
        Assert.Single(_server.To("com.atproto.server.refreshSession"));
        Assert.Equal(refreshed, Assert.IsType<PasswordSession>(client.Session).AccessJwt);
    }

    [Fact]
    public async Task ConcurrentCallsWithAnExpiredToken_ShareOneRefresh()
    {
        const int callers = 16;
        var refreshed = AccessJwt("a2");
        var rejections = 0;
        var allRejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _server.Handler = async (r, ct) =>
        {
            switch (r.Nsid)
            {
                case "com.atproto.server.createSession":
                    return SessionResponse(AccessJwt("a1"), "r1");
                case "com.atproto.server.refreshSession":
                    // Held until every caller has been turned away, so all of them want a refresh at once.
                    await allRejected.Task.WaitAsync(ct);
                    return SessionResponse(refreshed, "r2");
                case "com.example.ping" when r.Authorization == $"Bearer {refreshed}":
                    return JsonResponse("{}");
                case "com.example.ping":
                    if (Interlocked.Increment(ref rejections) == callers)
                        allRejected.SetResult();
                    return XrpcError(XrpcErrors.ExpiredToken);
                default:
                    throw new InvalidOperationException(r.Nsid);
            }
        };
        await using var client = NewClient();
        await client.LoginAsync("alice.test", "password");

        await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => client.QueryAsync<JsonElement>(Ping)));

        // A second refresh would have sent the spent r1 again.
        Assert.Single(_server.To("com.atproto.server.refreshSession"));
        Assert.Equal(callers, _server.To("com.example.ping").Count(r => r.Authorization == $"Bearer {refreshed}"));
    }

    [Fact]
    public async Task ConcurrentRefreshSessionAsyncCalls_ShareOneRefresh()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Handler = async (r, ct) =>
        {
            if (r.Nsid == "com.atproto.server.refreshSession")
                await release.Task.WaitAsync(ct);
            return SessionResponse(AccessJwt(r.Nsid!), "r2");
        };
        await using var client = NewClient();
        await client.LoginAsync("alice.test", "password");

        var refreshes = Enumerable.Range(0, 8).Select(_ => client.RefreshSessionAsync()).ToList();
        release.SetResult();
        await Task.WhenAll(refreshes);

        Assert.Single(_server.To("com.atproto.server.refreshSession"));
    }

    [Fact]
    public async Task AnAccessTokenAboutToExpire_IsRefreshedBeforeTheRequestIsSent()
    {
        var refreshed = AccessJwt("a2");
        _server.Respond = r => r.Nsid switch
        {
            // The PDS's own access tokens say when they expire; this one has 30 s left.
            "com.atproto.server.createSession" => SessionResponse(AccessJwt("a1", DateTimeOffset.UtcNow.AddSeconds(30)), "r1"),
            "com.atproto.server.refreshSession" => SessionResponse(refreshed, "r2"),
            _ => JsonResponse("{}"),
        };
        await using var client = NewClient();
        await client.LoginAsync("alice.test", "password");

        await client.QueryAsync<JsonElement>(Ping);

        Assert.Equal(
            ["com.atproto.server.createSession", "com.atproto.server.refreshSession", "com.example.ping"],
            _server.Requests.Select(r => r.Nsid));
        Assert.Equal($"Bearer {refreshed}", _server.To("com.example.ping").Single().Authorization);
    }

    [Fact]
    public async Task AnAccessTokenFarFromExpiry_IsNotRefreshed()
    {
        _server.Respond = r => r.Nsid == "com.atproto.server.createSession"
            ? SessionResponse(AccessJwt("a1", DateTimeOffset.UtcNow.AddHours(2)), "r1")
            : JsonResponse("{}");
        await using var client = NewClient();
        await client.LoginAsync("alice.test", "password");

        await client.QueryAsync<JsonElement>(Ping);

        Assert.Empty(_server.To("com.atproto.server.refreshSession"));
    }

    [Fact]
    public async Task AProactiveRefreshThatFailsTransiently_SendsTheRequestWithTheCurrentToken()
    {
        var access = AccessJwt("a1", DateTimeOffset.UtcNow.AddSeconds(30));
        _server.Respond = r => r.Nsid switch
        {
            "com.atproto.server.createSession" => SessionResponse(access, "r1"),
            "com.atproto.server.refreshSession" => XrpcError(XrpcErrors.InternalServerError, HttpStatusCode.InternalServerError),
            _ => JsonResponse("{}"),
        };
        await using var client = NewClient();
        await client.LoginAsync("alice.test", "password");

        await client.QueryAsync<JsonElement>(Ping);

        Assert.Equal($"Bearer {access}", _server.To("com.example.ping").Single().Authorization);
        Assert.True(client.IsAuthenticated);
    }

    [Fact]
    public async Task RefreshSessionAsync_RefreshJwtRejected_ExpiresTheSession()
    {
        _server.Respond = r => r.Nsid == "com.atproto.server.createSession"
            ? SessionResponse(AccessJwt("a1"), "r1")
            : XrpcError(XrpcErrors.ExpiredToken);
        var store = new InMemoryAtProtoSessionStore();
        await using var client = NewClient(store);
        await client.LoginAsync("alice.test", "password");

        var ex = await Assert.ThrowsAsync<XrpcAuthenticationException>(() => client.RefreshSessionAsync());

        Assert.Equal("com.atproto.server.refreshSession", ex.Nsid);
        Assert.False(client.IsAuthenticated);
        Assert.Null(await store.GetAsync(Alice));
        Assert.Equal([AtProtoSessionChange.Created, AtProtoSessionChange.Expired], Changes());
        lock (_changes)
            Assert.Same(ex, _changes[^1].Error);
    }

    [Fact]
    public async Task RefreshSessionAsync_ServiceUnavailable_KeepsTheSession()
    {
        _server.Respond = r => r.Nsid == "com.atproto.server.createSession"
            ? SessionResponse(AccessJwt("a1"), "r1")
            : XrpcError(XrpcErrors.NotEnoughResources, HttpStatusCode.ServiceUnavailable);
        await using var client = NewClient();
        var session = await client.LoginAsync("alice.test", "password");

        await Assert.ThrowsAsync<XrpcException>(() => client.RefreshSessionAsync());

        Assert.Same(session, client.Session);
    }

    [Fact]
    public async Task RefreshSessionAsync_WithoutASession_Throws()
    {
        await using var client = NewClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RefreshSessionAsync());
    }

    [Fact]
    public async Task WithAutoRefreshOff_AnExpiredTokenIsNotRefreshed()
    {
        _server.Respond = r => r.Nsid == "com.atproto.server.createSession"
            ? SessionResponse(AccessJwt("a1", DateTimeOffset.UtcNow.AddSeconds(5)), "r1")
            : XrpcError(XrpcErrors.ExpiredToken);
        await using var client = NewClient(configure: o => o.AutoRefreshSession = false);
        await client.LoginAsync("alice.test", "password");

        await Assert.ThrowsAsync<XrpcAuthenticationException>(() => client.QueryAsync<JsonElement>(Ping));

        Assert.Empty(_server.To("com.atproto.server.refreshSession"));
    }

    [Fact]
    public async Task BackgroundRefresh_RefreshesShortlyBeforeExpiryWhileIdle()
    {
        var time = new ManualTimeProvider();
        _server.Respond = r => SessionResponse(AccessJwt(r.Nsid!, time.Now.AddHours(1)), "r-" + r.Nsid);
        await using var client = NewClient(configure: o => o.BackgroundRefresh = true, time: time);
        await client.LoginAsync("alice.test", "password");

        var timer = Assert.Single(time.ActiveTimers);
        Assert.InRange(timer.DueTime, TimeSpan.FromMinutes(58), TimeSpan.FromMinutes(59));

        time.Now += timer.DueTime;
        timer.Fire();
        await Eventually(() => Changes().Contains(AtProtoSessionChange.Refreshed));

        Assert.Single(_server.To("com.atproto.server.refreshSession"));
        Assert.Single(time.ActiveTimers); // rescheduled for the new token
    }

    [Fact]
    public async Task WithoutBackgroundRefresh_NoTimerIsScheduled()
    {
        var time = new ManualTimeProvider();
        _server.Respond = _ => SessionResponse(AccessJwt("a1"), "r1");
        await using var client = NewClient(time: time);

        await client.LoginAsync("alice.test", "password");

        Assert.Empty(time.ActiveTimers);
    }

    // ──────────────────────────────────────────────────────────
    //  Sign-out
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LogoutAsync_DeletesTheSessionWithItsRefreshJwt()
    {
        _server.Respond = r => r.Nsid == "com.atproto.server.createSession" ? SessionResponse(AccessJwt("a1"), "r1") : JsonResponse("{}");
        var store = new InMemoryAtProtoSessionStore();
        await using var client = NewClient(store);
        await client.LoginAsync("alice.test", "password");

        await client.LogoutAsync();

        // deleteSession requires the refresh JWT; the access JWT is refused.
        Assert.Equal("Bearer r1", _server.To("com.atproto.server.deleteSession").Single().Authorization);
        Assert.False(client.IsAuthenticated);
        Assert.Null(await store.GetAsync(Alice));
        Assert.Equal([AtProtoSessionChange.Created, AtProtoSessionChange.Removed], Changes());
    }

    [Fact]
    public async Task LogoutAsync_ServiceFailure_TearsDownLocallyThenThrows()
    {
        _server.Respond = r => r.Nsid == "com.atproto.server.createSession"
            ? SessionResponse(AccessJwt("a1"), "r1")
            : XrpcError(XrpcErrors.InternalServerError, HttpStatusCode.InternalServerError);
        var store = new InMemoryAtProtoSessionStore();
        await using var client = NewClient(store);
        await client.LoginAsync("alice.test", "password");

        await Assert.ThrowsAsync<XrpcException>(() => client.LogoutAsync());

        Assert.False(client.IsAuthenticated);
        Assert.Null(await store.GetAsync(Alice));
        Assert.Equal(AtProtoSessionChange.Removed, Changes()[^1]);
    }

    [Fact]
    public async Task LogoutAsync_RefreshJwtAlreadyInvalid_Succeeds()
    {
        _server.Respond = r => r.Nsid == "com.atproto.server.createSession"
            ? SessionResponse(AccessJwt("a1"), "r1")
            : XrpcError(XrpcErrors.ExpiredToken);
        await using var client = NewClient();
        await client.LoginAsync("alice.test", "password");

        await client.LogoutAsync();

        Assert.False(client.IsAuthenticated);
    }

    [Fact]
    public async Task LogoutAsync_WithoutASession_DoesNothing()
    {
        await using var client = NewClient();

        await client.LogoutAsync();

        Assert.Empty(_server.Requests);
        Assert.Empty(Changes());
    }

    [Fact]
    public async Task ASessionChangedHandlerThatThrows_DoesNotFailTheSignIn()
    {
        _server.Respond = _ => SessionResponse(AccessJwt("a1"), "r1");
        await using var client = NewClient();
        client.SessionChanged += (_, _) => throw new InvalidOperationException("handler bug");

        await client.LoginAsync("alice.test", "password");

        Assert.True(client.IsAuthenticated);
    }

    // ──────────────────────────────────────────────────────────
    //  Disposal
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_WithARefreshInFlight_ReturnsAtOnceAndTheExchangeStillLands()
    {
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Handler = async (r, ct) =>
        {
            if (r.Nsid == "com.atproto.server.createSession")
                return SessionResponse(AccessJwt("a1"), "r1");

            refreshStarted.SetResult();
            await release.Task.WaitAsync(ct);
            return SessionResponse(AccessJwt("a2"), "r2");
        };
        var store = new InMemoryAtProtoSessionStore();
        var client = NewClient(store);
        await client.LoginAsync("alice.test", "password");
        var refresh = client.RefreshSessionAsync();
        await refreshStarted.Task;

        var stopwatch = Stopwatch.StartNew();
        client.Dispose();
        stopwatch.Stop();
        release.SetResult();
        await refresh;

        // The PDS has already rotated r1; abandoning the exchange would leave the store on it.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Dispose blocked for {stopwatch.Elapsed}.");
        Assert.Equal("r2", Assert.IsType<PasswordSession>(await store.GetAsync(Alice)).RefreshJwt);
    }

    [Fact]
    public async Task DisposeAsync_WithARefreshInFlight_WaitsForTheExchangeAndStoresIt()
    {
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Handler = async (r, ct) =>
        {
            if (r.Nsid == "com.atproto.server.createSession")
                return SessionResponse(AccessJwt("a1"), "r1");

            refreshStarted.SetResult();
            await Task.Delay(200, ct);
            return SessionResponse(AccessJwt("a2"), "r2");
        };
        var store = new InMemoryAtProtoSessionStore();
        var client = NewClient(store);
        await client.LoginAsync("alice.test", "password");
        var refresh = client.RefreshSessionAsync();
        await refreshStarted.Task;

        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(refresh.IsCompletedSuccessfully);
        Assert.Equal("r2", Assert.IsType<PasswordSession>(await store.GetAsync(Alice)).RefreshJwt);
    }

    [Fact]
    public async Task DisposeAsync_LeavesWhatItWasGivenUsable()
    {
        _server.Respond = _ => SessionResponse(AccessJwt("a1"), "r1");
        var store = new InMemoryAtProtoSessionStore();
        var client = NewClient(store);
        await client.LoginAsync("alice.test", "password");

        await client.DisposeAsync();
        await client.DisposeAsync(); // idempotent

        // Disposing is not signing out: the session stays stored, and the HttpClient works.
        Assert.NotNull(await store.GetAsync(Alice));
        using var response = await _http.GetAsync("https://pds.example.com/xrpc/com.example.ping");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task AfterDispose_SessionChangesAreRefused()
    {
        var client = NewClient();
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.LoginAsync("alice.test", "password"));
    }
}
