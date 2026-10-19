namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    /// <inheritdoc />
    public CommitVerdict VerifyCommit(ReadOnlySpan<byte> block, string signingKey) => throw Later(2);

    /// <inheritdoc />
    public ProofVerdict VerifyRecordProof(ReadOnlySpan<byte> car, string did, string collection, string rkey, string signingKey) => throw Later(2);
}
