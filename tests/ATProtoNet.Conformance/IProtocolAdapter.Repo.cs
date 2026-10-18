namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>Reads a signed repository commit block and verifies its signature against the <c>did:key</c>.</summary>
    CommitVerdict VerifyCommit(ReadOnlySpan<byte> block, string signingKey);

    /// <summary>
    /// Verifies a <c>com.atproto.sync.getRecord</c> proof of <c>{collection}/{rkey}</c> in the
    /// repository <paramref name="did"/> signed by the <c>did:key</c> <paramref name="signingKey"/>.
    /// </summary>
    ProofVerdict VerifyRecordProof(ReadOnlySpan<byte> car, string did, string collection, string rkey, string signingKey);
}

/// <summary>The outcome of verifying a commit block.</summary>
public enum CommitVerdict
{
    /// <summary>A well-formed commit whose signature verifies.</summary>
    Valid,
    /// <summary>Not a well-formed signed commit.</summary>
    Malformed,
    /// <summary>A well-formed commit whose signature does not verify against the key.</summary>
    BadSignature,
}

/// <summary>The outcome of verifying a record proof.</summary>
/// <param name="Status">What the proof shows.</param>
/// <param name="RecordCid">The proven record's CID, when present.</param>
/// <param name="Record">The proven record in the JSON data model, when present.</param>
public sealed record ProofVerdict(ProofStatus Status, string? RecordCid = null, string? Record = null)
{
    /// <summary>A proof that does not verify.</summary>
    public static ProofVerdict Invalid { get; } = new(ProofStatus.Invalid);
}

/// <summary>What a verified record proof shows.</summary>
public enum ProofStatus
{
    /// <summary>The record exists.</summary>
    Present,
    /// <summary>The repository holds no such record.</summary>
    Absent,
    /// <summary>The proof does not verify (malformed, incomplete, tampered, or another repository or key).</summary>
    Invalid,
}
