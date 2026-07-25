using ATProtoNet.Pds;

namespace ATProtoNet.Tests.Pds;

public class PdsInviteCodeTests
{
    private static (PdsService Pds, IInviteCodeStore Invites) CreateClosedPds(IAccountStore? accounts = null)
    {
        var options = new PdsOptions { Hostname = "test.local", OpenRegistration = false };
        var invites = new InMemoryInviteCodeStore();
        var pds = new PdsService(
            accounts ?? new InMemoryAccountStore(), new InMemoryRepoStore(),
            new PdsSessionService(options), options, invites);
        return (pds, invites);
    }

    // ── Redemption ──

    [Fact]
    public async Task CreateAccountAsync_ClosedRegistration_ArbitraryCodeIsRejected()
    {
        var (pds, _) = CreateClosedPds();

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123",
                inviteCode: "definitely-not-a-real-code"));
        Assert.Equal("InvalidInviteCode", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAccountAsync_ClosedRegistration_WhitespaceCodeIsRejected()
    {
        var (pds, _) = CreateClosedPds();

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.CreateAccountAsync("alice.test.local", null, "password123", inviteCode: "   "));
        Assert.Equal("InvalidInviteCode", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAccountAsync_ClosedRegistration_IssuedCodeIsAccepted()
    {
        var (pds, _) = CreateClosedPds();
        var code = await pds.CreateInviteCodeAsync();

        var result = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123",
            inviteCode: code);

        Assert.StartsWith("did:plc:", result.Did);
    }

    [Fact]
    public async Task CreateAccountAsync_SingleUseCode_CannotBeRedeemedTwice()
    {
        var (pds, _) = CreateClosedPds();
        var code = await pds.CreateInviteCodeAsync();

        await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123", inviteCode: code);

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.CreateAccountAsync("bob.test.local", "bob@test.local", "password123", inviteCode: code));
        Assert.Equal("InvalidInviteCode", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAccountAsync_MultiUseCode_RedeemsExactlyUseCountTimes()
    {
        var (pds, _) = CreateClosedPds();
        var code = await pds.CreateInviteCodeAsync(useCount: 2);

        await pds.CreateAccountAsync("alice.test.local", null, "password123", inviteCode: code);
        await pds.CreateAccountAsync("bob.test.local", null, "password123", inviteCode: code);

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.CreateAccountAsync("carol.test.local", null, "password123", inviteCode: code));
        Assert.Equal("InvalidInviteCode", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAccountAsync_DisabledCode_IsRejected()
    {
        var (pds, _) = CreateClosedPds();
        var code = await pds.CreateInviteCodeAsync();
        await pds.DisableInviteCodesAsync([code], accounts: null);

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.CreateAccountAsync("alice.test.local", null, "password123", inviteCode: code));
        Assert.Equal("InvalidInviteCode", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAccountAsync_RecordsRedeemingDidOnTheCode()
    {
        var (pds, invites) = CreateClosedPds();
        var code = await pds.CreateInviteCodeAsync();

        var result = await pds.CreateAccountAsync("alice.test.local", null, "password123", inviteCode: code);

        var stored = await invites.GetAsync(code);
        var use = Assert.Single(stored!.Uses);
        Assert.Equal(result.Did, use.UsedBy);
    }

    [Fact]
    public async Task CreateAccountAsync_HandleTaken_ReleasesTheClaimedCode()
    {
        var (pds, invites) = CreateClosedPds();
        var first = await pds.CreateInviteCodeAsync();
        var second = await pds.CreateInviteCodeAsync();

        await pds.CreateAccountAsync("alice.test.local", null, "password123", inviteCode: first);

        // Same handle, different code — creation fails, and the second code must survive it.
        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.CreateAccountAsync("alice.test.local", null, "password123", inviteCode: second));
        Assert.Equal("HandleNotAvailable", ex.ErrorCode);

        var stored = await invites.GetAsync(second);
        Assert.Equal(0, stored!.ClaimedUses);
        Assert.Empty(stored.Uses);

        // Still redeemable.
        await pds.CreateAccountAsync("bob.test.local", null, "password123", inviteCode: second);
    }

    [Fact]
    public async Task CreateAccountAsync_ConcurrentRedemptions_DoNotDoubleSpendACode()
    {
        var (pds, _) = CreateClosedPds();
        var code = await pds.CreateInviteCodeAsync(useCount: 3);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            try
            {
                await pds.CreateAccountAsync($"user{i}.test.local", null, "password123", inviteCode: code);
                return true;
            }
            catch (PdsException)
            {
                return false;
            }
        }));

        Assert.Equal(3, attempts.Count(a => a));
    }

    [Fact]
    public async Task CreateAccountAsync_OpenRegistration_IgnoresInviteCodes()
    {
        var options = new PdsOptions { Hostname = "test.local", OpenRegistration = true };
        var pds = new PdsService(new InMemoryAccountStore(), new InMemoryRepoStore(),
            new PdsSessionService(options), options, new InMemoryInviteCodeStore());

        var result = await pds.CreateAccountAsync("alice.test.local", null, "password123",
            inviteCode: "not-a-real-code");

        Assert.StartsWith("did:plc:", result.Did);
    }

    [Fact]
    public async Task CreateAccountAsync_ClosedRegistration_WithoutStore_FailsClosed()
    {
        // No invite store supplied at all: the service must still reject arbitrary codes
        // rather than falling back to a presence check.
        var options = new PdsOptions { Hostname = "test.local", OpenRegistration = false };
        var pds = new PdsService(new InMemoryAccountStore(), new InMemoryRepoStore(),
            new PdsSessionService(options), options);

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.CreateAccountAsync("alice.test.local", null, "password123", inviteCode: "anything"));
        Assert.Equal("InvalidInviteCode", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAccountAsync_FederatingPds_RedeemsTheCodeAgainstTheMintedDid()
    {
        // The federating constructor mints the DID through PdsIdentityService rather than
        // generating a placeholder, so the redemption has to be recorded against that DID.
        var options = new PdsOptions
        {
            Hostname = "test.local",
            OpenRegistration = false,
            DidMethod = PdsDidMethod.Web,   // no PLC directory call
        };
        var accounts = new InMemoryAccountStore();
        var repos = new InMemoryRepoStore();
        var invites = new InMemoryInviteCodeStore();
        var sequencer = new PdsSequencer(64);
        var manager = new PdsRepoManager(
            accounts, repos, new InMemoryRepoCommitStore(), sequencer, options);
        var pds = new PdsService(
            accounts, repos, new PdsSessionService(options), options,
            manager, new PdsIdentityService(options, accounts), invites);

        var code = await pds.CreateInviteCodeAsync();
        var result = await pds.CreateAccountAsync(
            "alice.test.local", null, "password123", inviteCode: code);

        var stored = await invites.GetAsync(code);
        Assert.NotNull(stored);
        Assert.Equal(0, stored!.RemainingUses);
        Assert.Equal(result.Did, Assert.Single(stored.Uses).UsedBy);
    }

    // ── Issuing ──

    [Fact]
    public async Task CreateInviteCodeAsync_GeneratesHostPrefixedCode()
    {
        var (pds, _) = CreateClosedPds();

        var code = await pds.CreateInviteCodeAsync();

        Assert.StartsWith("test-local-", code);
        Assert.Equal(3, code.Split('-').Length - 1); // test-local-xxxxx-xxxxx
    }

    [Fact]
    public async Task CreateInviteCodeAsync_GeneratesDistinctCodes()
    {
        var (pds, _) = CreateClosedPds();

        var codes = new HashSet<string>();
        for (var i = 0; i < 200; i++)
            codes.Add(await pds.CreateInviteCodeAsync());

        Assert.Equal(200, codes.Count);
    }

    [Fact]
    public async Task CreateInviteCodeAsync_ZeroUseCount_Throws()
    {
        var (pds, _) = CreateClosedPds();

        var ex = await Assert.ThrowsAsync<PdsException>(() => pds.CreateInviteCodeAsync(useCount: 0));
        Assert.Equal("InvalidRequest", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateInviteCodesAsync_ZeroUseCount_ThrowsWithoutIssuingAnyCode()
    {
        var (pds, invites) = CreateClosedPds();

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.CreateInviteCodesAsync(codeCount: 2, useCount: 0,
                forAccounts: ["did:plc:alice", "did:plc:bob"]));

        Assert.Equal("InvalidRequest", ex.ErrorCode);

        // Validated before the first write, so no account ends up holding a partial batch.
        var page = await invites.ListAsync(new InviteCodeQuery { Limit = int.MaxValue });
        Assert.Empty(page.Codes);
    }

    [Fact]
    public async Task CreateInviteCodesAsync_WithoutAccounts_IssuesOneAdminBatch()
    {
        var (pds, _) = CreateClosedPds();

        var batches = await pds.CreateInviteCodesAsync(codeCount: 3, useCount: 1);

        var batch = Assert.Single(batches);
        Assert.Equal(3, batch.Codes.Count);
        Assert.Equal("admin", batch.Account);
    }

    [Fact]
    public async Task CreateInviteCodesAsync_WithAccounts_IssuesOneBatchPerAccount()
    {
        var (pds, _) = CreateClosedPds();

        var batches = await pds.CreateInviteCodesAsync(codeCount: 2, useCount: 1,
            forAccounts: ["did:plc:alice", "did:plc:bob"]);

        Assert.Equal(2, batches.Count);
        Assert.All(batches, b => Assert.Equal(2, b.Codes.Count));
        Assert.Equal(["did:plc:alice", "did:plc:bob"], batches.Select(b => b.Account));
    }

    [Fact]
    public async Task GetAccountInviteCodesAsync_ReturnsOnlyThatAccountsCodes()
    {
        var (pds, _) = CreateClosedPds();
        await pds.CreateInviteCodesAsync(codeCount: 2, useCount: 1, forAccounts: ["did:plc:alice"]);
        await pds.CreateInviteCodesAsync(codeCount: 1, useCount: 1, forAccounts: ["did:plc:bob"]);

        var codes = await pds.GetAccountInviteCodesAsync("did:plc:alice");

        Assert.Equal(2, codes.Count);
        Assert.All(codes, c => Assert.Equal("did:plc:alice", c.ForAccount));
    }

    [Fact]
    public async Task GetAccountInviteCodesAsync_ExcludeUsed_DropsExhaustedCodes()
    {
        var (pds, _) = CreateClosedPds();
        var batch = await pds.CreateInviteCodesAsync(codeCount: 2, useCount: 1, forAccounts: ["did:plc:alice"]);
        await pds.CreateAccountAsync("bob.test.local", null, "password123", inviteCode: batch[0].Codes[0]);

        var all = await pds.GetAccountInviteCodesAsync("did:plc:alice");
        var unused = await pds.GetAccountInviteCodesAsync("did:plc:alice", includeUsed: false);

        Assert.Equal(2, all.Count);
        Assert.Equal(batch[0].Codes[1], Assert.Single(unused).Code);
    }

    [Fact]
    public async Task DisableInviteCodesAsync_ByAccount_DisablesEveryCodeForThatAccount()
    {
        var (pds, _) = CreateClosedPds();
        var alice = await pds.CreateInviteCodesAsync(codeCount: 2, useCount: 1, forAccounts: ["did:plc:alice"]);
        var adminCode = await pds.CreateInviteCodeAsync();

        var disabled = await pds.DisableInviteCodesAsync(codes: null, accounts: ["did:plc:alice"]);

        Assert.Equal(2, disabled);
        Assert.All(await pds.GetAccountInviteCodesAsync("did:plc:alice"), c => Assert.True(c.Disabled));

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.CreateAccountAsync("bob.test.local", null, "password123", inviteCode: alice[0].Codes[0]));
        Assert.Equal("InvalidInviteCode", ex.ErrorCode);

        // Admin-issued codes are untouched.
        await pds.CreateAccountAsync("carol.test.local", null, "password123", inviteCode: adminCode);
    }

    [Fact]
    public async Task GetInviteCodesAsync_ReturnsAllCodesWithPaging()
    {
        var (pds, _) = CreateClosedPds();
        await pds.CreateInviteCodesAsync(codeCount: 5, useCount: 1);

        var page1 = await pds.GetInviteCodesAsync(new InviteCodeQuery { Limit = 2 });
        Assert.Equal(2, page1.Codes.Count);
        Assert.NotNull(page1.Cursor);

        var page2 = await pds.GetInviteCodesAsync(new InviteCodeQuery { Limit = 10, Cursor = page1.Cursor });
        Assert.Equal(3, page2.Codes.Count);
        Assert.Null(page2.Cursor);
    }

    // ── Wire projection ──

    [Fact]
    public void PdsInviteCodeView_FromCode_MapsAvailableToTotalUseCount()
    {
        var code = new PdsInviteCode
        {
            Code = "test-local-abcde-fghij",
            AvailableUses = 5,
            ClaimedUses = 2,
            CreatedBy = "admin",
            Uses = [new PdsInviteCodeUse { UsedBy = "did:plc:alice" }],
        };

        var view = PdsInviteCodeView.FromCode(code);

        Assert.Equal(5, view.Available);
        Assert.Equal("admin", view.ForAccount);
        Assert.Equal("did:plc:alice", Assert.Single(view.Uses).UsedBy);
    }
}
