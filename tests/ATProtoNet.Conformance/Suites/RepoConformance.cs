namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// Commits and proofs: the atproto-interop-tests commit-proof fixtures, signed commit blocks and
/// record proofs (<c>repo/*.json</c>).
/// </summary>
public abstract class RepoConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    /// <summary>One row per commit-proof fixture, keyed by its comment.</summary>
    public static TheoryData<string> CommitProofs() =>
        new(Vectors.Json("repo/commit-proof-fixtures.json").AsArray().Select(f => $"repo/commit-proof-fixtures.json#{f!["comment"]}"));

    /// <summary>
    /// The tree before and after the commit has the reference roots, its covering proof holds exactly
    /// the reference blocks, and inverting the commit on the proof lands on the root before it.
    /// </summary>
    [Theory]
    [MemberData(nameof(CommitProofs))]
    [Trait("Spec", "REP-9"), Trait("Spec", "REP-20"), Trait("Spec", "REP-21")]
    public void MstInvert_CommitProofFixture_LandsOnTheRootBeforeTheCommit(string vectorId)
    {
        Vector(vectorId);
        var f = Vectors.Json("repo/commit-proof-fixtures.json").AsArray().Single(f => vectorId.EndsWith("#" + (string)f!["comment"]!, StringComparison.Ordinal))!;
        var leaf = (string)f["leafValue"]!;
        string[] keys = [.. f["keys"]!.AsArray().Select(k => (string)k!)], adds = [.. f["adds"]!.AsArray().Select(k => (string)k!)], dels = [.. f["dels"]!.AsArray().Select(k => (string)k!)];
        var before = keys.Select(k => KeyValuePair.Create(k, leaf)).ToList();
        var after = keys.Except(dels).Concat(adds).Order(StringComparer.Ordinal).Select(k => KeyValuePair.Create(k, leaf)).ToList();
        MstOperation[] operations = [.. adds.Select(k => new MstOperation(k, null, leaf)), .. dels.Select(k => new MstOperation(k, leaf, null))];

        Assert.Equal((string)f["rootBeforeCommit"]!, Adapter.MstEdit(before, []).Root);
        Assert.Equal((string)f["rootAfterCommit"]!, Adapter.MstEdit(before, operations).Root);
        var proof = Adapter.MstCoveringProof(after, [.. adds, .. dels]);
        Assert.Equal((string)f["rootAfterCommit"]!, proof.Root);
        Assert.Equal(f["blocksInProof"]!.AsArray().Select(b => (string)b!).Order(StringComparer.Ordinal), proof.Blocks.Keys.Order(StringComparer.Ordinal));
        Assert.Equal((string)f["rootBeforeCommit"]!, Adapter.MstInvert(proof, operations));
    }

    /// <summary>Rows of a <c>repo/*.json</c> array file.</summary>
    public static TheoryData<string> Cases(string file) => Vectors.Ids($"repo/{file}");

    /// <summary>A commit block gets the expected verdict against the key.</summary>
    [Theory]
    [MemberData(nameof(Cases), "commit-blocks.json")]
    [Trait("Spec", "REP-26"), Trait("Spec", "REP-27"), Trait("Spec", "REP-28"), Trait("Spec", "REP-29"), Trait("Spec", "REP-30"), Trait("Spec", "REP-31")]
    public void VerifyCommit_Case_MatchesExpected(string vectorId)
    {
        Vector(vectorId);
        var c = Vectors.Case(vectorId);
        var expected = (string)c["verdict"]! switch
        {
            "valid" => CommitVerdict.Valid,
            "bad-signature" => CommitVerdict.BadSignature,
            _ => CommitVerdict.Malformed,
        };
        Assert.Equal(expected, Adapter.VerifyCommit(Vectors.Hex(c["block"]), (string)c["signingKey"]!));
    }

    /// <summary>A record proof shows the expected record, absence, or does not verify.</summary>
    [Theory]
    [MemberData(nameof(Cases), "record-proof-cases.json")]
    [Trait("Spec", "REP-32"), Trait("Spec", "REP-33"), Trait("Spec", "REP-34"), Trait("Spec", "REP-35"), Trait("Spec", "REP-36"), Trait("Spec", "REP-37")]
    public void VerifyRecordProof_Case_MatchesExpected(string vectorId)
    {
        Vector(vectorId);
        var c = Vectors.Case(vectorId);
        var verdict = Adapter.VerifyRecordProof(Vectors.Hex(c["car"]), (string)c["did"]!, (string)c["collection"]!, (string)c["rkey"]!, (string)c["signingKey"]!);

        Assert.Equal(Enum.Parse<ProofStatus>((string)c["status"]!, ignoreCase: true), verdict.Status);
        Assert.Equal((string?)c["recordCid"], verdict.RecordCid);
        if (c["record"] is not null) AssertJson(Vectors.Text(c, "record"), verdict.Record);
    }
}
