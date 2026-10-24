using System.Buffers;
using System.Text;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.Repo;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    /// <inheritdoc />
    public CommitVerdict VerifyCommit(ReadOnlySpan<byte> block, string signingKey)
    {
        SignedCommit commit;
        try
        {
            commit = SignedCommit.Read(block.ToArray());
        }
        catch (RepoVerificationException)
        {
            return CommitVerdict.Malformed;
        }
        return commit.Verify(DidKey.Parse(signingKey).ToVerificationKey()) ? CommitVerdict.Valid : CommitVerdict.BadSignature;
    }

    /// <inheritdoc />
    public ProofVerdict VerifyRecordProof(ReadOnlySpan<byte> car, string did, string collection, string rkey, string signingKey)
    {
        if (!DidKey.TryParse(signingKey, out var key)) return ProofVerdict.Invalid;
        VerifiedRecord record;
        try
        {
            record = RecordProof.Verify(car, Did.Parse(did), Nsid.Parse(collection), RecordKey.Parse(rkey), key.ToVerificationKey());
        }
        catch (RepoVerificationException)
        {
            return ProofVerdict.Invalid;
        }
        if (!record.Exists) return new(ProofStatus.Absent);
        var json = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(json))
            DrislJson.WriteJson(record.Block!.Value, writer);
        return new(ProofStatus.Present, record.Cid!.Value, Encoding.UTF8.GetString(json.WrittenSpan));
    }
}
