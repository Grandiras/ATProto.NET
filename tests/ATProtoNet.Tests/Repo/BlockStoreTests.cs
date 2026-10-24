using ATProtoNet.Data.Car;
using ATProtoNet.Repo;
using static ATProtoNet.Tests.Repo.RepoFixtures;

namespace ATProtoNet.Tests.Repo;

public class BlockStoreTests
{
    [Fact]
    [Trait("Spec", "REP-43")]
    public void Put_RepeatedCid_KeepsTheFirstBlock()
    {
        var block = RecordBlock("a");
        var store = new MemoryBlockStore();

        store.Put(CidOf(block), block);
        store.Put(CidOf(block), RecordBlock("b"));

        Assert.Equal(1, store.Count);
        Assert.True(store.TryGet(CidOf(block), out var stored));
        Assert.Equal(block, stored.ToArray());
        Assert.False(store.TryGet(Fake("absent"), out _));
    }

    [Fact]
    [Trait("Spec", "REP-43")]
    public void FromCar_RepeatedCid_KeepsTheFirstBlock()
    {
        var block = RecordBlock("a");
        var car = CarReader.Read(Car([], [new(CidOf(block), block), new(CidOf(block), RecordBlock("b"))]));

        var store = MemoryBlockStore.FromCar(car);

        Assert.True(store.TryGet(CidOf(block), out var stored));
        Assert.Equal(block, stored.ToArray());
        Assert.Equal(1, store.Count);
    }

    [Fact]
    [Trait("Spec", "REP-43")]
    public void TryGet_NullCid_ThrowsArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => new MemoryBlockStore().TryGet(null!, out _));

    [Fact]
    [Trait("Spec", "REP-19")]
    [Trait("Spec", "REP-44")]
    public void Load_CallerStore_ReadsOnlyTheNodesAnOperationReaches()
    {
        var tree = GeneratedTree(8, 1000);
        var store = new CountingStore(Blocks(tree));

        var loaded = MerkleSearchTree.Load(tree.Root, store);
        var (key, value) = tree.Enumerate().ElementAt(500);

        Assert.Equal(value, loaded.Get(key));
        Assert.InRange(store.Reads, 2, 8);
        Assert.Equal(tree.Root, loaded.Root);
    }

    [Fact]
    [Trait("Spec", "REP-44")]
    public void LoadComplete_CallerStore_ReadsEveryNodeThroughIt()
    {
        var tree = GeneratedTree(8, 1000);
        var blocks = Blocks(tree);
        var store = new CountingStore(blocks);

        Assert.Equal(tree.Enumerate(), MerkleSearchTree.LoadComplete(tree.Root, store).Enumerate());
        Assert.True(store.Reads >= blocks.Count);
    }

    // A store backed by another, as a database or a CAR on disk would be, counting what is read from it.
    private sealed class CountingStore(IBlockStore inner) : IBlockStore
    {
        public int Reads { get; private set; }

        public bool TryGet(Cid cid, out ReadOnlyMemory<byte> block)
        {
            Reads++;
            return inner.TryGet(cid, out block);
        }
    }
}
