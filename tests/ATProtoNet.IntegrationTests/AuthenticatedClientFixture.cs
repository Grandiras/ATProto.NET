namespace ATProtoNet.IntegrationTests;

/// <summary>
/// Shared client fixture that authenticates once per test run.
/// </summary>
public class AuthenticatedClientFixture : IAsyncLifetime
{
    public AtProtoClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Client = new AtProtoClientBuilder()
            .WithInstanceUrl(TestConfig.PdsUrl)
            .WithAutoRefreshSession(false)
            .Build();

        await Client.LoginAsync(TestConfig.Handle, TestConfig.Password);
    }

    public async Task DisposeAsync()
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
    }
}

/// <summary>
/// Collection definition so xUnit knows to share the fixture.
/// </summary>
[CollectionDefinition("Authenticated")]
public class AuthenticatedCollection : ICollectionFixture<AuthenticatedClientFixture>
{
}
