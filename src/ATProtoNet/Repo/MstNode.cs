using System.Buffers;
using ATProtoNet.Data;

namespace ATProtoNet.Repo;

// What a node holds: its keys (all of the node's layer, ascending), their values, and the subtrees
// one layer down before, between and after them (Subtrees[i] lies before Keys[i]; null when empty).
internal sealed record MstContent(byte[][] Keys, Cid[] Values, MstNode?[] Subtrees);

// A tree node. Immutable: an edit builds new nodes along its path and shares the rest, so a node's
// CID, once computed, stays valid. A node read from a store loads its content on first use; one whose
// block the store lacks is a stub, known only by its CID.
internal sealed partial class MstNode
{
    private readonly Source? _source;
    private Cid? _cid;
    private MstContent? _content;

    public MstNode(int layer, byte[][] keys, Cid[] values, MstNode?[] subtrees) =>
        (Layer, _content) = (layer, new(keys, values, subtrees));

    private MstNode(Cid cid, int layer, Source source, MstContent? content = null) =>
        (_cid, Layer, _source, _content) = (cid, layer, source, content);

    public int Layer { get; }

    public Cid Cid => _cid ??= ComputeCid();

    // Loads the content if needed. Throws MstIncompleteException for a stub and
    // RepoVerificationException for a malformed node.
    public MstContent Content => _content ?? (TryLoad(out var content) ? content : throw new MstIncompleteException(_cid!));

    public static MstNode Empty() => new(0, [], [], [null]);

    public bool TryGetContent(out MstContent content)
    {
        if (_content is { } loaded)
        {
            content = loaded;
            return true;
        }
        return TryLoad(out content);
    }

    // The node's block: as stored when it was read from a store, otherwise its encoding.
    public ReadOnlyMemory<byte> GetBlock()
    {
        if (_source is not null && _source.Store.TryGet(_cid!, out var stored)) return stored;
        var buffer = new ArrayBufferWriter<byte>(MaxEncodedLength(Content));
        Write(buffer);
        return buffer.WrittenMemory;
    }

    // The CID of the node's canonical encoding, whatever it was read as.
    public Cid ComputeCid()
    {
        var content = Content;
        var buffer = ArrayPool<byte>.Shared.Rent(MaxEncodedLength(content));
        try
        {
            return Cid.Compute(CidCodec.DagCbor, buffer.AsSpan(0, Encode(content, buffer)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Write(IBufferWriter<byte> output)
    {
        var content = Content;
        output.Advance(Encode(content, output.GetSpan(MaxEncodedLength(content))));
    }

    // Reads the root of a tree, whose layer its keys fix.
    public static MstNode Read(Cid root, IBlockStore store)
    {
        if (!store.TryGet(root, out var block)) throw new MstIncompleteException(root);
        var source = new Source(store, null, null, 0);
        var (layer, content) = Decode(root, block, source, expectedLayer: -1);
        return new(root, layer, source, content);
    }

    private bool TryLoad(out MstContent content)
    {
        content = null!;
        if (_source is null || !_source.Store.TryGet(_cid!, out var block)) return false;
        content = Decode(_cid!, block, _source, Layer).Content;
        Interlocked.CompareExchange(ref _content, content, null);
        return true;
    }

    // Where a node to load comes from: the store, the key range its parent leaves it, and its depth.
    private sealed record Source(IBlockStore Store, byte[]? Lower, byte[]? Upper, int Depth);
}
