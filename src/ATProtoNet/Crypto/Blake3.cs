using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace ATProtoNet.Crypto;

/// <summary>
/// BLAKE3 in extendable-output (XOF) mode.
/// </summary>
/// <remarks>
/// <para>Only the unkeyed hash mode is implemented, which is the only mode the AT Protocol
/// uses: permissioned-space <see cref="Spaces.LtHash"/> expands each set element to 2048 bytes
/// with BLAKE3 XOF. Keyed hashing and key derivation are deliberately absent.</para>
/// <para>The input side is the reference construction — a chunk state feeding a
/// chaining-value stack — computed one block at a time. The output side is where LtHash spends
/// its time (32 output blocks per element), and XOF output blocks differ only in their counter,
/// so they are compressed 8 at a time with <see cref="Vector256{T}"/> (or 4 with
/// <see cref="Vector128{T}"/>), one block per vector lane. The compression function is written
/// once, generic over the lane type, so every path runs the same rounds. Hosts without
/// hardware vectors take the scalar path, and every byte/word conversion is explicitly
/// little-endian, so the output does not depend on the host.</para>
/// <para>Not a general-purpose hashing API, and not exposed as one: it exists so the SDK
/// carries no third-party cryptography dependency for the one primitive .NET does not ship.
/// Nothing is allocated on the heap.</para>
/// </remarks>
internal static class Blake3
{
    internal const int OutLen = 32;
    internal const int BlockLen = 64;
    internal const int ChunkLen = 1024;

    // One chaining value per tree level. 54 levels cover the spec's 2^64-byte input limit,
    // which is far more than a span can hold.
    private const int MaxDepth = 54;

    private const uint ChunkStart = 1 << 0;
    private const uint ChunkEnd = 1 << 1;
    private const uint Parent = 1 << 2;
    private const uint Root = 1 << 3;

    private const uint Iv0 = 0x6A09E667, Iv1 = 0xBB67AE85, Iv2 = 0x3C6EF372, Iv3 = 0xA54FF53A;
    private const uint Iv4 = 0x510E527F, Iv5 = 0x9B05688C, Iv6 = 0x1F83D9AB, Iv7 = 0x5BE0CD19;

    /// <summary>The widest output-block batch the host computes in hardware: 8, 4 or 1.</summary>
    internal static int MaxParallelism =>
        Vector256.IsHardwareAccelerated ? 8 : Vector128.IsHardwareAccelerated ? 4 : 1;

    /// <summary>
    /// Hashes <paramref name="input"/> and fills <paramref name="output"/> with that many
    /// bytes of the extended output. A 32-byte <paramref name="output"/> is the standard digest.
    /// </summary>
    internal static void HashExtended(ReadOnlySpan<byte> input, Span<byte> output)
        => HashExtended(input, output, MaxParallelism);

    /// <summary>
    /// <see cref="HashExtended(ReadOnlySpan{byte}, Span{byte})"/> with the output batch width
    /// capped at <paramref name="parallelism"/> (1, 4 or 8), so each path can be tested on any
    /// host. The result does not depend on it.
    /// </summary>
    internal static void HashExtended(ReadOnlySpan<byte> input, Span<byte> output, int parallelism)
    {
        if (parallelism is not (1 or 4 or 8))
            throw new ArgumentOutOfRangeException(nameof(parallelism), parallelism, "Expected 1, 4 or 8.");

        var root = input.Length <= ChunkLen ? ChunkNode(input, 0) : TreeRoot(input);
        Squeeze(in root, output, parallelism);
    }

    /// <summary>Returns the standard 32-byte BLAKE3 digest of <paramref name="input"/>.</summary>
    internal static byte[] Hash(ReadOnlySpan<byte> input)
    {
        var output = new byte[OutLen];
        HashExtended(input, output);
        return output;
    }

    // ──────────────────────────────────────────────────────────
    //  Tree
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// A node's compression inputs, held back so the node can be finalized either as an
    /// interior chaining value or, if it turns out to be the root, as extendable output.
    /// </summary>
    private struct Node
    {
        public ChainingValue InputCv;
        public Words16<uint> Block;
        public ulong Counter;
        public uint BlockLen;
        public uint Flags;
    }

    /// <summary>
    /// Compresses every block of a chunk but the last, which stays in the returned node: only
    /// it carries <see cref="ChunkEnd"/>, and <see cref="Root"/> too if the chunk is the root.
    /// </summary>
    private static Node ChunkNode(ReadOnlySpan<byte> chunk, ulong chunkCounter)
    {
        Debug.Assert(chunk.Length <= ChunkLen);

        var node = new Node { Counter = chunkCounter, Flags = ChunkStart };
        SetIv(ref node.InputCv);

        while (chunk.Length > BlockLen)
        {
            LoadBlock(chunk[..BlockLen], out var block);
            node.InputCv = CompressToChainingValue(in node.InputCv, in block, chunkCounter, BlockLen, node.Flags);
            node.Flags = 0;
            chunk = chunk[BlockLen..];
        }

        // An empty input still has one (empty) block.
        LoadBlock(chunk, out node.Block);
        node.BlockLen = (uint)chunk.Length;
        node.Flags |= ChunkEnd;
        return node;
    }

    private static Node ParentNode(in ChainingValue left, in ChainingValue right)
    {
        var node = new Node { Counter = 0, BlockLen = BlockLen, Flags = Parent };
        SetIv(ref node.InputCv);
        for (var i = 0; i < 8; i++)
        {
            node.Block[i] = left[i];
            node.Block[i + 8] = right[i];
        }
        return node;
    }

    private static ChainingValue ChainingValueOf(in Node node)
        => CompressToChainingValue(in node.InputCv, in node.Block, node.Counter, node.BlockLen, node.Flags);

    /// <summary>Builds the chunk tree over an input of more than one chunk and returns its root.</summary>
    private static Node TreeRoot(ReadOnlySpan<byte> input)
    {
        Debug.Assert(input.Length > ChunkLen);

        var stack = default(ChainingValueStack);
        var depth = 0;
        ulong chunkCounter = 0;

        // A chunk is only reduced to a chaining value once more input is known to follow it,
        // so the last chunk is still available to become the root.
        while (input.Length > ChunkLen)
        {
            var chunk = ChunkNode(input[..ChunkLen], chunkCounter);
            var cv = ChainingValueOf(in chunk);
            input = input[ChunkLen..];
            chunkCounter++;

            // A subtree is merged exactly when its sibling completes, which is the
            // trailing-zero pattern of the completed-chunk count.
            for (var total = chunkCounter; (total & 1) == 0; total >>= 1)
            {
                var parent = ParentNode(in stack[--depth], in cv);
                cv = ChainingValueOf(in parent);
            }

            stack[depth++] = cv;
        }

        // Fold the stack right-to-left: everything still on it is a left sibling.
        var root = ChunkNode(input, chunkCounter);
        while (depth > 0)
        {
            var right = ChainingValueOf(in root);
            root = ParentNode(in stack[--depth], in right);
        }

        return root;
    }

    /// <summary>
    /// Fills <paramref name="output"/> from the root node. Each 64-byte output block is a
    /// fresh compression at an incrementing counter, which is what makes BLAKE3 a XOF, and
    /// what lets several blocks be computed side by side.
    /// </summary>
    private static void Squeeze(in Node root, Span<byte> output, int parallelism)
    {
        var flags = root.Flags | Root;
        ulong counter = 0;

        if (parallelism >= 8)
        {
            while (output.Length >= 8 * BlockLen)
            {
                Compressor<Lanes8, Vector256<uint>>.Compress(
                    in root.InputCv, in root.Block, counter, root.BlockLen, flags, out var words);
                Compressor<Lanes8, Vector256<uint>>.Store(in words, output[..(8 * BlockLen)]);
                output = output[(8 * BlockLen)..];
                counter += 8;
            }
        }

        if (parallelism >= 4)
        {
            while (output.Length >= 4 * BlockLen)
            {
                Compressor<Lanes4, Vector128<uint>>.Compress(
                    in root.InputCv, in root.Block, counter, root.BlockLen, flags, out var words);
                Compressor<Lanes4, Vector128<uint>>.Store(in words, output[..(4 * BlockLen)]);
                output = output[(4 * BlockLen)..];
                counter += 4;
            }
        }

        while (output.Length >= BlockLen)
        {
            Compressor<Lanes1, uint>.Compress(
                in root.InputCv, in root.Block, counter, root.BlockLen, flags, out var words);
            Compressor<Lanes1, uint>.Store(in words, output[..BlockLen]);
            output = output[BlockLen..];
            counter++;
        }

        if (!output.IsEmpty)
        {
            Compressor<Lanes1, uint>.Compress(
                in root.InputCv, in root.Block, counter, root.BlockLen, flags, out var words);
            Span<byte> last = stackalloc byte[BlockLen];
            Compressor<Lanes1, uint>.Store(in words, last);
            last[..output.Length].CopyTo(output);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Compression function
    // ──────────────────────────────────────────────────────────

    private static ChainingValue CompressToChainingValue(
        in ChainingValue cv, in Words16<uint> block, ulong counter, uint blockLen, uint flags)
    {
        Compressor<Lanes1, uint>.Compress(in cv, in block, counter, blockLen, flags, out var words);

        var result = default(ChainingValue);
        for (var i = 0; i < 8; i++)
            result[i] = words[i];
        return result;
    }

    private static void SetIv(ref ChainingValue cv)
    {
        cv[0] = Iv0; cv[1] = Iv1; cv[2] = Iv2; cv[3] = Iv3;
        cv[4] = Iv4; cv[5] = Iv5; cv[6] = Iv6; cv[7] = Iv7;
    }

    /// <summary>Reads up to one block as little-endian words, zero-padding a short final block.</summary>
    private static void LoadBlock(ReadOnlySpan<byte> bytes, out Words16<uint> words)
    {
        Debug.Assert(bytes.Length <= BlockLen);

        if (bytes.Length < BlockLen)
        {
            Span<byte> padded = stackalloc byte[BlockLen];
            bytes.CopyTo(padded);
            LoadFullBlock(padded, out words);
        }
        else
        {
            LoadFullBlock(bytes, out words);
        }
    }

    private static void LoadFullBlock(ReadOnlySpan<byte> bytes, out Words16<uint> words)
    {
        words = default;
        for (var i = 0; i < 16; i++)
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(i * 4, 4));
    }

    /// <summary>
    /// The compression function over <typeparamref name="TLanes"/>.Count blocks that share
    /// every input except the counter, which is <c>counter</c>, <c>counter + 1</c>, … in
    /// successive lanes. With one lane it is the plain scalar compression.
    /// </summary>
    /// <remarks>
    /// The state and message live in locals rather than buffers, so that the JIT can keep
    /// them in registers.
    /// </remarks>
    private static class Compressor<TLanes, T>
        where TLanes : ILanes<T>
        where T : struct
    {
        /// <summary>Compresses and returns all 16 output words: the first 8 are the chaining value.</summary>
        public static void Compress(
            in ChainingValue cv, in Words16<uint> block, ulong counter, uint blockLen, uint flags,
            out Words16<T> output)
        {
            T m0 = TLanes.Broadcast(block[0]), m1 = TLanes.Broadcast(block[1]);
            T m2 = TLanes.Broadcast(block[2]), m3 = TLanes.Broadcast(block[3]);
            T m4 = TLanes.Broadcast(block[4]), m5 = TLanes.Broadcast(block[5]);
            T m6 = TLanes.Broadcast(block[6]), m7 = TLanes.Broadcast(block[7]);
            T m8 = TLanes.Broadcast(block[8]), m9 = TLanes.Broadcast(block[9]);
            T m10 = TLanes.Broadcast(block[10]), m11 = TLanes.Broadcast(block[11]);
            T m12 = TLanes.Broadcast(block[12]), m13 = TLanes.Broadcast(block[13]);
            T m14 = TLanes.Broadcast(block[14]), m15 = TLanes.Broadcast(block[15]);

            T v0 = TLanes.Broadcast(cv[0]), v1 = TLanes.Broadcast(cv[1]);
            T v2 = TLanes.Broadcast(cv[2]), v3 = TLanes.Broadcast(cv[3]);
            T v4 = TLanes.Broadcast(cv[4]), v5 = TLanes.Broadcast(cv[5]);
            T v6 = TLanes.Broadcast(cv[6]), v7 = TLanes.Broadcast(cv[7]);
            T v8 = TLanes.Broadcast(Iv0), v9 = TLanes.Broadcast(Iv1);
            T v10 = TLanes.Broadcast(Iv2), v11 = TLanes.Broadcast(Iv3);
            TLanes.Counters(counter, out var v12, out var v13);
            T v14 = TLanes.Broadcast(blockLen), v15 = TLanes.Broadcast(flags);

            // Seven rounds of column steps then diagonal steps, permuting the message words
            // between rounds.
            for (var round = 0; ; round++)
            {
                TLanes.G(ref v0, ref v4, ref v8, ref v12, m0, m1);
                TLanes.G(ref v1, ref v5, ref v9, ref v13, m2, m3);
                TLanes.G(ref v2, ref v6, ref v10, ref v14, m4, m5);
                TLanes.G(ref v3, ref v7, ref v11, ref v15, m6, m7);
                TLanes.G(ref v0, ref v5, ref v10, ref v15, m8, m9);
                TLanes.G(ref v1, ref v6, ref v11, ref v12, m10, m11);
                TLanes.G(ref v2, ref v7, ref v8, ref v13, m12, m13);
                TLanes.G(ref v3, ref v4, ref v9, ref v14, m14, m15);

                if (round == 6)
                    break;

                // The spec's MSG_PERMUTATION: 2, 6, 3, 10, 7, 0, 4, 13, 1, 11, 12, 5, 9, 14, 15, 8.
                (m0, m1, m2, m3, m4, m5, m6, m7, m8, m9, m10, m11, m12, m13, m14, m15) =
                    (m2, m6, m3, m10, m7, m0, m4, m13, m1, m11, m12, m5, m9, m14, m15, m8);
            }

            output = default;
            output[0] = TLanes.Xor(v0, v8);
            output[1] = TLanes.Xor(v1, v9);
            output[2] = TLanes.Xor(v2, v10);
            output[3] = TLanes.Xor(v3, v11);
            output[4] = TLanes.Xor(v4, v12);
            output[5] = TLanes.Xor(v5, v13);
            output[6] = TLanes.Xor(v6, v14);
            output[7] = TLanes.Xor(v7, v15);
            output[8] = TLanes.Xor(v8, TLanes.Broadcast(cv[0]));
            output[9] = TLanes.Xor(v9, TLanes.Broadcast(cv[1]));
            output[10] = TLanes.Xor(v10, TLanes.Broadcast(cv[2]));
            output[11] = TLanes.Xor(v11, TLanes.Broadcast(cv[3]));
            output[12] = TLanes.Xor(v12, TLanes.Broadcast(cv[4]));
            output[13] = TLanes.Xor(v13, TLanes.Broadcast(cv[5]));
            output[14] = TLanes.Xor(v14, TLanes.Broadcast(cv[6]));
            output[15] = TLanes.Xor(v15, TLanes.Broadcast(cv[7]));
        }

        /// <summary>
        /// Writes each lane's 16 output words as one little-endian 64-byte block. Lane
        /// <c>i</c> of every word belongs to block <c>i</c>, so this is a transpose.
        /// </summary>
        public static void Store(in Words16<T> words, Span<byte> destination)
        {
            Span<uint> column = stackalloc uint[8];
            for (var w = 0; w < 16; w++)
            {
                TLanes.CopyTo(words[w], column);
                for (var lane = 0; lane < TLanes.Count; lane++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        destination.Slice((lane * BlockLen) + (w * 4), 4), column[lane]);
                }
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Lane types
    // ──────────────────────────────────────────────────────────

    /// <summary>The word arithmetic of the compression function, over one or more lanes.</summary>
    private interface ILanes<T> where T : struct
    {
        static abstract int Count { get; }

        static abstract T Broadcast(uint value);

        /// <summary>Per-lane block counters <c>counter + lane</c>, split into low and high words.</summary>
        static abstract void Counters(ulong counter, out T low, out T high);

        static abstract T Xor(T left, T right);

        /// <summary>The quarter-round mixing function.</summary>
        static abstract void G(ref T a, ref T b, ref T c, ref T d, T mx, T my);

        static abstract void CopyTo(T value, Span<uint> destination);
    }

    private readonly struct Lanes1 : ILanes<uint>
    {
        public static int Count => 1;

        public static uint Broadcast(uint value) => value;

        public static void Counters(ulong counter, out uint low, out uint high)
        {
            low = (uint)counter;
            high = (uint)(counter >> 32);
        }

        public static uint Xor(uint left, uint right) => left ^ right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void G(ref uint a, ref uint b, ref uint c, ref uint d, uint mx, uint my)
        {
            a = a + b + mx;
            d = BitOperations.RotateRight(d ^ a, 16);
            c += d;
            b = BitOperations.RotateRight(b ^ c, 12);
            a = a + b + my;
            d = BitOperations.RotateRight(d ^ a, 8);
            c += d;
            b = BitOperations.RotateRight(b ^ c, 7);
        }

        public static void CopyTo(uint value, Span<uint> destination) => destination[0] = value;
    }

    private readonly struct Lanes4 : ILanes<Vector128<uint>>
    {
        public static int Count => 4;

        public static Vector128<uint> Broadcast(uint value) => Vector128.Create(value);

        public static void Counters(ulong counter, out Vector128<uint> low, out Vector128<uint> high)
        {
            var first = Vector128.Create((uint)counter);
            low = first + Vector128.Create(0u, 1, 2, 3);
            // A lane whose low word wrapped carries into its high word. The mask is all-ones
            // (-1) in exactly those lanes, so subtracting it adds the carry.
            high = Vector128.Create((uint)(counter >> 32)) - Vector128.LessThan(low, first);
        }

        public static Vector128<uint> Xor(Vector128<uint> left, Vector128<uint> right) => left ^ right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void G(
            ref Vector128<uint> a, ref Vector128<uint> b, ref Vector128<uint> c, ref Vector128<uint> d,
            Vector128<uint> mx, Vector128<uint> my)
        {
            a = a + b + mx;
            d = RotateRight(d ^ a, 16);
            c += d;
            b = RotateRight(b ^ c, 12);
            a = a + b + my;
            d = RotateRight(d ^ a, 8);
            c += d;
            b = RotateRight(b ^ c, 7);
        }

        public static void CopyTo(Vector128<uint> value, Span<uint> destination) => value.CopyTo(destination);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<uint> RotateRight(Vector128<uint> value, [ConstantExpected(Min = 1, Max = 31)] int count)
            => Vector128.ShiftRightLogical(value, count) | Vector128.ShiftLeft(value, 32 - count);
    }

    private readonly struct Lanes8 : ILanes<Vector256<uint>>
    {
        public static int Count => 8;

        public static Vector256<uint> Broadcast(uint value) => Vector256.Create(value);

        public static void Counters(ulong counter, out Vector256<uint> low, out Vector256<uint> high)
        {
            var first = Vector256.Create((uint)counter);
            low = first + Vector256.Create(0u, 1, 2, 3, 4, 5, 6, 7);
            high = Vector256.Create((uint)(counter >> 32)) - Vector256.LessThan(low, first);
        }

        public static Vector256<uint> Xor(Vector256<uint> left, Vector256<uint> right) => left ^ right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void G(
            ref Vector256<uint> a, ref Vector256<uint> b, ref Vector256<uint> c, ref Vector256<uint> d,
            Vector256<uint> mx, Vector256<uint> my)
        {
            a = a + b + mx;
            d = RotateRight(d ^ a, 16);
            c += d;
            b = RotateRight(b ^ c, 12);
            a = a + b + my;
            d = RotateRight(d ^ a, 8);
            c += d;
            b = RotateRight(b ^ c, 7);
        }

        public static void CopyTo(Vector256<uint> value, Span<uint> destination) => value.CopyTo(destination);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<uint> RotateRight(Vector256<uint> value, [ConstantExpected(Min = 1, Max = 31)] int count)
            => Vector256.ShiftRightLogical(value, count) | Vector256.ShiftLeft(value, 32 - count);
    }

    // ──────────────────────────────────────────────────────────
    //  Fixed-size buffers (stack-only, bounds-checked)
    // ──────────────────────────────────────────────────────────

    [InlineArray(8)]
    private struct ChainingValue
    {
        private uint _element0;
    }

    [InlineArray(16)]
    private struct Words16<T>
    {
        private T _element0;
    }

    [InlineArray(MaxDepth)]
    private struct ChainingValueStack
    {
        private ChainingValue _element0;
    }
}
