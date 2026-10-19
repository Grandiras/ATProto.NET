namespace ATProtoNet.Data;

/// <summary>Base58 with the Bitcoin alphabet: the multibase <c>z</c> encoding of multikeys and <c>did:key</c>.</summary>
public static class Base58Btc
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>The base58btc text of <paramref name="data"/>; each leading zero byte becomes a <c>1</c>.</summary>
    /// <param name="data">The bytes to encode.</param>
    /// <returns>The base58btc text, without a multibase prefix.</returns>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        var zeros = data.IndexOfAnyExcept((byte)0);
        if (zeros < 0) return new('1', data.Length);
        // Digits little-endian: digits = digits * 256 + byte, one byte at a time.
        Span<byte> digits = data.Length <= 256 ? stackalloc byte[data.Length * 138 / 100 + 1] : new byte[data.Length * 138 / 100 + 1];
        var length = 0;
        foreach (var b in data[zeros..])
        {
            int carry = b;
            for (var i = 0; i < length; i++)
            {
                carry += digits[i] << 8;
                digits[i] = (byte)(carry % 58);
                carry /= 58;
            }
            for (; carry > 0; carry /= 58) digits[length++] = (byte)(carry % 58);
        }
        var chars = new char[zeros + length];
        chars.AsSpan(0, zeros).Fill('1');
        for (var i = 0; i < length; i++) chars[zeros + i] = Alphabet[digits[length - 1 - i]];
        return new(chars);
    }

    /// <summary>Decodes base58btc.</summary>
    /// <param name="text">Base58btc text, without a multibase prefix.</param>
    /// <returns>The decoded bytes, leading zero bytes preserved.</returns>
    /// <exception cref="FormatException">The text has a character outside the alphabet.</exception>
    public static byte[] Decode(ReadOnlySpan<char> text)
    {
        var buffer = new byte[text.Length];
        return TryDecode(text, buffer, out var written) ? buffer[..written] : throw new FormatException("The text is not base58btc.");
    }

    /// <summary>Decodes base58btc into <paramref name="destination"/> without throwing.</summary>
    /// <param name="text">Base58btc text, without a multibase prefix.</param>
    /// <param name="destination">The buffer to decode into.</param>
    /// <param name="bytesWritten">The number of bytes decoded.</param>
    /// <returns>Whether the text is base58btc and its value fits <paramref name="destination"/>.</returns>
    public static bool TryDecode(ReadOnlySpan<char> text, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        var zeros = text.IndexOfAnyExcept('1');
        if (zeros < 0) zeros = text.Length;
        if (zeros > destination.Length) return false;
        // The value little-endian after the zeros: value = value * 58 + digit, one character at a time.
        var value = destination[zeros..];
        var length = 0;
        foreach (var c in text[zeros..])
        {
            var carry = c < 128 ? Alphabet.IndexOf(c) : -1;
            if (carry < 0) return false;
            for (var i = 0; i < length; i++)
            {
                carry += value[i] * 58;
                value[i] = (byte)carry;
                carry >>= 8;
            }
            for (; carry > 0; carry >>= 8)
            {
                if (length == value.Length) return false;
                value[length++] = (byte)carry;
            }
        }
        value[..length].Reverse();
        destination[..zeros].Clear();
        bytesWritten = zeros + length;
        return true;
    }
}
