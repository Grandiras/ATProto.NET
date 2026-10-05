using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.Identity;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// The verified-credential cache on the repo-host read path: a credential whose signature
/// verified is not verified again while it is cached, and everything that depends on the request
/// or the authority's current key — expiry, the requested space, the key the DID document
/// publishes now — still is.
/// </summary>
public class SpaceCredentialCacheTests
{
    private static readonly Did AuthorityDid = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");

    private static SpaceUri Space(string skey = "default", Did? authority = null) =>
        SpaceUri.Parse($"at://{authority ?? AuthorityDid}/space/com.atmoboards.forum/{skey}");

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    private SpaceCredentialVerifier CreateVerifier(IDidResolver resolver, SpaceServerOptions? options = null)
    {
        options ??= new SpaceServerOptions();
        return new SpaceCredentialVerifier(resolver, options, _clock);
    }

    [Fact]
    public async Task VerifyAsync_SameCredentialTwice_ChecksItsSignatureOnce()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        var verifier = CreateVerifier(new StubDidResolver().PublishAccount(AuthorityDid.Value, authorityKey));
        var credential = Credential(authorityKey);

        await verifier.VerifyAsync(credential, Space());
        var second = await verifier.VerifyAsync(credential, Space());

        Assert.Equal(Space(), second.Space);
        Assert.Equal(1, verifier.SignatureChecks);
        Assert.Equal(1, verifier.CachedCount);
    }

    [Fact]
    public async Task VerifyAsync_CachedCredential_StillChecksTheRequestedSpace()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        var verifier = CreateVerifier(new StubDidResolver().PublishAccount(AuthorityDid.Value, authorityKey));
        var credential = Credential(authorityKey);

        await verifier.VerifyAsync(credential, Space());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(credential, Space("other")));

        Assert.Contains("not the requested", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_CachedCredentialPastItsExpiry_IsRefused()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        var verifier = CreateVerifier(new StubDidResolver().PublishAccount(AuthorityDid.Value, authorityKey));
        var credential = Credential(authorityKey, lifetime: TimeSpan.FromMinutes(1));

        await verifier.VerifyAsync(credential, Space());

        _clock.Advance(TimeSpan.FromMinutes(2));

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(credential, Space()));

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
        var resolver = new StubDidResolver().PublishAccount(AuthorityDid.Value, oldKey);
        var verifier = CreateVerifier(resolver);
        var credential = Credential(oldKey);

        await verifier.VerifyAsync(credential, Space());
        resolver.PublishAccount(AuthorityDid.Value, newKey);

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(credential, Space()));
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
        var resolver = new StubDidResolver().PublishAccount(AuthorityDid.Value, oldKey);
        var verifier = CreateVerifier(resolver);
        var cached = Credential(oldKey);
        await verifier.VerifyAsync(cached, Space());

        // Rotated but still served stale: an old-key credential verifies whether cached or new,
        // however long the entry has existed.
        resolver.Rotate(AuthorityDid.Value, StubDidResolver.AccountDocument(AuthorityDid.Value, newKey));
        _clock.Advance(TimeSpan.FromMinutes(9));
        await verifier.VerifyAsync(cached, Space());
        var fresh = Credential(oldKey);
        await verifier.VerifyAsync(fresh, Space());

        // A credential under the new key refreshes the document; from then on an old-key
        // credential is refused, cached or not.
        var rotated = Credential(newKey);
        await verifier.VerifyAsync(rotated, Space());

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(cached, Space()));
        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(fresh, Space()));
    }

    [Fact]
    public async Task VerifyAsync_ANewDocumentWithTheSameKey_KeepsTheEntry()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(AuthorityDid.Value, authorityKey);
        var verifier = CreateVerifier(resolver);
        var credential = Credential(authorityKey);

        await verifier.VerifyAsync(credential, Space());
        resolver.PublishAccount(AuthorityDid.Value, authorityKey, "https://moved.example.com");
        await verifier.VerifyAsync(credential, Space());

        Assert.Equal(1, verifier.SignatureChecks);
    }

    // ── Bounds ────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_WithTheCacheOff_ChecksEverySignature()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        var verifier = CreateVerifier(
            new StubDidResolver().PublishAccount(AuthorityDid.Value, authorityKey),
            new SpaceServerOptions { VerifiedCredentialCacheCapacity = 0 });
        var credential = Credential(authorityKey);

        await verifier.VerifyAsync(credential, Space());
        await verifier.VerifyAsync(credential, Space());

        Assert.Equal(2, verifier.SignatureChecks);
        Assert.Equal(0, verifier.CachedCount);
    }

    [Fact]
    public async Task VerifyAsync_WhenFull_EvictsTheLeastRecentlyUsedRatherThanRefusingANewcomer()
    {
        var (resolver, keys) = Authorities(3);
        var verifier = CreateVerifier(resolver, new SpaceServerOptions { VerifiedCredentialCacheCapacity = 2 });
        var credA = Credential(keys[0].Key, keys[0].Did);
        var credB = Credential(keys[1].Key, keys[1].Did);
        var credC = Credential(keys[2].Key, keys[2].Did);

        await verifier.VerifyAsync(credA, Space(authority: keys[0].Did));
        await verifier.VerifyAsync(credB, Space(authority: keys[1].Did));
        await verifier.VerifyAsync(credA, Space(authority: keys[0].Did)); // A is now the most recent
        await verifier.VerifyAsync(credC, Space(authority: keys[2].Did)); // evicts B, cached
        var checks = verifier.SignatureChecks;

        await verifier.VerifyAsync(credA, Space(authority: keys[0].Did));
        await verifier.VerifyAsync(credC, Space(authority: keys[2].Did));
        Assert.Equal(checks, verifier.SignatureChecks);

        await verifier.VerifyAsync(credB, Space(authority: keys[1].Did));
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
        var verifier = CreateVerifier(resolver, new SpaceServerOptions { VerifiedCredentialCacheCapacity = 8 });
        var steady = Credential(legit.Key, legit.Did);
        await verifier.VerifyAsync(steady, Space(authority: legit.Did));

        for (var i = 0; i < 50; i++)
        {
            var minted = Credential(attacker.Key, attacker.Did, skey: $"s{i}");
            await verifier.VerifyAsync(minted, Space($"s{i}", attacker.Did));
        }

        var checks = verifier.SignatureChecks;
        await verifier.VerifyAsync(steady, Space(authority: legit.Did));

        Assert.Equal(checks, verifier.SignatureChecks);
        Assert.Equal(1 + 2, verifier.CachedCount); // the legit entry, and the attacker's quota of 8 / 4
        keys.ForEach(k => k.Key.Dispose());
    }

    [Fact]
    public async Task VerifyAsync_ACredentialThatFailed_IsNotRemembered()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var impostorKey = AtProtoCrypto.GenerateP256Key();
        var verifier = CreateVerifier(new StubDidResolver().PublishAccount(AuthorityDid.Value, authorityKey));
        var forged = Credential(impostorKey);

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(forged, Space()));
        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(forged, Space()));

        Assert.Equal(0, verifier.CachedCount);
    }

    private static (StubDidResolver Resolver, List<(Did Did, AtProtoKey Key)> Keys) Authorities(int count)
    {
        var resolver = new StubDidResolver();
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

    // A credential bound to a fresh holder key, whose private half the cache tests have no use for.
    private static string Credential(
        AtProtoKey key, Did? authority = null, string skey = "default", TimeSpan? lifetime = null)
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        return SpaceTokens.Create(
            SpaceTokenType.Credential,
            (authority ?? AuthorityDid).Value,
            Space(skey, authority).Value,
            key,
            confirmationKeyId: holder.ToDidKey(),
            lifetime: lifetime);
    }
}
