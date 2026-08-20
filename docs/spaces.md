# Spaces (Permissioned Data)

AT Protocol is not one protocol but several. The one everyone knows is **public broadcast**: users
publish signed, redistributable records into a repository, applications crawl those repositories,
and authority rests in the DID that published a record. **Permissioned data** is the second one. It
keeps the same shape — DID-based authority, per-user repositories, Lexicon-typed records,
applications crawling hosts to build views — and adds an access perimeter around it: a **space**.

Spaces serve the modalities public broadcast cannot: personal data (bookmarks, drafts, mutes),
gated content (subscriber-only posts), socially shared data (private posts, stories), and groups
(private forums, communities, group chats).

> **Alpha.** This implements [proposal 0016, *Permissioned Data*](https://github.com/bluesky-social/proposals/tree/main/0016-permissioned-data),
> announced in [AT Protocol Spaces (alpha)](https://atproto.com/blog/atproto-spaces-alpha). It is a
> proposal, not a final specification — terminology and wire details are expected to change, and it
> has not had a security review. Do not put production data behind it yet.

> **Access control, not confidentiality.** Permissioned data is **not end-to-end encrypted**. Every
> service that handles it — the PDS, and every authorized application — can read it, which is what
> makes server-side search, indexing, notifications, aggregation, and moderation possible at all.
> E2EE is a separate concern an application may layer on top.

## How it differs from public broadcast

| | Public broadcast | Permissioned data |
| --- | --- | --- |
| Repo scope | One repo per user | One permissioned repo per (user, space) |
| Record authority | User DID | User DID |
| URI authority | User DID | **Space authority** DID |
| Commit | Merkle Search Tree root | `LtHash` set-hash digest |
| Signature | Rebroadcastable, archival | **Deniable** on rebroadcast |
| Access | Public | Gated by a space credential |
| Distribution | Relay firehose | No relay — applications pull from each host |

## Addressing

A space is identified by three values, and a permissioned record by six:

```
Space:  at://{authority}/space/{spaceType}/{skey}
Record: at://{authority}/space/{spaceType}/{skey}/{author}/{collection}/{rkey}
```

```csharp
var space = SpaceUri.Parse("at://did:plc:abc123/space/com.atmoboards.forum/default");

space.Authority;   // did:plc:abc123 — the DID that gates access
space.SpaceType;   // com.atmoboards.forum — the modality
space.Skey;        // default

var record = space.Record(authorDid, "com.atmoboards.thread", "3l6oveex3ii2l");
record.Path;       // com.atmoboards.thread/3l6oveex3ii2l
```

Permissioned data reuses the `at://` scheme rather than defining its own. The literal `space`
segment sits where a collection NSID appears in a public AT-URI, and the two can never be
confused — a collection NSID always contains at least two dots and `space` contains none. Use
`SpaceUri.IsSpaceUri(value)` to tell them apart.

Authority splits in two here, which is the one structural difference from a public AT-URI. The
URI's authority is the **space** authority (which gates access); the record's authority remains the
**author** DID that wrote and signed it.

Neither may be a handle: a space's identity and its membership are keyed on DIDs.

## Reading and writing your own data

The simplest case needs no credential machinery at all. A personal-data space is anchored on the
user's own DID, so their PDS is both the space host and the repo host, and OAuth is enough.

```csharp
// Create a space on the user's own PDS. simplespace is the space-management
// implementation every PDS is required to support.
var created = await client.SimpleSpace.CreateSpaceAsync("com.example.bookmarks", skey: "self");
var space = created.ToSpaceUri();

await client.Space.CreateRecordAsync(
    space, client.Did!, "com.example.bookmark",
    new { @type = "com.example.bookmark", url = "https://atproto.com", createdAt = DateTime.UtcNow });

await foreach (var record in client.Space.EnumerateRecordsAsync(space, client.Did!))
    Console.WriteLine(record.Path);

// Spaces the user has written data to — note: written to, not "is a member of".
var spaces = await client.Space.ListSpacesAsync(type: "com.example.bookmarks");
```

Batch writes land under a single revision, which is how a syncer recognises them as one atomic
change:

```csharp
await client.Space.ApplyWritesAsync(space, client.Did!,
[
    new SpaceCreateOp { Collection = "com.example.bookmark", Value = first },
    new SpaceUpdateOp { Collection = "com.example.bookmark", Rkey = "abc", Value = second },
    new SpaceDeleteOp { Collection = "com.example.bookmark", Rkey = "def" },
]);
```

Blobs are **not** uploaded through this namespace. A space record references a blob uploaded with
`com.atproto.repo.uploadBlob`, so a client writing blob-bearing records needs a `blob:` permission
alongside its `space:` one. Reading them back is `client.Space.GetBlobAsync(...)`, and
`ListBlobsAsync` enumerates what a repo references in one space — `com.atproto.sync.listBlobs` never
will, because it is unauthenticated.

## OAuth scopes

Access is granted by space **type**, which is the consent boundary:

```csharp
var scope = AtProtoScopes.Combine(
    AtProtoScopes.AtProto,
    AtProtoScopes.Space("com.example.bookmarks"),                        // the user's own bookmarks
    AtProtoScopes.Space("com.atmoboards.forum", authority: "*"));        // forums anchored anywhere
```

`authority` defaults to `self`, so a bare grant covers only the user's **own** spaces of that type.
Reaching a forum or group anchored on someone else requires naming that authority, or `"*"`.

| Parameter | Default | Meaning |
| --- | --- | --- |
| `spaceType` | *(required)* | The type NSID, or `*` for any type |
| `authority` | `self` | The authority DID, `self`, or `*` |
| `skey` | `*` | A space key, or `*` |
| `collections` | the type's declared `collections` | What the write actions may target |
| `actions` | `SpaceAction.All` | `ReadSelf`, `Read`, `Create`, `Update`, `Delete` |
| `manage` | none | `Create`, `Update`, `Delete` — on the *spaces*, not the records |

Read access is **all-or-nothing at the space boundary**. There is no partial, per-record,
per-collection, or per-author read grant, so `Read` and `ReadSelf` ignore the collection list.

The distinction between the two read grants matters:

- `SpaceAction.Read` confers the read and sync methods **and** `getDelegationToken` — which is what
  an application exchanges for the credential that reads *every* member's repo.
- `SpaceAction.ReadSelf` confers the same methods for the holder's **own** repo only, and **not**
  `getDelegationToken`. An application holding only this cannot reach the rest of the space — the
  right grant for a personal export or backup tool.

`SpaceAction.ReadSelf` is the narrowest record grant there is. `SpaceAction.None` throws: an omitted
action list means the full default set, and the grammar has no marker for an empty one, so a
manage-only grant that touches no records at all cannot be expressed.

```csharp
// Administer the user's forums without being able to read other members' records.
AtProtoScopes.Space("com.atmoboards.forum", authority: "*",
    actions: SpaceAction.ReadSelf,
    manage: SpaceManage.Update | SpaceManage.Delete);
// → space:com.atmoboards.forum?authority=*&action=read_self&manage=update&manage=delete
```

The default collection set is resolved from the space type's declaration **as it stands when the
grant is evaluated**, not frozen at consent time. If the declaration later adds a collection,
existing bare grants widen to include it. Enumerate collections explicitly if you do not want that.

## Space type declarations

A space type NSID resolves to a Lexicon definition with `"type": "space"`. It names the modality —
so every space is some specific kind of space rather than a generic container — and supplies the
human-readable name a consent screen shows in place of the raw NSID.

```csharp
var declaration = SpaceTypeDeclaration.FromLexicon(lexiconJson)!;

declaration.Name;             // "AtmoBoards Forum" — shown on consent screens
declaration.GetName("es");    // "Foro AtmoBoards"
declaration.Collections;      // the default collection set for a bare space: scope
```

`Collections` is a recommendation, not a constraint. Any collection may be written to any space; the
protocol does not restrict it.

If you publish your own space type, `atproto-lexgen` generates the declaration in both directions —
a static holder from the Lexicon JSON, the Lexicon JSON back from the holder, and a diff that calls
out a widened collection set. See
[Space type declarations](lexicon-codegen.md#space-type-declarations).

## Reading someone else's data: the credential flow

Reading *another member's* repo needs a **space credential** issued by the space authority. Getting
one turns on two independent axes:

- **Which user** is being acted for — a **delegation token** minted by that user's PDS.
- **Which application** is acting — a **client attestation** signed by the application itself,
  required only when the space gates on app identity.

They are presented together but signed by different parties and evaluated independently.

```
┌──────┐        ┌────────────┐        ┌─────────────┐        ┌─────────────────┐
│ User │        │ User's PDS │        │ Application │        │ Space Authority │
└───┬──┘        └──────┬─────┘        └──────┬──────┘        └────────┬────────┘
    ├─ OAuth consent ──►                     │                        │
    │                  ├──── OAuth token ────►                        │
    │                  ◄─ getDelegationToken ┤                        │
    │                  ├─ delegation token ──►  getSpaceCredential    │
    │                  │                     ├─(token + DPoP ────────►│
    │                  │                     │   [+ attestation])     │
    │                  │                     ◄─── space credential ───┤
```

`SpaceCredentialProvider` runs all of it:

```csharp
await using var provider = new SpaceCredentialProvider(client);

var space = SpaceUri.Parse("at://did:plc:abc123/space/com.atmoboards.forum/default");

// The writer set: accounts that have written at least one record into the space.
await foreach (var writer in client.Space.EnumerateReposAsync(space))
{
    using var reader = await provider.CreateReaderForRepoAsync(space, writer.Did);

    await foreach (var record in reader.Space.EnumerateRecordsAsync(space, writer.Did))
        Console.WriteLine($"{writer.Did}: {record.Path}");
}
```

A credential is **not a bearer token**. It reads a whole space and is presented to every repo host
in it, so as a bearer token it would be a shared secret — a host given one in order to serve its own
repo could replay it against every other host in the space. It is bound at issuance to a key the
requester holds, and every request carries a [DPoP](https://www.rfc-editor.org/rfc/rfc9449) proof
signed by that key and naming the host it is addressed to. The provider generates a fresh keypair
per credential and discards it when the credential expires.

An application serving many users of a space does **not** need a credential per user. It may obtain
one using any single user's session and fan the data out from its own copy — which is also what
keeps the number of distinct syncers per repo low enough for PDSes to serve without a relay in
between. It loses access only when it loses every OAuth session for the space.

Whether a space requires a client attestation is not advertised. The provider asks without one first
and retries with one only if the authority refuses on app grounds, so configuring a factory costs
nothing against spaces that do not need it:

```csharp
var provider = new SpaceCredentialProvider(client, new SpaceCredentialOptions
{
    ClientAttestationFactory = (audience, ct) => Task.FromResult(
        SpaceTokens.Create(
            SpaceTokenType.ClientAttestation,
            issuer: clientId, subject: clientId, signingKey: clientKey, audience: audience)),
});
```

## Commits: why they prove nothing

Each permissioned repo is summarized by a short signed **commit**, so a syncer can check whether its
copy matches the source without transferring it. Unlike a public repository's commit it is
deliberately not a rebroadcastable proof of what its author wrote.

The digest is an [LtHash](https://eprint.iacr.org/2019/227) **set hash** — a homomorphic, lattice-
based (and so quantum-secure) hash over `{collection}/{rkey}/{cid}` for every record the repo holds.
Adding or removing a record is one cheap addition or subtraction rather than a recomputation, and
because both operations commute, the digest depends only on the *set* of records, never the order
they were written.

The signature then covers **only** the commit context — space, author, revision, and 32 fresh random
bytes — never the digest. The digest is bound to that context by a *symmetric* MAC keyed from those
public random bytes. A reader in the sync flow gets full authenticity and integrity; anyone holding
a leaked commit can compute a valid MAC for any digest they like, so it proves nothing about the
repo's contents.

```csharp
var repo = SpaceRepoCommit.FromRecords(records);
var commit = repo.Sign(new SpaceCommitContext(space, authorDid, rev), signingKey);

// A reader verifies authenticity and integrity, then compares digests.
if (SpaceCommitVerifier.Verify(commit, context, authorDidKey) && repo.Matches(commit))
    // the local copy is exactly current
```

A fresh nonce is generated on every `Sign`, so two commits over identical state differ — that is
intentional, and is what keeps a commit from becoming a stable fingerprint.

## Sync

There is **no relay** for permissioned data. Permissioned repos are non-rebroadcastable by
construction, so no intermediary can collate a firehose of them; an application pulls directly from
each repo host and keeps its own copy current.

Implement `ISpaceRepoStore` over whatever store you already have, and `SpaceSyncer` drives the
protocol:

```csharp
var syncer = new SpaceSyncer(space, myStore, SpaceSyncer.ResolveSigningKeyAsync(didResolver));

var cursor = new SpaceRepoCursor(writerDid, savedRev, savedState);
using var reader = await provider.CreateReaderForRepoAsync(space, writerDid);

var result = await syncer.SyncRepoAsync(reader.Space, cursor);

switch (result.Outcome)
{
    case SpaceSyncOutcome.UpToDate:  break;                       // digests agree
    case SpaceSyncOutcome.Partial:   break;                       // call again to continue
    case SpaceSyncOutcome.Recovered: break;                       // rebuilt from a full download
    case SpaceSyncOutcome.NoRepo:    break;                       // nothing there to sync
}

Persist(cursor.Rev, cursor.GetState());   // resume here after a restart
```

A pass walks the repo's **operation log** (`listRepoOps`) from the cursor's revision, applying each
entry to the store and to a running set hash. When a response reaches the head of the log it also
carries the repo's current signed commit — and comparing digests is what says whether the copy is
exactly current.

That comparison, rather than having received every individual operation, is what makes the whole
thing **self-healing**. A dropped write, a compacted oplog, a locally corrupted copy: all three show
up the same way, as a digest mismatch on the next pass, and all three are repaired the same way, by
falling back to `getRepo` and rebuilding.

The oplog is a transport optimization, not a committed data structure. A host may compact or drop
it, and it does not survive account migration — a `since` the host can no longer serve is an
expected condition, not an error, and the syncer recovers in full automatically.

`getRepo` returns a CAR with **two roots**: the signed commit, then a DAG-CBOR index mapping
`{collection}/{rkey}` to each record's CID, with the record blocks following in the same canonical
order. That layout is what lets `SpaceRepoCar.Verify` validate the whole thing in one pass —
verifying the commit makes its digest trustworthy, folding the index into a set hash authenticates
every path/CID pair *without reading a single record*, and each block is then checked against a CID
the index already vouched for.

For a copy that has diverged only slightly, `listRecords` with `excludeValues: true` plus
`getLatestCommit` is cheaper than a whole download: diff the listing against what you hold and fetch
only the differences.

### Discovering the writer set

`listRepos` on the space host returns the accounts that hold data in a space. It is the **sync
boundary**, not an access-control list: it enumerates accounts that have *written at least one
record*, never the broader set allowed to write, and never readers — the protocol does not enumerate
readers at all.

It is also only what the authority *claims*, kept current by the write notifications it has
received. A listed account's repo host is the source of truth. Because each entry carries that
repo's current revision, a periodic sweep can compare revisions and re-sync only what advanced.

### Write notifications

Rather than polling, a syncer registers for notifications:

```csharp
await reader.Space.RegisterNotifyAsync(space, "did:web:syncer.example.com#atproto_space_syncer");
```

Notifications carry no record data — only that a repo reached a new revision and hash — and are
**best-effort**. A dropped one is not a lost write: the repo is caught up by a later notification, or
by the periodic sweep above. They are the latency optimization; the sweep is the correctness
guarantee.

## Managing a space with `simplespace`

The protocol deliberately does not specify how spaces are created or how an authority decides who
may read one. Those belong to a *space-management implementation* sitting above the protocol.
`com.atproto.simplespace` is the baseline every PDS must support, so an application can build
against it without standing up a bespoke space service.

```csharp
var created = await client.SimpleSpace.CreateSpaceAsync(
    "com.atmoboards.forum",
    skey: "default",
    policy: new MemberListPolicy(),
    appAccess: new OpenAppAccess());

await client.SimpleSpace.AddMemberAsync(created.Uri, "did:plc:member");
await foreach (var member in client.SimpleSpace.EnumerateMembersAsync(created.Uri))
    Console.WriteLine(member.Did);
```

A user must be authorized by the **user policy** *and* their app by the **app access policy** for a
credential to be minted.

| User policy | Behaviour |
| --- | --- |
| `MemberListPolicy` | *(default)* Authorize users on the space's member list |
| `PublicPolicy` | Authorize any requester |
| `ManagingAppPolicy` | Ask the managing app per request, via `checkUserAccess` |

`ManagingAppPolicy` is what enables dynamic policies — follower-gating, paid subscriptions, join
approvals — without an app maintaining an explicit list.

| App access | Behaviour |
| --- | --- |
| `OpenAppAccess` | *(default)* Any app; no client attestation required, so public clients work |
| `AllowListAppAccess` | Only the named client IDs, evaluated against the **attested** `client_id` |

The member list is host-internal state consulted at mint time. It is not a synced protocol structure
and is never enumerated to the network — `listRepos` returns writers, not readers.

Removing a member stops the authority minting *new* credentials for them; one already issued stays
valid until it expires, and records they wrote remain their own data in their own repo.

## Space deletion

An authority deleting a space stops issuing credentials and deletes its own repo in it. Registered
syncers are notified over the same best-effort path as writes, and should drop every copy they hold,
including derived state.

A syncer that misses the notification learns on its next credential renewal, which answers
`SpaceDeleted`:

```csharp
try
{
    await provider.GetCredentialAsync(space, forceRenew: true);
}
catch (SpaceCredentialException ex) when (ex.Error == SpaceErrors.SpaceDeleted)
{
    DropEverythingFor(space);
}
// A renewal that fails for any other reason says nothing about the space — keep the copy.
```

Other members' repos are **not** deleted. A member's records are the member's own data, and deleting
the space does not entitle the authority to destroy them — they simply become unreadable to everyone
but the member's own account.

## Identity and account events

An account's participation in permissioned data is tied to the same DID and signing key as its
public identity, so migration, key rotation, deactivation, and deletion all behave the same way.

One consequence is easy to miss: those changes are announced on the **public** firehose, as
`#identity` and `#account` events. An application syncing only permissioned data still needs
`com.atproto.sync.subscribeRepos` to learn that a key rotated — otherwise it will keep verifying
commits against a stale key. See [Firehose Streaming](firehose.md).

Migration also needs `listSpaces` and `listBlobs`: an account has one permissioned repo *per space*
rather than a single repository, and each carries its own blobs.

## Moderation

A moderation service is just another reader. To label content in a space it must hold a space
credential like anything else, which means the authority admitted it under the same rules.

It should **not** publish public labels for permissioned records — that leaks metadata about
otherwise-private data — so `com.atproto.label.subscribeLabels` is a poor fit. Labels belong inside
the same access boundary as the content they describe, published as records in a permissioned repo
within the space.

An authority has one lever with no public analogue: because reading requires a credential it issues,
it can cut off a reader by declining to issue one.

## API surface

| Type | Purpose |
| --- | --- |
| `SpaceUri`, `SpaceRecordUri` | Parse and build permissioned `at://` URIs |
| `AtProtoClient.Space` | `com.atproto.space.*` — records, sync, credentials, notifications |
| `AtProtoClient.SimpleSpace` | `com.atproto.simplespace.*` — space management |
| `SpaceCredentialProvider`, `SpaceReader` | The credential exchange, caching, and per-host readers |
| `SpaceTokens`, `SpaceToken` | Delegation tokens, credentials, client attestations |
| `SpaceSyncer`, `SpaceRepoCursor`, `ISpaceRepoStore` | Incremental sync and full-state recovery |
| `LtHash`, `SpaceRepoCommit`, `SpaceCommitVerifier` | The set hash and the commit construction |
| `SpaceRepoCar`, `VerifiedSpaceRepo` | Serialize and verify a repo's CAR form |
| `SpaceAuthority` | Resolve `#atproto_space` / `#atproto_space_host` from a DID document |
| `SpaceTypeDeclaration` | The `"type": "space"` Lexicon definition |
| `AtProtoScopes.Space` | Build `space:` OAuth scopes |

## See also

- [Testing Against a Real Space Host](testing-spaces.md) — how to run the space integration tests against a permissioned-data PDS
- [OAuth Authentication](oauth.md) — the DPoP, PAR, and PKCE flow the credential exchange builds on
- [Low-Level Repo API](low-level-repo.md) — CAR files and DAG-CBOR, shared with public repositories
- [Cryptography](crypto.md) — key generation, signing, and `did:key` encoding
- [Firehose Streaming](firehose.md) — the `#identity` and `#account` events permissioned syncers need
