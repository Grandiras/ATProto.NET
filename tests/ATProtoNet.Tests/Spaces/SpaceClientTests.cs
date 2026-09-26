using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Spaces;

public sealed class SpaceClientTests : IDisposable
{
    private static readonly SpaceUri Space = SpaceUri.Parse("at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/space/com.atmoboards.forum/default");
    private static readonly Did Repo = Did.Parse("did:plc:z72i7hdynmk6r22z27h6tvur");

    // Distinct, well-formed CIDs for the payloads below.
    private const string Cid1 = "bafyreicuerrgxezkf745depqtasepklz7xoyws7ckhusinymwzuqbxybry";
    private const string Cid2 = "bafyreif5xp2wdd3kcu54tgzu4zjp2iazrs2zabrfkpz6gklpco7cfhfopa";

    private readonly XrpcTestClient _fixture = new();

    private SpaceClient Space_ => _fixture.Client.Space;
    private SimpleSpaceClient SimpleSpace => _fixture.Client.SimpleSpace;

    public void Dispose() => _fixture.Dispose();

    // ── Request shape ────────────────────────────────────────────

    [Fact]
    public async Task ListRepoOpsAsync_SendsEveryParameterItWasGiven()
    {
        _fixture.On("com.atproto.space.listRepoOps", """{"ops":[]}""");

        await Space_.ListRepoOpsAsync(
            Space, Repo, since: Tid.Parse("3l6oveex3ii2l"), excludeValues: true, limit: 25, cursor: "c");

        var query = _fixture.To("com.atproto.space.listRepoOps").Single().Uri.Query;
        Assert.Contains($"space={Uri.EscapeDataString(Space.Value)}", query, StringComparison.Ordinal);
        Assert.Contains($"repo={Uri.EscapeDataString(Repo.Value)}", query, StringComparison.Ordinal);
        Assert.Contains("since=3l6oveex3ii2l", query, StringComparison.Ordinal);
        Assert.Contains("limit=25", query, StringComparison.Ordinal);
        Assert.Contains("cursor=c", query, StringComparison.Ordinal);
        Assert.Contains("excludeValues=true", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListRepoOpsAsync_OmitsParametersLeftUnset()
    {
        _fixture.On("com.atproto.space.listRepoOps", """{"ops":[]}""");

        await Space_.ListRepoOpsAsync(Space, Repo);

        var query = _fixture.To("com.atproto.space.listRepoOps").Single().Uri.Query;
        Assert.DoesNotContain("since=", query, StringComparison.Ordinal);
        Assert.DoesNotContain("excludeValues=", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDelegationTokenAsync_HitsThePdsEndpoint()
    {
        _fixture.On("com.atproto.space.getDelegationToken", """{"token":"jwt"}""");

        var response = await Space_.GetDelegationTokenAsync(Space);

        Assert.Equal(
            "/xrpc/com.atproto.space.getDelegationToken",
            _fixture.To("com.atproto.space.getDelegationToken").Single().Path);
        Assert.Equal("jwt", response.Token);
    }

    // ── Response binding ─────────────────────────────────────────

    [Fact]
    public async Task GetLatestCommitAsync_DecodesTheLexiconBytesFields()
    {
        // Lexicon `bytes` arrives as { "$bytes": "<base64>" }, unpadded on the wire.
        _fixture.On("com.atproto.space.getLatestCommit", """
        {"commit":{"ver":1,
          "hash":{"$bytes":"AQID"},
          "ikm":{"$bytes":"BAUG"},
          "sig":{"$bytes":"BwgJ"},
          "mac":{"$bytes":"CgsM"},
          "rev":"3l6oveex3ii2l"}}
        """);

        var response = await Space_.GetLatestCommitAsync(Space, Repo);

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
        _fixture.On("com.atproto.space.listRepos", """
        {"repos":[{"did":"did:plc:z72i7hdynmk6r22z27h6tvur","rev":"3l6oveex3ii2l","hash":{"$bytes":"AQID"}}],
         "cursor":"next"}
        """);

        var response = await Space_.ListReposAsync(Space);

        Assert.Equal("next", response.Cursor);
        var repo = Assert.Single(response.Repos);
        Assert.Equal(Repo, repo.Did);
        Assert.Equal([1, 2, 3], repo.Hash);
    }

    [Fact]
    public async Task ListRepoOpsAsync_DistinguishesCreatesUpdatesAndDeletes()
    {
        _fixture.On("com.atproto.space.listRepoOps", $$"""
        {"ops":[
          {"rev":"3l6oveex3ii2a","collection":"com.example.n","rkey":"a","cid":"{{Cid1}}","prev":null,"value":{"text":"hi"} },
          {"rev":"3l6oveex3ii2b","collection":"com.example.n","rkey":"a","cid":"{{Cid2}}","prev":"{{Cid1}}"},
          {"rev":"3l6oveex3ii2c","collection":"com.example.n","rkey":"a","cid":null,"prev":"{{Cid2}}"}]}
        """);

        var response = await Space_.ListRepoOpsAsync(Space, Repo);

        var create = response.Ops[0].ToRepoOp();
        Assert.Null(create.Prev);
        Assert.Equal(Cid1, create.Cid);
        Assert.Equal("hi", response.Ops[0].Value!.Value.GetProperty("text").GetString());

        var update = response.Ops[1].ToRepoOp();
        Assert.Equal(Cid1, update.Prev);
        Assert.Equal(Cid2, update.Cid);

        var delete = response.Ops[2].ToRepoOp();
        Assert.Equal(Cid2, delete.Prev);
        Assert.Null(delete.Cid);
    }

    [Fact]
    public async Task ListRepoOpsAsync_OmitsTheCommitOnABackfillResponse()
    {
        // A commit arrives only at the head of the oplog; a paginated response carries a cursor
        // and no commit, which is what tells a syncer it has not caught up yet.
        _fixture.On("com.atproto.space.listRepoOps", """{"ops":[],"cursor":"more"}""");

        var response = await Space_.ListRepoOpsAsync(Space, Repo);

        Assert.Null(response.Commit);
        Assert.Equal("more", response.Cursor);
    }

    [Fact]
    public async Task EnumerateRecordsAsync_FollowsPaginationAndStops()
    {
        _fixture
            .On("com.atproto.space.listRecords", $$"""{"records":[{"collection":"com.example.n","rkey":"a","cid":"{{Cid1}}"}],"cursor":"next"}""")
            .On("com.atproto.space.listRecords", $$"""{"records":[{"collection":"com.example.n","rkey":"b","cid":"{{Cid2}}"}]}""");

        var records = new List<SpaceRecordView>();
        await foreach (var record in Space_.EnumerateRecordsAsync(Space, Repo))
            records.Add(record);

        Assert.Equal(["com.example.n/a", "com.example.n/b"], records.Select(r => r.Path));
    }

    [Fact]
    public async Task Enumerators_HostRepeatingItsCursor_StopAfterTheRepeat()
    {
        // Every page carries items and the same cursor. The loops these enumerators used to run
        // stopped only on an empty page, so such a host was asked for the same page forever.
        Assert.Equal(2, await CountRequestsAsync(
            "com.atproto.space.listSpaces",
            $$"""{"spaces":[{"uri":"{{Space}}"}],"cursor":"same"}""",
            () => Space_.EnumerateSpacesAsync()));
        Assert.Equal(2, await CountRequestsAsync(
            "com.atproto.space.listRepos",
            $$"""{"repos":[{"did":"{{Repo}}","rev":"3l6oveex3ii2l","hash":{"$bytes":"AQID"} }],"cursor":"same"}""",
            () => Space_.EnumerateReposAsync(Space)));
        Assert.Equal(2, await CountRequestsAsync(
            "com.atproto.space.listRecords",
            $$"""{"records":[{"collection":"com.example.n","rkey":"a","cid":"{{Cid1}}"}],"cursor":"same"}""",
            () => Space_.EnumerateRecordsAsync(Space, Repo)));
        Assert.Equal(2, await CountRequestsAsync(
            "com.atproto.space.listBlobs",
            $$"""{"cids":["{{Cid1}}"],"cursor":"same"}""",
            () => Space_.EnumerateBlobsAsync(Space, Repo)));
        Assert.Equal(2, await CountRequestsAsync(
            "com.atproto.simplespace.listMembers",
            $$"""{"members":[{"did":"{{Repo}}","read":true,"write":true}],"cursor":"same"}""",
            () => SimpleSpace.EnumerateMembersAsync(Space)));
    }

    /// <summary>
    /// Serves <paramref name="page"/> to every request to <paramref name="nsid"/> and counts the
    /// requests an enumeration makes, giving up after a few pages so a regression fails rather
    /// than hangs.
    /// </summary>
    private async Task<int> CountRequestsAsync<T>(string nsid, string page, Func<IAsyncEnumerable<T>> enumerate)
    {
        _fixture.On(nsid, page);

        var items = 0;
        await foreach (var _ in enumerate())
        {
            if (++items > 10)
                break;
        }

        return _fixture.To(nsid).Count();
    }

    // ── Writes ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateRecordAsync_PostsTheRecordAndOmitsUnsetOptions()
    {
        _fixture.On("com.atproto.space.createRecord", $$"""{"uri":"at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/space/com.atmoboards.forum/default/did:plc:z72i7hdynmk6r22z27h6tvur/com.example.n/a","cid":"{{Cid1}}"}""");

        var result = await Space_.CreateRecordAsync(
            Space, Repo, Nsid.Parse("com.example.n"), new { text = "hi" });

        var body = _fixture.To("com.atproto.space.createRecord").Single().JsonBody;
        Assert.Equal(Space.Value, body.GetProperty("space").GetString());
        Assert.Equal("hi", body.GetProperty("record").GetProperty("text").GetString());
        Assert.False(body.TryGetProperty("rkey", out _));
        Assert.False(body.TryGetProperty("validate", out _));

        Assert.Equal("com.example.n", result.Uri.Collection.Value);
    }

    [Fact]
    public async Task ApplyWritesAsync_TagsEachOperationWithItsUnionDiscriminator()
    {
        _fixture.On("com.atproto.space.applyWrites", """{"results":[]}""");

        await Space_.ApplyWritesAsync(Space, Repo,
        [
            new SpaceCreateOp { Collection = Nsid.Parse("com.example.n"), Value = new { text = "a" } },
            new SpaceUpdateOp { Collection = Nsid.Parse("com.example.n"), Rkey = RecordKey.Parse("b"), Value = new { text = "b" } },
            new SpaceDeleteOp { Collection = Nsid.Parse("com.example.n"), Rkey = RecordKey.Parse("c") },
        ]);

        var writes = _fixture.To("com.atproto.space.applyWrites").Single().JsonBody.GetProperty("writes");

        Assert.Equal("com.atproto.space.applyWrites#create", writes[0].GetProperty("$type").GetString());
        Assert.Equal("com.atproto.space.applyWrites#update", writes[1].GetProperty("$type").GetString());
        Assert.Equal("com.atproto.space.applyWrites#delete", writes[2].GetProperty("$type").GetString());
    }

    // ── simplespace ──────────────────────────────────────────────

    [Fact]
    public async Task CreateSpaceAsync_DefaultsBothPoliciesToAMemberListAndAppAccessToOpen()
    {
        // readPolicy, writePolicy, and appAccess are all required on the wire, so the defaults are
        // always sent rather than left for the server to assume.
        _fixture.On("com.atproto.simplespace.createSpace", $$"""{"uri":"{{Space}}"}""");

        await SimpleSpace.CreateSpaceAsync(Nsid.Parse("com.atmoboards.forum"));

        var body = _fixture.To("com.atproto.simplespace.createSpace").Single().JsonBody;
        Assert.Equal(
            SimpleSpaceTypes.MemberListPolicy, body.GetProperty("readPolicy").GetProperty("$type").GetString());
        Assert.Equal(
            SimpleSpaceTypes.MemberListPolicy, body.GetProperty("writePolicy").GetProperty("$type").GetString());
        Assert.Equal(
            SimpleSpaceTypes.Open, body.GetProperty("appAccess").GetProperty("$type").GetString());
        Assert.False(body.TryGetProperty("skey", out _));
        Assert.False(body.TryGetProperty("policy", out _));
    }

    [Fact]
    public async Task CreateSpaceAsync_SerializesEachPolicyUnderItsOwnName()
    {
        _fixture.On("com.atproto.simplespace.createSpace", $$"""{"uri":"{{Space}}"}""");

        await SimpleSpace.CreateSpaceAsync(
            Nsid.Parse("com.atmoboards.forum"),
            skey: RecordKey.Parse("default"),
            readPolicy: new ManagingAppPolicy { ManagingApp = "did:web:example.com#forum" },
            writePolicy: new PublicPolicy(),
            appAccess: new AllowListAppAccess { Allowed = ["https://app.example.com/client-metadata.json"] });

        var body = _fixture.To("com.atproto.simplespace.createSpace").Single().JsonBody;
        var readPolicy = body.GetProperty("readPolicy");
        Assert.Equal(SimpleSpaceTypes.ManagingAppPolicy, readPolicy.GetProperty("$type").GetString());
        Assert.Equal("did:web:example.com#forum", readPolicy.GetProperty("managingApp").GetString());
        Assert.Equal(SimpleSpaceTypes.PublicPolicy, body.GetProperty("writePolicy").GetProperty("$type").GetString());

        var appAccess = body.GetProperty("appAccess");
        Assert.Equal(SimpleSpaceTypes.AllowList, appAccess.GetProperty("$type").GetString());
        Assert.Equal(
            "https://app.example.com/client-metadata.json", appAccess.GetProperty("allowed")[0].GetString());
    }

    [Fact]
    public async Task GetSpaceAsync_DeserializesThePolicyUnions()
    {
        // The shape the reference getSpace serves since the read/write split.
        _fixture.On(
            "com.atproto.simplespace.getSpace",
            $$"""
            {"uri":"{{Space}}",
             "readPolicy":{"$type":"{{SimpleSpaceTypes.ManagingAppPolicy}}","managingApp":"did:web:example.com#forum"},
             "writePolicy":{"$type":"{{SimpleSpaceTypes.PublicPolicy}}"},
             "appAccess":{"$type":"{{SimpleSpaceTypes.AllowList}}",
                          "allowed":["https://app.example.com/client-metadata.json"]}
            }
            """);

        var response = await SimpleSpace.GetSpaceAsync(Space);

        var readPolicy = Assert.IsType<ManagingAppPolicy>(response.ReadPolicy);
        Assert.Equal("did:web:example.com#forum", readPolicy.ManagingApp);
        Assert.IsType<PublicPolicy>(response.WritePolicy);
        var appAccess = Assert.IsType<AllowListAppAccess>(response.AppAccess);
        Assert.Single(appAccess.Allowed);
    }

    [Fact]
    public async Task UpdateSpaceAsync_OmitsWhicheverPolicyWasLeftAlone()
    {
        _fixture.On("com.atproto.simplespace.updateSpace", "{}");

        await SimpleSpace.UpdateSpaceAsync(Space, writePolicy: new PublicPolicy());

        var body = _fixture.To("com.atproto.simplespace.updateSpace").Single().JsonBody;
        Assert.Equal(SimpleSpaceTypes.PublicPolicy, body.GetProperty("writePolicy").GetProperty("$type").GetString());
        Assert.False(body.TryGetProperty("readPolicy", out _));
        Assert.False(body.TryGetProperty("appAccess", out _));
        Assert.False(body.TryGetProperty("policy", out _));
    }

    [Fact]
    public async Task PutMemberAsync_SendsBothAccessFlags()
    {
        _fixture.On("com.atproto.simplespace.putMember", "{}");

        await SimpleSpace.PutMemberAsync(Space, Repo, read: true, write: false);

        var sent = _fixture.To("com.atproto.simplespace.putMember").Single();
        Assert.EndsWith("/xrpc/com.atproto.simplespace.putMember", sent.Path);
        var body = sent.JsonBody;
        Assert.Equal(Space.Value, body.GetProperty("space").GetString());
        Assert.Equal(Repo.Value, body.GetProperty("did").GetString());
        Assert.True(body.GetProperty("read").GetBoolean());
        Assert.False(body.GetProperty("write").GetBoolean());
    }

    [Fact]
    public async Task ListMembersAsync_ReadsEachMembersAccess()
    {
        _fixture.On("com.atproto.simplespace.listMembers", $$"""{"members":[{"did":"{{Repo}}","read":false,"write":true}]}""");

        var member = Assert.Single((await SimpleSpace.ListMembersAsync(Space)).Members);

        Assert.Equal(Repo, member.Did);
        Assert.False(member.Read);
        Assert.True(member.Write);
    }

    [Fact]
    public async Task CheckUserAccessAsync_SendsTheAccessKind()
    {
        _fixture.On("com.atproto.simplespace.checkUserAccess", """{"authorized":true}""");

        await SimpleSpace.CheckUserAccessAsync(Space, Repo, SimpleSpaceAccess.Write);

        var query = _fixture.To("com.atproto.simplespace.checkUserAccess").Single().Uri.Query;
        Assert.Contains($"user={Uri.EscapeDataString(Repo.Value)}", query, StringComparison.Ordinal);
        Assert.Contains("access=write", query, StringComparison.Ordinal);
        Assert.DoesNotContain("clientId", query, StringComparison.Ordinal);
    }
}
