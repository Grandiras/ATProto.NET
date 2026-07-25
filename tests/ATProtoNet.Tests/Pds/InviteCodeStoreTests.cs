using ATProtoNet.Pds;

namespace ATProtoNet.Tests.Pds;

public class InviteCodeStoreTests
{
    private static PdsInviteCode Code(string code, int uses = 1, string? forAccount = null) => new()
    {
        Code = code,
        AvailableUses = uses,
        ForAccount = forAccount,
        CreatedBy = "admin",
    };

    [Fact]
    public async Task CreateAsync_DuplicateCode_Throws()
    {
        var store = new InMemoryInviteCodeStore();
        await store.CreateAsync(Code("abc"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(Code("abc")));
    }

    [Fact]
    public async Task GetAsync_UnknownCode_ReturnsNull()
    {
        var store = new InMemoryInviteCodeStore();
        Assert.Null(await store.GetAsync("nope"));
    }

    [Fact]
    public async Task TryClaimAsync_UnknownCode_ReturnsFalse()
    {
        var store = new InMemoryInviteCodeStore();
        Assert.False(await store.TryClaimAsync("nope"));
    }

    [Fact]
    public async Task TryClaimAsync_SingleUseCode_SucceedsOnceOnly()
    {
        var store = new InMemoryInviteCodeStore();
        await store.CreateAsync(Code("abc"));

        Assert.True(await store.TryClaimAsync("abc"));
        Assert.False(await store.TryClaimAsync("abc"));
    }

    [Fact]
    public async Task TryClaimAsync_MultiUseCode_SucceedsUpToAvailableUses()
    {
        var store = new InMemoryInviteCodeStore();
        await store.CreateAsync(Code("abc", uses: 3));

        Assert.True(await store.TryClaimAsync("abc"));
        Assert.True(await store.TryClaimAsync("abc"));
        Assert.True(await store.TryClaimAsync("abc"));
        Assert.False(await store.TryClaimAsync("abc"));
    }

    [Fact]
    public async Task TryClaimAsync_DisabledCode_ReturnsFalse()
    {
        var store = new InMemoryInviteCodeStore();
        await store.CreateAsync(Code("abc"));
        await store.DisableAsync(["abc"]);

        Assert.False(await store.TryClaimAsync("abc"));
    }

    [Fact]
    public async Task TryClaimAsync_ConcurrentClaims_NeverExceedsAvailableUses()
    {
        var store = new InMemoryInviteCodeStore();
        await store.CreateAsync(Code("abc", uses: 5));

        var claims = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => store.TryClaimAsync("abc"))));

        Assert.Equal(5, claims.Count(c => c));

        var stored = await store.GetAsync("abc");
        Assert.Equal(5, stored!.ClaimedUses);
    }

    [Fact]
    public async Task ReleaseClaimAsync_MakesUseAvailableAgain()
    {
        var store = new InMemoryInviteCodeStore();
        await store.CreateAsync(Code("abc"));

        Assert.True(await store.TryClaimAsync("abc"));
        await store.ReleaseClaimAsync("abc");

        Assert.True(await store.TryClaimAsync("abc"));
    }

    [Fact]
    public async Task ReleaseClaimAsync_WithoutClaim_DoesNotGoNegative()
    {
        var store = new InMemoryInviteCodeStore();
        await store.CreateAsync(Code("abc"));

        await store.ReleaseClaimAsync("abc");

        var stored = await store.GetAsync("abc");
        Assert.Equal(0, stored!.ClaimedUses);
        Assert.Equal(1, stored.RemainingUses);
    }

    [Fact]
    public async Task ConfirmClaimAsync_RecordsUse()
    {
        var store = new InMemoryInviteCodeStore();
        await store.CreateAsync(Code("abc"));

        await store.TryClaimAsync("abc");
        await store.ConfirmClaimAsync("abc", "did:plc:alice");

        var stored = await store.GetAsync("abc");
        var use = Assert.Single(stored!.Uses);
        Assert.Equal("did:plc:alice", use.UsedBy);
    }

    [Fact]
    public async Task GetAsync_ReturnsSnapshot_MutationDoesNotAffectStore()
    {
        var store = new InMemoryInviteCodeStore();
        await store.CreateAsync(Code("abc"));

        var snapshot = await store.GetAsync("abc");
        snapshot!.ClaimedUses = 99;
        snapshot.Disabled = true;

        Assert.True(await store.TryClaimAsync("abc"));
    }

    [Fact]
    public async Task DisableAsync_ReturnsCountOfNewlyDisabledCodes()
    {
        var store = new InMemoryInviteCodeStore();
        await store.CreateAsync(Code("a"));
        await store.CreateAsync(Code("b"));

        Assert.Equal(2, await store.DisableAsync(["a", "b", "missing"]));
        Assert.Equal(0, await store.DisableAsync(["a"]));
    }

    [Fact]
    public async Task DisableForAccountsAsync_DisablesOnlyMatchingAccounts()
    {
        var store = new InMemoryInviteCodeStore();
        await store.CreateAsync(Code("a", forAccount: "did:plc:alice"));
        await store.CreateAsync(Code("b", forAccount: "did:plc:bob"));
        await store.CreateAsync(Code("c"));

        Assert.Equal(1, await store.DisableForAccountsAsync(["did:plc:alice"]));

        Assert.True((await store.GetAsync("a"))!.Disabled);
        Assert.False((await store.GetAsync("b"))!.Disabled);
        Assert.False((await store.GetAsync("c"))!.Disabled);
    }

    [Fact]
    public async Task ListAsync_FiltersByAccount()
    {
        var store = new InMemoryInviteCodeStore();
        await store.CreateAsync(Code("a", forAccount: "did:plc:alice"));
        await store.CreateAsync(Code("b", forAccount: "did:plc:bob"));

        var page = await store.ListAsync(new InviteCodeQuery { ForAccount = "did:plc:alice" });

        Assert.Equal("a", Assert.Single(page.Codes).Code);
    }

    [Fact]
    public async Task ListAsync_Paginates()
    {
        var store = new InMemoryInviteCodeStore();
        for (var i = 0; i < 5; i++)
            await store.CreateAsync(Code($"code{i}"));

        var page1 = await store.ListAsync(new InviteCodeQuery { Limit = 3 });
        Assert.Equal(3, page1.Codes.Count);
        Assert.NotNull(page1.Cursor);

        var page2 = await store.ListAsync(new InviteCodeQuery { Limit = 3, Cursor = page1.Cursor });
        Assert.Equal(2, page2.Codes.Count);
        Assert.Null(page2.Cursor);

        var seen = page1.Codes.Concat(page2.Codes).Select(c => c.Code).ToList();
        Assert.Equal(5, seen.Distinct().Count());
    }

    [Fact]
    public async Task ListAsync_SortByUsage_PutsMostUsedFirst()
    {
        var store = new InMemoryInviteCodeStore();
        await store.CreateAsync(Code("quiet", uses: 5));
        await store.CreateAsync(Code("busy", uses: 5));

        for (var i = 0; i < 3; i++)
        {
            await store.TryClaimAsync("busy");
            await store.ConfirmClaimAsync("busy", $"did:plc:user{i}");
        }

        var page = await store.ListAsync(new InviteCodeQuery { Sort = InviteCodeSort.Usage });

        Assert.Equal("busy", page.Codes[0].Code);
        Assert.Equal("quiet", page.Codes[1].Code);
    }
}
