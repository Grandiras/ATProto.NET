using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>The contract every <see cref="ISpaceCredentialRevocationStore"/> must satisfy.</summary>
public abstract class SpaceCredentialRevocationStoreContractTests
{
    protected static readonly SpaceUri Space = SpaceUri.Parse("at://did:plc:bbbbbbbbbbbbbbbbbbbbbbbb/space/com.atmoboards.forum/default");
    protected static readonly SpaceUri OtherSpace = SpaceUri.Parse("at://did:plc:bbbbbbbbbbbbbbbbbbbbbbbb/space/com.atmoboards.forum/other");

    protected static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    protected abstract ISpaceCredentialRevocationStore CreateStore();

    [Fact]
    public async Task IsRevokedAsync_RevokedCredential_IsRevokedUntilItsRetentionPasses()
    {
        var store = CreateStore();
        await store.RevokeAsync(Space, ["a"], Now.AddMinutes(60));

        Assert.True(await store.IsRevokedAsync(Space, "a", Now));
        Assert.True(await store.IsRevokedAsync(Space, "a", Now.AddMinutes(60).AddMilliseconds(-1)));
        Assert.False(await store.IsRevokedAsync(Space, "a", Now.AddMinutes(60)));
    }

    [Fact]
    public async Task IsRevokedAsync_UnrelatedCredentialOrSpace_IsNotRevoked()
    {
        var store = CreateStore();
        await store.RevokeAsync(Space, ["a"], Now.AddMinutes(60));

        Assert.False(await store.IsRevokedAsync(Space, "b", Now));
        Assert.False(await store.IsRevokedAsync(OtherSpace, "a", Now));
    }

    [Fact]
    public async Task RevokeAsync_RepeatedAndDuplicatedIds_IsIdempotent()
    {
        var store = CreateStore();
        await store.RevokeAsync(Space, ["a", "a", "b"], Now.AddMinutes(60));
        await store.RevokeAsync(Space, ["a"], Now.AddMinutes(60));

        Assert.True(await store.IsRevokedAsync(Space, "a", Now));
        Assert.True(await store.IsRevokedAsync(Space, "b", Now));
    }

    [Fact]
    public async Task RevokeAsync_AgainWithALaterRetention_ExtendsIt()
    {
        var store = CreateStore();
        await store.RevokeAsync(Space, ["a"], Now.AddMinutes(10));
        await store.RevokeAsync(Space, ["a"], Now.AddMinutes(70));

        Assert.True(await store.IsRevokedAsync(Space, "a", Now.AddMinutes(60)));
    }

    [Fact]
    public async Task RevokeAsync_AgainWithAnEarlierRetention_NeverShortensIt()
    {
        var store = CreateStore();
        await store.RevokeAsync(Space, ["a"], Now.AddMinutes(70));
        await store.RevokeAsync(Space, ["a"], Now.AddMinutes(10));

        Assert.True(await store.IsRevokedAsync(Space, "a", Now.AddMinutes(60)));
    }

    [Fact]
    public async Task RevokeAsync_MoreIdsThanOneBatch_RecordsAll()
    {
        var store = CreateStore();
        var ids = Enumerable.Range(0, 250).Select(i => $"jti-{i}").ToList();
        await store.RevokeAsync(Space, ids, Now.AddMinutes(60));

        Assert.True(await store.IsRevokedAsync(Space, "jti-0", Now));
        Assert.True(await store.IsRevokedAsync(Space, "jti-249", Now));
        Assert.False(await store.IsRevokedAsync(Space, "jti-250", Now));
    }
}

public class InMemorySpaceCredentialRevocationStoreTests : SpaceCredentialRevocationStoreContractTests
{
    protected override ISpaceCredentialRevocationStore CreateStore() => new InMemorySpaceCredentialRevocationStore();

    [Fact]
    public async Task RevokeAsync_SweepsExpiredEntries_AndOnlyThose()
    {
        var clock = new FakeTimeProvider(Now);
        var store = new InMemorySpaceCredentialRevocationStore(clock);
        await store.RevokeAsync(Space, ["expired"], Now.AddMinutes(30));
        await store.RevokeAsync(Space, ["kept"], Now.AddMinutes(90));

        // Past the first entry's retention and the sweep interval, but not the second's.
        clock.Advance(TimeSpan.FromMinutes(31));
        await store.RevokeAsync(Space, ["new"], clock.GetUtcNow().AddMinutes(60));
        await store.LastSweep;

        Assert.Equal(2, store.Count);
        Assert.False(await store.IsRevokedAsync(Space, "expired", clock.GetUtcNow()));
        Assert.True(await store.IsRevokedAsync(Space, "kept", clock.GetUtcNow()));
        Assert.True(await store.IsRevokedAsync(Space, "new", clock.GetUtcNow()));
    }
}
