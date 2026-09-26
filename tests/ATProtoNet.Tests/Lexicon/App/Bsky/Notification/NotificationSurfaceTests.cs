using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Notification;
using ATProtoNet.Serialization;
using static ATProtoNet.Tests.Lexicon.App.Bsky.BskyFixtures;

namespace ATProtoNet.Tests.Lexicon.App.Bsky.Notification;

/// <summary>
/// unregisterPush, the V2 notification preferences, activity subscriptions and the
/// notification declaration record.
/// </summary>
public sealed class NotificationSurfaceTests : IDisposable
{
    private const string PreferencesJson = """
        {"preferences":{
          "chat":{"include":"all","push":true},
          "follow":{"include":"all","list":true,"push":true},
          "like":{"include":"follows","list":true,"push":false},
          "likeViaRepost":{"include":"all","list":true,"push":true},
          "mention":{"include":"all","list":true,"push":true},
          "quote":{"include":"all","list":true,"push":true},
          "reply":{"include":"all","list":true,"push":true},
          "repost":{"include":"all","list":true,"push":true},
          "repostViaRepost":{"include":"all","list":false,"push":false},
          "starterpackJoined":{"list":true,"push":true},
          "subscribedPost":{"list":true,"push":true},
          "unverified":{"list":true,"push":true},
          "verified":{"list":true,"push":false}
        }}
        """;

    private readonly ScriptedXrpcHandler _handler = new();
    private readonly AtProtoClient _client;

    public NotificationSurfaceTests() => _client = _handler.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task UnregisterPushAsync_PostsTheRegistration()
    {
        _handler.On("app.bsky.notification.unregisterPush", "{}");

        await _client.Bsky.Notification.UnregisterPushAsync(
            Did.Parse("did:web:api.bsky.app"), "device-token", PushPlatform.Ios, "xyz.blueskyweb.app");

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            """{"serviceDid":"did:web:api.bsky.app","token":"device-token","platform":"ios","appId":"xyz.blueskyweb.app"}""",
            request.BodyText);
    }

    [Fact]
    public async Task GetPreferencesAsync_BindsEveryPreference()
    {
        _handler.On("app.bsky.notification.getPreferences", PreferencesJson);

        var preferences = await _client.Bsky.Notification.GetPreferencesAsync();

        Assert.Equal(HttpMethod.Get, Assert.Single(_handler.Requests).Method);
        Assert.Equal(NotificationInclude.Follows, preferences.Like.Include);
        Assert.False(preferences.Like.Push);
        Assert.False(preferences.RepostViaRepost.List);
        Assert.True(preferences.StarterpackJoined.Push);
        Assert.False(preferences.Verified.Push);
        Assert.Null(preferences.ExtensionData);
    }

    [Fact]
    public async Task PutPreferencesV2Async_SendsOnlyTheChangedPreferences()
    {
        _handler.On("app.bsky.notification.putPreferencesV2", PreferencesJson);

        var preferences = await _client.Bsky.Notification.PutPreferencesV2Async(new PutPreferencesV2Request
        {
            Like = new FilterablePreference { Include = NotificationInclude.Follows, List = true, Push = false },
            Verified = new NotificationPreference { List = true, Push = false },
        });

        Assert.Equal(
            """{"like":{"include":"follows","list":true,"push":false},"verified":{"list":true,"push":false}}""",
            Assert.Single(_handler.Requests).BodyText);
        Assert.Equal(NotificationInclude.Follows, preferences.Like.Include);
    }

    [Fact]
    public async Task ListActivitySubscriptionsAsync_BindsProfiles()
    {
        _handler.On("app.bsky.notification.listActivitySubscriptions", $$"""{"cursor":"n","subscriptions":[{{BobProfileJson}}]}""");

        var page = await _client.Bsky.Notification.ListActivitySubscriptionsAsync(limit: 5);

        Assert.Equal("limit=5", Assert.Single(_handler.Requests).Query);
        Assert.Equal("n", page.Cursor);
        Assert.Equal(BobDid, Assert.Single(page.Subscriptions).Did.Value);
    }

    [Fact]
    public async Task EnumerateActivitySubscriptionsAsync_FollowsTheCursor()
    {
        _handler
            .On("app.bsky.notification.listActivitySubscriptions", $$"""{"cursor":"n","subscriptions":[{{BobProfileJson}}]}""")
            .On("app.bsky.notification.listActivitySubscriptions", $$"""{"subscriptions":[{{BobProfileJson}}]}""");

        var subscriptions = await _client.Bsky.Notification.EnumerateActivitySubscriptionsAsync().ToListAsync();

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal(["", "cursor=n"], _handler.Requests.Select(r => r.Query));
    }

    [Fact]
    public async Task PutActivitySubscriptionAsync_PostsSubjectAndSubscription()
    {
        _handler.On("app.bsky.notification.putActivitySubscription", $$$"""{"subject":"{{{BobDid}}}","activitySubscription":{"post":true,"reply":false}}""");

        var stored = await _client.Bsky.Notification.PutActivitySubscriptionAsync(Did.Parse(BobDid), post: true, reply: false);

        Assert.Equal(
            $$$"""{"subject":"{{{BobDid}}}","activitySubscription":{"post":true,"reply":false}}""",
            Assert.Single(_handler.Requests).BodyText);
        Assert.Equal(BobDid, stored.Subject.Value);
        Assert.True(stored.ActivitySubscription!.Post);
        Assert.False(stored.ActivitySubscription.Reply);
    }

    [Fact]
    public async Task PutActivitySubscriptionAsync_Removed_HasNoSubscription()
    {
        _handler.On("app.bsky.notification.putActivitySubscription", $$"""{"subject":"{{BobDid}}"}""");

        var stored = await _client.Bsky.Notification.PutActivitySubscriptionAsync(Did.Parse(BobDid), post: false, reply: false);

        Assert.Null(stored.ActivitySubscription);
    }

    [Fact]
    public void NotificationDeclarationRecord_RoundTripsWithItsType()
    {
        const string json = """{"$type":"app.bsky.notification.declaration","allowSubscriptions":"mutuals"}""";

        var record = JsonSerializer.Deserialize<NotificationDeclarationRecord>(json, AtProtoJsonDefaults.Options)!;

        Assert.Equal(AllowedSubscribers.Mutuals, record.AllowSubscriptions);
        Assert.Equal(json, JsonSerializer.Serialize(record, AtProtoJsonDefaults.Options));
    }
}
