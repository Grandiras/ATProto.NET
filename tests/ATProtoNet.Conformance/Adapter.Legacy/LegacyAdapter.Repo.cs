using System.Runtime.CompilerServices;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Repo;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    private const string CommitBlock = "ATProtoNet.Repo.CommitBlock, ATProtoNet";

    /// <inheritdoc />
    public CommitVerdict VerifyCommit(ReadOnlySpan<byte> block, string signingKey)
    {
        object commit;
        try
        {
            commit = ReadCommit(null, block.ToArray());
        }
        catch (FormatException)
        {
            return CommitVerdict.Malformed;
        }

        return AtProtoCrypto.VerifySignature(signingKey, Unsigned(commit), Signature(commit)) ? CommitVerdict.Valid : CommitVerdict.BadSignature;
    }

    /// <inheritdoc />
    public ProofVerdict VerifyRecordProof(ReadOnlySpan<byte> car, string did, string collection, string rkey, string signingKey)
    {
        try
        {
            var record = RecordProof.Verify(car, Did.Parse(did), Nsid.Parse(collection), RecordKey.Parse(rkey), signingKey);
            return record.Exists ? new(ProofStatus.Present, record.Cid!.Value, record.Value!.Value.GetRawText()) : new(ProofStatus.Absent);
        }
        catch (Exception e) when (e is RepoVerificationException or FormatException)
        {
            return ProofVerdict.Invalid;
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Read")]
    [return: UnsafeAccessorType(CommitBlock)]
    private static extern object ReadCommit([UnsafeAccessorType(CommitBlock)] object? type, byte[] block);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Unsigned")]
    private static extern byte[] Unsigned([UnsafeAccessorType(CommitBlock)] object commit);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Signature")]
    private static extern byte[] Signature([UnsafeAccessorType(CommitBlock)] object commit);
}
