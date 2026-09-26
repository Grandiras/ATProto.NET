using ATProtoNet.Auth;
using ATProtoNet.Identity;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.TestSupport;

/// <summary>
/// Behaviour every <see cref="IAtProtoSessionStore"/> implementation must have. A derived class
/// supplies <see cref="CreateStore"/> and runs these against one implementation; keep anything
/// implementation-specific (encryption at rest, schema, cross-restart persistence, reference
/// identity) in that derived class instead.
/// </summary>
public abstract class SessionStoreContractTests
{
    /// <summary>Creates a fresh, empty store.</summary>
    protected abstract IAtProtoSessionStore CreateStore();

    private static OAuthSession TestSession(string did = "did:plc:alice") =>
        OAuthSession([10, 20, 30, 40, 50], accessToken: "access-token-value", refreshToken: "refresh-token-value")
            with { Did = Did.Parse(did) };

    [Fact]
    public async Task SetAsync_And_GetAsync_RoundTrips()
    {
        var store = CreateStore();
        var session = TestSession();
        await store.SetAsync(session);

        var retrieved = Assert.IsType<OAuthSession>(await store.GetAsync(Alice));

        Assert.Equal(session with { DPoPKey = retrieved.DPoPKey }, retrieved);
        Assert.Equal(new byte[] { 10, 20, 30, 40, 50 }, retrieved.DPoPKey.ToArray());
    }

    [Fact]
    public async Task APasswordSession_RoundTrips()
    {
        var store = CreateStore();
        var session = PasswordSession("access", "refresh");
        await store.SetAsync(session);

        Assert.Equal(session, await store.GetAsync(Alice));
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNotFound()
    {
        Assert.Null(await CreateStore().GetAsync(Did.Parse("did:plc:nonexistent")));
    }

    [Fact]
    public async Task SetAsync_ReplacesTheStoredSession()
    {
        var store = CreateStore();
        await store.SetAsync(TestSession());
        await store.SetAsync(TestSession() with { AccessToken = "new-access-token" });

        var retrieved = Assert.IsType<OAuthSession>(await store.GetAsync(Alice));
        Assert.Equal("new-access-token", retrieved.AccessToken);
    }

    [Fact]
    public async Task Accounts_AreIsolated()
    {
        var store = CreateStore();
        await store.SetAsync(TestSession("did:plc:alice") with { AccessToken = "alice" });
        await store.SetAsync(TestSession("did:plc:bob") with { AccessToken = "bob" });

        Assert.Equal("alice", Assert.IsType<OAuthSession>(await store.GetAsync(Did.Parse("did:plc:alice"))).AccessToken);
        Assert.Equal("bob", Assert.IsType<OAuthSession>(await store.GetAsync(Did.Parse("did:plc:bob"))).AccessToken);
    }

    [Fact]
    public async Task RemoveAsync_OnlyDropsThatAccount()
    {
        var store = CreateStore();
        await store.SetAsync(TestSession("did:plc:alice") with { AccessToken = "alice" });
        await store.SetAsync(TestSession("did:plc:bob") with { AccessToken = "bob" });

        await store.RemoveAsync(Alice);
        await store.RemoveAsync(Did.Parse("did:plc:nonexistent"));

        Assert.Null(await store.GetAsync(Alice));
        Assert.Equal("bob", Assert.IsType<OAuthSession>(await store.GetAsync(Did.Parse("did:plc:bob"))).AccessToken);
    }

    [Fact]
    public async Task SetAsync_ThrowsOnNullSession()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await CreateStore().SetAsync(null!));
    }

    [Fact]
    public async Task GetAsync_ThrowsOnNullDid()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await CreateStore().GetAsync(null!));
    }

    [Fact]
    public async Task RemoveAsync_ThrowsOnNullDid()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await CreateStore().RemoveAsync(null!));
    }
}
