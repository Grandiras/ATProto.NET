using ATProtoNet.Crypto;

namespace ATProtoNet.Tests.Crypto;

/// <summary>The injectable LRU of parsed verification keys.</summary>
public sealed class VerificationKeyCacheTests
{
    private static readonly byte[] Message = "cached verification"u8.ToArray();

    [Fact]
    [Trait("Spec", "CRY-25")]
    public void Verify_ValidAndTamperedSignatures_MatchTheKeysOwnVerify()
    {
        using var key = SigningKey.Generate(KeyCurve.P256);
        var signature = key.Sign(Message);
        var tampered = (byte[])signature.Clone();
        tampered[10] ^= 1;
        var cache = new VerificationKeyCache(capacity: 4);

        Assert.True(cache.Verify(key.PublicKey.DidKey, Message, signature));
        Assert.False(cache.Verify(key.PublicKey.DidKey, Message, tampered));
        Assert.True(cache.Verify(key.PublicKey.DidKey, Message, SignatureTests.HighS(signature, KeyCurve.P256), SignaturePolicy.AcceptHighS));
        Assert.Same(cache.Get(key.PublicKey.DidKey), cache.Get(DidKey.Parse(key.PublicKey.DidKey.Value)));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    [Trait("Spec", "CRY-25")]
    public void Get_MoreKeysThanCapacity_EvictsTheLeastRecentlyUsed()
    {
        var keys = Enumerable.Range(0, 3).Select(_ =>
        {
            using var key = SigningKey.Generate(KeyCurve.P256);
            return key.PublicKey.DidKey;
        }).ToArray();
        var cache = new VerificationKeyCache(capacity: 2);
        var first = cache.Get(keys[0]);
        var second = cache.Get(keys[1]);
        cache.Get(keys[0]);

        cache.Get(keys[2]);

        Assert.Equal(2, cache.Count);
        Assert.Same(first, cache.Get(keys[0]));
        Assert.NotSame(second, cache.Get(keys[1]));
    }

    [Fact]
    [Trait("Spec", "CRY-25")]
    public void Constructor_CapacityBelowOne_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new VerificationKeyCache(0));

    [Fact]
    [Trait("Spec", "CRY-26")]
    public void Get_MalformedDidKey_FailsBeforeTheCacheAndCachesNothing()
    {
        var cache = new VerificationKeyCache();

        Assert.False(DidKey.TryParse("did:key:z6MkhaXgBZDvotDkL5257faiztiGiC2QtKLGpbnnEGta2doK", out _));
        Assert.Throws<ArgumentNullException>(() => cache.Get(null!));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    [Trait("Spec", "CRY-2"), Trait("Spec", "CRY-26")]
    public async Task Verify_ConcurrentCallersAcrossEvictions_AllGetCorrectResults()
    {
        // Two keys through a one-entry cache: every call evicts the other key, often while another thread verifies with it.
        using var first = SigningKey.Generate(KeyCurve.P256);
        using var second = SigningKey.Generate(KeyCurve.K256);
        var signers = new[] { (first.PublicKey.DidKey, first.Sign(Message)), (second.PublicKey.DidKey, second.Sign(Message)) };
        var cache = new VerificationKeyCache(capacity: 1);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() => Enumerable.Range(0, 50).All(i =>
        {
            var (key, signature) = signers[(worker + i) % 2];
            var (_, other) = signers[(worker + i + 1) % 2];
            return cache.Verify(key, Message, signature) && !cache.Verify(key, Message, other);
        }), TestContext.Current.CancellationToken)));

        Assert.All(results, Assert.True);
        Assert.Equal(1, cache.Count);
    }
}
