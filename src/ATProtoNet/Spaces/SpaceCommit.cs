using System.Security.Cryptography;
using ATProtoNet.Data;

namespace ATProtoNet.Spaces;

/// <summary>
/// A space repository commit (<c>com.atproto.space.defs#commit</c>): which author's repository, in which space,
/// at which revision, and <c>data</c>, the digest of its <see cref="LtHash"/>.
/// </summary>
/// <remarks>
/// It carries no signature or MAC: the host authenticates it to each requester with a MAC in the response
/// headers (<see cref="SpaceMac"/>), so it proves nothing to a third party. See proposal 0016, <i>Space repos</i>.
/// </remarks>
public sealed class SpaceCommit
{
    /// <summary>The commit format version this SDK reads and writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The <c>$type</c> of a repository export's STAR metadata.</summary>
    public const string StarType = "atproto-space-repo";

    /// <summary>Creates a commit of the current version.</summary>
    /// <param name="repo">The author's DID.</param>
    /// <param name="space">The space.</param>
    /// <param name="rev">The revision.</param>
    /// <param name="data">The <see cref="LtHash.Digest"/> of the repository, 32 bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="data"/> is not 32 bytes.</exception>
    public SpaceCommit(Did repo, SpaceUri space, Tid rev, ReadOnlySpan<byte> data) : this(CurrentVersion, repo, space, rev.ToString(), data.ToArray())
    {
        if (data.Length != SHA256.HashSizeInBytes) throw new ArgumentException("A commit's data is a 32-byte SHA-256 digest.", nameof(data));
    }

    private SpaceCommit(long ver, Did repo, SpaceUri space, string rev, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(space);
        (Ver, Repo, Space, Rev, Data) = (ver, repo, space, rev, data);
    }

    /// <summary>The format version; <see cref="CurrentVersion"/> for every commit that verifies.</summary>
    public long Ver { get; }

    /// <summary>The author's DID.</summary>
    public Did Repo { get; }

    /// <summary>The space.</summary>
    public SpaceUri Space { get; }

    /// <summary>The revision, as read: the SDK writes TIDs, and reads any string, as the reference does.</summary>
    public string Rev { get; }

    /// <summary>The digest of the repository's <see cref="LtHash"/>.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Whether the commit names exactly this repository, at the current version: the syncer's first check.</summary>
    /// <param name="repo">The requested author.</param>
    /// <param name="space">The requested space.</param>
    /// <returns>Whether it does.</returns>
    public bool Describes(Did repo, SpaceUri space) => Ver == CurrentVersion && Repo == repo && Space == space;

    /// <summary>The metadata of the repository's STAR export: canonical DRISL of <c>$type</c> and the commit's fields.</summary>
    /// <returns>The DRISL map.</returns>
    public byte[] ToStarMetadata() => new DataModelMap(
    [
        new("$type", new DataModelString(StarType)),
        new("ver", new DataModelInteger(Ver)),
        new("repo", new DataModelString(Repo.Value)),
        new("space", new DataModelString(Space.Value)),
        new("rev", new DataModelString(Rev)),
        new("data", new DataModelBytes(Data)),
    ]).ToDrisl();

    /// <summary>Reads a commit from a repository export's STAR metadata. Unknown fields are ignored.</summary>
    /// <param name="metadata">The metadata, as strict DRISL.</param>
    /// <returns>The commit, of whatever version it names; check it with <see cref="Describes"/>.</returns>
    /// <exception cref="SpaceVerificationException">The metadata is not a commit with <c>$type</c> <see cref="StarType"/> (<see cref="SpaceVerificationError.Malformed"/>).</exception>
    public static SpaceCommit FromStarMetadata(ReadOnlyMemory<byte> metadata)
    {
        string? type = null;
        long? ver = null;
        Did? repo = null;
        SpaceUri? space = null;
        string? rev = null;
        byte[]? data = null;
        try
        {
            var reader = new DrislReader(metadata);
            var fields = reader.ReadStartMap();
            for (var i = 0; i < fields; i++)
            {
                switch (reader.ReadKey())
                {
                    case "$type": type = reader.ReadString(); break;
                    case "ver": ver = reader.ReadInt64(); break;
                    case "repo": repo = Did.TryParse(reader.ReadString(), out var d) ? d : throw new FormatException("repo is not a DID."); break;
                    case "space": space = SpaceUri.TryParse(reader.ReadString(), out var s) ? s : throw new FormatException("space is not a space URI."); break;
                    case "rev": rev = reader.ReadString(); break;
                    case "data": data = reader.ReadBytes().ToArray(); break;
                    default: reader.SkipValue(); break;
                }
            }
            reader.ReadEndMap();
            reader.ReadEnd();
        }
        catch (FormatException ex)
        {
            throw Malformed(ex.Message, ex);
        }
        if (type != StarType) throw Malformed($"$type is not \"{StarType}\".");
        if (ver is null || repo is null || space is null || rev is null || data is not { Length: SHA256.HashSizeInBytes })
            throw Malformed("it lacks ver, repo, space, rev or a 32-byte data.");
        // Every MAC context field carries a uint16 length (SPC-56).
        if (System.Text.Encoding.UTF8.GetByteCount(rev) > ushort.MaxValue) throw Malformed($"rev is longer than {ushort.MaxValue} bytes.");
        return new(ver.Value, repo, space, rev, data);
    }

    private static SpaceVerificationException Malformed(string problem, Exception? inner = null) =>
        new(SpaceVerificationError.Malformed, $"The space repository metadata is not a commit: {problem}", inner);
}
