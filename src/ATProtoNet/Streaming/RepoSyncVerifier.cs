using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;

namespace ATProtoNet.Streaming;

/// <summary>Configures a <see cref="RepoSyncVerifier"/>.</summary>
public sealed class RepoSyncVerifierOptions
{
    /// <summary>Where each repository's sync state is kept. Default: a new <see cref="InMemoryRepoSyncStateStore"/>.</summary>
    public IRepoSyncStateStore? StateStore { get; init; }

    /// <summary>Resolves signing keys. Default: the verifier's own <see cref="CachingDidResolver"/>. Pass a caching resolver: an uncached one makes a directory request for every commit.</summary>
    public IDidResolver? DidResolver { get; init; }

    /// <summary>How far a revision's timestamp may run ahead of this machine's clock before the event is rejected as coming from the future. Default: 5 minutes, as the reference relay allows.</summary>
    public TimeSpan MaxClockSkew { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The clock revisions are checked against. Default: <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>Invoked when an event breaks a repository's chain or resets it, with the reason. It is not invoked again for the events that follow while the repository stays desynchronized.</summary>
    public Action<RepoSyncResult>? OnDesynchronized { get; init; }
}

/// <summary>Verifies firehose <c>#commit</c> and <c>#sync</c> events inductively, as Sync 1.1 specifies: each commit is checked on its own and against the repository's previous state, so a consumer knows it has every change to a repository rather than just authentic ones.</summary>
/// <remarks>
/// <para>A <c>#commit</c> passes when its CAR is well formed and every block matches its CID
/// (at most 2,000,000 bytes and 200 operations), its commit block is the event's <c>commit</c> and
/// has version 3, the event's <c>repo</c> and <c>rev</c> match the signed commit, the revision is
/// not in the future (<see cref="RepoSyncVerifierOptions.MaxClockSkew"/>) and not older than the
/// last one verified, the signature verifies against the account's signing key (refetched once on
/// failure, in case the key rotated), every created or updated record is in the blocks and in the
/// tree, and, when the event carries <c>prevData</c>, inverting its operations against the partial
/// tree in the blocks lands exactly on <c>prevData</c>. A <c>#sync</c> event gets the same checks
/// of its commit block.</para>
/// <para>A commit that passes must then chain: its <c>prevData</c> must be the MST root of the last
/// verified revision. One that does not marks the repository
/// <see cref="RepoSyncStatus.Desynchronized"/>, and so does a <c>#sync</c> event that moves it to
/// a different tree; its records must then be fetched again. <c>since</c> is not required to match
/// when <c>prevData</c> does: an empty commit changes the revision and not the tree, so missing one
/// loses no record.</para>
/// <para>A commit without <c>prevData</c>, from a host that predates Sync 1.1, cannot be inverted,
/// so nothing proves that the operations it lists are all it made: a relay could strip
/// <c>prevData</c> and drop an operation unseen. Once a repository has a chain, such a commit
/// therefore desynchronizes it (the reference relay rejects it outright, as "missing prevData");
/// it is accepted, uninverted, only as the first commit of a repository with no state.</para>
/// <para>A repository the store has no state for starts its chain at its first valid event.</para>
/// <para>Verify and apply one repository's events in stream order. <see cref="TypedFirehoseConsumer"/>
/// does all of this when given a verifier through
/// <see cref="TypedFirehoseConsumerOptions.SyncVerifier"/>, and can fetch desynchronized
/// repositories again.</para>
/// <para>See: https://atproto.com/specs/sync</para>
/// </remarks>
public sealed class RepoSyncVerifier : IDisposable
{
    /// <summary>The most operations a <c>#commit</c> may carry.</summary>
    public const int MaxCommitOps = 200;

    /// <summary>The most bytes a <c>#commit</c> or <c>#sync</c> event's <c>blocks</c> may hold.</summary>
    public const int MaxBlocksBytes = 2_000_000;

    /// <summary>The most bytes one record block may hold.</summary>
    public const int MaxRecordBytes = 1_000_000;

    private const byte DagCborCodec = 0x71;

    private readonly IDidResolver _didResolver;
    private readonly bool _ownsResolver;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _maxClockSkew;
    private readonly Action<RepoSyncResult>? _onDesynchronized;

    /// <summary>Creates a verifier.</summary>
    /// <param name="options">The options. Defaults apply when omitted.</param>
    public RepoSyncVerifier(RepoSyncVerifierOptions? options = null)
    {
        options ??= new RepoSyncVerifierOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxClockSkew, TimeSpan.Zero, nameof(options.MaxClockSkew));

        StateStore = options.StateStore ?? new InMemoryRepoSyncStateStore();
        _ownsResolver = options.DidResolver is null;
        _didResolver = options.DidResolver ?? new CachingDidResolver();
        _timeProvider = options.TimeProvider ?? TimeProvider.System;
        _maxClockSkew = options.MaxClockSkew;
        _onDesynchronized = options.OnDesynchronized;
    }

    /// <summary>Where each repository's sync state is kept.</summary>
    public IRepoSyncStateStore StateStore { get; }

    // The resolver signing keys come from.
    internal IDidResolver DidResolver => _didResolver;

    /// <summary>Drops any cached DID document for an account, so its next event is verified against a freshly resolved key. Call it for every <c>#identity</c> event.</summary>
    /// <param name="did">The account whose identity changed.</param>
    public Task InvalidateIdentityAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);
        return _didResolver.InvalidateAsync(did, cancellationToken);
    }

    /// <summary>Verifies a <c>#commit</c> event, and checks that it chains on the repository's state.</summary>
    /// <param name="commit">The event.</param>
    /// <returns>
    /// The outcome. A <see cref="RepoSyncOutcome.Valid"/> commit's new state is not recorded until
    /// <see cref="ApplyAsync"/> is called for it, once the commit is processed, so a process that
    /// stops in between verifies it again rather than skipping it. A commit that breaks the chain
    /// marks the repository desynchronized at once.
    /// </returns>
    public async ValueTask<RepoSyncResult> VerifyCommitAsync(CommitEvent commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var did = commit.Repo;

        if (commit.Ops is { Count: > MaxCommitOps } tooMany)
            return Invalid(did, commit.Rev, $"The commit has {tooMany.Count} operations; at most {MaxCommitOps} are allowed.");

        if (ReadCommit(commit.Blocks, commit.Commit, did, commit.Rev, out var car, out var block) is { } error)
            return Invalid(did, commit.Rev, error);

        var state = await StateStore.GetAsync(did, cancellationToken).ConfigureAwait(false);
        if (StaleReason(state, commit.Rev) is { } stale)
            return new RepoSyncResult(RepoSyncOutcome.Stale, did, commit.Rev, null, state, stale);

        if (await VerifySignatureAsync(did, block, cancellationToken).ConfigureAwait(false) is { } signatureError)
            return Invalid(did, commit.Rev, signatureError);

        if (CheckOperations(commit, car, block) is { } opsError)
            return Invalid(did, commit.Rev, opsError);

        var data = Cid.FromBytes(block.Data);
        var next = new RepoSyncState(did, commit.Rev, data, RepoSyncStatus.Synchronized);
        if (state is null)
            return new RepoSyncResult(RepoSyncOutcome.Valid, did, commit.Rev, data, next, null);

        if (state.Status != RepoSyncStatus.Synchronized)
            return new RepoSyncResult(RepoSyncOutcome.Desynchronized, did, commit.Rev, data, state, $"The repository is {Describe(state.Status)}.");

        if (commit.PrevData is { } prevData)
        {
            if (!prevData.Equals(state.Data))
                return await DesynchronizeAsync(did, commit.Rev, data,
                    $"The commit's prevData {prevData} is not the last verified tree {state.Data?.ToString() ?? "(unknown)"}.",
                    cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Without prevData the operations cannot be inverted, so the commit cannot show that it
            // lists everything it changed: a matching since would not catch a dropped operation.
            return await DesynchronizeAsync(did, commit.Rev, data,
                "The commit carries no prevData, so it cannot be shown to follow the last verified tree " +
                $"{state.Data?.ToString() ?? "(unknown)"}.",
                cancellationToken).ConfigureAwait(false);
        }

        return new RepoSyncResult(RepoSyncOutcome.Valid, did, commit.Rev, data, next, null);
    }

    /// <summary>Verifies a <c>#sync</c> event, which asserts a repository's current commit.</summary>
    /// <param name="sync">The event.</param>
    /// <returns>
    /// <see cref="RepoSyncOutcome.Valid"/> when the event confirms the tree already verified (or
    /// the repository has no state yet): record it with <see cref="ApplyAsync"/>.
    /// <see cref="RepoSyncOutcome.Desynchronized"/> when it moves the repository to another tree,
    /// which marks the repository desynchronized at once.
    /// </returns>
    public async ValueTask<RepoSyncResult> VerifySyncAsync(SyncEvent sync, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sync);
        var did = sync.Did;

        if (ReadCommit(sync.Blocks, expectedCommit: null, did, sync.Rev, out _, out var block) is { } error)
            return Invalid(did, sync.Rev, error);

        var state = await StateStore.GetAsync(did, cancellationToken).ConfigureAwait(false);
        if (StaleReason(state, sync.Rev) is { } stale)
            return new RepoSyncResult(RepoSyncOutcome.Stale, did, sync.Rev, null, state, stale);

        if (await VerifySignatureAsync(did, block, cancellationToken).ConfigureAwait(false) is { } signatureError)
            return Invalid(did, sync.Rev, signatureError);

        var data = Cid.FromBytes(block.Data);
        var next = new RepoSyncState(did, sync.Rev, data, RepoSyncStatus.Synchronized);
        if (state is null)
            return new RepoSyncResult(RepoSyncOutcome.Valid, did, sync.Rev, data, next, null);

        if (state.Status != RepoSyncStatus.Synchronized)
            return new RepoSyncResult(RepoSyncOutcome.Desynchronized, did, sync.Rev, data, state, $"The repository is {Describe(state.Status)}.");

        // The same tree at a later revision: nothing changed but the revision.
        if (data.Equals(state.Data))
            return new RepoSyncResult(RepoSyncOutcome.Valid, did, sync.Rev, data, next, null);

        return await DesynchronizeAsync(did, sync.Rev, data,
            $"A #sync event moved the repository to tree {data}, from {state.Data?.ToString() ?? "(unknown)"}.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records the state a <see cref="RepoSyncOutcome.Valid"/> event moved its repository to. Call it once the event is processed; for any other outcome it does nothing.</summary>
    /// <param name="result">The result of verifying the event.</param>
    public ValueTask ApplyAsync(RepoSyncResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result is { Outcome: RepoSyncOutcome.Valid, State: { } state }
            ? StateStore.SetAsync(state, cancellationToken)
            : ValueTask.CompletedTask;
    }

    // Sets a repository's status, keeping the revision and tree it was at, or recording neither when it
    // has no state.
    internal async ValueTask SetStatusAsync(Did did, RepoSyncStatus status, CancellationToken cancellationToken)
    {
        var state = await StateStore.GetAsync(did, cancellationToken).ConfigureAwait(false);
        if (state?.Status == status)
            return;

        await StateStore.SetAsync(
            state is null ? new RepoSyncState(did, null, null, status) : state with { Status = status },
            cancellationToken).ConfigureAwait(false);
    }

    // Verifies a repository commit's signature against the account's #atproto key, through
    // DidResolverExtensions.VerifyWithRefreshAsync. Returns the reason it failed, or null.
    internal async ValueTask<string?> VerifySignatureAsync(Did did, CommitBlock block, CancellationToken cancellationToken)
    {
        (bool Verified, string? Key) result;
        try
        {
            result = await _didResolver.VerifyWithRefreshAsync(
                did,
                static document => document.GetSigningKey(),
                key => AtProtoCrypto.TryVerifySignature(key, block.Unsigned, block.Signature),
                cancellationToken).ConfigureAwait(false);
        }
        catch (DidResolutionException ex)
        {
            return $"The account's identity could not be resolved: {ex.Message}";
        }

        return result switch
        {
            { Verified: true } => null,
            { Key: null } => "The account publishes no usable atproto signing key.",
            _ => "The commit's signature does not verify against the account's signing key.",
        };
    }

    private async ValueTask<RepoSyncResult> DesynchronizeAsync(
        Did did, Tid rev, Cid data, string reason, CancellationToken cancellationToken)
    {
        // The event is authentic, so it is the newest known state: anything older is stale.
        var state = new RepoSyncState(did, rev, data, RepoSyncStatus.Desynchronized);
        await StateStore.SetAsync(state, cancellationToken).ConfigureAwait(false);

        var result = new RepoSyncResult(RepoSyncOutcome.Desynchronized, did, rev, data, state, reason);
        _onDesynchronized?.Invoke(result);
        return result;
    }

    private static string? StaleReason(RepoSyncState? state, Tid rev) =>
        state?.Rev is { } last && rev.CompareTo(last) <= 0 ? $"The revision {rev} is not after the last one seen, {last}." : null;

    // Reads an event's CAR, checking every block against its CID, and the commit block it carries as its
    // first root, checking the commit against the event. Returns the reason it fails, or null.
    private string? ReadCommit(
        byte[]? blocks, Cid? expectedCommit, Did did, Tid rev, out CarReader car, out CommitBlock block)
    {
        car = null!;
        block = null!;

        if (blocks is not { Length: > 0 })
            return "The event carries no blocks.";

        if (blocks.Length > MaxBlocksBytes)
            return $"The event's blocks hold {blocks.Length} bytes; at most {MaxBlocksBytes} are allowed.";

        try
        {
            block = CommitBlock.FromCar(blocks, out car, expectedCommit);
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }

        if (!string.Equals(block.Did, did.Value, StringComparison.Ordinal))
            return $"The signed commit is for {block.Did}, not the event's {did}.";

        if (!string.Equals(block.Rev, rev.Value, StringComparison.Ordinal))
            return $"The signed commit's revision is {block.Rev}, not the event's {rev}.";

        // A TID holds microseconds since the epoch above its 10-bit clock identifier.
        var limit = (_timeProvider.GetUtcNow() + _maxClockSkew).ToUnixTimeMilliseconds() * 1000;
        return (rev.ToInt64() >> 10) > limit ? $"The revision {rev} lies in the future." : null;
    }

    // Checks a commit's operations against its blocks: well formed, every created or updated record
    // present and in the tree, and, with prevData, inverting them lands on it. Returns the reason they
    // fail, or null.
    private static string? CheckOperations(CommitEvent commit, CarReader car, CommitBlock block)
    {
        var ops = commit.Ops ?? [];
        var inductive = commit.PrevData is not null;

        // Normalized as the reference implementation inverts them: deletions first, then by path.
        var normalized = new List<RepoOp>(ops.Count);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in ops)
        {
            if (!IsValidPath(op.Path))
                return $"The operation path '{op.Path}' is not a valid record path.";
            if (!paths.Add(op.Path))
                return $"The commit has two operations on {op.Path}.";

            var shapeError = op.Action switch
            {
                RepoOpAction.Create when op.Cid is null || op.Prev is not null => "a create carries a cid and no prev",
                RepoOpAction.Update when op.Cid is null || (inductive && op.Prev is null) => "an update carries a cid and a prev",
                RepoOpAction.Update when op.Prev is not null && op.Prev.Equals(op.Cid) => "an update changes the record",
                RepoOpAction.Delete when op.Cid is not null || (inductive && op.Prev is null) => "a delete carries a prev and no cid",
                _ => null,
            };

            if (shapeError is not null)
                return $"The {op.Action.ToString().ToLowerInvariant()} of {op.Path} is malformed: {shapeError}.";

            if (op.Cid is { } cid && CheckRecord(car, cid, op.Path) is { } recordError)
                return recordError;

            normalized.Add(op);
        }

        normalized.Sort(static (a, b) =>
        {
            var byKind = (a.Action == RepoOpAction.Delete ? 0 : 1).CompareTo(b.Action == RepoOpAction.Delete ? 0 : 1);
            return byKind != 0 ? byKind : string.CompareOrdinal(a.Path, b.Path);
        });

        PartialMerkleSearchTree tree;
        try
        {
            tree = PartialMerkleSearchTree.Load(block.Data, cid => car.FindBlock(cid)?.Data);
        }
        catch (FormatException ex)
        {
            return $"The commit's tree is malformed: {ex.Message}";
        }

        try
        {
            if (!inductive)
            {
                // Without prevData nothing can be inverted; the tree must still show the operations.
                foreach (var op in normalized)
                {
                    var value = tree.Get(op.Path);
                    if (op.Cid is { } cid ? value is null || !cid.AsSpan().SequenceEqual(value) : value is not null)
                        return $"The tree does not show the {op.Action.ToString().ToLowerInvariant()} of {op.Path}.";
                }

                return null;
            }

            foreach (var op in normalized)
            {
                switch (op.Action)
                {
                    case RepoOpAction.Create:
                        if (tree.Remove(op.Path) is not { } created || !op.Cid!.AsSpan().SequenceEqual(created))
                            return $"The tree does not hold the created {op.Path} at {op.Cid}.";
                        break;

                    case RepoOpAction.Update:
                        if (tree.Insert(op.Path, op.Prev!.ToBytes()) is not { } updated || !op.Cid!.AsSpan().SequenceEqual(updated))
                            return $"The tree does not hold the updated {op.Path} at {op.Cid}.";
                        break;

                    case RepoOpAction.Delete:
                        if (tree.Insert(op.Path, op.Prev!.ToBytes()) is not null)
                            return $"The tree still holds the deleted {op.Path}.";
                        break;
                }
            }

            var inverted = tree.RootCid();
            if (!commit.PrevData!.AsSpan().SequenceEqual(inverted))
                return $"Inverting the operations gives tree {CidComputation.EncodeCidToString(inverted)}, " +
                       $"not the commit's prevData {commit.PrevData}.";

            return null;
        }
        catch (FormatException ex)
        {
            // A PartialTreeException: the blocks do not cover the operations. Or a malformed tree.
            return $"The commit's blocks do not prove its operations: {ex.Message}";
        }
    }

    // Checks that a created or updated record is in the blocks, as DAG-CBOR of a legal size.
    private static string? CheckRecord(CarReader car, Cid cid, string path)
    {
        var bytes = cid.AsSpan();
        if (bytes[1] != DagCborCodec)
            return $"The record {path} is not DAG-CBOR.";

        if (car.FindBlock(bytes) is not { } record)
            return $"The event's blocks do not include the record {path} ({cid}).";

        return record.Data.Length > MaxRecordBytes
            ? $"The record {path} holds {record.Data.Length} bytes; at most {MaxRecordBytes} are allowed."
            : null;
    }

    // Whether a path is collection/rkey with a valid NSID and record key.
    private static bool IsValidPath(string path)
    {
        var slash = path.IndexOf('/');
        return slash > 0
            && MerkleSearchTree.IsValidKey(path.AsSpan())
            && Nsid.TryParse(path[..slash], out _)
            && RecordKey.TryParse(path[(slash + 1)..], out _);
    }

    private static string Describe(RepoSyncStatus status) => status switch
    {
        RepoSyncStatus.Resynchronizing => "being resynchronized",
        _ => "desynchronized",
    };

    private static RepoSyncResult Invalid(Did did, Tid rev, string reason) =>
        new(RepoSyncOutcome.Invalid, did, rev, null, null, reason);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsResolver && _didResolver is IDisposable disposable)
            disposable.Dispose();
    }
}

/// <summary>What <see cref="RepoSyncVerifier"/> made of an event.</summary>
public enum RepoSyncOutcome
{
    /// <summary>The event is authentic and chains on the repository's state: deliver it, then record its state with <see cref="RepoSyncVerifier.ApplyAsync"/>.</summary>
    Valid,

    /// <summary>The event is no newer than the last one seen for the repository, such as a replay after a reconnect: ignore it.</summary>
    Stale,

    /// <summary>The event failed verification: reject it. The repository's state is unchanged.</summary>
    Invalid,

    /// <summary>The event is authentic, but the repository's chain is broken, by this event or earlier: its records must be fetched again before its events are processed.</summary>
    Desynchronized,
}

/// <summary>The outcome of verifying one <c>#commit</c> or <c>#sync</c> event with a <see cref="RepoSyncVerifier"/>.</summary>
public sealed class RepoSyncResult
{
    internal RepoSyncResult(RepoSyncOutcome outcome, Did did, Tid? rev, Cid? data, RepoSyncState? state, string? reason)
    {
        Outcome = outcome;
        Did = did;
        Rev = rev;
        Data = data;
        State = state;
        Reason = reason;
    }

    /// <summary>What to do with the event.</summary>
    public RepoSyncOutcome Outcome { get; }

    /// <summary>Whether the event is <see cref="RepoSyncOutcome.Valid"/>.</summary>
    public bool IsValid => Outcome == RepoSyncOutcome.Valid;

    /// <summary>The repository.</summary>
    public Did Did { get; }

    /// <summary>The event's revision.</summary>
    public Tid? Rev { get; }

    /// <summary>The MST root of the event's signed commit, once its blocks were read.</summary>
    public Cid? Data { get; }

    /// <summary>For a <see cref="RepoSyncOutcome.Valid"/> event, the state it moves the repository to, which <see cref="RepoSyncVerifier.ApplyAsync"/> records; otherwise the repository's current state, or null when it has none.</summary>
    public RepoSyncState? State { get; }

    /// <summary>Why the event is not valid, or null when it is.</summary>
    public string? Reason { get; }

    /// <inheritdoc/>
    public override string ToString() => Reason is null ? Outcome.ToString() : $"{Outcome}: {Reason}";
}
