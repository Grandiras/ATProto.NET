using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.Identity;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// The verified-credential cache on the repo-host read path: a credential whose signature
/// verified is not verified again while it is cached, and everything that depends on the request
/// or the authority's current key — expiry, the requested space, the DPoP proof and its
/// <c>jti</c>, the key the DID document publishes now — still is.
/// </summary>
public class SpaceCredentialCacheTests
{
    private static readonly Did AuthorityDid = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");
    private const string Url = "https://pds.example.com/xrpc/com.atproto.space.getRecord";

    private static SpaceUri Space(string skey = "default", Did? authority = null) =>
        SpaceUri.Parse($"at://{authority ?? AuthorityDid}/space/com.atmoboards.forum/{skey}");

    private readonly ManualClock _clock = new(DateTimeOffset.UtcNow);

    private SpaceCredentialVerifier CreateVerifier(IDidResolver resolver, SpaceServerOptions? options = null)
    {
        options ??= new SpaceServerOptions();
        return new SpaceCredentialVerifier(
            resolver, new DPoPProofValidator(new InMemoryJtiReplayStore(_clock), options, _clock), options, _clock);
    }

    private string Proof(TestDPoPKey dpop, string credential) =>
        dpop.Proof("GET", Url, accessToken: credential, issuedAt: _clock.GetUtcNow());

    [Fact]
    public async Task VerifyAsync_SameCredentialTwice_ChecksItsSignatureOnce()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var verifier = CreateVerifier(new FakeDidDocumentResolver().PublishAccount(AuthorityDid.Value, authorityKey));
        var credential = Credential(authorityKey, dpop);

        await verifier.VerifyAsync(credential, Proof(dpop, credential), "GET", Url, Space());
        var second = await verifier.VerifyAsync(credential, Proof(dpop, credential), "GET", Url, Space());

        Assert.Equal(Space(), second.Space);
        Assert.Equal(dpop.Thumbprint, second.Proof.KeyThumbprint);
        Assert.Equal(1, verifier.SignatureChecks);
        Assert.Equal(1, verifier.CachedCount);
    }

    [Fact]
    public async Task VerifyAsync_CachedCredential_StillRefusesAReplayedProof()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var verifier = CreateVerifier(new FakeDidDocumentResolver().PublishAccount(AuthorityDid.Value, authorityKey));
        var credential = Credential(authorityKey, dpop);
        var proof = Proof(dpop, credential);

        await verifier.VerifyAsync(credential, proof, "GET", Url, Space());

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(credential, proof, "GET", Url, Space()));
    }

    [Fact]
    public async Task VerifyAsync_CachedCredential_StillRefusesAProofFromAnotherKey()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var holder = new TestDPoPKey();
        using var thief = new TestDPoPKey();
        var verifier = CreateVerifier(new FakeDidDocumentResolver().PublishAccount(AuthorityDid.Value, authorityKey));
        var credential = Credential(authorityKey, holder);

        await verifier.VerifyAsync(credential, Proof(holder, credential), "GET", Url, Space());

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(credential, Proof(thief, credential), "GET", Url, Space()));
    }

    [Fact]
    public async Task VerifyAsync_CachedCredential_StillChecksTheRequestedSpace()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var verifier = CreateVerifier(new FakeDidDocumentResolver().PublishAccount(AuthorityDid.Value, authorityKey));
        var credential = Credential(authorityKey, dpop);

        await verifier.VerifyAsync(credential, Proof(dpop, credential), "GET", Url, Space());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(credential, Proof(dpop, credential), "GET", Url, Space("other")));

        Assert.Contains("not the requested", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_CachedCredentialPastItsExpiry_IsRefused()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var verifier = CreateVerifier(new FakeDidDocumentResolver().PublishAccount(AuthorityDid.Value, authorityKey));
        var credential = Credential(authorityKey, dpop, lifetime: TimeSpan.FromMinutes(1));

        await verifier.VerifyAsync(credential, Proof(dpop, credential), "GET", Url, Space());

        _clock.Advance(TimeSpan.FromMinutes(2));

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(credential, Proof(dpop, credential), "GET", Url, Space()));

        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Key rotation: the cache never outlives the document ──────

    [Fact]
    public async Task VerifyAsync_OnceTheResolverServesARotatedKey_RefusesTheCachedCredentialAtOnce()
    {
        // No clock advance at all: the entry is only as good as the key the authority publishes
        // now, not as the one it published when the entry was made.
        using var oldKey = AtProtoCrypto.GenerateP256Key();
        using var newKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var resolver = new FakeDidDocumentResolver().PublishAccount(AuthorityDid.Value, oldKey);
        var verifier = CreateVerifier(resolver);
        var credential = Credential(oldKey, dpop);

        await verifier.VerifyAsync(credential, Proof(dpop, credential), "GET", Url, Space());
        resolver.PublishAccount(AuthorityDid.Value, newKey);

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(credential, Proof(dpop, credential), "GET", Url, Space()));
        Assert.Equal(0, verifier.CachedCount);
    }

    [Fact]
    public async Task VerifyAsync_CachedAndFreshCredentials_StopVerifyingAtTheSameMoment()
    {
        // The probe's shape: an entry made from a document that was about to go stale must not
        // outlive that document. The resolver keeps serving the old document, as a cache would,
        // until a failed check refreshes it.
        using var oldKey = AtProtoCrypto.GenerateP256Key();
        using var newKey = AtProtoCrypto.GenerateP256Key();
        using var cachedHolder = new TestDPoPKey();
        using var freshHolder = new TestDPoPKey();
        using var rotatedHolder = new TestDPoPKey();
        var resolver = new FakeDidDocumentResolver().PublishAccount(AuthorityDid.Value, oldKey);
        var verifier = CreateVerifier(resolver);
        var cached = Credential(oldKey, cachedHolder);
        await verifier.VerifyAsync(cached, Proof(cachedHolder, cached), "GET", Url, Space());

        // Rotated but still served stale: an old-key credential verifies whether cached or new,
        // however long the entry has existed.
        resolver.Rotate(AuthorityDid.Value, FakeDidDocumentResolver.AccountDocument(AuthorityDid.Value, newKey));
        _clock.Advance(TimeSpan.FromMinutes(9));
        await verifier.VerifyAsync(cached, Proof(cachedHolder, cached), "GET", Url, Space());
        var fresh = Credential(oldKey, freshHolder);
        await verifier.VerifyAsync(fresh, Proof(freshHolder, fresh), "GET", Url, Space());

        // A credential under the new key refreshes the document; from then on an old-key
        // credential is refused, cached or not.
        var rotated = Credential(newKey, rotatedHolder);
        await verifier.VerifyAsync(rotated, Proof(rotatedHolder, rotated), "GET", Url, Space());

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(cached, Proof(cachedHolder, cached), "GET", Url, Space()));
        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(fresh, Proof(freshHolder, fresh), "GET", Url, Space()));
    }

    [Fact]
    public async Task VerifyAsync_ANewDocumentWithTheSameKey_KeepsTheEntry()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var resolver = new FakeDidDocumentResolver().PublishAccount(AuthorityDid.Value, authorityKey);
        var verifier = CreateVerifier(resolver);
        var credential = Credential(authorityKey, dpop);

        await verifier.VerifyAsync(credential, Proof(dpop, credential), "GET", Url, Space());
        resolver.PublishAccount(AuthorityDid.Value, authorityKey, "https://moved.example.com");
        await verifier.VerifyAsync(credential, Proof(dpop, credential), "GET", Url, Space());

        Assert.Equal(1, verifier.SignatureChecks);
    }

    // ── Bounds ────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_WithTheCacheOff_ChecksEverySignature()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var verifier = CreateVerifier(
            new FakeDidDocumentResolver().PublishAccount(AuthorityDid.Value, authorityKey),
            new SpaceServerOptions { VerifiedCredentialCacheCapacity = 0 });
        var credential = Credential(authorityKey, dpop);

        await verifier.VerifyAsync(credential, Proof(dpop, credential), "GET", Url, Space());
        await verifier.VerifyAsync(credential, Proof(dpop, credential), "GET", Url, Space());

        Assert.Equal(2, verifier.SignatureChecks);
        Assert.Equal(0, verifier.CachedCount);
    }

    [Fact]
    public async Task VerifyAsync_WhenFull_EvictsTheLeastRecentlyUsedRatherThanRefusingANewcomer()
    {
        var (resolver, keys) = Authorities(3);
        using var a = new TestDPoPKey();
        using var b = new TestDPoPKey();
        using var c = new TestDPoPKey();
        var verifier = CreateVerifier(resolver, new SpaceServerOptions { VerifiedCredentialCacheCapacity = 2 });
        var credA = Credential(keys[0].Key, a, keys[0].Did);
        var credB = Credential(keys[1].Key, b, keys[1].Did);
        var credC = Credential(keys[2].Key, c, keys[2].Did);

        await verifier.VerifyAsync(credA, Proof(a, credA), "GET", Url, Space(authority: keys[0].Did));
        await verifier.VerifyAsync(credB, Proof(b, credB), "GET", Url, Space(authority: keys[1].Did));
        await verifier.VerifyAsync(credA, Proof(a, credA), "GET", Url, Space(authority: keys[0].Did)); // A is now the most recent
        await verifier.VerifyAsync(credC, Proof(c, credC), "GET", Url, Space(authority: keys[2].Did)); // evicts B, cached
        var checks = verifier.SignatureChecks;

        await verifier.VerifyAsync(credA, Proof(a, credA), "GET", Url, Space(authority: keys[0].Did));
        await verifier.VerifyAsync(credC, Proof(c, credC), "GET", Url, Space(authority: keys[2].Did));
        Assert.Equal(checks, verifier.SignatureChecks);

        await verifier.VerifyAsync(credB, Proof(b, credB), "GET", Url, Space(authority: keys[1].Did));
        Assert.Equal(checks + 1, verifier.SignatureChecks);
        keys.ForEach(k => k.Key.Dispose());
    }

    [Fact]
    public async Task VerifyAsync_AForeignAuthorityFillingTheCache_DisplacesOnlyItsOwnEntries()
    {
        // Any DID can mint credentials for spaces it is the authority of. However many it
        // presents, it holds a quarter of the cache, and a credential in steady use by another
        // authority stays cached.
        var (resolver, keys) = Authorities(2);
        var (legit, attacker) = (keys[0], keys[1]);
        using var holder = new TestDPoPKey();
        var verifier = CreateVerifier(resolver, new SpaceServerOptions { VerifiedCredentialCacheCapacity = 8 });
        var steady = Credential(legit.Key, holder, legit.Did);
        await verifier.VerifyAsync(steady, Proof(holder, steady), "GET", Url, Space(authority: legit.Did));

        for (var i = 0; i < 50; i++)
        {
            using var dpop = new TestDPoPKey();
            var minted = Credential(attacker.Key, dpop, attacker.Did, skey: $"s{i}");
            await verifier.VerifyAsync(minted, Proof(dpop, minted), "GET", Url, Space($"s{i}", attacker.Did));
        }

        var checks = verifier.SignatureChecks;
        await verifier.VerifyAsync(steady, Proof(holder, steady), "GET", Url, Space(authority: legit.Did));

        Assert.Equal(checks, verifier.SignatureChecks);
        Assert.Equal(1 + 2, verifier.CachedCount); // the legit entry, and the attacker's quota of 8 / 4
        keys.ForEach(k => k.Key.Dispose());
    }

    [Fact]
    public async Task VerifyAsync_ACredentialThatFailed_IsNotRemembered()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var impostorKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var verifier = CreateVerifier(new FakeDidDocumentResolver().PublishAccount(AuthorityDid.Value, authorityKey));
        var forged = Credential(impostorKey, dpop);

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(forged, Proof(dpop, forged), "GET", Url, Space()));
        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(forged, Proof(dpop, forged), "GET", Url, Space()));

        Assert.Equal(0, verifier.CachedCount);
    }

    private static (FakeDidDocumentResolver Resolver, List<(Did Did, AtProtoKey Key)> Keys) Authorities(int count)
    {
        var resolver = new FakeDidDocumentResolver();
        var keys = new List<(Did, AtProtoKey)>();
        for (var i = 0; i < count; i++)
        {
            var did = Did.Parse($"did:plc:{new string((char)('b' + i), 24)}");
            var key = AtProtoCrypto.GenerateP256Key();
            resolver.PublishAccount(did.Value, key);
            keys.Add((did, key));
        }

        return (resolver, keys);
    }

    private static string Credential(
        AtProtoKey key, TestDPoPKey dpop, Did? authority = null, string skey = "default", TimeSpan? lifetime = null) =>
        SpaceTokens.Create(
            SpaceTokenType.Credential,
            (authority ?? AuthorityDid).Value,
            Space(skey, authority).Value,
            key,
            dpopThumbprint: dpop.Thumbprint,
            lifetime: lifetime);
}
