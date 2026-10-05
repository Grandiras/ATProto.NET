using System.Net;
using System.Runtime.ExceptionServices;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Spaces;

/// <summary>Why a sync pass ended where it did.</summary>
public enum SpaceSyncOutcome
{
    /// <summary>The repo advanced incrementally and its digest now matches the signed commit, so the local copy is exactly current.</summary>
    UpToDate,

    /// <summary>Operations were applied but the response did not reach the head of the oplog, so the caller should sync again to continue.</summary>
    /// <remarks>
    /// Only reported when the pass has somewhere left to go — it either applied operations or
    /// the host offered a continuation cursor. A page carrying neither is
    /// <see cref="NoRepo"/>, because a caller looping on the documented contract would
    /// otherwise never leave it.
    /// </remarks>
    Partial,

    /// <summary>The oplog could not carry the copy forward and it was rebuilt from a full repo download.</summary>
    Recovered,

    /// <summary>The account holds no repo in this space, so there is nothing to sync.</summary>
    /// <remarks>
    /// A host may say so either way round: by refusing the read with <c>RepoNotFound</c>, or by
    /// serving an oplog page with no operations, no commit, and no continuation cursor — the
    /// shape a member who has never written to the space produces, since the commit is built
    /// from repo state that account does not have.
    /// </remarks>
    NoRepo,
}

/// <summary>The result of one sync pass over one repo.</summary>
/// <param name="Outcome">Why the pass ended.</param>
/// <param name="Rev">
/// The revision the local copy now stands at, or <see langword="null"/> when the repo advanced
/// no further.
/// </param>
/// <param name="Commit">
/// The signed commit the copy was verified against, when the pass reached the head of the oplog
/// or recovered in full.
/// </param>
/// <param name="Ops">
/// The operations applied, in order. Empty after a full recovery, which replaces the copy rather
/// than advancing it.
/// </param>
/// <param name="RecoveredRepo">The rebuilt repo, when <see cref="Outcome"/> is <see cref="SpaceSyncOutcome.Recovered"/>.</param>
public sealed record SpaceSyncResult(
    SpaceSyncOutcome Outcome,
    Tid? Rev,
    SignedSpaceCommit? Commit,
    IReadOnlyList<SpaceRepoOpEntry> Ops,
    VerifiedSpaceRepo? RecoveredRepo);

/// <summary>The local state a syncer keeps for one repo: where it has read up to, and a running set hash over what it holds.</summary>
/// <remarks>
/// The set hash is the whole point. It is the same digest the repo host maintains, so comparing
/// the two says whether the local copy is exactly current — without transferring the repo, and
/// without depending on having received every individual operation. That is what makes
/// permissioned sync self-healing: a missed write shows up as a mismatch on the next pass and is
/// repaired by falling back to a full download.
/// </remarks>
public sealed class SpaceRepoCursor
{
    /// <summary>Creates a cursor for a repo the syncer has never read.</summary>
    /// <param name="repo">The DID of the account whose repo this tracks.</param>
    public SpaceRepoCursor(Did repo) : this(repo, rev: null, state: default)
    {
    }

    /// <summary>Restores a cursor from persisted state.</summary>
    /// <param name="repo">The DID of the account whose repo this tracks.</param>
    /// <param name="rev">The revision last applied, or <see langword="null"/> if none.</param>
    /// <param name="state">
    /// The persisted <see cref="LtHash"/> state of the local copy, or an empty span for a repo
    /// the syncer holds nothing of.
    /// </param>
    public SpaceRepoCursor(Did repo, Tid? rev, ReadOnlySpan<byte> state)
    {
        ArgumentNullException.ThrowIfNull(repo);

        Repo = repo;
        Rev = rev;
        Commit = SpaceRepoCommit.FromState(state);
    }

    /// <summary>The DID of the account whose repo this tracks.</summary>
    public Did Repo { get; }

    /// <summary>The revision the local copy stands at, or <see langword="null"/> if it holds nothing.</summary>
    public Tid? Rev { get; internal set; }

    /// <summary>The running set hash over the local copy.</summary>
    public SpaceRepoCommit Commit { get; private set; }

    /// <summary>Serializes the running set hash for persistence alongside <see cref="Rev"/>.</summary>
    public byte[] GetState() => Commit.SetHash.GetState();

    internal void Reset(SpaceRepoCommit commit, Tid? rev)
    {
        Commit = commit;
        Rev = rev;
    }
}

/// <summary>Keeps a local copy of a space in sync by pulling directly from each member's repo host.</summary>
/// <remarks>
/// <para>There is no relay for permissioned data. Permissioned repos are non-rebroadcastable by
/// construction, so no intermediary can collate a firehose of them, and an application pulls
/// from each repo host itself and is responsible for keeping its own copy current. That places
/// the load on PDSes, which is why sync load scales with the number of <em>applications</em>
/// syncing a space rather than the number of end users: an application pulls each repo once and
/// fans it out from its own copy.</para>
/// <para>A pass over one repo advances through
/// <see cref="SpaceClient.ListRepoOpsAsync">the operation log</see>, applying each entry to the
/// caller's copy through <see cref="ISpaceRepoStore"/> and to a running set hash. When the
/// response reaches the head of the log it carries the repo's current signed commit; matching
/// digests means the copy is exactly current and the signature authenticates that state. A
/// mismatch, or a <c>since</c> the host can no longer serve, falls back to a full download.</para>
/// <para>To sync a space in full, start from
/// <see cref="SpaceClient.ListReposAsync">the writer set</see>. It is ordered by <c>spaceRev</c>, the
/// space host's sequence over every repo's updates, so a syncer persists the last <c>spaceRev</c> it
/// processed and resumes from it with <see cref="ListChangedReposAsync"/>: only what advanced since,
/// rather than polling every repo. That is also the backstop for a dropped write notification, which
/// is detected with <see cref="NeedsCatchUp"/>.</para>
/// </remarks>
public sealed class SpaceSyncer
{
    private readonly SpaceUri _space;
    private readonly ISpaceRepoStore _store;
    private readonly IDidResolver _didResolver;
    private readonly ILogger _logger;

    /// <summary>Creates a syncer for one space.</summary>
    /// <param name="space">The space being synced.</param>
    /// <param name="store">The caller's copy of the space, which this drives.</param>
    /// <param name="didResolver">
    /// Resolves an author's DID document, whose signing key verifies their commits. Every pass
    /// verifies at least one commit, so give it a <see cref="CachingDidResolver"/>, and invalidate
    /// it on the <c>#identity</c> firehose events that announce a key rotation — those apply to
    /// permissioned repos exactly as to public ones. A commit that fails against a cached key is
    /// retried once against a refreshed document.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public SpaceSyncer(
        SpaceUri space,
        ISpaceRepoStore store,
        IDidResolver didResolver,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(didResolver);

        _space = space;
        _store = store;
        _didResolver = didResolver;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The largest full repo download <see cref="RecoverAsync"/> accepts, in bytes. Defaults to 256 MiB.</summary>
    /// <remarks>
    /// A download is held in memory whole, because it is verified — commit, index and every
    /// record — before any of it replaces the local copy. A host that declares a longer body is
    /// refused before it is read, and one that sends more than it declared, or declares nothing,
    /// is cut off at this size. Either way the recovery throws
    /// <see cref="SpaceRepoVerificationException"/> and the local copy is left as it was.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive, or exceeds <see cref="Array.MaxLength"/>.</exception>
    public long MaxRepoSize
    {
        get => _maxRepoSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Array.MaxLength);
            _maxRepoSize = value;
        }
    }

    private long _maxRepoSize = 256L * 1024 * 1024;

    /// <summary>Reports whether a forwarded write notification shows that notifications were missed, so the syncer must catch up through <see cref="ListChangedReposAsync"/>.</summary>
    /// <param name="checkpoint">The last <c>spaceRev</c> this syncer has safely processed for the space, or <see langword="null"/> if none.</param>
    /// <param name="notification">A write notification forwarded by the space host.</param>
    /// <returns>
    /// <see langword="true"/> when the notification does not follow the checkpoint directly. A
    /// notification the checkpoint already covers (a duplicate, or one that arrived out of order
    /// behind a later one) returns <see langword="false"/>: apply nothing and never move the checkpoint
    /// backwards. One that directly follows it returns <see langword="false"/> too; sync the repo, then
    /// advance the checkpoint to its <c>spaceRev</c>.
    /// </returns>
    /// <remarks>A notification without a <c>spaceRev</c> was not forwarded by a space host and says nothing about the sequence.</remarks>
    public static bool NeedsCatchUp(Tid? checkpoint, NotifyWriteRequest notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.SpaceRev is not { } spaceRev || (checkpoint is not null && checkpoint.CompareTo(spaceRev) >= 0))
            return false;

        return notification.PrevSpaceRev != checkpoint;
    }

    /// <summary>Lists the repos that advanced after a checkpoint, in ascending <c>spaceRev</c> order: the catch-up after downtime or a gap in <c>prevSpaceRev</c>.</summary>
    /// <param name="client">A client for the space host, authenticated for this space.</param>
    /// <param name="checkpoint">The last <c>spaceRev</c> safely processed, or <see langword="null"/> to list the whole writer set.</param>
    /// <param name="pageSize">Repos per request (1–1000); <see langword="null"/> for the server default.</param>
    /// <remarks>
    /// <para>Sync each repo (<see cref="SyncRepoAsync"/>) and persist its <see cref="SpaceRepoView.SpaceRev"/>
    /// as the new checkpoint only after that repo is safely stored; never past work a crash would lose.
    /// A repo that is updated while the walk is in flight appears again later in it.</para>
    /// <para>A checkpoint persisted before <c>spaceRev</c> existed was a DID, not a revision, and cannot
    /// be reused: pass <see langword="null"/> to rebootstrap.</para>
    /// </remarks>
    public IAsyncEnumerable<SpaceRepoView> ListChangedReposAsync(
        SpaceClient client,
        Tid? checkpoint,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        return Pagination.EnumerateAsync<ListSpaceReposResponse, SpaceRepoView>(
            (cursor, ct) => client.ListReposAsync(_space, pageSize, cursor ?? checkpoint?.Value, ct),
            cancellationToken);
    }

    /// <summary>Advances one repo as far as it can, recovering in full if the operation log cannot carry the copy forward.</summary>
    /// <param name="client">A client for the repo's host, authenticated for this space.</param>
    /// <param name="cursor">The local state for this repo. Updated in place.</param>
    /// <param name="pageSize">Operations to request per page.</param>
    public async Task<SpaceSyncResult> SyncRepoAsync(
        SpaceClient client,
        SpaceRepoCursor cursor,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(cursor);

        ListSpaceRepoOpsResponse page;
        try
        {
            page = await client.ListRepoOpsAsync(
                _space, cursor.Repo, cursor.Rev, excludeValues: false, pageSize, cursor: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (XrpcException ex) when (IsMissingRepo(ex))
        {
            return new SpaceSyncResult(SpaceSyncOutcome.NoRepo, cursor.Rev, null, [], null);
        }
        catch (XrpcException ex) when (IsUnusableOplog(ex))
        {
            // A `since` the host can no longer serve is not an error condition — the oplog is a
            // transport optimization with no history guarantee, and it is reset by migration.
            // A throttled or broken host is a different matter and propagates: silently
            // downloading the whole repo because a PDS returned 429 would make things worse.
            _logger.LogDebug(ex, "Oplog unusable for {Repo} in {Space}; recovering in full.", cursor.Repo, _space);
            return await RecoverAsync(client, cursor, cancellationToken).ConfigureAwait(false);
        }

        // Apply what arrived, then decide whether the result can be trusted.
        var applied = new List<SpaceRepoOpEntry>(page.Ops.Count);
        foreach (var op in page.Ops)
        {
            await _store.ApplyAsync(_space, cursor.Repo, op, cancellationToken).ConfigureAwait(false);
            cursor.Commit.ApplyOp(op.ToRepoOp());
            cursor.Rev = op.Rev;
            applied.Add(op);
        }

        // A commit arrives only at the head of the log; short of that there is nothing to
        // compare against and the caller simply syncs again — provided the pass has somewhere
        // left to go. A page that applied nothing and offered no continuation cursor has
        // reached the end of what the host will serve, and reporting "call me again" for it
        // spins a caller following that contract forever.
        if (page.Commit is null)
        {
            return applied.Count == 0 && page.Cursor is null
                ? await NothingToCommitAsync(client, cursor, cancellationToken).ConfigureAwait(false)
                : new SpaceSyncResult(SpaceSyncOutcome.Partial, cursor.Rev, null, applied, null);
        }

        var context = new SpaceCommitContext(_space, cursor.Repo, page.Commit.Rev);
        await VerifyWithKeyRefreshAsync(
            cursor.Repo,
            didKey => SpaceCommitVerifier.Verify(page.Commit, context, didKey)
                ? true
                : throw new SpaceRepoVerificationException(
                    $"The commit for {cursor.Repo} in {_space} failed verification."),
            cancellationToken).ConfigureAwait(false);

        if (cursor.Commit.Matches(page.Commit))
        {
            cursor.Rev = page.Commit.Rev;
            return new SpaceSyncResult(SpaceSyncOutcome.UpToDate, cursor.Rev, page.Commit, applied, null);
        }

        // Digests disagree: the copy diverged, whether from a dropped operation, a compacted
        // oplog, or local corruption. Which of those it was does not matter — the repair is the
        // same, and detecting it at all is what the set hash is for.
        _logger.LogInformation(
            "Local copy of {Repo} in {Space} diverged from its commit; recovering in full.", cursor.Repo, _space);

        return await RecoverAsync(client, cursor, cancellationToken).ConfigureAwait(false);
    }

    // Answers a pass whose oplog page carried nothing at all — no operations, no commit, and no
    // continuation cursor — which means the host holds no repo state to build a commit over.
    //
    // An account that has never written to a space has no repo state, and the host builds the commit
    // from that state, so listRepoOps answers with an empty page rather than refusing the read: whether
    // an account holds a repo is not something the oplog discloses. A syncer walking a member list,
    // rather than the writer set listRepos returns, is what reaches this.
    private async Task<SpaceSyncResult> NothingToCommitAsync(
        SpaceClient client, SpaceRepoCursor cursor, CancellationToken cancellationToken)
    {
        // A syncer holding nothing for a repo the host has no state for is already correct, and
        // has the same answer to give as the read that is refused outright.
        if (cursor.Rev is null)
            return new SpaceSyncResult(SpaceSyncOutcome.NoRepo, null, null, [], null);

        // Standing at a revision is a different matter: the host is reporting no state for a
        // repo the caller holds a copy of. `getRepo` is the one call that answers definitively,
        // and it repairs either way — `RepoNotFound` drops what is held, and a repo that does
        // exist rebuilds it.
        _logger.LogInformation(
            "Oplog for {Repo} in {Space} carries no commit past {Rev}; recovering in full.",
            cursor.Repo, _space, cursor.Rev);

        return await RecoverAsync(client, cursor, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rebuilds a local copy from a full repo download, verifying the whole thing before replacing what is held.</summary>
    /// <param name="client">A client for the repo's host, authenticated for this space.</param>
    /// <param name="cursor">The local state for this repo. Reset in place.</param>
    public async Task<SpaceSyncResult> RecoverAsync(
        SpaceClient client,
        SpaceRepoCursor cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(cursor);

        ReadOnlyMemory<byte> car;
        try
        {
            #pragma warning disable CA2007 // The resource keeps the default context for disposal: ConfigureAwait on it would change its declared type.
            await using var response = await client.GetRepoAsync(
                _space, cursor.Repo, excludeValues: null, cancellationToken).ConfigureAwait(false);
            #pragma warning restore CA2007

            var limit = _maxRepoSize;
            if (response.ContentLength > limit)
            {
                throw new SpaceRepoVerificationException(
                    $"The repo download declares {response.ContentLength} bytes, over the {limit}-byte limit (MaxRepoSize).");
            }

            // Verified from the read buffer itself rather than a copy of it.
            car = await response.Content.ReadBoundedAsync(limit, response.ContentLength, cancellationToken).ConfigureAwait(false)
                ?? throw new SpaceRepoVerificationException(
                    $"The repo download exceeds the {limit}-byte limit (MaxRepoSize).");
        }
        catch (XrpcException ex) when (IsMissingRepo(ex))
        {
            await _store.DropAsync(_space, cursor.Repo, cancellationToken).ConfigureAwait(false);
            cursor.Reset(new SpaceRepoCommit(), rev: null);
            return new SpaceSyncResult(SpaceSyncOutcome.NoRepo, null, null, [], null);
        }

        var repo = await VerifyWithKeyRefreshAsync(
            cursor.Repo, didKey => SpaceRepoCar.Verify(car.Span, _space, cursor.Repo, didKey), cancellationToken).ConfigureAwait(false);

        await _store.ReplaceAsync(_space, cursor.Repo, repo, cancellationToken).ConfigureAwait(false);
        cursor.Reset(SpaceRepoCommit.FromIndex(repo.Index), repo.Commit.Rev);

        return new SpaceSyncResult(SpaceSyncOutcome.Recovered, repo.Commit.Rev, repo.Commit, [], repo);
    }

    // Runs a verification against the author's #atproto key, through
    // DidResolverExtensions.VerifyWithRefreshAsync. A space commit is signed with the account's own key,
    // like a public one; a #atproto_space entry is a space authority's credential key and plays no part
    // here.
    //
    // author: The author whose key signs what is verified.
    //
    // verify: The verification, throwing SpaceRepoVerificationException on failure.
    private async Task<T> VerifyWithKeyRefreshAsync<T>(
        Did author, Func<string, T> verify, CancellationToken cancellationToken)
    {
        var verified = default(T)!;
        SpaceRepoVerificationException? failure = null;
        var result = await _didResolver.VerifyWithRefreshAsync(
            author,
            document => document.GetSigningKey()
                ?? throw new SpaceRepoVerificationException($"'{author}' publishes no usable AT Protocol signing key."),
            key =>
            {
                if (failure is not null)
                    _logger.LogInformation("The signing key of {Repo} changed; verifying against the new one.", author);

                try
                {
                    verified = verify(key);
                    return true;
                }
                catch (SpaceRepoVerificationException ex)
                {
                    failure = ex;
                    return false;
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.Verified)
            ExceptionDispatchInfo.Throw(failure!);

        return verified;
    }

    // Whether the host rejected the request itself — a since it cannot serve, a filter it will not
    // honour — as opposed to failing to serve it. Only the former is repaired by a full download; a 429
    // or a 5xx is transient and belongs to the caller's retry policy.
    private static bool IsUnusableOplog(XrpcException exception) =>
        (int)exception.StatusCode is >= 400 and < 500 &&
        exception.StatusCode is not HttpStatusCode.TooManyRequests
            and not HttpStatusCode.Unauthorized
            and not HttpStatusCode.Forbidden;

    private static bool IsMissingRepo(XrpcException exception) =>
        exception.Error is SpaceErrors.RepoNotFound
            or SpaceErrors.RepoDeactivated
            or SpaceErrors.RepoSuspended
            or SpaceErrors.RepoTakendown;
}

/// <summary>The caller's copy of a space, which a <see cref="SpaceSyncer"/> drives.</summary>
/// <remarks>
/// A syncer owns the protocol — the oplog, the digest comparison, commit verification, the
/// fallback to full recovery — and nothing about where the data lands. Implement this over
/// whatever store the application already has.
/// </remarks>
public interface ISpaceRepoStore
{
    /// <summary>Applies one operation-log entry: a create, an update, or a delete.</summary>
    /// <param name="repo">The DID of the account whose repo advanced.</param>
    /// <param name="op">
    /// The operation. <see cref="SpaceRepoOpEntry.Cid"/> is <see langword="null"/> for a delete;
    /// <see cref="SpaceRepoOpEntry.Value"/> is absent for a delete and when a later operation in
    /// the same response superseded it.
    /// </param>
    Task ApplyAsync(SpaceUri space, Did repo, SpaceRepoOpEntry op, CancellationToken cancellationToken);

    /// <summary>Replaces everything held for one repo with a verified full download.</summary>
    /// <param name="repo">The DID of the account whose repo was recovered.</param>
    /// <param name="contents">The verified repo.</param>
    /// <remarks>
    /// An implementation replacing an existing copy may diff <paramref name="contents"/> against
    /// what it holds and keep only what it is missing, rather than rewriting everything.
    /// </remarks>
    Task ReplaceAsync(SpaceUri space, Did repo, VerifiedSpaceRepo contents, CancellationToken cancellationToken);

    /// <summary>Drops everything held for one repo, because the account no longer holds one in this space or is no longer served.</summary>
    /// <param name="repo">The DID of the account.</param>
    Task DropAsync(SpaceUri space, Did repo, CancellationToken cancellationToken);
}
