using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.Data.Car;
using ATProtoNet.Repo;
using static ATProtoNet.Tests.Repo.RepoFixtures;

namespace ATProtoNet.Tests.Repo;

// Reading a com.atproto.sync.getRepo export, what a resync fetches, written by RepoWriter.
public sealed class RepoSnapshotTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly SigningKey _key = K256();
    private readonly MemoryBlockStore _store = new();
    private readonly RepoWriter _writer;
    private int _revs;

    public RepoSnapshotTests() => _writer = new(Account, _key, _store);

    public void Dispose() => _key.Dispose();

    private static Tid Rev(int n) => Tid.FromInt64((1_700_000_000_000_000L << 10) + n);

    private void Write(params (string Key, string Text)[] records) =>
        _writer.Commit([.. records.Select(r => new RecordWrite(Key(r.Key), RecordBlock(r.Text)))], Rev(++_revs));

    private void WriteRecords(int count, string text = "") =>
        Write([.. Enumerable.Range(0, count).Select(i => ($"com.example.record/3l6oveex{i:D5}", $"record {i}{text}"))]);

    // The export: the head commit, every node of its tree and every record, each block mapped (null drops it).
    private byte[] Export(Func<CarBlock, CarBlock?>? map = null)
    {
        var head = _writer.Head!;
        List<CarBlock> blocks = [new(head.Cid, head.Block), .. Blocks(_writer.Tree).Blocks];
        foreach (var (_, cid) in _writer.Tree.Enumerate()) blocks.Add(new(cid, _store.TryGet(cid, out var record) ? record : throw new InvalidOperationException()));
        return Car([head.Cid], blocks.Select(map ?? (b => b)).OfType<CarBlock>().ToList());
    }

    private RepoSnapshot Verify(byte[] car, RepoLimits? limits = null) => RepoSnapshot.Verify(car, Account, _key.PublicKey, limits);

    private RepoVerificationError Refused(byte[] car, RepoLimits? limits = null, VerificationKey? key = null, Did? did = null) =>
        Assert.Throws<RepoVerificationException>(() => RepoSnapshot.Verify(car, did ?? Account, key ?? _key.PublicKey, limits)).Error;

    [Fact]
    [Trait("Spec", "REP-39")]
    [Trait("Spec", "REP-42")]
    public void Verify_Export_ReadsTheCommitTreeAndRecords()
    {
        Write(("com.example.other/3l6oveex00007", "other"));
        WriteRecords(40);

        var snapshot = Verify(Export());

        Assert.Equal(_writer.Head!.Cid, snapshot.Commit.Cid);
        Assert.Equal(_writer.Tree.Root, snapshot.Tree.Root);
        Assert.Equal(_writer.Tree.Enumerate().Select(e => new RepoSnapshotEntry(e.Key, e.Value)), snapshot.Entries);
        var records = snapshot.Records.ToList();
        Assert.Equal(41, records.Count);
        var other = Assert.Single(records, r => r.Collection.Value == "com.example.other");
        Assert.Equal("3l6oveex00007", other.Rkey.Value);
        Assert.Equal(RecordBlock("other"), other.Block.ToArray());
        Assert.Equal(new DataModelString("other"), ((DataModelMap)other.Decode())["text"]);
        Assert.True(snapshot.Blocks.TryGet(other.Cid, out _));
    }

    [Fact]
    [Trait("Spec", "REP-40")]
    public void Entries_KeysThatAreNotRecordPaths_AreKeptButAreNoRecords()
    {
        Write(("coll/self", "a"), ("com.example.record/..", "b"), ("com.example.record/c", "c"));

        var snapshot = Verify(Export());

        Assert.Equal(["coll/self", "com.example.record/..", "com.example.record/c"], snapshot.Entries.Select(e => e.Key.Value));
        Assert.Equal("c", Assert.Single(snapshot.Records).Rkey.Value);
    }

    [Fact]
    [Trait("Spec", "REP-39")]
    public void Verify_EmptyRepository_HasNoEntries()
    {
        _writer.Commit([], Rev(++_revs));

        var snapshot = Verify(Export());

        Assert.Equal(EmptyRoot, snapshot.Tree.Root);
        Assert.Empty(snapshot.Entries);
        Assert.Empty(snapshot.Records);
    }

    [Fact]
    [Trait("Spec", "REP-39")]
    public void Verify_AnotherKeyOrRepository_IsRefused()
    {
        using var other = P256();
        WriteRecords(3);

        Assert.Equal(RepoVerificationError.BadSignature, Refused(Export(), key: other.PublicKey));
        Assert.Equal(RepoVerificationError.WrongRepository, Refused(Export(), did: Did.Parse(ATProtoNet.TestKit.TestIds.OtherDid)));
    }

    [Fact]
    [Trait("Spec", "REP-39")]
    public void Verify_TamperedBlock_ThrowsCidMismatch()
    {
        WriteRecords(3);
        var car = Export();
        car[car.AsSpan().LastIndexOf("com.example.record"u8)] = (byte)'C';

        Assert.Equal(RepoVerificationError.CidMismatch, Refused(car));
    }

    [Fact]
    [Trait("Spec", "REP-16")]
    [Trait("Spec", "REP-39")]
    public void Verify_TreeNodeOrRecordMissing_ThrowsMissingBlock()
    {
        WriteRecords(60);
        var node = Blocks(_writer.Tree).Blocks.First(b => b.Cid != _writer.Tree.Root).Cid;
        var record = _writer.Tree.Enumerate().Last().Value;

        Assert.Equal(RepoVerificationError.MissingBlock, Refused(Export(b => b.Cid == node ? null : b)));
        Assert.Equal(RepoVerificationError.MissingBlock, Refused(Export(b => b.Cid == record ? null : b)));
    }

    [Fact]
    [Trait("Spec", "REP-15")]
    [Trait("Spec", "REP-39")]
    public void Verify_TreeNotInCanonicalForm_ThrowsMalformedTree()
    {
        // A tree written with omitted null links: every block matches its CID, but not the canonical tree.
        var record = RecordBlock("a");
        var node = Map(("e", Array(Map(("k", Bytes("com.example.record/a"u8)), ("p", Int(0)), ("v", Link(CidOf(record)))))));

        Assert.Equal(RepoVerificationError.MalformedTree, Refused(SignedCar(_key, CidOf(node), node, record)));
    }

    [Fact]
    [Trait("Spec", "REP-39")]
    public void Verify_NotACarOrTwoRoots_ThrowsMalformedCar()
    {
        WriteRecords(1);

        Assert.Equal(RepoVerificationError.MalformedCar, Refused([1, 2, 3]));
        Assert.Equal(RepoVerificationError.MalformedCar, Refused(Rewrite(Export(), b => b, [_writer.Head!.Cid, _writer.Head.Cid])));
    }

    [Fact]
    [Trait("Spec", "REP-39")]
    [Trait("Spec", "REP-41")]
    public async Task VerifyAsync_Stream_ReadsTheSameSnapshot()
    {
        WriteRecords(30);
        var car = Export();

        var snapshot = await RepoSnapshot.VerifyAsync(new MemoryStream(car), Account, _key.PublicKey, cancellationToken: Ct);

        Assert.Equal(Verify(car).Commit.Cid, snapshot.Commit.Cid);
        Assert.Equal(Verify(car).Entries, snapshot.Entries);
    }

    [Theory]
    [Trait("Spec", "REP-41")]
    [InlineData(nameof(RepoLimits.MaxCarBytes))]
    [InlineData(nameof(RepoLimits.MaxBlockCount))]
    [InlineData(nameof(RepoLimits.MaxRecordBytes))]
    public async Task Verify_CarBeyondALimit_ThrowsLimitExceeded(string limit)
    {
        var limits = limit switch
        {
            nameof(RepoLimits.MaxCarBytes) => new RepoLimits { MaxCarBytes = 4096 },
            nameof(RepoLimits.MaxBlockCount) => new RepoLimits { MaxBlockCount = 10 },
            _ => new RepoLimits { MaxRecordBytes = 1000 },
        };
        Write(("com.example.record/big", new string('x', 1200)));
        WriteRecords(40);
        var car = Export();

        Assert.Equal(RepoVerificationError.LimitExceeded, Refused(car, limits));
        var ex = await Assert.ThrowsAsync<RepoVerificationException>(() => RepoSnapshot.VerifyAsync(new MemoryStream(car), Account, _key.PublicKey, limits, Ct).AsTask());
        Assert.Equal(RepoVerificationError.LimitExceeded, ex.Error);
    }

    [Fact]
    [Trait("Spec", "REP-41")]
    public async Task VerifyAsync_StreamBeyondMaxCarBytes_StopsReadingEarly()
    {
        WriteRecords(2000, new string('x', 500));
        var car = Export();
        var stream = new CountingStream(car);

        var ex = await Assert.ThrowsAsync<RepoVerificationException>(() =>
            RepoSnapshot.VerifyAsync(stream, Account, _key.PublicKey, new() { MaxCarBytes = 64 * 1024 }, Ct).AsTask());

        Assert.Equal(RepoVerificationError.LimitExceeded, ex.Error);
        Assert.InRange(stream.Consumed, 64 * 1024, car.Length / 4);
    }

    // Counts what a reader takes from the stream.
    private sealed class CountingStream(byte[] data) : MemoryStream(data)
    {
        public long Consumed { get; private set; }

        public override int Read(byte[] buffer, int offset, int count) => Count(base.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(base.Read(buffer));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await base.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private int Count(int read)
        {
            Consumed += read;
            return read;
        }
    }
}
