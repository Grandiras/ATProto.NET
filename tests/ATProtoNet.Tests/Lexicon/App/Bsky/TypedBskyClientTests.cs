using System.Reflection;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Lexicon.App.Bsky.Notification;
using ATProtoNet.Models;
using ATProtoNet.Serialization;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Lexicon.App.Bsky;

/// <summary>
/// The typed app.bsky surface: identifiers and datetimes go out as their text and come back
/// parsed, parameters keep their Lexicon names, and the enumerators walk every page.
/// </summary>
public class TypedBskyClientTests : IDisposable
{
    private const string DidText = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string Cid1 = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";
    private const string PostUri = $"at://{DidText}/app.bsky.feed.post/3k2la";

    private static readonly Did Alice = Did.Parse(DidText);

    private static readonly string PostJson =
        $$$"""{"uri":"{{{PostUri}}}","cid":"{{{Cid1}}}","author":{"did":"{{{DidText}}}","handle":"alice.test"},"record":{},"indexedAt":"2024-01-01T00:00:00Z","viewer":{"like":"at://{{{DidText}}}/app.bsky.feed.like/3k2lb"}}""";

    private static readonly string ProfileJson = $$"""{"did":"{{DidText}}","handle":"alice.test"}""";

    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly AtProtoClient _client;

    public TypedBskyClientTests()
    {
        _stub.Fallback("{}");
        _httpClient = new HttpClient(_stub);
        _client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
            _httpClient, null, null);
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _stub.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetAuthorFeedAsync_SendsTheTypedActorAndFilters_ParsesTypedPosts()
    {
        _stub.Fallback($$"""{"feed":[{"post":{{PostJson}}}]}""");

        var page = await _client.Bsky.Feed.GetAuthorFeedAsync(
            Alice, filter: "posts_no_replies", includePins: false, limit: 5, cursor: "c");

        Assert.Equal(
            $"actor={DidText}&filter=posts_no_replies&includePins=false&limit=5&cursor=c",
            Query(Assert.Single(_stub.Requests)));

        var post = Assert.Single(page.Feed).Post;
        Assert.Equal(RecordKey.Parse("3k2la"), post.Uri.RecordKey);
        Assert.Equal(Cid1, post.Cid.Value);
        Assert.Equal(Alice, post.Author.Did);
        Assert.Equal(Handle.Parse("alice.test"), post.Author.Handle);
        Assert.Equal("2024-01-01T00:00:00Z", post.IndexedAt.ToString());
        Assert.Equal("app.bsky.feed.like", post.Viewer!.Like!.Collection);
    }

    [Fact]
    public async Task SearchPostsAsync_TypedAuthorAndMentions_GoOutAsTheirText()
    {
        _stub.Fallback("""{"posts":[]}""");

        await _client.Bsky.Feed.SearchPostsAsync(
            "hello", since: "2024-01-01", mentions: Handle.Parse("bob.test"), author: Alice);

        var query = Query(Assert.Single(_stub.Requests));
        Assert.Contains("since=2024-01-01", query);
        Assert.Contains("mentions=bob.test", query);
        Assert.Contains($"author={DidText}", query);
    }

    [Fact]
    public async Task GetPostsAsync_SendsOneUrisKeyPerPost()
    {
        _stub.Fallback($$"""{"posts":[{{PostJson}}]}""");

        var posts = await _client.Bsky.Feed.GetPostsAsync(
            [AtUri.Parse(PostUri), AtUri.Parse($"at://{DidText}/app.bsky.feed.post/3k2lc")]);

        Assert.Equal(
            $"uris={PostUri}&uris=at://{DidText}/app.bsky.feed.post/3k2lc",
            Query(Assert.Single(_stub.Requests)));
        Assert.Equal(AtUri.Parse(PostUri), Assert.Single(posts.Posts).Uri);
    }

    [Fact]
    public async Task ListNotificationsAsync_SendsReasons_AndParsesTypedNotifications()
    {
        _stub.Fallback(
            $$"""{"notifications":[{"uri":"at://{{DidText}}/app.bsky.feed.like/3k2lb","cid":"{{Cid1}}","author":{{ProfileJson}},"reason":"like","reasonSubject":"{{PostUri}}","record":{},"isRead":false,"indexedAt":"2024-05-01T12:00:00.000Z"}],"seenAt":"2024-05-01T12:00:00+02:00"}""");

        var page = await _client.Bsky.Notification.ListNotificationsAsync(
            reasons: [NotificationReasons.Like, NotificationReasons.LikeViaRepost], limit: 10);

        // seenAt is deliberately not a parameter: upstream answers it with an error since 2026-09-21.
        Assert.Equal("reasons=like&reasons=like-via-repost&limit=10", Query(Assert.Single(_stub.Requests)));
        var notification = Assert.Single(page.Notifications);
        Assert.Equal(AtUri.Parse(PostUri), notification.ReasonSubject);
        Assert.Equal(Cid1, notification.Cid.Value);
        Assert.Equal("2024-05-01T12:00:00+02:00", page.SeenAt.ToString());
    }

    [Fact]
    public async Task UpdateSeenAsync_WritesTheDatetimeText()
    {
        await _client.Bsky.Notification.UpdateSeenAsync(AtDatetime.Parse("2024-05-01T12:00:00Z"));

        Assert.Equal("2024-05-01T12:00:00Z", Assert.Single(_stub.Requests).JsonBody.GetProperty("seenAt").GetString());
    }

    [Fact]
    public async Task MuteActorAsync_WritesTheActor()
    {
        await _client.Bsky.Graph.MuteActorAsync(Handle.Parse("bob.test"));

        Assert.Equal("bob.test", Assert.Single(_stub.Requests).JsonBody.GetProperty("actor").GetString());
    }

    [Fact]
    public async Task EnumerateTimelineAsync_ServerRepeatsItsCursor_EndsAfterTwoRequests()
    {
        _stub.Fallback($$"""{"cursor":"same","feed":[{"post":{{PostJson}}}]}""");

        var items = new List<FeedViewPost>();
        await foreach (var item in _client.Bsky.Feed.EnumerateTimelineAsync(pageSize: 30))
            items.Add(item);

        Assert.Equal(2, items.Count);
        Assert.Equal(["limit=30", "limit=30&cursor=same"], _stub.Requests.Select(Query));
    }

    [Fact]
    public async Task EnumerateListMembersAsync_WalksEveryPageOfTheList()
    {
        var list = AtUri.Parse($"at://{DidText}/app.bsky.graph.list/3k2ld");
        var listView =
            $$"""{"uri":"{{list}}","cid":"{{Cid1}}","creator":{{ProfileJson}},"name":"L","purpose":"app.bsky.graph.defs#curatelist","indexedAt":"2024-01-01T00:00:00Z"}""";
        var item = $$"""{"uri":"at://{{DidText}}/app.bsky.graph.listitem/3k2le","subject":{{ProfileJson}}}""";
        _stub.Fallback(request => HttpStub.JsonResponse(request.Query.Contains("cursor=")
            ? $$"""{"list":{{listView}},"items":[{{item}}]}"""
            : $$"""{"cursor":"p2","list":{{listView}},"items":[{{item}},{{item}}]}"""));

        var members = new List<ATProtoNet.Lexicon.App.Bsky.Graph.ListItemView>();
        await foreach (var member in _client.Bsky.Graph.EnumerateListMembersAsync(list))
            members.Add(member);

        Assert.Equal(3, members.Count);
        Assert.All(members, member => Assert.Equal(Alice, member.Subject.Did));
        Assert.Equal([$"list={list}", $"list={list}&cursor=p2"], _stub.Requests.Select(Query));
    }

    [Fact]
    public async Task EnumerateNotificationsAsync_SendsTheFiltersWithEveryPage()
    {
        _stub.Fallback(request => HttpStub.JsonResponse(request.Query.Contains("cursor=")
            ? """{"notifications":[]}"""
            : """{"cursor":"n2","notifications":[]}"""));

        await foreach (var _ in _client.Bsky.Notification.EnumerateNotificationsAsync(reasons: [NotificationReasons.Reply]))
        {
        }

        Assert.Equal(["reasons=reply", "reasons=reply&cursor=n2"], _stub.Requests.Select(Query));
    }

    [Fact]
    public async Task PostAsync_WritesACanonicalCreatedAt()
    {
        await LoginAsync();
        _stub.Fallback($$"""{"uri":"{{PostUri}}","cid":"{{Cid1}}"}""");

        var created = await _client.Bsky.PostAsync("hi");

        var record = Assert.Single(_stub.Requests).JsonBody.GetProperty("record");
        var createdAt = AtDatetime.Parse(record.GetProperty("createdAt").GetString()!);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", createdAt.ToString());
        Assert.Equal(AtUri.Parse(PostUri), created.Uri);
    }

    [Fact]
    public async Task FollowAsync_WritesTheSubjectDid()
    {
        await LoginAsync();
        _stub.Fallback($$"""{"uri":"at://{{DidText}}/app.bsky.graph.follow/3k2lf","cid":"{{Cid1}}"}""");

        await _client.Bsky.FollowAsync(Did.Parse("did:plc:bob"));

        var body = Assert.Single(_stub.Requests).JsonBody;
        Assert.Equal("app.bsky.graph.follow", body.GetProperty("collection").GetString());
        Assert.Equal("did:plc:bob", body.GetProperty("record").GetProperty("subject").GetString());
    }

    [Fact]
    public void PostRecord_RoundTrip_KeepsTheCreatedAtText()
    {
        // Valid but not canonical (no milliseconds): rewriting it would change the record's CID.
        const string json = """{"$type":"app.bsky.feed.post","text":"hi","createdAt":"2024-01-01T00:00:00Z"}""";

        var post = JsonSerializer.Deserialize<PostRecord>(json, AtProtoJsonDefaults.Options)!;

        Assert.Equal(json, JsonSerializer.Serialize(post, AtProtoJsonDefaults.Options));
    }

    [Fact]
    public void CursoredResponses_AllImplementICursorPage()
    {
        var missing = typeof(FeedClient).Assembly.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("ATProtoNet.Lexicon.App.Bsky.", StringComparison.Ordinal) == true)
            .Where(type => type.GetProperty("Cursor", BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(string))
            .Where(type => !type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICursorPage<>)))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(missing);
    }

    private async Task LoginAsync()
    {
        _stub.Fallback($$"""{"did":"{{DidText}}","handle":"alice.test","accessJwt":"a","refreshJwt":"r"}""");
        await _client.LoginAsync("alice.test", "password");
        _stub.ClearRequests();
    }

    private static string Query(HttpStub.RecordedRequest request) => Uri.UnescapeDataString(request.Query);
}
