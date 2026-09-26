using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;
using ATProtoNet.Streaming;
using ATProtoNet.Tests.Identity;

namespace ATProtoNet.Tests.Streaming;

/// <summary>
/// Sync 1.1 inductive verification: the spec's validation checklist, against real frames from the
/// network and against a repository that builds the edge cases.
/// </summary>
public sealed class RepoSyncVerifierTests : IDisposable
{
    private readonly SyncTestRepo _repo = new();
    private readonly StubDidResolver _resolver = new();
    private readonly InMemoryRepoSyncStateStore _store = new();
    private readonly List<RepoSyncResult> _desynchronized = [];

    public RepoSyncVerifierTests() => _resolver.Add(_repo.Did, _repo.SigningKey);

    public void Dispose() => _repo.Dispose();

    private RepoSyncVerifier Verifier(TimeProvider? clock = null, IDidResolver? resolver = null) => new(new RepoSyncVerifierOptions
    {
        StateStore = _store,
        DidResolver = resolver ?? _resolver,
        TimeProvider = clock,
        OnDesynchronized = _desynchronized.Add,
    });

    /// <summary>Verifies and, when valid, applies: what a consumer does with each event.</summary>
    private static async Task<RepoSyncResult> Process(RepoSyncVerifier verifier, FirehoseEvent evt)
    {
        var result = evt switch
        {
            CommitEvent commit => await verifier.VerifyCommitAsync(commit),
            SyncEvent sync => await verifier.VerifySyncAsync(sync),
            _ => throw new ArgumentException("Not a repository event.", nameof(evt)),
        };

        await verifier.ApplyAsync(result);
        return result;
    }

    private static void AssertOutcome(RepoSyncOutcome expected, RepoSyncResult result) =>
        Assert.True(result.Outcome == expected, $"Expected {expected}, got {result}");

    // ── Real frames from the network ─────────────────────────

    [Theory]
    [InlineData("chain-first")]
    [InlineData("chain-second")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("multi-op")]
    [InlineData("sync")]
    public async Task VerifyAsync_LiveFrame_IsValid(string name)
    {
        using var verifier = Verifier(resolver: LiveFrames.Resolver());

        var result = await Process(verifier, LiveFrames.Event(name));

        AssertOutcome(RepoSyncOutcome.Valid, result);
    }

    [Fact]
    public async Task VerifyCommitAsync_LiveConsecutiveCommits_Chain()
    {
        using var verifier = Verifier(resolver: LiveFrames.Resolver());
        var first = LiveFrames.Commit("chain-first");
        var second = LiveFrames.Commit("chain-second");

        AssertOutcome(RepoSyncOutcome.Valid, await Process(verifier, first));
        var result = await Process(verifier, second);

        AssertOutcome(RepoSyncOutcome.Valid, result);
        var state = await _store.GetAsync(second.Repo);
        Assert.Equal(new RepoSyncState(second.Repo, second.Rev, result.Data, RepoSyncStatus.Synchronized), state);
        Assert.Empty(_desynchronized);
    }

    [Fact]
    public async Task VerifyCommitAsync_LiveCommitAfterAMissedOne_Desynchronizes()
    {
        using var verifier = Verifier(resolver: LiveFrames.Resolver());
        var second = LiveFrames.Commit("chain-second");

        // The state the repository had before the first commit: the first one never arrived.
        await _store.SetAsync(new RepoSyncState(second.Repo, Tid.Parse("3mwgnaaaaaaa2"),
            Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm"), RepoSyncStatus.Synchronized));

        var result = await Process(verifier, second);

        AssertOutcome(RepoSyncOutcome.Desynchronized, result);
        Assert.Contains("prevData", result.Reason);
        Assert.Equal(RepoSyncStatus.Desynchronized, (await _store.GetAsync(second.Repo))!.Status);
        Assert.Same(result, Assert.Single(_desynchronized));
    }

    [Fact]
    public async Task VerifyCommitAsync_LiveCommitMissingAnOperation_FailsInversion()
    {
        using var verifier = Verifier(resolver: LiveFrames.Resolver());
        var multi = LiveFrames.Commit("multi-op");

        var result = await Process(verifier, LiveFrames.With(multi, ops: multi.Ops!.Skip(1).ToList()));

        AssertOutcome(RepoSyncOutcome.Invalid, result);
    }

    [Fact]
    public async Task VerifyCommitAsync_LiveCommitWithAForgedPrevData_FailsInversion()
    {
        using var verifier = Verifier(resolver: LiveFrames.Resolver());
        var update = LiveFrames.Commit("update");

        var result = await Process(verifier, LiveFrames.With(update,
            prevData: Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm")));

        AssertOutcome(RepoSyncOutcome.Invalid, result);
        Assert.Contains("Inverting", result.Reason);
    }

    [Fact]
    public async Task VerifyCommitAsync_LiveDeleteClaimedAsAnotherRecord_Invalid()
    {
        using var verifier = Verifier(resolver: LiveFrames.Resolver());
        var delete = LiveFrames.Commit("delete");
        var op = delete.Ops![0];

        var result = await Process(verifier, LiveFrames.With(delete,
            ops: [new RepoOp { Action = RepoOpAction.Delete, Path = op.Path + "x", Prev = op.Prev }]));

        AssertOutcome(RepoSyncOutcome.Invalid, result);
    }

    [Fact]
    public async Task VerifyCommitAsync_LiveCommitReplayed_IsStale()
    {
        using var verifier = Verifier(resolver: LiveFrames.Resolver());
        var commit = LiveFrames.Commit("chain-first");

        await Process(verifier, commit);
        var replay = await Process(verifier, commit);

        AssertOutcome(RepoSyncOutcome.Stale, replay);
    }

    [Fact]
    public async Task VerifyCommitAsync_LiveCommitUnderAnotherKey_InvalidAfterOneRefresh()
    {
        using var impostor = AtProtoCrypto.GenerateK256Key();
        var resolver = new StubDidResolver();
        var commit = LiveFrames.Commit("chain-first");
        resolver.Add(commit.Repo, impostor.ToDidKey());
        using var verifier = Verifier(resolver: resolver);

        var result = await Process(verifier, commit);

        AssertOutcome(RepoSyncOutcome.Invalid, result);
        Assert.Contains("signature", result.Reason);
        Assert.Equal(1, resolver.Refreshes);
    }

    // ── The chain ────────────────────────────────────────────

    [Fact]
    public async Task VerifyCommitAsync_CreatesUpdatesAndDeletes_ChainAcrossCommits()
    {
        using var verifier = Verifier();
        var a = _repo.NewPath();
        var b = _repo.NewPath();
        var c = _repo.NewPath("com.example.other");

        AssertOutcome(RepoSyncOutcome.Valid, await Process(verifier, _repo.Commit((RepoOpAction.Create, a), (RepoOpAction.Create, b))));
        AssertOutcome(RepoSyncOutcome.Valid, await Process(verifier, _repo.Commit((RepoOpAction.Update, a), (RepoOpAction.Create, c))));
        AssertOutcome(RepoSyncOutcome.Valid, await Process(verifier, _repo.Commit((RepoOpAction.Delete, b))));
        AssertOutcome(RepoSyncOutcome.Valid, await Process(verifier, _repo.Commit((RepoOpAction.Delete, a), (RepoOpAction.Update, c))));
        AssertOutcome(RepoSyncOutcome.Valid, await Process(verifier, _repo.Commit()));

        Assert.Equal(new RepoSyncState(_repo.Did, _repo.Rev, _repo.Data, RepoSyncStatus.Synchronized), await _store.GetAsync(_repo.Did));
    }

    [Fact]
    public async Task VerifyCommitAsync_ManyRecords_ChainsOverTheCoveringProof()
    {
        using var verifier = Verifier();
        for (var i = 0; i < 30; i++)
            AssertOutcome(RepoSyncOutcome.Valid, await Process(verifier, _repo.Commit(
                Enumerable.Range(0, 10).Select(_ => (RepoOpAction.Create, _repo.NewPath())).ToArray())));

        var existing = _repo.Tree.GetEntries().Select(e => e.Key).ToList();
        var result = await Process(verifier, _repo.Commit(
            (RepoOpAction.Delete, existing[3]), (RepoOpAction.Update, existing[150]), (RepoOpAction.Create, _repo.NewPath())));

        AssertOutcome(RepoSyncOutcome.Valid, result);
    }

    [Fact]
    public async Task VerifyCommitAsync_MissedCommit_DesynchronizesOnceAndHoldsTheRepository()
    {
        using var verifier = Verifier();
        await Process(verifier, _repo.Create());
        _repo.Create(); // never delivered

        var broken = await Process(verifier, _repo.Create());
        var after = await Process(verifier, _repo.Create());

        AssertOutcome(RepoSyncOutcome.Desynchronized, broken);
        AssertOutcome(RepoSyncOutcome.Desynchronized, after);
        Assert.Same(broken, Assert.Single(_desynchronized));
        var state = await _store.GetAsync(_repo.Did);
        Assert.Equal(RepoSyncStatus.Desynchronized, state!.Status);
        Assert.Equal(broken.Rev, state.Rev);
    }

    [Fact]
    public async Task VerifyCommitAsync_SinceMismatchButPrevDataMatches_IsValid()
    {
        // An empty commit that changed only the revision went missing: no record was lost.
        using var verifier = Verifier();
        await Process(verifier, _repo.Create());

        var result = await Process(verifier, _repo.Create(new CommitTweaks { Since = Tid.Parse("3aaaaaaaaaaaa") }));

        AssertOutcome(RepoSyncOutcome.Valid, result);
    }

    [Fact]
    public async Task VerifyCommitAsync_PrevDataStrippedAndAnOperationDropped_Desynchronizes()
    {
        // A relay strips prevData and drops the delete: since still matches, and without prevData
        // nothing can be inverted to show the list is short.
        using var verifier = Verifier();
        var kept = _repo.NewPath();
        var deleted = _repo.NewPath();
        await Process(verifier, _repo.Commit((RepoOpAction.Create, kept), (RepoOpAction.Create, deleted)));

        var result = await Process(verifier, _repo.Commit(
            [(RepoOpAction.Update, kept), (RepoOpAction.Delete, deleted)],
            new CommitTweaks { OmitPrevData = true, Ops = ops => [ops[0]] }));

        AssertOutcome(RepoSyncOutcome.Desynchronized, result);
        Assert.Contains("prevData", result.Reason);
        Assert.Equal(RepoSyncStatus.Desynchronized, (await _store.GetAsync(_repo.Did))!.Status);
    }

    [Fact]
    public async Task VerifyCommitAsync_CommitWithoutPrevDataAfterAChain_Desynchronizes()
    {
        using var verifier = Verifier();
        await Process(verifier, _repo.Create());

        var result = await Process(verifier, _repo.Create(new CommitTweaks { OmitPrevData = true }));

        AssertOutcome(RepoSyncOutcome.Desynchronized, result);
    }

    [Fact]
    public async Task VerifyCommitAsync_FirstCommitWithoutPrevData_StartsTheChain()
    {
        using var verifier = Verifier();

        var result = await Process(verifier, _repo.Create(new CommitTweaks { OmitPrevData = true, OmitSince = true }));

        AssertOutcome(RepoSyncOutcome.Valid, result);
    }

    [Fact]
    public async Task VerifyCommitAsync_UnknownRepository_StartsItsChain()
    {
        using var verifier = Verifier();
        _repo.Create();
        _repo.Create();

        var result = await Process(verifier, _repo.Create());

        AssertOutcome(RepoSyncOutcome.Valid, result);
        Assert.Equal(RepoSyncStatus.Synchronized, (await _store.GetAsync(_repo.Did))!.Status);
    }

    [Fact]
    public async Task VerifyCommitAsync_RevisionNotAfterTheLast_IsStale()
    {
        using var verifier = Verifier();
        var first = _repo.Create();
        await Process(verifier, _repo.Create(new CommitTweaks { RevMicros = 1_780_000_000_000_000 }));

        var result = await Process(verifier, first);

        AssertOutcome(RepoSyncOutcome.Stale, result);
    }

    [Fact]
    public async Task ApplyAsync_IsWhatRecordsAValidCommit()
    {
        using var verifier = Verifier();
        var commit = _repo.Create();

        var result = await verifier.VerifyCommitAsync(commit);
        Assert.Null(await _store.GetAsync(_repo.Did));

        await verifier.ApplyAsync(result);
        Assert.Equal(result.State, await _store.GetAsync(_repo.Did));
    }

    // ── #sync ────────────────────────────────────────────────

    [Fact]
    public async Task VerifySyncAsync_SameTree_MovesTheRevisionOn()
    {
        using var verifier = Verifier();
        await Process(verifier, _repo.Create());

        var sync = await Process(verifier, _repo.Sync());
        var next = await Process(verifier, _repo.Create());

        AssertOutcome(RepoSyncOutcome.Valid, sync);
        AssertOutcome(RepoSyncOutcome.Valid, next);
    }

    [Fact]
    public async Task VerifySyncAsync_OtherTree_Desynchronizes()
    {
        using var verifier = Verifier();
        await Process(verifier, _repo.Create());
        _repo.Create(); // the tree moves on without the verifier

        var result = await Process(verifier, _repo.Sync());

        AssertOutcome(RepoSyncOutcome.Desynchronized, result);
        Assert.Contains("#sync", result.Reason);
        Assert.Single(_desynchronized);
    }

    [Fact]
    public async Task VerifySyncAsync_ForgedSignature_Invalid()
    {
        using var forger = AtProtoCrypto.GenerateK256Key();
        using var verifier = Verifier();

        var result = await Process(verifier, _repo.Sync(new CommitTweaks { SignWith = forger }));

        AssertOutcome(RepoSyncOutcome.Invalid, result);
    }

    // ── The checklist, one rule at a time ────────────────────

    public static TheoryData<string, string> Tampered => new()
    {
        { "version 2", "version 2" },
        { "signed by another key", "signature" },
        { "signed for another DID", "is for did:plc:someoneelse" },
        { "signed with another revision", "revision is 3jzfcijpj2z2a" },
        { "commit is not the root", "root is not" },
        { "record block missing", "do not include the record" },
        { "operation missing", "Inverting the operations" },
        { "operation not in the tree", "does not hold the created" },
        { "two operations on one path", "two operations" },
        { "create with prev", "malformed" },
        { "update without prev", "malformed" },
        { "delete without prev", "malformed" },
        { "delete with cid", "malformed" },
        { "proof missing", "do not prove" },
        { "invalid path", "not a valid record path" },
    };

    [Theory]
    [MemberData(nameof(Tampered))]
    public async Task VerifyCommitAsync_BrokenRule_IsInvalid(string rule, string reason)
    {
        using var forger = AtProtoCrypto.GenerateK256Key();
        using var verifier = Verifier();
        var existing = _repo.NewPath();
        var other = _repo.NewPath();
        await Process(verifier, _repo.Commit((RepoOpAction.Create, existing), (RepoOpAction.Create, other)));
        for (var i = 0; i < 20; i++)
            await Process(verifier, _repo.Create());

        CommitEvent Update(CommitTweaks tweaks) => _repo.Commit([(RepoOpAction.Update, existing), (RepoOpAction.Create, _repo.NewPath())], tweaks);
        CommitEvent Delete(CommitTweaks tweaks) => _repo.Commit([(RepoOpAction.Delete, existing)], tweaks);

        var commit = rule switch
        {
            "version 2" => _repo.Create(new CommitTweaks { Version = 2 }),
            "signed by another key" => _repo.Create(new CommitTweaks { SignWith = forger }),
            "signed for another DID" => _repo.Create(new CommitTweaks { SignedDid = "did:plc:someoneelseaaaaaaaaaaaa" }),
            "signed with another revision" => _repo.Create(new CommitTweaks { SignedRev = "3jzfcijpj2z2a" }),
            "commit is not the root" => _repo.Create(new CommitTweaks { Commit = Cid.Parse(EventStreamFrames.CommitCid) }),
            "record block missing" => _repo.Create(new CommitTweaks { OmitRecords = true }),
            "operation missing" => Update(new CommitTweaks { Ops = ops => [ops[0]] }),
            "operation not in the tree" => Update(new CommitTweaks
            {
                Ops = ops => [.. ops, new RepoOp { Action = RepoOpAction.Create, Path = "com.example.record/3zzzzzzzzzzzz", Cid = ops[1].Cid }],
            }),
            "two operations on one path" => Update(new CommitTweaks { Ops = ops => [ops[0], ops[1], ops[1]] }),
            "create with prev" => Update(new CommitTweaks { Ops = ops => [ops[0], Copy(ops[1], prev: ops[0].Prev)] }),
            "update without prev" => Update(new CommitTweaks { Ops = ops => [Copy(ops[0], clearPrev: true), ops[1]] }),
            "delete without prev" => Delete(new CommitTweaks { Ops = ops => [Copy(ops[0], clearPrev: true)] }),
            "delete with cid" => Delete(new CommitTweaks { Ops = ops => [Copy(ops[0], cid: ops[0].Prev)] }),
            "proof missing" => Update(new CommitTweaks { RootOnly = true }),
            "invalid path" => Update(new CommitTweaks { Ops = ops => [ops[0], Copy(ops[1], path: "com.example.record/not a key")] }),
            _ => throw new ArgumentOutOfRangeException(nameof(rule)),
        };

        var result = await Process(verifier, commit);

        AssertOutcome(RepoSyncOutcome.Invalid, result);
        Assert.Contains(reason, result.Reason);
        Assert.Equal(RepoSyncStatus.Synchronized, (await _store.GetAsync(_repo.Did))!.Status);
        Assert.Empty(_desynchronized);
    }

    private static RepoOp Copy(RepoOp op, string? path = null, Cid? cid = null, Cid? prev = null, bool clearPrev = false) => new()
    {
        Action = op.Action,
        Path = path ?? op.Path,
        Cid = cid ?? op.Cid,
        Prev = clearPrev ? null : prev ?? op.Prev,
    };

    [Fact]
    public async Task VerifyCommitAsync_TooManyOperations_IsInvalid()
    {
        using var verifier = Verifier();
        var ops = Enumerable.Range(0, RepoSyncVerifier.MaxCommitOps + 1).Select(_ => (RepoOpAction.Create, _repo.NewPath())).ToArray();

        var result = await Process(verifier, _repo.Commit(ops));

        AssertOutcome(RepoSyncOutcome.Invalid, result);
        Assert.Contains("operations", result.Reason);
    }

    [Fact]
    public async Task VerifyCommitAsync_MaximumOperations_IsValid()
    {
        using var verifier = Verifier();
        var ops = Enumerable.Range(0, RepoSyncVerifier.MaxCommitOps).Select(_ => (RepoOpAction.Create, _repo.NewPath())).ToArray();

        AssertOutcome(RepoSyncOutcome.Valid, await Process(verifier, _repo.Commit(ops)));
    }

    [Fact]
    public async Task VerifyCommitAsync_BlocksOverTheLimit_IsInvalid()
    {
        using var verifier = Verifier();
        var commit = _repo.Create();

        var result = await Process(verifier, LiveFrames.With(commit, blocks: new byte[RepoSyncVerifier.MaxBlocksBytes + 1]));

        AssertOutcome(RepoSyncOutcome.Invalid, result);
        Assert.Contains("bytes", result.Reason);
    }

    [Fact]
    public async Task VerifyCommitAsync_RevisionInTheFuture_IsInvalid()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var verifier = Verifier(clock);

        var soon = await Process(verifier, _repo.Create(new CommitTweaks { RevMicros = clock.GetUtcNow().AddMinutes(4).ToUnixTimeMilliseconds() * 1000 }));
        var later = await Process(verifier, _repo.Create(new CommitTweaks { RevMicros = clock.GetUtcNow().AddMinutes(6).ToUnixTimeMilliseconds() * 1000 }));

        AssertOutcome(RepoSyncOutcome.Valid, soon);
        AssertOutcome(RepoSyncOutcome.Invalid, later);
        Assert.Contains("future", later.Reason);
    }

    [Fact]
    public async Task VerifyCommitAsync_UnresolvableAccount_IsInvalid()
    {
        using var verifier = Verifier(resolver: new StubDidResolver());

        var result = await Process(verifier, _repo.Create());

        AssertOutcome(RepoSyncOutcome.Invalid, result);
        Assert.Contains("identity", result.Reason);
    }

    [Fact]
    public async Task VerifyCommitAsync_KeyRotated_VerifiesAfterOneRefresh()
    {
        using var verifier = Verifier();
        await Process(verifier, _repo.Create());

        using var rotated = AtProtoCrypto.GenerateK256Key();
        _resolver.RotatedKeys[_repo.Did] = rotated.ToDidKey();
        _repo.Key = rotated;

        var result = await Process(verifier, _repo.Create());

        AssertOutcome(RepoSyncOutcome.Valid, result);
        Assert.Equal(1, _resolver.Refreshes);
    }

    [Fact]
    public async Task InvalidateIdentityAsync_ReachesTheResolver()
    {
        using var verifier = Verifier();

        await verifier.InvalidateIdentityAsync(_repo.Did);

        Assert.Equal(1, _resolver.Invalidations);
    }

    // ── The store ────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_ListUnsynchronized_ReturnsOnlyThoseUpToTheLimit()
    {
        var store = new InMemoryRepoSyncStateStore();
        for (var i = 0; i < 5; i++)
            await store.SetAsync(new RepoSyncState(Did.Parse($"did:plc:aaaaaaaaaaaaaaaaaaaaaaa{i}"), null, null, RepoSyncStatus.Desynchronized));
        await store.SetAsync(new RepoSyncState(Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb"), null, null, RepoSyncStatus.Synchronized));
        await store.SetAsync(new RepoSyncState(Did.Parse("did:plc:cccccccccccccccccccccccc"), null, null, RepoSyncStatus.Resynchronizing));

        Assert.Equal(6, (await store.ListUnsynchronizedAsync(100)).Count);
        Assert.Equal(3, (await store.ListUnsynchronizedAsync(3)).Count);
        Assert.DoesNotContain(await store.ListUnsynchronizedAsync(100), s => s.Status == RepoSyncStatus.Synchronized);

        await store.RemoveAsync(Did.Parse("did:plc:cccccccccccccccccccccccc"));
        Assert.Equal(6, store.Count);
    }
}
