namespace ATProtoNet.Repo;

/// <summary>What failed when repository data was verified.</summary>
public enum RepoVerificationError
{
    /// <summary>The CAR is malformed, or does not have exactly one root.</summary>
    MalformedCar,
    /// <summary>A block the data names is not present.</summary>
    MissingBlock,
    /// <summary>A block does not match its CID.</summary>
    CidMismatch,
    /// <summary>The commit is for another repository.</summary>
    WrongRepository,
    /// <summary>The commit's signature does not verify against the key.</summary>
    BadSignature,
    /// <summary>The commit block is not a well-formed signed commit.</summary>
    MalformedCommit,
    /// <summary>The commit has a version other than 3.</summary>
    UnsupportedVersion,
    /// <summary>A tree node is malformed, or the tree is not a well-formed, canonical MST.</summary>
    MalformedTree,
    /// <summary>The blocks do not reach what was asked for: a proof that does not cover its key.</summary>
    Incomplete,
    /// <summary>The data exceeds a <see cref="RepoLimits"/> bound.</summary>
    LimitExceeded,
}

/// <summary>Repository data (a commit, a tree, a proof or a snapshot) failed verification.</summary>
/// <param name="error">What failed.</param>
/// <param name="message">A description of the failure.</param>
/// <param name="innerException">The underlying cause, if any.</param>
public sealed class RepoVerificationException(RepoVerificationError error, string message, Exception? innerException = null)
    : AtProtoException(message, innerException)
{
    /// <summary>What failed.</summary>
    public RepoVerificationError Error { get; } = error;
}

/// <summary>A tree operation reached a subtree whose block the store does not have.</summary>
public sealed class MstIncompleteException : Exception
{
    /// <summary>Creates the exception for the subtree <paramref name="cid"/>.</summary>
    /// <param name="cid">The CID of the missing node.</param>
    public MstIncompleteException(Cid cid) : base($"The MST is incomplete: node {cid} is not in the block store.") => Cid = cid;

    /// <summary>The CID of the missing node.</summary>
    public Cid Cid { get; }
}
