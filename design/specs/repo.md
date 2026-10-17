# Repository: MST, commits, proofs

| | |
|---|---|
| **Status** | Draft |
| **Package** | `ATProtoNet.Repo` (namespace `ATProtoNet.Repo`) |
| **Phase** | 2 |
| **Depends on** | [syntax](syntax.md), [data-model](data-model.md) (DRISL-CBOR, `Cid`, CAR), [crypto](crypto.md) (`SigningKey`, `VerificationKey`, `SignedObject`) |
| **Normative references** | [Repository](https://atproto.com/specs/repository) · [Sync](https://atproto.com/specs/sync) · [Data model](https://atproto.com/specs/data-model) · reference implementation `@atproto/repo` 0.10.14 (`mst.ts`, `verifyProofs`, `getCoveringProof`) · indigo `atproto/repo/mst` |

## Scope

**In:** the Merkle Search Tree (key rules, layers, node encoding, loading untrusted trees,
editing, partial trees with unloaded subtrees, inversion), version-3 commits (encoding, signing,
the signed-bytes rule, verification), record proofs (generation and verification, including
proofs of absence), repository snapshots (`getRepo` CAR read into a verified commit, tree and
records), the `IBlockStore` seam, commit diffs (the blocks a `#commit` event carries).

**Out:** CAR framing and bounds ([data-model](data-model.md)), signatures and keys
([crypto](crypto.md)), Sync 1.1 chain rules, `prevData` inductive checks over a stream and resync
([streaming](streaming.md); they *use* this package's inversion and proof walk), fetching a CAR
from a host ([identity](identity.md) untrusted fetch, [streaming](streaming.md) fetchers), space
repositories (LtHash set hashes, [spaces](spaces.md)). This package does no I/O.

## Public API sketch

```csharp
namespace ATProtoNet.Repo;

// Block storage the tree reads from and writes to. Content-addressed: the CID is the key.
public interface IBlockStore
{
    bool TryGet(Cid cid, out ReadOnlyMemory<byte> block);
}
public interface IWritableBlockStore : IBlockStore
{
    void Put(Cid cid, ReadOnlyMemory<byte> block);   // caller guarantees cid matches block
}
public sealed class MemoryBlockStore : IWritableBlockStore
{
    public static MemoryBlockStore FromCar(CarReader car);   // blocks already CID-checked by the reader
    public int Count { get; }
}

public readonly record struct MstKey   // "collection/rkey", validated (REP-1..REP-3)
{
    public static MstKey Parse(string value);
    public static bool TryParse(ReadOnlySpan<char> value, out MstKey key);
    public static MstKey For(Nsid collection, RecordKey rkey);
    public bool TryGetRecordPath(out Nsid collection, out RecordKey rkey);  // false for valid MST keys that are not NSID/rkey
    public int Layer { get; }                                              // leading zero 2-bit pairs of SHA-256
}

// The one MST. A tree may be fully loaded, or partial: subtrees not present in the block store
// stay CID stubs until an operation needs them.
public sealed class MerkleSearchTree
{
    public static MerkleSearchTree Empty();
    public static MerkleSearchTree Build(IEnumerable<KeyValuePair<MstKey, Cid>> entries);
    public static MerkleSearchTree Load(Cid root, IBlockStore blocks, MstLoadOptions? options = null); // lazy, validates what it touches
    public static MerkleSearchTree LoadComplete(Cid root, IBlockStore blocks);                           // walks and validates every node

    public Cid Root { get; }                                   // incremental: only dirty nodes are re-hashed
    public Cid? Get(MstKey key);                               // throws MstIncompleteException if the path is stubbed
    public MerkleSearchTree Insert(MstKey key, Cid value);     // returns the new tree; the old one is unchanged
    public MerkleSearchTree Update(MstKey key, Cid value);
    public MerkleSearchTree Delete(MstKey key);
    public MstEdit Apply(MstKey key, Cid? value, out Cid? previous); // upsert/remove primitive used by inversion

    public IEnumerable<KeyValuePair<MstKey, Cid>> Enumerate(MstKey? from = null);   // ordered; throws if a stub is reached
    public void WriteBlocks(IWritableBlockStore destination);                       // every node, canonical encoding
    public void WriteCoveringProof(IEnumerable<MstKey> keys, IWritableBlockStore destination);
    public void WriteDiffBlocks(MerkleSearchTree previous, IWritableBlockStore destination); // new nodes + proofs for changed keys
}

public sealed record RepoCommit
{
    public const int Version = 3;
    public required Did Did { get; init; }
    public required Tid Rev { get; init; }
    public required Cid Data { get; init; }
    public Cid? Prev { get; init; }                            // always encoded, null when absent (REP-24)
    public SignedCommit Sign(SigningKey key);
}

public sealed class SignedCommit
{
    public RepoCommit Commit { get; }
    public ReadOnlyMemory<byte> Signature { get; }
    public ReadOnlyMemory<byte> Block { get; }                 // exact bytes, as received or as written
    public Cid Cid { get; }
    public static SignedCommit Read(ReadOnlyMemory<byte> block);            // strict; REP-26..REP-31
    public bool Verify(VerificationKey key);                                 // low-S only (crypto CRY rules)
}

public static class RecordProof
{
    public static VerifiedRecord Verify(ReadOnlySpan<byte> car, Did did, Nsid collection, RecordKey rkey, VerificationKey key);
    public static void Write(SignedCommit commit, MerkleSearchTree tree, IEnumerable<MstKey> keys,
                             IBlockStore records, Stream destination);       // CAR: commit root, proof nodes, record blocks
}
public sealed record VerifiedRecord(Did Did, Tid Rev, Cid Commit, Nsid Collection, RecordKey Rkey, Cid? Cid, ReadOnlyMemory<byte>? Block)
{
    public bool Exists => Cid is not null;
}

public sealed class RepoSnapshot
{
    public static RepoSnapshot Verify(ReadOnlySpan<byte> car, Did did, VerificationKey key, RepoLimits? limits = null);
    public static ValueTask<RepoSnapshot> VerifyAsync(Stream car, Did did, VerificationKey key, RepoLimits? limits = null, CancellationToken ct = default);
    public SignedCommit Commit { get; }
    public MerkleSearchTree Tree { get; }
    public IBlockStore Blocks { get; }
    public IEnumerable<RepoSnapshotEntry> Entries { get; }     // every MST key, ordered
    public IEnumerable<RepoSnapshotRecord> Records { get; }    // entries whose key is a valid NSID/rkey
}

public sealed record RepoLimits
{
    public long MaxCarBytes { get; init; } = 256L * 1024 * 1024;
    public int MaxBlockCount { get; init; } = 2_000_000;
    public int MaxRecordBytes { get; init; } = 1_000_000;
}

public sealed class RepoVerificationException : AtProtoException
{
    public RepoVerificationError Error { get; }   // MalformedCar, MissingBlock, CidMismatch, WrongRepository,
                                                 // BadSignature, MalformedCommit, UnsupportedVersion, MalformedTree,
                                                 // Incomplete, LimitExceeded
}
public sealed class MstIncompleteException : Exception;  // an operation reached a stub
```

The sketch replaces 0.8's two trees (`MerkleSearchTree`, which rebuilds and re-hashes the whole
tree on every `ComputeRootCid`/`Serialize`, and the internal `PartialMerkleSearchTree`) with one
node-based tree grown from `PartialMerkleSearchTree`'s algorithms, and replaces 0.8's three CID
representations (`byte[]`, string-keyed block dictionaries, `Cid`) with `Cid` everywhere
(AG-9a).

## Requirements

### Keys and layers

- **REP-1** (MUST) An MST key is `collection/rkey`: exactly one `/`, both segments non-empty,
  characters from `A–Z a–z 0–9 _ ~ - : .`, at most 1,024 characters in total.
  *Source:* [Repository spec, MST structure](https://atproto.com/specs/repository) · 0.8
  `MerkleSearchTree.cs:239` (mirrors `isValidMstKey`) · tests
  `MstReferenceVectorTests.Add_KeyTheReferenceRejects_Throws`, `Add_KeyOfExactly1024Characters_Succeeds`
- **REP-2** (MUST) `MstKey` accepts every key the reference implementation accepts, including keys
  whose segments are not a valid NSID or record key; such keys are stored, proven and enumerated
  like any other.
  *Source:* reference `isValidMstKey` · test `MstReferenceVectorTests.Add_KeyTheReferenceAllows_Succeeds`
- **REP-3** (MUST) Keys compare by ordinal byte order of their ASCII encoding.
  *Source:* Repository spec · test `MerkleSearchTreeTests.GetEntries_ReturnsSortedOrder`
- **REP-4** (MUST) A key's layer is the number of leading zero 2-bit pairs in SHA-256 of the key
  bytes (fanout 4).
  *Source:* Repository spec · 0.8 `MstKeyDepth.cs` · tests
  `MstKeyDepthTests.ComputeDepth_InteropKeyHeights_MatchExpectedDepth`,
  `ComputeDepth_InteropExampleKeys_MatchTheHeightTheirNameStates` (interop `mst/key_heights.json`,
  `example_keys.txt`)

### Node encoding

- **REP-5** (MUST) A node encodes as the DRISL map `{e: [{k, p, t, v}], l}`: `l` is the left
  subtree link or null, each entry has `p` (shared prefix length with the previous key in the
  node, 0 for the first), `k` (key suffix bytes), `v` (value link) and `t` (right subtree link or
  null).
  *Source:* Repository spec · 0.8 `MstNodeData.cs` · tests `MstNodeDataTests.Roundtrip_*`
- **REP-6** (MUST) The writer always writes `l` and every `t`, as null when absent. The empty tree
  is the node `a2616580616cf6` with root CID `bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm`.
  *Source:* reference encoding · 0.8 fix (canonical form corrected twice) · tests
  `MstNodeDataTests.ToBytes_AbsentLinks_AreWrittenAsNull`,
  `MstReferenceVectorTests.Serialize_EmptyTree_IsTheReferenceEncodedNode`,
  `ComputeRootCid_EmptyTree_MatchesReference`
- **REP-7** (MUST) The reader accepts a node whose `l` or `t` fields are omitted, reading them as
  null, but a tree containing such nodes is not canonical (REP-15).
  *Source:* 0.8's own earlier encoding · tests `MstNodeDataTests.FromBytes_OmittedLinks_ReadAsNull`,
  `MerkleSearchTreeTests.Validate_TreeWrittenWithOmittedNullLinks_Throws`
- **REP-8** (MUST) Layers with no keys between a key and its subtree are written as entry-less
  wrapper nodes exactly as the reference does, so the same entries always give the same root.
  *Source:* reference `mst.ts` · tests `MstReferenceVectorTests.Add_KeyThatSplitsTwoLayersDown_MatchesReferenceBothWays`,
  `Add_KeyTwoLayersAboveTheRest_MatchesReferenceBothWays`, `Delete_OnlyKeyOfTheTopLayer_TrimsTheTree`
- **REP-9** (MUST) The root CID depends only on the set of entries, never on insertion order or
  edit history.
  *Source:* Repository spec · tests `MerkleSearchTreeTests.RootCid_IsDeterministic_RegardlessOfInsertionOrder`,
  `AddThenDelete_RestoresTheOriginalRoot`, `RandomEdits_AlwaysMatchTheBulkBuildOfTheFinalEntries`

### Loading untrusted trees

Every check below runs on the node as it is read, before its contents are used.

- **REP-10** (MUST) Nodes are read with the strict DRISL reader (definite lengths, no duplicate
  keys, canonical integers, only tag 42). Unknown node fields are skipped.
  *Source:* [data-model](data-model.md) strict mode · 0.8 `MstNodeData.cs:141-185` (definite lengths, #134)
- **REP-11** (MUST) An entry's prefix length is checked against the previous key's length *before*
  it is used to slice or allocate: a negative value, a value over `UInt64`, or a value above the
  previous key's length is malformed. A prefix equal to the previous key's length is valid.
  *Source:* indigo SEC-4 (a prefix of `1L<<40` once drove a terabyte preallocation) · 0.8
  `MstNodeData.cs:209` · tests `MerkleSearchTreeTests.Deserialize_FirstEntryPrefixOutOfRange_ThrowsFormatException`,
  `Deserialize_PrefixLongerThanThePreviousKey_ThrowsFormatException`,
  `Deserialize_PrefixEqualToThePreviousKey_IsAccepted`, `Deserialize_PrefixBeyondUInt64_ThrowsFormatException`
- **REP-12** (MUST) Every reconstructed key satisfies REP-1 (valid UTF-8/ASCII, valid characters),
  keys within a node are strictly increasing, and every key lies strictly inside the range its
  parent leaves for that subtree.
  *Source:* 0.8 `MstNodeData.cs:104-110`, `MerkleSearchTree.cs:437` · tests
  `Deserialize_InvalidKey_*`, `Deserialize_InvalidUtf8Key_*`, `Deserialize_KeysOutOfOrder_*`,
  `Deserialize_SubtreeKeysOutsideTheirRange_*`
- **REP-13** (MUST) All keys in one node have the same layer, and a child node sits exactly one
  layer below its parent; a node linking below layer 0 is malformed.
  *Source:* 0.8 `PartialMerkleSearchTree.cs:110-149` · tests
  `PartialMerkleSearchTreeTests.Load_ChildOnTheWrongLayer_ThrowsFormatException`,
  `RecordProofTests.Verify_NodeMixingLayers_Throws`, `Verify_ChildOnItsParentsLayer_Throws`
- **REP-14** (MUST) A non-root node with no entries and no left link is malformed; a root holding
  only a link and no keys is malformed; two subtree links in a row are malformed; the same node
  referenced twice in one tree is malformed; depth is capped at `MaxTreeDepth`.
  *Source:* 0.8 `MerkleSearchTree.cs:440-452`, `PartialMerkleSearchTree.cs:125-131,610` · tests
  `Deserialize_EmptyInnerNode_*`, `Load_RootWithOnlyALink_*`, `Deserialize_SubtreeLinkedTwice_*`,
  `Load_SameNodeReferencedTwice_*`, `Deserialize_ChainDeeperThanAnyRealTree_*`
- **REP-15** (MUST) `LoadComplete` also checks canonical form: re-encoding the loaded tree yields
  the same root CID. A tree with an extra wrapper layer or omitted null links fails.
  *Source:* 0.8 `MerkleSearchTree.Validate` (`:219-232`) · tests
  `Validate_LoadedTreeWithAnExtraWrapperLayer_Throws`, `Validate_TreeWrittenWithOmittedNullLinks_Throws`
- **REP-16** (MUST) A missing block is reported as `Incomplete` for lazy loads and proofs (the
  operation needed a node the store does not have), and as `MissingBlock` for `LoadComplete` and
  snapshots.
  *Source:* 0.8 `PartialTreeException` vs `FormatException` split · tests
  `PartialMerkleSearchTreeTests.Remove_KeyUnderAMissingSubtree_ThrowsPartialTreeException`,
  `Deserialize_MissingBlock_ThrowsFormatException`

### Editing

- **REP-17** (MUST) `Insert` of an existing key, and `Update`/`Delete` of a missing key, throw
  `InvalidOperationException`. `Apply` is the upsert/remove primitive and reports the previous
  value instead. Inserting the value a key already has changes nothing (same root, no dirty nodes).
  *Source:* 0.8 `MerkleSearchTree.Add/Update/Delete`, `PartialMerkleSearchTree.Insert/Remove` ·
  tests `Add_DuplicateKey_Throws`, `Update_MissingKey_Throws`, `Delete_MissingKey_Throws`,
  `Insert_SameValue_ChangesNothing`
- **REP-18** (MUST) Trees are persistent: an edit returns a new tree sharing unchanged nodes and
  leaves the original usable. Root computation re-encodes and re-hashes only nodes changed since
  the last computation.
  *Source:* **Decision**: 0.8's full-tree rebuild is O(n) per commit (primitives audit, problem 9)
- **REP-19** (MUST) Editing a partial tree loads only the nodes on the edited paths; untouched
  subtrees stay stubs and keep their CIDs. An edit that needs an absent node throws
  `MstIncompleteException`.
  *Source:* 0.8 `PartialMerkleSearchTree` · tests `Load_UntouchedSubtrees_StayCidReferencesAndKeepTheRoot`,
  `Remove_KeyUnderAMissingSubtree_ThrowsPartialTreeException`
- **REP-20** (MUST) Inverting a set of operations on a partial tree built from a commit's proof
  blocks (create → delete, update → restore previous value, delete → re-insert previous value)
  yields the root before the commit; deleting the last key yields the empty-tree root.
  *Source:* [Sync spec, commit validation](https://atproto.com/specs/sync) · tests
  `PartialMerkleSearchTreeTests.Invert_CommitProofFixture_LandsOnTheRootBeforeTheCommit` (interop
  `firehose/commit-proof-fixtures.json`), `Invert_RandomCommits_*`, `Invert_DeletingTheLastKey_LandsOnTheEmptyTree`
- **REP-21** (MUST) `WriteCoveringProof` writes exactly the nodes the reference
  `getCoveringProof` returns for the keys: the path to each key plus the paths to its left and
  right neighbours, so the proof also proves absence. The root is always included.
  *Source:* reference `getCoveringProof` · tests
  `MstReferenceVectorTests.SerializeProof_GeneratedTree_MatchesReferenceCoveringProofExactly`,
  `MerkleSearchTreeTests.SerializeProof_*`
- **REP-22** (MUST) `WriteDiffBlocks` writes every node of the new tree that is not in the
  previous tree, plus the covering proof (in the new tree) of every key whose value changed, so
  that a consumer holding only these blocks and the previous root can invert the commit (REP-20).
  *Source:* Sync spec (`#commit` `blocks`) · test `RepoWritePathTests.CommitDiff_ProofBlocksCarryEverythingTheChangedPathsNeed`

### Commits

- **REP-23** (MUST) A commit is the DRISL map `{did, version, data, rev, prev, sig}` with
  `version = 3`, `did` a DID, `rev` a TID, `data` the MST root link and `sig` the signature bytes.
  *Source:* Repository spec (commit objects) · 0.8 `RepoCommit.cs`
- **REP-24** (MUST) The writer always includes `prev`, as null when absent (v3 semantics), and
  encodes it as a CID link when set.
  *Source:* Repository spec · 0.8 `RepoCommit.cs:40` · test `RepoCommitTests.Sign_WithPrev_EncodesPrevAsCidLink`
- **REP-25** (MUST) Signing encodes the commit without `sig` as canonical DRISL, signs SHA-256 of
  those bytes with a low-S signature (via `SignedObject`, [crypto](crypto.md)), then encodes the
  map with `sig` added. The commit CID is the CID of the signed block.
  *Source:* Repository spec · tests `RepoCommitTests.Sign_SignatureIsOverTheUnsignedEncoding`,
  `Sign_PopulatesCidFromTheSignedBytes`, `EncodeUnsigned_IsDeterministic`
- **REP-26** (MUST) Verification reconstructs the signed bytes from the *received* block by
  removing the `sig` pair and shortening the map header by one entry, byte for byte. It never
  re-encodes the decoded commit.
  *Source:* 0.8 `CommitBlock.cs:60-147` (integer widths, key order and link shapes must be the
  signer's) · tests `CommitBlockTests.Read_SplicesOutTheSigPairAndKeepsEveryOtherByte`,
  `RepoCommitTests.Sign_TheVerifiersSplicedViewMatchesTheUnsignedEncoding`
- **REP-27** (MUST) The commit block reader is strict: a definite-length map, no duplicate keys
  (which could show the verifier a different field than the one signed), and no bytes after the
  map (the block's CID covers them but the signature does not, so they would let anyone mint new
  commit CIDs for one signed commit).
  *Source:* 0.8 `CommitBlock.cs:60-70,119`
- **REP-28** (MUST) A commit block is malformed unless it has `did`, `rev`, `data`, `version` and a
  non-empty byte-string `sig`; `data` is a valid CID link; `prev`, when present, is null or a valid
  CID link; `version` is 3. Unknown fields are skipped (and covered by the signature).
  *Source:* 0.8 `CommitBlock.cs:127-137` · tests `CommitBlockTests.Read_NotAWellFormedSignedCommit_ThrowsFormatException`
  (table, incl. row "data not a CID"), `Read_NullPrev_IsAccepted`; #180
- **REP-29** (MUST) `did` must parse as a DID and `rev` as a TID; otherwise the commit is malformed.
  *Source:* **Decision**: 0.8 kept them as strings (`RepoCommit.cs:25,34`) against the typed-identifier
  policy; the Sync verifier already required a TID rev
- **REP-30** (MUST) A malformed link inside a signed commit is a verification failure
  (`MalformedCommit`), never an unhandled exception.
  *Source:* #180 · test `VerifySyncAsync_SignedCommitWhoseDataIsNotACid_IsInvalidRatherThanThrowing`
- **REP-31** (MUST) `SignedCommit.Verify` accepts only low-S 64-byte `r‖s` signatures from the
  given key ([crypto](crypto.md)); a commit re-signed with another key fails for the account's key.
  *Source:* Cryptography spec · tests `RepoCommitTests.Sign_CommitDoesNotVerifyWithADifferentKey`,
  `RecordProofTests.Verify_CommitResignedByAnotherKey_ThrowsForTheAccountsKey`

### Record proofs

- **REP-32** (MUST) A record proof is a CAR with exactly one root (the signed commit), the MST
  nodes on the covering path, and the record block when the record exists.
  *Source:* Sync spec (`com.atproto.sync.getRecord`) · tests `RecordProofTests.Verify_TwoRoots_Throws`,
  `Verify_NotACar_Throws`
- **REP-33** (MUST) Verification checks, in order: every block matches its CID (the CAR reader's
  check); the root block is a well-formed commit (REP-27..REP-30); its `did` equals the expected
  DID; its signature verifies with the caller's key; then walks from `data` to the key applying
  REP-10..REP-14 to each node on the path.
  *Source:* reference `verifyProofs` · 0.8 `RecordProof.cs` · tests `Verify_AnotherRepository_Throws`,
  `Verify_AnotherSigningKey_Throws`, `Verify_SignatureAltered_Throws`, `Verify_NodeKeysOutOfOrder_Throws`,
  `Verify_SubtreeKeyOutsideItsRange_Throws`, `Verify_WellFormedHandBuiltTree_Verifies`
- **REP-34** (MUST) When the walk ends at the key, the result carries the record's CID and its
  block, which must be present and be a DRISL map; a record block swapped for another (even one
  carrying its own matching CID) fails because the MST value names the original.
  *Source:* tests `Verify_ReferenceProof_ReturnsTheRecord` (vector `record-proof.json`),
  `Verify_RecordBytesAltered_Throws`, `Verify_RecordSwappedForAnotherWithItsOwnCid_Throws`
- **REP-35** (MUST) When the walk shows the key cannot be in the tree (it falls between two
  adjacent keys of a fully present path), the result is a verified absence (`Exists == false`).
  A path that passes the key by does not prove that key present; a proof of one record also
  proves its immediate neighbours absent when they are.
  *Source:* reference `verifyProofs` · tests `Verify_ReferenceAbsenceProof_ProvesTheRecordAbsent`,
  `Verify_AbsenceProof_DoesNotProveTheRecordItPassesBy`, `Verify_ProofOfARecord_AlsoProvesItsNeighbourAbsent`,
  `Verify_EmptyRepository_ProvesEveryRecordAbsent`
- **REP-36** (MUST) A walk that needs a node the CAR does not contain fails with `Incomplete`; it
  never reports absence.
  *Source:* tests `Verify_KeyOffTheProvenPath_ThrowsIncomplete`, `Verify_TreeNodeMissing_Throws`,
  `Verify_CommitBlockMissing_Throws`
- **REP-37** (MUST) The verification key is a `VerificationKey` supplied by the caller (the
  account's `#atproto` key from a DID document the caller resolved itself, never from the host
  that served the proof). A string that is not a `did:key` is rejected when the caller parses it,
  not inside verification.
  *Source:* 0.8 `RecordProof.cs:22-24` · test `Verify_SigningKeyNotADidKey_ThrowsFormatException`
  (moves to `DidKey.Parse`, [crypto](crypto.md))
- **REP-38** (MUST) Proofs written by `RecordProof.Write` verify with `RecordProof.Verify` for
  every key written, present or absent.
  *Source:* test `Verify_ProofsOfAnSdkWrittenRepo_ProveEachRecordAndAbsence`

### Repository snapshots

- **REP-39** (MUST) `RepoSnapshot.Verify` reads a `getRepo` CAR: CID-checks every block, checks
  the single root is a well-formed commit for the expected DID, verifies its signature, loads the
  complete tree (`LoadComplete`, so REP-15 canonical form applies), and checks every record block
  the tree references is present.
  *Source:* 0.8 `RepoSnapshot.cs:13-20,110` · tests `RepoSnapshotTests.Verify_*`
- **REP-40** (MUST) `Entries` exposes every MST key, and `Records` exposes the entries whose key is
  a valid NSID and record key. Entries that are not record paths are never dropped silently from
  `Entries`.
  *Source:* **Decision**: 0.8 `RepoSnapshot.Records` silently drops them (`RepoSnapshot.cs:212`)
- **REP-41** (MUST) Snapshot reading enforces `RepoLimits` (total bytes, block count, record block
  size) while reading, before allocating for a block. The stream overload reads incrementally and
  never buffers more than `MaxCarBytes`.
  *Source:* **Decision**: 0.8 `CarReader.FromStreamAsync` has no limit and callers each picked one
  (`RepoResync.cs:45`, Spaces 256 MiB)
- **REP-42** (SHOULD) The snapshot decodes a record body only on request (`RepoSnapshotRecord`
  exposes the CID and raw block; typed decoding goes through the Lexicon contracts, D-10).
  *Source:* D-10

### Block store

- **REP-43** (MUST) `IBlockStore` is keyed by `Cid` only. Implementations never return a block that
  does not hash to the requested CID; `MemoryBlockStore.FromCar` relies on the CAR reader's CID
  check and keeps the first block of a repeated CID.
  *Source:* 0.8 `CarReader.FindBlock` (first block wins) · AG-9a
- **REP-44** (MUST) The tree, proofs and snapshots read blocks only through `IBlockStore`, so a
  caller can back them with a database or a CAR on disk.
  *Source:* **Decision** (primitives audit recommendation 4)

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `MstKey.MaxLength` | 1,024 characters | REP-1 |
| `MerkleSearchTree.MaxTreeDepth` | 64 layers | REP-14 (0.8 `MaxTreeDepth = 64`) |
| `MerkleSearchTree.MaxEntriesPerNode` | 1,024 | malformed beyond (proposed; see open points) |
| `RepoLimits.MaxCarBytes` (default) | 256 MiB | REP-41 (matches 0.8 resync and Spaces bounds) |
| `RepoLimits.MaxBlockCount` (default) | 2,000,000 | REP-41 (proposed) |
| `RepoLimits.MaxRecordBytes` (default) | 1,000,000 bytes | REP-41 (Sync 1.1 record bound, 0.8 `RepoSyncVerifier`) |
| CAR header, varint and block bounds | see [data-model](data-model.md) | all CAR input |
| DRISL nesting depth | see [data-model](data-model.md) | node and commit decoding |

## Errors

| Failure | Result |
|---|---|
| Invalid key passed by the caller | `FormatException` (`MstKey.Parse`), `ArgumentException` |
| Duplicate insert, update/delete of missing key | `InvalidOperationException` |
| Operation reaches a stub | `MstIncompleteException` |
| Malformed node, commit or tree in untrusted input | `RepoVerificationException` (`MalformedTree`, `MalformedCommit`, `UnsupportedVersion`) |
| Block missing / wrong CID / wrong repo / bad signature / limit | `RepoVerificationException` with the matching `RepoVerificationError` |
| Proof does not cover the key | `RepoVerificationException(Incomplete)` |

`RepoVerificationException` derives from `AtProtoException` (AG-19). No `FormatException` or
`CborContentException` escapes a verification entry point.

## Conformance and tests

**Vectors** (conformance suite, [04 — Testing](../04-testing.md)): interop `mst/key_heights.json`,
`mst/example_keys.txt`, `mst/common_prefix.json`; `firehose/commit-proof-fixtures.json`;
`record-proof.json` (generated by `@atproto/repo` 0.10.14, with its `.mjs` generator); the
`mst.test.ts` vectors and generated reference trees currently pasted into
`MstReferenceVectorTests`; the canonical empty node `a2616580616cf6`. Adapter operations:
`MstRoot`, `VerifyRecordProof`, `VerifyCommit`, plus `InvertCommit` and `CoveringProof`.

**0.8 scenarios to port:** `MerkleSearchTreeTests`, `MstNodeDataTests`,
`PartialMerkleSearchTreeTests`, `MstReferenceVectorTests`, `MstKeyDepthTests`, `CommitBlockTests`,
`RepoCommitTests`, `RecordProofTests`, `RepoSnapshotTests`, `RepoWritePathTests`. The
hostile-input cases (REP-11..REP-14, REP-27) join the regression corpus.

## Changes from 0.8

| Change | Reason |
|---|---|
| One persistent, incrementally hashed MST instead of a rebuild-everything tree plus an internal partial tree | O(n) re-hash per commit; three copies of the layer/range checks (`MerkleSearchTree.Load`, `PartialMerkleSearchTree.LoadNode`, `RecordProof.FindInTree`) |
| `Cid` everywhere; blocks keyed by `Cid` through `IBlockStore` | 0.8 used `byte[]`, string-keyed dictionaries and `Cid` side by side, converting per node (`RepoSnapshot.cs:147`) |
| `RepoCommit.Did`/`Rev` typed; non-TID `rev` is malformed | typed-identifier policy |
| `RepoSnapshot.Entries` keeps non-record MST keys | 0.8 dropped them silently |
| Snapshot reading is bounded and can stream | 0.8 read whole buffers with no limit in `FromStreamAsync` |
| Verification takes a `VerificationKey`, not a `did:key` string | AG-9a; parsing errors surface at the call site |
| Signing goes through `SignedObject` | one implementation of "encode without `sig`, sign, verify" (0.8 had five) |

## Open points

1. **Per-node entry cap.** The reference has none; a hostile node with millions of entries is
   bounded only by block size. Proposed: reject nodes over 1,024 entries (far above any real
   fanout-4 node) and record the choice here.
2. **Snapshot defaults.** `MaxBlockCount = 2,000,000` is a proposal; confirm against the largest
   real repositories during phase 2 and against what `ATProtoNet.Streaming` resync needs.
3. **Record body decoding in snapshots** (REP-42): expose raw blocks only, or also a
   `DataModelValue` view? Proposed: raw block plus `DecodeAs<T>()` via the Lexicon contracts.
4. **Writable repository helper.** 0.8 has no PDS-side "apply writes and produce a commit + diff"
   type beyond `RepoWritePathTests`. Proposed: a small `RepoWriter` (tree + block store + signer →
   `SignedCommit` + diff blocks) in phase 2, since Spaces and test fixtures need it.
