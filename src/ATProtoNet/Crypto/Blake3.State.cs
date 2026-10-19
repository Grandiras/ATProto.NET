using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace ATProtoNet.Crypto;

public static partial class Blake3
{
    private const int ChunkLength = 1024;

    // One chaining value per tree level: 54 levels cover the spec's 2^64-byte input limit.
    private const int MaxDepth = 54;

    private const uint ChunkStart = 1 << 0, ChunkEnd = 1 << 1, Parent = 1 << 2, Root = 1 << 3, KeyedHash = 1 << 4;

    // The reference incremental construction: a chunk state feeding a stack of chaining values. A chunk (and
    // a block in it) is only compressed once more input is known to follow, so the last one can still become
    // the root. Stack-only in one-shot hashing, so nothing is allocated.
    internal struct State
    {
        private readonly uint _flags;
        private ChainingValue _key, _cv;
        private Block _block;
        private CvStack _stack;
        private int _blockLength, _blocksCompressed, _depth;
        private ulong _chunkCounter;

        // An empty key is the unkeyed hash.
        public State(ReadOnlySpan<byte> key)
        {
            if (key.IsEmpty) SetIv(ref _key);
            else LoadWords(key, _key);
            _flags = key.IsEmpty ? 0 : KeyedHash;
            _cv = _key;
        }

        private readonly uint StartFlag => _blocksCompressed == 0 ? ChunkStart : 0;

        public void Append(ReadOnlySpan<byte> input)
        {
            while (!input.IsEmpty)
            {
                if (_blocksCompressed * BlockLength + _blockLength == ChunkLength)
                {
                    PushChunk(ChainingValueOf(ChunkNode()), ++_chunkCounter);
                    (_cv, _blocksCompressed, _blockLength) = (_key, 0, 0);
                    ((Span<byte>)_block).Clear();
                }
                if (_blockLength == BlockLength)
                {
                    LoadBlock(_block, out var words);
                    _cv = CompressToChainingValue(in _cv, in words, _chunkCounter, BlockLength, _flags | StartFlag);
                    (_blocksCompressed, _blockLength) = (_blocksCompressed + 1, 0);
                    ((Span<byte>)_block).Clear();
                }
                var take = Math.Min(BlockLength - _blockLength, input.Length);
                input[..take].CopyTo(((Span<byte>)_block)[_blockLength..]);
                _blockLength += take;
                input = input[take..];
            }
        }

        // Fills output with the extended output of everything appended so far; the state is unchanged.
        public readonly void Squeeze(Span<byte> output)
        {
            var node = ChunkNode();
            for (var i = _depth - 1; i >= 0; i--) node = ParentNode(in _stack[i], ChainingValueOf(in node));
            Output(in node, output);
        }

        // A subtree is merged exactly when its sibling completes: the trailing zeros of the chunk count.
        private void PushChunk(ChainingValue cv, ulong totalChunks)
        {
            for (; (totalChunks & 1) == 0; totalChunks >>= 1) cv = ChainingValueOf(ParentNode(in _stack[--_depth], in cv));
            _stack[_depth++] = cv;
        }

        private readonly Node ChunkNode()
        {
            var node = new Node { InputCv = _cv, Counter = _chunkCounter, BlockLen = (uint)_blockLength, Flags = _flags | StartFlag | ChunkEnd };
            LoadBlock(_block, out node.Block);
            return node;
        }

        private readonly Node ParentNode(in ChainingValue left, in ChainingValue right)
        {
            var node = new Node { InputCv = _key, BlockLen = BlockLength, Flags = _flags | Parent };
            for (var i = 0; i < 8; i++) (node.Block[i], node.Block[i + 8]) = (left[i], right[i]);
            return node;
        }

        private static void LoadBlock(ReadOnlySpan<byte> bytes, out Words16<uint> words)
        {
            words = default;
            LoadWords(bytes, words);
        }
    }

    // A node's compression inputs, held back so the node can become a chaining value or, as the root, output.
    private struct Node
    {
        public ChainingValue InputCv;
        public Words16<uint> Block;
        public ulong Counter;
        public uint BlockLen;
        public uint Flags;
    }

    private static ChainingValue ChainingValueOf(in Node node) =>
        CompressToChainingValue(in node.InputCv, in node.Block, node.Counter, node.BlockLen, node.Flags);

    // Each 64-byte output block is a fresh compression of the root at an incrementing counter, which is what
    // makes BLAKE3 a XOF and what lets several blocks be computed side by side.
    private static void Output(in Node root, Span<byte> output)
    {
        var flags = root.Flags | Root;
        ulong counter = 0;
        if (Vector256.IsHardwareAccelerated)
            for (; output.Length >= 8 * BlockLength; output = output[(8 * BlockLength)..], counter += 8)
            {
                Compressor<Lanes8, Vector256<uint>>.Compress(in root.InputCv, in root.Block, counter, root.BlockLen, flags, out var words);
                Compressor<Lanes8, Vector256<uint>>.Store(in words, output);
            }
        if (Vector128.IsHardwareAccelerated)
            for (; output.Length >= 4 * BlockLength; output = output[(4 * BlockLength)..], counter += 4)
            {
                Compressor<Lanes4, Vector128<uint>>.Compress(in root.InputCv, in root.Block, counter, root.BlockLen, flags, out var words);
                Compressor<Lanes4, Vector128<uint>>.Store(in words, output);
            }
        Span<byte> last = stackalloc byte[BlockLength];
        for (; !output.IsEmpty; counter++)
        {
            Compressor<Lanes1, uint>.Compress(in root.InputCv, in root.Block, counter, root.BlockLen, flags, out var words);
            if (output.Length >= BlockLength)
            {
                Compressor<Lanes1, uint>.Store(in words, output);
                output = output[BlockLength..];
                continue;
            }
            Compressor<Lanes1, uint>.Store(in words, last);
            last[..output.Length].CopyTo(output);
            output = [];
        }
    }

    [InlineArray(BlockLength)]
    private struct Block
    {
        private byte _element0;
    }

    [InlineArray(MaxDepth)]
    private struct CvStack
    {
        private ChainingValue _element0;
    }
}
