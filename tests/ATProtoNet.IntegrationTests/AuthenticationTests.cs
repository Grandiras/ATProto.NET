namespace ATProtoNet.IntegrationTests;

/// <summary>
/// Tests for authentication and session management against a real PDS.
/// </summary>
public class AuthenticationTests
{
    [RequiresPdsFact]
    public async Task Login_WithValidCredentials_Succeeds()
    {
        using var client = new AtProtoClientBuilder()
            .WithInstanceUrl(TestConfig.PdsUrl)
            .WithAutoRefreshSession(false)
            .Build();

        var session = await client.LoginAsync(TestConfig.Handle, TestConfig.Password);

        Assert.NotNull(session);
        Assert.True(client.IsAuthenticated);
        Assert.NotNull(client.Did);
        Assert.NotNull(client.Handle);
        Assert.NotEmpty(session.Did);
        Assert.NotEmpty(session.Handle);
        Assert.NotEmpty(session.AccessJwt);
        Assert.NotEmpty(session.RefreshJwt);
    }

    [RequiresPdsFact]
    public async Task Login_WithInvalidCredentials_Throws()
    {
        using var client = new AtProtoClientBuilder()
            .WithInstanceUrl(TestConfig.PdsUrl)
            .WithAutoRefreshSession(false)
            .Build();

        await Assert.ThrowsAsync<Http.AtProtoHttpException>(
            () => client.LoginAsync("invalid.handle", "wrong-password"));
    }

    [RequiresPdsFact]
    public async Task GetSession_AfterLogin_ReturnsSession()
    {
        using var client = new AtProtoClientBuilder()
            .WithInstanceUrl(TestConfig.PdsUrl)
            .WithAutoRefreshSession(false)
            .Build();

        await client.LoginAsync(TestConfig.Handle, TestConfig.Password);

        var sessionResponse = await client.Server.GetSessionAsync();

        Assert.NotEmpty(sessionResponse.Did);
        Assert.NotEmpty(sessionResponse.Handle);
    }

    [RequiresPdsFact]
    public async Task ResumeSession_WithValidTokens_Succeeds()
    {
        using var client1 = new AtProtoClientBuilder()
            .WithInstanceUrl(TestConfig.PdsUrl)
            .WithAutoRefreshSession(false)
            .Build();

        var session = await client1.LoginAsync(TestConfig.Handle, TestConfig.Password);

        // Create a new client and resume with saved session
        using var client2 = new AtProtoClientBuilder()
            .WithInstanceUrl(TestConfig.PdsUrl)
            .WithAutoRefreshSession(false)
            .Build();

        await client2.ResumeSessionAsync(session);

        Assert.True(client2.IsAuthenticated);
        Assert.Equal(session.Did, client2.Did);
    }

    [RequiresPdsFact]
    public async Task Logout_ClearsSession()
    {
        using var client = new AtProtoClientBuilder()
            .WithInstanceUrl(TestConfig.PdsUrl)
            .WithAutoRefreshSession(false)
            .Build();

        await client.LoginAsync(TestConfig.Handle, TestConfig.Password);
        Assert.True(client.IsAuthenticated);

        await client.LogoutAsync();
        Assert.False(client.IsAuthenticated);
        Assert.Null(client.Session);
    }
}
