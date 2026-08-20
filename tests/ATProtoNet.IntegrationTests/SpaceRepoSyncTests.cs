using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.IntegrationTests;

/// <summary>
/// A permissioned repo's committed state, round-tripped through a live host.
/// </summary>
/// <remarks>
/// <para>This is the part unit tests cannot reach. The SDK's LtHash, its commit-context
/// encoding, its MAC, and its canonical DAG-CBOR key ordering are pinned in unit tests against
/// vectors — which proves the SDK agrees with a reading of the specification, not that it agrees
/// with an implementation of it. Here the server computes the set hash, signs the commit, and
/// lays out the CAR, and <see cref="SpaceRepoCar.Verify"/> has to accept all of it.</para>
/// <para>The sync tests then drive the same agreement incrementally: every operation the SDK
/// folds into its running hash has to land on the digest the host independently signed.</para>
/// </remarks>
[Collection("Spaces")]
public class SpaceRepoSyncTests(SpaceNetworkFixture fixture)
{
    [RequiresSpacesFact]
    public async Task GetRepoAsync_ServesACarThatVerifiesAgainstTheServersOwnCommit()
    {
        var space = await fixture.CreateSpaceAsync("car-verify", members: [fixture.Member]);

        foreach (var collection in new[] { SpaceNetworkFixture.Collection, SpaceNetworkFixture.CollectionAlt })
        {
            for (var i = 0; i < 2; i++)
                await fixture.WriteAsync(fixture.Member, space, $"car {i}", $"car-{i}", collection);
        }

        await using var provider = fixture.CreateProvider(fixture.Authority);
        using var reader = await provider.CreateReaderForRepoAsync(space, fixture.Member.Did);

        var car = await DownloadRepoAsync(reader, space, fixture.Member.Did);
        var didKey = await fixture.ResolveSigningKeyAsync(fixture.Member.Did);

        // Everything the SDK computes independently has to agree with what the server signed:
        // the commit's signature and MAC, the index folded into a set hash, and each record
        // against the CID the index vouched for.
        var repo = SpaceRepoCar.Verify(car, space, fixture.Member.Did, didKey);

        Assert.Equal(4, repo.Records.Count);
        Assert.Equal(4, repo.Index.Count);
        Assert.True(SpaceRepoCommit.FromIndex(repo.Index).Matches(repo.Commit));

        var latest = await reader.Space.GetLatestCommitAsync(space.Value, fixture.Member.Did);
        Assert.Equal(latest.Commit.Rev, repo.Commit.Rev);
        Assert.Equal(latest.Commit.Hash, repo.Commit.Hash);

        // The index is canonically ordered, which is what lets a consumer verify the CAR as a
        // stream rather than buffering it — the blocks arrive in the order the index names them.
        Assert.Equal(repo.Index.Select(entry => entry.Key), repo.Records.Select(record => record.Path));

        Assert.Equal(
            [
                $"{SpaceNetworkFixture.CollectionAlt}/car-0",
                $"{SpaceNetworkFixture.CollectionAlt}/car-1",
                $"{SpaceNetworkFixture.Collection}/car-0",
                $"{SpaceNetworkFixture.Collection}/car-1",
            ],
            repo.Records.Select(record => record.Path).Order(StringComparer.Ordinal));
    }

    [RequiresSpacesFact]
    public async Task GetRepoAsync_WithExcludeValues_ServesAnIndexOnlyCarThatStillVerifies()
    {
        var space = await fixture.CreateSpaceAsync("car-index", members: [fixture.Member]);
        for (var i = 0; i < 2; i++)
            await fixture.WriteAsync(fixture.Member, space, $"idx {i}", $"idx-{i}");

        await using var provider = fixture.CreateProvider(fixture.Authority);
        using var reader = await provider.CreateReaderForRepoAsync(space, fixture.Member.Did);

        var car = await DownloadRepoAsync(reader, space, fixture.Member.Did, excludeValues: true);
        var didKey = await fixture.ResolveSigningKeyAsync(fixture.Member.Did);

        // The set hash folds from the index alone, so an index-only CAR still authenticates every
        // path/CID pair — which is what makes a diff-then-fetch sync possible.
        var repo = SpaceRepoCar.Verify(car, space, fixture.Member.Did, didKey, expectValues: false);

        Assert.Empty(repo.Records);
        Assert.Equal(2, repo.Index.Count);
        Assert.True(SpaceRepoCommit.FromIndex(repo.Index).Matches(repo.Commit));
    }

    [RequiresSpacesFact]
    public async Task GetRepoAsync_AfterADelete_VerifiesAgainstTheAdvancedCommit()
    {
        var space = await fixture.CreateSpaceAsync("car-delete", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Member, space, "keep", "keep");
        await fixture.WriteAsync(fixture.Member, space, "drop", "drop");

        await fixture.Member.Client.Space.DeleteRecordAsync(
            space.Value, fixture.Member.Did, SpaceNetworkFixture.Collection, "drop");

        await using var provider = fixture.CreateProvider(fixture.Authority);
        using var reader = await provider.CreateReaderForRepoAsync(space, fixture.Member.Did);

        var car = await DownloadRepoAsync(reader, space, fixture.Member.Did);
        var didKey = await fixture.ResolveSigningKeyAsync(fixture.Member.Did);

        // Removal is subtraction from the set hash rather than a recomputation, on both sides.
        // If the SDK's lane arithmetic disagreed with the server's, this is where it would show.
        var repo = SpaceRepoCar.Verify(car, space, fixture.Member.Did, didKey);

        Assert.Single(repo.Records);
        Assert.Equal($"{SpaceNetworkFixture.Collection}/keep", repo.Records[0].Path);
    }

    [RequiresSpacesFact]
    public async Task SyncRepoAsync_ReplaysTheOplogToTheRepoSignedCommit()
    {
        var space = await fixture.CreateSpaceAsync("sync-replay", members: [fixture.Member]);
        for (var i = 0; i < 3; i++)
            await fixture.WriteAsync(fixture.Member, space, $"op {i}", $"op-{i}");

        var store = new InMemorySpaceRepoStore();
        var syncer = new SpaceSyncer(space, store, fixture.ResolveSigningKeyAsync);
        var cursor = new SpaceRepoCursor(fixture.Member.Did);

        await using var provider = fixture.CreateProvider(fixture.Authority);
        using var reader = await provider.CreateReaderForRepoAsync(space, fixture.Member.Did);

        var result = await syncer.SyncRepoAsync(reader.Space, cursor);

        Assert.Equal(SpaceSyncOutcome.UpToDate, result.Outcome);
        Assert.Equal(3, result.Ops.Count);
        Assert.Equal(3, store.Count(fixture.Member.Did));
        Assert.NotNull(result.Commit);

        // The digest the SDK folded from the oplog is the one the host signed. Nothing else in
        // the pass asserts the two implementations agree on what the repo now contains.
        Assert.True(cursor.Commit.Matches(result.Commit!));
        Assert.Equal(result.Commit!.Rev, cursor.Rev);
    }

    [RequiresSpacesFact]
    public async Task SyncRepoAsync_ResumesFromItsCursorAndAppliesOnlyWhatIsNew()
    {
        var space = await fixture.CreateSpaceAsync("sync-resume", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Member, space, "first", "first");

        var store = new InMemorySpaceRepoStore();
        var syncer = new SpaceSyncer(space, store, fixture.ResolveSigningKeyAsync);
        var cursor = new SpaceRepoCursor(fixture.Member.Did);

        await using var provider = fixture.CreateProvider(fixture.Authority);
        using var reader = await provider.CreateReaderForRepoAsync(space, fixture.Member.Did);

        await syncer.SyncRepoAsync(reader.Space, cursor);

        await fixture.WriteAsync(fixture.Member, space, "second", "second");
        await fixture.Member.Client.Space.DeleteRecordAsync(
            space.Value, fixture.Member.Did, SpaceNetworkFixture.Collection, "first");

        var caught = await syncer.SyncRepoAsync(reader.Space, cursor);

        Assert.Equal(SpaceSyncOutcome.UpToDate, caught.Outcome);
        Assert.Equal(2, caught.Ops.Count);
        Assert.Equal(1, store.Count(fixture.Member.Did));
        Assert.True(cursor.Commit.Matches(caught.Commit!));

        // A cursor survives a restart as a rev plus a set-hash state, so a syncer that persists
        // those two values resumes exactly where it left off.
        var restored = new SpaceRepoCursor(fixture.Member.Did, cursor.Rev, cursor.GetState());
        var idle = await syncer.SyncRepoAsync(reader.Space, restored);

        Assert.Equal(SpaceSyncOutcome.UpToDate, idle.Outcome);
        Assert.Empty(idle.Ops);
    }

    [RequiresSpacesFact]
    public async Task SyncRepoAsync_WhenTheLocalCopyDiverges_RecoversInFull()
    {
        var space = await fixture.CreateSpaceAsync("sync-diverge", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Member, space, "missed", "missed");

        var store = new InMemorySpaceRepoStore();
        var syncer = new SpaceSyncer(space, store, fixture.ResolveSigningKeyAsync);
        var cursor = new SpaceRepoCursor(fixture.Member.Did);

        await using var provider = fixture.CreateProvider(fixture.Authority);
        using var reader = await provider.CreateReaderForRepoAsync(space, fixture.Member.Did);

        await syncer.SyncRepoAsync(reader.Space, cursor);
        await fixture.WriteAsync(fixture.Member, space, "later", "later");

        // A copy that reached the same rev while holding nothing: the operation it never saw is
        // exactly what a dropped notification or a compacted oplog leaves behind. The digests
        // disagree, and disagreement alone is enough to trigger the repair.
        var diverged = new SpaceRepoCursor(fixture.Member.Did, cursor.Rev, state: default);
        var result = await syncer.SyncRepoAsync(reader.Space, diverged);

        Assert.Equal(SpaceSyncOutcome.Recovered, result.Outcome);
        Assert.NotNull(result.RecoveredRepo);
        Assert.Equal(2, result.RecoveredRepo!.Records.Count);
        Assert.Equal(2, store.Count(fixture.Member.Did));
        Assert.True(diverged.Commit.Matches(result.Commit!));
    }

    [RequiresSpacesFact]
    public async Task SyncRepoAsync_WithASinceTheHostCannotServe_RecoversInFull()
    {
        var space = await fixture.CreateSpaceAsync("sync-since", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Member, space, "one", "one");
        await fixture.WriteAsync(fixture.Member, space, "two", "two");

        var store = new InMemorySpaceRepoStore();
        var syncer = new SpaceSyncer(space, store, fixture.ResolveSigningKeyAsync);

        await using var provider = fixture.CreateProvider(fixture.Authority);
        using var reader = await provider.CreateReaderForRepoAsync(space, fixture.Member.Did);

        // The oplog is a transport optimization with no history guarantee — a host may compact
        // it, and it does not survive migration. Pruning it is not reachable over the wire, but
        // a `since` the host refuses drives the same fallback, which is the branch that matters:
        // an unserviceable cursor must end in a full download rather than an exception.
        var stale = new SpaceRepoCursor(fixture.Member.Did, "not-a-revision", state: default);
        var result = await syncer.SyncRepoAsync(reader.Space, stale);

        Assert.Equal(SpaceSyncOutcome.Recovered, result.Outcome);
        Assert.Equal(2, store.Count(fixture.Member.Did));
        Assert.True(stale.Commit.Matches(result.Commit!));
        Assert.Equal(result.Commit!.Rev, stale.Rev);
    }

    [RequiresSpacesFact]
    public async Task SyncRepoAsync_ForAnAccountThatHasWrittenNothing_AppliesNothing()
    {
        var space = await fixture.CreateSpaceAsync("sync-norepo", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Authority, space, "only the authority wrote here");

        var store = new InMemorySpaceRepoStore();
        var syncer = new SpaceSyncer(space, store, fixture.ResolveSigningKeyAsync);
        var cursor = new SpaceRepoCursor(fixture.Member.Did);

        await using var provider = fixture.CreateProvider(fixture.Authority);
        using var reader = await provider.CreateReaderForRepoAsync(space, fixture.Member.Did);

        // A member who has written nothing has no repo to build a commit over, so the oplog
        // answers with neither operations nor a commit rather than refusing the read.
        var result = await syncer.SyncRepoAsync(reader.Space, cursor);

        Assert.Empty(result.Ops);
        Assert.Null(result.Commit);
        Assert.Null(cursor.Rev);
        Assert.Equal(0, store.Count(fixture.Member.Did));

        // Recovering in full is what does report the absence — `getRepo` answers `RepoNotFound`,
        // and the syncer drops whatever the caller held for that repo.
        var recovered = await syncer.RecoverAsync(reader.Space, cursor);

        // Deliberately the same answer a caller who may not read the repo gets. Whether an
        // account holds one is not something an unauthorized caller may learn.
        Assert.Equal(SpaceSyncOutcome.NoRepo, recovered.Outcome);
        Assert.Equal(0, store.Count(fixture.Member.Did));
    }

    [RequiresSpacesFact]
    public async Task ListReposAsync_EnumeratesTheWriterSetTheAuthorityRecorded()
    {
        var space = await fixture.CreateSpaceAsync("writer-set", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Member, space, "makes me a writer");

        await using var provider = fixture.CreateProvider(fixture.Authority);
        using var host = await provider.CreateReaderAsync(space, fixture.PdsUrl);

        // The writer set is maintained from write notifications, which are fire-and-forget, so
        // it is eventually consistent by design — poll rather than expecting it immediately.
        var writers = await Poll(
            async () =>
            {
                var page = await host.Space.ListReposAsync(space.Value);
                return page.Repos;
            },
            repos => repos.Any(repo => repo.Did == fixture.Member.Did));

        var writer = Assert.Single(writers, repo => repo.Did == fixture.Member.Did);
        Assert.NotEmpty(writer.Rev);
        Assert.NotEmpty(writer.Hash);

        // Each entry carries the repo's current rev, which is what lets a sweep re-sync only the
        // repos that advanced instead of polling every one of them.
        var commit = await host.Space.GetLatestCommitAsync(space.Value, fixture.Member.Did);
        Assert.Equal(commit.Commit.Rev, writer.Rev);
    }

    private static async Task<byte[]> DownloadRepoAsync(
        SpaceReader reader, SpaceUri space, string repo, bool? excludeValues = null)
    {
        await using var stream = await reader.Space.GetRepoAsync(space.Value, repo, excludeValues);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static async Task<T> Poll<T>(
        Func<Task<T>> read, Func<T, bool> until, int attempts = 50, int delayMs = 100)
    {
        var value = await read();
        for (var i = 0; i < attempts && !until(value); i++)
        {
            await Task.Delay(delayMs);
            value = await read();
        }

        return value;
    }
}

/// <summary>
/// The caller's copy of a space, held in memory — the smallest thing a
/// <see cref="SpaceSyncer"/> can drive.
/// </summary>
internal sealed class InMemorySpaceRepoStore : ISpaceRepoStore
{
    private readonly Dictionary<string, Dictionary<string, string>> _repos = [];

    public int Count(string repo) => _repos.TryGetValue(repo, out var records) ? records.Count : 0;

    public IReadOnlyDictionary<string, string> Records(string repo) =>
        _repos.TryGetValue(repo, out var records) ? records : new Dictionary<string, string>();

    public Task ApplyAsync(SpaceUri space, string repo, SpaceRepoOpEntry op, CancellationToken cancellationToken)
    {
        if (!_repos.TryGetValue(repo, out var records))
            _repos[repo] = records = new Dictionary<string, string>(StringComparer.Ordinal);

        var path = $"{op.Collection}/{op.Rkey}";
        if (op.Cid is null)
            records.Remove(path);
        else
            records[path] = op.Cid;

        return Task.CompletedTask;
    }

    public Task ReplaceAsync(
        SpaceUri space, string repo, VerifiedSpaceRepo contents, CancellationToken cancellationToken)
    {
        _repos[repo] = contents.Index.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        return Task.CompletedTask;
    }

    public Task DropAsync(SpaceUri space, string repo, CancellationToken cancellationToken)
    {
        _repos.Remove(repo);
        return Task.CompletedTask;
    }
}
