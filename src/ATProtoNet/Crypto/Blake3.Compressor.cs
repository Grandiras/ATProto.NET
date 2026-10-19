using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace ATProtoNet.Crypto;

// The BLAKE3 compression function, written once and generic over the lane type: one lane is the plain scalar
// compression, and 4 or 8 lanes (Vector128, Vector256) compress that many output blocks at once, which differ
// only in their counter. Every byte/word conversion is explicitly little-endian, so output does not depend on
// the host.
public static partial class Blake3
{
    private const uint Iv0 = 0x6A09E667, Iv1 = 0xBB67AE85, Iv2 = 0x3C6EF372, Iv3 = 0xA54FF53A;
    private const uint Iv4 = 0x510E527F, Iv5 = 0x9B05688C, Iv6 = 0x1F83D9AB, Iv7 = 0x5BE0CD19;

    private static void SetIv(ref ChainingValue cv)
    {
        cv[0] = Iv0; cv[1] = Iv1; cv[2] = Iv2; cv[3] = Iv3;
        cv[4] = Iv4; cv[5] = Iv5; cv[6] = Iv6; cv[7] = Iv7;
    }

    private static void LoadWords(ReadOnlySpan<byte> bytes, Span<uint> words)
    {
        for (var i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i * 4)..]);
    }

    private static ChainingValue CompressToChainingValue(in ChainingValue cv, in Words16<uint> block, ulong counter, uint blockLen, uint flags)
    {
        Compressor<Lanes1, uint>.Compress(in cv, in block, counter, blockLen, flags, out var words);
        var result = default(ChainingValue);
        for (var i = 0; i < 8; i++) result[i] = words[i];
        return result;
    }

    // The compression function over TLanes.Count blocks sharing every input but the counter, which is
    // counter, counter + 1, … in successive lanes. State and message live in locals so the JIT keeps them in registers.
    private static class Compressor<TLanes, T> where TLanes : ILanes<T> where T : struct
    {
        public static void Compress(in ChainingValue cv, in Words16<uint> block, ulong counter, uint blockLen, uint flags, out Words16<T> output)
        {
            T m0 = TLanes.Broadcast(block[0]), m1 = TLanes.Broadcast(block[1]), m2 = TLanes.Broadcast(block[2]), m3 = TLanes.Broadcast(block[3]);
            T m4 = TLanes.Broadcast(block[4]), m5 = TLanes.Broadcast(block[5]), m6 = TLanes.Broadcast(block[6]), m7 = TLanes.Broadcast(block[7]);
            T m8 = TLanes.Broadcast(block[8]), m9 = TLanes.Broadcast(block[9]), m10 = TLanes.Broadcast(block[10]), m11 = TLanes.Broadcast(block[11]);
            T m12 = TLanes.Broadcast(block[12]), m13 = TLanes.Broadcast(block[13]), m14 = TLanes.Broadcast(block[14]), m15 = TLanes.Broadcast(block[15]);

            T v0 = TLanes.Broadcast(cv[0]), v1 = TLanes.Broadcast(cv[1]), v2 = TLanes.Broadcast(cv[2]), v3 = TLanes.Broadcast(cv[3]);
            T v4 = TLanes.Broadcast(cv[4]), v5 = TLanes.Broadcast(cv[5]), v6 = TLanes.Broadcast(cv[6]), v7 = TLanes.Broadcast(cv[7]);
            T v8 = TLanes.Broadcast(Iv0), v9 = TLanes.Broadcast(Iv1), v10 = TLanes.Broadcast(Iv2), v11 = TLanes.Broadcast(Iv3);
            TLanes.Counters(counter, out var v12, out var v13);
            T v14 = TLanes.Broadcast(blockLen), v15 = TLanes.Broadcast(flags);

            // Seven rounds of column steps then diagonal steps, permuting the message words between rounds.
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
                if (round == 6) break;
                // The spec's MSG_PERMUTATION: 2, 6, 3, 10, 7, 0, 4, 13, 1, 11, 12, 5, 9, 14, 15, 8.
                (m0, m1, m2, m3, m4, m5, m6, m7, m8, m9, m10, m11, m12, m13, m14, m15) =
                    (m2, m6, m3, m10, m7, m0, m4, m13, m1, m11, m12, m5, m9, m14, m15, m8);
            }

            output = default;
            output[0] = TLanes.Xor(v0, v8); output[1] = TLanes.Xor(v1, v9); output[2] = TLanes.Xor(v2, v10); output[3] = TLanes.Xor(v3, v11);
            output[4] = TLanes.Xor(v4, v12); output[5] = TLanes.Xor(v5, v13); output[6] = TLanes.Xor(v6, v14); output[7] = TLanes.Xor(v7, v15);
            output[8] = TLanes.Xor(v8, TLanes.Broadcast(cv[0])); output[9] = TLanes.Xor(v9, TLanes.Broadcast(cv[1]));
            output[10] = TLanes.Xor(v10, TLanes.Broadcast(cv[2])); output[11] = TLanes.Xor(v11, TLanes.Broadcast(cv[3]));
            output[12] = TLanes.Xor(v12, TLanes.Broadcast(cv[4])); output[13] = TLanes.Xor(v13, TLanes.Broadcast(cv[5]));
            output[14] = TLanes.Xor(v14, TLanes.Broadcast(cv[6])); output[15] = TLanes.Xor(v15, TLanes.Broadcast(cv[7]));
        }

        // Writes each lane's 16 output words as one little-endian 64-byte block: lane i of every word is block i.
        public static void Store(in Words16<T> words, Span<byte> destination)
        {
            Span<uint> column = stackalloc uint[8];
            for (var w = 0; w < 16; w++)
            {
                TLanes.CopyTo(words[w], column);
                for (var lane = 0; lane < TLanes.Count; lane++)
                    BinaryPrimitives.WriteUInt32LittleEndian(destination[(lane * BlockLength + w * 4)..], column[lane]);
            }
        }
    }

    // The word arithmetic of the compression function, over one or more lanes.
    private interface ILanes<T> where T : struct
    {
        static abstract int Count { get; }

        static abstract T Broadcast(uint value);

        // Per-lane block counters counter + lane, split into low and high words.
        static abstract void Counters(ulong counter, out T low, out T high);

        static abstract T Xor(T left, T right);

        // The quarter-round mixing function.
        static abstract void G(ref T a, ref T b, ref T c, ref T d, T mx, T my);

        static abstract void CopyTo(T value, Span<uint> destination);
    }

    private readonly struct Lanes1 : ILanes<uint>
    {
        public static int Count => 1;

        public static uint Broadcast(uint value) => value;

        public static void Counters(ulong counter, out uint low, out uint high) => (low, high) = ((uint)counter, (uint)(counter >> 32));

        public static uint Xor(uint left, uint right) => left ^ right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void G(ref uint a, ref uint b, ref uint c, ref uint d, uint mx, uint my)
        {
            a = a + b + mx; d = BitOperations.RotateRight(d ^ a, 16);
            c += d; b = BitOperations.RotateRight(b ^ c, 12);
            a = a + b + my; d = BitOperations.RotateRight(d ^ a, 8);
            c += d; b = BitOperations.RotateRight(b ^ c, 7);
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
            // A lane whose low word wrapped carries into its high word: the mask is all-ones (-1) there.
            high = Vector128.Create((uint)(counter >> 32)) - Vector128.LessThan(low, first);
        }

        public static Vector128<uint> Xor(Vector128<uint> left, Vector128<uint> right) => left ^ right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void G(ref Vector128<uint> a, ref Vector128<uint> b, ref Vector128<uint> c, ref Vector128<uint> d, Vector128<uint> mx, Vector128<uint> my)
        {
            a = a + b + mx; d = Rotate(d ^ a, 16);
            c += d; b = Rotate(b ^ c, 12);
            a = a + b + my; d = Rotate(d ^ a, 8);
            c += d; b = Rotate(b ^ c, 7);
        }

        public static void CopyTo(Vector128<uint> value, Span<uint> destination) => value.CopyTo(destination);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<uint> Rotate(Vector128<uint> value, [ConstantExpected(Min = 1, Max = 31)] int count) =>
            Vector128.ShiftRightLogical(value, count) | Vector128.ShiftLeft(value, 32 - count);
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
        public static void G(ref Vector256<uint> a, ref Vector256<uint> b, ref Vector256<uint> c, ref Vector256<uint> d, Vector256<uint> mx, Vector256<uint> my)
        {
            a = a + b + mx; d = Rotate(d ^ a, 16);
            c += d; b = Rotate(b ^ c, 12);
            a = a + b + my; d = Rotate(d ^ a, 8);
            c += d; b = Rotate(b ^ c, 7);
        }

        public static void CopyTo(Vector256<uint> value, Span<uint> destination) => value.CopyTo(destination);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<uint> Rotate(Vector256<uint> value, [ConstantExpected(Min = 1, Max = 31)] int count) =>
            Vector256.ShiftRightLogical(value, count) | Vector256.ShiftLeft(value, 32 - count);
    }

    [InlineArray(8)]
    internal struct ChainingValue
    {
        private uint _element0;
    }

    [InlineArray(16)]
    private struct Words16<T>
    {
        private T _element0;
    }
}
