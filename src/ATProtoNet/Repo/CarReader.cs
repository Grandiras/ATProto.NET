using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ATProtoNet.Repo;

/// <summary>
/// Reads Content Addressable aRchive (CAR v1) files as used by AT Protocol for
/// repository exports (<c>com.atproto.sync.getRepo</c>).
/// <para>
/// CAR files contain a header followed by a series of content-addressed blocks.
/// Each block is identified by its CID (Content Identifier).
/// </para>
/// </summary>
/// <remarks>
/// See: https://ipld.io/specs/transport/car/carv1/
/// </remarks>
public sealed class CarReader
{
    private readonly IReadOnlyList<CarBlock> _blocks;
    private readonly CarHeader _header;

    /// <summary>The CAR header containing the file version and root CIDs.</summary>
    public CarHeader Header => _header;

    /// <summary>All blocks contained in the CAR file.</summary>
    public IReadOnlyList<CarBlock> Blocks => _blocks;

    /// <summary>The root CID(s) specified in the CAR header.</summary>
    public IReadOnlyList<byte[]> Roots => _header.Roots;

    private CarReader(CarHeader header, IReadOnlyList<CarBlock> blocks)
    {
        _header = header;
        _blocks = blocks;
    }

    /// <summary>
    /// Parses a CAR file from a byte array.
    /// </summary>
    /// <param name="data">The raw CAR file bytes.</param>
    /// <returns>A <see cref="CarReader"/> containing the parsed header and blocks.</returns>
    /// <exception cref="FormatException">Thrown when the CAR file is malformed.</exception>
    public static CarReader FromBytes(ReadOnlySpan<byte> data)
    {
        var offset = 0;

        // Parse header length (unsigned varint)
        var headerLen = ReadUvarint(data, ref offset);
        if (headerLen == 0 || offset + (int)headerLen > data.Length)
            throw new FormatException("Invalid CAR header length.");

        // Parse header (DAG-CBOR encoded)
        var headerBytes = data.Slice(offset, (int)headerLen);
        offset += (int)headerLen;

        var header = ParseHeader(headerBytes);

        if (header.Version != 1)
            throw new FormatException($"Unsupported CAR version: {header.Version}. Only v1 is supported.");

        // Parse blocks
        var blocks = new List<CarBlock>();

        while (offset < data.Length)
        {
            // Block = varint(len) + CID + data
            var blockLen = ReadUvarint(data, ref offset);
            if (blockLen == 0)
                break;

            var blockStart = offset;
            var blockEnd = offset + (int)blockLen;

            if (blockEnd > data.Length)
                throw new FormatException("CAR block extends past end of file.");

            // Parse CID from the block
            var cidStart = offset;
            var cid = ParseCid(data, ref offset);

            // Remaining bytes are the block data
            var dataLen = blockEnd - offset;
            var blockData = data.Slice(offset, dataLen).ToArray();
            offset = blockEnd;

            blocks.Add(new CarBlock(cid, blockData));
        }

        return new CarReader(header, blocks);
    }

    /// <summary>
    /// Parses a CAR file from a stream.
    /// </summary>
    /// <param name="stream">The stream containing the CAR data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="CarReader"/> containing the parsed header and blocks.</returns>
    public static async Task<CarReader> FromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return FromBytes(ms.ToArray());
    }

    /// <summary>
    /// Finds a block by its CID bytes.
    /// </summary>
    /// <param name="cid">The CID to search for.</param>
    /// <returns>The matching block, or <c>null</c> if not found.</returns>
    public CarBlock? FindBlock(ReadOnlySpan<byte> cid)
    {
        foreach (var block in _blocks)
        {
            if (cid.SequenceEqual(block.Cid))
                return block;
        }
        return null;
    }

    /// <summary>
    /// Gets the root block (first root CID's data).
    /// </summary>
    /// <returns>The root block, or <c>null</c> if not found.</returns>
    public CarBlock? GetRootBlock()
    {
        if (_header.Roots.Count == 0)
            return null;

        return FindBlock(_header.Roots[0]);
    }

    // ── Header parsing (simplified DAG-CBOR) ─────────────────

    private static CarHeader ParseHeader(ReadOnlySpan<byte> cbor)
    {
        // A minimal CBOR map parser for the CAR v1 header:
        // { "version": 1, "roots": [ CID, ... ] }
        var offset = 0;
        int version = 1;
        var roots = new List<byte[]>();

        if (cbor.Length == 0)
            throw new FormatException("Empty CAR header.");

        var mapInfo = ReadCborInfo(cbor, ref offset);
        if (mapInfo.MajorType != 5) // Major type 5 = map
            throw new FormatException("CAR header must be a CBOR map.");

        var mapLen = (int)mapInfo.Value;

        for (var i = 0; i < mapLen; i++)
        {
            // Read key (expected: text string)
            var keyInfo = ReadCborInfo(cbor, ref offset);
            if (keyInfo.MajorType != 3) // text string
                throw new FormatException("CAR header map key must be a text string.");

            var key = Encoding.UTF8.GetString(cbor.Slice(offset, (int)keyInfo.Value));
            offset += (int)keyInfo.Value;

            switch (key)
            {
                case "version":
                    var verInfo = ReadCborInfo(cbor, ref offset);
                    version = (int)verInfo.Value;
                    break;

                case "roots":
                    var arrInfo = ReadCborInfo(cbor, ref offset);
                    if (arrInfo.MajorType != 4) // array
                        throw new FormatException("CAR header 'roots' must be a CBOR array.");

                    for (var j = 0; j < (int)arrInfo.Value; j++)
                    {
                        // CID in CBOR is tagged (tag 42) byte string
                        var rootInfo = ReadCborInfo(cbor, ref offset);

                        if (rootInfo.MajorType == 6) // tag
                        {
                            // Read the tagged value (byte string)
                            rootInfo = ReadCborInfo(cbor, ref offset);
                        }

                        if (rootInfo.MajorType != 2) // byte string
                            throw new FormatException("CAR header root must be a CBOR byte string.");

                        // The byte string may include an identity multibase prefix (0x00)
                        var rootBytes = cbor.Slice(offset, (int)rootInfo.Value).ToArray();
                        offset += (int)rootInfo.Value;

                        // Strip identity multibase prefix if present
                        if (rootBytes.Length > 0 && rootBytes[0] == 0x00)
                            rootBytes = rootBytes[1..];

                        roots.Add(rootBytes);
                    }
                    break;

                default:
                    // Skip unknown values
                    SkipCborValue(cbor, ref offset);
                    break;
            }
        }

        return new CarHeader(version, roots);
    }

    // ── CID parsing ──────────────────────────────────────────

    private static byte[] ParseCid(ReadOnlySpan<byte> data, ref int offset)
    {
        var cidStart = offset;

        // Check for CIDv0 (starts with 0x12 0x20 — SHA2-256 with 32-byte digest)
        if (offset + 2 <= data.Length && data[offset] == 0x12 && data[offset + 1] == 0x20)
        {
            // CIDv0: multihash only (sha2-256, 32 bytes)
            offset += 2 + 32; // 0x12 0x20 + 32 bytes digest
            return data[cidStart..offset].ToArray();
        }

        // CIDv1: version + codec + multihash
        var version = ReadUvarint(data, ref offset);
        if (version != 1)
            throw new FormatException($"Unsupported CID version: {version}");

        var codec = ReadUvarint(data, ref offset); // codec (e.g., 0x71 = dag-cbor)

        // Multihash: hash function code + digest size + digest
        var hashFunc = ReadUvarint(data, ref offset);
        var digestSize = ReadUvarint(data, ref offset);
        offset += (int)digestSize;

        return data[cidStart..offset].ToArray();
    }

    // ── CBOR utilities ───────────────────────────────────────

    private readonly record struct CborInfo(byte MajorType, ulong Value);

    private static CborInfo ReadCborInfo(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset >= data.Length)
            throw new FormatException("Unexpected end of CBOR data.");

        var initial = data[offset++];
        var majorType = (byte)(initial >> 5);
        var additionalInfo = initial & 0x1F;

        ulong value = additionalInfo switch
        {
            < 24 => (ulong)additionalInfo,
            24 when offset < data.Length => data[offset++],
            25 when offset + 2 <= data.Length => ReadUInt16(data, ref offset),
            26 when offset + 4 <= data.Length => ReadUInt32(data, ref offset),
            27 when offset + 8 <= data.Length => ReadUInt64(data, ref offset),
            _ => throw new FormatException("Invalid CBOR additional info."),
        };

        return new CborInfo(majorType, value);
    }

    private static void SkipCborValue(ReadOnlySpan<byte> data, ref int offset)
    {
        var info = ReadCborInfo(data, ref offset);

        switch (info.MajorType)
        {
            case 0: // unsigned int — already consumed
            case 1: // negative int — already consumed
            case 7: // simple value / float — already consumed (for small values)
                break;
            case 2: // byte string
            case 3: // text string
                offset += (int)info.Value;
                break;
            case 4: // array
                for (var i = 0; i < (int)info.Value; i++)
                    SkipCborValue(data, ref offset);
                break;
            case 5: // map
                for (var i = 0; i < (int)info.Value; i++)
                {
                    SkipCborValue(data, ref offset); // key
                    SkipCborValue(data, ref offset); // value
                }
                break;
            case 6: // tag
                SkipCborValue(data, ref offset); // tagged value
                break;
        }
    }

    // ── Unsigned varint (LEB128) ─────────────────────────────

    private static ulong ReadUvarint(ReadOnlySpan<byte> data, ref int offset)
    {
        ulong result = 0;
        var shift = 0;

        while (offset < data.Length)
        {
            var b = data[offset++];
            result |= (ulong)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
                return result;

            shift += 7;
            if (shift > 63)
                throw new FormatException("Varint overflow.");
        }

        throw new FormatException("Unexpected end of varint.");
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        offset += 2;
        return value;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        offset += 4;
        return value;
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt64BigEndian(data[offset..]);
        offset += 8;
        return value;
    }
}

/// <summary>The header of a CAR v1 file.</summary>
/// <param name="Version">CAR format version (must be 1).</param>
/// <param name="Roots">The root CID(s) of the DAG.</param>
public sealed record CarHeader(int Version, IReadOnlyList<byte[]> Roots);

/// <summary>A content-addressed block within a CAR file.</summary>
/// <param name="Cid">The CID (Content Identifier) of this block.</param>
/// <param name="Data">The raw block data.</param>
public sealed record CarBlock(byte[] Cid, byte[] Data)
{
    /// <summary>Returns the CID as a hex string for debugging.</summary>
    public string CidHex => Convert.ToHexString(Cid).ToLowerInvariant();

    /// <summary>Returns the block data length.</summary>
    public int DataLength => Data.Length;
}
