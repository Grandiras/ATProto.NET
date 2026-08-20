using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.Repo;

namespace ATProtoNet.Spaces;

/// <summary>
/// One record in a serialized permissioned repo: its path, its CID, and its DAG-CBOR bytes.
/// </summary>
/// <param name="Collection">The record collection NSID.</param>
/// <param name="Rkey">The record key.</param>
/// <param name="Cid">The record's CID.</param>
/// <param name="Bytes">The record encoded as DAG-CBOR.</param>
public readonly record struct SpaceRepoRecord(string Collection, string Rkey, string Cid, byte[] Bytes)
{
    /// <summary>The record's path within the repo, <c>{collection}/{rkey}</c>.</summary>
    public string Path => $"{Collection}/{Rkey}";

    /// <summary>
    /// Encodes a record value as DAG-CBOR and computes its CID.
    /// </summary>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="value">The record value, in the AT Protocol JSON data model.</param>
    public static SpaceRepoRecord Create(string collection, string rkey, JsonElement value)
    {
        var (bytes, cid) = DagCborEncoder.EncodeWithCid(value);
        return new SpaceRepoRecord(collection, rkey, cid.Value, bytes);
    }
}

/// <summary>
/// A permissioned repo decoded from its serialized CAR form, with everything already verified.
/// </summary>
/// <param name="Commit">The signed commit, verified against the author's signing key.</param>
/// <param name="Index">
/// The repo index: <c>{collection}/{rkey}</c> to record CID, in the order the CAR carried it.
/// Authenticated against <paramref name="Commit"/> without reading a single record.
/// </param>
/// <param name="Records">
/// The records, each checked against its own CID and its index entry. Empty when the CAR was
/// requested with <c>excludeValues</c>.
/// </param>
public sealed record VerifiedSpaceRepo(
    SignedSpaceCommit Commit,
    IReadOnlyList<KeyValuePair<string, string>> Index,
    IReadOnlyList<SpaceRepoRecord> Records);

/// <summary>
/// Serializes and verifies a permissioned repo in its CAR form, as served by
/// <c>com.atproto.space.getRepo</c>.
/// </summary>
/// <remarks>
/// <para>The CAR declares <b>two</b> roots, in order: the signed commit, then a DAG-CBOR index
/// mapping <c>{collection}/{rkey}</c> to each record's CID. The record blocks follow, in the
/// same canonical map-key order the index used.</para>
/// <para>That layout is what lets a consumer validate the whole thing as a stream. Verifying
/// the commit makes its digest trustworthy; folding the index's entries into a set hash and
/// comparing against that digest authenticates every path/CID pair without reading a record;
/// and each record block is then checked against the CID the index already vouched for.</para>
/// </remarks>
public static class SpaceRepoCar
{
    /// <summary>
    /// Serializes a permissioned repo as a CAR file.
    /// </summary>
    /// <param name="commit">The signed commit over the repo's current contents.</param>
    /// <param name="records">The records the repo holds. Order does not matter; they are sorted canonically.</param>
    /// <param name="excludeValues">
    /// When <see langword="true"/>, writes only the two roots. The index still authenticates
    /// against the commit, since the set hash is folded from the index's entries rather than
    /// from the record blocks — so a syncer can diff it against a local copy and fetch only
    /// what it lacks.
    /// </param>
    /// <remarks>Blobs are never included; they are fetched separately via <c>getBlob</c>.</remarks>
    public static byte[] Serialize(
        SignedSpaceCommit commit,
        IEnumerable<SpaceRepoRecord> records,
        bool excludeValues = false)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(records);

        // Last write for a path wins, matching the map semantics of the index itself.
        var byPath = new Dictionary<string, SpaceRepoRecord>(StringComparer.Ordinal);
        foreach (var record in records)
            byPath[record.Path] = record;

        // A consumer walks the index in the order the DAG-CBOR encoder emitted its keys, so
        // the blocks have to follow that same order.
        var paths = byPath.Keys.ToList();
        paths.Sort(DagCborEncoder.CompareCanonical);

        var index = new JsonObject();
        foreach (var path in paths)
            index[path] = new JsonObject { ["$link"] = byPath[path].Cid };

        var commitBytes = commit.ToDagCbor();
        var indexBytes = DagCborEncoder.Encode(JsonSerializer.SerializeToElement(index));

        var commitCid = CidComputation.ComputeBinaryForDagCbor(commitBytes);
        var indexCid = CidComputation.ComputeBinaryForDagCbor(indexBytes);

        var blocks = new List<CarBlock>(excludeValues ? 2 : paths.Count + 2)
        {
            new(commitCid, commitBytes),
            new(indexCid, indexBytes),
        };

        if (!excludeValues)
        {
            foreach (var path in paths)
            {
                var record = byPath[path];
                blocks.Add(new CarBlock(CidComputation.DecodeCidString(record.Cid), record.Bytes));
            }
        }

        return CarWriter.Write([commitCid, indexCid], blocks);
    }

    /// <summary>
    /// Verifies a serialized permissioned repo and decodes its contents.
    /// </summary>
    /// <param name="car">The CAR bytes, as returned by <c>com.atproto.space.getRepo</c>.</param>
    /// <param name="space">The space the repo belongs to.</param>
    /// <param name="author">The DID of the account whose repo this is.</param>
    /// <param name="didKey">The author's signing key as a <c>did:key</c> string.</param>
    /// <param name="expectValues">
    /// Whether record blocks are expected. Pass <see langword="false"/> for an index-only CAR
    /// fetched with <c>excludeValues</c>; a CAR carrying no blocks is otherwise treated as
    /// missing every record its index names.
    /// </param>
    /// <exception cref="SpaceRepoVerificationException">Thrown at the first thing that does not check out.</exception>
    public static VerifiedSpaceRepo Verify(
        ReadOnlySpan<byte> car,
        SpaceUri space,
        string author,
        string didKey,
        bool expectValues = true)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        ArgumentException.ThrowIfNullOrWhiteSpace(didKey);

        CarReader reader;
        try
        {
            reader = CarReader.FromBytes(car);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            throw new SpaceRepoVerificationException($"Could not read the repo CAR: {ex.Message}", ex);
        }

        if (reader.Roots.Count != 2)
        {
            throw new SpaceRepoVerificationException(
                $"Expected 2 CAR roots (commit, index), got {reader.Roots.Count}.");
        }

        var blocks = reader.Blocks;
        if (blocks.Count < 2)
            throw new SpaceRepoVerificationException("The repo CAR is missing its commit and index blocks.");

        // ── 1. The commit, which makes its digest trustworthy ────────────
        if (!blocks[0].Cid.AsSpan().SequenceEqual(reader.Roots[0]))
            throw new SpaceRepoVerificationException("Expected the commit block to lead the CAR.");

        var commit = SignedSpaceCommit.FromDagCbor(blocks[0].Data);
        var context = new SpaceCommitContext(space, author, commit.Rev);
        if (!SpaceCommitVerifier.Verify(commit, context, didKey))
            throw new SpaceRepoVerificationException("The repo's commit failed verification.");

        // ── 2. The index, against the now-trusted digest ─────────────────
        if (!blocks[1].Cid.AsSpan().SequenceEqual(reader.Roots[1]))
            throw new SpaceRepoVerificationException("Expected the index block to follow the commit.");

        var index = DecodeIndex(blocks[1].Data);
        if (!SpaceRepoCommit.FromIndex(index).Matches(commit))
            throw new SpaceRepoVerificationException("The repo index does not match the commit hash.");

        // ── 3. Each record, against the index entry the commit vouched for ──
        var records = new List<SpaceRepoRecord>(index.Count);
        var blockCount = blocks.Count - 2;

        if (blockCount == 0 && !expectValues)
            return new VerifiedSpaceRepo(commit, index, records);

        if (blockCount > index.Count)
            throw new SpaceRepoVerificationException("The repo CAR has more blocks than index entries.");
        if (blockCount < index.Count)
        {
            throw new SpaceRepoVerificationException(
                $"The repo CAR is missing {index.Count - blockCount} record(s) named in the index.");
        }

        for (var i = 0; i < index.Count; i++)
        {
            var (path, cid) = index[i];
            var block = blocks[i + 2];

            var expectedCid = CidComputation.DecodeCidString(cid);
            if (!block.Cid.AsSpan().SequenceEqual(expectedCid))
            {
                throw new SpaceRepoVerificationException(
                    $"Expected block {cid} at '{path}', got {CidComputation.EncodeCidToString(block.Cid)}.");
            }

            if (CarReader.VerifyBlockCid(block) == BlockCidVerification.Mismatch)
                throw new SpaceRepoVerificationException($"Block at '{path}' does not hash to its CID.");

            var separator = path.LastIndexOf('/');
            if (separator <= 0 || separator == path.Length - 1)
                throw new SpaceRepoVerificationException($"Invalid record path in the repo index: '{path}'.");

            records.Add(new SpaceRepoRecord(
                path[..separator], path[(separator + 1)..], cid, block.Data));
        }

        return new VerifiedSpaceRepo(commit, index, records);
    }

    /// <summary>
    /// Decodes the index block into path/CID pairs, preserving the CAR's own order so that the
    /// record blocks can be matched against it positionally.
    /// </summary>
    private static List<KeyValuePair<string, string>> DecodeIndex(ReadOnlyMemory<byte> indexBlock)
    {
        JsonElement element;
        try
        {
            element = DagCborDecoder.Decode(indexBlock);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new SpaceRepoVerificationException($"Invalid repo index block: {ex.Message}", ex);
        }

        if (element.ValueKind != JsonValueKind.Object)
            throw new SpaceRepoVerificationException("The repo index must be a DAG-CBOR map.");

        var index = new List<KeyValuePair<string, string>>();
        foreach (var entry in element.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object ||
                !entry.Value.TryGetProperty("$link", out var link) ||
                link.ValueKind != JsonValueKind.String)
            {
                throw new SpaceRepoVerificationException(
                    $"Repo index entry '{entry.Name}' is not a CID link.");
            }

            index.Add(new KeyValuePair<string, string>(entry.Name, link.GetString()!));
        }

        return index;
    }
}
