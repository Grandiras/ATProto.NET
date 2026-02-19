namespace ATProtoNet.IntegrationTests;

/// <summary>
/// Tests for Bluesky social features against a real PDS.
/// </summary>
[Collection("Authenticated")]
public class BlueskyFeatureTests
{
    private readonly AuthenticatedClientFixture _fixture;

    public BlueskyFeatureTests(AuthenticatedClientFixture fixture)
    {
        _fixture = fixture;
    }

    [RequiresPdsFact]
    public async Task CreatePost_PublishesText()
    {
        var client = _fixture.Client;

        var postRef = await client.PostAsync($"Hello from ATProtoNet integration test! {Guid.NewGuid():N}");

        Assert.NotNull(postRef);
        Assert.NotEmpty(postRef.Uri);
        Assert.NotEmpty(postRef.Cid);

        // Clean up
        var uri = Identity.AtUri.Parse(postRef.Uri);
        await client.Repo.DeleteRecordAsync(client.Did!, "app.bsky.feed.post", uri.RecordKey!);
    }

    [RequiresBlueskyFact]
    public async Task GetProfile_ReturnsSelfProfile()
    {
        var client = _fixture.Client;

        var profile = await client.Bsky.Actor.GetProfileAsync(client.Handle!);

        Assert.NotNull(profile);
        Assert.Equal(client.Did, profile.Did);
        Assert.NotNull(profile.Handle);
    }

    [RequiresBlueskyFact]
    public async Task GetTimeline_ReturnsResults()
    {
        var client = _fixture.Client;

        var timeline = await client.Bsky.Feed.GetTimelineAsync(limit: 5);

        Assert.NotNull(timeline);
        // Timeline might be empty for a test account, but it should not throw
    }

    [RequiresBlueskyFact]
    public async Task GetNotificationCount_ReturnsResult()
    {
        var client = _fixture.Client;

        var count = await client.Bsky.Notification.GetUnreadCountAsync();

        Assert.NotNull(count);
        Assert.True(count.Count >= 0);
    }

    [RequiresPdsFact]
    public async Task CreatePostWithRichText_PublishesWithFacets()
    {
        var client = _fixture.Client;

        var (text, facets) = new Lexicon.App.Bsky.RichText.RichTextBuilder()
            .Text("Testing ")
            .Tag("atproto")
            .Text(" SDK ")
            .Link("link", "https://example.com")
            .Build();

        var postRef = await client.PostAsync(text, facets: facets);

        Assert.NotNull(postRef);
        Assert.NotEmpty(postRef.Uri);

        // Clean up
        var uri = Identity.AtUri.Parse(postRef.Uri);
        await client.Repo.DeleteRecordAsync(client.Did!, "app.bsky.feed.post", uri.RecordKey!);
    }
}
