using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Crypto;
using ATProtoNet.Repo;
using ATProtoNet.Serialization;

namespace ATProtoNet.Spaces;

/// <summary>
/// A signed commit over the current state of a permissioned repo
/// (<c>com.atproto.space.defs#signedCommit</c>).
/// </summary>
/// <remarks>
/// <para>A commit is a short digest a syncer can compare against its own copy without
/// re-reading the repo. Unlike a public repository's commit it is deliberately
/// <b>not</b> a rebroadcastable proof of what the author wrote.</para>
/// <para>The signature covers only the commit context — space, author, revision, and a fresh
/// random <see cref="Ikm"/> — never the digest itself. The digest is bound to that context by a
/// <em>symmetric</em> MAC keyed from the public <see cref="Ikm"/>, so a reader in the sync flow
/// gets full authenticity and integrity while anyone holding a leaked commit can compute a
/// valid <see cref="Mac"/> for any <see cref="Hash"/> they like. A leaked commit therefore
/// proves nothing about its contents — only that the author signed a
/// <c>(space, author, rev, ikm)</c> context.</para>
/// </remarks>
public sealed class SignedSpaceCommit
{
    /// <summary>The commit format version currently defined by the protocol.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Commit format version, currently <see cref="CurrentVersion"/>. Corresponds to the
    /// version carried in the <c>atproto-space-v1</c> context protocol tag.
    /// </summary>
    [JsonPropertyName("ver")]
    public required int Ver { get; init; }

    /// <summary>The <c>sha256</c> digest of the repo's <see cref="LtHash"/> state (32 bytes).</summary>
    [JsonPropertyName("hash")]
    [JsonConverter(typeof(LexBytesJsonConverter))]
    public required byte[] Hash { get; init; }

    /// <summary>
    /// Per-signature input keying material: 32 random bytes, freshly generated for each reader
    /// a commit is served to.
    /// </summary>
    [JsonPropertyName("ikm")]
    [JsonConverter(typeof(LexBytesJsonConverter))]
    public required byte[] Ikm { get; init; }

    /// <summary>
    /// The author's signature over the commit context. Does <b>not</b> cover
    /// <see cref="Hash"/> — see the remarks on <see cref="SignedSpaceCommit"/>.
    /// </summary>
    [JsonPropertyName("sig")]
    [JsonConverter(typeof(LexBytesJsonConverter))]
    public required byte[] Sig { get; init; }

    /// <summary>
    /// <c>HMAC-SHA256</c> over <see cref="Hash"/>, keyed by <c>HKDF-Expand</c> of
    /// <see cref="Ikm"/> with the context as <c>info</c>. Binds the digest to this commit's context.
    /// </summary>
    [JsonPropertyName("mac")]
    [JsonConverter(typeof(LexBytesJsonConverter))]
    public required byte[] Mac { get; init; }

    /// <summary>The commit revision (a TID), also bound into the context.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>
    /// Encodes the commit as a DAG-CBOR block, the form it takes as the first root of a
    /// serialized repo.
    /// </summary>
    public byte[] ToDagCbor() =>
        DagCborEncoder.Encode(JsonSerializer.SerializeToElement(this, SpaceJson.Options));

    /// <summary>
    /// Decodes a commit from its DAG-CBOR block.
    /// </summary>
    /// <param name="dagCbor">The encoded block.</param>
    /// <exception cref="SpaceRepoVerificationException">Thrown when the block is not a well-formed commit.</exception>
    public static SignedSpaceCommit FromDagCbor(ReadOnlyMemory<byte> dagCbor)
    {
        try
        {
            var element = DagCborDecoder.Decode(dagCbor);
            return JsonSerializer.Deserialize<SignedSpaceCommit>(element, SpaceJson.Options)
                ?? throw new SpaceRepoVerificationException("Commit block decoded to null.");
        }
        catch (Exception ex) when (ex is not SpaceRepoVerificationException and not OutOfMemoryException)
        {
            // Everything the CBOR reader, the DRISL rules, and the JSON binder can throw for
            // malformed input lands here as one exception type the caller can act on.
            throw new SpaceRepoVerificationException($"Invalid signed commit block: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// The context a commit's signature and MAC are both domain-separated by:
/// the space, the author, and the revision.
/// </summary>
/// <param name="Space">The space URI the repo belongs to.</param>
/// <param name="Author">The DID of the account whose repo it is.</param>
/// <param name="Rev">The commit revision (a TID).</param>
public readonly record struct SpaceCommitContext(string Space, string Author, string Rev)
{
    /// <summary>Builds a context from a typed space URI.</summary>
    /// <param name="space">The space.</param>
    /// <param name="author">The DID of the repo's account.</param>
    /// <param name="rev">The commit revision.</param>
    public SpaceCommitContext(SpaceUri space, string author, string rev)
        : this(space?.Value ?? throw new ArgumentNullException(nameof(space)), author, rev)
    {
    }

    /// <summary>The fixed protocol tag that opens every encoded context.</summary>
    public const string ProtocolTag = "atproto-space-v1";

    /// <summary>
    /// Encodes the context for signing and for MAC derivation.
    /// </summary>
    /// <param name="ikm">The commit's per-signature nonce.</param>
    /// <remarks>
    /// <para>The encoding is the fixed protocol tag followed by each variable field prefixed
    /// with its big-endian <see cref="ushort"/> length, the variable-length-vector convention
    /// from TLS 1.3 §3.4:</para>
    /// <code>
    /// ctx = "atproto-space-v1"
    ///    || uint16be(len(space))  || space
    ///    || uint16be(len(author)) || author
    ///    || uint16be(len(rev))    || rev
    ///    || uint16be(len(ikm))    || ikm
    /// </code>
    /// <para>These length prefixes are big-endian, deliberately the opposite byte order from
    /// the little-endian lanes of <see cref="LtHash"/>. The two come from different specs and
    /// each keeps its native order.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when a field is longer than a <see cref="ushort"/> can prefix.</exception>
    public byte[] Encode(ReadOnlySpan<byte> ikm)
    {
        var spaceBytes = Encoding.UTF8.GetBytes(Space ?? string.Empty);
        var authorBytes = Encoding.UTF8.GetBytes(Author ?? string.Empty);
        var revBytes = Encoding.UTF8.GetBytes(Rev ?? string.Empty);

        var tag = Encoding.UTF8.GetBytes(ProtocolTag);
        var size = tag.Length + 8 + spaceBytes.Length + authorBytes.Length + revBytes.Length + ikm.Length;

        var buffer = new byte[size];
        var offset = 0;

        tag.CopyTo(buffer, offset);
        offset += tag.Length;

        WriteField(buffer, ref offset, spaceBytes);
        WriteField(buffer, ref offset, authorBytes);
        WriteField(buffer, ref offset, revBytes);
        WriteField(buffer, ref offset, ikm);

        return buffer;
    }

    private static void WriteField(Span<byte> buffer, ref int offset, ReadOnlySpan<byte> field)
    {
        if (field.Length > ushort.MaxValue)
            throw new ArgumentException("Commit context field exceeds its uint16 length prefix.", nameof(field));

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)field.Length);
        offset += 2;
        field.CopyTo(buffer[offset..]);
        offset += field.Length;
    }
}

/// <summary>
/// A single operation in a permissioned repo's operation log, as applied to a set hash.
/// </summary>
/// <param name="Collection">The record collection NSID.</param>
/// <param name="Rkey">The record key.</param>
/// <param name="Cid">The record's new CID, or <see langword="null"/> for a delete.</param>
/// <param name="Prev">The record's previous CID, or <see langword="null"/> for a create.</param>
public readonly record struct SpaceRepoOp(string Collection, string Rkey, string? Cid, string? Prev);

/// <summary>
/// The running <see cref="LtHash"/> over a permissioned repo's records, and the commit that
/// summarizes it.
/// </summary>
/// <remarks>
/// <para>A repo host maintains one of these per hosted repo and updates it on every write. A
/// syncer maintains its own alongside its copy of the repo; when the two digests agree, the
/// syncer's copy is exactly current. That comparison — not the receipt of every individual
/// operation — is what makes permissioned sync self-healing: a missed write shows up as a
/// mismatch on the next sync and is repaired by falling back to full-state recovery.</para>
/// </remarks>
public sealed class SpaceRepoCommit
{
    /// <summary>The underlying set hash. Mutating it directly changes what this commit describes.</summary>
    public LtHash SetHash { get; }

    /// <summary>Creates a commit over an empty repo.</summary>
    public SpaceRepoCommit() : this(new LtHash())
    {
    }

    /// <summary>Creates a commit over an existing set hash, which is taken by reference.</summary>
    /// <param name="setHash">The set hash to wrap.</param>
    public SpaceRepoCommit(LtHash setHash)
    {
        ArgumentNullException.ThrowIfNull(setHash);
        SetHash = setHash;
    }

    /// <summary>Restores a commit from a persisted <see cref="LtHash"/> state.</summary>
    /// <param name="state">The state, or an empty span for an empty repo.</param>
    public static SpaceRepoCommit FromState(ReadOnlySpan<byte> state) => new(new LtHash(state));

    /// <summary>Folds a set of records into a fresh commit.</summary>
    /// <param name="records">The records currently in the repo.</param>
    public static SpaceRepoCommit FromRecords(IEnumerable<(string Collection, string Rkey, string Cid)> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var commit = new SpaceRepoCommit();
        foreach (var (collection, rkey, cid) in records)
            commit.Add(collection, rkey, cid);
        return commit;
    }

    /// <summary>
    /// Folds a repo index — <c>{collection}/{rkey}</c> to record CID — into a fresh commit, so
    /// the index can be checked against a signed commit without reading a single record.
    /// </summary>
    /// <param name="index">The index, as carried by the second root of a serialized repo.</param>
    public static SpaceRepoCommit FromIndex(IEnumerable<KeyValuePair<string, string>> index)
    {
        ArgumentNullException.ThrowIfNull(index);

        var commit = new SpaceRepoCommit();
        foreach (var (path, cid) in index)
            commit.SetHash.Add($"{path}/{cid}");
        return commit;
    }

    /// <summary>The element a record contributes to the set hash.</summary>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">The record's CID.</param>
    public static string SetHashElement(string collection, string rkey, string cid) =>
        $"{collection}/{rkey}/{cid}";

    /// <summary>Adds a record to the repo's contents.</summary>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">The record's CID.</param>
    /// <returns>This instance, for chaining.</returns>
    public SpaceRepoCommit Add(string collection, string rkey, string cid)
    {
        SetHash.Add(SetHashElement(collection, rkey, cid));
        return this;
    }

    /// <summary>Removes a record from the repo's contents.</summary>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">The CID the record had.</param>
    /// <returns>This instance, for chaining.</returns>
    public SpaceRepoCommit Remove(string collection, string rkey, string cid)
    {
        SetHash.Remove(SetHashElement(collection, rkey, cid));
        return this;
    }

    /// <summary>
    /// Applies one operation-log entry: removes the previous version if there was one, adds the
    /// new version unless the operation was a delete.
    /// </summary>
    /// <param name="op">The operation.</param>
    /// <returns>This instance, for chaining.</returns>
    public SpaceRepoCommit ApplyOp(SpaceRepoOp op)
    {
        if (op.Prev is not null)
            Remove(op.Collection, op.Rkey, op.Prev);
        if (op.Cid is not null)
            Add(op.Collection, op.Rkey, op.Cid);
        return this;
    }

    /// <summary>Applies a sequence of operation-log entries in order.</summary>
    /// <param name="ops">The operations.</param>
    /// <returns>This instance, for chaining.</returns>
    public SpaceRepoCommit ApplyOps(IEnumerable<SpaceRepoOp> ops)
    {
        ArgumentNullException.ThrowIfNull(ops);

        foreach (var op in ops)
            ApplyOp(op);
        return this;
    }

    /// <summary>
    /// The 32-byte digest of the repo's current contents — what a commit carries as its
    /// <see cref="SignedSpaceCommit.Hash"/>.
    /// </summary>
    public byte[] Digest() => SetHash.Digest();

    /// <summary>
    /// Whether this repo's contents match a signed commit's digest.
    /// </summary>
    /// <param name="commit">The commit to compare against.</param>
    /// <remarks>
    /// Verify the commit with <see cref="SpaceCommitVerifier"/> first — on its own this says
    /// nothing about authenticity, only that two digests agree.
    /// </remarks>
    public bool Matches(SignedSpaceCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return CryptographicOperations.FixedTimeEquals(SetHash.Digest(), commit.Hash);
    }

    /// <summary>
    /// Signs a commit over the repo's current contents.
    /// </summary>
    /// <param name="context">The commit context. Its <see cref="SpaceCommitContext.Rev"/> becomes the commit's revision.</param>
    /// <param name="signingKey">The author's AT Protocol signing key.</param>
    /// <remarks>
    /// A fresh <see cref="SignedSpaceCommit.Ikm"/> is generated on every call, so each reader
    /// receives a distinct commit for the same repo state. That is intentional: it is what keeps
    /// a commit from becoming a transferable proof of what its author wrote.
    /// </remarks>
    public SignedSpaceCommit Sign(SpaceCommitContext context, AtProtoKey signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);

        var hash = SetHash.Digest();
        var ikm = RandomNumberGenerator.GetBytes(32);
        var encodedContext = context.Encode(ikm);

        return new SignedSpaceCommit
        {
            Ver = SignedSpaceCommit.CurrentVersion,
            Hash = hash,
            Ikm = ikm,
            Sig = signingKey.Sign(encodedContext),
            Mac = SpaceCommitVerifier.ComputeMac(ikm, encodedContext, hash),
            Rev = context.Rev,
        };
    }
}

/// <summary>
/// Verifies a permissioned repo's signed commit.
/// </summary>
public static class SpaceCommitVerifier
{
    /// <summary>
    /// Verifies a commit's signature (authenticity) and MAC (integrity).
    /// </summary>
    /// <param name="commit">The commit to verify.</param>
    /// <param name="context">The context the commit should have been signed over.</param>
    /// <param name="didKey">The author's signing key as a <c>did:key</c> string.</param>
    /// <returns><see langword="true"/> when the commit is authentic and intact.</returns>
    /// <remarks>
    /// Once this passes, <see cref="SignedSpaceCommit.Hash"/> is trusted as the author's claim
    /// about their repo — which is what makes <see cref="SpaceRepoCommit.Matches"/> meaningful.
    /// </remarks>
    public static bool Verify(SignedSpaceCommit commit, SpaceCommitContext context, string didKey)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(didKey);

        if (commit.Ver != SignedSpaceCommit.CurrentVersion)
            return false;
        if (!string.Equals(commit.Rev, context.Rev, StringComparison.Ordinal))
            return false;

        var encodedContext = context.Encode(commit.Ikm);

        // Integrity before authenticity: the MAC is cheap and the signature is not.
        var mac = ComputeMac(commit.Ikm, encodedContext, commit.Hash);
        if (!CryptographicOperations.FixedTimeEquals(mac, commit.Mac))
            return false;

        try
        {
            return AtProtoCrypto.VerifySignature(didKey, encodedContext, commit.Sig);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Computes a commit's MAC: <c>HMAC-SHA256(HKDF-Expand(ikm, ctx, 32), hash)</c>.
    /// </summary>
    /// <param name="ikm">The commit's per-signature nonce, used directly as the pseudorandom key.</param>
    /// <param name="encodedContext">The encoded commit context, used as HKDF <c>info</c>.</param>
    /// <param name="hash">The repo digest being bound.</param>
    /// <remarks>
    /// There is no HKDF extract step: <paramref name="ikm"/> is already uniformly random, so it
    /// serves as the PRK for <c>HKDF-Expand</c> (RFC 5869 §2.3) directly.
    /// </remarks>
    public static byte[] ComputeMac(
        ReadOnlySpan<byte> ikm, ReadOnlySpan<byte> encodedContext, ReadOnlySpan<byte> hash)
    {
        Span<byte> key = stackalloc byte[32];
        HKDF.Expand(HashAlgorithmName.SHA256, ikm, key, encodedContext);
        return HMACSHA256.HashData(key, hash);
    }
}

/// <summary>
/// Thrown when a serialized permissioned repo, or a commit within it, fails verification.
/// </summary>
public sealed class SpaceRepoVerificationException : Exception
{
    /// <summary>Creates a new exception with the given message.</summary>
    /// <param name="message">A description of what failed to verify.</param>
    public SpaceRepoVerificationException(string message) : base(message)
    {
    }

    /// <summary>Creates a new exception with the given message and cause.</summary>
    /// <param name="message">A description of what failed to verify.</param>
    /// <param name="innerException">The underlying cause.</param>
    public SpaceRepoVerificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// JSON options for permissioned-space structures that round-trip through DAG-CBOR.
/// </summary>
internal static class SpaceJson
{
    internal static JsonSerializerOptions Options { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
