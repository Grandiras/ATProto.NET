using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Http;

namespace ATProtoNet.Tests;

/// <summary>
/// <see cref="AtProtoClient.UpdateProfileAsync"/> is a guarded read-modify-write that keeps every
/// field the caller does not touch, including ones this SDK does not model.
/// </summary>
public sealed class AtProtoClientUpdateProfileTests
{
    private const string Did = "did:plc:ragtjsm2j2vknwkz3zp4oxrd";

    private const string StoredProfile =
        """
        {"$type":"app.bsky.actor.profile","displayName":"Alice","description":"old bio","pronouns":"she/her","website":"https://alice.example.com",
         "labels":{"$type":"com.atproto.label.defs#selfLabels","values":[{"val":"!no-unauthenticated"}]},
         "joinedViaStarterPack":{"uri":"at://did:plc:b/app.bsky.graph.starterpack/1","cid":"bafy2"},
         "pinnedPost":{"uri":"at://did:plc:ragtjsm2j2vknwkz3zp4oxrd/app.bsky.feed.post/1","cid":"bafy1"},
         "createdAt":"2024-01-01T00:00:00.000Z","futureField":{"x":1}}
        """;

    private sealed class FakePds : HttpMessageHandler
    {
        public string? Profile { get; set; }

        public string ProfileCid { get; set; } = "bafyprofile1";

        public Queue<HttpStatusCode> PutOutcomes { get; } = new();

        public List<JsonElement> Puts { get; } = [];

        public int Gets { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("com.atproto.server.createSession", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, $$"""{"did":"{{Did}}","handle":"alice.test","accessJwt":"a","refreshJwt":"r"}""");

            if (path.EndsWith("com.atproto.repo.getRecord", StringComparison.Ordinal))
            {
                Gets++;
                return Profile is null
                    ? Json(HttpStatusCode.BadRequest, """{"error":"RecordNotFound","message":"Could not locate record"}""")
                    : Json(HttpStatusCode.OK, $$"""{"uri":"at://{{Did}}/app.bsky.actor.profile/self","cid":"{{ProfileCid}}","value":{{Profile}}}""");
            }

            if (path.EndsWith("com.atproto.repo.putRecord", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Puts.Add(body.RootElement.Clone());

                if (PutOutcomes.TryDequeue(out var outcome) && outcome != HttpStatusCode.OK)
                    return Json(outcome, """{"error":"InvalidSwap","message":"Record was at bafyother"}""");

                return Json(HttpStatusCode.OK, $$"""{"uri":"at://{{Did}}/app.bsky.actor.profile/self","cid":"bafyprofile2"}""");
            }

            return Json(HttpStatusCode.NotFound, """{"error":"MethodNotImplemented"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static async Task<(AtProtoClient Client, FakePds Pds)> LoggedInAsync()
    {
        var pds = new FakePds();
        var client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
            new HttpClient(pds), null, null);
        await client.LoginAsync("alice.test", "password");
        return (client, pds);
    }

    [Fact]
    public async Task UpdateProfileAsync_ChangesOneField_KeepsEveryOtherFieldIncludingUnknownOnes()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;
        pds.Profile = StoredProfile;

        var written = await client.UpdateProfileAsync(p => p.Description = "new bio");

        Assert.Equal("bafyprofile2", written.Cid);
        Assert.Equal("self", written.RecordKey);

        var put = Assert.Single(pds.Puts);
        var record = put.GetProperty("record");
        using var expected = JsonDocument.Parse(StoredProfile.Replace("old bio", "new bio"));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, record), record.GetRawText());
        Assert.Equal("bafyprofile1", put.GetProperty("swapRecord").GetString());
    }

    [Fact]
    public async Task UpdateProfileAsync_SettingNull_RemovesTheField()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;
        pds.Profile = StoredProfile;

        await client.UpdateProfileAsync(p => p.PinnedPost = null);

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

        await client.UpdateProfileAsync(p =>
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
            () => client.UpdateProfileAsync(p => p.DisplayName = "x"));

        Assert.True(ex.Is(XrpcErrors.InvalidSwap));
        Assert.Equal(3, pds.Puts.Count);
    }

    [Fact]
    public async Task UpdateProfileAsync_NoProfileYet_CreatesOneWithoutSwapGuard()
    {
        var (client, pds) = await LoggedInAsync();
        using var _ = client;
        ProfileSeen? seen = null;

        await client.UpdateProfileAsync(p =>
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

    [Fact]
    public async Task UpdateProfileAsync_NotAuthenticated_Throws()
    {
        using var client = new AtProtoClient(new AtProtoClientOptions { AutoRefreshSession = false });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.UpdateProfileAsync(p => p.DisplayName = "x"));
    }

    private sealed record ProfileSeen(string? DisplayName, string? CreatedAt, IDictionary<string, JsonElement>? ExtensionData);
}
