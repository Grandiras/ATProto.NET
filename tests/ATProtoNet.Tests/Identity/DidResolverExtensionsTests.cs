using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.TestKit;

namespace ATProtoNet.Tests.Identity;

public sealed class DidResolverExtensionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Did Signer = Did.Parse("did:plc:ssssssssssssssssssssssss");
    private static readonly DidKey K1 = DidKey.Parse(TestKeys.K256DidKey);
    private static readonly DidKey K2 = DidKey.Parse(TestKeys.P256DidKey);

    private readonly List<DidKey> _tried = [];
    private int _refreshes;
    private DidKey? _cached, _refreshed;

    private Task<KeyVerification> VerifyAsync(DidKey? valid, Func<DidDocument, DidKey?>? select = null) =>
        new Resolver(this).VerifyWithRefreshAsync(Signer, select ?? (d => d.GetSigningKey()), key =>
        {
            _tried.Add(key.DidKey);
            return key.DidKey == valid;
        }, Ct);

    [Fact]
    [Trait("Spec", "IDN-44")]
    public async Task VerifyWithRefreshAsync_ValidAgainstTheCachedKey_NeverRefreshes()
    {
        (_cached, _refreshed) = (K1, K2);

        Assert.Equal(new KeyVerification(true, K1), await VerifyAsync(K1));
        Assert.Equal(0, _refreshes);
    }

    [Fact]
    [Trait("Spec", "IDN-44")]
    public async Task VerifyWithRefreshAsync_RotatedKey_VerifiesAfterOneRefresh()
    {
        (_cached, _refreshed) = (K1, K2);

        Assert.Equal(new KeyVerification(true, K2), await VerifyAsync(K2));
        Assert.Equal([K1, K2], _tried);
        Assert.Equal(1, _refreshes);
    }

    [Fact]
    [Trait("Spec", "IDN-44")]
    public async Task VerifyWithRefreshAsync_ForgedSignature_RefreshesOnceAndDoesNotRetryAnUnchangedKey()
    {
        (_cached, _refreshed) = (K1, K1);

        Assert.Equal(new KeyVerification(false, K1), await VerifyAsync(null));
        Assert.Equal([K1], _tried);
        Assert.Equal(1, _refreshes);
    }

    [Theory]
    [Trait("Spec", "IDN-44")]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    public async Task VerifyWithRefreshAsync_MissingKey_IsLookedUpOnceMore(bool cached, bool refreshed, bool verified)
    {
        (_cached, _refreshed) = (cached ? K1 : null, refreshed ? K1 : null);

        Assert.Equal(new KeyVerification(verified, _refreshed), await VerifyAsync(_refreshed));
        Assert.Equal(1, _refreshes);
    }

    [Fact]
    [Trait("Spec", "IDN-44")]
    public async Task VerifyWithRefreshAsync_KeySelectorRefuses_PropagatesWithoutARefresh()
    {
        (_cached, _refreshed) = (K1, K2);

        await Assert.ThrowsAsync<FormatException>(() => VerifyAsync(K1, _ => throw new FormatException("malformed")));
        Assert.Equal(0, _refreshes);
    }

    [Fact]
    [Trait("Spec", "IDN-45")]
    public async Task VerifyWithRefreshAsync_LabelKeySelector_DoesNotFallBackToTheAccountKey()
    {
        (_cached, _refreshed) = (K1, K1);

        var result = await VerifyAsync(K1, d => d.TryGetVerificationKey(DidDocument.LabelKeyId).Value);

        Assert.Equal(new KeyVerification(false, null), result);
        Assert.Empty(_tried);
    }

    [Fact]
    [Trait("Spec", "IDN-44")]
    public async Task VerifyWithRefreshAsync_ResolutionFailure_Propagates()
    {
        var failing = new StubDidResolver(did => Task.FromException<DidDocument>(new IdentityResolutionException(IdentityResolutionError.NotFound, "gone", did)));

        await Assert.ThrowsAsync<IdentityResolutionException>(() => failing.VerifyWithRefreshAsync(Signer, d => d.GetSigningKey(), _ => true, Ct));
    }

    private sealed class Resolver(DidResolverExtensionsTests test) : IDidResolver
    {
        public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default) =>
            Task.FromResult(Docs.Parse(did.Value, signingKey: test._cached?.Value));

        public Task<DidDocument> RefreshAsync(Did did, CancellationToken cancellationToken = default)
        {
            test._refreshes++;
            return Task.FromResult(Docs.Parse(did.Value, signingKey: test._refreshed?.Value));
        }
    }
}
