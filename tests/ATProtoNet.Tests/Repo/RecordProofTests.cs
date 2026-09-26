using System.Text;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Repo;
using ATProtoNet.Serialization;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Repo;

/// <summary>
/// <see cref="RecordProof"/> against <c>com.atproto.sync.getRecord</c> proofs the reference
/// implementation produced (<c>Repo/TestData/record-proof.json</c>), tampered copies of them, and
/// hand-built trees that break the MST rules.
/// </summary>
public sealed class RecordProofTests
{
    private static readonly Vector Reference = Vector.Load();

    private static readonly Nsid Records = Nsid.Parse("com.example.record");

    // ──────────────────────────────────────────────────────────
    //  Reference proofs
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Verify_ReferenceProof_ReturnsTheRecord()
    {
        var record = Verify(Reference.PresentCar, Reference.Rkey);

        Assert.True(record.Exists);
        Assert.Equal(AtUri.Parse($"at://{Reference.Did}/com.example.record/{Reference.Rkey}"), record.Uri);
        Assert.Equal(Reference.RecordCid, record.Cid);
        Assert.Equal(Reference.Commit, record.Commit);
        Assert.Equal(Reference.Rev, record.Rev);

        var value = record.Value!.Value;
        Assert.Equal("com.example.record", value.GetProperty("$type").GetString());
        Assert.Equal("the proven record", value.GetProperty("text").GetString());
        Assert.Equal(42, value.GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("flags")[2].ValueKind);
        Assert.Equal(
            [1, 2, 3, 4],
            LexBase64.Decode(value.GetProperty("nested").GetProperty("bytes").GetProperty("$bytes").GetString()!));
        Assert.Equal("2026-01-02T03:04:05.678Z", value.GetProperty("createdAt").GetString());
    }

    [Fact]
    public void Verify_ReferenceAbsenceProof_ProvesTheRecordAbsent()
    {
        var record = Verify(Reference.AbsentCar, Reference.AbsentRkey);

        Assert.False(record.Exists);
        Assert.Null(record.Cid);
        Assert.Null(record.Value);
        Assert.Equal(Reference.Commit, record.Commit);
        Assert.Equal(Reference.Rev, record.Rev);
    }

    [Fact]
    public void Verify_ProofOfARecord_AlsoProvesItsNeighbourAbsent()
    {
        // Both keys end in the same leaf node, so its path settles either question.
        Assert.False(Verify(Reference.PresentCar, Reference.AbsentRkey).Exists);
    }

    [Fact]
    public void Verify_AbsenceProof_DoesNotProveTheRecordItPassesBy()
    {
        // The absence proof walks through the record's leaf node but carries no record block.
        var ex = Assert.Throws<RepoVerificationException>(() => Verify(Reference.AbsentCar, Reference.Rkey));

        Assert.Contains("record block", ex.Message);
    }

    [Fact]
    public void Verify_KeyOffTheProvenPath_ThrowsIncomplete()
    {
        // Between the root's second and third keys: a subtree the proof does not include.
        var ex = Assert.Throws<RepoVerificationException>(() => Verify(Reference.PresentCar, RecordKey.Parse("3lenax35zzz22")));

        Assert.Contains("incomplete", ex.Message);
    }

    // ──────────────────────────────────────────────────────────
    //  Tampered proofs
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Verify_AnotherSigningKey_Throws()
    {
        using var other = AtProtoCrypto.GenerateK256Key();

        var ex = Assert.Throws<RepoVerificationException>(
            () => RecordProof.Verify(Reference.PresentCar, Reference.Did, Records, Reference.Rkey, other.ToDidKey()));

        Assert.Contains("signature", ex.Message);
    }

    [Fact]
    public void Verify_AnotherRepository_Throws()
    {
        var ex = Assert.Throws<RepoVerificationException>(
            () => RecordProof.Verify(
                Reference.PresentCar, Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz"), Records, Reference.Rkey,
                Reference.SigningKey));

        Assert.Contains("did:plc:vwzwgnygau7ed7b7wt5ux7y2", ex.Message);
    }

    [Fact]
    public void Verify_RecordBytesAltered_Throws()
    {
        var recordCid = CidComputation.DecodeCidString(Reference.RecordCid.Value);
        var car = Rewrite(Reference.PresentCar, block =>
            block.Cid.AsSpan().SequenceEqual(recordCid) ? block with { Data = Flip(block.Data, 20) } : block);

        var ex = Assert.Throws<RepoVerificationException>(() => Verify(car, Reference.Rkey));

        Assert.IsType<FormatException>(ex.InnerException);
    }

    [Fact]
    public void Verify_RecordSwappedForAnotherWithItsOwnCid_Throws()
    {
        // Every block still matches its CID, but the signed tree points at the original record.
        var recordCid = CidComputation.DecodeCidString(Reference.RecordCid.Value);
        var forged = DagCborEncoder.Encode(JsonSerializer.SerializeToElement(
            new Dictionary<string, object> { ["$type"] = "com.example.record", ["text"] = "forged" }));
        var car = Rewrite(Reference.PresentCar, block =>
            block.Cid.AsSpan().SequenceEqual(recordCid)
                ? new CarBlock(CidComputation.ComputeBinaryForDagCbor(forged), forged)
                : block);

        var ex = Assert.Throws<RepoVerificationException>(() => Verify(car, Reference.Rkey));

        Assert.Contains("record block", ex.Message);
    }

    [Fact]
    public void Verify_TreeNodeMissing_Throws()
    {
        var reader = CarReader.FromBytes(Reference.PresentCar);
        var commit = DagCborDecoder.Decode(reader.GetRootBlock()!.Data);
        var dataCid = CidComputation.DecodeCidString(commit.GetProperty("data").GetProperty("$link").GetString()!);
        var car = Rewrite(Reference.PresentCar, block => block.Cid.AsSpan().SequenceEqual(dataCid) ? null : block);

        var ex = Assert.Throws<RepoVerificationException>(() => Verify(car, Reference.Rkey));

        Assert.Contains("incomplete", ex.Message);
    }

    [Fact]
    public void Verify_CommitBlockMissing_Throws()
    {
        var root = CarReader.FromBytes(Reference.PresentCar).Roots[0];
        var car = Rewrite(Reference.PresentCar, block => block.Cid.AsSpan().SequenceEqual(root) ? null : block);

        var ex = Assert.Throws<RepoVerificationException>(() => Verify(car, Reference.Rkey));

        Assert.Contains("commit block", ex.Message);
    }

    [Fact]
    public void Verify_SignatureAltered_Throws()
    {
        var reader = CarReader.FromBytes(Reference.PresentCar);
        var commit = reader.GetRootBlock()!;
        var signature = FirehoseVerifier.ExtractSignedView(commit.Data)!.Value.SigBytes!;
        var at = commit.Data.AsSpan().IndexOf(signature);
        var altered = Flip(commit.Data, at + 10);
        var alteredCid = CidComputation.ComputeBinaryForDagCbor(altered);

        var car = Rewrite(
            Reference.PresentCar,
            block => block.Cid.AsSpan().SequenceEqual(commit.Cid) ? new CarBlock(alteredCid, altered) : block,
            [alteredCid]);

        var ex = Assert.Throws<RepoVerificationException>(() => Verify(car, Reference.Rkey));

        Assert.Contains("signature", ex.Message);
    }

    [Fact]
    public void Verify_CommitResignedByAnotherKey_ThrowsForTheAccountsKey()
    {
        using var other = AtProtoCrypto.GenerateP256Key();
        var reader = CarReader.FromBytes(Reference.PresentCar);
        var original = reader.GetRootBlock()!;
        var fields = DagCborDecoder.Decode(original.Data);
        var resigned = new RepoCommit
        {
            Did = fields.GetProperty("did").GetString()!,
            Data = CidComputation.DecodeCidString(fields.GetProperty("data").GetProperty("$link").GetString()!),
            Rev = fields.GetProperty("rev").GetString()!,
        }.Sign(other);

        var car = Rewrite(
            Reference.PresentCar,
            block => block.Cid.AsSpan().SequenceEqual(original.Cid) ? new CarBlock(resigned.BinaryCid, resigned.Bytes) : block,
            [resigned.BinaryCid]);

        // A well-formed commit, signed by a key that is not the account's.
        Assert.True(RecordProof.Verify(car, Reference.Did, Records, Reference.Rkey, other.ToDidKey()).Exists);
        Assert.Throws<RepoVerificationException>(() => Verify(car, Reference.Rkey));
    }

    [Fact]
    public void Verify_TwoRoots_Throws()
    {
        var reader = CarReader.FromBytes(Reference.PresentCar);
        var car = CarWriter.Write([reader.Roots[0], reader.Roots[0]], reader.Blocks);

        var ex = Assert.Throws<RepoVerificationException>(() => Verify(car, Reference.Rkey));

        Assert.Contains("one root", ex.Message);
    }

    [Fact]
    public void Verify_NotACar_Throws()
    {
        var ex = Assert.Throws<RepoVerificationException>(() => Verify([0x01, 0x02, 0x03], Reference.Rkey));

        Assert.IsType<FormatException>(ex.InnerException);
    }

    [Fact]
    public void Verify_SigningKeyNotADidKey_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(
            () => RecordProof.Verify(Reference.PresentCar, Reference.Did, Records, Reference.Rkey, "did:key:zNotAKey"));
    }

    // ──────────────────────────────────────────────────────────
    //  Proofs of SDK-written repositories
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Verify_ProofsOfAnSdkWrittenRepo_ProveEachRecordAndAbsence()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var mst = MerkleSearchTree.Create();
        var records = new Dictionary<string, CarBlock>();
        for (var i = 0; i < 200; i++)
        {
            var (bytes, cid) = DagCborEncoder.EncodeWithCid(JsonSerializer.SerializeToElement(
                new Dictionary<string, object> { ["$type"] = "com.example.record", ["n"] = i }));
            var path = $"com.example.record/3l6oveex{i:D5}";
            var binary = CidComputation.DecodeCidString(cid.Value);
            mst.Add(path, binary);
            records[path] = new CarBlock(binary, bytes);
        }

        var commit = new RepoCommit { Did = Reference.Did.Value, Data = mst.ComputeRootCid(), Rev = "3m7fmkxcn5d2a" }.Sign(key);

        foreach (var i in new[] { 0, 57, 123, 199 })
        {
            var path = $"com.example.record/3l6oveex{i:D5}";
            var (_, proof) = mst.SerializeProof([path]);
            var car = CarWriter.Write(
                commit.BinaryCid,
                [
                    new CarBlock(commit.BinaryCid, commit.Bytes),
                    .. proof.Select(b => new CarBlock(CidComputation.DecodeCidString(b.Key), b.Value)),
                    records[path],
                ]);

            var record = RecordProof.Verify(car, Reference.Did, Records, RecordKey.Parse($"3l6oveex{i:D5}"), key.ToDidKey());
            Assert.Equal(i, record.Value!.Value.GetProperty("n").GetInt32());
            Assert.Equal(commit.Cid, record.Commit);

            var gap = RecordProof.Verify(car, Reference.Did, Records, RecordKey.Parse($"3l6oveex{i:D5}a"), key.ToDidKey());
            Assert.False(gap.Exists);
        }
    }

    [Fact]
    public void Verify_EmptyRepository_ProvesEveryRecordAbsent()
    {
        using var key = AtProtoCrypto.GenerateK256Key();
        var (root, blocks) = MerkleSearchTree.Create().Serialize();

        var car = SignedProof(key, root, blocks.Select(b => new CarBlock(CidComputation.DecodeCidString(b.Key), b.Value)));

        Assert.False(RecordProof.Verify(car, Reference.Did, Records, Reference.Rkey, key.ToDidKey()).Exists);
    }

    // ──────────────────────────────────────────────────────────
    //  Trees that break the MST rules
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Verify_NodeKeysOutOfOrder_Throws()
    {
        using var key = AtProtoCrypto.GenerateK256Key();
        var root = Node(null, (Layer0[1], Leaf), (Layer0[0], Leaf));

        var ex = Assert.Throws<RepoVerificationException>(() => VerifyHandBuilt(key, root, Layer0[0]));

        Assert.Contains("out of order", ex.Message);
    }

    [Fact]
    public void Verify_NodeMixingLayers_Throws()
    {
        using var key = AtProtoCrypto.GenerateK256Key();
        var keys = new[] { Layer0[0], Layer1[0] }.Order(StringComparer.Ordinal).ToArray();
        var root = Node(null, (keys[0], Leaf), (keys[1], Leaf));

        var ex = Assert.Throws<RepoVerificationException>(() => VerifyHandBuilt(key, root, keys[0]));

        Assert.Contains("wrong layer", ex.Message);
    }

    [Fact]
    public void Verify_ChildOnItsParentsLayer_Throws()
    {
        using var key = AtProtoCrypto.GenerateK256Key();
        var child = Node(null, (Layer1[0], Leaf));
        var root = Node(child, (Layer1[1], Leaf));

        var ex = Assert.Throws<RepoVerificationException>(() => VerifyHandBuilt(key, root, Layer1[0], child));

        Assert.Contains("wrong layer", ex.Message);
    }

    [Fact]
    public void Verify_SubtreeKeyOutsideItsRange_Throws()
    {
        // The subtree left of a key holds a key greater than it. A walk for a smaller key lands
        // there, and without the range check would report the smaller key absent.
        using var key = AtProtoCrypto.GenerateK256Key();
        var parent = Layer1.First(k => string.CompareOrdinal(k, Layer0[0]) > 0);
        var misplaced = Layer0.First(k => string.CompareOrdinal(k, parent) > 0);
        var child = Node(null, (misplaced, Leaf));
        var root = Node(child, (parent, Leaf));

        var ex = Assert.Throws<RepoVerificationException>(() => VerifyHandBuilt(key, root, SmallestKey, child));

        Assert.Contains("out of order", ex.Message);
    }

    [Fact]
    public void Verify_WellFormedHandBuiltTree_Verifies()
    {
        // The same shape built by the rules passes: the checks are not over-eager.
        using var key = AtProtoCrypto.GenerateK256Key();
        var parent = Layer1.First(k => string.CompareOrdinal(k, Layer0[0]) > 0);
        var child = Node(null, (Layer0[0], Leaf));
        var root = Node(child, (parent, Leaf));

        Assert.False(VerifyHandBuilt(key, root, SmallestKey, child).Exists);
        Assert.True(VerifyHandBuiltWithLeaf(key, root, parent, child).Exists);
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    /// <summary>The record every hand-built leaf points at, and its CID.</summary>
    private static readonly byte[] LeafRecord = DagCborEncoder.Encode(
        JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["$type"] = "com.example.record" }));

    /// <inheritdoc cref="LeafRecord"/>
    private static readonly byte[] Leaf = CidComputation.ComputeBinaryForDagCbor(LeafRecord);

    private static VerifiedRecord Verify(byte[] car, RecordKey rkey) =>
        RecordProof.Verify(car, Reference.Did, Records, rkey, Reference.SigningKey);

    private static VerifiedRecord VerifyHandBuilt(AtProtoKey key, CarBlock root, string path, params CarBlock[] others)
    {
        var slash = path.IndexOf('/');
        var car = SignedProof(key, root.Cid, [root, .. others]);
        return RecordProof.Verify(
            car, Reference.Did, Nsid.Parse(path[..slash]), RecordKey.Parse(path[(slash + 1)..]), key.ToDidKey());
    }

    /// <summary>As <see cref="VerifyHandBuilt"/>, with the record block every leaf points at included.</summary>
    private static VerifiedRecord VerifyHandBuiltWithLeaf(AtProtoKey key, CarBlock root, string path, params CarBlock[] others) =>
        VerifyHandBuilt(key, root, path, [.. others, new CarBlock(Leaf, LeafRecord)]);

    private static byte[] SignedProof(AtProtoKey key, byte[] dataCid, IEnumerable<CarBlock> nodes)
    {
        var commit = new RepoCommit { Did = Reference.Did.Value, Data = dataCid, Rev = "3m7fmkxcn5d2a" }.Sign(key);
        return CarWriter.Write(commit.BinaryCid, [new CarBlock(commit.BinaryCid, commit.Bytes), .. nodes]);
    }

    /// <summary>Keys on MST layer 0, and on layer 1, in ascending order.</summary>
    private static readonly string[] Layer0 = KeysOnLayer(0, 8);

    /// <inheritdoc cref="Layer0"/>
    private static readonly string[] Layer1 = KeysOnLayer(1, 8);

    /// <summary>A key below every <see cref="Layer0"/> and <see cref="Layer1"/> key.</summary>
    private const string SmallestKey = "com.example.aaa/x";

    private static string[] KeysOnLayer(int layer, int count) =>
        Enumerable.Range(0, 100_000)
            .Select(i => $"com.example.record/k{i:D6}")
            .Where(k => MstKeyDepth.ComputeDepth(k) == layer)
            .Take(count)
            .ToArray();

    /// <summary>Encodes an MST node as given, without the rules <see cref="MerkleSearchTree"/> keeps.</summary>
    private static CarBlock Node(CarBlock? left, params (string Key, byte[] Value)[] entries)
    {
        var encoded = new List<MstTreeEntry>();
        var previous = string.Empty;
        foreach (var (entryKey, value) in entries)
        {
            var prefix = MerkleSearchTree.SharedPrefixLength(previous, entryKey);
            encoded.Add(new MstTreeEntry(prefix, Encoding.ASCII.GetBytes(entryKey[prefix..]), value, null));
            previous = entryKey;
        }

        var bytes = new MstNodeData { Left = left?.Cid, Entries = encoded }.ToBytes();
        return new CarBlock(CidComputation.ComputeBinaryForDagCbor(bytes), bytes);
    }

    private static byte[] Rewrite(byte[] car, Func<CarBlock, CarBlock?> map, IReadOnlyList<byte[]>? roots = null)
    {
        var reader = CarReader.FromBytes(car);
        return CarWriter.Write(roots ?? reader.Roots, reader.Blocks.Select(map).OfType<CarBlock>().ToList());
    }

    private static byte[] Flip(byte[] data, int index)
    {
        var copy = data.ToArray();
        copy[index] ^= 0x01;
        return copy;
    }

    /// <summary>The contents of <c>Repo/TestData/record-proof.json</c>.</summary>
    private sealed record Vector(
        Did Did,
        string SigningKey,
        Cid Commit,
        Tid Rev,
        RecordKey Rkey,
        Cid RecordCid,
        RecordKey AbsentRkey,
        byte[] PresentCar,
        byte[] AbsentCar)
    {
        public static Vector Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Repo", "TestData", "record-proof.json");
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = json.RootElement;
            string Get(string name) => root.GetProperty(name).GetString()!;

            return new Vector(
                Did.Parse(Get("did")),
                Get("signingKey"),
                Cid.Parse(Get("commit")),
                Tid.Parse(Get("rev")),
                RecordKey.Parse(Get("rkey")),
                Cid.Parse(Get("recordCid")),
                RecordKey.Parse(Get("absentRkey")),
                Convert.FromBase64String(Get("presentCar")),
                Convert.FromBase64String(Get("absentCar")));
        }
    }
}
