using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Spaces;

public class SpaceClientTests : IDisposable
{
    private const string Space = "at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/space/com.atmoboards.forum/default";
    private const string Repo = "did:plc:z72i7hdynmk6r22z27h6tvur";

    private readonly MockHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;
    private readonly SpaceClient _space;
    private readonly SimpleSpaceClient _simpleSpace;

    public SpaceClientTests()
    {
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://pds.example.com/") };
        _xrpc = new XrpcClient(_httpClient, NullLogger.Instance, AtProtoJsonDefaults.Options);
        _xrpc.SetTokens("test-token");
        _space = new SpaceClient(_xrpc);
        _simpleSpace = new SimpleSpaceClient(_xrpc);
    }

    private void RespondWith(string json)
    {
        _handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    // ── Request shape ────────────────────────────────────────────

    [Fact]
    public async Task ListRepoOpsAsync_SendsEveryParameterItWasGiven()
    {
        RespondWith("""{"ops":[]}""");

        await _space.ListRepoOpsAsync(Space, Repo, since: "3l6ov", limit: 25, cursor: "c", excludeValues: true);

        var query = _handler.LastRequest!.RequestUri!.Query;
        Assert.Contains($"space={Uri.EscapeDataString(Space)}", query, StringComparison.Ordinal);
        Assert.Contains($"repo={Uri.EscapeDataString(Repo)}", query, StringComparison.Ordinal);
        Assert.Contains("since=3l6ov", query, StringComparison.Ordinal);
        Assert.Contains("limit=25", query, StringComparison.Ordinal);
        Assert.Contains("cursor=c", query, StringComparison.Ordinal);
        Assert.Contains("excludeValues=true", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListRepoOpsAsync_OmitsParametersLeftUnset()
    {
        RespondWith("""{"ops":[]}""");

        await _space.ListRepoOpsAsync(Space, Repo);

        var query = _handler.LastRequest!.RequestUri!.Query;
        Assert.DoesNotContain("since=", query, StringComparison.Ordinal);
        Assert.DoesNotContain("excludeValues=", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDelegationTokenAsync_HitsThePdsEndpoint()
    {
        RespondWith("""{"token":"jwt"}""");

        var response = await _space.GetDelegationTokenAsync(Space);

        Assert.Equal("/xrpc/com.atproto.space.getDelegationToken", _handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("jwt", response.Token);
    }

    // ── Response binding ─────────────────────────────────────────

    [Fact]
    public async Task GetLatestCommitAsync_DecodesTheLexiconBytesFields()
    {
        // Lexicon `bytes` arrives as { "$bytes": "<base64>" }, unpadded on the wire.
        RespondWith("""
        {"commit":{"ver":1,
          "hash":{"$bytes":"AQID"},
          "ikm":{"$bytes":"BAUG"},
          "sig":{"$bytes":"BwgJ"},
          "mac":{"$bytes":"CgsM"},
          "rev":"3l6oveex3ii2l"}}
        """);

        var response = await _space.GetLatestCommitAsync(Space, Repo);

        Assert.Equal(1, response.Commit.Ver);
        Assert.Equal([1, 2, 3], response.Commit.Hash);
        Assert.Equal([4, 5, 6], response.Commit.Ikm);
        Assert.Equal([7, 8, 9], response.Commit.Sig);
        Assert.Equal([10, 11, 12], response.Commit.Mac);
        Assert.Equal("3l6oveex3ii2l", response.Commit.Rev);
    }

    [Fact]
    public async Task ListReposAsync_DecodesTheWriterSet()
    {
        RespondWith("""
        {"repos":[{"did":"did:plc:z72i7hdynmk6r22z27h6tvur","rev":"3l6oveex3ii2l","hash":{"$bytes":"AQID"}}],
         "cursor":"next"}
        """);

        var response = await _space.ListReposAsync(Space);

        Assert.Equal("next", response.Cursor);
        var repo = Assert.Single(response.Repos);
        Assert.Equal(Repo, repo.Did);
        Assert.Equal([1, 2, 3], repo.Hash);
    }

    [Fact]
    public async Task ListRepoOpsAsync_DistinguishesCreatesUpdatesAndDeletes()
    {
        RespondWith("""
        {"ops":[
          {"rev":"1","collection":"com.example.n","rkey":"a","cid":"bafy1","prev":null,"value":{"text":"hi"}},
          {"rev":"2","collection":"com.example.n","rkey":"a","cid":"bafy2","prev":"bafy1"},
          {"rev":"3","collection":"com.example.n","rkey":"a","cid":null,"prev":"bafy2"}]}
        """);

        var response = await _space.ListRepoOpsAsync(Space, Repo);

        var create = response.Ops[0].ToRepoOp();
        Assert.Null(create.Prev);
        Assert.Equal("bafy1", create.Cid);
        Assert.Equal("hi", response.Ops[0].Value!.Value.GetProperty("text").GetString());

        var update = response.Ops[1].ToRepoOp();
        Assert.Equal("bafy1", update.Prev);
        Assert.Equal("bafy2", update.Cid);

        var delete = response.Ops[2].ToRepoOp();
        Assert.Equal("bafy2", delete.Prev);
        Assert.Null(delete.Cid);
    }

    [Fact]
    public async Task ListRepoOpsAsync_OmitsTheCommitOnABackfillResponse()
    {
        // A commit arrives only at the head of the oplog; a paginated response carries a cursor
        // and no commit, which is what tells a syncer it has not caught up yet.
        RespondWith("""{"ops":[],"cursor":"more"}""");

        var response = await _space.ListRepoOpsAsync(Space, Repo);

        Assert.Null(response.Commit);
        Assert.Equal("more", response.Cursor);
    }

    [Fact]
    public async Task EnumerateRecordsAsync_FollowsPaginationAndStops()
    {
        var page = 0;
        _handler.ResponseFactory = _ =>
        {
            var json = page++ == 0
                ? """{"records":[{"collection":"com.example.n","rkey":"a","cid":"bafy1"}],"cursor":"next"}"""
                : """{"records":[{"collection":"com.example.n","rkey":"b","cid":"bafy2"}]}""";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        };

        var records = new List<SpaceRecordView>();
        await foreach (var record in _space.EnumerateRecordsAsync(Space, Repo))
            records.Add(record);

        Assert.Equal(["com.example.n/a", "com.example.n/b"], records.Select(r => r.Path));
    }

    // ── Writes ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateRecordAsync_PostsTheRecordAndOmitsUnsetOptions()
    {
        RespondWith("""{"uri":"at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/space/com.atmoboards.forum/default/did:plc:z72i7hdynmk6r22z27h6tvur/com.example.n/a","cid":"bafy1"}""");

        var result = await _space.CreateRecordAsync(
            Space, Repo, "com.example.n", new { text = "hi" });

        var body = JsonSerializer.Deserialize<JsonElement>(_handler.LastBody!);
        Assert.Equal(Space, body.GetProperty("space").GetString());
        Assert.Equal("hi", body.GetProperty("record").GetProperty("text").GetString());
        Assert.False(body.TryGetProperty("rkey", out _));
        Assert.False(body.TryGetProperty("validate", out _));

        Assert.Equal("com.example.n", result.ToRecordUri().Collection);
    }

    [Fact]
    public async Task ApplyWritesAsync_TagsEachOperationWithItsUnionDiscriminator()
    {
        RespondWith("""{"results":[]}""");

        await _space.ApplyWritesAsync(Space, Repo,
        [
            new SpaceCreateOp { Collection = "com.example.n", Value = new { text = "a" } },
            new SpaceUpdateOp { Collection = "com.example.n", Rkey = "b", Value = new { text = "b" } },
            new SpaceDeleteOp { Collection = "com.example.n", Rkey = "c" },
        ]);

        var writes = JsonSerializer.Deserialize<JsonElement>(_handler.LastBody!).GetProperty("writes");

        Assert.Equal("com.atproto.space.applyWrites#create", writes[0].GetProperty("$type").GetString());
        Assert.Equal("com.atproto.space.applyWrites#update", writes[1].GetProperty("$type").GetString());
        Assert.Equal("com.atproto.space.applyWrites#delete", writes[2].GetProperty("$type").GetString());
    }

    // ── simplespace ──────────────────────────────────────────────

    [Fact]
    public async Task CreateSpaceAsync_DefaultsToAMemberListAndOpenAppAccess()
    {
        RespondWith($$"""{"uri":"{{Space}}"}""");

        await _simpleSpace.CreateSpaceAsync("com.atmoboards.forum");

        var body = JsonSerializer.Deserialize<JsonElement>(_handler.LastBody!);
        Assert.Equal(
            SimpleSpaceTypes.MemberListPolicy, body.GetProperty("policy").GetProperty("$type").GetString());
        Assert.Equal(
            SimpleSpaceTypes.Open, body.GetProperty("appAccess").GetProperty("$type").GetString());
        Assert.False(body.TryGetProperty("skey", out _));
    }

    [Fact]
    public async Task CreateSpaceAsync_SerializesAManagingAppPolicyAndAnAllowList()
    {
        RespondWith($$"""{"uri":"{{Space}}"}""");

        await _simpleSpace.CreateSpaceAsync(
            "com.atmoboards.forum",
            skey: "default",
            policy: new ManagingAppPolicy { ManagingApp = "did:web:example.com#forum" },
            appAccess: new AllowListAppAccess { Allowed = ["https://app.example.com/client-metadata.json"] });

        var body = JsonSerializer.Deserialize<JsonElement>(_handler.LastBody!);
        var policy = body.GetProperty("policy");
        Assert.Equal(SimpleSpaceTypes.ManagingAppPolicy, policy.GetProperty("$type").GetString());
        Assert.Equal("did:web:example.com#forum", policy.GetProperty("managingApp").GetString());

        var appAccess = body.GetProperty("appAccess");
        Assert.Equal(SimpleSpaceTypes.AllowList, appAccess.GetProperty("$type").GetString());
        Assert.Equal(
            "https://app.example.com/client-metadata.json", appAccess.GetProperty("allowed")[0].GetString());
    }

    [Fact]
    public async Task GetSpaceAsync_DeserializesThePolicyUnions()
    {
        RespondWith(
            $$"""
            {"uri":"{{Space}}",
             "policy":{"$type":"{{SimpleSpaceTypes.ManagingAppPolicy}}","managingApp":"did:web:example.com#forum"},
             "appAccess":{"$type":"{{SimpleSpaceTypes.AllowList}}",
                          "allowed":["https://app.example.com/client-metadata.json"]}
            }
            """);

        var response = await _simpleSpace.GetSpaceAsync(Space);

        var policy = Assert.IsType<ManagingAppPolicy>(response.Policy);
        Assert.Equal("did:web:example.com#forum", policy.ManagingApp);
        var appAccess = Assert.IsType<AllowListAppAccess>(response.AppAccess);
        Assert.Single(appAccess.Allowed);
    }

    [Fact]
    public async Task UpdateSpaceAsync_OmitsWhicheverPolicyWasLeftAlone()
    {
        RespondWith("{}");

        await _simpleSpace.UpdateSpaceAsync(Space, policy: new PublicPolicy());

        var body = JsonSerializer.Deserialize<JsonElement>(_handler.LastBody!);
        Assert.Equal(SimpleSpaceTypes.PublicPolicy, body.GetProperty("policy").GetProperty("$type").GetString());
        Assert.False(body.TryGetProperty("appAccess", out _));
    }

    public void Dispose()
    {
        _xrpc.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return ResponseFactory(request);
        }
    }
}
