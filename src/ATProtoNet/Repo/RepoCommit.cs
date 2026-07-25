using System.Formats.Cbor;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;

namespace ATProtoNet.Repo;

/// <summary>
/// An AT Protocol repository commit — the signed root object of a repo, referencing the
/// MST root through its <c>data</c> field.
/// <para>
/// This is the producer counterpart to <see cref="ATProtoNet.Streaming.FirehoseVerifier"/>:
/// build a commit, sign it with the account's repo signing key, and the resulting block is
/// what <c>com.atproto.sync.getRepo</c> serves as the CAR root and what relays verify.
/// </para>
/// </summary>
/// <remarks>
/// See: https://atproto.com/specs/repository#commit-objects
/// </remarks>
public sealed class RepoCommit
{
    /// <summary>The commit format version. Always 3 for current AT Protocol repositories.</summary>
    public const int CurrentVersion = 3;

    /// <summary>The DID of the repository this commit belongs to.</summary>
    public required string Did { get; init; }

    /// <summary>The commit version. Defaults to <see cref="CurrentVersion"/>.</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Binary CID of the MST root node.</summary>
    public required byte[] Data { get; init; }

    /// <summary>The commit revision — a TID that increases monotonically with each commit.</summary>
    public required string Rev { get; init; }

    /// <summary>
    /// Binary CID of the previous commit. Deprecated by the repo spec and written as
    /// <c>null</c> by current implementations, but the field itself must be present.
    /// </summary>
    public byte[]? Prev { get; init; }

    /// <summary>
    /// Encodes the commit without its <c>sig</c> field. This is the exact byte sequence that
    /// gets signed, and it is a byte-for-byte prefix-preserving subset of the signed encoding
    /// (DAG-CBOR sorts map keys length-first, so removing <c>sig</c> leaves the rest in place).
    /// </summary>
    public byte[] EncodeUnsigned()
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(5);
        WriteCommonFields(writer);
        writer.WriteEndMap();
        return writer.Encode();
    }

    /// <summary>
    /// Signs this commit with the repository's signing key and returns the encoded block.
    /// </summary>
    /// <param name="signingKey">The account's repo signing key (P-256 or K-256).</param>
    /// <returns>The signed commit, with its DAG-CBOR bytes and CID.</returns>
    public SignedRepoCommit Sign(AtProtoKey signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);

        var unsigned = EncodeUnsigned();
        var signature = signingKey.Sign(unsigned);

        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(6);
        WriteCommonFields(writer);
        writer.WriteTextString("sig");
        writer.WriteByteString(signature);
        writer.WriteEndMap();

        var bytes = writer.Encode();
        return new SignedRepoCommit(
            Did, Version, Data, Rev, Prev, signature, bytes,
            CidComputation.ComputeBinaryForDagCbor(bytes));
    }

    private void WriteCommonFields(CborWriter writer)
    {
        writer.WriteTextString("did");
        writer.WriteTextString(Did);

        writer.WriteTextString("rev");
        writer.WriteTextString(Rev);

        writer.WriteTextString("data");
        WriteCidLink(writer, Data);

        writer.WriteTextString("prev");
        if (Prev is not null)
            WriteCidLink(writer, Prev);
        else
            writer.WriteNull();

        writer.WriteTextString("version");
        writer.WriteInt32(Version);
    }

    private static void WriteCidLink(CborWriter writer, byte[] cidBytes)
    {
        writer.WriteTag((CborTag)42);
        var tagged = new byte[cidBytes.Length + 1];
        cidBytes.CopyTo(tagged.AsSpan(1));
        writer.WriteByteString(tagged);
    }
}

/// <summary>
/// A signed repository commit together with its encoded DAG-CBOR block and CID.
/// </summary>
/// <param name="Did">The repository DID.</param>
/// <param name="Version">The commit format version.</param>
/// <param name="Data">Binary CID of the MST root.</param>
/// <param name="Rev">The commit revision (TID).</param>
/// <param name="Prev">Binary CID of the previous commit, or <c>null</c>.</param>
/// <param name="Signature">The raw signature bytes (IEEE P1363 r||s, low-S).</param>
/// <param name="Bytes">The DAG-CBOR encoding of the signed commit — the block itself.</param>
/// <param name="BinaryCid">The binary CID of <paramref name="Bytes"/>.</param>
public sealed record SignedRepoCommit(
    string Did,
    int Version,
    byte[] Data,
    string Rev,
    byte[]? Prev,
    byte[] Signature,
    byte[] Bytes,
    byte[] BinaryCid)
{
    /// <summary>The commit CID as a base32 string (<c>bafyrei…</c>).</summary>
    public Cid Cid => Identity.Cid.Parse(CidComputation.EncodeCidToString(BinaryCid));

    /// <summary>The MST root CID as a base32 string.</summary>
    public Cid DataCid => Identity.Cid.Parse(CidComputation.EncodeCidToString(Data));

    /// <summary>
    /// Verifies this commit's signature against a public key. Used by tests and by any
    /// consumer that wants to re-check its own output before publishing it.
    /// </summary>
    /// <param name="publicKey">The repo signing key (public component suffices).</param>
    public bool Verify(AtProtoKey publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        var unsigned = new RepoCommit
        {
            Did = Did,
            Version = Version,
            Data = Data,
            Rev = Rev,
            Prev = Prev,
        }.EncodeUnsigned();

        return publicKey.Verify(unsigned, Signature);
    }
}
