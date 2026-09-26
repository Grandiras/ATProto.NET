using ATProtoNet.Identity;
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
            ["createdAt"] = ATProtoNet.Serialization.AtProtoJsonDefaults.NowTimestamp(),
        };

        var createResult = await client.Repo.CreateRecordAsync(
            client.Did!, Nsid.Parse("app.bsky.feed.post"), record);

        Assert.NotNull(createResult);
        Assert.NotNull(createResult.Uri);
        Assert.NotNull(createResult.Cid);

        // Extract record key from URI
        var uri = Identity.AtUri.Parse(createResult.Uri);
        var rkey = uri.RecordKey!;

        // Get the record back
        var getResult = await client.Repo.GetRecordAsync(
            client.Did!, Nsid.Parse("app.bsky.feed.post"), rkey);

        Assert.NotNull(getResult);
        Assert.Equal(createResult.Uri, getResult.Uri);

        // Clean up - delete the record
        await client.Repo.DeleteRecordAsync(
            client.Did!, Nsid.Parse("app.bsky.feed.post"), rkey);
    }

    [RequiresPdsFact]
    public async Task GetVerifiedRecord_ProvesACreatedRecordAndThenItsDeletion()
    {
        var client = _fixture.Client;
        var collection = Nsid.Parse("app.bsky.feed.post");
        var text = $"Integration test proof {Guid.NewGuid():N}";

        var created = await client.Repo.CreateRecordAsync(client.Did!, collection, new Dictionary<string, object>
        {
            ["$type"] = "app.bsky.feed.post",
            ["text"] = text,
            ["createdAt"] = ATProtoNet.Serialization.AtProtoJsonDefaults.NowTimestamp(),
        });
        var rkey = created.Uri.RecordKey!;

        // The key the PDS signs this account's commits with.
        var credentials = await client.Identity.GetRecommendedDidCredentialsAsync();
        var signingKey = credentials.VerificationMethods!["atproto"];

        try
        {
            var proven = await client.Sync.GetVerifiedRecordAsync(client.Did!, collection, rkey, signingKey);

            Assert.True(proven.Exists);
            Assert.Equal(created.Cid, proven.Cid);
            Assert.Equal(text, proven.Value!.Value.GetProperty("text").GetString());
        }
        finally
        {
            await client.Repo.DeleteRecordAsync(client.Did!, collection, rkey);
        }

        var gone = await client.Sync.GetVerifiedRecordAsync(client.Did!, collection, rkey, signingKey);
        Assert.False(gone.Exists);
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
