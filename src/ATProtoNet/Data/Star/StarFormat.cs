namespace ATProtoNet.Data.Star;

// The STAR v1 framing and the atproto space repository profile on top of it:
// magic, varint-prefixed DRISL metadata, then varint-prefixed (path, record) pairs in strict path order.
internal static class StarFormat
{
    // "*l\x01\0": STAR-lite, version 1. Its first two bytes are never those of a CAR.
    public static ReadOnlySpan<byte> Magic => [0x2A, 0x6C, 0x01, 0x00];

    // The metadata is one strict DRISL map with a string $type.
    public static void CheckHeader(ReadOnlyMemory<byte> header)
    {
        var reader = new DrislReader(header);
        if (reader.PeekKind() != DrislValueKind.Map) throw new FormatException("The STAR metadata is not a map.");
        reader.ReadStartMap();
        var typed = false;
        while (reader.PeekKind() != DrislValueKind.End)
            if (reader.ReadKeyUtf8().SequenceEqual("$type"u8) && reader.PeekKind() == DrislValueKind.String) typed = reader.ReadStringUtf8().Length > 0;
            else reader.SkipValue();
        reader.ReadEndMap();
        reader.ReadEnd();
        if (!typed) throw new FormatException("The STAR metadata has no $type string.");
    }

    // A record is one strict DRISL map, nothing after it.
    public static void CheckRecord(ReadOnlyMemory<byte> record)
    {
        var reader = new DrislReader(record);
        if (reader.PeekKind() != DrislValueKind.Map) throw new FormatException("A STAR record is not a DRISL map.");
        reader.SkipValue();
        reader.ReadEnd();
    }

    // {collection}/{rkey}: an NSID and a record key.
    public static bool IsRecordPath(ReadOnlySpan<char> path)
    {
        var slash = path.IndexOf('/');
        return slash > 0 && path.Length <= StarLimits.MaxPathBytes && Nsid.TryParse(path[..slash], out _) && RecordKey.TryParse(path[(slash + 1)..], out _);
    }
}
