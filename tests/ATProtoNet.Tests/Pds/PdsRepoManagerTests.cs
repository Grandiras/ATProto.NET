using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Pds;
using ATProtoNet.Repo;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Pds;

public sealed class PdsRepoManagerTests
{
    private readonly PdsOptions _options = new()
    {
        Hostname = "test.local",
        PublicUrl = "https://test.local",
        OpenRegistration = true,
        AvailableUserDomains = ["test.local"],
    };

    private readonly InMemoryAccountStore _accounts = new();
    private readonly InMemoryRepoStore _repos = new();
    private readonly InMemoryRepoCommitStore _commits = new();
    private readonly PdsSequencer _sequencer = new();

    private PdsRepoManager CreateManager() =>
        new(_accounts, _repos, _commits, _sequencer, _options);

    private PdsService CreateService() =>
        new(_accounts, _repos, new PdsSessionService(_options), _options,
            CreateManager(), new PdsIdentityService(_options, _accounts));

    private static JsonElement Record(string text) =>
        JsonSerializer.Deserialize<JsonElement>(
            $$"""{"$type":"app.bsky.feed.post","text":{{JsonSerializer.Serialize(text)}},"createdAt":"2026-07-25T00:00:00Z"}""");

    private async Task<string> CreateAccountAsync(PdsService pds, string handle = "alice.test.local")
        => (await pds.CreateAccountAsync(handle, $"{handle}@test.local", "password123")).Did;

    // ── Identity ─────────────────────────────────────────────

    [Fact]
    public async Task CreateAccountAsync_MintsADidDerivedFromASignedGenesisOperation()
    {
        var did = await CreateAccountAsync(CreateService());

        Assert.StartsWith("did:plc:", did);
        Assert.Equal(24, did["did:plc:".Length..].Length);
    }

    [Fact]
    public async Task CreateAccountAsync_StoresARotationKeyDistinctFromTheSigningKey()
    {
        var did = await CreateAccountAsync(CreateService());
        var account = await _accounts.GetByDidAsync(did);

        Assert.NotNull(account!.RotationKey);
        Assert.NotEqual(account.SigningKey, account.RotationKey);
    }

    [Fact]
    public async Task CreateAccountAsync_CreatesAnEmptySignedRepo()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);

        var head = await pds.RepoManager!.GetHeadAsync(did);

        Assert.NotNull(head);
        Assert.StartsWith("bafyrei", head!.CommitCid);
        Assert.NotEmpty(head.Rev);
    }

    // ── Commits ──────────────────────────────────────────────

    [Fact]
    public async Task CreateRecordAsync_AdvancesTheRepoRevision()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);

        var before = await pds.RepoManager!.GetHeadAsync(did);
        await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("hello"));
        var after = await pds.RepoManager.GetHeadAsync(did);

        Assert.NotEqual(before!.Rev, after!.Rev);
        Assert.True(string.CompareOrdinal(after.Rev, before.Rev) > 0);
        Assert.NotEqual(before.CommitCid, after.CommitCid);
    }

    [Fact]
    public async Task CreateRecordAsync_ReturnsARealCidV1()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);

        var record = Record("hello");
        var result = await pds.CreateRecordAsync(did, "app.bsky.feed.post", record);

        // The CID must be the true content address of the record's DAG-CBOR encoding, not a
        // hash-shaped placeholder — this is what a relay recomputes and compares.
        var expected = CidComputation.ComputeForDagCbor(DagCborEncoder.Encode(record));
        Assert.Equal(expected.Value, result.Cid);
    }

    [Fact]
    public async Task CommitAsync_SignsTheCommitWithTheAccountKey()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("signed"));

        var head = await pds.RepoManager!.GetHeadAsync(did);
        var account = await _accounts.GetByDidAsync(did);

        var view = FirehoseVerifier.ExtractSignedView(head!.CommitBlock);
        Assert.NotNull(view);

        using var key = PdsRepoManager.ImportSigningKey(account!.SigningKey);
        Assert.True(key.Verify(view!.Value.UnsignedBytes, view.Value.SigBytes!));
    }

    [Fact]
    public async Task CommitAsync_CommitDataFieldMatchesTheMstRoot()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("mst"));

        var head = await pds.RepoManager!.GetHeadAsync(did);
        var json = DagCborDecoder.Decode(head!.CommitBlock);

        Assert.Equal(head.DataCid, json.GetProperty("data").GetProperty("$link").GetString());
        Assert.Equal(did, json.GetProperty("did").GetString());
        Assert.Equal(3, json.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task DeleteRecordAsync_NonExistentRecord_DoesNotCommit()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        var before = await pds.RepoManager!.GetHeadAsync(did);

        await pds.DeleteRecordAsync(did, "app.bsky.feed.post", "missing");

        var after = await pds.RepoManager.GetHeadAsync(did);
        Assert.Equal(before!.Rev, after!.Rev);
    }

    [Fact]
    public async Task DeleteRecordAsync_ExistingRecord_CommitsAndRemovesItFromTheTree()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        var created = await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("bye"), rkey: "abc");
        Assert.NotNull(created);

        var before = await pds.RepoManager!.GetHeadAsync(did);
        await pds.DeleteRecordAsync(did, "app.bsky.feed.post", "abc");
        var after = await pds.RepoManager.GetHeadAsync(did);

        Assert.NotEqual(before!.Rev, after!.Rev);
        Assert.Null(await pds.GetRecordAsync(did, "app.bsky.feed.post", "abc"));
    }

    // ── CAR export ───────────────────────────────────────────

    [Fact]
    public async Task ExportRepoAsync_ProducesACarRootedAtTheSignedCommit()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("one"));
        await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("two"));

        var car = await pds.RepoManager!.ExportRepoAsync(did);
        var head = await pds.RepoManager.GetHeadAsync(did);

        Assert.NotNull(car);
        var reader = CarReader.FromBytes(car!, verifyBlockCids: true);

        Assert.Single(reader.Roots);
        Assert.Equal(head!.CommitCid, CidComputation.EncodeCidToString(reader.Roots[0]));
    }

    [Fact]
    public async Task ExportRepoAsync_CarContainsAWalkableMstWithEveryRecord()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        for (var i = 0; i < 12; i++)
            await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record($"post {i}"), rkey: $"key{i:D2}");

        var car = await pds.RepoManager!.ExportRepoAsync(did);
        var reader = CarReader.FromBytes(car!, verifyBlockCids: true);

        var byCid = reader.Blocks.ToDictionary(
            b => CidComputation.EncodeCidToString(b.Cid), b => b.Data, StringComparer.Ordinal);

        // Walk exactly as a relay would: commit root → data → MST → record blocks.
        var commit = DagCborDecoder.Decode(byCid[CidComputation.EncodeCidToString(reader.Roots[0])]);
        var dataCid = commit.GetProperty("data").GetProperty("$link").GetString()!;

        var mst = MerkleSearchTree.Deserialize(
            CidComputation.DecodeCidString(dataCid), cid => byCid.GetValueOrDefault(cid));

        Assert.True(mst.Validate());
        Assert.Equal(12, mst.Count);

        foreach (var (path, recordCid) in mst.GetEntries())
        {
            Assert.StartsWith("app.bsky.feed.post/", path);
            Assert.True(byCid.ContainsKey(CidComputation.EncodeCidToString(recordCid)));
        }
    }

    [Fact]
    public async Task ExportRepoAsync_UnknownDid_ReturnsNull()
    {
        Assert.Null(await CreateManager().ExportRepoAsync("did:plc:doesnotexist"));
    }

    [Fact]
    public async Task ExportRecordProofAsync_IncludesTheRecordBlock()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        var created = await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("proof"), rkey: "xyz");

        var car = await pds.RepoManager!.ExportRecordProofAsync(did, "app.bsky.feed.post", "xyz");
        var reader = CarReader.FromBytes(car!, verifyBlockCids: true);

        Assert.Contains(reader.Blocks, b => CidComputation.EncodeCidToString(b.Cid) == created.Cid);
    }

    [Fact]
    public async Task ExportBlocksAsync_ReturnsOnlyTheRequestedBlocks()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        var created = await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("block"));

        var car = await pds.RepoManager!.ExportBlocksAsync(did, [created.Cid, "bafyreiinvalidcid"]);
        var reader = CarReader.FromBytes(car!, verifyBlockCids: true);

        Assert.Single(reader.Blocks);
        Assert.Equal(created.Cid, CidComputation.EncodeCidToString(reader.Blocks[0].Cid));
    }

    // ── Firehose events ──────────────────────────────────────

    [Fact]
    public async Task CreateAccountAsync_PublishesAccountIdentityAndCommitEvents()
    {
        var pds = CreateService();
        await CreateAccountAsync(pds);

        var types = _sequencer.Backfill(0).Select(e => e.Type).ToList();
        Assert.Equal(["#account", "#identity", "#commit"], types);
    }

    [Fact]
    public async Task CreateRecordAsync_PublishesACommitEventDescribingTheCreate()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        var baseline = _sequencer.CurrentSeq;

        var created = await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("fire"), rkey: "hose");

        var evt = Assert.Single(_sequencer.Backfill(baseline));
        var parsed = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(evt.Frame));

        Assert.Equal(did, parsed.Repo);
        var op = Assert.Single(parsed.Ops!);
        Assert.Equal("create", op.Action);
        Assert.Equal("app.bsky.feed.post/hose", op.Path);
        Assert.Equal(created.Cid, op.Cid);
    }

    [Fact]
    public async Task PutRecordAsync_OverExistingRecord_PublishesAnUpdateWithPrev()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        var first = await pds.PutRecordAsync(did, "app.bsky.feed.post", "k", Record("v1"));

        var baseline = _sequencer.CurrentSeq;
        var second = await pds.PutRecordAsync(did, "app.bsky.feed.post", "k", Record("v2"));

        var evt = Assert.Single(_sequencer.Backfill(baseline));
        var op = Assert.Single(Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(evt.Frame)).Ops!);

        Assert.Equal("update", op.Action);
        Assert.Equal(second.Cid, op.Cid);
        Assert.Equal(first.Cid, op.Prev);
    }

    [Fact]
    public async Task CommitAsync_SecondCommit_CarriesSinceAndPrevData()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        var firstHead = await pds.RepoManager!.GetHeadAsync(did);

        var baseline = _sequencer.CurrentSeq;
        await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("second"));

        var evt = Assert.Single(_sequencer.Backfill(baseline));
        var parsed = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(evt.Frame));

        Assert.Equal(firstHead!.Rev, parsed.Since);
        Assert.Equal(firstHead.DataCid, parsed.PrevData);
    }

    [Fact]
    public async Task CommitAsync_EventBlocksVerifyAgainstTheirCids()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        var baseline = _sequencer.CurrentSeq;
        await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("verify"));

        var evt = Assert.Single(_sequencer.Backfill(baseline));
        var parsed = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(evt.Frame));

        var reader = CarReader.FromBytes(parsed.Blocks!, verifyBlockCids: true);
        reader.VerifyAllBlockCids();
        Assert.Equal(parsed.Commit, CidComputation.EncodeCidToString(reader.Roots[0]));
    }

    [Fact]
    public async Task CommitAsync_EventPassesFirehoseCidVerification()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        var baseline = _sequencer.CurrentSeq;
        await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("cid-check"));

        var evt = Assert.Single(_sequencer.Backfill(baseline));
        var parsed = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(evt.Frame));

        Assert.True(FirehoseVerifier.VerifyCid(parsed).IsValid);
    }

    [Fact]
    public async Task CommitAsync_LargeCommit_SetsTooBigInsteadOfInliningBlocks()
    {
        _options.MaxFirehoseFrameBytes = 256;

        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        var baseline = _sequencer.CurrentSeq;
        await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record(new string('x', 2000)));

        var evt = Assert.Single(_sequencer.Backfill(baseline));
        var parsed = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(evt.Frame));

        Assert.True(parsed.TooBig);
        Assert.Empty(parsed.Blocks!);
    }

    [Fact]
    public async Task CommitAsync_LargeRepo_InlinesOnlyTheCoveringProof()
    {
        // Inlining the whole MST made every write on a repo of any size exceed
        // MaxFirehoseFrameBytes and fall back to tooBig, which defeats incremental consumption.
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        for (var i = 0; i < 300; i++)
            await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record($"p{i}"), rkey: $"r{i:D4}");

        var baseline = _sequencer.CurrentSeq;
        await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("one more"), rkey: "r9999");

        var evt = Assert.Single(_sequencer.Backfill(baseline));
        var parsed = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(evt.Frame));

        Assert.False(parsed.TooBig);

        // The proof is the root→key path plus the record, far fewer blocks than the whole tree.
        var manager = CreateManager();
        var snapshot = await manager.BuildSnapshotAsync(did, CancellationToken.None);
        var carBlockCount = CarReader.FromBytes(parsed.Blocks!).Blocks.Count;

        Assert.True(carBlockCount < snapshot.MstBlocks.Count,
            $"expected fewer than the {snapshot.MstBlocks.Count} MST blocks, got {carBlockCount}");
    }

    [Fact]
    public async Task CommitAsync_ProofBlocksStillVerifyAgainstTheirCids()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        for (var i = 0; i < 50; i++)
            await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record($"p{i}"), rkey: $"r{i:D3}");

        var baseline = _sequencer.CurrentSeq;
        await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("last"), rkey: "r999");

        var evt = Assert.Single(_sequencer.Backfill(baseline));
        var parsed = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(evt.Frame));

        Assert.True(FirehoseVerifier.VerifyCid(parsed).IsValid);
    }

    [Fact]
    public async Task DeleteAccountAsync_PublishesAnInactiveAccountEvent()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        var baseline = _sequencer.CurrentSeq;

        await pds.DeleteAccountAsync(did, "password123");

        var evt = _sequencer.Backfill(baseline).Last();
        var parsed = Assert.IsType<AccountEvent>(FirehoseEventParser.Parse(evt.Frame));

        Assert.False(parsed.Active);
        Assert.Equal("deleted", parsed.Status);
        Assert.Null(await pds.RepoManager!.GetHeadAsync(did));
    }

    // ── Determinism and concurrency ──────────────────────────

    [Fact]
    public async Task BuildSnapshotAsync_IsDeterministicForTheSameRecordSet()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        for (var i = 0; i < 20; i++)
            await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record($"p{i}"), rkey: $"r{i:D2}");

        var manager = CreateManager();
        var first = await manager.BuildSnapshotAsync(did, CancellationToken.None);
        var second = await manager.BuildSnapshotAsync(did, CancellationToken.None);

        Assert.Equal(first.MstRootCid, second.MstRootCid);
        Assert.Equal(first.MstBlocks.Keys.Order(), second.MstBlocks.Keys.Order());
    }

    [Fact]
    public async Task CommitAsync_ConcurrentCommits_ProduceStrictlyIncreasingRevisions()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        var manager = pds.RepoManager!;

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => manager.CommitAsync(did, [])));

        var revs = _sequencer.Backfill(0)
            .Where(e => e.Type == "#commit")
            .Select(e => Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(e.Frame)).Rev)
            .ToList();

        Assert.Equal(revs.Order(StringComparer.Ordinal), revs);
        Assert.Equal(revs.Distinct().Count(), revs.Count);
    }

    // ── Signing key compatibility ────────────────────────────

    [Fact]
    public void ImportSigningKey_AcceptsThePreFederationSec1Encoding()
    {
        // Accounts created before federation support stored a bare SEC1 key. Rejecting those
        // would silently re-key every existing repository.
        using var ecdsa = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var sec1 = Convert.ToBase64String(ecdsa.ExportECPrivateKey());

        using var imported = PdsRepoManager.ImportSigningKey(sec1);

        using var expected = AtProtoCrypto.ImportPrivateKey(ecdsa.ExportPkcs8PrivateKey(), KeyCurve.P256);
        Assert.Equal(expected.ToDidKey(), imported.ToDidKey());
    }

    [Fact]
    public void ImportSigningKey_AcceptsPkcs8()
    {
        using var generated = AtProtoCrypto.GenerateP256Key();
        var pkcs8 = Convert.ToBase64String(generated.ExportPrivateKey());

        using var imported = PdsRepoManager.ImportSigningKey(pkcs8);
        Assert.Equal(generated.ToDidKey(), imported.ToDidKey());
    }

    // ── Store contract ───────────────────────────────────────

    [Fact]
    public async Task ListAllRecordsAsync_ReturnsEveryCollectionInMstKeyOrder()
    {
        var pds = CreateService();
        var did = await CreateAccountAsync(pds);
        await pds.PutRecordAsync(did, "app.bsky.feed.post", "b", Record("post"));
        await pds.PutRecordAsync(did, "app.bsky.actor.profile", "self", Record("profile"));
        await pds.PutRecordAsync(did, "app.bsky.feed.post", "a", Record("post2"));

        var all = await _repos.ListAllRecordsAsync(did);

        Assert.Equal(
            ["app.bsky.actor.profile/self", "app.bsky.feed.post/a", "app.bsky.feed.post/b"],
            all.Select(r => $"{r.Collection}/{r.Rkey}"));
    }

    [Fact]
    public async Task ListAllRecordsAsync_DoesNotLeakOtherRepos()
    {
        var pds = CreateService();
        var alice = await CreateAccountAsync(pds, "alice.test.local");
        var bob = await CreateAccountAsync(pds, "bob.test.local");

        await pds.PutRecordAsync(alice, "app.bsky.feed.post", "a", Record("alice"));
        await pds.PutRecordAsync(bob, "app.bsky.feed.post", "b", Record("bob"));

        Assert.Single(await _repos.ListAllRecordsAsync(alice));
        Assert.Single(await _repos.ListAllRecordsAsync(bob));
    }

    [Fact]
    public async Task ListAllRecordsAsync_StoreWithoutOverride_ThrowsWithAnActionableMessage()
    {
        IRepoStore store = new LegacyRepoStore();
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => store.ListAllRecordsAsync("did:plc:whatever"));

        Assert.Contains("ListAllRecordsAsync", ex.Message);
        Assert.Contains(nameof(LegacyRepoStore), ex.Message);
    }

    [Fact]
    public async Task CreateRecordAsync_StoreWithoutEnumeration_StillWritesTheRecord()
    {
        // The compatibility promise on IRepoStore: a store written before federation support
        // keeps serving repo CRUD, and only the federation surface reports the gap. Because
        // AddAtProtoPds always wires a PdsRepoManager, every write goes through CommitAsync —
        // so CommitAsync has to degrade rather than throw.
        var store = new LegacyBackedRepoStore();
        var manager = new PdsRepoManager(_accounts, store, _commits, _sequencer, _options);
        var pds = new PdsService(_accounts, store, new PdsSessionService(_options), _options,
            manager, new PdsIdentityService(_options, _accounts));

        var did = await CreateAccountAsync(pds);
        var result = await pds.CreateRecordAsync(did, "app.bsky.feed.post", Record("hello"));

        Assert.NotNull(result.Cid);
        Assert.NotNull(await store.Inner.GetRecordAsync(did, "app.bsky.feed.post", result.Uri.Split('/')[^1]));
    }

    [Fact]
    public async Task CommitAsync_StoreWithoutEnumeration_ReturnsNullAndPublishesNothing()
    {
        var store = new LegacyBackedRepoStore();
        var manager = new PdsRepoManager(_accounts, store, _commits, _sequencer, _options);
        var pds = new PdsService(_accounts, store, new PdsSessionService(_options), _options,
            manager, new PdsIdentityService(_options, _accounts));

        var did = await CreateAccountAsync(pds);
        var baseline = _sequencer.CurrentSeq;

        Assert.Null(await manager.CommitAsync(did, []));
        Assert.True(manager.IsRepositoryEnumerationUnsupported);
        Assert.DoesNotContain(_sequencer.Backfill(baseline), e => e.Type == "#commit");
    }

    [Fact]
    public async Task ExportRepoAsync_StoreWithoutEnumeration_StillReportsTheGap()
    {
        // Degrading the commit path must not swallow the error on the sync surface, which
        // genuinely cannot work without the record set.
        var store = new LegacyBackedRepoStore();
        var manager = new PdsRepoManager(_accounts, store, _commits, _sequencer, _options);

        await _commits.SetAsync(new RepoCommitState
        {
            Did = "did:plc:legacy",
            CommitCid = "bafyreiabc",
            Rev = "3l000000000",
            DataCid = "bafyreidef",
            CommitBlock = [1, 2, 3],
        });

        await Assert.ThrowsAsync<NotSupportedException>(
            () => manager.ExportRepoAsync("did:plc:legacy"));
    }

    /// <summary>
    /// A pre-federation store that really persists: <see cref="LegacyRepoStore"/> with the CRUD
    /// members delegating to an in-memory store, so writes can be observed.
    /// </summary>
    private sealed class LegacyBackedRepoStore : IRepoStore
    {
        public InMemoryRepoStore Inner { get; } = new();

        public Task PutRecordAsync(RepoRecord record, CancellationToken cancellationToken = default)
            => Inner.PutRecordAsync(record, cancellationToken);

        public Task<RepoRecord?> GetRecordAsync(string did, string collection, string rkey,
            CancellationToken cancellationToken = default)
            => Inner.GetRecordAsync(did, collection, rkey, cancellationToken);

        public Task<RecordPage> ListRecordsAsync(string did, string collection, int limit = 50,
            string? cursor = null, bool reverse = false, CancellationToken cancellationToken = default)
            => Inner.ListRecordsAsync(did, collection, limit, cursor, reverse, cancellationToken);

        public Task<bool> DeleteRecordAsync(string did, string collection, string rkey,
            CancellationToken cancellationToken = default)
            => Inner.DeleteRecordAsync(did, collection, rkey, cancellationToken);

        public Task DeleteAllAsync(string did, CancellationToken cancellationToken = default)
            => Inner.DeleteAllAsync(did, cancellationToken);

        public Task PutBlobAsync(RepoBlob blob, CancellationToken cancellationToken = default)
            => Inner.PutBlobAsync(blob, cancellationToken);

        public Task<RepoBlob?> GetBlobAsync(string did, string cid,
            CancellationToken cancellationToken = default)
            => Inner.GetBlobAsync(did, cid, cancellationToken);

        public Task<bool> DeleteBlobAsync(string did, string cid,
            CancellationToken cancellationToken = default)
            => Inner.DeleteBlobAsync(did, cid, cancellationToken);
    }

    /// <summary>An IRepoStore written before federation support: no enumeration members.</summary>
    private sealed class LegacyRepoStore : IRepoStore
    {
        public Task PutRecordAsync(RepoRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<RepoRecord?> GetRecordAsync(string did, string collection, string rkey,
            CancellationToken cancellationToken = default) => Task.FromResult<RepoRecord?>(null);

        public Task<RecordPage> ListRecordsAsync(string did, string collection, int limit = 50,
            string? cursor = null, bool reverse = false, CancellationToken cancellationToken = default)
            => Task.FromResult(new RecordPage { Records = [] });

        public Task<bool> DeleteRecordAsync(string did, string collection, string rkey,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task DeleteAllAsync(string did, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PutBlobAsync(RepoBlob blob, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<RepoBlob?> GetBlobAsync(string did, string cid,
            CancellationToken cancellationToken = default) => Task.FromResult<RepoBlob?>(null);

        public Task<bool> DeleteBlobAsync(string did, string cid,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
