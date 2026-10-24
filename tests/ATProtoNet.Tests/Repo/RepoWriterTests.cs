using ATProtoNet.Crypto;
using ATProtoNet.Repo;
using static ATProtoNet.Tests.Repo.RepoFixtures;

namespace ATProtoNet.Tests.Repo;

public sealed class RepoWriterTests : IDisposable
{
    private readonly SigningKey _key = K256();
    private readonly MemoryBlockStore _store = new();

    public void Dispose() => _key.Dispose();

    private static Tid Rev(int n) => Tid.FromInt64((1_700_000_000_000_000L << 10) + n);

    private static RecordWrite Put(string key, string text) => new(Key(key), RecordBlock(text));

    private static RecordWrite Remove(string key) => new(Key(key), null);

    private RepoWriter Writer() => new(Account, _key, _store);

    [Fact]
    [Trait("Spec", "REP-22")]
    public void Commit_NewRepository_SignsTheTreeOfItsRecords()
    {
        var writer = Writer();

        var result = writer.Commit([Put("com.example.record/a", "a"), Put("com.example.record/b", "b"), Put("app.bsky.feed.post/c", "c")], Rev(1));

        var commit = result.Commit;
        Assert.Same(commit, writer.Head);
        Assert.True(commit.Verify(_key.PublicKey));
        Assert.Equal((Account, Rev(1)), (commit.Commit.Did, commit.Commit.Rev));
        Assert.Equal(Tree(("com.example.record/a", CidOf(RecordBlock("a"))), ("com.example.record/b", CidOf(RecordBlock("b"))), ("app.bsky.feed.post/c", CidOf(RecordBlock("c")))).Root, commit.Commit.Data);
        Assert.Equal(commit.Commit.Data, writer.Tree.Root);
        Assert.Equal([new RepoOperation(Key("com.example.record/a"), null, CidOf(RecordBlock("a"))), new RepoOperation(Key("com.example.record/b"), null, CidOf(RecordBlock("b"))), new RepoOperation(Key("app.bsky.feed.post/c"), null, CidOf(RecordBlock("c")))], result.Operations);
        // A new repository's every node is new, so the diff is the whole repository.
        Assert.True(result.Blocks.TryGet(commit.Cid, out _));
        Assert.All(Blocks(writer.Tree).Blocks, b => Assert.True(result.Blocks.TryGet(b.Cid, out _)));
        Assert.All(result.Operations, op => Assert.True(result.Blocks.TryGet(op.After!, out _)));
        Assert.All(result.Blocks.Blocks, b => Assert.True(_store.TryGet(b.Cid, out _)));
    }

    [Fact]
    public void Commit_CreateUpdateAndDelete_ReportsEachKeysValuesBeforeAndAfter()
    {
        var writer = Writer();
        writer.Commit([Put("com.example.record/a", "a"), Put("com.example.record/b", "b")], Rev(1));

        var result = writer.Commit([Put("com.example.record/a", "a2"), Remove("com.example.record/b"), Put("com.example.record/c", "c")], Rev(2));

        Assert.Equal(
            [new RepoOperation(Key("com.example.record/a"), CidOf(RecordBlock("a")), CidOf(RecordBlock("a2"))), new RepoOperation(Key("com.example.record/b"), CidOf(RecordBlock("b")), null), new RepoOperation(Key("com.example.record/c"), null, CidOf(RecordBlock("c")))],
            result.Operations);
        Assert.Equal(["com.example.record/a", "com.example.record/c"], writer.Tree.Enumerate().Select(e => e.Key.Value));
    }

    [Fact]
    public void Commit_RecordItAlreadyHas_IsNoOperationAndKeepsTheRoot()
    {
        var writer = Writer();
        var first = writer.Commit([Put("com.example.record/a", "a")], Rev(1));

        var second = writer.Commit([Put("com.example.record/a", "a")], Rev(2));

        Assert.Empty(second.Operations);
        Assert.Equal(first.Commit.Commit.Data, second.Commit.Commit.Data);
        Assert.NotEqual(first.Commit.Cid, second.Commit.Cid);
    }

    [Fact]
    [Trait("Spec", "REP-20")]
    [Trait("Spec", "REP-22")]
    public void Commit_Diff_LetsAHolderOfOnlyTheDiffAndThePreviousRootInvertIt()
    {
        // Seeded so a failure reproduces: a consumer of #commit events holds nothing but the blocks.
        var random = new Random(4242);
        var writer = Writer();
        var keys = GeneratedKeys(12, 400);
        writer.Commit([.. keys[..300].Select(k => new RecordWrite(k, RecordBlock(k.Value)))], Rev(0));

        for (var round = 1; round <= 30; round++)
        {
            var previous = writer.Head!.Commit.Data;
            var present = writer.Tree.Enumerate().Select(e => e.Key).ToHashSet();
            var writes = keys.OrderBy(_ => random.Next()).Take(random.Next(1, 8))
                .Select(k => present.Contains(k) && random.Next(2) == 0 ? new RecordWrite(k, null) : new RecordWrite(k, RecordBlock($"{k} {round}"))).ToList();

            var result = writer.Commit(writes, Rev(round));

            var tree = MerkleSearchTree.Load(result.Commit.Commit.Data, result.Blocks);
            foreach (var (key, before, after) in Enumerable.Reverse(result.Operations))
            {
                tree = tree.Apply(key, before, out var found);
                Assert.Equal(after, found);
                if (after is not null) Assert.True(result.Blocks.TryGet(after, out _));
            }
            Assert.Equal(previous, tree.Root);
            Assert.True(result.Blocks.TryGet(result.Commit.Cid, out var block));
            Assert.Equal(result.Commit.Block.ToArray(), block.ToArray());
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(4)]
    public void Commit_RevNotAfterTheHead_ThrowsArgumentExceptionAndKeepsTheHead(int rev)
    {
        var writer = Writer();
        var head = writer.Commit([Put("com.example.record/a", "a")], Rev(5)).Commit;

        Assert.Throws<ArgumentException>(() => writer.Commit([Put("com.example.record/b", "b")], Rev(rev)));
        Assert.Same(head, writer.Head);
    }

    [Fact]
    public void Commit_KeyWrittenTwice_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => Writer().Commit([Put("com.example.record/a", "a"), Remove("com.example.record/a")], Rev(1)));

    [Theory]
    [InlineData(new byte[] { 0x80 })]       // DRISL, but an array
    [InlineData(new byte[] { 0xA1, 0x61 })] // not DRISL
    public void Commit_RecordNotADrislMap_ThrowsArgumentException(byte[] record) =>
        Assert.Throws<ArgumentException>(() => Writer().Commit([new RecordWrite(Key("com.example.record/a"), record)], Rev(1)));

    [Fact]
    public void Commit_DeleteOfAnAbsentRecord_ThrowsInvalidOperationExceptionAndChangesNothing()
    {
        var writer = Writer();
        var head = writer.Commit([Put("com.example.record/a", "a")], Rev(1)).Commit;
        var blocks = _store.Count;

        Assert.Throws<InvalidOperationException>(() => writer.Commit([Put("com.example.record/b", "b"), Remove("com.example.record/zzz")], Rev(2)));

        Assert.Same(head, writer.Head);
        Assert.Equal(head.Commit.Data, writer.Tree.Root);
        Assert.Equal(blocks, _store.Count);
    }

    [Fact]
    public void Constructor_HeadOfAnotherRepository_ThrowsArgumentException()
    {
        var head = new RepoCommit { Did = Did.Parse(ATProtoNet.TestKit.TestIds.OtherDid), Rev = Rev(1), Data = EmptyRoot }.Sign(_key);

        Assert.Throws<ArgumentException>(() => new RepoWriter(Account, _key, _store, head));
    }

    [Fact]
    [Trait("Spec", "REP-44")]
    public void Constructor_ExistingHead_ContinuesFromItsTreeInTheStore()
    {
        var head = Writer().Commit([Put("com.example.record/a", "a"), Put("com.example.record/b", "b")], Rev(1)).Commit;

        var writer = new RepoWriter(Account, _key, _store, head);
        var result = writer.Commit([Remove("com.example.record/a")], Rev(2));

        Assert.Equal(Tree(("com.example.record/b", CidOf(RecordBlock("b")))).Root, result.Commit.Commit.Data);
        Assert.Throws<ArgumentException>(() => new RepoWriter(Account, _key, _store, head).Commit([], Rev(1)));
    }
}
