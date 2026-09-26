using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.Identity;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// The per-<c>client_id</c> cache of published attestation keys: an app renewing its credentials
/// does not cost a metadata fetch each time, a rotated key is still picked up, and forged
/// attestations cannot drive a fetch per request.
/// </summary>
public class SpaceClientKeyCacheTests
{
    private const string ClientId = "https://app.example.com/client-metadata.json";
    private static readonly Did AuthorityDid = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");
    private static string Audience => SpaceAuthority.HostAudience(AuthorityDid);

    private readonly ManualClock _clock = new(DateTimeOffset.UtcNow);
    private readonly CountingResolver _resolver = new();

    private SpaceClientAttestationVerifier CreateVerifier(TimeSpan? cacheLifetime = null) =>
        new(
            _resolver,
            new InMemoryJtiReplayStore(_clock),
            new SpaceServerOptions { ClientMetadataCacheLifetime = cacheLifetime ?? TimeSpan.FromMinutes(5) },
            _clock);

    private string Attestation(TestDPoPKey key, string kid) =>
        key.SignJws(
            new Dictionary<string, object> { ["typ"] = SpaceTokens.ClientAttestationType, ["alg"] = "ES256", ["kid"] = kid },
            new Dictionary<string, object>
            {
                ["iss"] = ClientId,
                ["sub"] = ClientId,
                ["aud"] = Audience,
                ["iat"] = _clock.GetUtcNow().ToUnixTimeSeconds(),
                ["exp"] = _clock.GetUtcNow().AddSeconds(60).ToUnixTimeSeconds(),
                ["jti"] = Guid.NewGuid().ToString("N"),
            });

    [Fact]
    public async Task VerifyAsync_SameClientTwice_FetchesItsKeysOnce()
    {
        using var key = new TestDPoPKey();
        _resolver.Keys = [key.ToJsonWebKey("key-1")];
        var verifier = CreateVerifier();

        await verifier.VerifyAsync(Attestation(key, "key-1"), Audience);
        await verifier.VerifyAsync(Attestation(key, "key-1"), Audience);

        Assert.Equal(1, _resolver.Calls);
    }

    [Fact]
    public async Task VerifyAsync_AfterTheCacheLifetime_FetchesAgain()
    {
        using var key = new TestDPoPKey();
        _resolver.Keys = [key.ToJsonWebKey("key-1")];
        var verifier = CreateVerifier();

        await verifier.VerifyAsync(Attestation(key, "key-1"), Audience);
        _clock.Advance(TimeSpan.FromMinutes(6));
        await verifier.VerifyAsync(Attestation(key, "key-1"), Audience);

        Assert.Equal(2, _resolver.Calls);
    }

    [Fact]
    public async Task VerifyAsync_KeyRotatedInSinceTheLastFetch_IsPickedUpWithoutWaitingForExpiry()
    {
        using var oldKey = new TestDPoPKey();
        using var newKey = new TestDPoPKey();
        _resolver.Keys = [oldKey.ToJsonWebKey("key-1")];
        var verifier = CreateVerifier();

        await verifier.VerifyAsync(Attestation(oldKey, "key-1"), Audience);

        _resolver.Keys = [newKey.ToJsonWebKey("key-2")];
        _clock.Advance(TimeSpan.FromSeconds(31));

        var verified = await verifier.VerifyAsync(Attestation(newKey, "key-2"), Audience);

        Assert.Equal(ClientId, verified.ClientId);
        Assert.Equal(2, _resolver.Calls);
    }

    [Fact]
    public async Task VerifyAsync_UnknownKidsInQuickSuccession_FetchAtMostOnceEveryThirtySeconds()
    {
        // A forged attestation naming a kid the client never published must not turn into a
        // fetch of the client's metadata per request.
        using var key = new TestDPoPKey();
        using var forger = new TestDPoPKey();
        _resolver.Keys = [key.ToJsonWebKey("key-1")];
        var verifier = CreateVerifier();

        await verifier.VerifyAsync(Attestation(key, "key-1"), Audience);
        _clock.Advance(TimeSpan.FromSeconds(31));

        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<SpaceVerificationException>(
                () => verifier.VerifyAsync(Attestation(forger, "key-9"), Audience));
        }

        Assert.Equal(2, _resolver.Calls);
    }

    [Fact]
    public async Task VerifyAsync_WithTheCacheOff_FetchesEveryTime()
    {
        using var key = new TestDPoPKey();
        _resolver.Keys = [key.ToJsonWebKey("key-1")];
        var verifier = CreateVerifier(TimeSpan.Zero);

        await verifier.VerifyAsync(Attestation(key, "key-1"), Audience);
        await verifier.VerifyAsync(Attestation(key, "key-1"), Audience);

        Assert.Equal(2, _resolver.Calls);
    }

    [Fact]
    public async Task VerifyAsync_WhileTheClientHostIsFailing_FetchesAtMostOnceEveryThirtySeconds()
    {
        // A failed refetch counts as an attempt, so forged attestations cannot turn a failing
        // client host into a fetch per request.
        using var key = new TestDPoPKey();
        using var forger = new TestDPoPKey();
        _resolver.Keys = [key.ToJsonWebKey("key-1")];
        var verifier = CreateVerifier();
        await verifier.VerifyAsync(Attestation(key, "key-1"), Audience);

        _clock.Advance(TimeSpan.FromSeconds(31));
        _resolver.Fail = true;

        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<SpaceVerificationException>(
                () => verifier.VerifyAsync(Attestation(forger, "key-9"), Audience));
        }

        Assert.Equal(2, _resolver.Calls);

        // The cached keys still serve the client meanwhile.
        await verifier.VerifyAsync(Attestation(key, "key-1"), Audience);
    }

    [Fact]
    public async Task VerifyAsync_AClientWhoseMetadataCannotBeFetched_IsNotFetchedAgainForThirtySeconds()
    {
        using var key = new TestDPoPKey();
        _resolver.Fail = true;
        var verifier = CreateVerifier();

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(Attestation(key, "key-1"), Audience));
        Assert.Equal(1, _resolver.Calls);

        _clock.Advance(TimeSpan.FromSeconds(31));
        _resolver.Fail = false;
        _resolver.Keys = [key.ToJsonWebKey("key-1")];

        await verifier.VerifyAsync(Attestation(key, "key-1"), Audience);
        Assert.Equal(2, _resolver.Calls);
    }

    [Fact]
    public async Task VerifyAsync_ConcurrentAttestationsFromOneClient_ShareOneFetch()
    {
        using var key = new TestDPoPKey();
        _resolver.Keys = [key.ToJsonWebKey("key-1")];
        _resolver.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var verifier = CreateVerifier();

        var attestations = Enumerable.Range(0, 5).Select(_ => Attestation(key, "key-1")).ToList();
        var pending = attestations.Select(a => verifier.VerifyAsync(a, Audience)).ToList();
        _resolver.Gate.SetResult();
        await Task.WhenAll(pending);

        Assert.Equal(1, _resolver.Calls);
    }

    [Fact]
    public async Task VerifyAsync_AFetchOneCallerStoppedWaitingFor_StillServesTheNext()
    {
        using var key = new TestDPoPKey();
        _resolver.Keys = [key.ToJsonWebKey("key-1")];
        _resolver.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var verifier = CreateVerifier(TimeSpan.Zero);
        using var cancel = new CancellationTokenSource();

        var abandoned = verifier.VerifyAsync(Attestation(key, "key-1"), Audience, cancel.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        _resolver.Gate.SetResult();
        var verified = await verifier.VerifyAsync(Attestation(key, "key-1"), Audience);

        Assert.Equal(ClientId, verified.ClientId);
    }

    private sealed class CountingResolver : ISpaceClientMetadataResolver
    {
        public IReadOnlyList<JsonWebKey> Keys { get; set; } = [];

        public bool Fail { get; set; }

        public TaskCompletionSource? Gate { get; set; }

        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async Task<IReadOnlyList<JsonWebKey>> ResolveKeysAsync(string clientId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            if (Gate is not null)
                await Gate.Task;
            if (Fail)
                throw new SpaceVerificationException("InvalidClientAttestation", "The client's host is down.");
            return Keys;
        }
    }
}
