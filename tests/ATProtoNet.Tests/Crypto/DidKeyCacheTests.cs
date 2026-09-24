using ATProtoNet.Crypto;

namespace ATProtoNet.Tests.Crypto;

/// <summary>Tests for the parsed-key cache behind <see cref="AtProtoCrypto.VerifySignature"/>.</summary>
public sealed class DidKeyCacheTests
{
    private static readonly byte[] s_message = "cached verification"u8.ToArray();

    [Fact]
    public void Verify_ValidAndTamperedSignatures_MatchTheKeysOwnVerify()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var signature = key.Sign(s_message);
        var tampered = (byte[])signature.Clone();
        tampered[10] ^= 0x01;
        var cache = new DidKeyCache(capacity: 4, maxIdleKeysPerEntry: 2);

        Assert.True(cache.Verify(key.ToDidKey(), s_message, signature));
        Assert.False(cache.Verify(key.ToDidKey(), s_message, tampered));
        Assert.False(cache.Verify(key.ToDidKey(), "another message"u8, signature));
        Assert.True(cache.Verify(key.ToDidKey(), s_message, signature));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Verify_MoreKeysThanCapacity_EvictsTheLeastRecentlyUsed()
    {
        var keys = Enumerable.Range(0, 3).Select(_ => AtProtoCrypto.GenerateP256Key()).ToArray();
        try
        {
            var cache = new DidKeyCache(capacity: 2, maxIdleKeysPerEntry: 1);
            var signatures = keys.Select(k => k.Sign(s_message)).ToArray();

            for (var round = 0; round < 3; round++)
            {
                for (var i = 0; i < keys.Length; i++)
                    Assert.True(cache.Verify(keys[i].ToDidKey(), s_message, signatures[i]));
            }

            Assert.Equal(2, cache.Count);
        }
        finally
        {
            foreach (var key in keys)
                key.Dispose();
        }
    }

    [Theory]
    [InlineData("did:web:example.com")]
    [InlineData("did:key:zInvalid0Base58")]
    [InlineData("did:key:z6MkhaXgBZDvotDkL5257faiztiGiC2QtKLGpbnnEGta2doK")] // Ed25519: not an atproto curve
    public void Verify_MalformedOrUnsupportedDidKey_ThrowsAndCachesNothing(string didKey)
    {
        var cache = new DidKeyCache(capacity: 4, maxIdleKeysPerEntry: 1);

        Assert.Throws<FormatException>(() => cache.Verify(didKey, s_message, new byte[64]));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task Verify_ConcurrentCallersAcrossEvictions_AllGetCorrectResults()
    {
        // Two keys through a one-entry cache: every call evicts the other key's entry, often
        // while another thread is still verifying with a key rented from it.
        using var first = AtProtoCrypto.GenerateP256Key();
        using var second = AtProtoCrypto.GenerateP256Key();
        var signers = new[]
        {
            (DidKey: first.ToDidKey(), Signature: first.Sign(s_message)),
            (DidKey: second.ToDidKey(), Signature: second.Sign(s_message)),
        };
        var cache = new DidKeyCache(capacity: 1, maxIdleKeysPerEntry: 2);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                var (didKey, signature) = signers[(worker + i) % 2];
                var (_, otherSignature) = signers[(worker + i + 1) % 2];
                if (!cache.Verify(didKey, s_message, signature) || cache.Verify(didKey, s_message, otherSignature))
                    return false;
            }
            return true;
        })));

        Assert.All(results, Assert.True);
        Assert.Equal(1, cache.Count);
    }
}
