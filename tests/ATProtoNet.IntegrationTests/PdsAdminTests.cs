using ATProtoNet.Admin;

namespace ATProtoNet.IntegrationTests;

/// <summary>
/// Exercises <see cref="PdsAdminClient"/> against a live reference PDS
/// (<c>ghcr.io/bluesky-social/pds</c>).
/// </summary>
/// <remarks>
/// These are the tests that cover the seam unit tests cannot: that the wire format,
/// auth scheme, and account-provisioning flow match what the real server accepts.
/// Each test provisions the accounts it needs and deletes them again.
/// </remarks>
public class PdsAdminTests : IDisposable
{
    private readonly PdsAdminClient _admin;
    private readonly List<string> _createdDids = [];

    public PdsAdminTests()
    {
        _admin = new PdsAdminClient(
            new PdsAdminOptions
            {
                Url = TestConfig.PdsUrl,
                AdminPassword = TestConfig.AdminPassword,
                // The CI PDS is reached over the job network, not loopback.
                AllowInsecureHttp = true,
            },
            null,
            null);
    }

    [RequiresPdsAdminFact]
    public async Task DescribeServerAsync_ReportsHandleDomains()
    {
        var server = await _admin.DescribeServerAsync();

        Assert.NotEmpty(server.Did);
        Assert.NotEmpty(server.AvailableUserDomains);
    }

    [RequiresPdsAdminFact]
    public async Task CreateInviteCodeAsync_ReturnsAUsableCode()
    {
        var code = await _admin.CreateInviteCodeAsync();

        Assert.NotEmpty(code);

        // The reference PDS shapes codes after its own hostname.
        Assert.Contains('-', code);
    }

    [RequiresPdsAdminFact]
    public async Task CreateAccountAsync_ProvisionsAnAccountWithAResolvableDid()
    {
        var handle = await NextHandleAsync();

        var account = await _admin.CreateAccountAsync(new CreatePdsAccountRequest
        {
            Handle = handle,
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = "correct-horse-battery-staple",
        });

        Track(account.Did);

        Assert.StartsWith("did:", account.Did);
        Assert.Equal(handle, account.Handle);
        Assert.NotEmpty(account.AccessJwt);
        Assert.NotEmpty(account.RefreshJwt);
    }

    [RequiresPdsAdminFact]
    public async Task CreateAccountAsync_MintsAnInviteCodeWhenTheServerRequiresOne()
    {
        var server = await _admin.DescribeServerAsync();

        // The reference PDS requires invite codes by default; if this server does not,
        // the auto-minting path is not what is under test here.
        Assert.True(
            server.InviteCodeRequired == true,
            "expected the test PDS to require invite codes");

        // No InviteCode supplied — the client has to mint one with the admin credentials.
        var account = await _admin.CreateAccountAsync(new CreatePdsAccountRequest
        {
            Handle = await NextHandleAsync(),
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = "correct-horse-battery-staple",
        });

        Track(account.Did);

        Assert.StartsWith("did:", account.Did);
    }

    [RequiresPdsAdminFact]
    public async Task GetAccountAsync_ReturnsTheProvisionedAccount()
    {
        var handle = await NextHandleAsync();
        var email = $"{Guid.NewGuid():N}@example.com";

        var created = await _admin.CreateAccountAsync(new CreatePdsAccountRequest
        {
            Handle = handle,
            Email = email,
            Password = "correct-horse-battery-staple",
        });

        Track(created.Did);

        var account = await _admin.GetAccountAsync(created.Did);

        Assert.Equal(created.Did, account.Did);
        Assert.Equal(handle, account.Handle);
        Assert.Equal(email, account.Email);
    }

    [RequiresPdsAdminFact]
    public async Task UpdateAccountHandleAsync_ChangesTheHandle()
    {
        var created = await _admin.CreateAccountAsync(new CreatePdsAccountRequest
        {
            Handle = await NextHandleAsync(),
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = "correct-horse-battery-staple",
        });

        Track(created.Did);

        var newHandle = await NextHandleAsync();
        await _admin.UpdateAccountHandleAsync(created.Did, newHandle);

        var account = await _admin.GetAccountAsync(created.Did);
        Assert.Equal(newHandle, account.Handle);
    }

    [RequiresPdsAdminFact]
    public async Task TakedownAndRestoreAccountAsync_RoundTrip()
    {
        var created = await _admin.CreateAccountAsync(new CreatePdsAccountRequest
        {
            Handle = await NextHandleAsync(),
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = "correct-horse-battery-staple",
        });

        Track(created.Did);

        await _admin.TakedownAccountAsync(created.Did, reference: "integration-test");

        var status = await _admin.Admin.GetSubjectStatusAsync(did: created.Did);
        Assert.True(status.Takedown?.Applied);

        await _admin.RestoreAccountAsync(created.Did);

        status = await _admin.Admin.GetSubjectStatusAsync(did: created.Did);
        Assert.False(status.Takedown?.Applied ?? false);
    }

    [RequiresPdsAdminFact]
    public async Task NewAccount_CanSignInWithTheSessionItWasGiven()
    {
        const string password = "correct-horse-battery-staple";

        var created = await _admin.CreateAccountAsync(new CreatePdsAccountRequest
        {
            Handle = await NextHandleAsync(),
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = password,
        });

        Track(created.Did);

        // The point of the whole feature: the account the app provisioned is a real
        // account its owner can use.
        using var client = _admin.CreateClient();
        var session = await client.LoginAsync(created.Handle, password);

        Assert.Equal(created.Did, session.Did);
    }

    [RequiresPdsAdminFact]
    public async Task DeleteAccountAsync_RemovesTheAccount()
    {
        var created = await _admin.CreateAccountAsync(new CreatePdsAccountRequest
        {
            Handle = await NextHandleAsync(),
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = "correct-horse-battery-staple",
        });

        await _admin.DeleteAccountAsync(created.Did);

        await Assert.ThrowsAnyAsync<Exception>(() => _admin.GetAccountAsync(created.Did));
    }

    /// <summary>
    /// Builds a handle under a domain the server actually accepts — with
    /// <c>PDS_HOSTNAME=localhost</c> the reference PDS serves <c>.test</c>, not
    /// <c>.localhost</c>, so the domain has to be read from the server.
    /// </summary>
    private async Task<string> NextHandleAsync()
    {
        var server = await _admin.DescribeServerAsync();
        var domain = server.AvailableUserDomains[0];

        return $"u{Guid.NewGuid():N}"[..12] + domain;
    }

    private void Track(string did) => _createdDids.Add(did);

    public void Dispose()
    {
        foreach (var did in _createdDids)
        {
            try
            {
                _admin.DeleteAccountAsync(did).GetAwaiter().GetResult();
            }
            catch
            {
                // Best-effort cleanup; a failed delete must not mask a test result.
            }
        }

        _admin.Dispose();
        GC.SuppressFinalize(this);
    }
}
