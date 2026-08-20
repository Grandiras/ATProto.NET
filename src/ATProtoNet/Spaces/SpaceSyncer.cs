using System.Net;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Spaces;

/// <summary>
/// Why a sync pass ended where it did.
/// </summary>
public enum SpaceSyncOutcome
{
    /// <summary>
    /// The repo advanced incrementally and its digest now matches the signed commit, so the
    /// local copy is exactly current.
    /// </summary>
    UpToDate,

    /// <summary>
    /// Operations were applied but the response did not reach the head of the oplog, so the
    /// caller should sync again to continue.
    /// </summary>
    Partial,

    /// <summary>
    /// The oplog could not carry the copy forward and it was rebuilt from a full repo download.
    /// </summary>
    Recovered,

    /// <summary>The account holds no repo in this space, so there is nothing to sync.</summary>
    NoRepo,
}

/// <summary>
/// The result of one sync pass over one repo.
/// </summary>
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
    string? Rev,
    SignedSpaceCommit? Commit,
    IReadOnlyList<SpaceRepoOpEntry> Ops,
    VerifiedSpaceRepo? RecoveredRepo);

/// <summary>
/// The local state a syncer keeps for one repo: where it has read up to, and a running set hash
/// over what it holds.
/// </summary>
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
    public SpaceRepoCursor(string repo) : this(repo, rev: null, state: default)
    {
    }

    /// <summary>Restores a cursor from persisted state.</summary>
    /// <param name="repo">The DID of the account whose repo this tracks.</param>
    /// <param name="rev">The revision last applied, or <see langword="null"/> if none.</param>
    /// <param name="state">
    /// The persisted <see cref="LtHash"/> state of the local copy, or an empty span for a repo
    /// the syncer holds nothing of.
    /// </param>
    public SpaceRepoCursor(string repo, string? rev, ReadOnlySpan<byte> state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        Repo = repo;
        Rev = rev;
        Commit = SpaceRepoCommit.FromState(state);
    }

    /// <summary>The DID of the account whose repo this tracks.</summary>
    public string Repo { get; }

    /// <summary>The revision the local copy stands at, or <see langword="null"/> if it holds nothing.</summary>
    public string? Rev { get; internal set; }

    /// <summary>The running set hash over the local copy.</summary>
    public SpaceRepoCommit Commit { get; private set; }

    /// <summary>Serializes the running set hash for persistence alongside <see cref="Rev"/>.</summary>
    public byte[] GetState() => Commit.SetHash.GetState();

    internal void Reset(SpaceRepoCommit commit, string? rev)
    {
        Commit = commit;
        Rev = rev;
    }
}

/// <summary>
/// Keeps a local copy of a space in sync by pulling directly from each member's repo host.
/// </summary>
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
/// <see cref="SpaceClient.ListReposAsync">the writer set</see>. Because each entry carries that
/// repo's current revision, a periodic sweep can compare revisions and re-sync only what
/// advanced, rather than polling every repo — which is also the backstop for a dropped write
/// notification.</para>
/// </remarks>
public sealed class SpaceSyncer
{
    private readonly SpaceUri _space;
    private readonly ISpaceRepoStore _store;
    private readonly Func<string, CancellationToken, Task<string>> _signingKeyResolver;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a syncer for one space.
    /// </summary>
    /// <param name="space">The space being synced.</param>
    /// <param name="store">The caller's copy of the space, which this drives.</param>
    /// <param name="signingKeyResolver">
    /// Resolves an author's DID to its <c>did:key</c> signing key, used to verify commits.
    /// Supply <see cref="ResolveSigningKeyAsync"/> over a <see cref="DidResolver"/> for the
    /// ordinary case, or your own cache — every pass verifies at least one commit.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public SpaceSyncer(
        SpaceUri space,
        ISpaceRepoStore store,
        Func<string, CancellationToken, Task<string>> signingKeyResolver,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(signingKeyResolver);

        _space = space;
        _store = store;
        _signingKeyResolver = signingKeyResolver;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Advances one repo as far as it can, recovering in full if the operation log cannot carry
    /// the copy forward.
    /// </summary>
    /// <param name="client">A client for the repo's host, authenticated for this space.</param>
    /// <param name="cursor">The local state for this repo. Updated in place.</param>
    /// <param name="pageSize">Operations to request per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
                _space.Value, cursor.Repo, cursor.Rev, pageSize, cursor: null,
                excludeValues: false, cancellationToken);
        }
        catch (AtProtoHttpException ex) when (IsMissingRepo(ex))
        {
            return new SpaceSyncResult(SpaceSyncOutcome.NoRepo, cursor.Rev, null, [], null);
        }
        catch (AtProtoHttpException ex) when (IsUnusableOplog(ex))
        {
            // A `since` the host can no longer serve is not an error condition — the oplog is a
            // transport optimization with no history guarantee, and it is reset by migration.
            // A throttled or broken host is a different matter and propagates: silently
            // downloading the whole repo because a PDS returned 429 would make things worse.
            _logger.LogDebug(ex, "Oplog unusable for {Repo} in {Space}; recovering in full.", cursor.Repo, _space);
            return await RecoverAsync(client, cursor, cancellationToken);
        }

        // Apply what arrived, then decide whether the result can be trusted.
        var applied = new List<SpaceRepoOpEntry>(page.Ops.Count);
        foreach (var op in page.Ops)
        {
            await _store.ApplyAsync(_space, cursor.Repo, op, cancellationToken);
            cursor.Commit.ApplyOp(op.ToRepoOp());
            cursor.Rev = op.Rev;
            applied.Add(op);
        }

        // A commit arrives only at the head of the log; short of that there is nothing to
        // compare against and the caller simply syncs again.
        if (page.Commit is null)
            return new SpaceSyncResult(SpaceSyncOutcome.Partial, cursor.Rev, null, applied, null);

        var didKey = await _signingKeyResolver(cursor.Repo, cancellationToken);
        var context = new SpaceCommitContext(_space, cursor.Repo, page.Commit.Rev);

        if (!SpaceCommitVerifier.Verify(page.Commit, context, didKey))
        {
            throw new SpaceRepoVerificationException(
                $"The commit for {cursor.Repo} in {_space} failed verification.");
        }

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

        return await RecoverAsync(client, cursor, cancellationToken);
    }

    /// <summary>
    /// Rebuilds a local copy from a full repo download, verifying the whole thing before
    /// replacing what is held.
    /// </summary>
    /// <param name="client">A client for the repo's host, authenticated for this space.</param>
    /// <param name="cursor">The local state for this repo. Reset in place.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SpaceSyncResult> RecoverAsync(
        SpaceClient client,
        SpaceRepoCursor cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(cursor);

        byte[] car;
        try
        {
            await using var stream = await client.GetRepoAsync(
                _space.Value, cursor.Repo, excludeValues: null, cancellationToken);

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            car = buffer.ToArray();
        }
        catch (AtProtoHttpException ex) when (IsMissingRepo(ex))
        {
            await _store.DropAsync(_space, cursor.Repo, cancellationToken);
            cursor.Reset(new SpaceRepoCommit(), rev: null);
            return new SpaceSyncResult(SpaceSyncOutcome.NoRepo, null, null, [], null);
        }

        var didKey = await _signingKeyResolver(cursor.Repo, cancellationToken);
        var repo = SpaceRepoCar.Verify(car, _space, cursor.Repo, didKey);

        await _store.ReplaceAsync(_space, cursor.Repo, repo, cancellationToken);
        cursor.Reset(SpaceRepoCommit.FromIndex(repo.Index), repo.Commit.Rev);

        return new SpaceSyncResult(SpaceSyncOutcome.Recovered, repo.Commit.Rev, repo.Commit, [], repo);
    }

    /// <summary>
    /// Builds a signing-key resolver over a <see cref="DidResolver"/>, suitable for the
    /// <c>signingKeyResolver</c> constructor argument.
    /// </summary>
    /// <param name="resolver">The DID resolver to read documents through.</param>
    /// <remarks>
    /// This resolves a document per call. A syncer verifying many commits should cache the
    /// result, and must invalidate that cache on the <c>#identity</c> firehose events that
    /// announce a key rotation — those apply to permissioned repos exactly as they do to public
    /// ones, so an application syncing only permissioned data still needs that stream.
    /// </remarks>
    public static Func<string, CancellationToken, Task<string>> ResolveSigningKeyAsync(DidResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return async (did, cancellationToken) =>
        {
            var document = await resolver.ResolveDidAsync(did, cancellationToken);
            return SpaceAuthority.GetSigningKey(document)
                ?? throw new SpaceRepoVerificationException($"'{did}' publishes no AT Protocol signing key.");
        };
    }

    /// <summary>
    /// Whether the host rejected the request itself — a <c>since</c> it cannot serve, a filter it
    /// will not honour — as opposed to failing to serve it. Only the former is repaired by a full
    /// download; a 429 or a 5xx is transient and belongs to the caller's retry policy.
    /// </summary>
    private static bool IsUnusableOplog(AtProtoHttpException exception) =>
        exception.StatusCode is { } status &&
        (int)status is >= 400 and < 500 &&
        status is not HttpStatusCode.TooManyRequests
            and not HttpStatusCode.Unauthorized
            and not HttpStatusCode.Forbidden;

    private static bool IsMissingRepo(AtProtoHttpException exception) =>
        exception.ErrorType is SpaceErrors.RepoNotFound
            or SpaceErrors.RepoDeactivated
            or SpaceErrors.RepoSuspended
            or SpaceErrors.RepoTakendown;
}

/// <summary>
/// The caller's copy of a space, which a <see cref="SpaceSyncer"/> drives.
/// </summary>
/// <remarks>
/// A syncer owns the protocol — the oplog, the digest comparison, commit verification, the
/// fallback to full recovery — and nothing about where the data lands. Implement this over
/// whatever store the application already has.
/// </remarks>
public interface ISpaceRepoStore
{
    /// <summary>
    /// Applies one operation-log entry: a create, an update, or a delete.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the account whose repo advanced.</param>
    /// <param name="op">
    /// The operation. <see cref="SpaceRepoOpEntry.Cid"/> is <see langword="null"/> for a delete;
    /// <see cref="SpaceRepoOpEntry.Value"/> is absent for a delete and when a later operation in
    /// the same response superseded it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ApplyAsync(SpaceUri space, string repo, SpaceRepoOpEntry op, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces everything held for one repo with a verified full download.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the account whose repo was recovered.</param>
    /// <param name="contents">The verified repo.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// An implementation replacing an existing copy may diff <paramref name="contents"/> against
    /// what it holds and keep only what it is missing, rather than rewriting everything.
    /// </remarks>
    Task ReplaceAsync(SpaceUri space, string repo, VerifiedSpaceRepo contents, CancellationToken cancellationToken);

    /// <summary>
    /// Drops everything held for one repo, because the account no longer holds one in this space
    /// or is no longer served.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="repo">The DID of the account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DropAsync(SpaceUri space, string repo, CancellationToken cancellationToken);
}
