using System.Net;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;
using ATProtoNet.Spaces;
using Microsoft.Extensions.Logging.Abstractions;

using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Spaces;

public class SpaceSyncerTests : IDisposable
{
    private static readonly SpaceUri _space =
        SpaceUri.Parse("at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/space/com.atmoboards.forum/default");

    private static readonly Did Repo = Did.Parse("did:plc:z72i7hdynmk6r22z27h6tvur");

    private readonly StubHost _host = new();
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;
    private readonly SpaceClient _client;
    private readonly AtProtoKey _key = AtProtoCrypto.GenerateP256Key();
    private readonly RecordingStore _store = new();

    public SpaceSyncerTests()
    {
        _httpClient = new HttpClient(_host.Stub) { BaseAddress = new Uri("https://repo.example.com/") };
        _xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        _xrpc.SetTokens("credential");
        _client = new SpaceClient(_xrpc);
    }

    private readonly StubDidResolver _resolver = new();

    private SpaceSyncer CreateSyncer()
    {
        _resolver.PublishAccount(Repo.Value, _key);
        return new(_space, _store, _resolver);
    }

    private SignedSpaceCommit SignOver(string rev, params (Nsid Collection, RecordKey Rkey, Cid Cid)[] records) =>
        SpaceRepoCommit.FromRecords(records).Sign(new SpaceCommitContext(_space, Repo, Tid.Parse(rev)), _key);

    private static SpaceRepoRecord Record(string collection, string rkey, string text)
    {
        var value = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["$type"] = collection,
            ["text"] = text,
        });
        return SpaceRepoRecord.Create(Nsid.Parse(collection), RecordKey.Parse(rkey), value);
    }

    private static string OpsJson(SignedSpaceCommit? commit, string? cursor, params string[] ops)
    {
        var parts = new List<string> { $"\"ops\":[{string.Join(',', ops)}]" };
        if (commit is not null)
            parts.Add($"\"commit\":{JsonSerializer.Serialize(commit, AtProtoJsonDefaults.Options)}");
        if (cursor is not null)
            parts.Add($"\"cursor\":\"{cursor}\"");
        return "{" + string.Join(',', parts) + "}";
    }

    private static string CreateOp(string rev, string collection, string rkey, string cid) =>
        $$"""
        {"rev":"{{rev}}","collection":"{{collection}}","rkey":"{{rkey}}","cid":"{{cid}}",
         "prev":null,"value":{"text":"x"}
        }
        """;

    // ── Incremental sync ─────────────────────────────────────────

    [Fact]
    public async Task SyncRepoAsync_AppliesOpsAndReportsUpToDateWhenDigestsAgree()
    {
        var record = Record("com.example.n", "a", "x");
        var commit = SignOver("3l6oveex3ii24", (record.Collection, record.Rkey, record.Cid));

        _host.Ops = OpsJson(commit, cursor: null, CreateOp("3l6oveex3ii24", "com.example.n", "a", record.Cid));

        var cursor = new SpaceRepoCursor(Repo);
        var result = await CreateSyncer().SyncRepoAsync(_client, cursor);

        Assert.Equal(SpaceSyncOutcome.UpToDate, result.Outcome);
        Assert.Equal("3l6oveex3ii24", cursor.Rev);
        Assert.Single(result.Ops);
        Assert.Single(_store.Applied);
        Assert.True(cursor.Commit.Matches(commit));
    }

    [Fact]
    public async Task SyncRepoAsync_SendsTheCursorsRevAsSince()
    {
        _host.Ops = OpsJson(commit: null, cursor: "more");

        await CreateSyncer().SyncRepoAsync(_client, new SpaceRepoCursor(Repo, Tid.Parse("3l6oveex3ii23"), default));

        Assert.Contains("since=3l6oveex3ii23", _host.Stub.Last.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncRepoAsync_WithoutACommit_ReportsPartialSoTheCallerContinues()
    {
        var record = Record("com.example.n", "a", "x");
        _host.Ops = OpsJson(commit: null, cursor: "more", CreateOp("3l6oveex3ii24", "com.example.n", "a", record.Cid));

        var cursor = new SpaceRepoCursor(Repo);
        var result = await CreateSyncer().SyncRepoAsync(_client, cursor);

        Assert.Equal(SpaceSyncOutcome.Partial, result.Outcome);
        Assert.Equal("3l6oveex3ii24", cursor.Rev);
        Assert.Null(result.Commit);
    }

    [Fact]
    public async Task SyncRepoAsync_WithoutACommitButWithAContinuation_ReportsPartialEvenWithNoOps()
    {
        // No operations in a page is not the same as no page left. A cursor is the host saying
        // there is more to read, and that is what `Partial` is for.
        _host.Ops = OpsJson(commit: null, cursor: "more");

        var result = await CreateSyncer().SyncRepoAsync(_client, new SpaceRepoCursor(Repo));

        Assert.Equal(SpaceSyncOutcome.Partial, result.Outcome);
        Assert.Empty(result.Ops);
    }

    [Fact]
    public async Task SyncRepoAsync_ForAnAccountThatHasWrittenNothing_ReportsNoRepoRatherThanPartial()
    {
        // A member who has never written to the space has no repo state, and the commit is built
        // from that state — so the oplog answers with an empty page rather than refusing the
        // read. Nothing was applied and no cursor was offered: a caller looping on `Partial`,
        // which is documented as "sync again to continue", would spin on this repo forever.
        _host.Ops = """{"ops":[]}""";

        var cursor = new SpaceRepoCursor(Repo);
        var result = await CreateSyncer().SyncRepoAsync(_client, cursor);

        Assert.Equal(SpaceSyncOutcome.NoRepo, result.Outcome);
        Assert.Empty(result.Ops);
        Assert.Null(result.Commit);
        Assert.Null(cursor.Rev);

        // The same answer the refused read gives, and reached without a full download: the copy
        // holds nothing, so there is nothing to repair or drop.
        Assert.Empty(_store.Applied);
        Assert.Empty(_store.Replaced);
        Assert.Empty(_store.Dropped);
    }

    [Fact]
    public async Task SyncRepoAsync_WhenAHeldRepoHasNothingToCommit_RecoversRatherThanAssumingItIsGone()
    {
        // Standing at a revision is the other case: the host reports no state for a repo the
        // caller holds a copy of. Only `getRepo` answers that definitively, and here it says the
        // repo is gone — so the stale copy goes with it instead of being kept forever.
        _host.Ops = """{"ops":[]}""";
        _host.CarStatus = HttpStatusCode.BadRequest;
        _host.CarError = """{"error":"RepoNotFound","message":"no repo in this space"}""";

        var record = Record("com.example.n", "a", "x");
        var cursor = new SpaceRepoCursor(
            Repo, Tid.Parse("3l6oveex3ii23"), new SpaceRepoCommit().Add(record.Collection, record.Rkey, record.Cid).SetHash.GetState());

        var result = await CreateSyncer().SyncRepoAsync(_client, cursor);

        Assert.Equal(SpaceSyncOutcome.NoRepo, result.Outcome);
        Assert.Equal(Repo, Assert.Single(_store.Dropped));
        Assert.Null(cursor.Rev);
        Assert.True(cursor.Commit.SetHash.IsEmpty);
    }

    [Fact]
    public async Task SyncRepoAsync_ResumesFromAPersistedCursorWithoutRefetching()
    {
        // A syncer restarting mid-stream restores its set hash from storage rather than
        // re-reading the repo, which is the whole point of persisting the state.
        var first = Record("com.example.n", "a", "x");
        var second = Record("com.example.n", "b", "x");

        var before = new SpaceRepoCommit().Add(first.Collection, first.Rkey, first.Cid);
        var persisted = new SpaceRepoCursor(Repo, Tid.Parse("3l6oveex3ii23"), before.SetHash.GetState());

        var commit = SignOver(
            "3l6oveex3ii24",
            (first.Collection, first.Rkey, first.Cid),
            (second.Collection, second.Rkey, second.Cid));

        _host.Ops = OpsJson(commit, cursor: null, CreateOp("3l6oveex3ii24", "com.example.n", "b", second.Cid));

        var result = await CreateSyncer().SyncRepoAsync(_client, persisted);

        Assert.Equal(SpaceSyncOutcome.UpToDate, result.Outcome);
    }

    // ── Divergence and recovery ──────────────────────────────────

    [Fact]
    public async Task SyncRepoAsync_WhenTheDigestDisagrees_RecoversInFull()
    {
        // The op stream omits a record the commit accounts for — a dropped write. Nothing in
        // the oplog reveals that; only the set hash comparison does.
        var seen = Record("com.example.n", "a", "x");
        var missed = Record("com.example.n", "b", "x");

        var commit = SignOver(
            "3l6oveex3ii25",
            (seen.Collection, seen.Rkey, seen.Cid),
            (missed.Collection, missed.Rkey, missed.Cid));

        _host.Ops = OpsJson(commit, cursor: null, CreateOp("3l6oveex3ii24", "com.example.n", "a", seen.Cid));
        _host.Car = SpaceRepoCar.Serialize(commit, [seen, missed]);

        var cursor = new SpaceRepoCursor(Repo);
        var result = await CreateSyncer().SyncRepoAsync(_client, cursor);

        Assert.Equal(SpaceSyncOutcome.Recovered, result.Outcome);
        Assert.Equal("3l6oveex3ii25", cursor.Rev);
        Assert.True(cursor.Commit.Matches(commit));

        var replaced = Assert.Single(_store.Replaced);
        Assert.Equal(2, replaced.Records.Count);
    }

    [Fact]
    public async Task RecoverAsync_AuthorPublishingASpaceKey_VerifiesTheCommitAgainstTheAccountKey()
    {
        // A commit is signed with the author's repo key, #atproto. An author that is also a space
        // authority may publish a #atproto_space key too; that one signs credentials, not commits.
        using var spaceKey = AtProtoCrypto.GenerateP256Key();
        _resolver.Publish(Repo.Value, new DidDocument
        {
            Id = Repo,
            VerificationMethod =
            [
                new VerificationMethod { Id = $"{Repo}#atproto_space", Type = "Multikey", PublicKeyMultibase = spaceKey.ToMultikey() },
                new VerificationMethod { Id = $"{Repo}#atproto", Type = "Multikey", PublicKeyMultibase = _key.ToMultikey() },
            ],
        });
        var record = Record("com.example.n", "a", "x");
        var commit = SignOver("3l6oveex3ii25", (record.Collection, record.Rkey, record.Cid));
        _host.Car = SpaceRepoCar.Serialize(commit, [record]);

        var result = await new SpaceSyncer(_space, _store, _resolver).RecoverAsync(_client, new SpaceRepoCursor(Repo));

        Assert.Equal(SpaceSyncOutcome.Recovered, result.Outcome);
    }

    [Fact]
    public async Task SyncRepoAsync_WhenTheOplogCannotServeSince_RecoversInFull()
    {
        // The oplog is a transport optimization with no history guarantee: a host may compact
        // it, and it does not survive account migration.
        var record = Record("com.example.n", "a", "x");
        var commit = SignOver("3l6oveex3ii25", (record.Collection, record.Rkey, record.Cid));

        _host.OpsStatus = HttpStatusCode.BadRequest;
        _host.Ops = """{"error":"InvalidRequest","message":"since is no longer available"}""";
        _host.Car = SpaceRepoCar.Serialize(commit, [record]);

        var cursor = new SpaceRepoCursor(Repo, Tid.Parse("3l6oveex3ii22"), default);
        var result = await CreateSyncer().SyncRepoAsync(_client, cursor);

        Assert.Equal(SpaceSyncOutcome.Recovered, result.Outcome);
        Assert.Equal("3l6oveex3ii25", cursor.Rev);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task SyncRepoAsync_WhenTheHostIsThrottledOrBroken_PropagatesRatherThanRedownloading(
        HttpStatusCode status)
    {
        // A full repo download in response to a 429 would make the throttling worse, and one in
        // response to a 401 would just fail again. These belong to the caller's retry policy.
        _host.OpsStatus = status;
        _host.Ops = """{"error":"UpstreamFailure","message":"try again"}""";
        _host.Car = [];

        await Assert.ThrowsAnyAsync<XrpcException>(
            () => CreateSyncer().SyncRepoAsync(_client, new SpaceRepoCursor(Repo, Tid.Parse("3l6oveex3ii22"), default)));
    }

    [Fact]
    public async Task SyncRepoAsync_WhenTheAccountHoldsNoRepo_ReportsNoRepoWithoutRecovering()
    {
        _host.OpsStatus = HttpStatusCode.BadRequest;
        _host.Ops = """{"error":"RepoNotFound","message":"no repo in this space"}""";

        var result = await CreateSyncer().SyncRepoAsync(_client, new SpaceRepoCursor(Repo));

        Assert.Equal(SpaceSyncOutcome.NoRepo, result.Outcome);
        Assert.Empty(_store.Replaced);
        Assert.Empty(_store.Dropped);
    }

    [Fact]
    public async Task RecoverAsync_WhenTheRepoIsGone_DropsTheLocalCopy()
    {
        _host.CarStatus = HttpStatusCode.BadRequest;
        _host.CarError = """{"error":"RepoDeactivated","message":"account deactivated"}""";

        var record = Record("com.example.n", "a", "x");
        var cursor = new SpaceRepoCursor(
            Repo, Tid.Parse("3l6oveex3ii23"), new SpaceRepoCommit().Add(record.Collection, record.Rkey, record.Cid).SetHash.GetState());

        var result = await CreateSyncer().RecoverAsync(_client, cursor);

        Assert.Equal(SpaceSyncOutcome.NoRepo, result.Outcome);
        Assert.Equal(Repo, Assert.Single(_store.Dropped));
        Assert.Null(cursor.Rev);
        Assert.True(cursor.Commit.SetHash.IsEmpty);
    }

    // ── Verification ─────────────────────────────────────────────

    [Fact]
    public async Task SyncRepoAsync_WithACommitSignedByAnotherKey_Throws()
    {
        using var attacker = AtProtoCrypto.GenerateP256Key();
        var record = Record("com.example.n", "a", "x");
        var forged = SpaceRepoCommit
            .FromRecords([(record.Collection, record.Rkey, record.Cid)])
            .Sign(new SpaceCommitContext(_space, Repo, Tid.Parse("3l6oveex3ii24")), attacker);

        _host.Ops = OpsJson(forged, cursor: null, CreateOp("3l6oveex3ii24", "com.example.n", "a", record.Cid));

        await Assert.ThrowsAsync<SpaceRepoVerificationException>(
            () => CreateSyncer().SyncRepoAsync(_client, new SpaceRepoCursor(Repo)));

        // One refetch before the signature is declared bad; the key it found was the same.
        Assert.Equal(1, _resolver.Refreshes);
    }

    [Theory]
    [InlineData(null, null)]                                                  // no #atproto entry
    [InlineData("EcdsaSecp256k1VerificationKey2019", "z1111111111111111111")] // an entry that does not decode
    public async Task SyncRepoAsync_AuthorPublishingNoUsableKey_IsRefusedWithoutARefresh(string? type, string? multibase)
    {
        // Only a failed signature refetches the author's document; a document with no key to
        // verify against is refused as it stands.
        _resolver.Publish(Repo.Value, new DidDocument
        {
            Id = Repo,
            VerificationMethod = type is null ? [] : [new VerificationMethod { Id = $"{Repo}#atproto", Type = type, PublicKeyMultibase = multibase }],
        });
        var record = Record("com.example.n", "a", "x");
        var commit = SignOver("3l6oveex3ii24", (record.Collection, record.Rkey, record.Cid));
        _host.Ops = OpsJson(commit, cursor: null, CreateOp("3l6oveex3ii24", "com.example.n", "a", record.Cid));

        var ex = await Assert.ThrowsAsync<SpaceRepoVerificationException>(
            () => new SpaceSyncer(_space, _store, _resolver).SyncRepoAsync(_client, new SpaceRepoCursor(Repo)));

        Assert.Contains("publishes no usable AT Protocol signing key", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, _resolver.Refreshes);
    }

    [Fact]
    public async Task SyncRepoAsync_WithACommitSignedByARotatedKey_VerifiesAfterOneRefresh()
    {
        using var rotated = AtProtoCrypto.GenerateP256Key();
        var syncer = CreateSyncer();
        _resolver.Rotate(Repo.Value, StubDidResolver.AccountDocument(Repo.Value, rotated));

        var record = Record("com.example.n", "a", "x");
        var commit = SpaceRepoCommit
            .FromRecords([(record.Collection, record.Rkey, record.Cid)])
            .Sign(new SpaceCommitContext(_space, Repo, Tid.Parse("3l6oveex3ii24")), rotated);
        _host.Ops = OpsJson(commit, cursor: null, CreateOp("3l6oveex3ii24", "com.example.n", "a", record.Cid));

        var result = await syncer.SyncRepoAsync(_client, new SpaceRepoCursor(Repo));

        Assert.Equal(SpaceSyncOutcome.UpToDate, result.Outcome);
        Assert.Equal(1, _resolver.Refreshes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoverAsync_ARepoOverMaxRepoSize_IsRefusedAndTheCopyKept(bool declared)
    {
        // Declared or not, a download past the limit stops being read: the whole repo is held in
        // memory to be verified, so the host must not choose how much that is.
        var record = Record("com.example.n", "a", "x");
        var commit = SignOver("3l6oveex3ii25", (record.Collection, record.Rkey, record.Cid));
        _host.Car = SpaceRepoCar.Serialize(commit, [record]);
        _host.UndeclaredLength = !declared;
        var syncer = CreateSyncer();
        syncer.MaxRepoSize = _host.Car.Length - 1;

        var ex = await Assert.ThrowsAsync<SpaceRepoVerificationException>(
            () => syncer.RecoverAsync(_client, new SpaceRepoCursor(Repo)));

        Assert.Contains("MaxRepoSize", ex.Message, StringComparison.Ordinal);
        Assert.Empty(_store.Replaced);
        Assert.Empty(_store.Dropped);
    }

    [Fact]
    public async Task RecoverAsync_ARepoExactlyAtMaxRepoSize_IsRecovered()
    {
        var record = Record("com.example.n", "a", "x");
        var commit = SignOver("3l6oveex3ii25", (record.Collection, record.Rkey, record.Cid));
        _host.Car = SpaceRepoCar.Serialize(commit, [record]);
        _host.UndeclaredLength = true;
        var syncer = CreateSyncer();
        syncer.MaxRepoSize = _host.Car.Length;

        var result = await syncer.RecoverAsync(_client, new SpaceRepoCursor(Repo));

        Assert.Equal(SpaceSyncOutcome.Recovered, result.Outcome);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void MaxRepoSize_OutOfRange_Throws(long value)
    {
        var syncer = CreateSyncer();

        Assert.Throws<ArgumentOutOfRangeException>(() => syncer.MaxRepoSize = value);
    }

    [Fact]
    public async Task RecoverAsync_WithACarForAnotherSpace_Throws()
    {
        var other = SpaceUri.Create(_space.Authority, _space.SpaceType, RecordKey.Parse("other"));
        var record = Record("com.example.n", "a", "x");
        var commit = SpaceRepoCommit
            .FromRecords([(record.Collection, record.Rkey, record.Cid)])
            .Sign(new SpaceCommitContext(other, Repo, Tid.Parse("3l6oveex3ii25")), _key);

        _host.Car = SpaceRepoCar.Serialize(commit, [record]);

        await Assert.ThrowsAsync<SpaceRepoVerificationException>(
            () => CreateSyncer().RecoverAsync(_client, new SpaceRepoCursor(Repo)));

        Assert.Empty(_store.Replaced);
    }

    // ── Catch-up from a spaceRev checkpoint ─────────────────────

    private static string RepoJson(string did, string repoRev, string spaceRev) =>
        $$"""{"did":"{{did}}","repoRev":"{{repoRev}}","hash":{"$bytes":"AQID"},"spaceRev":"{{spaceRev}}"}""";

    [Fact]
    public async Task ListChangedReposAsync_ResumesFromTheCheckpointAndWalksUntilAnEmptyPage()
    {
        // The server pages by exclusive spaceRev checkpoint, returns the last one even on a short page,
        // and omits the cursor on the empty page that ends the walk.
        var cursors = new List<string?>();
        _host.Stub.On("com.atproto.space.listRepos", request =>
        {
            var cursor = System.Web.HttpUtility.ParseQueryString(request.Uri.Query)["cursor"];
            cursors.Add(cursor);
            return HttpStub.JsonResponse(cursor switch
            {
                "3l6oveex3ii2c" => $$"""{"repos":[{{RepoJson("did:plc:a", "3l6oveex3ii2x", "3l6oveex3ii2d")}},{{RepoJson("did:plc:b", "3l6oveex3ii2y", "3l6oveex3ii2e")}}],"cursor":"3l6oveex3ii2e"}""",
                "3l6oveex3ii2e" => $$"""{"repos":[{{RepoJson("did:plc:a", "3l6oveex3ii2z", "3l6oveex3ii2f")}}],"cursor":"3l6oveex3ii2f"}""",
                _ => """{"repos":[]}""",
            });
        });

        var changed = new List<SpaceRepoView>();
        await foreach (var repo in CreateSyncer().ListChangedReposAsync(_client, Tid.Parse("3l6oveex3ii2c"), pageSize: 2))
            changed.Add(repo);

        Assert.Equal(["3l6oveex3ii2c", "3l6oveex3ii2e", "3l6oveex3ii2f"], cursors);
        Assert.Equal(["did:plc:a", "did:plc:b", "did:plc:a"], changed.Select(r => r.Did.Value));
        Assert.Equal("3l6oveex3ii2f", changed[^1].SpaceRev.Value);
    }

    [Fact]
    public async Task ListChangedReposAsync_WithoutACheckpoint_ListsTheWholeWriterSet()
    {
        _host.Stub.On("com.atproto.space.listRepos", request =>
            HttpStub.JsonResponse(request.Uri.Query.Contains("cursor=", StringComparison.Ordinal)
                ? """{"repos":[]}"""
                : $$"""{"repos":[{{RepoJson("did:plc:a", "3l6oveex3ii2x", "3l6oveex3ii2d")}}],"cursor":"3l6oveex3ii2d"}"""));

        var changed = new List<SpaceRepoView>();
        await foreach (var repo in CreateSyncer().ListChangedReposAsync(_client, checkpoint: null))
            changed.Add(repo);

        Assert.Equal("did:plc:a", Assert.Single(changed).Did);
    }

    private static NotifyWriteRequest Forwarded(string? spaceRev, string? prevSpaceRev) => new()
    {
        Space = _space,
        Repo = Repo,
        RepoRev = Tid.Parse("3l6oveex3ii2l"),
        Hash = [1],
        SpaceRev = spaceRev is null ? null : Tid.Parse(spaceRev),
        PrevSpaceRev = prevSpaceRev is null ? null : Tid.Parse(prevSpaceRev),
    };

    [Theory]
    [InlineData("3l6oveex3ii2c", "3l6oveex3ii2d", "3l6oveex3ii2c", false)]  // directly follows the checkpoint
    [InlineData(null, "3l6oveex3ii2d", null, false)]                        // the space's first update, from nothing
    [InlineData("3l6oveex3ii2c", "3l6oveex3ii2f", "3l6oveex3ii2e", true)]   // notifications between were missed
    [InlineData(null, "3l6oveex3ii2f", "3l6oveex3ii2e", true)]              // nothing held, but the space has history
    [InlineData("3l6oveex3ii2e", "3l6oveex3ii2d", "3l6oveex3ii2c", false)]  // out of order behind the checkpoint: covered
    [InlineData("3l6oveex3ii2d", "3l6oveex3ii2d", "3l6oveex3ii2c", false)]  // duplicate: covered
    [InlineData("3l6oveex3ii2c", "3l6oveex3ii2d", null, true)]              // the space was reset behind the checkpoint
    public void NeedsCatchUp_ComparesTheNotificationsChainToTheCheckpoint(string? checkpoint, string spaceRev, string? prev, bool expected) =>
        Assert.Equal(expected, SpaceSyncer.NeedsCatchUp(checkpoint is null ? null : Tid.Parse(checkpoint), Forwarded(spaceRev, prev)));

    [Fact]
    public void NeedsCatchUp_NotificationWithoutASpaceRev_SaysNothingAboutTheSequence() =>
        Assert.False(SpaceSyncer.NeedsCatchUp(Tid.Parse("3l6oveex3ii2c"), Forwarded(null, null)));

    // ── Cursor persistence───────────────────────────────────────

    [Fact]
    public void GetState_RoundTripsThroughTheCursorConstructor()
    {
        var record = Record("com.example.n", "a", "x");
        var cursor = new SpaceRepoCursor(Repo);
        cursor.Commit.Add(record.Collection, record.Rkey, record.Cid);

        var restored = new SpaceRepoCursor(Repo, Tid.Parse("3l6oveex3ii23"), cursor.GetState());

        Assert.Equal(cursor.Commit.Digest(), restored.Commit.Digest());
    }

    public void Dispose()
    {
        _key.Dispose();
        _httpClient.Dispose();
        _host.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class RecordingStore : ISpaceRepoStore
    {
        public List<SpaceRepoOpEntry> Applied { get; } = [];

        public List<VerifiedSpaceRepo> Replaced { get; } = [];

        public List<Did> Dropped { get; } = [];

        public Task ApplyAsync(SpaceUri space, Did repo, SpaceRepoOpEntry op, CancellationToken cancellationToken)
        {
            Applied.Add(op);
            return Task.CompletedTask;
        }

        public Task ReplaceAsync(
            SpaceUri space, Did repo, VerifiedSpaceRepo contents, CancellationToken cancellationToken)
        {
            Replaced.Add(contents);
            return Task.CompletedTask;
        }

        public Task DropAsync(SpaceUri space, Did repo, CancellationToken cancellationToken)
        {
            Dropped.Add(repo);
            return Task.CompletedTask;
        }
    }

    /// <summary>A space host on an <see cref="HttpStub"/>, answering from what the test last set.</summary>
    private sealed class StubHost : IDisposable
    {
        public StubHost()
        {
            Stub.On("com.atproto.space.listRepoOps", _ => HttpStub.JsonResponse(Ops, OpsStatus));
            Stub.Fallback(_ => CarStatus != HttpStatusCode.OK
                ? HttpStub.JsonResponse(CarError, CarStatus)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = UndeclaredLength ? new UnsizedContent(Car ?? []) : new ByteArrayContent(Car ?? []),
                });
        }

        public HttpStub Stub { get; } = new();

        public string Ops { get; set; } = """{"ops":[]}""";

        public HttpStatusCode OpsStatus { get; set; } = HttpStatusCode.OK;

        public byte[]? Car { get; set; }

        public HttpStatusCode CarStatus { get; set; } = HttpStatusCode.OK;

        public string CarError { get; set; } = "{}";

        /// <summary>Sends the CAR without a <c>Content-Length</c>, as a chunked response would.</summary>
        public bool UndeclaredLength { get; set; }

        public void Dispose() => Stub.Dispose();
    }

    /// <summary>A body whose length is not known up front.</summary>
    private sealed class UnsizedContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
