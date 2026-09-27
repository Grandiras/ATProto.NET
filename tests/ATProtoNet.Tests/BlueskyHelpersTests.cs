using System.Net;
using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Models;
using ATProtoNet.Tests.TestSupport;
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

    private const string GetRecord = "com.atproto.repo.getRecord";
    private const string PutRecord = "com.atproto.repo.putRecord";
    private const string CreateRecord = "com.atproto.repo.createRecord";
    private const string DeleteRecord = "com.atproto.repo.deleteRecord";

    /// <summary>
    /// A client signed in to a PDS that stores <paramref name="profile"/> (none when null), refuses
    /// the first <paramref name="conflicts"/> profile writes with <c>InvalidSwap</c>, and answers
    /// every created record with its own collection.
    /// </summary>
    private static async Task<(AtProtoClient Client, HttpStub Pds)> LoggedInAsync(string? profile = null, int conflicts = 0)
    {
        var pds = new HttpStub()
            .On("com.atproto.server.createSession", $$"""{"did":"{{DidText}}","handle":"alice.test","accessJwt":"a","refreshJwt":"r"}""")
            .On(GetRecord, _ => profile is null
                ? HttpStub.ErrorResponse(HttpStatusCode.BadRequest, "RecordNotFound", "Could not locate record")
                : HttpStub.JsonResponse($$"""{"uri":"at://{{DidText}}/app.bsky.actor.profile/self","cid":"bafyreid6erjndi5bsjevws6dtsmi76ag5mmcerfo6cfhb2vpv6wuygrvve","value":{{profile}}}"""))
            .On(DeleteRecord, "{}")
            .On(CreateRecord, request => HttpStub.JsonResponse(
                $$"""{"uri":"at://{{DidText}}/{{request.JsonBody.GetProperty("collection").GetString()}}/3l2abc","cid":"{{PostCid}}","commit":{"cid":"{{PostCid}}","rev":"3l2abcdefgh22"},"validationStatus":"valid"}"""));
        for (var i = 0; i < conflicts; i++)
            pds.On(PutRecord, HttpStatusCode.BadRequest, """{"error":"InvalidSwap","message":"Record was at bafyreidhormwlyipxyuuzqc6mspskhovsiybzk76rtn5fnr264hhotls3u"}""");
        pds.On(PutRecord, $$"""{"uri":"at://{{DidText}}/app.bsky.actor.profile/self","cid":"bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi"}""");

        var client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
            new HttpClient(pds));
        await client.LoginAsync("alice.test", "password");
        return (client, pds);
    }

    private static List<JsonElement> Bodies(HttpStub pds, string nsid) => [.. pds.To(nsid).Select(r => r.JsonBody)];

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

        var body = Assert.Single(Bodies(pds, CreateRecord));
        Assert.Equal(DidText, body.GetProperty("repo").GetString());
        Assert.Equal("app.bsky.feed.post", body.GetProperty("collection").GetString());
        var record = body.GetProperty("record");
        Assert.Equal("app.bsky.feed.post", record.GetProperty("$type").GetString());
        Assert.Equal("hello", record.GetProperty("text").GetString());
        Assert.False(record.TryGetProperty("facets", out var _));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", record.GetProperty("createdAt").GetString());

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

        var record = Assert.Single(Bodies(pds, CreateRecord)).GetProperty("record");
        Assert.Equal("Hi #atproto", record.GetProperty("text").GetString());
        var facet = Assert.Single(record.GetProperty("facets").EnumerateArray());
        Assert.Equal(3, facet.GetProperty("index").GetProperty("byteStart").GetInt32());
        Assert.Equal(PostUri, record.GetProperty("reply").GetProperty("parent").GetProperty("uri").GetString());
        Assert.Equal("en", Assert.Single(record.GetProperty("langs").EnumerateArray()).GetString());
        Assert.Equal("sdk", Assert.Single(record.GetProperty("tags").EnumerateArray()).GetString());
        Assert.Equal("2026-01-02T03:04:05.000Z", record.GetProperty("createdAt").GetString());
    }

    [Theory]
    [InlineData("app.bsky.feed.like", $$"""{"uri":"{{PostUri}}","cid":"{{PostCid}}"}""")]
    [InlineData("app.bsky.feed.repost", $$"""{"uri":"{{PostUri}}","cid":"{{PostCid}}"}""")]
    [InlineData("app.bsky.graph.follow", "\"did:plc:bob\"")]
    public async Task SubjectHelpers_WriteTheirRecordAboutTheSubject(string collection, string subject)
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;

        var written = collection switch
        {
            "app.bsky.feed.like" => await client.Bsky.LikeAsync(Post),
            "app.bsky.feed.repost" => await client.Bsky.RepostAsync(Post),
            _ => await client.Bsky.FollowAsync(Did.Parse("did:plc:bob")),
        };

        var body = Assert.Single(Bodies(pds, CreateRecord));
        Assert.Equal(collection, body.GetProperty("collection").GetString());
        var record = body.GetProperty("record");
        Assert.Equal(collection, record.GetProperty("$type").GetString());
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(subject).RootElement, record.GetProperty("subject")), record.GetRawText());
        Assert.Equal(Nsid.Parse(collection), written.Uri.Collection);
    }

    [Fact]
    public async Task DeleteRecordAsync_DeletesTheRecordTheUriNames()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;

        await client.Bsky.DeleteRecordAsync(AtUri.Parse($"at://{DidText}/app.bsky.feed.like/3l2xyz"));

        var body = Assert.Single(Bodies(pds, DeleteRecord));
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
        var before = pds.Requests.Count;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.Bsky.DeleteRecordAsync(AtUri.Parse(uri)));

        Assert.Equal("uri", ex.ParamName);
        Assert.Equal(before, pds.Requests.Count);
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
        var (client, pds) = await LoggedInAsync(StoredProfile);
        using var _ = client;

        var written = await client.Bsky.UpdateProfileAsync(p => p.Description = "new bio");

        Assert.Equal("bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi", written.Cid);
        Assert.Equal("self", written.RecordKey);

        var put = Assert.Single(Bodies(pds, PutRecord));
        var record = put.GetProperty("record");
        using var expected = JsonDocument.Parse(StoredProfile.Replace("old bio", "new bio"));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, record), record.GetRawText());
        Assert.Equal("bafyreid6erjndi5bsjevws6dtsmi76ag5mmcerfo6cfhb2vpv6wuygrvve", put.GetProperty("swapRecord").GetString());
    }

    [Fact]
    public async Task UpdateProfileAsync_SettingNull_RemovesTheField()
    {
        var (client, pds) = await LoggedInAsync(StoredProfile);
        using var _ = client;

        await client.Bsky.UpdateProfileAsync(p => p.PinnedPost = null);

        var record = Assert.Single(Bodies(pds, PutRecord)).GetProperty("record");
        Assert.False(record.TryGetProperty("pinnedPost", out var _));
        Assert.Equal("she/her", record.GetProperty("pronouns").GetString());
    }

    [Fact]
    public async Task UpdateProfileAsync_ConcurrentWrite_RereadsAndRetries()
    {
        var (client, pds) = await LoggedInAsync(StoredProfile, conflicts: 1);
        using var _ = client;
        var calls = 0;

        await client.Bsky.UpdateProfileAsync(p =>
        {
            calls++;
            p.DisplayName = "Alice " + calls;
        });

        Assert.Equal(2, calls);
        Assert.Equal(2, pds.To(GetRecord).Count());
        Assert.Equal(2, Bodies(pds, PutRecord).Count);
        Assert.Equal("Alice 2", Bodies(pds, PutRecord)[1].GetProperty("record").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task UpdateProfileAsync_PersistentConflict_GivesUpAfterThreeAttempts()
    {
        var (client, pds) = await LoggedInAsync(StoredProfile, conflicts: 5);
        using var _ = client;

        var ex = await Assert.ThrowsAsync<XrpcException>(
            () => client.Bsky.UpdateProfileAsync(p => p.DisplayName = "x"));

        Assert.True(ex.Is(XrpcErrors.InvalidSwap));
        Assert.Equal(3, Bodies(pds, PutRecord).Count);
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

        var put = Assert.Single(Bodies(pds, PutRecord));
        Assert.False(put.TryGetProperty("swapRecord", out var _));
        Assert.Equal("app.bsky.actor.profile", put.GetProperty("record").GetProperty("$type").GetString());
        Assert.Equal("Alice", put.GetProperty("record").GetProperty("displayName").GetString());
    }

    private sealed record ProfileSeen(string? DisplayName, AtDatetime? CreatedAt, IDictionary<string, JsonElement>? ExtensionData);
}
