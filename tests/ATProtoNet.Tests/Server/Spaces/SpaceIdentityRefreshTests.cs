using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// How the space verifiers use a cached DID document: one refetch when a signature fails
/// against it, resolution failures reported in the space error contract, and the two documents
/// <see cref="SpaceServiceAuthVerifier.IsRepoHostAsync"/> needs resolved together.
/// </summary>
public class SpaceIdentityRefreshTests
{
    private static readonly Did UserDid = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Did AuthorityDid = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");
    private static readonly Did HostDid = Did.Parse("did:plc:cccccccccccccccccccccccc");
    private static readonly SpaceUri Space = SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/default");
    private static readonly Nsid NotifyWrite = Nsid.Parse(SpaceNsids.NotifyWrite);

    private static string DelegationToken(AtProtoKey key) =>
        SpaceTokens.Create(SpaceTokenType.Delegation, UserDid, Space.Value, key, audience: Space.HostAudience);

    [Fact]
    public async Task DelegationToken_SignedWithAKeyRotatedSinceCaching_VerifiesAfterOneRefresh()
    {
        using var oldKey = AtProtoCrypto.GenerateP256Key();
        using var newKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new FakeDidDocumentResolver()
            .PublishAccount(UserDid, oldKey)
            .Rotate(UserDid, FakeDidDocumentResolver.AccountDocument(UserDid, newKey));
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());

        var verified = await verifier.VerifyAsync(DelegationToken(newKey), Space);

        Assert.Equal(UserDid, verified.UserDid);
        Assert.Equal(1, resolver.RefreshCount);
    }

    [Fact]
    public async Task DelegationToken_ForgedSignature_IsRefusedAfterOneRefresh()
    {
        using var userKey = AtProtoCrypto.GenerateP256Key();
        using var forger = AtProtoCrypto.GenerateP256Key();
        var resolver = new FakeDidDocumentResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(DelegationToken(forger), Space));

        Assert.Equal(SpaceErrors.InvalidDelegationToken, ex.Error);
        Assert.Equal(1, resolver.RefreshCount);
    }

    [Fact]
    public async Task DelegationToken_ExpiredToken_IsRefusedWithoutARefresh()
    {
        // Only a signature failure is something a newer document could change.
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new FakeDidDocumentResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());
        var expired = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, Space.Value, userKey, audience: Space.HostAudience,
            lifetime: TimeSpan.FromMinutes(-5));

        await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(expired, Space));

        Assert.Equal(0, resolver.RefreshCount);
    }

    [Fact]
    public async Task DelegationToken_IssuerThatDoesNotResolve_IsARefusal()
    {
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var verifier = new SpaceDelegationTokenVerifier(new FakeDidDocumentResolver(), new InMemorySpaceReplayStore());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(DelegationToken(userKey), Space));

        Assert.Equal(SpaceErrors.NotAuthorized, ex.Error);
        Assert.IsType<DidResolutionException>(ex.InnerException);
    }

    [Fact]
    public async Task DelegationToken_ResolverThatFailsAnyOtherWay_IsARefusalNotAFault()
    {
        // A custom resolver, or a document it could not make sense of, still means the issuer
        // did not resolve: a 401 in the space error contract, never a 500.
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = Substitute.For<IDidResolver>();
        resolver.ResolveAsync(UserDid, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<DidDocument>(new KeyNotFoundException("no such entry")));
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(DelegationToken(userKey), Space));

        Assert.Equal(SpaceErrors.NotAuthorized, ex.Error);
        Assert.IsType<KeyNotFoundException>(ex.InnerException);
    }

    [Fact]
    public async Task Credential_SignedWithAKeyRotatedSinceCaching_VerifiesAfterOneRefresh()
    {
        using var oldKey = AtProtoCrypto.GenerateP256Key();
        using var newKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var resolver = new FakeDidDocumentResolver()
            .PublishAccount(AuthorityDid, oldKey)
            .Rotate(AuthorityDid, FakeDidDocumentResolver.AccountDocument(AuthorityDid, newKey));
        var verifier = new SpaceCredentialVerifier(resolver, new DPoPProofValidator(new InMemorySpaceReplayStore()));
        const string url = "https://host.example.com/xrpc/com.atproto.space.listRecords";
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, Space.Value, newKey, dpopThumbprint: dpop.Thumbprint);

        var verified = await verifier.VerifyAsync(credential, dpop.Proof("GET", url, accessToken: credential), "GET", url, Space);

        Assert.Equal(Space, verified.Space);
        Assert.Equal(1, resolver.RefreshCount);
    }

    [Fact]
    public async Task ServiceAuth_SignedWithAKeyRotatedSinceCaching_VerifiesAfterOneRefresh()
    {
        using var oldKey = AtProtoCrypto.GenerateP256Key();
        using var newKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new FakeDidDocumentResolver()
            .PublishAccount(HostDid, oldKey)
            .Rotate(HostDid, FakeDidDocumentResolver.AccountDocument(HostDid, newKey));
        var verifier = new SpaceServiceAuthVerifier(resolver, new InMemorySpaceReplayStore());
        using var generator = new ServiceAuthGenerator(HostDid, newKey);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {generator.CreateToken(AuthorityDid.Value, NotifyWrite)}";
        var verified = await verifier.VerifyAsync(context, AuthorityDid.Value, NotifyWrite);

        Assert.Equal(HostDid, verified.Issuer);
        Assert.Equal(1, resolver.RefreshCount);
    }

    [Fact]
    public async Task IsRepoHostAsync_ResolvesBothDocumentsConcurrently()
    {
        // Each resolution waits until the other has started: resolved one after the other, this
        // never completes.
        var resolver = new BarrierResolver(participants: 2)
            .Publish(UserDid, "https://pds.example.com")
            .Publish(HostDid, "https://pds.example.com");
        var verifier = new SpaceServiceAuthVerifier(resolver, new InMemorySpaceReplayStore());

        var isHost = await verifier.IsRepoHostAsync(HostDid, UserDid).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(isHost);
    }

    [Fact]
    public async Task IsRepoHostAsync_RepoWithNoHost_IsFalseEvenWhenTheServiceDoesNotResolve()
    {
        var resolver = new FakeDidDocumentResolver().Publish(UserDid, new DidDocument { Id = UserDid });
        var verifier = new SpaceServiceAuthVerifier(resolver, new InMemorySpaceReplayStore());

        Assert.False(await verifier.IsRepoHostAsync(HostDid, UserDid));
    }

    [Fact]
    public async Task IsRepoHostAsync_DifferentOrigins_IsFalse()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var resolver = new FakeDidDocumentResolver()
            .PublishAccount(UserDid, key, "https://pds.example.com")
            .PublishAccount(HostDid, key, "https://elsewhere.example.com");
        var verifier = new SpaceServiceAuthVerifier(resolver, new InMemorySpaceReplayStore());

        Assert.False(await verifier.IsRepoHostAsync(HostDid, UserDid));
    }

    private sealed class BarrierResolver(int participants) : IDidResolver
    {
        private readonly Dictionary<Did, DidDocument> _documents = new();
        private readonly Barrier _barrier = new(participants);

        public BarrierResolver Publish(Did did, string pds)
        {
            _documents[did] = Identity.DidDocs.Parse(did.Value, pds: pds);
            return this;
        }

        public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default) =>
            Task.Run(() =>
            {
                if (!_barrier.SignalAndWait(TimeSpan.FromSeconds(5), cancellationToken))
                    throw new TimeoutException("The other document was not being resolved at the same time.");
                return _documents[did];
            }, cancellationToken);
    }
}
