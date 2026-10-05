using System.Net;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.TestSupport;

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
            new HttpClient(_network.Stub));
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
                RenewalWindow = renewalWindow ?? TimeSpan.FromMinutes(1),
            },
            new HttpClient(_network.Stub));

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
        Assert.Equal(64, first.Key.Sign("request"u8).Length);
    }

    [Fact]
    public async Task GetCredentialAsync_ReplacingAnExpiredCredential_ReleasesItsKey()
    {
        // A replaced credential that has expired is refused by every host, so nothing can use its
        // key any more: it is disposed rather than kept for the provider's lifetime.
        await using var provider = CreateProvider();
        _network.Lifetime = TimeSpan.FromMinutes(-10);

        var expired = await provider.GetCredentialAsync(Space("a"));
        _network.Lifetime = SpaceTokens.DefaultCredentialLifetime;
        var current = await provider.GetCredentialAsync(Space("a"));

        Assert.NotSame(expired, current);
        Assert.Equal(0, provider.SupersededCount);
        Assert.Throws<ObjectDisposedException>(() => expired.Key.Sign("request"u8.ToArray()));
    }

    [Fact]
    public async Task GetCredentialAsync_ExchangeIsSignedWithTheKeyTheCredentialIsBoundTo()
    {
        // The fake authority verifies the exchange as the protocol specifies and binds the credential
        // to the keyid it names, so a credential here is proof the signature verified.
        await using var provider = CreateProvider();

        var credential = await provider.GetCredentialAsync(Space("a"));

        var request = _network.Stub.To("com.atproto.space.getSpaceCredential").Single();
        Assert.Equal("Bearer delegation", request.Headers.GetValues("Authorization").Single());
        Assert.StartsWith($"atproto-space=(\"authorization\");keyid=\"did:key:zDn", request.Headers.GetValues("Signature-Input").Single(), StringComparison.Ordinal);
        Assert.Equal(credential.Key.ToDidKey(), credential.Token.ConfirmationKeyId);
        Assert.Equal(SpaceTokens.DefaultCredentialLifetime, credential.ExpiresAt - credential.Token.IssuedAt);
    }

    [Fact]
    public async Task GetCredentialAsync_EachCredentialGetsAFreshKey()
    {
        await using var provider = CreateProvider();

        var first = await provider.GetCredentialAsync(Space("a"));
        var second = await provider.GetCredentialAsync(Space("a"), forceRenew: true);

        Assert.NotEqual(first.Token.ConfirmationKeyId, second.Token.ConfirmationKeyId);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(90)]
    [InlineData(600)]
    public async Task GetCredentialAsync_CredentialWithinItsRealLifetime_IsReusedWhateverTheWindow(int seconds)
    {
        // Renewal follows the expiry the authority actually granted: a credential shorter than the
        // renewal window is renewed half-way through its life, not on every call.
        await using var provider = CreateProvider(renewalWindow: TimeSpan.FromMinutes(5));
        _network.Lifetime = TimeSpan.FromSeconds(seconds);

        var first = await provider.GetCredentialAsync(Space("a"));
        var second = await provider.GetCredentialAsync(Space("a"));

        Assert.Same(first, second);
        Assert.Equal(1, _network.MintCount(Space("a")));
    }

    [Fact]
    public async Task GetCredentialAsync_CredentialNearItsRealExpiry_IsRenewed()
    {
        await using var provider = CreateProvider();
        _network.Lifetime = TimeSpan.FromSeconds(2);

        var first = await provider.GetCredentialAsync(Space("a"));
        await Task.Delay(TimeSpan.FromSeconds(1.3));
        var second = await provider.GetCredentialAsync(Space("a"));

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task Reader_RepoOperation_IsSignedForTheRepoOwner()
    {
        await using var provider = CreateProvider();
        var space = Space("a");
        _network.Stub.On("com.atproto.space.getLatestCommit", "{}");
        using var reader = await provider.CreateReaderAsync(space, "https://member.example.com");

        var member = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
        await Record.ExceptionAsync(() => reader.Space.GetLatestCommitAsync(space, member));

        var request = _network.Stub.To("com.atproto.space.getLatestCommit").Single();
        AssertSigned(request, reader.Credential, expectedAudience: member);
    }

    [Fact]
    public async Task Reader_SpaceHostOperation_IsSignedForTheAuthority()
    {
        await using var provider = CreateProvider();
        var space = Space("a");
        _network.Stub.On("com.atproto.space.listRepos", """{"repos":[]}""");
        using var reader = await provider.CreateReaderAsync(space, AuthorityHost);

        await reader.Space.ListReposAsync(space);

        var request = _network.Stub.To("com.atproto.space.listRepos").Single();
        AssertSigned(request, reader.Credential, expectedAudience: Did.Parse(Authority));
    }

    // The request carries the credential under its own scheme and an audience, and a signature that verifies
    // against the credential's bound key — which is all a host checks.
    private static void AssertSigned(HttpStub.RecordedRequest request, SpaceCredential credential, Did expectedAudience)
    {
        var authorization = request.Headers.GetValues("Authorization").Single();
        Assert.Equal($"Atproto-Space {credential.Raw}", authorization);
        Assert.False(request.Headers.Contains("DPoP"));

        var audience = request.Headers.GetValues("Atproto-Space-Audience").Single();
        Assert.Equal(expectedAudience.Value, audience);

        var keyId = SpaceHttpSignature.VerifyRequest(
            authorization,
            Did.Parse(audience),
            request.Headers.GetValues("Signature-Input").Single(),
            request.Headers.GetValues("Signature").Single(),
            credential.Token.ConfirmationKeyId!);
        Assert.Equal(credential.Key.ToDidKey(), keyId);
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
    /// whichever key the request's HTTP message signature names — after verifying it.
    /// </summary>
    private sealed class FakeSpaceNetwork : IDisposable
    {
        private readonly AtProtoKey _authorityKey = AtProtoCrypto.GenerateP256Key();
        private readonly Dictionary<string, TaskCompletionSource> _gates = [];
        private readonly Dictionary<string, TaskCompletionSource> _started = [];
        private readonly Dictionary<string, int> _mints = [];

        public FakeSpaceNetwork()
        {
            Stub.On("com.atproto.space.getDelegationToken", """{"token":"delegation"}""");
            Stub.On("com.atproto.space.getSpaceCredential", MintAsync);
        }

        public HttpStub Stub { get; } = new();

        public TimeSpan Lifetime { get; set; } = SpaceTokens.DefaultCredentialLifetime;

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

        private async Task<HttpResponseMessage> MintAsync(HttpStub.RecordedRequest request, CancellationToken cancellationToken)
        {
            var space = SpaceUri.Parse(request.JsonBody.GetProperty("space").GetString()!);

            lock (_mints)
                _mints[space.Value] = _mints.GetValueOrDefault(space.Value) + 1;
            Started(space).TrySetResult();

            TaskCompletionSource? gate;
            lock (_gates)
                _gates.TryGetValue(space.Value, out gate);
            if (gate is not null)
                await gate.Task.WaitAsync(cancellationToken);

            if (Refuse)
                return HttpStub.JsonResponse("""{"error":"NotAuthorized","message":"no"}""", HttpStatusCode.Forbidden);

            var keyId = SpaceHttpSignature.VerifyExchange(
                request.Headers.GetValues("Authorization").Single(),
                request.Headers.GetValues("Signature-Input").Single(),
                request.Headers.GetValues("Signature").Single());

            return HttpStub.JsonResponse(JsonSerializer.Serialize(new { credential = Credential(space, keyId) }));
        }

        // A negative Lifetime mints a credential that expired that long ago, which Create refuses to.
        private string Credential(SpaceUri space, string keyId)
        {
            if (Lifetime > TimeSpan.Zero)
                return SpaceTokens.Create(
                    SpaceTokenType.Credential, Authority, space.Value, _authorityKey,
                    confirmationKeyId: keyId, lifetime: Lifetime);

            var now = DateTimeOffset.UtcNow;
            return TestJws.Mint(
                new Dictionary<string, object> { ["typ"] = SpaceTokens.CredentialType, ["alg"] = "ES256", ["kid"] = "#atproto" },
                new Dictionary<string, object>
                {
                    ["iss"] = Authority,
                    ["sub"] = space.Value,
                    ["cnf"] = new Dictionary<string, string> { ["kid"] = keyId },
                    ["iat"] = now.Add(Lifetime * 2).ToUnixTimeSeconds(),
                    ["exp"] = now.Add(Lifetime).ToUnixTimeSeconds(),
                    ["jti"] = Guid.NewGuid().ToString("N"),
                },
                input => _authorityKey.Sign(input));
        }

        public void Dispose()
        {
            _authorityKey.Dispose();
            Stub.Dispose();
        }
    }
}
