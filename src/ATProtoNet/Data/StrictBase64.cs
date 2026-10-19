using System.Numerics;

namespace ATProtoNet.Data;

// RFC 4648 base64 (§4) and base64url (§5), strictly: the alphabet only (no whitespace), padding only when
// allowed and then complete, zero spare bits. A value has one spelling apart from optional padding, which
// matters wherever the text is hashed or signed.
internal static class StrictBase64
{
    public static bool TryDecode<TChar>(ReadOnlySpan<TChar> text, bool url, bool allowPadding, out byte[] bytes)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        bytes = [];
        var length = text.Length;
        while (length > 0 && uint.CreateTruncating(text[length - 1]) == '=') length--;
        if (length < text.Length && (!allowPadding || text.Length % 4 != 0 || text.Length - length > 2) || length % 4 == 1) return false;
        var result = new byte[length * 3 / 4];
        int buffer = 0, bits = 0, at = 0;
        foreach (var unit in text[..length])
        {
            var value = Value(uint.CreateTruncating(unit), url);
            if (value < 0) return false;
            buffer = (buffer << 6 | value) & 0xFFFF;
            bits += 6;
            if (bits < 8) continue;
            bits -= 8;
            result[at++] = (byte)(buffer >> bits);
        }
        if ((buffer & ((1 << bits) - 1)) != 0) return false;
        bytes = result;
        return true;
    }

    private static int Value(uint c, bool url) => c switch
    {
        >= 'A' and <= 'Z' => (int)(c - 'A'),
        >= 'a' and <= 'z' => (int)(c - 'a' + 26),
        >= '0' and <= '9' => (int)(c - '0' + 52),
        '+' when !url => 62,
        '/' when !url => 63,
        '-' when url => 62,
        '_' when url => 63,
        _ => -1,
    };
}
