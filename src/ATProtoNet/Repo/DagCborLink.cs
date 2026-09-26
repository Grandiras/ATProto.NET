using System.Formats.Cbor;

namespace ATProtoNet.Repo;

/// <summary>
/// Reads and writes DAG-CBOR CID links: CBOR tag 42 around a byte string holding the binary CID
/// behind a single <c>0x00</c> (identity multibase) prefix byte.
/// </summary>
/// <remarks>
/// Every encoder and decoder in the SDK goes through here, so the tag, the prefix byte and the
/// error for a malformed link are defined once. Readers throw <see cref="FormatException"/>, which
/// is what the parsers built on top promise for untrusted input.
/// </remarks>
internal static class DagCborLink
{
    /// <summary>The IPLD CBOR tag for a CID.</summary>
    internal const CborTag Tag = (CborTag)42;

    /// <summary>Longest CID written through a stack buffer; an atproto CID is 36 bytes.</summary>
    private const int MaxStackCidBytes = 128;

    /// <summary>Writes <paramref name="cid"/> (binary, without the multibase prefix) as a link.</summary>
    internal static void Write(CborWriter writer, ReadOnlySpan<byte> cid)
    {
        writer.WriteTag(Tag);

        Span<byte> tagged = cid.Length < MaxStackCidBytes
            ? stackalloc byte[cid.Length + 1]
            : new byte[cid.Length + 1];
        tagged[0] = 0x00;
        cid.CopyTo(tagged[1..]);
        writer.WriteByteString(tagged);
    }

    /// <summary>Writes <paramref name="cid"/> as a link, or CBOR <c>null</c> when it is absent.</summary>
    internal static void WriteNullable(CborWriter writer, byte[]? cid)
    {
        if (cid is null)
            writer.WriteNull();
        else
            Write(writer, cid);
    }

    /// <summary>Reads a link, returning the binary CID without its multibase prefix.</summary>
    /// <exception cref="FormatException">The next value is not a well-formed CID link.</exception>
    internal static byte[] Read(CborReader reader)
    {
        ReadTag(reader);
        return ReadPayload(reader);
    }

    /// <summary>Reads a link or a CBOR <c>null</c>.</summary>
    /// <exception cref="FormatException">The next value is neither <c>null</c> nor a well-formed CID link.</exception>
    internal static byte[]? ReadNullable(CborReader reader)
    {
        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        return Read(reader);
    }

    /// <summary>
    /// Reads the byte string of a link whose tag the caller has already consumed, returning the
    /// binary CID without its multibase prefix.
    /// </summary>
    /// <exception cref="FormatException">The payload is not a prefixed CID.</exception>
    internal static byte[] ReadPayload(CborReader reader)
    {
        if (reader.PeekState() != CborReaderState.ByteString)
            throw new FormatException("A CID link (tag 42) must wrap a byte string.");

        // Sliced from the input rather than read into an array first: one allocation per link.
        var bytes = reader.ReadDefiniteLengthByteString().Span;
        ValidatePrefix(bytes);
        return bytes[1..].ToArray();
    }

    /// <summary>
    /// Reads the byte string of a link whose tag the caller has already consumed, returning the
    /// CID in its base32 string form.
    /// </summary>
    /// <exception cref="FormatException">The payload is not a prefixed CID.</exception>
    internal static string ReadPayloadAsString(CborReader reader)
    {
        if (reader.PeekState() != CborReaderState.ByteString)
            throw new FormatException("A CID link (tag 42) must wrap a byte string.");

        // A CID fits the stack buffer, so the link is encoded straight to its string form
        // without an intermediate array.
        Span<byte> buffer = stackalloc byte[MaxStackCidBytes];
        if (reader.TryReadByteString(buffer, out var written))
        {
            ValidatePrefix(buffer[..written]);
            return CidComputation.EncodeCidToString(buffer[1..written]);
        }

        var bytes = reader.ReadByteString();
        ValidatePrefix(bytes);
        return CidComputation.EncodeCidToString(bytes.AsSpan(1));
    }

    private static void ReadTag(CborReader reader)
    {
        if (reader.PeekState() != CborReaderState.Tag || reader.ReadTag() != Tag)
            throw new FormatException("Expected a CID link (CBOR tag 42).");
    }

    private static void ValidatePrefix(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2 || bytes[0] != 0x00)
            throw new FormatException("Invalid CID link: missing the 0x00 identity multibase prefix.");
    }
}
