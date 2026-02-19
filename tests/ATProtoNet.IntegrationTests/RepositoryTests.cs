namespace ATProtoNet.IntegrationTests;

/// <summary>
/// Tests for repository operations against a real PDS.
/// </summary>
[Collection("Authenticated")]
public class RepositoryTests
{
    private readonly AuthenticatedClientFixture _fixture;

    public RepositoryTests(AuthenticatedClientFixture fixture)
    {
        _fixture = fixture;
    }

    [RequiresPdsFact]
    public async Task CreateAndGetRecord_RoundTrips()
    {
        var client = _fixture.Client;

        // Create a test record
        var record = new Dictionary<string, object>
        {
            ["$type"] = "app.bsky.feed.post",
            ["text"] = $"Integration test post {Guid.NewGuid():N}",
            ["createdAt"] = DateTime.UtcNow.ToString("O"),
        };

        var createResult = await client.Repo.CreateRecordAsync(
            client.Did!, "app.bsky.feed.post", record);

        Assert.NotNull(createResult);
        Assert.NotEmpty(createResult.Uri);
        Assert.NotEmpty(createResult.Cid);

        // Extract record key from URI
        var uri = Identity.AtUri.Parse(createResult.Uri);
        var rkey = uri.RecordKey!;

        // Get the record back
        var getResult = await client.Repo.GetRecordAsync(
            client.Did!, "app.bsky.feed.post", rkey);

        Assert.NotNull(getResult);
        Assert.Equal(createResult.Uri, getResult.Uri);

        // Clean up - delete the record
        await client.Repo.DeleteRecordAsync(
            client.Did!, "app.bsky.feed.post", rkey);
    }

    [RequiresPdsFact]
    public async Task DescribeRepo_ReturnsRepoInfo()
    {
        var client = _fixture.Client;

        var result = await client.Repo.DescribeRepoAsync(client.Did!);

        Assert.NotNull(result);
        Assert.Equal(client.Did, result.Did);
        Assert.NotNull(result.Handle);
    }
}
