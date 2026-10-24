using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.Data.Car;
using ATProtoNet.Repo;
using static ATProtoNet.Tests.Repo.RepoFixtures;

namespace ATProtoNet.Tests.Repo;

// Proofs @atproto/repo 0.10.14 produced (repo/record-proof.json), tampered copies of them,
// proofs this SDK writes, and hand-built trees that break the MST rules.
public class RecordProofTests
{
    private static readonly Nsid Records = Nsid.Parse("com.example.record");

    private static class Reference
    {
        private static readonly System.Text.Json.Nodes.JsonNode Json = Vector("repo/record-proof.json");
        public static readonly Did Did = Did.Parse((string)Json["did"]!);
        public static readonly VerificationKey Key = DidKey.Parse((string)Json["signingKey"]!).ToVerificationKey();
        public static readonly Cid Commit = Cid.Parse((string)Json["commit"]!);
        public static readonly Tid Rev = Tid.Parse((string)Json["rev"]!);
        public static readonly RecordKey Rkey = RecordKey.Parse((string)Json["rkey"]!);
        public static readonly RecordKey AbsentRkey = RecordKey.Parse((string)Json["absentRkey"]!);
        public static readonly Cid RecordCid = Cid.Parse((string)Json["recordCid"]!);
        public static readonly byte[] PresentCar = Convert.FromBase64String((string)Json["presentCar"]!);
        public static readonly byte[] AbsentCar = Convert.FromBase64String((string)Json["absentCar"]!);
    }

    private static VerifiedRecord Verify(byte[] car, RecordKey rkey, VerificationKey? key = null) =>
        RecordProof.Verify(car, Reference.Did, Records, rkey, key ?? Reference.Key);

    private static RepoVerificationError Refused(byte[] car, RecordKey? rkey = null, VerificationKey? key = null, Did? did = null) =>
        Assert.Throws<RepoVerificationException>(() => RecordProof.Verify(car, did ?? Reference.Did, Records, rkey ?? Reference.Rkey, key ?? Reference.Key)).Error;

    private static CarBlock RootBlock(byte[] car)
    {
        var file = CarReader.Read(car);
        return file.Blocks.Single(b => b.Cid == file.Header.Roots[0]);
    }

    // ── Reference proofs ──

    [Fact]
    [Trait("Spec", "REP-32")]
    [Trait("Spec", "REP-34")]
    public void Verify_ReferenceProof_ReturnsTheRecord()
    {
        var record = Verify(Reference.PresentCar, Reference.Rkey);

        Assert.True(record.Exists);
        Assert.Equal((Reference.Did, Reference.Rev, Reference.Commit, Records, Reference.Rkey, Reference.RecordCid), (record.Did, record.Rev, record.Commit, record.Collection, record.Rkey, record.Cid));
        var value = (DataModelMap)DataModelValue.FromDrisl(record.Block!.Value);
        Assert.Equal(new DataModelString("the proven record"), value["text"]);
        Assert.Equal(new DataModelInteger(42), value["count"]);
        Assert.Equal(DataModelNull.Instance, ((DataModelArray)value["flags"])[2]);
        Assert.Equal([1, 2, 3, 4], ((DataModelBytes)((DataModelMap)value["nested"])["bytes"]).Value.ToArray());
    }

    [Fact]
    [Trait("Spec", "REP-35")]
    public void Verify_ReferenceAbsenceProof_ProvesTheRecordAbsent()
    {
        var record = Verify(Reference.AbsentCar, Reference.AbsentRkey);

        Assert.False(record.Exists);
        Assert.Null(record.Cid);
        Assert.Null(record.Block);
        Assert.Equal((Reference.Commit, Reference.Rev), (record.Commit, record.Rev));
    }

    [Fact]
    [Trait("Spec", "REP-35")]
    public void Verify_ProofOfARecord_AlsoProvesItsNeighbourAbsent() =>
        Assert.False(Verify(Reference.PresentCar, Reference.AbsentRkey).Exists);

    [Fact]
    [Trait("Spec", "REP-35")]
    public void Verify_AbsenceProof_DoesNotProveTheRecordItPassesBy() =>
        // The absence proof walks through the record's leaf node but carries no record block.
        Assert.Equal(RepoVerificationError.MissingBlock, Refused(Reference.AbsentCar));

    [Fact]
    [Trait("Spec", "REP-36")]
    public void Verify_KeyOffTheProvenPath_ThrowsIncomplete() =>
        // Between the root's second and third keys: a subtree the proof does not include.
        Assert.Equal(RepoVerificationError.Incomplete, Refused(Reference.PresentCar, RecordKey.Parse("3lenax35zzz22")));

    // ── Tampered proofs ──

    [Fact]
    [Trait("Spec", "REP-31")]
    [Trait("Spec", "REP-33")]
    public void Verify_AnotherSigningKey_ThrowsBadSignature()
    {
        using var other = K256();

        Assert.Equal(RepoVerificationError.BadSignature, Refused(Reference.PresentCar, key: other.PublicKey));
    }

    [Fact]
    [Trait("Spec", "REP-33")]
    public void Verify_AnotherRepository_ThrowsWrongRepository() =>
        Assert.Equal(RepoVerificationError.WrongRepository, Refused(Reference.PresentCar, did: Account));

    [Fact]
    [Trait("Spec", "REP-33")]
    public void Verify_RecordBytesAltered_ThrowsCidMismatch() =>
        Assert.Equal(RepoVerificationError.CidMismatch,
            Refused(Rewrite(Reference.PresentCar, b => b.Cid == Reference.RecordCid ? new(b.Cid, Flip(b.Data, 20)) : b)));

    [Fact]
    [Trait("Spec", "REP-34")]
    public void Verify_RecordSwappedForAnotherWithItsOwnCid_ThrowsMissingBlock()
    {
        // Every block still matches its CID, but the signed tree names the original record.
        var forged = RecordBlock("forged");

        Assert.Equal(RepoVerificationError.MissingBlock,
            Refused(Rewrite(Reference.PresentCar, b => b.Cid == Reference.RecordCid ? new(CidOf(forged), forged) : b)));
    }

    [Fact]
    [Trait("Spec", "REP-36")]
    public void Verify_TreeRootMissing_ThrowsIncomplete()
    {
        var data = SignedCommit.Read(RootBlock(Reference.PresentCar).Data).Commit.Data;

        Assert.Equal(RepoVerificationError.Incomplete, Refused(Rewrite(Reference.PresentCar, b => b.Cid == data ? null : b)));
    }

    [Fact]
    [Trait("Spec", "REP-36")]
    public void Verify_CommitBlockMissing_ThrowsMissingBlock()
    {
        var root = RootBlock(Reference.PresentCar).Cid;

        Assert.Equal(RepoVerificationError.MissingBlock, Refused(Rewrite(Reference.PresentCar, b => b.Cid == root ? null : b)));
    }

    [Fact]
    [Trait("Spec", "REP-33")]
    public void Verify_SignatureAltered_ThrowsBadSignature()
    {
        var commit = RootBlock(Reference.PresentCar);
        var altered = Flip(commit.Data, commit.Data.Span.IndexOf(SignedCommit.Read(commit.Data).Signature.Span) + 10);

        var car = Rewrite(Reference.PresentCar, b => b.Cid == commit.Cid ? new(CidOf(altered), altered) : b, [CidOf(altered)]);

        Assert.Equal(RepoVerificationError.BadSignature, Refused(car));
    }

    [Fact]
    [Trait("Spec", "REP-31")]
    public void Verify_CommitResignedByAnotherKey_ThrowsForTheAccountsKey()
    {
        using var other = P256();
        var original = RootBlock(Reference.PresentCar);
        var resigned = SignedCommit.Read(original.Data).Commit.Sign(other);

        var car = Rewrite(Reference.PresentCar, b => b.Cid == original.Cid ? new(resigned.Cid, resigned.Block) : b, [resigned.Cid]);

        // A well-formed commit, signed by a key that is not the account's.
        Assert.True(Verify(car, Reference.Rkey, other.PublicKey).Exists);
        Assert.Equal(RepoVerificationError.BadSignature, Refused(car));
    }

    [Fact]
    [Trait("Spec", "REP-32")]
    public void Verify_TwoRoots_ThrowsMalformedCar()
    {
        var root = RootBlock(Reference.PresentCar).Cid;

        Assert.Equal(RepoVerificationError.MalformedCar, Refused(Rewrite(Reference.PresentCar, b => b, [root, root])));
    }

    [Fact]
    [Trait("Spec", "REP-32")]
    public void Verify_NotACar_ThrowsMalformedCar() => Assert.Equal(RepoVerificationError.MalformedCar, Refused([0x01, 0x02, 0x03]));

    [Fact]
    [Trait("Spec", "REP-37")]
    public void DidKeyParse_SigningKeyNotADidKey_ThrowsFormatExceptionBeforeVerification()
    {
        Assert.False(DidKey.TryParse("did:key:zNotAKey", out _));
        Assert.Throws<FormatException>(() => DidKey.Parse("did:key:zNotAKey"));
    }

    // ── Proofs this SDK writes ──

    [Fact]
    [Trait("Spec", "REP-38")]
    public void Write_ProofsOfAnSdkWrittenRepo_ProveEachRecordAndItsNeighboursAbsence()
    {
        using var key = K256();
        var records = new MemoryBlockStore();
        var tree = MerkleSearchTree.Build(Enumerable.Range(0, 200).Select(i =>
        {
            var block = RecordBlock($"record {i}");
            records.Put(CidOf(block), block);
            return KeyValuePair.Create(MstKey.For(Records, RecordKey.Parse($"3l6oveex{i:D5}")), CidOf(block));
        }));
        var commit = new RepoCommit { Did = Reference.Did, Rev = Reference.Rev, Data = tree.Root }.Sign(key);

        foreach (var i in (int[])[0, 57, 123, 199])
        {
            RecordKey rkey = RecordKey.Parse($"3l6oveex{i:D5}"), gap = RecordKey.Parse($"3l6oveex{i:D5}a");
            using var car = new MemoryStream();
            RecordProof.Write(commit, tree, [MstKey.For(Records, rkey)], records, car);

            var record = Verify(car.ToArray(), rkey, key.PublicKey);
            Assert.Equal(RecordBlock($"record {i}"), record.Block!.Value.ToArray());
            Assert.Equal(commit.Cid, record.Commit);
            Assert.False(Verify(car.ToArray(), gap, key.PublicKey).Exists);
        }
    }

    [Fact]
    [Trait("Spec", "REP-38")]
    public void Write_SeveralKeysPresentAndAbsent_ProvesEachInOneCar()
    {
        using var key = K256();
        var block = RecordBlock("one");
        var tree = Tree(("com.example.record/a", CidOf(block)), ("com.example.record/c", CidOf(block)));
        var commit = new RepoCommit { Did = Reference.Did, Rev = Reference.Rev, Data = tree.Root }.Sign(key);
        using var car = new MemoryStream();

        RecordProof.Write(commit, tree, [Key("com.example.record/a"), Key("com.example.record/b"), Key("com.example.record/c")], Store(block), car);

        Assert.True(Verify(car.ToArray(), RecordKey.Parse("a"), key.PublicKey).Exists);
        Assert.False(Verify(car.ToArray(), RecordKey.Parse("b"), key.PublicKey).Exists);
        Assert.True(Verify(car.ToArray(), RecordKey.Parse("c"), key.PublicKey).Exists);
    }

    [Fact]
    public void Write_TreeNotTheCommitsOrRecordMissing_Throws()
    {
        using var key = K256();
        var tree = Tree(("com.example.record/a", Leaf));
        var commit = new RepoCommit { Did = Reference.Did, Rev = Reference.Rev, Data = tree.Root }.Sign(key);

        Assert.Throws<ArgumentException>(() => RecordProof.Write(commit, MerkleSearchTree.Empty(), [], new MemoryBlockStore(), Stream.Null));
        Assert.Throws<InvalidOperationException>(() => RecordProof.Write(commit, tree, [Key("com.example.record/a")], new MemoryBlockStore(), Stream.Null));
    }

    [Fact]
    [Trait("Spec", "REP-35")]
    public void Verify_EmptyRepository_ProvesEveryRecordAbsent()
    {
        using var key = K256();
        var car = SignedCar(key, EmptyRoot, [.. Blocks(MerkleSearchTree.Empty()).Blocks.Select(b => b.Data.ToArray())]);

        var record = RecordProof.Verify(car, Account, Records, Reference.Rkey, key.PublicKey);

        Assert.False(record.Exists);
    }

    // ── Trees that break the MST rules ──

    private static readonly string[] Layer0 = KeysOnLayer(0, 8), Layer1 = KeysOnLayer(1, 8);

    // A key below every Layer0 and Layer1 key.
    private const string SmallestKey = "com.example.aaa/x";

    private static readonly byte[] LeafRecord = RecordBlock("leaf");

    private static VerifiedRecord VerifyHandBuilt(string path, byte[] root, params byte[][] others)
    {
        using var key = K256();
        var car = SignedCar(key, CidOf(root), [root, .. others]);
        Assert.True(Key(path).TryGetRecordPath(out var collection, out var rkey));
        return RecordProof.Verify(car, Account, collection, rkey, key.PublicKey);
    }

    private static void AssertMalformed(string path, byte[] root, params byte[][] others) =>
        Assert.Equal(RepoVerificationError.MalformedTree, Assert.Throws<RepoVerificationException>(() => VerifyHandBuilt(path, root, others)).Error);

    [Fact]
    [Trait("Spec", "REP-12")]
    [Trait("Spec", "REP-33")]
    public void Verify_NodeKeysOutOfOrder_ThrowsMalformedTree() =>
        AssertMalformed(Layer0[0], Node(null, (Layer0[1], CidOf(LeafRecord), null), (Layer0[0], CidOf(LeafRecord), null)));

    [Fact]
    [Trait("Spec", "REP-13")]
    public void Verify_NodeMixingLayers_ThrowsMalformedTree()
    {
        var keys = new[] { Layer0[0], Layer1[0] }.Order(StringComparer.Ordinal).ToArray();

        AssertMalformed(keys[0], Node(null, (keys[0], CidOf(LeafRecord), null), (keys[1], CidOf(LeafRecord), null)));
    }

    [Fact]
    [Trait("Spec", "REP-13")]
    public void Verify_ChildOnItsParentsLayer_ThrowsMalformedTree()
    {
        var child = Node(null, (Layer1[0], CidOf(LeafRecord), null));

        AssertMalformed(Layer1[0], Node(CidOf(child), (Layer1[1], CidOf(LeafRecord), null)), child);
    }

    [Fact]
    [Trait("Spec", "REP-12")]
    [Trait("Spec", "REP-33")]
    public void Verify_SubtreeKeyOutsideItsRange_ThrowsMalformedTree()
    {
        // The subtree left of a key holds a key greater than it. A walk for a smaller key lands
        // there, and without the range check would report the smaller key absent.
        var parent = Layer1.First(k => string.CompareOrdinal(k, Layer0[0]) > 0);
        var child = Node(null, (Layer0.First(k => string.CompareOrdinal(k, parent) > 0), CidOf(LeafRecord), null));

        AssertMalformed(SmallestKey, Node(CidOf(child), (parent, CidOf(LeafRecord), null)), child);
    }

    [Fact]
    [Trait("Spec", "REP-33")]
    [Trait("Spec", "REP-35")]
    public void Verify_WellFormedHandBuiltTree_Verifies()
    {
        // The same shape built by the rules passes: the checks are not over-eager.
        var parent = Layer1.First(k => string.CompareOrdinal(k, Layer0[0]) > 0);
        var child = Node(null, (Layer0[0], CidOf(LeafRecord), null));
        var root = Node(CidOf(child), (parent, CidOf(LeafRecord), null));

        Assert.False(VerifyHandBuilt(SmallestKey, root, child).Exists);
        Assert.Equal(CidOf(LeafRecord), VerifyHandBuilt(parent, root, child, LeafRecord).Cid);
        Assert.Equal(CidOf(LeafRecord), VerifyHandBuilt(Layer0[0], root, child, LeafRecord).Cid);
    }
}
