using System.Runtime.CompilerServices;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

/// <summary>
/// <see cref="TypedFirehoseConsumer"/> with a <see cref="RepoSyncVerifier"/>: only chained events
/// are delivered, state is recorded on delivery, and broken repositories are fetched again.
/// </summary>
public sealed class TypedFirehoseConsumerSyncTests : IDisposable
{
    private const string TickDid = "did:plc:ticktickticktickticktick";

    private readonly SyncTestRepo _repo = new();
    private readonly StubDidResolver _resolver = new();
    private readonly InMemoryRepoSyncStateStore _store = new();
    private readonly List<DroppedStreamEvent> _dropped = [];
    private readonly List<RepoSyncResult> _desynchronized = [];

    public TypedFirehoseConsumerSyncTests() => _resolver.Add(_repo.Did, _repo.SigningKey);

    public void Dispose() => _repo.Dispose();

    private RepoSyncVerifier Verifier() => new(new RepoSyncVerifierOptions
    {
        StateStore = _store,
        DidResolver = _resolver,
        OnDesynchronized = _desynchronized.Add,
    });

    private TypedFirehoseConsumer Consumer(
        StreamConnector connector, RepoSyncVerifier verifier, RepoResyncOptions? resync = null, IReadOnlySet<Nsid>? filter = null,
        IStreamCursorStore? cursorStore = null) => new(
        new TypedFirehoseConsumerOptions
        {
            ServiceUrl = "wss://relay.example.com",
            SyncVerifier = verifier,
            Resync = resync,
            CollectionFilter = filter,
            CursorStore = cursorStore,
            CursorPersistInterval = 1,
            Reconnect = StreamTestExtensions.Immediate(0),
            OnEventDropped = _dropped.Add,
        },
        connector);

    private static StreamConnector Frames(params FirehoseEvent[] events) =>
        new ScriptedConnector().Connection([.. events.Select(SyncFrames.Of)]).Connect;

    /// <summary>A connection whose frames come from an async script, so it can wait on the test.</summary>
    private static StreamConnector Script(Func<IAsyncEnumerable<byte[]>> frames) =>
        (_, _, _) => Wrap(frames());

    private static async IAsyncEnumerable<StreamSocketMessage> Wrap(IAsyncEnumerable<byte[]> frames)
    {
        await foreach (var frame in frames)
        {
            await Task.Yield();
            yield return new StreamSocketMessage(frame, IsBinary: true);
        }
    }

    /// <summary>
    /// Unrelated frames, sent until <paramref name="until"/> completes: they keep the consumer's
    /// loop turning while a fetch runs in the background.
    /// </summary>
    private static async IAsyncEnumerable<byte[]> Ticks(Task until, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var seq = 100_000; seq < 102_000 && !until.IsCompleted; seq++)
        {
            await Task.Delay(5, cancellationToken);
            yield return EventStreamFrames.IdentityFrame(seq, TickDid);
        }
    }

    /// <summary>Collects every message until the script ends, calling <paramref name="onEach"/> on each.</summary>
    private static async Task<List<FirehoseMessage>> Run(IAsyncEnumerable<FirehoseMessage> stream, Action<FirehoseMessage>? onEach = null)
    {
        var messages = new List<FirehoseMessage>();
        try
        {
            await foreach (var message in stream)
            {
                messages.Add(message);
                onEach?.Invoke(message);
            }
        }
        catch (EventStreamException ex) when (ex.GetType() == typeof(EventStreamException) && ex.Error is null && ex.InnerException is null)
        {
            // The script ran out and the policy allows no reconnect.
        }

        return messages;
    }

    private static List<FirehoseMessage> Repository(IEnumerable<FirehoseMessage> messages) =>
        [.. messages.Where(m => m is not IdentityEvent { Did.Value: TickDid })];

    // ── Verification ─────────────────────────────────────────

    [Fact]
    public async Task ConsumeAsync_ChainedCommits_AreDelivered()
    {
        using var verifier = Verifier();
        var events = new FirehoseEvent[] { _repo.Create(), _repo.Create(), _repo.Sync(), _repo.Create() };

        var messages = await Consumer(Frames(events), verifier).ConsumeAsync().DrainAsync();

        Assert.Equal(events.Select(e => e.Seq), messages.OfType<FirehoseEvent>().Select(e => e.Seq));
        Assert.Empty(_dropped);
        Assert.Equal(new RepoSyncState(_repo.Did, _repo.Rev, _repo.Data, RepoSyncStatus.Synchronized), await _store.GetAsync(_repo.Did));
    }

    [Fact]
    public async Task ConsumeAsync_LiveFrames_AreDelivered()
    {
        using var verifier = new RepoSyncVerifier(new RepoSyncVerifierOptions { StateStore = _store, DidResolver = LiveFrames.Resolver() });
        // In stream order: the fixture lists them by kind.
        StreamConnector connector = new ScriptedConnector()
            .Connection([.. LiveFrames.Names.OrderBy(n => LiveFrames.Event(n).Seq).Select(LiveFrames.Frame)]).Connect;

        var messages = await Consumer(connector, verifier).ConsumeAsync().DrainAsync();

        Assert.Equal(LiveFrames.Names.Count(), messages.Count);
        Assert.Empty(_dropped);
    }

    [Fact]
    public async Task ConsumeAsync_RecordsTheStateOnlyOnceTheNextMessageIsAskedFor()
    {
        using var verifier = Verifier();
        var first = _repo.Create();
        var second = _repo.Create();

        await using var messages = Consumer(Frames(first, second), verifier).ConsumeAsync().GetAsyncEnumerator();

        Assert.True(await messages.MoveNextAsync());
        Assert.Null(await _store.GetAsync(_repo.Did));

        Assert.True(await messages.MoveNextAsync());
        Assert.Equal(first.Rev, (await _store.GetAsync(_repo.Did))!.Rev);
    }

    [Fact]
    public async Task ConsumeAsync_InvalidStaleAndDesynchronized_AreDroppedWithTheirReasons()
    {
        using var forger = ATProtoNet.Crypto.AtProtoCrypto.GenerateK256Key();
        using var verifier = Verifier();
        var first = _repo.Create();
        var forged = _repo.Create(new CommitTweaks { SignWith = forger });
        var afterGap = _repo.Create();

        // The replayed commit reaches the consumer under a later sequence number.
        var messages = await Consumer(Frames(first, forged, Reissue(first, 50_000), Reissue(afterGap, 60_000)), verifier)
            .ConsumeAsync().DrainAsync();

        Assert.Equal([first.Seq], messages.OfType<FirehoseEvent>().Select(e => e.Seq));
        Assert.Equal(
            [StreamDropReason.VerificationFailed, StreamDropReason.Stale, StreamDropReason.Desynchronized],
            _dropped.Select(d => d.Reason));
        Assert.All(_dropped, d => Assert.Contains(_repo.Did.Value, d.Detail));
        Assert.Single(_desynchronized);
    }

    private static CommitEvent Reissue(CommitEvent commit, long seq) => new()
    {
        Seq = seq,
        Repo = commit.Repo,
        Commit = commit.Commit,
        Rev = commit.Rev,
        Since = commit.Since,
        Blocks = commit.Blocks,
        Ops = commit.Ops,
        PrevData = commit.PrevData,
    };

    [Fact]
    public async Task ConsumeAsync_CollectionFilter_StillChainsTheCommitsItSkips()
    {
        using var verifier = Verifier();
        var wanted = _repo.Commit((RepoOpAction.Create, _repo.NewPath("com.example.wanted")));
        var skipped = _repo.Commit((RepoOpAction.Create, _repo.NewPath("com.example.other")));
        var next = _repo.Commit((RepoOpAction.Create, _repo.NewPath("com.example.wanted")));

        var messages = await Consumer(Frames(wanted, skipped, next), verifier,
            filter: new HashSet<Nsid> { Nsid.Parse("com.example.wanted") }).ConsumeAsync().DrainAsync();

        Assert.Equal([wanted.Seq, next.Seq], messages.OfType<FirehoseEvent>().Select(e => e.Seq));
        Assert.Empty(_dropped);
    }

    [Fact]
    public async Task ConsumeAsync_IdentityEvent_InvalidatesTheSyncVerifiersCache()
    {
        using var verifier = Verifier();

        await Consumer(new ScriptedConnector().Connection(EventStreamFrames.IdentityFrame(1, _repo.Did.Value)).Connect, verifier)
            .ConsumeAsync().DrainAsync();

        Assert.Equal(1, _resolver.Invalidations);
    }

    [Fact]
    public void Options_VerifierAndSyncVerifier_AreExclusive()
    {
        using var verifier = Verifier();
        using var firehoseVerifier = new FirehoseVerifier(_resolver);

        Assert.Throws<ArgumentException>(() => new TypedFirehoseConsumer(new TypedFirehoseConsumerOptions
        {
            ServiceUrl = "wss://relay.example.com",
            Verifier = firehoseVerifier,
            SyncVerifier = verifier,
        }));
    }

    [Fact]
    public void Options_ResyncWithoutSyncVerifier_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TypedFirehoseConsumer(new TypedFirehoseConsumerOptions
        {
            ServiceUrl = "wss://relay.example.com",
            Resync = new RepoResyncOptions(),
        }));
    }

    // ── Resynchronization ────────────────────────────────────

    [Fact]
    public async Task ConsumeAsync_BrokenChain_DeliversTheSnapshotThenTheEventsHeldMeanwhile()
    {
        using var verifier = Verifier();
        var c1 = _repo.Create();
        _repo.Create(); // missed
        var c3 = _repo.Create();
        var export = _repo.Export();
        var c4 = _repo.Create();

        // Sequenced after the ticks, as the relay would.
        var c5 = Reissue(_repo.Create(), 200_000);

        var resynced = new TaskCompletionSource();
        var fetcher = new ScriptedRepoFetcher(_ => Task.FromResult<byte[]?>(export));

        var messages = await Run(
            Consumer(
                Script(() => Concat([SyncFrames.Of(c1), SyncFrames.Of(c3), SyncFrames.Of(c4)], Ticks(resynced.Task), [SyncFrames.Of(c5)])),
                verifier, new RepoResyncOptions { Fetchers = [fetcher] }).ConsumeAsync(),
            message =>
            {
                if (message is RepoResyncEvent)
                    resynced.TrySetResult();
            });

        var resync = Assert.IsType<RepoResyncEvent>(Repository(messages)[1]);
        Assert.Equal(c3.Rev, resync.Snapshot.Rev);
        Assert.Equal(3, resync.Snapshot.Count);
        Assert.Contains("prevData", resync.Reason);
        Assert.Equal([c1.Seq, c4.Seq, c5.Seq], Repository(messages).OfType<CommitEvent>().Select(c => c.Seq));

        // c3 was held, then found covered by the snapshot.
        Assert.Equal([StreamDropReason.Stale], _dropped.Select(d => d.Reason));
        Assert.Equal(1, fetcher.Requests);
        Assert.Equal(new RepoSyncState(_repo.Did, c5.Rev, _repo.Data, RepoSyncStatus.Synchronized), await _store.GetAsync(_repo.Did));
    }

    [Fact]
    public async Task ConsumeAsync_StopBetweenSnapshotAndHeldEvents_ReplaysTheHeldEvents()
    {
        using var verifier = Verifier();
        var cursors = new InMemoryStreamCursorStore();
        var c1 = _repo.Create();
        _repo.Create(); // missed
        var c3 = _repo.Create();
        var export = _repo.Export();
        var c4 = _repo.Create();
        var c5 = Reissue(_repo.Create(), 200_000);

        // The process stops once the snapshot is recorded, holding c4 but before processing it.
        var resynced = new TaskCompletionSource();
        var fetcher = new ScriptedRepoFetcher(_ => Task.FromResult<byte[]?>(export));
        await foreach (var message in Consumer(
            Script(() => Concat([SyncFrames.Of(c1), SyncFrames.Of(c3), SyncFrames.Of(c4)], Ticks(resynced.Task), [])),
            verifier, new RepoResyncOptions { Fetchers = [fetcher] }, cursorStore: cursors).ConsumeAsync())
        {
            if (message is RepoResyncEvent)
                resynced.TrySetResult();
            if (message is CommitEvent { Seq: var seq } && seq == c4.Seq)
                break;
        }

        // The ticks after c4 were passed, but the saved cursor stays below it.
        var saved = await cursors.GetCursorAsync("wss://relay.example.com");
        Assert.True(saved < c4.Seq, $"The cursor was saved at {saved}, past the undelivered {c4.Seq}.");
        Assert.Equal(RepoSyncStatus.Synchronized, (await _store.GetAsync(_repo.Did))!.Status);

        // After the restart the relay replays from the saved cursor, and c4 chains on the snapshot.
        var replay = await Consumer(Frames(c4, c5), verifier, cursorStore: cursors).ConsumeAsync().DrainAsync();

        Assert.Equal([c4.Seq, c5.Seq], replay.OfType<CommitEvent>().Select(c => c.Seq));
        Assert.Equal(c5.Rev, (await _store.GetAsync(_repo.Did))!.Rev);
    }

    [Fact]
    public async Task ConsumeAsync_FailedFetch_LeavesTheRepositoryDesynchronized()
    {
        using var verifier = Verifier();
        var c1 = _repo.Create();
        _repo.Create();
        var c3 = _repo.Create();
        var c4 = Reissue(_repo.Create(), 200_000);

        var failed = new TaskCompletionSource();
        var fetcher = new ScriptedRepoFetcher(_ =>
        {
            failed.TrySetResult();
            throw new RepoFetchException("The host is down.");
        });

        var messages = await Run(Consumer(
            Script(() => Concat([SyncFrames.Of(c1), SyncFrames.Of(c3)], Ticks(Task.Delay(300)), [SyncFrames.Of(c4)])),
            verifier,
            new RepoResyncOptions { Fetchers = [fetcher], RetryDelay = TimeSpan.FromHours(1) }).ConsumeAsync());

        Assert.True(failed.Task.IsCompleted);
        Assert.DoesNotContain(messages, m => m is RepoResyncEvent);
        Assert.Equal([c1.Seq], Repository(messages).OfType<CommitEvent>().Select(c => c.Seq));
        Assert.Equal(RepoSyncStatus.Desynchronized, (await _store.GetAsync(_repo.Did))!.Status);

        // The held c3, then c4 while the retry waits, are reported as they are dropped.
        Assert.Equal([StreamDropReason.Desynchronized, StreamDropReason.Desynchronized], _dropped.Select(d => d.Reason));
        Assert.Equal(1, fetcher.Requests);
    }

    [Fact]
    public async Task ConsumeAsync_RepositoryMarkedInTheStore_IsFetchedAndDelivered()
    {
        using var verifier = Verifier();
        _repo.Create();
        _repo.Create();
        await _store.SetAsync(new RepoSyncState(_repo.Did, null, null, RepoSyncStatus.Desynchronized));

        var resynced = new TaskCompletionSource();
        var fetcher = new ScriptedRepoFetcher(_ => Task.FromResult<byte[]?>(_repo.Export()));
        var messages = await Run(
            Consumer(Script(() => Ticks(resynced.Task)), verifier, new RepoResyncOptions { Fetchers = [fetcher] }).ConsumeAsync(),
            message =>
            {
                if (message is RepoResyncEvent)
                    resynced.TrySetResult();
            });

        var resync = Assert.Single(messages.OfType<RepoResyncEvent>());
        Assert.Null(resync.Reason);
        Assert.Equal(2, resync.Snapshot.Records.Count());
        Assert.Equal(new RepoSyncState(_repo.Did, _repo.Rev, _repo.Data, RepoSyncStatus.Synchronized), await _store.GetAsync(_repo.Did));
    }

    [Fact]
    public async Task ConsumeAsync_StaleCopyFromTheRelay_FallsBackToTheNextFetcher()
    {
        using var verifier = Verifier();
        var c1 = _repo.Create();
        var old = _repo.Export();
        _repo.Create();
        var c3 = _repo.Create();
        var current = _repo.Export();

        var relay = new ScriptedRepoFetcher(_ => Task.FromResult<byte[]?>(old));
        var pds = new ScriptedRepoFetcher(_ => Task.FromResult<byte[]?>(current));
        var resynced = new TaskCompletionSource();
        var messages = await Run(
            Consumer(
                Script(() => Concat([SyncFrames.Of(c1), SyncFrames.Of(c3)], Ticks(resynced.Task), [])),
                verifier, new RepoResyncOptions { Fetchers = [relay, pds] }).ConsumeAsync(),
            message =>
            {
                if (message is RepoResyncEvent)
                    resynced.TrySetResult();
            });

        var resync = Assert.Single(messages.OfType<RepoResyncEvent>());
        Assert.Equal(c3.Rev, resync.Snapshot.Rev);
        Assert.Equal(1, relay.Requests);
        Assert.Equal(1, pds.Requests);
    }

    [Fact]
    public async Task ConsumeAsync_FetcherThatNeverAnswers_TimesOutAndTheNextOneServes()
    {
        using var verifier = Verifier();
        var c1 = _repo.Create();
        _repo.Create();
        var c3 = _repo.Create();
        var export = _repo.Export();

        // A custom fetcher that never answers on its own: only FetchTimeout stops it.
        var hangingWithToken = new TokenRespectingFetcher();
        var good = new ScriptedRepoFetcher(_ => Task.FromResult<byte[]?>(export));
        var resynced = new TaskCompletionSource();

        var messages = await Run(
            Consumer(
                Script(() => Concat([SyncFrames.Of(c1), SyncFrames.Of(c3)], Ticks(resynced.Task), [])),
                verifier, new RepoResyncOptions { Fetchers = [hangingWithToken, good], FetchTimeout = TimeSpan.FromMilliseconds(200) }).ConsumeAsync(),
            message =>
            {
                if (message is RepoResyncEvent)
                    resynced.TrySetResult();
            });

        Assert.Single(messages.OfType<RepoResyncEvent>());
        Assert.Equal(1, hangingWithToken.Requests);
        Assert.Equal(1, good.Requests);
    }

    /// <summary>A fetcher that answers only when its token is cancelled.</summary>
    private sealed class TokenRespectingFetcher : IRepoFetcher
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        public async Task<byte[]?> FetchAsync(Did did, long maxBytes, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _requests);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }

    [Fact]
    public async Task ConsumeAsync_HeldEventsOverflow_FetchesTheRepositoryAgain()
    {
        // Every state recorded, with how many fetches had started by then.
        var recorded = new List<(RepoSyncStatus Status, int Fetches)>();
        ScriptedRepoFetcher? fetcherRef = null;
        using var verifier = new RepoSyncVerifier(new RepoSyncVerifierOptions
        {
            StateStore = new RecordingStore(_store, state => recorded.Add((state.Status, fetcherRef?.Requests ?? 0))),
            DidResolver = _resolver,
        });
        var c1 = _repo.Create();
        _repo.Create();
        var c3 = _repo.Create();
        var c4 = _repo.Create();
        var c5 = _repo.Create();

        // The first fetch waits until everything has arrived, so c4 and c5 overflow the one slot.
        var release = new TaskCompletionSource();
        var fetcher = new ScriptedRepoFetcher(async _ =>
        {
            await release.Task;
            return _repo.Export();
        });
        fetcherRef = fetcher;

        var done = new TaskCompletionSource();
        var resyncs = 0;
        var messages = await Run(
            Consumer(
                Script(() => Concat([SyncFrames.Of(c1), SyncFrames.Of(c3), SyncFrames.Of(c4), SyncFrames.Of(c5)], Release(release), Ticks(done.Task))),
                verifier, new RepoResyncOptions { Fetchers = [fetcher], MaxHeldEventsPerRepo = 1 }).ConsumeAsync(),
            message =>
            {
                if (message is RepoResyncEvent && ++resyncs == 2)
                    done.TrySetResult();
            });

        Assert.Equal(2, messages.OfType<RepoResyncEvent>().Count());
        Assert.Equal(2, fetcher.Requests);
        Assert.Equal(RepoSyncStatus.Synchronized, (await _store.GetAsync(_repo.Did))!.Status);

        // The first snapshot is recorded as desynchronized: the events that overflowed are not in
        // it, so a process stopping before the refetch must fetch again.
        Assert.All(recorded.Where(r => r.Status == RepoSyncStatus.Synchronized && r.Fetches > 0), r => Assert.Equal(2, r.Fetches));
    }

    /// <summary>Passes everything to <paramref name="inner"/>, reporting each state set.</summary>
    private sealed class RecordingStore(IRepoSyncStateStore inner, Action<RepoSyncState> onSet) : IRepoSyncStateStore
    {
        public ValueTask<RepoSyncState?> GetAsync(Did did, CancellationToken cancellationToken = default) => inner.GetAsync(did, cancellationToken);

        public ValueTask SetAsync(RepoSyncState state, CancellationToken cancellationToken = default)
        {
            onSet(state);
            return inner.SetAsync(state, cancellationToken);
        }

        public ValueTask RemoveAsync(Did did, CancellationToken cancellationToken = default) => inner.RemoveAsync(did, cancellationToken);

        public ValueTask<IReadOnlyList<RepoSyncState>> ListUnsynchronizedAsync(int limit, CancellationToken cancellationToken = default) =>
            inner.ListUnsynchronizedAsync(limit, cancellationToken);
    }

    [Fact]
    public async Task ConsumeAsync_FetchedSnapshotNotYetTaken_KeepsItsConcurrencySlot()
    {
        using var other = new SyncTestRepo("did:plc:othersynctestrepoaaaaaaa");
        _resolver.Add(other.Did, other.SigningKey);
        using var verifier = Verifier();

        var a1 = _repo.Create();
        _repo.Create();
        var a3 = _repo.Create();
        var aExport = _repo.Export();
        var b1 = other.Create();
        other.Create();
        var b3 = other.Create();
        var bExport = other.Export();

        // A's download finishes only once the consumer is blocked waiting for a frame, so its
        // snapshot waits there, untaken.
        var requested = new System.Collections.Concurrent.ConcurrentQueue<Did>();
        var aGo = new TaskCompletionSource();
        var fetcher = new ScriptedRepoFetcher(async did =>
        {
            requested.Enqueue(did);
            if (did == _repo.Did)
            {
                await aGo.Task;
                return aExport;
            }

            return bExport;
        });

        var blocked = new TaskCompletionSource();
        var open = new TaskCompletionSource();
        var done = new TaskCompletionSource();
        var resyncs = 0;
        var run = Run(
            Consumer(
                Script(() => Concat(
                    [SyncFrames.Of(Reissue(a1, 1)), SyncFrames.Of(Reissue(b1, 2)), SyncFrames.Of(Reissue(a3, 3)), SyncFrames.Of(Reissue(b3, 4))],
                    Gate(blocked, open.Task),
                    Ticks(done.Task))),
                verifier, new RepoResyncOptions { Fetchers = [fetcher], MaxConcurrency = 1 }).ConsumeAsync(),
            message =>
            {
                if (message is RepoResyncEvent && ++resyncs == 2)
                    done.TrySetResult();
            });

        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        aGo.SetResult();
        await Task.Delay(300);

        // One slot, and A's fetched snapshot still holds it: B has not started.
        Assert.Equal([_repo.Did], requested);

        open.SetResult();
        var messages = await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal([_repo.Did, other.Did], messages.OfType<RepoResyncEvent>().Select(r => r.Did));
        Assert.Equal([_repo.Did, other.Did], requested);
    }

    /// <summary>Signals <paramref name="reached"/> once the consumer asks for the next frame, then holds it until <paramref name="open"/>.</summary>
    private static async IAsyncEnumerable<byte[]> Gate(TaskCompletionSource reached, Task open)
    {
        reached.TrySetResult();
        await open;
        yield break;
    }

    private static async IAsyncEnumerable<byte[]> Release(TaskCompletionSource release)
    {
        await Task.Yield();
        release.TrySetResult();
        yield break;
    }

    private static async IAsyncEnumerable<byte[]> Concat(IEnumerable<byte[]> before, IAsyncEnumerable<byte[]> middle, IEnumerable<byte[]> after)
    {
        foreach (var frame in before)
            yield return frame;
        await foreach (var frame in middle)
            yield return frame;
        foreach (var frame in after)
            yield return frame;
    }

    private static async IAsyncEnumerable<byte[]> Concat(IEnumerable<byte[]> before, IAsyncEnumerable<byte[]> first, IAsyncEnumerable<byte[]> second)
    {
        foreach (var frame in before)
            yield return frame;
        await foreach (var frame in first)
            yield return frame;
        await foreach (var frame in second)
            yield return frame;
    }

    [Theory]
    [InlineData("wss://bsky.network", "https://bsky.network/")]
    [InlineData("wss://relay.example.com:8443/", "https://relay.example.com:8443/")]
    [InlineData("ws://localhost:2583", "http://localhost:2583/")]
    [InlineData("https://relay.example.com/xrpc", "https://relay.example.com/")]
    public void UpstreamOf_TheFirehoseUrl_IsTheRelaysHttpUrl(string serviceUrl, string expected)
    {
        Assert.Equal(new Uri(expected), RepoResyncCoordinator.UpstreamOf(serviceUrl));
    }
}
