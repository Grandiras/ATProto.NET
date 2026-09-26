using ATProtoNet.Auth.OAuth;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.TestSupport;

/// <summary>
/// Behaviour every <see cref="IOAuthStateStore"/> implementation must have. A derived class
/// supplies <see cref="CreateStore"/>; keep anything implementation-specific (per-requester and
/// total capacity, encryption, key hashing) in that derived class instead.
/// </summary>
public abstract class OAuthStateStoreContractTests
{
    protected abstract IOAuthStateStore CreateStore();

    protected static OAuthPendingAuthorization Pending(string state, string? requester = null, DateTimeOffset? expiresAt = null) => new()
    {
        State = state,
        RequesterId = requester,
        Issuer = Issuer,
        TokenEndpoint = TokenEndpoint,
        RedirectUri = RedirectUri,
        CodeVerifier = "verifier",
        DPoPKey = NewDPoPKey(),
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10),
    };

    [Fact]
    public async Task TakeAsync_IsSingleUse()
    {
        var store = CreateStore();
        await store.SetAsync(Pending("s-1"));

        Assert.NotNull(await store.TakeAsync("s-1"));
        Assert.Null(await store.TakeAsync("s-1"));
    }

    [Fact]
    public async Task TakeAsync_UnknownState_ReturnsNull()
    {
        Assert.Null(await CreateStore().TakeAsync("nope"));
    }

    [Fact]
    public async Task SetAsync_TheSameStateTwice_KeepsTheLatest()
    {
        var store = CreateStore();
        await store.SetAsync(Pending("s-1", "a"));
        await store.SetAsync(Pending("s-1", "b") with { CodeVerifier = "second" });

        Assert.Equal("second", (await store.TakeAsync("s-1"))!.CodeVerifier);
    }
}
