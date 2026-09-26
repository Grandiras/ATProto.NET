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

var record = space.Record(authorDid, Nsid.Parse("com.atmoboards.thread"), RecordKey.Parse("3l6oveex3ii2l"));
record.Path;       // com.atmoboards.thread/3l6oveex3ii2l
```

The components are typed — `Authority` and a record's `Author` are `Did`s, `SpaceType` and
`Collection` are `Nsid`s, and `Skey` and `Rkey` are `RecordKey`s — and so is every space
identifier across the API: a space is a `SpaceUri`, a record in one a `SpaceRecordUri`, a
participant a `Did`, and revisions and CIDs `Tid` and `Cid`. A string from the outside world goes
through the type's `Parse` or `TryParse` once, at the edge.

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
var created = await client.SimpleSpace.CreateSpaceAsync(
    Nsid.Parse("com.example.bookmarks"), skey: RecordKey.Parse("self"));
var space = created.Uri;

await client.Space.CreateRecordAsync(
    space, client.Did!, Nsid.Parse("com.example.bookmark"),
    new { @type = "com.example.bookmark", url = "https://atproto.com", createdAt = AtDatetime.Now() });

await foreach (var record in client.Space.EnumerateRecordsAsync(space, client.Did!))
    Console.WriteLine(record.Path);

// Spaces the user has written data to — note: written to, not "is a member of".
await foreach (var view in client.Space.EnumerateSpacesAsync(type: Nsid.Parse("com.example.bookmarks")))
    Console.WriteLine(view.Uri);
```

Batch writes land under a single revision, which is how a syncer recognises them as one atomic
change:

```csharp
var bookmark = Nsid.Parse("com.example.bookmark");

await client.Space.ApplyWritesAsync(space, client.Did!,
[
    new SpaceCreateOp { Collection = bookmark, Value = first },
    new SpaceUpdateOp { Collection = bookmark, Rkey = RecordKey.Parse("abc"), Value = second },
    new SpaceDeleteOp { Collection = bookmark, Rkey = RecordKey.Parse("def") },
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

// The writer set: accounts that have written into the space and that its authority tracks.
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
// A CachingDidResolver: every pass verifies at least one commit against its author's key.
var syncer = new SpaceSyncer(space, myStore, didResolver);

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

`Partial` means the pass has somewhere left to go, so looping on it terminates. A member who has
never written to the space has no repo state for the host to build a commit from, and `listRepoOps`
answers that with an empty page rather than refusing the read — no operations, no commit, and no
cursor. That is reported as `NoRepo`, the same answer `getRepo` gives by refusing outright, because
calling again would only produce the same empty page.

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

`listRepos` on the space host returns the accounts whose data a space's syncers should pull. It is
the **sync boundary**, not an access-control list: it enumerates accounts that have *written at least
one record* **and** that the authority admits as writers — under `simplespace`, the ones its write
policy admits. It never lists the broader set allowed to write, and never readers; the protocol does
not enumerate readers at all.

Being left out of it does not stop anyone writing to their own repo. It means the authority neither
lists those writes nor forwards their notifications, so no syncer following the space picks them
up. An authority may exclude writers for any reason, spam and abuse included; with a `public` write
policy it tracks writers who were never members.

It is also only what the authority *claims*, kept current by the write notifications it has
accepted. A listed account's repo host is the source of truth. Because each entry carries that
repo's current revision, a periodic sweep can compare revisions and re-sync only what advanced.

### Write notifications

Rather than polling, a syncer registers for notifications:

```csharp
await reader.Space.RegisterNotifyAsync(space, "did:web:syncer.example.com#atproto_space_syncer");
```

A writer's repo host tells the space's authority, and the authority forwards each notification it
accepts to every service registered with it. Notifications carry no record data — only that a repo
reached a new revision and hash — and are **best-effort**. A dropped one is not a lost write: the
repo is caught up by a later notification, or by the periodic sweep above. They are the latency
optimization; the sweep is the correctness guarantee.

## Managing a space with `simplespace`

The protocol deliberately does not specify how spaces are created or how an authority decides who
may read one. Those belong to a *space-management implementation* sitting above the protocol.
`com.atproto.simplespace` is the baseline every PDS must support, so an application can build
against it without standing up a bespoke space service.

```csharp
var created = await client.SimpleSpace.CreateSpaceAsync(
    Nsid.Parse("com.atmoboards.forum"),
    skey: RecordKey.Parse("default"),
    readPolicy: new MemberListPolicy(),
    writePolicy: new MemberListPolicy(),
    appAccess: new OpenAppAccess());

// An upsert: both flags are replaced every time.
await client.SimpleSpace.PutMemberAsync(created.Uri, Did.Parse("did:plc:member"), read: true, write: true);
await client.SimpleSpace.PutMemberAsync(created.Uri, Did.Parse("did:plc:lurker"), read: true, write: false);

await foreach (var member in client.SimpleSpace.EnumerateMembersAsync(created.Uri))
    Console.WriteLine($"{member.Did} read={member.Read} write={member.Write}");

// Each policy is replaced wholesale when supplied and left alone when not.
await client.SimpleSpace.UpdateSpaceAsync(created.Uri, writePolicy: new PublicPolicy());
```

A space has three policies, and reading and writing are governed separately:

- **Reading.** The authority mints a credential only when the user passes the **read policy** *and*
  their app passes the **app access policy**.
- **Writing.** The **write policy** decides whether the authority tracks a writer in the writer set
  and forwards its write notifications. It cannot stop anyone writing to their own repo. App access
  does not apply: the notification comes from the writer's repo host, not from an app.

The space's owner is always admitted to both, whatever the policies say.

| Read / write policy | Behaviour |
| --- | --- |
| `MemberListPolicy` | *(default for both)* Members whose `read` / `write` flag is set |
| `PublicPolicy` | Anyone |
| `ManagingAppPolicy` | Ask the managing app via `checkUserAccess`, with `access=read` or `access=write` |

`ManagingAppPolicy` is what enables dynamic policies — follower-gating, paid subscriptions, join
approvals — without an app maintaining an explicit list. The call carries the attested client ID on
a read check and none on a write check. A managing app that cannot be reached is a refusal.

| App access | Behaviour |
| --- | --- |
| `OpenAppAccess` | *(default)* Any app; no client attestation required, so public clients work |
| `AllowListAppAccess` | Only the named client IDs, evaluated against the **attested** `client_id` |

The member list is host-internal state. Each member carries two independent flags, so a member can
be read-only, write-only, both, or on the list but admitted to neither. It is not a synced protocol
structure and is never enumerated to the network. `listRepos` returns the writers the write policy
admitted, not the member list.

Removing a member, or clearing their `read` flag, stops the authority minting *new* credentials for
them. One already issued stays valid until it expires, and records they wrote remain their own data
in their own repo. Clearing the `write` flag stops the authority recording and forwarding their
writes from the next notification on.

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
commits against a stale key. Pass each `#identity` event's DID to the resolver's
`InvalidateAsync`. See [Firehose Streaming](firehose.md).

Short of that, a commit or token that fails against a cached key is retried once against a
refreshed DID document before it is refused: `SpaceSyncer` and every server-side verifier do this,
and the resolver rate-limits the refreshes so forged signatures cannot each cost a directory
request. See [Identity Resolution](did-resolution.md#caching).

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

## Serving a space

Everything above reads a space. `ATProtoNet.Server` serves one — as a **space authority**, as a
**repo host**, or as both, which is what a PDS is. The endpoints are ordinary XRPC handlers, so
`MapXrpcEndpoints()` maps them alongside an application's own.

```csharp
builder.Services
    .AddAtProtoSpaces(options =>
    {
        options.ServiceDid = Did.Parse("did:web:pds.example.com");
        options.PublicBaseUrl = "https://pds.example.com";   // what a DPoP proof's htu names
    })
    .AddSpaceAuthority<MyAuthorityStore>(credentialSigningKey)  // getSpaceCredential, listRepos, …
    .AddSimpleSpace<MySimpleSpaceStore>()                       // com.atproto.simplespace.*
    .AddSpaceRepoHost<MyRepoHost>();                            // getRecord, getRepo, listRepoOps, …

app.MapXrpcEndpoints();
```

Register only the half a service implements: a route that answers is a route that has to be
secured. `AddAtProtoSpaces()` on its own registers just the verifiers, which is all a moderation
service or a proxy needs.

The verifiers resolve DID documents through a `CachingDidResolver` of their own, registered under
`SpaceServerExtensions.DidResolverKey`. Its lifetime is much shorter than the SDK's general
default: `SpaceServerOptions.DidCache` holds a document for a hard 5 minutes (`StaleAfter` and
`ExpireAfter` both 5 minutes) and refetches it before use after that. Every document here backs a
credential check, and a cached document is how long a key its owner rotated away — perhaps because
it leaked — keeps verifying; a space server rarely follows the firehose's `#identity` events, so the
cache lifetime is what bounds that window. With the default, a rotated-out key is never accepted
more than 5 minutes after it was fetched. A token that fails against a cached key is retried once
against a refreshed document.

Raising `ExpireAfter` above `StaleAfter` opts into stale-while-revalidate: a stale document is still
served while a background fetch replaces it. That keeps the fetch off the request path, at the cost
of accepting the old key until the fetch completes — on an idle server, for up to `ExpireAfter`. Set
`UseDistributedCache` to share documents across instances.

Fetching follows the options `AddAtProtoIdentity()` registers (`AddAtProtoSpaces()` calls it): every
DID a token names is fetched under the SDK's SSRF policy; call `AddAtProtoIdentity(o => …)` first to
change the PLC directory or opt out of the policy for a local test network. A DID that does not
resolve, for whatever reason, is refused as `NotAuthorized`. The `AtProtoSpaces` named client, which
fetches client metadata and reaches managing apps and subscribers at URLs taken from DID documents,
runs on the same hardened handler: public addresses only and no redirects, under the same opt-out.
To replace the resolver (a fixture, a directory mirror),
register a keyed `IDidResolver` under `SpaceServerExtensions.DidResolverKey`. See
[Identity Resolution](did-resolution.md).

### The verifiers

Four credential classes reach a space server, and each is verified by a type that can be used
directly:

| Type | Verifies |
| --- | --- |
| `SpaceDelegationTokenVerifier` | The delegation token on `getSpaceCredential` |
| `DPoPProofValidator` | The DPoP proof on every authenticated request |
| `SpaceCredentialVerifier` | A space credential together with its proof |
| `SpaceClientAttestationVerifier` | A client attestation, against the client's published JWKS |

Three checks in there are the ones the protocol's guarantees rest on.

**A delegation token is confined to one authority.** Its `aud` must equal
`{authority}#atproto_space_host` for the authority named in the token's *own* `sub` — derived from
the token, never taken from the request. An authority handed a token minted for a different
authority therefore cannot present it there.

**A credential's signer comes from the space URI, not from the credential.** `iss` is checked
against the space's authority and the key is resolved from that authority's DID document, so
nobody but a space's own authority can mint credentials for it.

**A proof binds a credential to its holder.** The signature is verified against the proof's own
embedded `jwk`, which proves nothing on its own — anyone can embed any key — so the thumbprint is
matched against the credential's `cnf.jkt`, which is what makes it mean something. `ath` pins the
proof to the credential presented, `htm` and `htu` pin it to this request, `iat` bounds how long a
captured proof is useful, and `jti` is spent once.

That last "spent once" is `ISpaceReplayStore`, keyed on `(iss, jti, exp)`. The default is
per-process: **replace it with a shared store** if more than one instance answers for the same DID,
or a replay is only caught by the instance that saw the original. Two shared implementations ship
in `ATProtoNet.Server` — see [The stores](#the-stores) below — and the service logs a warning at
startup while the in-process default is still registered.

Single-use tokens are also bounded in how long they may claim to live. A delegation token, a
client attestation, and a service auth token are all minted to live 60 seconds, but the `exp` on
one arriving here is whatever its signer chose, so `MaxSingleUseTokenLifetime` (five minutes by
default) is the ceiling this service will accept. It bounds two things at once: how long a
captured token stays replayable, and how long its `jti` occupies the replay store — which drops
an entry only once the token it guards has expired anyway.

> `PublicBaseUrl` is not optional behind a reverse proxy. A proof names the URL it was minted for
> and the verifier compares it against the request *as received*, which behind a proxy is an
> internal `http://` name that no client could have named. Set it to the URL clients actually
> address, or apply `UseForwardedHeaders` before the endpoints run.

### The stores

The space server keeps state in three places, and each has an in-process default that a real
deployment should replace:

| Seam | Default | Durable implementations |
| --- | --- | --- |
| `ISpaceReplayStore` | `InMemorySpaceReplayStore` | `RedisSpaceReplayStore`, `EfCoreSpaceReplayStore<T>` |
| `ISimpleSpaceStore` | `InMemorySimpleSpaceStore` | `EfCoreSimpleSpaceStore<T>` |
| `ISpaceAuthorityStore` | `InMemorySpaceAuthorityStore` | `EfCoreSpaceAuthorityStore<T>` |

Two of those defaults are more than an inconvenience.

**The replay store is a correctness gap across instances.** It is what makes a delegation token, a
client attestation, and a DPoP proof single-use, and being per-process means a replay is caught
only by the instance that saw the original — two replicas behind a load balancer accept the same
delegation token twice.

**A `simplespace` member list cannot be rebuilt.** The writer set is only what an authority claims,
and any repo host's next `notifyWrite` restores it; a member list is never published to the network
at all, so losing it on a restart loses the space's access control while the space itself carries
on existing.

`AddAtProtoSpaces()` says so at startup — a warning for each of those two, an informational line
for the writer set — until a durable store is registered. Set
`SpaceServerOptions.WarnOnInMemoryStores = false` where the defaults are the intended choice, as in
a test host.

```csharp
// Redis for the replay store: SET NX is one round trip and expires with the token's own exp.
builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("redis")!));

// A relational database for the state that has to outlive the process.
builder.Services.AddDbContextFactory<SpaceDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("spaces")));

builder.Services
    .AddAtProtoSpaces(options => { /* … */ })
    .AddAtProtoRedisSpaceReplayStore()
    .AddAtProtoEfCoreSpaceAuthority<SpaceDbContext>(credentialSigningKey)
    .AddAtProtoEfCoreSimpleSpace<SpaceDbContext>();
```

The EF Core stores take an `IDbContextFactory<T>` — they open a context per operation — and use
`SpaceDbContext` or any context of your own that calls `SpaceDbContext.ConfigureSpaceModel()` (or
one of `ConfigureSpaceAuthorityModel`, `ConfigureSimpleSpaceModel`, `ConfigureSpaceReplayModel`)
from its `OnModelCreating`. Pagination is by DID, as in the in-memory stores, so a cursor names a
position rather than an offset into a set that reorders as writes arrive.

#### Upgrading a `simplespace` database from 0.6

The read/write split changed the `simplespace` schema, and since the model lives in your context,
the migration is yours to generate (`dotnet ef migrations add SimpleSpaceReadWriteAccess`):

| Table | Before | After |
| --- | --- | --- |
| `AtProtoSimpleSpaces` | `Policy` | `ReadPolicy`, `WritePolicy` (both required, JSON like `Policy` was) |
| `AtProtoSimpleSpaceMembers` | `Space`, `Did` | plus `Read`, `Write` (`bool`, required) |

EF generates the column changes but not the data. Edit the migration so existing spaces keep the
policy they had for both reading and writing, and existing members keep both kinds of access. This
matches the reference implementation's own `003-space-access` migration:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Keep the column types EF generated for your provider; only the data steps are added.
    migrationBuilder.AddColumn<string>(name: "ReadPolicy", table: "AtProtoSimpleSpaces", nullable: false, defaultValue: "");
    migrationBuilder.AddColumn<string>(name: "WritePolicy", table: "AtProtoSimpleSpaces", nullable: false, defaultValue: "");
    migrationBuilder.Sql("""UPDATE "AtProtoSimpleSpaces" SET "ReadPolicy" = "Policy", "WritePolicy" = "Policy" """);
    migrationBuilder.DropColumn(name: "Policy", table: "AtProtoSimpleSpaces");

    // Existing members could read and had their writes tracked; keep it that way.
    migrationBuilder.AddColumn<bool>(name: "Read", table: "AtProtoSimpleSpaceMembers", nullable: false, defaultValue: true);
    migrationBuilder.AddColumn<bool>(name: "Write", table: "AtProtoSimpleSpaceMembers", nullable: false, defaultValue: true);
}
```

If EF generated a rename of `Policy` rather than a drop, replace it with the steps above. The
`defaultValue: true` belongs in the migration only. The model deliberately configures no database
default for `Read` and `Write`, because EF would then treat a `false` as unset and store the
default instead. Adjust the identifier quoting in the `UPDATE` for your provider if needed.

`AddAtProtoEfCoreSpaceReplayStore<T>()` is the option for a deployment with no Redis: the replay
check is a single insert whose primary key is `(iss, jti, exp)`, so the database's own uniqueness
enforcement is what makes it atomic, and expired rows are swept opportunistically. Redis is the
lighter hop for something on every authenticated request.

The two stores answer different questions, and `AddSpaceAuthority<T>()` bridges them: whenever an
`ISimpleSpaceStore` is registered — in either order — the authority store is wrapped in a
`SimpleSpaceAuthorityStore`, which reads *whether a space exists and whether it was deleted* from
the `simplespace` store and keeps only the writer set and the notification registrations of its
own. So a space created through `createSpace` is one `listRepos`, `registerNotify`, and
`notifyWrite` answer for straight away, and one deleted through `deleteSpace` answers
`SpaceDeleted` from the same moment — nothing has to be declared twice, and there is no second
copy to fall out of step.

A service running a **bespoke space type** has no such store, so it declares its spaces to the
authority store itself: `InMemorySpaceAuthorityStore.DeclareSpace()` / `MarkDeleted()`, or their
durable counterparts `EfCoreSpaceAuthorityStore<T>.DeclareSpaceAsync()` / `MarkDeletedAsync()`.
A space the `simplespace` store has never heard of falls through to those, so the two can be run
side by side. Registering an `ISpaceAuthorityStore` of your own *before* `AddSpaceAuthority<T>()`
opts out of the bridge entirely — wrap it in `SimpleSpaceAuthorityStore` yourself if it needs
one.

### The authority: deciding who reads and whose writes count

An authority's read decision happens once, at `getSpaceCredential`; every repo host in the space
trusts it afterwards and has no state with which to revisit it. Its write decision happens on each
`notifyWrite`, and decides whether the writer enters the writer set and has its notification
forwarded. `ISpaceAccessPolicy` answers both, told which by `SpaceAccessRequest.Access`:

```csharp
public sealed class ForumPolicy : ISpaceAccessPolicy
{
    public async Task<SpaceAccessDecision> EvaluateAsync(SpaceAccessRequest request, CancellationToken ct)
    {
        var allowed = request.Access switch
        {
            SpaceAccessKind.Read => await IsSubscriberAsync(request.UserDid, ct),
            // A write carries no attestation: the caller is the writer's repo host, not an app.
            SpaceAccessKind.Write => await IsContributorAsync(request.UserDid, ct),
            _ => false,
        };

        return allowed
            ? SpaceAccessDecision.Granted
            : SpaceAccessDecision.Refuse(SpaceAccessOutcome.UserNotAuthorized);
    }
}
```

`AddSimpleSpace<T>()` supplies the baseline policy instead, over an `ISimpleSpaceStore` you
implement. For a read it evaluates both perimeters: the read policy (`MemberListPolicy`,
`PublicPolicy`, `ManagingAppPolicy`) and the app access policy (`OpenAppAccess`,
`AllowListAppAccess`). For a write it evaluates the write policy alone.

A few behaviours there are deliberate and worth keeping in a bespoke policy. Refusing an unattested
read with `AppNotAuthorized` is what tells a client holding an attestation to retry with it, since
nothing else advertises that a space gates on app identity. A write must **not** be put through an
app perimeter: it never carries an attestation, so every write would be refused. And a
`ManagingAppPolicy` whose app is unreachable **refuses**: failing open would turn every outage of
the app into an open space.

### The repo host: serving reads

`ISpaceRepoHost` is seven methods over whatever store already holds the records. The handlers do
the verification, the addressing, and the error names; the implementation only reads.

```csharp
public sealed class MyRepoHost : ISpaceRepoHost
{
    public async Task<Stream?> GetRepoAsync(SpaceUri space, Did repo, bool excludeValues, CancellationToken ct)
    {
        var records = await LoadAsync(space, repo, ct);
        var commit = SpaceRepoCommit.FromRecords(...).Sign(new SpaceCommitContext(space, repo, rev), signingKey);

        return new MemoryStream(SpaceRepoCar.Serialize(commit, records, excludeValues));
    }
    // getRecord, listRecords, getLatestCommit, listRepoOps, listBlobs, getBlob
}
```

Two of them answer with bytes rather than JSON, through the new `IXrpcBlobQuery<TParams>`, so the
CAR is streamed rather than buffered.

Returning `null` from `GetRepoAsync`, `GetLatestCommitAsync`, or `ListRepoOpsAsync` produces
`RepoNotFound`, which deliberately does not distinguish *a member who has never written* from *not
a member at all* — the protocol carries no reader set, and saying more would leak membership. The
same applies to `GetBlobAsync`: the reference check **is** the access check, because serving a blob
on the basis of its CID alone would hand out any blob the account holds to anyone with a credential
for any of its spaces.

### Write notifications

`SpaceWriteNotifier` fans `notifyWrite` out to the services registered for a space, authenticated
with service auth issued by this service. Delivery is best-effort by design: a failure is logged
and dropped, because the syncer's periodic sweep over `listRepos` is the correctness guarantee and
the notification is only the latency optimization.

```csharp
// On a repo host, after a write into a space anchored on someone else's DID.
await notifier.EnsureAuthoritySubscribedAsync(space, repoDid, ct);
await notifier.NotifyWriteAsync(space, repoDid, rev, hash, ct);
```

`EnsureAuthoritySubscribedAsync` is what puts an account into a space's writer set at all: an
authority learns who holds data in its spaces only from the notifications it receives, and it
receives them only because it is registered to. It is a no-op for a personal-data space, where the
authority and the repo host are the same service.

Each delivery is addressed (`aud`) to the service identifier the subscriber registered, fragment
and all, because that is what a syncer or managing app such as `did:web:app.example.com#forum`
verifies. The authority's own registration, `{authority}#atproto_space_host`, is the exception. It
is reached at the authority's `#atproto_space_host` endpoint, or at its `#atproto_pds` when it
publishes none, as an authority on an ordinary PDS does. It is addressed by the authority's bare
DID, which is what the reference authority checks.

Inbound, `NotifyWriteEndpoint` does four things in order:

1. It refuses malformed input before any auth check. `rev` must be a TID.
2. It accepts a token addressed to the authority's bare DID, to `{authority}#atproto_space_host`,
   or to `SpaceServerOptions.ServiceDid`. Reference repo hosts send the first, so a multi-tenant
   host configured with its own `ServiceDid` still accepts them.
3. It accepts a notification only from the host that actually answers for the named repo.
   Otherwise any service could advance any account's revision in the writer set, which is what a
   syncer uses to decide whether to re-read a repo.
4. It puts the writer to the access policy as a write. A refused writer gets a 403 and is neither
   recorded nor forwarded. An admitted one is recorded, then forwarded in the background to every
   service registered for the space (`SpaceWriteNotifier.ForwardWriteAsync`). The authority's own
   registration is skipped, so a service that is both repo host and authority does not notify
   itself.

### What is not here

The **write** path — `createRecord`, `putRecord`, `deleteRecord`, `applyWrites`,
`getDelegationToken`, and `listSpaces` — is served by a user's PDS over its own OAuth session, and
is not part of this layer. It needs the record store, the oplog, and the OAuth scope evaluation a
PDS already has.

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
| `AddAtProtoSpaces` / `AddSpaceAuthority` / `AddSpaceRepoHost` / `AddSimpleSpace` | Register the server halves (`ATProtoNet.Server`) |
| `SpaceRequestAuthenticator`, `DPoPProofValidator`, `SpaceDelegationTokenVerifier`, `SpaceCredentialVerifier`, `SpaceClientAttestationVerifier` | Verify what a caller presents |
| `ISpaceAccessPolicy`, `ISpaceCredentialIssuer`, `ISpaceAuthorityStore`, `ISpaceRepoHost`, `ISimpleSpaceStore` | The seams a server implements |
| `RedisSpaceReplayStore`, `EfCoreSpaceReplayStore`, `EfCoreSpaceAuthorityStore`, `EfCoreSimpleSpaceStore` | Durable, multi-instance implementations of those stores |
| `SpaceWriteNotifier` | Deliver write and deletion notifications |

## See also

- [`samples/SpacesSample`](../samples/SpacesSample/Program.cs) — a runnable walk through personal data, sync, and the credential exchange against a permissioned-data PDS
- [Testing Against a Real Space Host](testing-spaces.md) — how to run the space integration tests against a permissioned-data PDS
- [OAuth Authentication](oauth.md) — the DPoP, PAR, and PKCE flow the credential exchange builds on
- [Low-Level Repo API](low-level-repo.md) — CAR files and DAG-CBOR, shared with public repositories
- [Cryptography](crypto.md) — key generation, signing, and `did:key` encoding
- [Firehose Streaming](firehose.md) — the `#identity` and `#account` events permissioned syncers need
