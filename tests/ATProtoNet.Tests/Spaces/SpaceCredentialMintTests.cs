using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

/// <summary>
/// How <see cref="SpaceCredentialProvider"/> mints and keeps credentials: one mint per space at a
/// time, no space waiting on another's, and replaced credentials released once they expire.
/// </summary>
public sealed class SpaceCredentialMintTests : IDisposable
{
    private const string Authority = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string AuthorityHost = "https://authority.example.com";

    private readonly FakeSpaceNetwork _network = new();
    private readonly AtProtoClient _client;

    public SpaceCredentialMintTests()
    {
        _client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
            new HttpClient(_network));
    }

    public void Dispose()
    {
        _client.Dispose();
        _network.Dispose();
    }

    private static SpaceUri Space(string skey) => SpaceUri.Parse($"at://{Authority}/space/com.atmoboards.forum/{skey}");

    private SpaceCredentialProvider CreateProvider(TimeSpan? renewalWindow = null) =>
        new(
            _client,
            new SpaceCredentialOptions
            {
                HostResolver = (_, _) => Task.FromResult(AuthorityHost),
                RenewalWindow = renewalWindow ?? TimeSpan.FromMinutes(5),
            },
            new HttpClient(_network));

    [Fact]
    public async Task GetCredentialAsync_ConcurrentCallersForOneSpace_ShareOneMint()
    {
        await using var provider = CreateProvider();
        var gate = _network.Gate(Space("a"));

        var callers = Enumerable.Range(0, 5).Select(_ => provider.GetCredentialAsync(Space("a"))).ToList();
        await _network.WaitForMintAsync(Space("a"));
        gate.SetResult();

        var credentials = await Task.WhenAll(callers);

        Assert.All(credentials, c => Assert.Same(credentials[0], c));
        Assert.Equal(1, _network.MintCount(Space("a")));
    }

    [Fact]
    public async Task GetCredentialAsync_ASlowMintForOneSpace_DoesNotHoldUpAnother()
    {
        await using var provider = CreateProvider();
        var gate = _network.Gate(Space("slow"));

        var slow = provider.GetCredentialAsync(Space("slow"));
        await _network.WaitForMintAsync(Space("slow"));

        // Completes while the other space's authority is still answering.
        var fast = await provider.GetCredentialAsync(Space("fast")).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(Space("fast"), fast.Space);
        Assert.False(slow.IsCompleted);

        gate.SetResult();
        Assert.Equal(Space("slow"), (await slow).Space);
    }

    [Fact]
    public async Task GetCredentialAsync_ForcedRenewal_KeepsTheLiveCredentialItReplacedForItsReaders()
    {
        await using var provider = CreateProvider();

        var first = await provider.GetCredentialAsync(Space("a"));
        var renewed = await provider.GetCredentialAsync(Space("a"), forceRenew: true);

        Assert.NotSame(first, renewed);
        Assert.Equal(1, provider.SupersededCount);

        // A reader created before the renewal still signs with the first key.
        Assert.NotNull(first.Key.GenerateProof("GET", $"{AuthorityHost}/xrpc/com.atproto.space.getRecord"));
    }

    [Fact]
    public async Task GetCredentialAsync_ReplacingAnExpiredCredential_ReleasesItsKey()
    {
        // A replaced credential that has expired is refused by every host, so nothing can use its
        // key any more: it is disposed rather than kept for the provider's lifetime.
        await using var provider = CreateProvider();
        _network.Lifetime = TimeSpan.FromMinutes(-10);

        var expired = await provider.GetCredentialAsync(Space("a"));
        _network.Lifetime = TimeSpan.FromHours(2);
        var current = await provider.GetCredentialAsync(Space("a"));

        Assert.NotSame(expired, current);
        Assert.Equal(0, provider.SupersededCount);
        Assert.Throws<ObjectDisposedException>(() => expired.Key.GenerateProof("GET", AuthorityHost));
    }

    [Fact]
    public async Task GetCredentialAsync_AFailingMint_IsSharedByItsWaiters()
    {
        // A refusal costs one exchange, and one delegation token, not one per waiter.
        await using var provider = CreateProvider();
        _network.Refuse = true;
        var gate = _network.Gate(Space("a"));

        var callers = Enumerable.Range(0, 5).Select(_ => provider.GetCredentialAsync(Space("a"))).ToList();
        await _network.WaitForMintAsync(Space("a"));
        gate.SetResult();

        foreach (var caller in callers)
            await Assert.ThrowsAsync<SpaceCredentialException>(() => caller);
        Assert.Equal(1, _network.MintCount(Space("a")));

        // The failure is not remembered: the next call tries again.
        _network.Refuse = false;
        Assert.Equal(Space("a"), (await provider.GetCredentialAsync(Space("a"))).Space);
        Assert.Equal(2, _network.MintCount(Space("a")));
    }

    [Fact]
    public async Task GetCredentialAsync_TheFirstWaiterCancelling_DoesNotCancelTheMintForTheOthers()
    {
        await using var provider = CreateProvider();
        var gate = _network.Gate(Space("a"));
        using var cancel = new CancellationTokenSource();

        var first = provider.GetCredentialAsync(Space("a"), cancellationToken: cancel.Token);
        await _network.WaitForMintAsync(Space("a"));
        var second = provider.GetCredentialAsync(Space("a"));

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        gate.SetResult();
        Assert.Equal(Space("a"), (await second).Space);
        Assert.Equal(1, _network.MintCount(Space("a")));
    }

    [Fact]
    public async Task Dispose_WhileAMintIsInFlight_StopsItAndStoresNothing()
    {
        var provider = CreateProvider();
        _network.Gate(Space("a"));

        var pending = provider.GetCredentialAsync(Space("a"));
        await _network.WaitForMintAsync(Space("a"));
        provider.Dispose();

        var ex = await Record.ExceptionAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(ex is OperationCanceledException or ObjectDisposedException, ex?.ToString());
        Assert.Equal(0, provider.SupersededCount);
    }

    /// <summary>
    /// A PDS that hands out delegation tokens and an authority that mints credentials bound to
    /// whatever key the request's DPoP proof carries.
    /// </summary>
    private sealed class FakeSpaceNetwork : HttpMessageHandler
    {
        private readonly AtProtoKey _authorityKey = AtProtoCrypto.GenerateP256Key();
        private readonly Dictionary<string, TaskCompletionSource> _gates = [];
        private readonly Dictionary<string, TaskCompletionSource> _started = [];
        private readonly Dictionary<string, int> _mints = [];

        public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(2);

        /// <summary>Refuses every credential request with <c>NotAuthorized</c>.</summary>
        public bool Refuse { get; set; }

        public TaskCompletionSource Gate(SpaceUri space)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gates)
                _gates[space.Value] = gate;
            return gate;
        }

        public Task WaitForMintAsync(SpaceUri space) => Started(space).Task.WaitAsync(TimeSpan.FromSeconds(10));

        public int MintCount(SpaceUri space)
        {
            lock (_mints)
                return _mints.GetValueOrDefault(space.Value);
        }

        private TaskCompletionSource Started(SpaceUri space)
        {
            lock (_started)
            {
                if (!_started.TryGetValue(space.Value, out var started))
                    _started[space.Value] = started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return started;
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("com.atproto.space.getDelegationToken", StringComparison.Ordinal))
                return Json("""{"token":"delegation"}""");

            if (!path.EndsWith("com.atproto.space.getSpaceCredential", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var space = SpaceUri.Parse(body.RootElement.GetProperty("space").GetString()!);

            lock (_mints)
                _mints[space.Value] = _mints.GetValueOrDefault(space.Value) + 1;
            Started(space).TrySetResult();

            TaskCompletionSource? gate;
            lock (_gates)
                _gates.TryGetValue(space.Value, out gate);
            if (gate is not null)
                await gate.Task.WaitAsync(cancellationToken);

            if (Refuse)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("""{"error":"NotAuthorized","message":"no"}""", Encoding.UTF8, "application/json"),
                };
            }

            var proofHeader = TestJws.DecodeJson(request.Headers.GetValues("DPoP").Single(), 0);
            var jwk = proofHeader.GetProperty("jwk").Deserialize<JsonWebKey>()!;

            var credential = SpaceTokens.Create(
                SpaceTokenType.Credential, Authority, space.Value, _authorityKey,
                dpopThumbprint: DPoP.Thumbprint(jwk), lifetime: Lifetime);

            return Json(JsonSerializer.Serialize(new { credential }));
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _authorityKey.Dispose();
            base.Dispose(disposing);
        }
    }
}
