using System.Buffers.Binary;
using System.Formats.Cbor;
using ATProtoNet.Identity;

namespace ATProtoNet.Repo;

/// <summary>
/// The fields of a signed repository commit block that verification reads, with the bytes its
/// signature covers.
/// </summary>
internal sealed class CommitBlock
{
    private CommitBlock(string did, string rev, byte[] data, byte[] unsigned, byte[] signature)
    {
        Did = did;
        Rev = rev;
        Data = data;
        Unsigned = unsigned;
        Signature = signature;
    }

    public string Did { get; }

    public string Rev { get; }

    /// <summary>The binary CID of the commit's MST root.</summary>
    public byte[] Data { get; }

    /// <summary>The commit without its <c>sig</c> field, byte for byte as the signer encoded it.</summary>
    public byte[] Unsigned { get; }

    public byte[] Signature { get; }

    /// <summary>
    /// Reads a CAR whose first root (<c>car.Roots[0]</c>) is a signed commit, checking every block
    /// against its CID, and the root against <paramref name="expectedRoot"/> when one is given.
    /// </summary>
    /// <exception cref="FormatException">
    /// The CAR is malformed or lacks its commit, or the commit is not one <see cref="Read"/> accepts.
    /// </exception>
    public static CommitBlock FromCar(ReadOnlySpan<byte> bytes, out CarReader car, Cid? expectedRoot = null)
    {
        try
        {
            car = CarReader.FromBytes(bytes, verifyBlockCids: true);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"The blocks are not a valid CAR: {ex.Message}", ex);
        }

        if (car.Roots.Count == 0)
            throw new FormatException("The CAR names no root.");

        var root = car.Roots[0];
        if (expectedRoot is not null && !expectedRoot.AsSpan().SequenceEqual(root))
            throw new FormatException($"The CAR's root is not the commit {expectedRoot}.");

        var block = car.FindBlock(root) ?? throw new FormatException("The CAR does not include its commit block.");
        return Read(block.Data);
    }

    /// <summary>Reads a commit block, splicing its <c>sig</c> field out in the same pass.</summary>
    /// <remarks>
    /// The signed bytes are the original encoding minus the <c>sig</c> pair, not a re-encoding:
    /// integer widths, key order and link shapes must be exactly the signer's for the hash to
    /// match. The strict reader refuses duplicate keys, which could otherwise show the verifier a
    /// different field than the one the signature covers, and bytes after the map are refused: the
    /// block's CID covers them but the signature does not, so they would let anyone mint new
    /// commit CIDs for one signed commit.
    /// </remarks>
    /// <exception cref="FormatException">
    /// The block is not a definite-length map, and nothing after it, carrying a <c>did</c>,
    /// <c>rev</c>, <c>data</c> CID, <c>version</c> 3, a non-empty byte-string <c>sig</c>, and a
    /// <c>prev</c> that is null or a CID when present.
    /// </exception>
    public static CommitBlock Read(byte[] block)
    {
        ArgumentNullException.ThrowIfNull(block);

        string? did = null, rev = null;
        byte[]? data = null, signature = null;
        long? version = null;
        int? count;
        int bodyStart = 0, sigStart = 0, sigEnd = 0, end = 0;
        try
        {
            // Strict, not a canonical mode: those forbid the tag 42 every commit's links carry.
            var reader = new CborReader(block, CborConformanceMode.Strict);
            count = reader.ReadStartMap();
            bodyStart = block.Length - reader.BytesRemaining;
            for (var i = 0; i < count; i++)
            {
                var start = block.Length - reader.BytesRemaining;
                switch (reader.ReadTextString())
                {
                    case "did":
                        did = reader.ReadTextString();
                        break;
                    case "rev":
                        rev = reader.ReadTextString();
                        break;
                    case "data":
                        data = DagCborLink.Read(reader);
                        break;
                    case "prev":
                        if (DagCborLink.ReadNullable(reader) is { } prev && !Cid.IsValid(prev))
                            throw new FormatException("The commit's prev is not a CID.");
                        break;
                    case "version":
                        version = reader.ReadInt64();
                        break;
                    case "sig" when reader.PeekState() == CborReaderState.ByteString:
                        signature = reader.ReadByteString();
                        (sigStart, sigEnd) = (start, block.Length - reader.BytesRemaining);
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }

            end = block.Length - reader.BytesRemaining;
            reader.ReadEndMap();
            if (reader.BytesRemaining > 0)
                throw new FormatException("Bytes follow the commit map.");
        }
        catch (Exception ex) when (DagCborDecoder.IsMalformed(ex))
        {
            throw new FormatException($"The commit block is malformed: {ex.Message}", ex);
        }

        if (count is not { } entries)
            throw new FormatException("The commit block is not a definite-length map.");
        if (signature is null)
            throw new FormatException("The commit block is not a signed commit.");
        if (signature.Length == 0)
            throw new FormatException("The commit has an empty signature.");
        if (did is null || rev is null || data is null || version is null)
            throw new FormatException("The commit block lacks a did, rev, data or version field.");
        if (!Cid.IsValid(data))
            throw new FormatException("The commit's data is not a CID.");
        if (version != RepoCommit.CurrentVersion)
            throw new FormatException($"The commit is version {version}; only version {RepoCommit.CurrentVersion} is supported.");

        // A map header one entry shorter, then every pair but sig, byte for byte.
        Span<byte> header = stackalloc byte[5];
        var headerLength = WriteMapHeader(header, entries - 1);
        var unsigned = new byte[headerLength + (sigStart - bodyStart) + (end - sigEnd)];
        header[..headerLength].CopyTo(unsigned);
        block.AsSpan(bodyStart, sigStart - bodyStart).CopyTo(unsigned.AsSpan(headerLength));
        block.AsSpan(sigEnd, end - sigEnd).CopyTo(unsigned.AsSpan(headerLength + sigStart - bodyStart));

        return new CommitBlock(did, rev, data, unsigned, signature);
    }

    /// <summary>
    /// Writes a CBOR map header for <paramref name="count"/> entries in its shortest form, as
    /// DAG-CBOR requires, and returns its length.
    /// </summary>
    internal static int WriteMapHeader(Span<byte> destination, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        const int map = 5 << 5;

        if (count < 24)
        {
            destination[0] = (byte)(map | count);
            return 1;
        }

        if (count <= byte.MaxValue)
        {
            destination[0] = map | 24;
            destination[1] = (byte)count;
            return 2;
        }

        if (count <= ushort.MaxValue)
        {
            destination[0] = map | 25;
            BinaryPrimitives.WriteUInt16BigEndian(destination[1..], (ushort)count);
            return 3;
        }

        destination[0] = map | 26;
        BinaryPrimitives.WriteUInt32BigEndian(destination[1..], (uint)count);
        return 5;
    }
}
