namespace ATProtoNet.Data;

/// <summary>RFC 4648 base32 with the lower-case alphabet <c>a–z2–7</c> and no padding: the multibase <c>b</c> encoding.</summary>
public static class Base32
{
    /// <summary>The base32 text of <paramref name="data"/>.</summary>
    /// <param name="data">The bytes to encode.</param>
    /// <returns>Unpadded lower-case base32.</returns>
    public static string Encode(ReadOnlySpan<byte> data) =>
        data.IsEmpty ? "" : string.Create(Base32Lower.EncodedLength(data.Length), data.ToArray(), static (chars, bytes) => Base32Lower.Encode(bytes, chars));

    /// <summary>Decodes canonical base32.</summary>
    /// <param name="text">Unpadded lower-case base32 whose unused bits are zero.</param>
    /// <returns>The decoded bytes.</returns>
    /// <exception cref="FormatException">The text is not canonical base32.</exception>
    public static byte[] Decode(ReadOnlySpan<char> text)
    {
        var bytes = new byte[Base32Lower.DecodedLength(text.Length)];
        return Base32Lower.TryDecode(text, bytes) ? bytes : throw new FormatException("The text is not canonical unpadded lower-case base32.");
    }

    /// <summary>Decodes canonical base32 into <paramref name="destination"/> without throwing.</summary>
    /// <param name="text">Unpadded lower-case base32 whose unused bits are zero.</param>
    /// <param name="destination">The buffer to decode into.</param>
    /// <param name="bytesWritten">The number of bytes decoded.</param>
    /// <returns>Whether the text is canonical base32 and fits <paramref name="destination"/>.</returns>
    public static bool TryDecode(ReadOnlySpan<char> text, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = Base32Lower.DecodedLength(text.Length);
        if (bytesWritten <= destination.Length && Base32Lower.TryDecode(text, destination)) return true;
        bytesWritten = 0;
        return false;
    }
}
