using System.Text;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Tests.Auth;
using ATProtoNet.Tests.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// Where pending authorizations wait for their callbacks: bounded per requester and in total in
/// memory, shareable through a distributed cache, single-use, and never able to block other
/// people's logins.
/// </summary>
public sealed class OAuthStateStoreTests : IDisposable
{
    private readonly StubServer _server = new() { Respond = AuthorizationServer };
    private readonly HttpClient _http;

    public OAuthStateStoreTests() => _http = new HttpClient(_server);

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    private static OAuthPendingAuthorization Pending(string state, string? requester = null, DateTimeOffset? expiresAt = null) => new()
    {
        State = state,
        RequesterId = requester,
        Issuer = Issuer,
        TokenEndpoint = TokenEndpoint,
        RedirectUri = RedirectUri,
        CodeVerifier = "verifier",
        DPoPKey = NewDPoPKey(),
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10),
    };

    private OAuthClient Client(IOAuthStateStore? store = null, TimeProvider? time = null) => new(
        new OAuthOptions
        {
            ClientMetadata = new OAuthClientMetadata { ClientId = ClientId, RedirectUris = [RedirectUri] },
            HttpClient = _http,
            IdentityResolver = AliceIdentity(),
            StateStore = store,
        },
        NullLogger.Instance)
    {
        NonceCache = new DPoPNonceCache(),
        TimeProvider = time ?? TimeProvider.System,
    };

    // ──────────────────────────────────────────────────────────
    //  In memory
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemory_Take_IsSingleUse()
    {
        var store = new InMemoryOAuthStateStore();
        await store.SetAsync(Pending("s-1"));

        Assert.NotNull(await store.TakeAsync("s-1"));
        Assert.Null(await store.TakeAsync("s-1"));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task InMemory_ARequesterOverItsLimit_DisplacesOnlyItsOwnOldest()
    {
        var store = new InMemoryOAuthStateStore(maxPerRequester: 3);
        await store.SetAsync(Pending("victim", "198.51.100.1"));

        for (var i = 0; i < 50; i++)
            await store.SetAsync(Pending($"flood-{i}", "203.0.113.7"));

        Assert.NotNull(await store.TakeAsync("victim"));
        Assert.Equal(3, store.Count);
        Assert.Null(await store.TakeAsync("flood-46"));
        Assert.NotNull(await store.TakeAsync("flood-47"));
        Assert.NotNull(await store.TakeAsync("flood-49"));
    }

    [Fact]
    public async Task InMemory_PastCapacity_TheOldestGoes()
    {
        var store = new InMemoryOAuthStateStore(maxPerRequester: 100, capacity: 3);

        for (var i = 0; i < 5; i++)
            await store.SetAsync(Pending($"s-{i}", requester: i % 2 == 0 ? "a" : null));

        Assert.Equal(3, store.Count);
        Assert.Null(await store.TakeAsync("s-1"));
        Assert.NotNull(await store.TakeAsync("s-2"));
        Assert.NotNull(await store.TakeAsync("s-4"));
    }

    [Fact]
    public async Task InMemory_ExpiredEntries_AreDroppedAsNewOnesArrive()
    {
        var clock = new ManualClock();
        var store = new InMemoryOAuthStateStore(timeProvider: clock);
        await store.SetAsync(Pending("old", expiresAt: clock.GetUtcNow().AddMinutes(10)));

        clock.Advance(TimeSpan.FromMinutes(11));
        await store.SetAsync(Pending("new", expiresAt: clock.GetUtcNow().AddMinutes(10)));

        Assert.Equal(1, store.Count);
        Assert.Null(await store.TakeAsync("old"));
    }

    [Fact]
    public async Task InMemory_TheSameStateTwice_KeepsTheLatest()
    {
        var store = new InMemoryOAuthStateStore();
        await store.SetAsync(Pending("s-1", "a"));
        await store.SetAsync(Pending("s-1", "b") with { CodeVerifier = "second" });

        Assert.Equal(1, store.Count);
        Assert.Equal("second", (await store.TakeAsync("s-1"))!.CodeVerifier);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    public void InMemory_ALimitBelowOne_Throws(int maxPerRequester, int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryOAuthStateStore(maxPerRequester, capacity));
    }

    [Fact]
    public void PendingAuthorization_ToString_ShowsNoSecret()
    {
        var text = Pending("s-1").ToString();

        Assert.Contains("s-1", text);
        Assert.DoesNotContain("verifier", text);
    }

    // ──────────────────────────────────────────────────────────
    //  Through the client
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAuthorization_ReturnsWhereToGoAndUntilWhen()
    {
        var clock = new ManualClock();
        using var oauth = Client(time: clock);

        var authorization = await oauth.StartAuthorizationAsync(AliceHandle.Value, RedirectUri);

        Assert.Equal(
            $"{Issuer}/oauth/authorize?client_id={Uri.EscapeDataString(ClientId)}&request_uri={Uri.EscapeDataString("urn:ietf:params:oauth:request_uri:stub")}",
            authorization.AuthorizationUrl.AbsoluteUri);
        Assert.Equal(43, authorization.State.Length);
        Assert.Equal(clock.GetUtcNow() + ATProtoNet.Auth.OAuth.OAuthClient.AuthorizationLifetime, authorization.ExpiresAt);
    }

    [Fact]
    public async Task StartAuthorization_StoresWhatTheCallbackNeeds()
    {
        var store = new InMemoryOAuthStateStore();
        using var oauth = Client(store);

        var authorization = await oauth.StartAuthorizationAsync(
            AliceHandle.Value, RedirectUri, new OAuthAuthorizationOptions { RequesterId = "203.0.113.7", AppState = "{\"back\":\"/x\"}" });

        var pending = await store.TakeAsync(authorization.State);
        Assert.NotNull(pending);
        Assert.Equal("203.0.113.7", pending.RequesterId);
        Assert.Equal(Issuer, pending.Issuer);
        Assert.Equal(Alice, pending.Did);
        Assert.Equal(RedirectUri, pending.RedirectUri);
        Assert.Equal(authorization.ExpiresAt, pending.ExpiresAt);
        Assert.Equal("{\"back\":\"/x\"}", pending.AppState);

        // The verifier behind the challenge the pushed request carried.
        var par = _server.To("/oauth/par").Single();
        Assert.Equal(PkceGenerator.ComputeCodeChallenge(pending.CodeVerifier), par.Form["code_challenge"]);
    }

    [Fact]
    public async Task AFloodOfLoginsFromOneRequester_DoesNotBlockAnotherOne()
    {
        // The global cap of 100 let anyone block every login for ten minutes.
        using var oauth = Client();
        for (var i = 0; i < 150; i++)
            await oauth.StartAuthorizationAsync(AliceHandle.Value, RedirectUri, new OAuthAuthorizationOptions { RequesterId = "203.0.113.7" });

        var authorization = await oauth.StartAuthorizationAsync(
            AliceHandle.Value, RedirectUri, new OAuthAuthorizationOptions { RequesterId = "198.51.100.1" });
        var session = await oauth.CompleteAuthorizationAsync("code", authorization.State, Issuer);

        Assert.Equal(Alice, session.Did);
    }

    [Fact]
    public async Task Complete_AfterTheLifetime_IsStateExpired()
    {
        var clock = new ManualClock();
        using var oauth = Client(new InMemoryOAuthStateStore(timeProvider: clock), clock);
        var authorization = await oauth.StartAuthorizationAsync(AliceHandle.Value, RedirectUri);

        clock.Advance(ATProtoNet.Auth.OAuth.OAuthClient.AuthorizationLifetime + TimeSpan.FromSeconds(1));
        var ex = await Assert.ThrowsAsync<OAuthException>(() => oauth.CompleteAuthorizationAsync("code", authorization.State, Issuer));

        Assert.Equal("state_expired", ex.Error);
        Assert.Empty(_server.To("/oauth/token"));
    }

    [Fact]
    public async Task Complete_TwiceWithOneState_IsRefusedTheSecondTime()
    {
        using var oauth = Client();
        var authorization = await oauth.StartAuthorizationAsync(AliceHandle.Value, RedirectUri);

        await oauth.CompleteAuthorizationAsync("code", authorization.State, Issuer);
        var ex = await Assert.ThrowsAsync<OAuthException>(() => oauth.CompleteAuthorizationAsync("code", authorization.State, Issuer));

        Assert.Equal("invalid_state", ex.Error);
    }

    [Fact]
    public async Task Complete_ADifferentIssuer_IsRefusedBeforeTheExchange()
    {
        using var oauth = Client();
        var authorization = await oauth.StartAuthorizationAsync(AliceHandle.Value, RedirectUri);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => oauth.CompleteAuthorizationAsync("code", authorization.State, "https://AUTH.example.com"));

        Assert.Equal("issuer_mismatch", ex.Error);
        Assert.Empty(_server.To("/oauth/token"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    public async Task Complete_AStateNoStoreCouldHold_IsNotLookedUp(string suffix)
    {
        var store = Substitute.For<IOAuthStateStore>();
        using var oauth = Client(store);
        var state = suffix.Length == 0 ? "" : new string('s', 256) + suffix;

        var ex = await Assert.ThrowsAsync<OAuthException>(() => oauth.CompleteAuthorizationAsync("code", state, Issuer));

        Assert.Equal("invalid_state", ex.Error);
        await store.DidNotReceiveWithAnyArgs().TakeAsync(default!, default);
    }

    // ──────────────────────────────────────────────────────────
    //  Distributed
    // ──────────────────────────────────────────────────────────

    private static MemoryDistributedCache NewCache() => new(Options.Create(new MemoryDistributedCacheOptions()));

    private static readonly DistributedCacheOAuthStateStoreOptions Plaintext = new() { StoreSecretsUnencrypted = true };

    [Fact]
    public async Task Distributed_RoundTripsOnceAndKeysByAHashOfTheState()
    {
        var cache = new RecordingCache(NewCache());
        var store = new DistributedCacheOAuthStateStore(cache, Plaintext);
        var pending = Pending("s-1", "203.0.113.7");

        await store.SetAsync(pending);
        var taken = await store.TakeAsync("s-1");

        Assert.Equal(pending.CodeVerifier, taken!.CodeVerifier);
        Assert.Equal(pending.DPoPKey.ToArray(), taken.DPoPKey.ToArray());
        Assert.Equal(pending.RequesterId, taken.RequesterId);
        Assert.Null(await store.TakeAsync("s-1"));

        var key = Assert.Single(cache.Keys.Distinct());
        Assert.StartsWith("atproto:oauth-state:", key);
        Assert.DoesNotContain("s-1", key);
        Assert.Equal(pending.ExpiresAt, cache.LastOptions!.AbsoluteExpiration);
    }

    [Fact]
    public async Task Distributed_WithAProtector_WritesOnlyWhatItProtected()
    {
        var cache = new RecordingCache(NewCache());
        var store = new DistributedCacheOAuthStateStore(cache, new DistributedCacheOAuthStateStoreOptions
        {
            Protect = bytes => [.. bytes.Select(b => (byte)(b ^ 0x5A))],
            Unprotect = bytes => [.. bytes.Select(b => (byte)(b ^ 0x5A))],
        });

        await store.SetAsync(Pending("s-1"));

        Assert.DoesNotContain("verifier", Encoding.UTF8.GetString(cache.LastValue!));
        Assert.Equal("verifier", (await store.TakeAsync("s-1"))!.CodeVerifier);
    }

    [Fact]
    public async Task Distributed_AnEntryTheProtectorRefuses_IsAbsent()
    {
        var cache = NewCache();
        var writer = new DistributedCacheOAuthStateStore(cache, Plaintext);
        var reader = new DistributedCacheOAuthStateStore(cache, new DistributedCacheOAuthStateStoreOptions
        {
            Protect = bytes => bytes,
            Unprotect = _ => throw new System.Security.Cryptography.CryptographicException("not ours"),
        });

        await writer.SetAsync(Pending("s-1"));

        Assert.Null(await reader.TakeAsync("s-1"));
    }

    [Fact]
    public void Distributed_OnlyOneOfProtectAndUnprotect_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DistributedCacheOAuthStateStore(
            NewCache(), new DistributedCacheOAuthStateStoreOptions { Protect = bytes => bytes }));
    }

    [Fact]
    public async Task Distributed_ALoginStartedOnOneInstance_CompletesOnAnother()
    {
        var store = new DistributedCacheOAuthStateStore(NewCache(), Plaintext);
        using var first = Client(store);
        using var second = Client(store);

        var authorization = await first.StartAuthorizationAsync(AliceHandle.Value, RedirectUri);
        var session = await second.CompleteAuthorizationAsync("code", authorization.State, Issuer);

        Assert.Equal(Alice, session.Did);
        Assert.Equal(AliceHandle, session.Handle);
    }

    [Fact]
    public void Distributed_WithoutAProtectorOrTheOptOut_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new DistributedCacheOAuthStateStore(NewCache(), new DistributedCacheOAuthStateStoreOptions()));

        Assert.Contains("StoreSecretsUnencrypted", ex.Message);
    }

    [Fact]
    public async Task Distributed_AnEntryForAnotherState_IsRefused()
    {
        // An entry copied under another state's key: the key matches, the embedded state does not.
        var cache = new RecordingCache(NewCache());
        var store = new DistributedCacheOAuthStateStore(cache, Plaintext);
        await store.SetAsync(Pending("s-2"));
        var copied = cache.LastValue!;

        await store.SetAsync(Pending("s-1"));
        await cache.SetAsync(cache.Keys[^1], copied, new DistributedCacheEntryOptions());

        Assert.Null(await store.TakeAsync("s-1"));
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    public void RequesterIdFor_GroupsAnIpv6SubscriberByItsSlash64(string address, string expected)
    {
        Assert.Equal(expected, OAuthAuthorizationOptions.RequesterIdFor(System.Net.IPAddress.Parse(address)));
    }

    [Fact]
    public void RequesterIdFor_AnUnknownAddress_IsNull()
    {
        Assert.Null(OAuthAuthorizationOptions.RequesterIdFor(null));
    }

    [Fact]
    public async Task StartAuthorization_AnOverlongAppState_Throws()
    {
        using var oauth = Client();

        await Assert.ThrowsAsync<ArgumentException>(() => oauth.StartAuthorizationAsync(
            AliceHandle.Value, RedirectUri,
            new OAuthAuthorizationOptions { AppState = new string('x', OAuthAuthorizationOptions.MaxAppStateLength + 1) }));
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task CompleteWithAppState_HandsBackTheAppState()
    {
        using var oauth = Client();
        var authorization = await oauth.StartAuthorizationAsync(
            AliceHandle.Value, RedirectUri, new OAuthAuthorizationOptions { AppState = "back-to=/inbox" });

        var result = await oauth.CompleteAuthorizationWithAppStateAsync("code", authorization.State, Issuer);

        Assert.Equal("back-to=/inbox", result.AppState);
        Assert.Equal(Alice, result.Session.Did);
    }

    /// <summary>A distributed cache that records the keys, the last value and options written.</summary>
    private sealed class RecordingCache(IDistributedCache inner) : IDistributedCache
    {
        private readonly List<string> _keys = [];

        public IReadOnlyList<string> Keys
        {
            get
            {
                lock (_keys)
                    return [.. _keys];
            }
        }

        public byte[]? LastValue { get; private set; }

        public DistributedCacheEntryOptions? LastOptions { get; private set; }

        private void Record(string key)
        {
            lock (_keys)
                _keys.Add(key);
        }

        public byte[]? Get(string key) => inner.Get(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            Record(key);
            return inner.GetAsync(key, token);
        }

        public void Refresh(string key) => inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

        public void Remove(string key) => inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Record(key);
            return inner.RemoveAsync(key, token);
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Record(key);
            LastValue = value;
            LastOptions = options;
            return inner.SetAsync(key, value, options, token);
        }
    }
}
