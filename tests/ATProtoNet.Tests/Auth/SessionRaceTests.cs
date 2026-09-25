using System.Net;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Auth;

/// <summary>
/// What happens when a session changes while calls, refreshes and store writes are under way:
/// account switches mid-call, disposal and cancellation mid-refresh, clients sharing a store,
/// and the background timer after a failure.
/// </summary>
public sealed class SessionRaceTests : IDisposable
{
    private static readonly Nsid Ping = Nsid.Parse("com.example.ping");
    private static readonly Did Bob = Did.Parse("did:plc:bob");
    private static readonly Uri BobPds = new("https://pds-bob.example.com/");

    private readonly StubServer _server = new();
    private readonly HttpClient _http;

    public SessionRaceTests()
    {
        _http = new HttpClient(_server);
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    private static PasswordSession BobSession(string accessJwt) => new()
    {
        Did = Bob,
        Handle = Handle.Parse("bob.test"),
        ServiceEndpoint = BobPds,
        AccessJwt = accessJwt,
        RefreshJwt = "rb",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
    };

    // ──────────────────────────────────────────────────────────
    //  Account switches while a call is in flight
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ACallRejectedAfterAnAccountSwitch_IsNotResentWithTheOtherAccountsToken()
    {
        var accessA = AccessJwt("a1");
        var accessB = AccessJwt("b1");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Handler = async (r, ct) =>
        {
            if (r.Authorization == $"Bearer {accessA}")
            {
                await gate.Task.WaitAsync(ct);
                return XrpcError(XrpcErrors.ExpiredToken);
            }
            return JsonResponse("{}");
        };
        await using var client = Client(_http);
        await client.ApplySessionAsync(PasswordSession(accessA, "ra"));

        var call = client.QueryAsync<JsonElement>(Ping);
        await Eventually(() => _server.Requests.Count == 1);
        await client.ApplySessionAsync(BobSession(accessB));
        gate.SetResult();

        // Resending would run Alice's call as Bob, on Alice's PDS.
        await Assert.ThrowsAsync<XrpcAuthenticationException>(() => call);
        Assert.Single(_server.Requests);
        Assert.DoesNotContain(_server.Requests, r => r.Authorization == $"Bearer {accessB}");
    }

    [Fact]
    public async Task ACallWaitingOutA429_IsResentToItsOwnServiceWithItsOwnToken_AfterASwitch()
    {
        var accessA = AccessJwt("a1");
        var accessB = AccessJwt("b1");
        AtProtoClient? client = null;
        var attempts = 0;
        _server.Handler = async (r, ct) =>
        {
            if (Interlocked.Increment(ref attempts) > 1)
                return JsonResponse("{}");

            // The account switches while the call is about to wait out a rate limit.
            await client!.ApplySessionAsync(BobSession(accessB), cancellationToken: ct);
            var limited = XrpcError(XrpcErrors.RateLimitExceeded, HttpStatusCode.TooManyRequests);
            limited.Headers.TryAddWithoutValidation("Retry-After", "0");
            return limited;
        };
        await using var c = client = Client(_http);
        await client.ApplySessionAsync(PasswordSession(accessA, "ra"));

        await client.QueryAsync<JsonElement>(Ping);

        Assert.All(_server.Requests, r =>
        {
            Assert.Equal("pds.example.com", r.Uri.Host);
            Assert.Equal($"Bearer {accessA}", r.Authorization);
        });
        Assert.Equal(2, _server.Requests.Count);
    }

    [Fact]
    public async Task ACallOfASessionReplacedMeanwhile_FailsInsteadOfSigningWithTheReleasedKey()
    {
        AtProtoClient? client = null;
        var attempts = 0;
        _server.Handler = async (r, ct) =>
        {
            if (Interlocked.Increment(ref attempts) > 1)
                return JsonResponse("{}");

            // Replacing the session releases its DPoP key, which the resend would sign with.
            await client!.ApplySessionAsync(OAuthSession(NewDPoPKey(), accessToken: "at-other"), cancellationToken: ct);
            var limited = XrpcError(XrpcErrors.RateLimitExceeded, HttpStatusCode.TooManyRequests);
            limited.Headers.TryAddWithoutValidation("Retry-After", "0");
            return limited;
        };
        await using var c = client = Client(_http);
        await client.ApplySessionAsync(OAuthSession(NewDPoPKey()));

        var ex = await Assert.ThrowsAsync<XrpcAuthenticationException>(() => client.QueryAsync<JsonElement>(Ping));

        Assert.True(ex.Is(XrpcErrors.AuthenticationRequired));
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task AProactiveRefreshThatMovesThePds_SendsTheCallToTheNewPds()
    {
        _server.Respond = r => r.Nsid switch
        {
            "com.atproto.server.createSession" =>
                SessionResponse(AccessJwt("a1", DateTimeOffset.UtcNow.AddSeconds(30)), "r1", pdsInDidDoc: "https://pds.example.com"),
            "com.atproto.server.refreshSession" =>
                SessionResponse(AccessJwt("a2"), "r2", pdsInDidDoc: "https://pds2.example.com"),
            _ => JsonResponse("{}"),
        };
        await using var client = Client(_http);
        await client.LoginAsync("alice.test", "password");

        await client.QueryAsync<JsonElement>(Ping);

        Assert.Equal("pds2.example.com", _server.To("com.example.ping").Single().Uri.Host);
    }

    // ──────────────────────────────────────────────────────────
    //  Refreshes that outlive their caller or their client
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ARefreshWhoseCallerGivesUpAndWhoseClientIsDisposed_StillStoresTheRotatedToken()
    {
        var spent = new HashSet<string>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Handler = async (r, ct) =>
        {
            if (r.Path != TokenEndpoint.AbsolutePath)
                return JsonResponse("{}");

            lock (spent)
            {
                if (!spent.Add(r.Form["refresh_token"]))
                    return OAuthError("invalid_grant");
            }

            // The authorization server has rotated the token; its answer is slow.
            received.TrySetResult();
            await Task.Delay(300, ct);
            return TokenResponse("at-2", "rt-2");
        };
        using var oauth = OAuthClient(_http);
        var store = new InMemoryAtProtoSessionStore();
        var client = Client(_http, store);
        await client.ApplySessionAsync(OAuthSession(NewDPoPKey()), oauth);

        using var cts = new CancellationTokenSource();
        var refresh = client.RefreshSessionAsync(cts.Token);
        await received.Task;
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        await client.DisposeAsync();

        Assert.Equal("rt-2", Assert.IsType<OAuthSession>(await store.GetAsync(Alice)).RefreshToken);

        // The next per-request client refreshes from rt-2 rather than the spent rt-1.
        await using var next = Client(_http, store);
        await next.TryRestoreSessionAsync(Alice, oauth);
        _server.Respond = r => r.Path == TokenEndpoint.AbsolutePath ? TokenResponse("at-3", "rt-3") : JsonResponse("{}");
        await next.RefreshSessionAsync();
        Assert.Equal("rt-3", Assert.IsType<OAuthSession>(next.Session).RefreshToken);
    }

    [Fact]
    public async Task DisposingDuringTheStoreWrite_LetsTheWriteFinish()
    {
        _server.Respond = r => r.Nsid == "com.atproto.server.refreshSession"
            ? SessionResponse(AccessJwt("a2"), "r2")
            : JsonResponse("{}");
        var inner = new InMemoryAtProtoSessionStore();
        var store = new GatedStore(inner);
        var client = Client(_http, store);
        await client.ApplySessionAsync(PasswordSession(AccessJwt("a1"), "r1"));
        store.Gate = true;

        var refresh = client.RefreshSessionAsync();
        await store.Entered.Task;
        var disposal = client.DisposeAsync().AsTask();
        store.Release.SetResult();
        await disposal;
        await refresh;

        Assert.Equal("r2", Assert.IsType<PasswordSession>(await inner.GetAsync(Alice)).RefreshJwt);
    }

    // ──────────────────────────────────────────────────────────
    //  Clients sharing one store
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ALosingConcurrentRefresh_LeavesTheWinnersStoredSessionAlone()
    {
        var current = "rt-1";
        var issued = 1;
        _server.Respond = r =>
        {
            if (r.Path != TokenEndpoint.AbsolutePath)
                return JsonResponse("{}");

            lock (_server)
            {
                if (r.Form["refresh_token"] != current)
                    return OAuthError("invalid_grant");
                current = $"rt-{++issued}";
                return TokenResponse($"at-{issued}", current);
            }
        };
        using var oauth = OAuthClient(_http);
        var store = new InMemoryAtProtoSessionStore();
        await store.SetAsync(OAuthSession(NewDPoPKey()));

        await using var a = Client(_http, store);
        await using var b = Client(_http, store);
        await a.TryRestoreSessionAsync(Alice, oauth);
        await b.TryRestoreSessionAsync(Alice, oauth);

        await a.RefreshSessionAsync();
        var ex = await Assert.ThrowsAsync<OAuthException>(() => b.RefreshSessionAsync());

        // b spent a token a had already rotated; its refusal must not delete a's rt-2.
        Assert.Equal("invalid_grant", ex.Error);
        Assert.Null(b.Session);
        Assert.Equal("rt-2", Assert.IsType<OAuthSession>(await store.GetAsync(Alice)).RefreshToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Background refresh
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ABackgroundRefreshThatFailsTransiently_IsRetriedWithBackoff()
    {
        var failures = 0;
        _server.Respond = r =>
        {
            if (r.Nsid != "com.atproto.server.refreshSession")
                return JsonResponse("{}");
            Interlocked.Increment(ref failures);
            return XrpcError(XrpcErrors.InternalServerError, HttpStatusCode.InternalServerError);
        };
        var time = new ManualTimeProvider();
        await using var client = Client(_http, configure: o => o.BackgroundRefresh = true, time: time);
        await client.ApplySessionAsync(PasswordSession(AccessJwt("a1", time.Now.AddMinutes(5)), "r1"));

        var first = Assert.Single(time.ActiveTimers);
        time.Now += first.DueTime;
        first.Fire();
        await Eventually(() => Volatile.Read(ref failures) == 1 && time.ActiveTimers.Count == 1 && time.ActiveTimers[0] != first);

        var retry = Assert.Single(time.ActiveTimers);
        Assert.Equal(TimeSpan.FromSeconds(30), retry.DueTime);

        time.Now += retry.DueTime;
        retry.Fire();
        await Eventually(() => Volatile.Read(ref failures) == 2 && time.ActiveTimers.Count == 1 && time.ActiveTimers[0] != retry);

        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(time.ActiveTimers).DueTime);
        Assert.True(client.IsAuthenticated);
    }

    // ──────────────────────────────────────────────────────────
    //  Resume, install and sign-out edge cases
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResumeSessionAsync_ATokenTheServiceRejects_RemovesTheSession()
    {
        _server.Respond = r => r.Nsid == "com.atproto.server.getSession"
            ? XrpcError(XrpcErrors.InvalidToken)
            : JsonResponse("{}");
        var store = new InMemoryAtProtoSessionStore();
        await using var client = Client(_http, store);
        var changes = new List<AtProtoSessionChange>();
        client.SessionChanged += (_, e) => changes.Add(e.Change);

        await Assert.ThrowsAsync<XrpcAuthenticationException>(
            () => client.ResumeSessionAsync(PasswordSession(AccessJwt("a1"), "r1")));

        Assert.Null(client.Session);
        Assert.Null(await store.GetAsync(Alice));
        Assert.Equal([AtProtoSessionChange.Created, AtProtoSessionChange.Expired], changes);
    }

    [Fact]
    public async Task ResumeSessionAsync_ServiceUnreachable_KeepsTheSession()
    {
        _server.Handler = (_, _) => throw new HttpRequestException("unreachable");
        var store = new InMemoryAtProtoSessionStore();
        await using var client = Client(_http, store);
        var session = PasswordSession(AccessJwt("a1"), "r1");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ResumeSessionAsync(session));

        Assert.Same(session, client.Session);
        Assert.Same(session, await store.GetAsync(Alice));
    }

    [Fact]
    public async Task ApplySessionAsync_CallerCancelsDuringTheStoreWrite_TheWriteAndTheEventStillHappen()
    {
        var inner = new InMemoryAtProtoSessionStore();
        var store = new GatedStore(inner) { Gate = true };
        await using var client = Client(_http, store);
        var changes = new List<AtProtoSessionChange>();
        client.SessionChanged += (_, e) => changes.Add(e.Change);
        using var cts = new CancellationTokenSource();

        var apply = client.ApplySessionAsync(PasswordSession(AccessJwt("a1"), "r1"), cancellationToken: cts.Token);
        await store.Entered.Task;
        cts.Cancel();
        store.Release.SetResult();
        await apply;

        Assert.NotNull(client.Session);
        Assert.NotNull(await inner.GetAsync(Alice));
        Assert.Equal([AtProtoSessionChange.Created], changes);
    }

    [Fact]
    public async Task LogoutAsync_AnOAuthSessionWithoutItsOAuthClient_TearsDownThenThrows()
    {
        var store = new InMemoryAtProtoSessionStore();
        await using var client = Client(_http, store);
        await client.ApplySessionAsync(OAuthSession(NewDPoPKey()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LogoutAsync());

        Assert.False(client.IsAuthenticated);
        Assert.Null(await store.GetAsync(Alice));
        Assert.Empty(_server.Requests);
    }

    // ──────────────────────────────────────────────────────────
    //  Recovery details
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ANonSeekableUploadRejectedAsExpired_RefreshesButIsNotResent()
    {
        var refreshed = AccessJwt("a2");
        _server.Respond = r => r.Nsid switch
        {
            "com.atproto.server.refreshSession" => SessionResponse(refreshed, "r2"),
            _ => XrpcError(XrpcErrors.ExpiredToken),
        };
        await using var client = Client(_http);
        await client.ApplySessionAsync(PasswordSession(AccessJwt("a1"), "r1"));

        await using var body = new NonSeekableStream([1, 2, 3]);
        var ex = await Assert.ThrowsAsync<XrpcAuthenticationException>(() => client.Repo.UploadBlobAsync(body, "image/png"));

        // The body cannot be sent twice, but the next call has fresh tokens.
        Assert.True(ex.Is(XrpcErrors.ExpiredToken));
        Assert.Single(_server.To("com.atproto.repo.uploadBlob"));
        Assert.Single(_server.To("com.atproto.server.refreshSession"));
        Assert.Equal(refreshed, Assert.IsType<PasswordSession>(client.Session).AccessJwt);
    }

    [Fact]
    public async Task AnInvalidTokenChallengeCarryingANewNonce_IsRecoveredWithoutASeparateNonceRetry()
    {
        using var oauth = OAuthClient(_http);
        _server.Respond = r =>
        {
            if (r.Path == TokenEndpoint.AbsolutePath)
                return TokenResponse("at-2", "rt-2");
            if (r.Authorization == "DPoP at-2")
                return JsonResponse("{}");

            var challenge = JsonResponse("""{"error":"InvalidToken"}""", HttpStatusCode.Unauthorized);
            challenge.Headers.TryAddWithoutValidation("WWW-Authenticate", """DPoP error="invalid_token" """);
            challenge.Headers.TryAddWithoutValidation("DPoP-Nonce", "rs-nonce");
            return challenge;
        };
        await using var client = Client(_http);
        await client.ApplySessionAsync(OAuthSession(NewDPoPKey()), oauth);

        await client.QueryAsync<JsonElement>(Ping);

        var calls = _server.To("com.example.ping");
        Assert.Equal(["DPoP at-1", "DPoP at-2"], calls.Select(r => r.Authorization));
        Assert.Equal("rs-nonce", calls[1].DPoPClaims.GetProperty("nonce").GetString());
    }

    [Fact]
    public void Credentials_ToString_NeverShowsTheTokens()
    {
        using var key = new DPoPProofGenerator();
        var credentials = new XrpcCredentials("secret-access", "secret-refresh", key) { Account = Alice, Service = Pds };

        Assert.DoesNotContain("secret", credentials.ToString());
        Assert.Contains("did:plc:alice", credentials.ToString());
    }

    /// <summary>A store whose writes can be held open, to land events in the middle of one.</summary>
    private sealed class GatedStore(IAtProtoSessionStore inner) : IAtProtoSessionStore
    {
        public volatile bool Gate;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<AtProtoSession?> GetAsync(Did did, CancellationToken cancellationToken = default) =>
            inner.GetAsync(did, cancellationToken);

        public async ValueTask SetAsync(AtProtoSession session, CancellationToken cancellationToken = default)
        {
            if (Gate)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            await inner.SetAsync(session, cancellationToken);
        }

        public ValueTask RemoveAsync(Did did, CancellationToken cancellationToken = default) =>
            inner.RemoveAsync(did, cancellationToken);
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
