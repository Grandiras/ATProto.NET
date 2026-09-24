using System.Formats.Cbor;

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

    /// <summary>
    /// CID → block index, built on the first <see cref="FindBlock"/> call.
    /// </summary>
    /// <remarks>
    /// Resolving a repository's MST means one lookup per node, and a repo export runs to tens
    /// of thousands of blocks — a linear scan per lookup makes the walk quadratic. The index is
    /// deferred because plenty of callers only enumerate <see cref="Blocks"/> and never look
    /// one up by CID.
    /// </remarks>
    private Dictionary<byte[], CarBlock>? _index;

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
    /// <param name="verifyBlockCids">
    /// When <c>true</c>, every block's CID is recomputed from its bytes and compared
    /// against the embedded CID; a mismatch throws <see cref="FormatException"/>.
    /// Pass <c>true</c> for any CAR coming from untrusted input.
    /// </param>
    /// <returns>A <see cref="CarReader"/> containing the parsed header and blocks.</returns>
    /// <exception cref="FormatException">
    /// Thrown when the CAR file is malformed, a block is addressed by a CIDv0, or a block CID does
    /// not match its data.
    /// </exception>
    public static CarReader FromBytes(ReadOnlySpan<byte> data, bool verifyBlockCids = false)
    {
        var offset = 0;

        // Every length below comes from the input, so each is checked against the bytes actually
        // left before it is narrowed to an int or used to slice.
        var headerLen = ReadUvarint(data, ref offset);
        if (headerLen == 0 || headerLen > (ulong)(data.Length - offset))
            throw new FormatException("Invalid CAR header length.");

        var header = ParseHeader(data.Slice(offset, (int)headerLen).ToArray());
        offset += (int)headerLen;

        if (header.Version != 1)
            throw new FormatException($"Unsupported CAR version: {header.Version}. Only v1 is supported.");

        var blocks = new List<CarBlock>();

        while (offset < data.Length)
        {
            // Block = varint(len) + CID + data
            var blockLen = ReadUvarint(data, ref offset);
            if (blockLen == 0)
                break;

            if (blockLen > (ulong)(data.Length - offset))
                throw new FormatException("CAR block extends past end of file.");

            var blockEnd = offset + (int)blockLen;
            var cid = ParseCid(data[..blockEnd], ref offset);

            // Remaining bytes are the block data
            blocks.Add(new CarBlock(cid, data[offset..blockEnd].ToArray()));
            offset = blockEnd;
        }

        var reader = new CarReader(header, blocks);
        if (verifyBlockCids)
            reader.VerifyAllBlockCids();
        return reader;
    }

    /// <summary>
    /// Recomputes each block's CID from its bytes and asserts it matches the embedded CID.
    /// Throws <see cref="FormatException"/> on mismatches AND on unknown codecs.
    /// </summary>
    /// <remarks>
    /// AT Protocol only ever uses dag-cbor (0x71) and raw (0x55) on the wire; any
    /// other codec from an untrusted source is suspicious enough to fail closed
    /// rather than silently pass — a hostile relay could otherwise smuggle blocks
    /// with codecs the verifier can't recompute (e.g. dag-pb 0x70) and have them
    /// sail through. The lower-level <see cref="VerifyBlockCid"/> still returns the
    /// tri-state result so callers who want softer semantics can opt in.
    /// </remarks>
    public void VerifyAllBlockCids()
    {
        foreach (var block in _blocks)
        {
            switch (VerifyBlockCid(block))
            {
                case BlockCidVerification.Mismatch:
                    throw new FormatException(
                        $"CAR block CID does not match its data (CID: {block.CidHex}). " +
                        "The CAR file may be corrupt or has been tampered with.");
                case BlockCidVerification.UnknownCodec:
                    throw new FormatException(
                        $"CAR block (CID: {block.CidHex}) uses an unknown codec. " +
                        "AT Protocol only permits dag-cbor (0x71) and raw (0x55); " +
                        "rejecting to avoid smuggled blocks bypassing CID verification.");
            }
        }
    }

    /// <summary>
    /// Recomputes a block's CID from its bytes and reports whether it matches.
    /// Returns <see cref="BlockCidVerification.UnknownCodec"/> when the codec is one
    /// we can't recompute (rather than conflating that with tampering).
    /// </summary>
    public static BlockCidVerification VerifyBlockCid(CarBlock block)
    {
        // CIDv1: version(0x01) + codec + 0x12 + 0x20 + 32-byte SHA-256 digest. Anything else,
        // CIDv0 (a bare multihash, which implies dag-pb) included, is a format AT Protocol does
        // not use and this cannot recompute.
        if (block.Cid.Length < 4 || block.Cid[0] != 0x01)
            return BlockCidVerification.UnknownCodec;

        var codec = block.Cid[1];
        var isDagCbor = codec == 0x71;
        var isRaw = codec == 0x55;
        if (!isDagCbor && !isRaw)
            return BlockCidVerification.UnknownCodec;

        var expected = isDagCbor
            ? CidComputation.ComputeBinaryForDagCbor(block.Data)
            : CidComputation.ComputeBinaryForRaw(block.Data);

        return expected.AsSpan().SequenceEqual(block.Cid)
            ? BlockCidVerification.Match
            : BlockCidVerification.Mismatch;
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

        // Parse the stream's own buffer. ToArray() would copy the whole CAR a second time,
        // and repo exports are large enough for that to land on the large object heap.
        return FromBytes(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    /// <summary>
    /// Finds a block by its CID bytes.
    /// </summary>
    /// <param name="cid">The CID to search for.</param>
    /// <returns>The matching block, or <c>null</c> if not found.</returns>
    public CarBlock? FindBlock(ReadOnlySpan<byte> cid)
    {
        var index = _index;
        if (index is null)
        {
            index = new Dictionary<byte[], CarBlock>(_blocks.Count, CidComparer.Instance);
            // First writer wins, matching the linear scan this replaced: a CAR that repeats a
            // CID resolves to its earliest block either way.
            foreach (var block in _blocks)
                index.TryAdd(block.Cid, block);

            // A racing caller may build its own copy; both are equivalent, so publishing
            // whichever finishes last is fine and avoids locking on every lookup.
            _index = index;
        }

        // The span-keyed lookup hashes the caller's bytes in place, so probing costs no
        // allocation even though the stored keys are arrays.
        return index.GetAlternateLookup<ReadOnlySpan<byte>>().TryGetValue(cid, out var found)
            ? found
            : null;
    }

    /// <summary>
    /// Compares CIDs by content rather than by array reference, and accepts a
    /// <see cref="ReadOnlySpan{T}"/> as an alternate key so lookups need not copy.
    /// </summary>
    private sealed class CidComparer
        : IEqualityComparer<byte[]>, IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
    {
        public static readonly CidComparer Instance = new();

        public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj) => Hash(obj);

        public bool Equals(ReadOnlySpan<byte> alternate, byte[] other) => alternate.SequenceEqual(other);

        public int GetHashCode(ReadOnlySpan<byte> alternate) => Hash(alternate);

        public byte[] Create(ReadOnlySpan<byte> alternate) => alternate.ToArray();

        private static int Hash(ReadOnlySpan<byte> cid)
        {
            // A CID ends in a SHA-256 digest, so its trailing bytes are already uniformly
            // distributed — hashing the tail is as good as hashing all of it, and cheaper.
            var hash = new HashCode();
            hash.AddBytes(cid.Length <= 8 ? cid : cid[^8..]);
            return hash.ToHashCode();
        }
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

    // ── Header parsing ───────────────────────────────────────

    /// <summary>
    /// Parses the DAG-CBOR header, <c>{"roots": [&lt;CID link&gt;, …], "version": 1}</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="CborReader"/> walks nested values iteratively, so a hostile header of deeply
    /// nested arrays under an unknown key is skipped rather than overflowing the stack.
    /// </remarks>
    private static CarHeader ParseHeader(byte[] cbor)
    {
        try
        {
            var reader = new CborReader(cbor, CborConformanceMode.Lax);
            int? version = null;
            var roots = new List<byte[]>();

            if (reader.PeekState() != CborReaderState.StartMap)
                throw new FormatException("CAR header must be a CBOR map.");

            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                if (reader.PeekState() != CborReaderState.TextString)
                    throw new FormatException("CAR header map key must be a text string.");

                switch (reader.ReadTextString())
                {
                    case "version":
                        version = reader.ReadInt32();
                        break;

                    case "roots":
                        if (reader.PeekState() != CborReaderState.StartArray)
                            throw new FormatException("CAR header 'roots' must be a CBOR array.");

                        reader.ReadStartArray();
                        while (reader.PeekState() != CborReaderState.EndArray)
                            roots.Add(DagCborLink.Read(reader));
                        reader.ReadEndArray();
                        break;

                    default:
                        reader.SkipValue();
                        break;
                }
            }

            reader.ReadEndMap();

            if (reader.BytesRemaining != 0)
                throw new FormatException("CAR header has trailing bytes after its map.");

            return new CarHeader(
                version ?? throw new FormatException("CAR header has no 'version'."),
                roots);
        }
        catch (Exception ex) when (ex is CborContentException or InvalidOperationException or OverflowException)
        {
            throw new FormatException($"Invalid CAR header: {ex.Message}", ex);
        }
    }

    // ── CID parsing ──────────────────────────────────────────

    /// <summary>
    /// Reads the CID at the start of a block section. <paramref name="data"/> ends where the
    /// section does, so a CID claiming more bytes than the section holds is caught here.
    /// </summary>
    private static byte[] ParseCid(ReadOnlySpan<byte> data, ref int offset)
    {
        var cidStart = offset;

        // CIDv1: version + codec + multihash. A CIDv0 is a bare multihash, so it starts with the
        // SHA-256 code (0x12) where the version belongs.
        var version = ReadUvarint(data, ref offset);
        if (version == 0x12)
            throw new FormatException("CIDv0 blocks are not supported; AT Protocol uses CIDv1 only.");
        if (version != 1)
            throw new FormatException($"Unsupported CID version: {version}");

        ReadUvarint(data, ref offset); // codec (e.g., 0x71 = dag-cbor)

        // Multihash: hash function code + digest size + digest
        ReadUvarint(data, ref offset); // hash function (e.g., 0x12 = sha2-256)
        var digestSize = ReadUvarint(data, ref offset);
        if (digestSize > (ulong)(data.Length - offset))
            throw new FormatException("CID digest extends past the end of its CAR block.");

        offset += (int)digestSize;
        return data[cidStart..offset].ToArray();
    }

    // ── Unsigned varint (LEB128) ─────────────────────────────

    /// <summary>
    /// Reads a multiformats unsigned varint: at most 9 bytes (63 bits), minimally encoded.
    /// </summary>
    /// <exception cref="FormatException">The varint is truncated, too long, or not minimal.</exception>
    internal static ulong ReadUvarint(ReadOnlySpan<byte> data, ref int offset)
    {
        const int MaxBytes = 9;

        ulong result = 0;
        for (var i = 0; i < MaxBytes; i++)
        {
            if (offset >= data.Length)
                throw new FormatException("Unexpected end of varint.");

            var b = data[offset++];
            result |= (ulong)(b & 0x7F) << (7 * i);

            if ((b & 0x80) == 0)
            {
                // A zero final byte after the first adds nothing but length, so the same value
                // has a shorter encoding.
                if (b == 0 && i > 0)
                    throw new FormatException("Varint is not minimally encoded.");
                return result;
            }
        }

        throw new FormatException($"Varint overflow: longer than {MaxBytes} bytes.");
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
    public string CidHex => Convert.ToHexStringLower(Cid);
}

/// <summary>Tri-state outcome of recomputing a CAR block's CID from its bytes.</summary>
public enum BlockCidVerification
{
    /// <summary>The recomputed CID matches the embedded CID.</summary>
    Match,

    /// <summary>The recomputed CID does not match the embedded CID — likely tampering or corruption.</summary>
    Mismatch,

    /// <summary>
    /// The block uses a codec this implementation can't recompute (e.g. dag-pb, dag-json,
    /// a future codec). The block isn't necessarily bad — we just can't verify it here.
    /// </summary>
    UnknownCodec,
}
