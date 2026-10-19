using System.Buffers.Binary;

namespace ATProtoNet.Data;

// The CBOR wire primitives DRISL uses: initial bytes, headers in their shortest form, canonical key order.
internal static class DrislEncoding
{
    public const int UnsignedInteger = 0, NegativeInteger = 1, ByteString = 2, TextString = 3, Array = 4, Map = 5, Tag = 6, Simple = 7;
    public const byte False = 0xF4, True = 0xF5, Null = 0xF6, Break = 0xFF;
    public const ulong LinkTag = 42;

    // A link's byte string: the 0x00 multibase identity prefix, then the binary CID.
    public const int LinkLength = 1 + Cid.BinaryLength;

    public static int HeaderLength(ulong argument) =>
        argument < 24 ? 1 : argument <= byte.MaxValue ? 2 : argument <= ushort.MaxValue ? 3 : argument <= uint.MaxValue ? 5 : 9;

    // Writes a header in its shortest form and returns its length.
    public static int WriteHeader(Span<byte> destination, int major, ulong argument)
    {
        var initial = (byte)(major << 5);
        switch (HeaderLength(argument))
        {
            case 1:
                destination[0] = (byte)(initial | (int)argument);
                return 1;
            case 2:
                destination[0] = (byte)(initial | 24);
                destination[1] = (byte)argument;
                return 2;
            case 3:
                destination[0] = (byte)(initial | 25);
                BinaryPrimitives.WriteUInt16BigEndian(destination[1..], (ushort)argument);
                return 3;
            case 5:
                destination[0] = (byte)(initial | 26);
                BinaryPrimitives.WriteUInt32BigEndian(destination[1..], (uint)argument);
                return 5;
            default:
                destination[0] = (byte)(initial | 27);
                BinaryPrimitives.WriteUInt64BigEndian(destination[1..], argument);
                return 9;
        }
    }

    // DRISL key order: the shorter key first, equal lengths by unsigned bytes. It holds for keys with or
    // without their headers, since a longer key never has a shorter header.
    public static int CompareKeys(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        left.Length != right.Length ? left.Length.CompareTo(right.Length) : left.SequenceCompareTo(right);
}

// A data item's initial byte and argument, as read; Length counts the header bytes.
internal readonly record struct DrislHeader(int Major, int Info, ulong Argument, int Length)
{
    public bool Indefinite => Info == 31;

    public bool IsShortest => Info switch
    {
        < 24 => true,
        24 => Argument >= 24,
        25 => Argument > byte.MaxValue,
        26 => Argument > ushort.MaxValue,
        27 => Argument > uint.MaxValue,
        _ => false,
    };

    public static DrislHeader Read(ReadOnlySpan<byte> data, int at)
    {
        if (at >= data.Length) throw Truncated();
        var initial = data[at];
        int major = initial >> 5, info = initial & 0x1F;
        if (info < 24) return new(major, info, (ulong)info, 1);
        if (info == 31)
            return major is DrislEncoding.UnsignedInteger or DrislEncoding.NegativeInteger or DrislEncoding.Tag
                ? throw new FormatException($"CBOR major type {major} has no indefinite length.")
                : new(major, info, 0, 1);
        if (info > 27) throw new FormatException($"Reserved CBOR additional information {info}.");
        var size = 1 << (info - 24);
        if (data.Length - at - 1 < size) throw Truncated();
        var bytes = data.Slice(at + 1, size);
        var argument = size switch
        {
            1 => bytes[0],
            2 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            4 => BinaryPrimitives.ReadUInt32BigEndian(bytes),
            _ => BinaryPrimitives.ReadUInt64BigEndian(bytes),
        };
        return new(major, info, argument, 1 + size);
    }

    public static FormatException Truncated() => new("The CBOR input ends inside a value.");
}
