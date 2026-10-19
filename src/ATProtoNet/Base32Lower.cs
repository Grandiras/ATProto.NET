using System.Numerics;

namespace ATProtoNet;

// RFC 4648 base32, lower-case alphabet, unpadded, canonical only. It lives beside Cid, which needs it,
// so the root namespace does not depend on ATProtoNet.Data; ATProtoNet.Data.Base32 is its public face.
internal static class Base32Lower
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static int EncodedLength(int byteCount) => (int)(((long)byteCount * 8 + 4) / 5);

    public static int DecodedLength(int charCount) => (int)((long)charCount * 5 / 8);

    // Writes the base32 of data into destination, which holds EncodedLength(data.Length) characters.
    public static void Encode(ReadOnlySpan<byte> data, Span<char> destination)
    {
        int buffer = 0, bits = 0, at = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                destination[at++] = Alphabet[(buffer >> bits) & 0x1F];
            }
        }
        if (bits > 0) destination[at] = Alphabet[(buffer << (5 - bits)) & 0x1F];
    }

    // Decodes text into destination, which holds DecodedLength(text.Length) bytes. Only the canonical
    // encoding is accepted: alphabet characters only, no character left over, unused bits zero.
    public static bool TryDecode<TChar>(ReadOnlySpan<TChar> text, Span<byte> destination)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        int buffer = 0, bits = 0, at = 0;
        foreach (var unit in text)
        {
            var c = uint.CreateTruncating(unit);
            var value = c < 128 ? DecodeTable[(int)c] : 255;
            if (value > 31) return false;
            buffer = ((buffer << 5) | value) & 0xFFF;
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            destination[at++] = (byte)(buffer >> bits);
        }
        return bits < 5 && (buffer & ((1 << bits) - 1)) == 0;
    }

    private static ReadOnlySpan<byte> DecodeTable =>
    [
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 26, 27, 28, 29, 30, 31, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
        15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 255, 255, 255, 255, 255,
    ];
}
