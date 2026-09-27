using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Notification;
using ATProtoNet.Serialization;
using ATProtoNet.Tests.TestSupport;
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

    private readonly HttpStub _handler = new();
    private readonly AtProtoClient _client;

    public NotificationSurfaceTests() => _client = _handler.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    // unregisterPush is covered by ATProtoNet.Tests.Lexicon.EndpointRequestTests.

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

    // listActivitySubscriptions and putActivitySubscription's basic case are covered by
    // ATProtoNet.Tests.Lexicon.EndpointRequestTests.

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
