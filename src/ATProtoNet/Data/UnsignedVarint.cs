using System.Buffers;

namespace ATProtoNet.Data;

/// <summary>The multiformats unsigned varint: minimal LEB128 of at most <see cref="MaxLength"/> bytes (63 bits).</summary>
/// <remarks>See <see href="https://github.com/multiformats/unsigned-varint"/>.</remarks>
public static class UnsignedVarint
{
    /// <summary>The longest varint, in bytes.</summary>
    public const int MaxLength = 9;

    /// <summary>The largest value a varint holds.</summary>
    public const ulong MaxValue = (1UL << 63) - 1;

    /// <summary>The number of bytes <paramref name="value"/> encodes to.</summary>
    /// <param name="value">At most <see cref="MaxValue"/>.</param>
    /// <returns>Between 1 and <see cref="MaxLength"/>.</returns>
    public static int GetLength(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxValue);
        var length = 1;
        for (; value >= 0x80; value >>= 7) length++;
        return length;
    }

    /// <summary>Writes <paramref name="value"/> into <paramref name="destination"/>.</summary>
    /// <param name="value">At most <see cref="MaxValue"/>.</param>
    /// <param name="destination">At least <see cref="GetLength"/> bytes.</param>
    /// <returns>The number of bytes written.</returns>
    public static int Write(ulong value, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxValue);
        var at = 0;
        for (; value >= 0x80; value >>= 7) destination[at++] = (byte)(value | 0x80);
        destination[at++] = (byte)value;
        return at;
    }

    /// <summary>Reads a varint from the start of <paramref name="source"/> without throwing.</summary>
    /// <param name="source">The bytes to read.</param>
    /// <param name="value">The value read.</param>
    /// <param name="bytesRead">The number of bytes it took.</param>
    /// <returns>
    /// <see cref="OperationStatus.Done"/>; <see cref="OperationStatus.NeedMoreData"/> when
    /// <paramref name="source"/> ends inside the varint; <see cref="OperationStatus.InvalidData"/> when
    /// it is longer than <see cref="MaxLength"/> bytes or not minimal.
    /// </returns>
    public static OperationStatus TryRead(ReadOnlySpan<byte> source, out ulong value, out int bytesRead)
    {
        value = 0;
        for (bytesRead = 0; bytesRead < MaxLength; bytesRead++)
        {
            if (bytesRead == source.Length) return OperationStatus.NeedMoreData;
            var b = source[bytesRead];
            value |= (ulong)(b & 0x7F) << (7 * bytesRead);
            if (b >= 0x80) continue;
            // A zero last byte after the first only adds length: the same value has a shorter form.
            if (b == 0 && bytesRead > 0) return OperationStatus.InvalidData;
            bytesRead++;
            return OperationStatus.Done;
        }
        return OperationStatus.InvalidData;
    }

    /// <summary>Reads a varint from the start of <paramref name="source"/>.</summary>
    /// <param name="source">The bytes to read.</param>
    /// <param name="bytesRead">The number of bytes it took.</param>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The varint is truncated, longer than <see cref="MaxLength"/> bytes or not minimal.</exception>
    public static ulong Read(ReadOnlySpan<byte> source, out int bytesRead) => TryRead(source, out var value, out bytesRead) switch
    {
        OperationStatus.Done => value,
        OperationStatus.NeedMoreData => throw new FormatException("The varint is truncated."),
        _ => throw new FormatException($"The varint is longer than {MaxLength} bytes or not minimally encoded."),
    };
}
