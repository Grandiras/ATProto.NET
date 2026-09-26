using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;
using ATProtoNet.Tests.Streaming;

namespace ATProtoNet.Tests.Repo;

/// <summary>Reading and verifying a full repository export, what a resync fetches.</summary>
public sealed class RepoSnapshotTests : IDisposable
{
    private readonly SyncTestRepo _repo = new();

    public void Dispose() => _repo.Dispose();

    private byte[] ExportWithRecords(int count)
    {
        for (var i = 0; i < count; i++)
            _repo.Create();
        return _repo.Export();
    }

    [Fact]
    public void Verify_Export_ReadsTheCommitTreeAndRecords()
    {
        var path = _repo.NewPath("com.example.other");
        _repo.Commit((RepoOpAction.Create, path));
        var car = ExportWithRecords(40);

        var snapshot = RepoSnapshot.Verify(car, _repo.Did, _repo.SigningKey);

        Assert.Equal(_repo.Did, snapshot.Did);
        Assert.Equal(_repo.Rev, snapshot.Rev);
        Assert.Equal(_repo.Data, snapshot.Data);
        Assert.Equal(41, snapshot.Count);

        var records = snapshot.Records.ToList();
        Assert.Equal(records.Select(r => r.Path).Order(StringComparer.Ordinal), records.Select(r => r.Path));
        var other = Assert.Single(records, r => r.Collection == Nsid.Parse("com.example.other"));
        Assert.Equal(path, other.Path);
        Assert.Equal(path[(path.IndexOf('/') + 1)..], other.Rkey.Value);

        var value = snapshot.GetRecord(other.Cid);
        Assert.Equal("com.example.record", value!.Value.GetProperty("$type").GetString());
    }

    [Fact]
    public void Verify_EmptyRepository_HasNoRecords()
    {
        var snapshot = RepoSnapshot.Verify(_repo.Export(), _repo.Did, _repo.SigningKey);

        Assert.Equal(0, snapshot.Count);
        Assert.Empty(snapshot.Records);
    }

    [Fact]
    public void Verify_AnotherKey_Throws()
    {
        using var other = AtProtoCrypto.GenerateK256Key();
        var car = ExportWithRecords(3);

        Assert.Throws<RepoVerificationException>(() => RepoSnapshot.Verify(car, _repo.Did, other.ToDidKey()));
    }

    [Fact]
    public void Verify_AnotherRepository_Throws()
    {
        var car = ExportWithRecords(3);

        var ex = Assert.Throws<RepoVerificationException>(() =>
            RepoSnapshot.Verify(car, Did.Parse("did:plc:someoneelseaaaaaaaaaaaa"), _repo.SigningKey));
        Assert.Contains("is for", ex.Message);
    }

    [Fact]
    public void Verify_TamperedBlock_Throws()
    {
        var car = ExportWithRecords(3);

        // Every record carries "com.example.record"; change a letter of the last one's.
        var at = car.AsSpan().LastIndexOf("com.example.record"u8);
        car[at] = (byte)'C';

        Assert.Throws<RepoVerificationException>(() => RepoSnapshot.Verify(car, _repo.Did, _repo.SigningKey));
    }

    [Fact]
    public void Verify_MissingTreeNode_Throws()
    {
        var car = ExportWithRecords(60);
        var reader = CarReader.FromBytes(car);
        var (_, nodes) = _repo.Tree.Serialize();
        var rootText = CidComputation.EncodeCidToString(_repo.Tree.ComputeRootCid());
        var dropped = nodes.Keys.First(k => k != rootText);

        var partial = CarWriter.Write(reader.Roots[0],
            reader.Blocks.Where(b => CidComputation.EncodeCidToString(b.Cid) != dropped));

        var ex = Assert.Throws<RepoVerificationException>(() => RepoSnapshot.Verify(partial, _repo.Did, _repo.SigningKey));
        Assert.Contains("tree", ex.Message);
    }

    [Fact]
    public void Verify_NotACar_Throws()
    {
        Assert.Throws<RepoVerificationException>(() => RepoSnapshot.Verify([1, 2, 3], _repo.Did, _repo.SigningKey));
    }
}
