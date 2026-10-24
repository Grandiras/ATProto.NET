using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.Data.Star;

namespace ATProtoNet.Spaces;

/// <summary>A space repository whose export verified: its commit, and every record in path order.</summary>
public sealed class VerifiedSpaceRepo
{
    internal VerifiedSpaceRepo(SpaceCommit commit, IReadOnlyList<StarEntry> records) => (Commit, Records) = (commit, records);

    /// <summary>The commit, authenticated by its MAC.</summary>
    public SpaceCommit Commit { get; }

    /// <summary>The records, which hash to the commit's <c>data</c>.</summary>
    public IReadOnlyList<StarEntry> Records { get; }
}

/// <summary>Space repository exports: the STAR archive of <c>com.atproto.space.getRepo</c>.</summary>
/// <remarks>See proposal 0016, <i>Repo serialization</i>.</remarks>
public static class SpaceRepoExport
{
    /// <summary>
    /// Verifies an export: its metadata is a commit of the requested repository whose MAC verifies, and its records
    /// fold into an <see cref="LtHash"/> equal to the commit's <c>data</c>. Only that last match shows the whole
    /// repository arrived, so nothing is returned before it.
    /// </summary>
    /// <param name="star">The archive; read to its end, not disposed.</param>
    /// <param name="repo">The author whose repository was requested.</param>
    /// <param name="space">The space it was requested in.</param>
    /// <param name="secret">The secret of the request's <see cref="SpaceMacRequest"/>.</param>
    /// <param name="mac">The MAC from the response's <see cref="SpaceMac.MacHeader"/>.</param>
    /// <param name="options">Bounds on the archive; the defaults when omitted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The verified repository.</returns>
    /// <exception cref="SpaceVerificationException">The export does not verify; <see cref="SpaceVerificationException.Error"/> says why.</exception>
    public static async ValueTask<VerifiedSpaceRepo> VerifyAsync(Stream star, Did repo, SpaceUri space, SharedSecret secret, ReadOnlyMemory<byte> mac,
        StarReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(star);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(secret);
        try
        {
            var commit = SpaceCommit.FromStarMetadata((await StarReader.ReadHeaderAsync(star, options, cancellationToken).ConfigureAwait(false)).Drisl);
            if (!commit.Describes(repo, space))
                throw new SpaceVerificationException(SpaceVerificationError.WrongRepository, $"The export is of {commit.Repo} in {commit.Space} (version {commit.Ver}), not the repository requested.");
            if (!SpaceMac.VerifyCommit(secret, commit, mac.Span))
                throw new SpaceVerificationException(SpaceVerificationError.BadMac, "The export's commit MAC does not verify.");

            var hash = LtHash.Empty();
            List<StarEntry> records = [];
            await foreach (var entry in StarReader.ReadEntriesAsync(star, options, cancellationToken).ConfigureAwait(false))
            {
                var slash = entry.Path.IndexOf('/');
                hash.Add(Nsid.Parse(entry.Path.AsSpan(0, slash)), RecordKey.Parse(entry.Path.AsSpan(slash + 1)), entry.Cid);
                records.Add(entry);
            }
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(hash.Digest(), commit.Data.Span)
                ? new(commit, records)
                : throw new SpaceVerificationException(SpaceVerificationError.DigestMismatch, "The export's records do not hash to its commit.");
        }
        catch (DataLimitExceededException ex)
        {
            throw new SpaceVerificationException(SpaceVerificationError.LimitExceeded, ex.Message, ex);
        }
        catch (FormatException ex)
        {
            throw new SpaceVerificationException(SpaceVerificationError.Malformed, $"The export is malformed: {ex.Message}", ex);
        }
    }

    /// <summary>Writes an export: the commit as its metadata, then the records, which must be in strictly increasing path order.</summary>
    /// <param name="output">Where the archive goes.</param>
    /// <param name="commit">The repository's commit.</param>
    /// <param name="records">The records as paths and canonical DRISL maps.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the archive is written.</returns>
    public static ValueTask WriteAsync(Stream output, SpaceCommit commit, IAsyncEnumerable<(string Path, ReadOnlyMemory<byte> Record)> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return StarWriter.WriteAsync(output, commit.ToStarMetadata(), records, cancellationToken);
    }
}
