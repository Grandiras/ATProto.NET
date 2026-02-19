namespace ATProtoNet.IntegrationTests;

/// <summary>
/// Tests for identity resolution against a real PDS.
/// </summary>
[Collection("Authenticated")]
public class IdentityResolutionTests
{
    private readonly AuthenticatedClientFixture _fixture;

    public IdentityResolutionTests(AuthenticatedClientFixture fixture)
    {
        _fixture = fixture;
    }

    [RequiresPdsFact]
    public async Task ResolveHandle_ReturnsDidForKnownHandle()
    {
        var client = _fixture.Client;

        var result = await client.Identity.ResolveHandleAsync(client.Handle!);

        Assert.NotNull(result);
        Assert.Equal(client.Did, result.Did);
    }
}
