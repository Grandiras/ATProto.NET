# Repositories

`ATProtoNet.Repo` holds what an atproto repository is made of: the Merkle Search Tree (MST) that
maps record paths to record CIDs, the signed commit over its root, record proofs, and verified
snapshots of a whole repository. None of it does I/O; blocks come and go through `IBlockStore`.
See the [repository specification](https://atproto.com/specs/repository).

## The tree

A `MerkleSearchTree` maps `MstKey`s (`collection/rkey`) to CIDs. It is persistent: an edit returns
a new tree and the old one stays usable, sharing every node the edit did not touch. `Root`
re-hashes only the nodes changed since it was last computed, so a commit costs a few node
encodings whatever the size of the repository. The shape, and so the root, depends only on the
entries, never on the order they were added in.

```csharp
using ATProtoNet;
using ATProtoNet.Repo;

var post = MstKey.For(Nsid.Parse("app.bsky.feed.post"), RecordKey.Parse("3jzfcijpj2z2a"));
var record = Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm");

var empty = MerkleSearchTree.Empty();
var tree = empty.Insert(post, record);
Cid root = tree.Root;
Cid? found = tree.Get(post);                    // null when absent
var restored = tree.Delete(post);               // restored.Root == empty.Root
```

`Insert` refuses a key the tree has, `Update` and `Delete` a key it lacks
(`InvalidOperationException`). `Apply(key, value, out previous)` sets or removes a key whatever it
held, which is how a firehose consumer inverts a commit. `Enumerate` walks the entries in key
order, optionally from a key on.

Keys that are not an NSID and a record key are still valid MST keys, as in the reference
implementation; `MstKey.TryGetRecordPath` tells them apart.

## Blocks and partial trees

`MerkleSearchTree.Load(root, blocks)` reads nodes from an `IBlockStore` as operations reach them,
checking each one (key order, ranges, layers, prefix lengths) before it is used. A subtree whose
block the store lacks stays known by its CID only: the tree is partial, and an operation that needs
that subtree throws `MstIncompleteException`. `LoadComplete` loads and checks every node, and
also that the blocks are the tree's canonical encoding.

```csharp continued
var blocks = new MemoryBlockStore();
tree.WriteBlocks(blocks);
var loaded = MerkleSearchTree.LoadComplete(tree.Root, blocks);

var proof = new MemoryBlockStore();
tree.WriteCoveringProof([post], proof);         // the root and the paths to the key and its neighbours
var diff = new MemoryBlockStore();
tree.WriteDiffBlocks(empty, diff);              // what a #commit event carries
```

`MemoryBlockStore` keeps blocks in memory; a database or a CAR on disk can stand behind the same
interface.

## Commits and writing

A `RepoCommit` is the version-3 commit: the repository DID, a TID revision, the tree root and an
optional previous commit. Signing gives a `SignedCommit`, which keeps its exact block.
`SignedCommit.Read` reads one strictly, and `Verify` checks the signature over the received bytes
with the `sig` entry cut out, never over a re-encoding.

`RepoWriter` applies a batch of record writes and signs it as one commit, returning the commit,
the changed keys and the blocks a `#commit` event carries:

```csharp continued
using ATProtoNet.Crypto;
using ATProtoNet.Data;

using var key = SigningKey.Generate(KeyCurve.K256);
var store = new MemoryBlockStore();
var writer = new RepoWriter(Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz"), key, store);

var body = new DataModelMap([new("$type", new DataModelString("app.bsky.feed.post")), new("text", new DataModelString("hi"))]).ToDrisl();
var result = writer.Commit([new RecordWrite(post, body)], Tid.Next());
SignedCommit head = result.Commit;
```

## Proofs and snapshots

`RecordProof.Verify` checks a `com.atproto.sync.getRecord` CAR: every block against its CID, the
commit's repository and signature, and the path from the root to the record. It proves absence
too: `VerifiedRecord.Exists` is false when the path shows there is no such record. A proof that
does not reach the key fails as `Incomplete`; it never reports absence.

`RepoSnapshot.Verify` and `VerifyAsync` read a `com.atproto.sync.getRepo` CAR into a verified
commit, a complete tree and its records, bounded by `RepoLimits` while reading. `Entries` lists
every tree key; `Records` those that are record paths, with their raw blocks.

```csharp continued
var car = new MemoryStream();
RecordProof.Write(head, writer.Tree, [post], store, car);
var verified = RecordProof.Verify(car.ToArray(), head.Commit.Did, Nsid.Parse("app.bsky.feed.post"),
    RecordKey.Parse("3jzfcijpj2z2a"), key.PublicKey);
bool exists = verified.Exists;
```

The verification key is always the caller's: the account's `#atproto` key from a DID document the
caller resolved, never one supplied by the host that served the data. Every failure is a
`RepoVerificationException` whose `Error` names the check that failed.
