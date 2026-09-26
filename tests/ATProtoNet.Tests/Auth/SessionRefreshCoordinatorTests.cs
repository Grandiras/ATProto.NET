using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Auth;

/// <summary>
/// Clients that share a session store and act for one account at once, as the per-request
/// clients of a web application do: with a <see cref="ISessionRefreshCoordinator"/>, the
/// account's single-use refresh token is spent once.
/// </summary>
public sealed class SessionRefreshCoordinatorTests : IDisposable
{
    private static readonly Nsid Ping = Nsid.Parse("com.example.ping");

    private readonly StubServer _server = new();
    private readonly HttpClient _http;
    private readonly OAuthClient _oauth;
    private readonly InMemoryAtProtoSessionStore _store = new();
    private readonly byte[] _key = NewDPoPKey();

    // The authorization server's view: the one refresh token that is live, as a real one keeps it.
    private readonly object _grant = new();
    private string _liveRefreshToken = "rt-1";
    private int _issued = 1;

    public SessionRefreshCoordinatorTests()
    {
        _http = new HttpClient(_server);
        _oauth = OAuthClient(_http);
    }

    public void Dispose()
    {
        _oauth.Dispose();
        _http.Dispose();
        _server.Dispose();
    }

    /// <summary>
    /// A token endpoint that rotates the refresh token and refuses a spent one, answering once
    /// <paramref name="release"/> completes; the PDS accepts any token.
    /// </summary>
    private void ServeRotatingTokens(Task release)
    {
        _server.Handler = async (r, ct) =>
        {
            if (r.Path != TokenEndpoint.AbsolutePath)
                return JsonResponse("{}");

            await release.WaitAsync(TimeSpan.FromSeconds(10), ct);
            lock (_grant)
            {
                if (r.Form["refresh_token"] != _liveRefreshToken)
                    return OAuthError("invalid_grant");

                _issued++;
                _liveRefreshToken = $"rt-{_issued}";
                return TokenResponse($"at-{_issued}", _liveRefreshToken);
            }
        };
    }

    /// <summary>A per-request client: restored from the shared store, like the server factory's.</summary>
    private async Task<AtProtoClient> RestoreAsync(ISessionRefreshCoordinator? coordinator)
    {
        var client = Client(_http, _store, o => o.RefreshCoordinator = coordinator);
        Assert.True(await client.TryRestoreSessionAsync(Alice, _oauth));
        return client;
    }

    private Task StoreExpiringSessionAsync() =>
        _store.SetAsync(OAuthSession(_key, expiresAt: DateTimeOffset.UtcNow.AddSeconds(20))).AsTask();

    [Fact]
    public async Task ConcurrentRequests_WithoutACoordinator_SpendTheRefreshTokenTwice()
    {
        // The race the coordinator exists for: both clients see the token about to expire and
        // exchange the same refresh token, and the authorization server refuses the second.
        var bothExchanging = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ServeRotatingTokens(bothExchanging.Task);
        await StoreExpiringSessionAsync();
        await using var a = await RestoreAsync(coordinator: null);
        await using var b = await RestoreAsync(coordinator: null);

        var calls = new[] { a.QueryAsync<JsonElement>(Ping), b.QueryAsync<JsonElement>(Ping) };
        await Eventually(() => _server.To(TokenEndpoint.AbsolutePath).Count == 2);
        bothExchanging.SetResult();
        var outcomes = await Task.WhenAll(calls.Select(Outcome));

        Assert.Equal(2, _server.To(TokenEndpoint.AbsolutePath).Count);
        Assert.Contains(outcomes, o => o is OAuthException { Error: "invalid_grant" });
    }

    [Fact]
    public async Task ConcurrentRequests_WithACoordinator_SpendTheRefreshTokenOnce()
    {
        var coordinator = new WatchedCoordinator(new InProcessSessionRefreshCoordinator());
        ServeRotatingTokens(coordinator.SecondWaiter.Task);
        await StoreExpiringSessionAsync();
        await using var a = await RestoreAsync(coordinator);
        await using var b = await RestoreAsync(coordinator);

        // The first exchange is held until the other client waits for the account's lock, so
        // the two refreshes really do overlap.
        await Task.WhenAll(a.QueryAsync<JsonElement>(Ping), b.QueryAsync<JsonElement>(Ping));

        Assert.Single(_server.To(TokenEndpoint.AbsolutePath));
        Assert.All(_server.To("com.example.ping"), r => Assert.Equal("DPoP at-2", r.Authorization));
        Assert.Equal("rt-2", Assert.IsType<OAuthSession>(await _store.GetAsync(Alice)).RefreshToken);
        Assert.Equal("rt-2", Assert.IsType<OAuthSession>(b.Session).RefreshToken);
    }

    [Fact]
    public async Task ManyConcurrentClients_WithACoordinator_ExchangeOnceAndAllSucceed()
    {
        const int clients = 16;
        ServeRotatingTokens(Task.CompletedTask);
        await StoreExpiringSessionAsync();
        var coordinator = new InProcessSessionRefreshCoordinator();
        var restored = await Task.WhenAll(Enumerable.Range(0, clients).Select(_ => RestoreAsync(coordinator)));

        try
        {
            await Task.WhenAll(restored.Select(c => Task.Run(() => c.QueryAsync<JsonElement>(Ping))));
        }
        finally
        {
            foreach (var client in restored)
                await client.DisposeAsync();
        }

        Assert.Single(_server.To(TokenEndpoint.AbsolutePath));
        Assert.Equal(0, coordinator.ActiveCount);
    }

    [Fact]
    public async Task ARefreshAfterAnotherClientRefreshed_TakesUpTheStoredSession()
    {
        ServeRotatingTokens(Task.CompletedTask);
        await StoreExpiringSessionAsync();
        var coordinator = new InProcessSessionRefreshCoordinator();
        await using var a = await RestoreAsync(coordinator);
        await using var b = await RestoreAsync(coordinator);
        var changes = new List<AtProtoSessionChange>();
        b.SessionChanged += (_, e) => changes.Add(e.Change);

        await a.RefreshSessionAsync();
        await b.RefreshSessionAsync();

        Assert.Single(_server.To(TokenEndpoint.AbsolutePath));
        Assert.Equal(a.Session, b.Session);
        Assert.Equal([AtProtoSessionChange.Refreshed], changes);

        await b.QueryAsync<JsonElement>(Ping);
        Assert.Equal("DPoP at-2", _server.To("com.example.ping").Single().Authorization);
    }

    [Fact]
    public async Task ASessionTheStoreNoLongerHolds_EndsWithoutSpendingItsRefreshToken()
    {
        // Signed out by another request: the refresh token must not bring the session back.
        ServeRotatingTokens(Task.CompletedTask);
        await StoreExpiringSessionAsync();
        await using var client = await RestoreAsync(new InProcessSessionRefreshCoordinator());
        await _store.RemoveAsync(Alice);

        var ex = await Assert.ThrowsAsync<XrpcAuthenticationException>(() => client.QueryAsync<JsonElement>(Ping));

        Assert.Equal(XrpcErrors.InvalidToken, ex.Error);
        Assert.Null(client.Session);
        Assert.Empty(_server.Requests);
        Assert.Null(await _store.GetAsync(Alice));
    }

    [Fact]
    public async Task AStoredSessionOfANewSignIn_IsTakenUpWithItsOwnKey()
    {
        ServeRotatingTokens(Task.CompletedTask);
        await StoreExpiringSessionAsync();
        await using var client = await RestoreAsync(new InProcessSessionRefreshCoordinator());
        var newKey = NewDPoPKey();
        await _store.SetAsync(OAuthSession(newKey, accessToken: "at-new", refreshToken: "rt-new"));

        await client.RefreshSessionAsync();
        await client.QueryAsync<JsonElement>(Ping);

        Assert.Empty(_server.To(TokenEndpoint.AbsolutePath));
        var ping = _server.To("com.example.ping").Single();
        Assert.Equal("DPoP at-new", ping.Authorization);
        using var expected = new DPoPProofGenerator(newKey);
        Assert.Equal(expected.KeyThumbprint, Thumbprint(ping.DPoP!));
    }

    [Fact]
    public async Task SigningOutWhileAnotherClientRefreshes_LeavesTheStoreEmpty_AndRevokesTheLiveToken()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ServeRotatingTokens(release.Task);
        await _store.SetAsync(OAuthSession(
            _key, expiresAt: DateTimeOffset.UtcNow.AddSeconds(20), revocationEndpoint: RevocationEndpoint));
        var coordinator = new InProcessSessionRefreshCoordinator();
        await using var a = await RestoreAsync(coordinator);
        await using var b = await RestoreAsync(coordinator);

        // A is exchanging the refresh token when B signs out.
        var refresh = a.RefreshSessionAsync();
        await Eventually(() => _server.To(TokenEndpoint.AbsolutePath).Count == 1);
        var logout = b.LogoutAsync();
        release.SetResult();
        await Task.WhenAll(refresh, logout);

        // A's result is removed with the rest rather than written back after the sign-out, and
        // the tokens revoked are the ones A's refresh made, which are the live ones.
        Assert.Null(await _store.GetAsync(Alice));
        Assert.Equal("rt-2", Assert.Single(_server.To(RevocationEndpoint.AbsolutePath)).Form["token"]);
    }

    [Fact]
    public async Task SigningInWhileAnotherClientRefreshes_KeepsTheNewSession()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ServeRotatingTokens(release.Task);
        await StoreExpiringSessionAsync();
        var coordinator = new InProcessSessionRefreshCoordinator();
        await using var a = await RestoreAsync(coordinator);
        await using var b = Client(_http, _store, o => o.RefreshCoordinator = coordinator);

        // A is refreshing the old session when B installs a new sign-in of the account.
        var refresh = a.RefreshSessionAsync();
        await Eventually(() => _server.To(TokenEndpoint.AbsolutePath).Count == 1);
        var signIn = b.ApplySessionAsync(OAuthSession(NewDPoPKey(), accessToken: "at-new", refreshToken: "rt-new"), _oauth);
        release.SetResult();
        await Task.WhenAll(refresh, signIn);

        Assert.Equal("rt-new", Assert.IsType<OAuthSession>(await _store.GetAsync(Alice)).RefreshToken);
    }

    [Fact]
    public async Task WithoutASessionStore_TheCoordinatorIsNotUsed()
    {
        ServeRotatingTokens(Task.CompletedTask);
        var coordinator = new WatchedCoordinator(new InProcessSessionRefreshCoordinator());
        await using var client = Client(_http, configure: o => o.RefreshCoordinator = coordinator);
        await client.ApplySessionAsync(OAuthSession(_key), _oauth);

        await client.RefreshSessionAsync();

        Assert.Equal(0, coordinator.Acquisitions);
        Assert.Single(_server.To(TokenEndpoint.AbsolutePath));
    }

    [Fact]
    public async Task InProcessCoordinator_SerializesOneAccountAndNotOthers()
    {
        var coordinator = new InProcessSessionRefreshCoordinator();

        var alice = await coordinator.AcquireAsync(Alice);
        var bob = await coordinator.AcquireAsync(Did.Parse("did:plc:bob"));
        var aliceAgain = coordinator.AcquireAsync(Alice).AsTask();

        Assert.False(aliceAgain.IsCompleted);
        await alice.DisposeAsync();
        await (await aliceAgain.WaitAsync(TimeSpan.FromSeconds(5))).DisposeAsync();
        await bob.DisposeAsync();

        Assert.Equal(0, coordinator.ActiveCount);
    }

    [Fact]
    public async Task InProcessCoordinator_ACancelledWait_HoldsNothing()
    {
        var coordinator = new InProcessSessionRefreshCoordinator();
        await using (await coordinator.AcquireAsync(Alice))
        {
            using var cancel = new CancellationTokenSource();
            var waiting = coordinator.AcquireAsync(Alice, cancel.Token).AsTask();
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }

        Assert.Equal(0, coordinator.ActiveCount);
    }

    private static async Task<Exception?> Outcome(Task call)
    {
        try
        {
            await call;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static string Thumbprint(string proof)
    {
        Assert.True(Jwt.TryDecode(proof, out var jwt, out _));
        var jwk = jwt.Header.GetProperty("jwk");
        return DPoP.Thumbprint(new JsonWebKey
        {
            Kty = jwk.GetProperty("kty").GetString()!,
            Crv = jwk.GetProperty("crv").GetString(),
            X = jwk.GetProperty("x").GetString(),
            Y = jwk.GetProperty("y").GetString(),
        });
    }

    /// <summary>A coordinator that reports when a second client waits for a lock.</summary>
    private sealed class WatchedCoordinator(ISessionRefreshCoordinator inner) : ISessionRefreshCoordinator
    {
        private int _acquisitions;

        public int Acquisitions => Volatile.Read(ref _acquisitions);

        public TaskCompletionSource SecondWaiter { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<IAsyncDisposable> AcquireAsync(Did did, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _acquisitions) == 2)
                SecondWaiter.TrySetResult();
            return inner.AcquireAsync(did, cancellationToken);
        }
    }
}
