using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Models;
using RichTextBuilder = ATProtoNet.Lexicon.App.Bsky.RichText.RichTextBuilder;

namespace ATProtoNet.Tests;

/// <summary>
/// The Bluesky record helpers on <see cref="AtProtoClient.Bsky"/>: posts, likes, reposts, follows,
/// deleting them, and the guarded profile read-modify-write, which keeps every field the caller
/// does not touch, including ones this SDK does not model.
/// </summary>
public sealed class BlueskyHelpersTests
{
    private const string DidText = "did:plc:ragtjsm2j2vknwkz3zp4oxrd";
    private const string PostUri = "at://did:plc:ragtjsm2j2vknwkz3zp4oxrd/app.bsky.feed.post/3l2abc";
    private const string PostCid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";

    private const string StoredProfile =
        """
        {"$type":"app.bsky.actor.profile","displayName":"Alice","description":"old bio","pronouns":"she/her","website":"https://alice.example.com",
         "labels":{"$type":"com.atproto.label.defs#selfLabels","values":[{"val":"!no-unauthenticated"}]},
         "joinedViaStarterPack":{"uri":"at://did:plc:b/app.bsky.graph.starterpack/1","cid":"bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4"},
         "pinnedPost":{"uri":"at://did:plc:ragtjsm2j2vknwkz3zp4oxrd/app.bsky.feed.post/1","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm"},
         "createdAt":"2024-01-01T00:00:00.000Z","futureField":{"x":1}}
        """;

    private sealed class FakePds : HttpMessageHandler
    {
        public string? Profile { get; set; }

        public string ProfileCid { get; set; } = "bafyreid6erjndi5bsjevws6dtsmi76ag5mmcerfo6cfhb2vpv6wuygrvve";

        public Queue<HttpStatusCode> PutOutcomes { get; } = new();

        public List<JsonElement> Puts { get; } = [];

        public List<JsonElement> Creates { get; } = [];

        public List<JsonElement> Deletes { get; } = [];

        public int Gets { get; private set; }

        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("com.atproto.server.createSession", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, $$"""{"did":"{{DidText}}","handle":"alice.test","accessJwt":"a","refreshJwt":"r"}""");

            if (path.EndsWith("com.atproto.repo.getRecord", StringComparison.Ordinal))
            {
                Gets++;
                return Profile is null
                    ? Json(HttpStatusCode.BadRequest, """{"error":"RecordNotFound","message":"Could not locate record"}""")
                    : Json(HttpStatusCode.OK, $$"""{"uri":"at://{{DidText}}/app.bsky.actor.profile/self","cid":"{{ProfileCid}}","value":{{Profile}}}""");
            }

            if (path.EndsWith("com.atproto.repo.putRecord", StringComparison.Ordinal))
            {
                Puts.Add(await BodyAsync(request, ct));

                if (PutOutcomes.TryDequeue(out var outcome) && outcome != HttpStatusCode.OK)
                    return Json(outcome, """{"error":"InvalidSwap","message":"Record was at bafyreidhormwlyipxyuuzqc6mspskhovsiybzk76rtn5fnr264hhotls3u"}""");

                return Json(HttpStatusCode.OK, $$"""{"uri":"at://{{DidText}}/app.bsky.actor.profile/self","cid":"bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi"}""");
            }

            if (path.EndsWith("com.atproto.repo.createRecord", StringComparison.Ordinal))
            {
                var body = await BodyAsync(request, ct);
                Creates.Add(body);
                var collection = body.GetProperty("collection").GetString();
                return Json(HttpStatusCode.OK, $$"""{"uri":"at://{{DidText}}/{{collection}}/3l2abc","cid":"{{PostCid}}","commit":{"cid":"{{PostCid}}","rev":"3l2abcdefgh22"},"validationStatus":"valid"}""");
            }

            if (path.EndsWith("com.atproto.repo.deleteRecord", StringComparison.Ordinal))
            {
                Deletes.Add(await BodyAsync(request, ct));
                return Json(HttpStatusCode.OK, "{}");
            }

            return Json(HttpStatusCode.NotFound, """{"error":"MethodNotImplemented"}""");
        }

        private static async Task<JsonElement> BodyAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            return body.RootElement.Clone();
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static async Task<(AtProtoClient Client, FakePds Pds)> LoggedInAsync()
    {
        var pds = new FakePds();
        var client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
            new HttpClient(pds));
        await client.LoginAsync("alice.test", "password");
        return (client, pds);
    }

    private static readonly StrongRef Post = new() { Uri = AtUri.Parse(PostUri), Cid = Cid.Parse(PostCid) };

    // ──────────────────────────────────────────────────────────
    //  Posts, likes, reposts, follows
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PostAsync_PlainString_WritesTextWithoutFacets()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;

        var posted = await client.Bsky.PostAsync("hello");

        var body = Assert.Single(pds.Creates);
        Assert.Equal(DidText, body.GetProperty("repo").GetString());
        Assert.Equal("app.bsky.feed.post", body.GetProperty("collection").GetString());
        var record = body.GetProperty("record");
        Assert.Equal("app.bsky.feed.post", record.GetProperty("$type").GetString());
        Assert.Equal("hello", record.GetProperty("text").GetString());
        Assert.False(record.TryGetProperty("facets", out var _));
        Assert.True(record.TryGetProperty("createdAt", out var _));

        Assert.Equal("3l2abc", posted.RecordKey);
        Assert.Equal(Tid.Parse("3l2abcdefgh22"), posted.Commit?.Rev);
        Assert.Equal("valid", posted.ValidationStatus);
    }

    [Fact]
    public async Task PostAsync_RichTextAndOptions_WritesFacetsAndEveryOption()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;
        var text = new RichTextBuilder().Text("Hi ").Tag("atproto").Build();

        await client.Bsky.PostAsync(text, new PostOptions
        {
            Reply = new ReplyRef { Root = Post, Parent = Post },
            Langs = ["en"],
            Tags = ["sdk"],
            CreatedAt = AtDatetime.Parse("2026-01-02T03:04:05.000Z"),
        });

        var record = Assert.Single(pds.Creates).GetProperty("record");
        Assert.Equal("Hi #atproto", record.GetProperty("text").GetString());
        var facet = Assert.Single(record.GetProperty("facets").EnumerateArray());
        Assert.Equal(3, facet.GetProperty("index").GetProperty("byteStart").GetInt32());
        Assert.Equal(PostUri, record.GetProperty("reply").GetProperty("parent").GetProperty("uri").GetString());
        Assert.Equal("en", Assert.Single(record.GetProperty("langs").EnumerateArray()).GetString());
        Assert.Equal("sdk", Assert.Single(record.GetProperty("tags").EnumerateArray()).GetString());
        Assert.Equal("2026-01-02T03:04:05.000Z", record.GetProperty("createdAt").GetString());
    }

    [Fact]
    public async Task LikeAsync_WritesALikeOfTheSubject()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;

        var like = await client.Bsky.LikeAsync(Post);

        var body = Assert.Single(pds.Creates);
        Assert.Equal("app.bsky.feed.like", body.GetProperty("collection").GetString());
        var subject = body.GetProperty("record").GetProperty("subject");
        Assert.Equal(PostUri, subject.GetProperty("uri").GetString());
        Assert.Equal(PostCid, subject.GetProperty("cid").GetString());
        Assert.Equal(Nsid.Parse("app.bsky.feed.like"), like.Uri.Collection);
    }

    [Fact]
    public async Task RepostAsync_WritesARepostOfTheSubject()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;

        await client.Bsky.RepostAsync(new RecordRef(AtUri.Parse(PostUri), Cid.Parse(PostCid)).ToStrongRef());

        var body = Assert.Single(pds.Creates);
        Assert.Equal("app.bsky.feed.repost", body.GetProperty("collection").GetString());
        Assert.Equal(PostUri, body.GetProperty("record").GetProperty("subject").GetProperty("uri").GetString());
    }

    [Fact]
    public async Task DeleteRecordAsync_DeletesTheRecordTheUriNames()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;

        await client.Bsky.DeleteRecordAsync(AtUri.Parse($"at://{DidText}/app.bsky.feed.like/3l2xyz"));

        var body = Assert.Single(pds.Deletes);
        Assert.Equal(DidText, body.GetProperty("repo").GetString());
        Assert.Equal("app.bsky.feed.like", body.GetProperty("collection").GetString());
        Assert.Equal("3l2xyz", body.GetProperty("rkey").GetString());
    }

    [Theory]
    [InlineData("at://did:plc:ragtjsm2j2vknwkz3zp4oxrd")]
    [InlineData("at://did:plc:ragtjsm2j2vknwkz3zp4oxrd/app.bsky.feed.like")]
    public async Task DeleteRecordAsync_UriWithoutCollectionAndRecordKey_ThrowsWithoutARequest(string uri)
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;
        var before = pds.Requests;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.Bsky.DeleteRecordAsync(AtUri.Parse(uri)));

        Assert.Equal("uri", ex.ParamName);
        Assert.Equal(before, pds.Requests);
    }

    [Fact]
    public async Task Helpers_NotAuthenticated_ThrowAuthenticationRequired()
    {
        using var client = new AtProtoClient(new AtProtoClientOptions { AutoRefreshSession = false });

        var ex = await Assert.ThrowsAsync<XrpcAuthenticationException>(() => client.Bsky.PostAsync("hi"));
        Assert.True(ex.Is(XrpcErrors.AuthenticationRequired));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);

        await Assert.ThrowsAsync<XrpcAuthenticationException>(() => client.Bsky.LikeAsync(Post));
        await Assert.ThrowsAsync<XrpcAuthenticationException>(() => client.Bsky.RepostAsync(Post));
        await Assert.ThrowsAsync<XrpcAuthenticationException>(() => client.Bsky.FollowAsync(Did.Parse("did:plc:bob")));
        await Assert.ThrowsAsync<XrpcAuthenticationException>(() => client.Bsky.DeleteRecordAsync(AtUri.Parse(PostUri)));
        await Assert.ThrowsAsync<XrpcAuthenticationException>(() => client.Bsky.UpdateProfileAsync(p => p.DisplayName = "x"));
    }

    // ──────────────────────────────────────────────────────────
    //  Profile
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProfileAsync_ChangesOneField_KeepsEveryOtherFieldIncludingUnknownOnes()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;
        pds.Profile = StoredProfile;

        var written = await client.Bsky.UpdateProfileAsync(p => p.Description = "new bio");

        Assert.Equal("bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi", written.Cid);
        Assert.Equal("self", written.RecordKey);

        var put = Assert.Single(pds.Puts);
        var record = put.GetProperty("record");
        using var expected = JsonDocument.Parse(StoredProfile.Replace("old bio", "new bio"));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, record), record.GetRawText());
        Assert.Equal("bafyreid6erjndi5bsjevws6dtsmi76ag5mmcerfo6cfhb2vpv6wuygrvve", put.GetProperty("swapRecord").GetString());
    }

    [Fact]
    public async Task UpdateProfileAsync_SettingNull_RemovesTheField()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;
        pds.Profile = StoredProfile;

        await client.Bsky.UpdateProfileAsync(p => p.PinnedPost = null);

        var record = Assert.Single(pds.Puts).GetProperty("record");
        Assert.False(record.TryGetProperty("pinnedPost", out var _));
        Assert.Equal("she/her", record.GetProperty("pronouns").GetString());
    }

    [Fact]
    public async Task UpdateProfileAsync_ConcurrentWrite_RereadsAndRetries()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;
        pds.Profile = StoredProfile;
        pds.PutOutcomes.Enqueue(HttpStatusCode.BadRequest);
        var calls = 0;

        await client.Bsky.UpdateProfileAsync(p =>
        {
            calls++;
            p.DisplayName = "Alice " + calls;
        });

        Assert.Equal(2, calls);
        Assert.Equal(2, pds.Gets);
        Assert.Equal(2, pds.Puts.Count);
        Assert.Equal("Alice 2", pds.Puts[1].GetProperty("record").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task UpdateProfileAsync_PersistentConflict_GivesUpAfterThreeAttempts()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;
        pds.Profile = StoredProfile;
        for (var i = 0; i < 5; i++)
            pds.PutOutcomes.Enqueue(HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<XrpcException>(
            () => client.Bsky.UpdateProfileAsync(p => p.DisplayName = "x"));

        Assert.True(ex.Is(XrpcErrors.InvalidSwap));
        Assert.Equal(3, pds.Puts.Count);
    }

    [Fact]
    public async Task UpdateProfileAsync_NoProfileYet_CreatesOneWithoutSwapGuard()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;
        ProfileSeen? seen = null;

        await client.Bsky.UpdateProfileAsync(p =>
        {
            seen = new ProfileSeen(p.DisplayName, p.CreatedAt, p.ExtensionData);
            p.DisplayName = "Alice";
        });

        Assert.Null(seen!.DisplayName);
        Assert.NotNull(seen.CreatedAt);
        Assert.Null(seen.ExtensionData);

        var put = Assert.Single(pds.Puts);
        Assert.False(put.TryGetProperty("swapRecord", out var _));
        Assert.Equal("app.bsky.actor.profile", put.GetProperty("record").GetProperty("$type").GetString());
        Assert.Equal("Alice", put.GetProperty("record").GetProperty("displayName").GetString());
    }

    private sealed record ProfileSeen(string? DisplayName, AtDatetime? CreatedAt, IDictionary<string, JsonElement>? ExtensionData);
}
