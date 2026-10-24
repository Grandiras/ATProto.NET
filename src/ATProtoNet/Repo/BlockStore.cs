using ATProtoNet.Data.Car;

namespace ATProtoNet.Repo;

/// <summary>Content-addressed block storage that trees, proofs and snapshots read from.</summary>
/// <remarks>An implementation never returns a block that does not hash to the CID asked for.</remarks>
public interface IBlockStore
{
    /// <summary>Finds a block.</summary>
    /// <param name="cid">The block's CID.</param>
    /// <param name="block">The block's bytes, when present.</param>
    /// <returns>Whether the store has the block.</returns>
    bool TryGet(Cid cid, out ReadOnlyMemory<byte> block);
}

/// <summary>Block storage that trees and writers also write to.</summary>
public interface IWritableBlockStore : IBlockStore
{
    /// <summary>Stores a block; storing one that is already present changes nothing.</summary>
    /// <param name="cid">The block's CID. The caller guarantees it names <paramref name="block"/>.</param>
    /// <param name="block">The block's bytes.</param>
    void Put(Cid cid, ReadOnlyMemory<byte> block);
}

/// <summary>An in-memory block store. Not safe for concurrent writes.</summary>
public sealed class MemoryBlockStore : IWritableBlockStore
{
    private readonly Dictionary<Cid, ReadOnlyMemory<byte>> _blocks = [];

    /// <summary>A store holding a CAR's blocks; when a CID repeats, its first block.</summary>
    /// <param name="car">A CAR whose block CIDs the reader verified.</param>
    /// <returns>The store.</returns>
    public static MemoryBlockStore FromCar(CarFile car)
    {
        ArgumentNullException.ThrowIfNull(car);
        var store = new MemoryBlockStore();
        foreach (var block in car.Blocks) store.Put(block.Cid, block.Data);
        return store;
    }

    /// <summary>The number of blocks.</summary>
    public int Count => _blocks.Count;

    /// <summary>The blocks, in no particular order.</summary>
    public IEnumerable<CarBlock> Blocks => _blocks.Select(b => new CarBlock(b.Key, b.Value));

    /// <inheritdoc />
    public bool TryGet(Cid cid, out ReadOnlyMemory<byte> block)
    {
        ArgumentNullException.ThrowIfNull(cid);
        return _blocks.TryGetValue(cid, out block);
    }

    /// <inheritdoc />
    public void Put(Cid cid, ReadOnlyMemory<byte> block)
    {
        ArgumentNullException.ThrowIfNull(cid);
        _blocks.TryAdd(cid, block);
    }
}
