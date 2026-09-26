using ATProtoNet.Admin;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Server;

namespace ATProtoNet.IntegrationTests;

/// <summary>
/// Shared client fixture that authenticates once per test run.
/// </summary>
/// <remarks>
/// By default this provisions its own throwaway account through the admin API, the same way
/// <see cref="SpaceNetworkFixture"/> does, and deletes it again on teardown — so the suite needs
/// nothing more than <c>ATPROTO_PDS_ADMIN_PASSWORD</c>. Setting <c>ATPROTO_TEST_HANDLE</c> and
/// <c>ATPROTO_TEST_PASSWORD</c> overrides this and signs in as that existing account instead,
/// for a PDS where provisioning is unavailable or a fixed account is preferred.
/// </remarks>
public class AuthenticatedClientFixture : IAsyncLifetime
{
    private const string ProvisionedPassword = "correct-horse-battery-staple";

    private PdsAdminClient? _admin;
    private Did? _provisionedDid;

    public AtProtoClient Client { get; private set; } = null!;

    /// <summary>The account's handle: <c>ATPROTO_TEST_HANDLE</c> if set, otherwise a provisioned one.</summary>
    public string Handle { get; private set; } = null!;

    /// <summary>The account's password: <c>ATPROTO_TEST_PASSWORD</c> if set, otherwise the provisioned one.</summary>
    public string Password { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        if (!string.IsNullOrEmpty(TestConfig.Handle) && !string.IsNullOrEmpty(TestConfig.Password))
        {
            Handle = TestConfig.Handle;
            Password = TestConfig.Password;
        }
        else
        {
            _admin = new PdsAdminClient(
                new PdsAdminOptions
                {
                    Url = TestConfig.PdsUrl,
                    AdminPassword = TestConfig.AdminPassword,
                    AllowInsecureHttp = true,
                },
                null,
                null);

            var server = await _admin.DescribeServerAsync();
            var domain = server.AvailableUserDomains[0];
            Handle = $"itest-{Guid.NewGuid():N}"[..16].TrimEnd('-') + domain;
            Password = ProvisionedPassword;

            var account = await _admin.CreateAccountAsync(new CreateAccountRequest
            {
                Handle = ATProtoNet.Identity.Handle.Parse(Handle),
                Email = $"{Guid.NewGuid():N}@example.com",
                Password = Password,
            });
            _provisionedDid = account.Did;
        }

        Client = new AtProtoClient(new AtProtoClientOptions { InstanceUrl = TestConfig.PdsUrl, AutoRefreshSession = false });
        await Client.LoginAsync(Handle, Password);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Client.LogoutAsync();
        }
        catch
        {
            // Ignore errors during cleanup
        }
        Client.Dispose();

        if (_provisionedDid is { } did && _admin is not null)
        {
            try
            {
                await _admin.DeleteAccountAsync(did);
            }
            catch
            {
                // Best-effort: a failed delete must not mask a test result.
            }

            _admin.Dispose();
        }
    }
}

/// <summary>
/// Collection definition so xUnit knows to share the fixture.
/// </summary>
[CollectionDefinition("Authenticated")]
public class AuthenticatedCollection : ICollectionFixture<AuthenticatedClientFixture>
{
}
