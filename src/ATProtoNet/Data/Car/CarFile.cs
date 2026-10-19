namespace ATProtoNet.Data.Car;

/// <summary>A CAR header: its roots. The version is always 1, the only one read or written.</summary>
public sealed class CarHeader
{
    internal CarHeader(IReadOnlyList<Cid> roots) => Roots = roots;

    /// <summary>The root CIDs, possibly none.</summary>
    public IReadOnlyList<Cid> Roots { get; }
}

/// <summary>A CAR block: a CID and the bytes it names.</summary>
/// <param name="cid">The block's CID, as written in the CAR.</param>
/// <param name="data">The block's bytes.</param>
public sealed class CarBlock(Cid cid, ReadOnlyMemory<byte> data)
{
    /// <summary>The block's CID, as written in the CAR.</summary>
    public Cid Cid { get; } = cid ?? throw new ArgumentNullException(nameof(cid));

    /// <summary>The block's bytes.</summary>
    public ReadOnlyMemory<byte> Data { get; } = data;
}

/// <summary>A CAR read whole: its header and its blocks, which slice the input rather than copy it.</summary>
public sealed class CarFile
{
    private readonly List<CarBlock> _blocks;

    // Built on the first lookup: plenty of readers only walk the blocks.
    private Dictionary<Cid, ReadOnlyMemory<byte>>? _byCid;

    internal CarFile(CarHeader header, List<CarBlock> blocks) => (Header, _blocks) = (header, blocks);

    /// <summary>The header.</summary>
    public CarHeader Header { get; }

    /// <summary>Every block, in file order, duplicates included.</summary>
    public IReadOnlyList<CarBlock> Blocks => _blocks;

    /// <summary>Finds a block by CID; when a CID occurs more than once, the first occurrence.</summary>
    /// <param name="cid">The CID.</param>
    /// <param name="data">The block's bytes, when found.</param>
    /// <returns>Whether the CAR has the block.</returns>
    public bool TryGetBlock(Cid cid, out ReadOnlyMemory<byte> data)
    {
        if (_byCid is null)
        {
            var byCid = new Dictionary<Cid, ReadOnlyMemory<byte>>(_blocks.Count);
            foreach (var block in _blocks) byCid.TryAdd(block.Cid, block.Data);
            Interlocked.CompareExchange(ref _byCid, byCid, null);
        }
        return _byCid.TryGetValue(cid, out data);
    }
}
