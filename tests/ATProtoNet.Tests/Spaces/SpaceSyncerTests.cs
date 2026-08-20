using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Http;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;
using ATProtoNet.Spaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Spaces;

public class SpaceSyncerTests : IDisposable
{
    private static readonly SpaceUri _space =
        SpaceUri.Parse("at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/space/com.atmoboards.forum/default");

    private const string Repo = "did:plc:z72i7hdynmk6r22z27h6tvur";

    private readonly StubHost _host = new();
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;
    private readonly SpaceClient _client;
    private readonly AtProtoKey _key = AtProtoCrypto.GenerateP256Key();
    private readonly RecordingStore _store = new();

    public SpaceSyncerTests()
    {
        _httpClient = new HttpClient(_host) { BaseAddress = new Uri("https://repo.example.com/") };
        _xrpc = new XrpcClient(_httpClient, NullLogger.Instance, AtProtoJsonDefaults.Options);
        _xrpc.SetTokens("credential");
        _client = new SpaceClient(_xrpc);
    }

    private SpaceSyncer CreateSyncer() =>
        new(_space, _store, (_, _) => Task.FromResult(_key.ToDidKey()));

    private SignedSpaceCommit SignOver(string rev, params (string Collection, string Rkey, string Cid)[] records) =>
        SpaceRepoCommit.FromRecords(records).Sign(new SpaceCommitContext(_space, Repo, rev), _key);

    private static SpaceRepoRecord Record(string collection, string rkey, string text)
    {
        var value = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["$type"] = collection,
            ["text"] = text,
        });
        return SpaceRepoRecord.Create(collection, rkey, value);
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
        var commit = SignOver("3l6ov2", (record.Collection, record.Rkey, record.Cid));

        _host.Ops = OpsJson(commit, cursor: null, CreateOp("3l6ov2", "com.example.n", "a", record.Cid));

        var cursor = new SpaceRepoCursor(Repo);
        var result = await CreateSyncer().SyncRepoAsync(_client, cursor);

        Assert.Equal(SpaceSyncOutcome.UpToDate, result.Outcome);
        Assert.Equal("3l6ov2", cursor.Rev);
        Assert.Single(result.Ops);
        Assert.Single(_store.Applied);
        Assert.True(cursor.Commit.Matches(commit));
    }

    [Fact]
    public async Task SyncRepoAsync_SendsTheCursorsRevAsSince()
    {
        _host.Ops = OpsJson(commit: null, cursor: "more");

        await CreateSyncer().SyncRepoAsync(_client, new SpaceRepoCursor(Repo, "3l6ov1", default));

        Assert.Contains("since=3l6ov1", _host.LastQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncRepoAsync_WithoutACommit_ReportsPartialSoTheCallerContinues()
    {
        var record = Record("com.example.n", "a", "x");
        _host.Ops = OpsJson(commit: null, cursor: "more", CreateOp("3l6ov2", "com.example.n", "a", record.Cid));

        var cursor = new SpaceRepoCursor(Repo);
        var result = await CreateSyncer().SyncRepoAsync(_client, cursor);

        Assert.Equal(SpaceSyncOutcome.Partial, result.Outcome);
        Assert.Equal("3l6ov2", cursor.Rev);
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
            Repo, "3l6ov1", new SpaceRepoCommit().Add(record.Collection, record.Rkey, record.Cid).SetHash.GetState());

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
        var persisted = new SpaceRepoCursor(Repo, "3l6ov1", before.SetHash.GetState());

        var commit = SignOver(
            "3l6ov2",
            (first.Collection, first.Rkey, first.Cid),
            (second.Collection, second.Rkey, second.Cid));

        _host.Ops = OpsJson(commit, cursor: null, CreateOp("3l6ov2", "com.example.n", "b", second.Cid));

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
            "3l6ov3",
            (seen.Collection, seen.Rkey, seen.Cid),
            (missed.Collection, missed.Rkey, missed.Cid));

        _host.Ops = OpsJson(commit, cursor: null, CreateOp("3l6ov2", "com.example.n", "a", seen.Cid));
        _host.Car = SpaceRepoCar.Serialize(commit, [seen, missed]);

        var cursor = new SpaceRepoCursor(Repo);
        var result = await CreateSyncer().SyncRepoAsync(_client, cursor);

        Assert.Equal(SpaceSyncOutcome.Recovered, result.Outcome);
        Assert.Equal("3l6ov3", cursor.Rev);
        Assert.True(cursor.Commit.Matches(commit));

        var replaced = Assert.Single(_store.Replaced);
        Assert.Equal(2, replaced.Records.Count);
    }

    [Fact]
    public async Task SyncRepoAsync_WhenTheOplogCannotServeSince_RecoversInFull()
    {
        // The oplog is a transport optimization with no history guarantee: a host may compact
        // it, and it does not survive account migration.
        var record = Record("com.example.n", "a", "x");
        var commit = SignOver("3l6ov3", (record.Collection, record.Rkey, record.Cid));

        _host.OpsStatus = HttpStatusCode.BadRequest;
        _host.Ops = """{"error":"InvalidRequest","message":"since is no longer available"}""";
        _host.Car = SpaceRepoCar.Serialize(commit, [record]);

        var cursor = new SpaceRepoCursor(Repo, "3l6ov0", default);
        var result = await CreateSyncer().SyncRepoAsync(_client, cursor);

        Assert.Equal(SpaceSyncOutcome.Recovered, result.Outcome);
        Assert.Equal("3l6ov3", cursor.Rev);
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

        await Assert.ThrowsAsync<AtProtoHttpException>(
            () => CreateSyncer().SyncRepoAsync(_client, new SpaceRepoCursor(Repo, "3l6ov0", default)));
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
            Repo, "3l6ov1", new SpaceRepoCommit().Add(record.Collection, record.Rkey, record.Cid).SetHash.GetState());

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
            .Sign(new SpaceCommitContext(_space, Repo, "3l6ov2"), attacker);

        _host.Ops = OpsJson(forged, cursor: null, CreateOp("3l6ov2", "com.example.n", "a", record.Cid));

        await Assert.ThrowsAsync<SpaceRepoVerificationException>(
            () => CreateSyncer().SyncRepoAsync(_client, new SpaceRepoCursor(Repo)));
    }

    [Fact]
    public async Task RecoverAsync_WithACarForAnotherSpace_Throws()
    {
        var other = SpaceUri.Create(_space.Authority, _space.SpaceType, "other");
        var record = Record("com.example.n", "a", "x");
        var commit = SpaceRepoCommit
            .FromRecords([(record.Collection, record.Rkey, record.Cid)])
            .Sign(new SpaceCommitContext(other, Repo, "3l6ov3"), _key);

        _host.Car = SpaceRepoCar.Serialize(commit, [record]);

        await Assert.ThrowsAsync<SpaceRepoVerificationException>(
            () => CreateSyncer().RecoverAsync(_client, new SpaceRepoCursor(Repo)));

        Assert.Empty(_store.Replaced);
    }

    // ── Cursor persistence ───────────────────────────────────────

    [Fact]
    public void GetState_RoundTripsThroughTheCursorConstructor()
    {
        var record = Record("com.example.n", "a", "x");
        var cursor = new SpaceRepoCursor(Repo);
        cursor.Commit.Add(record.Collection, record.Rkey, record.Cid);

        var restored = new SpaceRepoCursor(Repo, "3l6ov1", cursor.GetState());

        Assert.Equal(cursor.Commit.Digest(), restored.Commit.Digest());
    }

    public void Dispose()
    {
        _key.Dispose();
        _xrpc.Dispose();
        _httpClient.Dispose();
        _host.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class RecordingStore : ISpaceRepoStore
    {
        public List<SpaceRepoOpEntry> Applied { get; } = [];

        public List<VerifiedSpaceRepo> Replaced { get; } = [];

        public List<string> Dropped { get; } = [];

        public Task ApplyAsync(SpaceUri space, string repo, SpaceRepoOpEntry op, CancellationToken cancellationToken)
        {
            Applied.Add(op);
            return Task.CompletedTask;
        }

        public Task ReplaceAsync(
            SpaceUri space, string repo, VerifiedSpaceRepo contents, CancellationToken cancellationToken)
        {
            Replaced.Add(contents);
            return Task.CompletedTask;
        }

        public Task DropAsync(SpaceUri space, string repo, CancellationToken cancellationToken)
        {
            Dropped.Add(repo);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHost : HttpMessageHandler
    {
        public string Ops { get; set; } = """{"ops":[]}""";

        public HttpStatusCode OpsStatus { get; set; } = HttpStatusCode.OK;

        public byte[]? Car { get; set; }

        public HttpStatusCode CarStatus { get; set; } = HttpStatusCode.OK;

        public string CarError { get; set; } = "{}";

        public string LastQuery { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastQuery = request.RequestUri!.Query;

            if (request.RequestUri.AbsolutePath.EndsWith("listRepoOps", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(OpsStatus)
                {
                    Content = new StringContent(Ops, Encoding.UTF8, "application/json"),
                });
            }

            if (CarStatus != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(CarStatus)
                {
                    Content = new StringContent(CarError, Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Car ?? []),
            });
        }
    }
}
