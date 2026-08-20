using System.Buffers.Binary;

namespace ATProtoNet.Crypto;

/// <summary>
/// BLAKE3 in extendable-output (XOF) mode.
/// </summary>
/// <remarks>
/// <para>Only the unkeyed hash mode is implemented, which is the only mode the AT Protocol
/// uses: permissioned-space <see cref="Spaces.LtHash"/> expands each set element to 2048 bytes
/// with BLAKE3 XOF. Keyed hashing and key derivation are deliberately absent.</para>
/// <para>This is a straightforward single-threaded implementation of the reference
/// construction — a chunk state feeding a chaining-value stack — with no SIMD. LtHash
/// elements are one short chunk each, so the vectorized paths would not be exercised.</para>
/// <para>Not a general-purpose hashing API, and not exposed as one: it exists so the SDK
/// carries no third-party cryptography dependency for the one primitive .NET does not ship.</para>
/// </remarks>
internal static class Blake3
{
    internal const int OutLen = 32;
    internal const int KeyLen = 32;
    internal const int BlockLen = 64;
    internal const int ChunkLen = 1024;

    private const uint ChunkStart = 1 << 0;
    private const uint ChunkEnd = 1 << 1;
    private const uint Parent = 1 << 2;
    private const uint Root = 1 << 3;

    private static ReadOnlySpan<uint> Iv =>
    [
        0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A,
        0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19,
    ];

    private static ReadOnlySpan<byte> MessagePermutation =>
    [
        2, 6, 3, 10, 7, 0, 4, 13, 1, 11, 12, 5, 9, 14, 15, 8,
    ];

    /// <summary>
    /// Hashes <paramref name="input"/> and fills <paramref name="output"/> with that many
    /// bytes of the extended output. A 32-byte <paramref name="output"/> is the standard digest.
    /// </summary>
    internal static void HashExtended(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var hasher = new Hasher();
        hasher.Update(input);
        hasher.Finalize(output);
    }

    /// <summary>Returns the standard 32-byte BLAKE3 digest of <paramref name="input"/>.</summary>
    internal static byte[] Hash(ReadOnlySpan<byte> input)
    {
        var output = new byte[OutLen];
        HashExtended(input, output);
        return output;
    }

    // ──────────────────────────────────────────────────────────
    //  Compression function
    // ──────────────────────────────────────────────────────────

    private static void Compress(
        ReadOnlySpan<uint> chainingValue,
        ReadOnlySpan<uint> blockWords,
        ulong counter,
        uint blockLen,
        uint flags,
        Span<uint> state)
    {
        state[0] = chainingValue[0];
        state[1] = chainingValue[1];
        state[2] = chainingValue[2];
        state[3] = chainingValue[3];
        state[4] = chainingValue[4];
        state[5] = chainingValue[5];
        state[6] = chainingValue[6];
        state[7] = chainingValue[7];
        state[8] = Iv[0];
        state[9] = Iv[1];
        state[10] = Iv[2];
        state[11] = Iv[3];
        state[12] = (uint)counter;
        state[13] = (uint)(counter >> 32);
        state[14] = blockLen;
        state[15] = flags;

        Span<uint> message = stackalloc uint[16];
        blockWords[..16].CopyTo(message);

        for (var round = 0; ; round++)
        {
            Round(state, message);
            if (round == 6)
                break;
            Permute(message);
        }

        for (var i = 0; i < 8; i++)
        {
            state[i] ^= state[i + 8];
            state[i + 8] ^= chainingValue[i];
        }
    }

    private static void Round(Span<uint> s, ReadOnlySpan<uint> m)
    {
        // Columns
        G(s, 0, 4, 8, 12, m[0], m[1]);
        G(s, 1, 5, 9, 13, m[2], m[3]);
        G(s, 2, 6, 10, 14, m[4], m[5]);
        G(s, 3, 7, 11, 15, m[6], m[7]);
        // Diagonals
        G(s, 0, 5, 10, 15, m[8], m[9]);
        G(s, 1, 6, 11, 12, m[10], m[11]);
        G(s, 2, 7, 8, 13, m[12], m[13]);
        G(s, 3, 4, 9, 14, m[14], m[15]);
    }

    private static void G(Span<uint> s, int a, int b, int c, int d, uint mx, uint my)
    {
        s[a] = s[a] + s[b] + mx;
        s[d] = uint.RotateRight(s[d] ^ s[a], 16);
        s[c] += s[d];
        s[b] = uint.RotateRight(s[b] ^ s[c], 12);
        s[a] = s[a] + s[b] + my;
        s[d] = uint.RotateRight(s[d] ^ s[a], 8);
        s[c] += s[d];
        s[b] = uint.RotateRight(s[b] ^ s[c], 7);
    }

    private static void Permute(Span<uint> message)
    {
        Span<uint> permuted = stackalloc uint[16];
        for (var i = 0; i < 16; i++)
            permuted[i] = message[MessagePermutation[i]];
        permuted.CopyTo(message);
    }

    private static void WordsFromBlock(ReadOnlySpan<byte> block, Span<uint> words)
    {
        for (var i = 0; i < 16; i++)
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(block[(i * 4)..]);
    }

    // ──────────────────────────────────────────────────────────
    //  Output — a chaining value plus the block that produced it
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// A node's compression inputs, held back so the node can be finalized either as an
    /// interior chaining value or, if it turns out to be the root, as extendable output.
    /// </summary>
    private struct Output
    {
        internal readonly uint[] InputChainingValue;
        internal readonly uint[] BlockWords;
        internal readonly ulong Counter;
        internal readonly uint BlockLen;
        internal readonly uint Flags;

        internal Output(uint[] inputChainingValue, uint[] blockWords, ulong counter, uint blockLen, uint flags)
        {
            InputChainingValue = inputChainingValue;
            BlockWords = blockWords;
            Counter = counter;
            BlockLen = blockLen;
            Flags = flags;
        }

        internal readonly void ChainingValue(Span<uint> destination)
        {
            Span<uint> state = stackalloc uint[16];
            Compress(InputChainingValue, BlockWords, Counter, BlockLen, Flags, state);
            state[..8].CopyTo(destination);
        }

        /// <summary>
        /// Fills <paramref name="output"/> from the root node. Each 64-byte output block is a
        /// fresh compression at an incrementing counter, which is what makes BLAKE3 a XOF.
        /// </summary>
        internal readonly void RootBytes(Span<byte> output)
        {
            Span<uint> state = stackalloc uint[16];
            Span<byte> wordBytes = stackalloc byte[4];
            ulong outputBlockCounter = 0;
            var written = 0;

            while (written < output.Length)
            {
                Compress(InputChainingValue, BlockWords, outputBlockCounter, BlockLen, Flags | Root, state);

                for (var i = 0; i < 16 && written < output.Length; i++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(wordBytes, state[i]);
                    var take = Math.Min(4, output.Length - written);
                    wordBytes[..take].CopyTo(output[written..]);
                    written += take;
                }

                outputBlockCounter++;
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Chunk state
    // ──────────────────────────────────────────────────────────

    private struct ChunkState
    {
        internal uint[] ChainingValue;
        internal ulong ChunkCounter;
        private readonly byte[] _block;
        private int _blockLen;
        private int _blocksCompressed;
        private readonly uint _flags;

        internal ChunkState(ReadOnlySpan<uint> key, ulong chunkCounter, uint flags)
        {
            ChainingValue = key.ToArray();
            ChunkCounter = chunkCounter;
            _block = new byte[BlockLen];
            _blockLen = 0;
            _blocksCompressed = 0;
            _flags = flags;
        }

        internal readonly int Length => (BlockLen * _blocksCompressed) + _blockLen;

        private readonly uint StartFlag => _blocksCompressed == 0 ? ChunkStart : 0;

        internal void Update(ReadOnlySpan<byte> input)
        {
            Span<uint> blockWords = stackalloc uint[16];
            Span<uint> state = stackalloc uint[16];

            while (!input.IsEmpty)
            {
                // A full block is only compressed once more input is known to follow it,
                // so the final block of a chunk is still available to Finalize as an Output.
                if (_blockLen == BlockLen)
                {
                    WordsFromBlock(_block, blockWords);
                    Compress(ChainingValue, blockWords, ChunkCounter, BlockLen, _flags | StartFlag, state);
                    state[..8].CopyTo(ChainingValue);
                    _blocksCompressed++;
                    _blockLen = 0;
                    _block.AsSpan().Clear();
                }

                var take = Math.Min(BlockLen - _blockLen, input.Length);
                input[..take].CopyTo(_block.AsSpan(_blockLen));
                _blockLen += take;
                input = input[take..];
            }
        }

        internal readonly Output Finalize()
        {
            var blockWords = new uint[16];
            WordsFromBlock(_block, blockWords);
            return new Output(
                ChainingValue,
                blockWords,
                ChunkCounter,
                (uint)_blockLen,
                _flags | StartFlag | ChunkEnd);
        }
    }

    private static Output ParentOutput(
        ReadOnlySpan<uint> leftChildCv,
        ReadOnlySpan<uint> rightChildCv,
        ReadOnlySpan<uint> key,
        uint flags)
    {
        var blockWords = new uint[16];
        leftChildCv[..8].CopyTo(blockWords);
        rightChildCv[..8].CopyTo(blockWords.AsSpan(8));
        return new Output(key.ToArray(), blockWords, 0, BlockLen, flags | Parent);
    }

    // ──────────────────────────────────────────────────────────
    //  Hasher
    // ──────────────────────────────────────────────────────────

    /// <summary>Incremental BLAKE3 state. Not thread-safe.</summary>
    private struct Hasher
    {
        private ChunkState _chunkState;
        private readonly uint[] _key;
        // One entry per set bit of the completed-chunk count: the subtree stack.
        private readonly uint[][] _cvStack;
        private int _cvStackLen;
        private readonly uint _flags;

        public Hasher()
        {
            _key = Iv.ToArray();
            _flags = 0;
            _chunkState = new ChunkState(_key, 0, _flags);
            _cvStack = new uint[54][];
            _cvStackLen = 0;
        }

        private void PushStack(uint[] cv) => _cvStack[_cvStackLen++] = cv;

        private uint[] PopStack() => _cvStack[--_cvStackLen];

        /// <summary>
        /// Merges completed subtrees. The number of merges is determined by the count of
        /// completed chunks: a subtree is merged exactly when its sibling completes, which
        /// is the count's trailing-zero pattern.
        /// </summary>
        private void AddChunkChainingValue(uint[] newCv, ulong totalChunks)
        {
            while ((totalChunks & 1) == 0)
            {
                var left = PopStack();
                var merged = new uint[8];
                ParentOutput(left, newCv, _key, _flags).ChainingValue(merged);
                newCv = merged;
                totalChunks >>= 1;
            }
            PushStack(newCv);
        }

        internal void Update(ReadOnlySpan<byte> input)
        {
            while (!input.IsEmpty)
            {
                if (_chunkState.Length == ChunkLen)
                {
                    var chunkCv = new uint[8];
                    _chunkState.Finalize().ChainingValue(chunkCv);
                    var totalChunks = _chunkState.ChunkCounter + 1;
                    AddChunkChainingValue(chunkCv, totalChunks);
                    _chunkState = new ChunkState(_key, totalChunks, _flags);
                }

                var want = ChunkLen - _chunkState.Length;
                var take = Math.Min(want, input.Length);
                _chunkState.Update(input[..take]);
                input = input[take..];
            }
        }

        internal readonly void Finalize(Span<byte> output)
        {
            var current = _chunkState.Finalize();
            var cv = new uint[8];

            // Fold the stack right-to-left: everything still on it is a left sibling.
            for (var i = _cvStackLen - 1; i >= 0; i--)
            {
                current.ChainingValue(cv);
                current = ParentOutput(_cvStack[i], cv, _key, _flags);
            }

            current.RootBytes(output);
        }
    }
}
