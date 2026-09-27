using ATProtoNet.Identity;
using NSubstitute;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// The refetch-once rule every signature check shares (<see cref="DidResolverExtensions"/>). The
/// verifiers' own tests pin it through each caller; these pin it on the helper.
/// </summary>
public class DidResolverExtensionsTests
{
    private static readonly Did Signer = Did.Parse("did:plc:signer");

    private readonly IDidResolver _resolver = Substitute.For<IDidResolver>();
    private readonly List<string> _tried = [];

    private void Publish(string? cached, string? refreshed)
    {
        _resolver.ResolveAsync(Signer, Arg.Any<CancellationToken>()).Returns(Document(cached));
        _resolver.RefreshAsync(Signer, Arg.Any<CancellationToken>()).Returns(Document(refreshed));
    }

    private Task<(bool Verified, string? Key)> VerifyAsync(string? valid) =>
        _resolver.VerifyWithRefreshAsync(
            Signer,
            document => document.AlsoKnownAs.FirstOrDefault(),
            key =>
            {
                _tried.Add(key);
                return key == valid;
            },
            CancellationToken.None);

    [Fact]
    public async Task VerifyWithRefreshAsync_ValidAgainstTheCachedKey_NeverRefreshes()
    {
        Publish(cached: "k1", refreshed: "k2");

        Assert.Equal((true, "k1"), await VerifyAsync(valid: "k1"));
        await _resolver.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
    }

    [Fact]
    public async Task VerifyWithRefreshAsync_RotatedKey_VerifiesAfterOneRefresh()
    {
        Publish(cached: "old", refreshed: "new");

        Assert.Equal((true, "new"), await VerifyAsync(valid: "new"));
        Assert.Equal(["old", "new"], _tried);
        await _resolver.Received(1).RefreshAsync(Signer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyWithRefreshAsync_ForgedSignature_RefreshesOnceAndDoesNotRetryAnUnchangedKey()
    {
        Publish(cached: "k1", refreshed: "k1");

        Assert.Equal((false, "k1"), await VerifyAsync(valid: null));
        Assert.Equal(["k1"], _tried);
        await _resolver.Received(1).RefreshAsync(Signer, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "k1", true, "k1")]   // published since the document was cached
    [InlineData(null, null, false, null)]  // still none: nothing to verify against
    [InlineData("k1", null, false, null)]  // withdrawn: the refetched document is the one that counts
    public async Task VerifyWithRefreshAsync_MissingKey_IsLookedUpOnceMore(
        string? cached, string? refreshed, bool verified, string? key)
    {
        Publish(cached, refreshed);

        Assert.Equal((verified, key), await VerifyAsync(valid: refreshed));
        await _resolver.Received(1).RefreshAsync(Signer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyWithRefreshAsync_KeySelectorRefuses_PropagatesWithoutARefresh()
    {
        Publish(cached: "k1", refreshed: "k2");

        await Assert.ThrowsAsync<FormatException>(() => _resolver.VerifyWithRefreshAsync(
            Signer, _ => throw new FormatException("malformed"), _ => true, CancellationToken.None));
        await _resolver.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
    }

    [Fact]
    public async Task VerifyWithRefreshAsync_ResolutionFailure_Propagates()
    {
        _resolver.ResolveAsync(Signer, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DidDocument>(new DidResolutionException("gone", DidResolutionErrorKind.NotFound, Signer)));

        await Assert.ThrowsAsync<DidResolutionException>(() => VerifyAsync(valid: "k1"));
    }

    // The key rides in alsoKnownAs so the selector stays trivial; the helper never reads it.
    private static DidDocument Document(string? key) =>
        new() { Id = Signer, AlsoKnownAs = key is null ? [] : [key] };
}
