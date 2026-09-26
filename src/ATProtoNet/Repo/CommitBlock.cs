using System.Formats.Cbor;
using ATProtoNet.Streaming;

namespace ATProtoNet.Repo;

/// <summary>
/// The fields of a signed repository commit block that sync verification reads, with the bytes
/// its signature covers.
/// </summary>
internal sealed class CommitBlock
{
    private CommitBlock(string did, string rev, byte[] data, long version, byte[] unsigned, byte[] signature)
    {
        Did = did;
        Rev = rev;
        Data = data;
        Version = version;
        Unsigned = unsigned;
        Signature = signature;
    }

    public string Did { get; }

    public string Rev { get; }

    /// <summary>The binary CID of the commit's MST root.</summary>
    public byte[] Data { get; }

    public long Version { get; }

    /// <summary>The commit without its <c>sig</c> field, byte for byte as the signer encoded it.</summary>
    public byte[] Unsigned { get; }

    public byte[] Signature { get; }

    /// <summary>
    /// Reads a commit block.
    /// </summary>
    /// <exception cref="FormatException">
    /// The block is not a map carrying a <c>did</c>, <c>rev</c>, <c>data</c> link, <c>version</c>
    /// and a non-empty byte-string <c>sig</c>.
    /// </exception>
    public static CommitBlock Read(byte[] block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var signed = FirehoseVerifier.ExtractSignedView(block)
            ?? throw new FormatException("The commit block is not a signed commit.");
        if (signed.SigBytes is not { Length: > 0 } signature)
            throw new FormatException("The commit has an empty signature.");

        string? did = null;
        string? rev = null;
        byte[]? data = null;
        long? version = null;
        try
        {
            var reader = new CborReader(block, CborConformanceMode.Lax);
            var count = reader.ReadStartMap()
                ?? throw new FormatException("The commit block is not a definite-length map.");

            for (var i = 0; i < count; i++)
            {
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
                    case "version":
                        version = reader.ReadInt64();
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is CborContentException or InvalidOperationException or OverflowException)
        {
            throw new FormatException($"The commit block is malformed: {ex.Message}", ex);
        }

        if (did is null || rev is null || data is null || version is null)
            throw new FormatException("The commit block lacks a did, rev, data or version field.");

        return new CommitBlock(did, rev, data, version.Value, signed.UnsignedBytes, signature);
    }
}
