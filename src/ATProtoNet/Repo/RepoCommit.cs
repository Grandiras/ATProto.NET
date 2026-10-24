using ATProtoNet.Crypto;
using ATProtoNet.Data;

namespace ATProtoNet.Repo;

/// <summary>An unsigned version-3 repository commit: the repository, its revision and its tree root.</summary>
/// <remarks>See <see href="https://atproto.com/specs/repository#commit-objects"/>.</remarks>
public sealed record RepoCommit
{
    /// <summary>The commit format version this SDK reads and writes.</summary>
    public const int Version = 3;

    /// <summary>The repository's DID.</summary>
    public required Did Did { get; init; }

    /// <summary>The revision, increasing with every commit.</summary>
    public required Tid Rev { get; init; }

    /// <summary>The root of the repository's tree.</summary>
    public required Cid Data { get; init; }

    /// <summary>The previous commit, usually <see langword="null"/>; written as null when absent.</summary>
    public Cid? Prev { get; init; }

    /// <summary>Signs the commit: a low-S signature over the canonical encoding without <c>sig</c>.</summary>
    /// <param name="key">The repository's signing key.</param>
    /// <returns>The signed commit.</returns>
    public SignedCommit Sign(SigningKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        DataModelMap unsigned = new(
        [
            new("did", new DataModelString(Did.Value)),
            new("version", new DataModelInteger(Version)),
            new("data", new DataModelLink(Data)),
            new("rev", new DataModelString(Rev.ToString())),
            new("prev", Prev is null ? DataModelNull.Instance : new DataModelLink(Prev)),
        ]);
        return SignedCommit.Read(SignedObject.Sign(unsigned, key));
    }
}

/// <summary>A signed commit, as its exact block.</summary>
public sealed class SignedCommit
{
    private SignedCommit(RepoCommit commit, ReadOnlyMemory<byte> signature, ReadOnlyMemory<byte> block) =>
        (Commit, Signature, Block, Cid) = (commit, signature, block, Cid.Compute(CidCodec.DagCbor, block.Span));

    /// <summary>The commit's fields.</summary>
    public RepoCommit Commit { get; }

    /// <summary>The signature: 64 bytes <c>r‖s</c> for a well-formed one.</summary>
    public ReadOnlyMemory<byte> Signature { get; }

    /// <summary>The block, byte for byte as read or written.</summary>
    public ReadOnlyMemory<byte> Block { get; }

    /// <summary>The CID of <see cref="Block"/>.</summary>
    public Cid Cid { get; }

    /// <summary>Reads a commit block strictly; unknown fields are skipped (and covered by the signature).</summary>
    /// <param name="block">The block, nothing after it.</param>
    /// <returns>The commit. Its signature is not checked: see <see cref="Verify"/>.</returns>
    /// <exception cref="RepoVerificationException">The block is not a well-formed signed commit
    /// (<see cref="RepoVerificationError.MalformedCommit"/>), or not version 3 (<see cref="RepoVerificationError.UnsupportedVersion"/>).</exception>
    public static SignedCommit Read(ReadOnlyMemory<byte> block)
    {
        Did? did = null;
        Tid? rev = null;
        Cid? data = null, prev = null;
        long? version = null;
        ReadOnlyMemory<byte> sig = default;
        try
        {
            // Strict: a duplicate key could show a verifier a field other than the one signed, and
            // trailing bytes would mint new commit CIDs for one signature.
            var reader = new DrislReader(block);
            var fields = reader.ReadStartMap();
            for (var i = 0; i < fields; i++)
            {
                switch (reader.ReadKey())
                {
                    case "did": did = Did.TryParse(reader.ReadString(), out var d) ? d : throw Malformed("its did is not a DID"); break;
                    case "rev": rev = Tid.TryParse(reader.ReadString(), out var r) ? r : throw Malformed("its rev is not a TID"); break;
                    case "data": data = reader.ReadLink(); break;
                    case "prev" when reader.PeekKind() == DrislValueKind.Null: reader.ReadNull(); break;
                    case "prev": prev = reader.ReadLink(); break;
                    case "version": version = reader.ReadInt64(); break;
                    case "sig": sig = reader.ReadBytes(); break;
                    default: reader.SkipValue(); break;
                }
            }
            reader.ReadEndMap();
            reader.ReadEnd();
        }
        catch (FormatException ex)
        {
            throw Malformed($"it does not decode: {ex.Message}", ex);
        }
        if (version is { } v and not RepoCommit.Version)
            throw new RepoVerificationException(RepoVerificationError.UnsupportedVersion, $"Commit version {v} is not supported; only version {RepoCommit.Version} is.");
        if (did is null || rev is null || data is null || version is null || sig.IsEmpty)
            throw Malformed("it lacks did, rev, data, version or a non-empty sig");
        return new(new() { Did = did, Rev = rev.Value, Data = data, Prev = prev }, sig, block);
    }

    /// <summary>Whether the commit carries a valid low-S signature by <paramref name="key"/>.</summary>
    /// <remarks>The signed bytes are the received block with the <c>sig</c> entry cut out, never a re-encoding.</remarks>
    /// <param name="key">The repository's signing key, from a DID document the caller resolved.</param>
    /// <returns>Whether the signature verifies.</returns>
    public bool Verify(VerificationKey key) => SignedObject.Verify(Block, key);

    private static RepoVerificationException Malformed(string problem, Exception? inner = null) =>
        new(RepoVerificationError.MalformedCommit, $"Not a well-formed signed commit: {problem}.", inner);
}
