# AT Protocol Overview

The [Authenticated Transfer Protocol](https://atproto.com) (AT Protocol) is an open, decentralized protocol for social applications. This guide covers the core concepts you'll encounter when building apps with ATProto.NET.

## Core Architecture

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Your App  │     │  Bluesky    │     │  Other Apps  │
│  (ATProtoNet)│    │             │     │             │
└──────┬──────┘     └──────┬──────┘     └──────┬──────┘
       │                   │                   │
       └───────────┬───────┘───────────────────┘
                   │
           ┌───────▼───────┐
           │  User's PDS   │   ← Personal Data Server
           │  (Repository) │      stores ALL records
           └───────────────┘
```

**One account, many apps.** A user's PDS stores data for all their AT Protocol applications. Each app uses its own Lexicon namespace to avoid collisions.

## Key Concepts

### DID (Decentralized Identifier)

A persistent, globally unique identifier for a user:

```
did:plc:z72i7hdynmk6r22z27h6tvur
did:web:alice.example.com
```

DIDs never change, even if the user changes their handle or moves to a different PDS. In the
SDK a DID is a [`Did`](identity-types.md#did), and resolving one to its DID document is covered in
[Identity Resolution](did-resolution.md).

### Handle

A human-readable domain-name identifier:

```
alice.bsky.social
bob.example.com
```

Handles map to DIDs via DNS or HTTP resolution. They can change. In the SDK a handle is a
[`Handle`](identity-types.md#handle).

### Repository

Each user has a **repository** — a signed data store on their PDS containing all their records. Records are organized into **collections** identified by NSIDs.

```
Repository (did:plc:abc123)
├── app.bsky.feed.post/          ← Bluesky posts
│   ├── 3k2la7r...
│   └── 3k2lb8s...
├── app.bsky.graph.follow/       ← Bluesky follows
│   └── 3k2lc9t...
├── com.example.todo.item/       ← Your custom app records
│   ├── 3k2ld0u...
│   └── 3k2le1v...
└── com.example.bookmarks.bookmark/  ← Another custom app
    └── 3k2lf2w...
```

### Lexicon

A **Lexicon** is a schema definition for AT Protocol data types and API methods. It uses a Lexicon JSON schema format.

Each Lexicon has an **NSID** (Namespaced Identifier), an [`Nsid`](identity-types.md#nsid-namespaced-identifier) in the SDK:

```
com.atproto.repo.createRecord    ← Protocol-level method
app.bsky.feed.post               ← Bluesky record type
com.example.todo.item            ← Your custom record type
```

### AT URI

A URI scheme identifying a specific record, an [`AtUri`](identity-types.md#aturi) in the SDK:

```
at://did:plc:abc123/com.example.todo.item/3k2la7r
     ───────────── ──────────────────────── ───────
     authority      collection               record key
```

### TID (Timestamp Identifier)

A 13-character, base32-sortable identifier used as the default record key:

```
3k2la7rxjgs2t
```

TIDs encode a microsecond timestamp and a clock ID. `Tid.Next()` returns strictly increasing
values, so TIDs from one process never repeat and sort chronologically (see
[`Tid`](identity-types.md#tid-timestamp-identifier)).

### Record Key

The unique key for a record within a collection. Usually a TID, but can be:

- A TID (auto-generated): `3k2la7rxjgs2t`
- A literal: `self` (used by profile records)
- A custom string following AT Protocol naming rules

In the SDK a record key is a [`RecordKey`](identity-types.md#recordkey).

### CID (Content Identifier)

A hash-based content identifier for a specific version of a record:

```
bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm
```

Used for content addressing and optimistic concurrency (CAS operations). In the SDK a CID is a
[`Cid`](identity-types.md#cid-content-identifier).

## XRPC

AT Protocol APIs use **XRPC** — a simple HTTP-based RPC framework:

- **Queries** → HTTP GET (reading data)
- **Procedures** → HTTP POST (writing data)
- **Subscriptions** → WebSocket (streaming)

All endpoints are identified by Lexicon NSIDs:

```
GET  /xrpc/com.atproto.repo.listRecords?repo=did:plc:abc&collection=com.example.todo.item
POST /xrpc/com.atproto.repo.createRecord  { repo, collection, record }
```

ATProto.NET handles all of this internally — you work with typed C# APIs.

## Authentication

A client acts for an account with one of two kinds of session:

- **OAuth** (recommended for anything user-facing): the user signs in at their own PDS's
  authorization server, and the app receives DPoP-bound tokens — it never sees the password. See
  [OAuth Authentication](oauth.md).
- **Password sessions**: sign in with a handle and an app password, and receive an access and a
  refresh JWT. Simple, and right for bots, scripts and services acting as their own account.

Either way the access token is short-lived and the refresh token single-use. ATProto.NET refreshes
on demand (`AutoRefreshSession`, on by default); see [Session Management](session-management.md).

Services calling one another on a user's behalf use **service auth**: a short-lived JWT signed with
the account's key and bound to one method (see
[Serving XRPC to other services](xrpc-handlers.md#serving-xrpc-to-other-services)).

## Further Reading

- [AT Protocol Specification](https://atproto.com/specs/atp)
- [Lexicon Guide](https://atproto.com/guides/lexicon)
- [Repository Structure](https://atproto.com/specs/repository)
- [Identity](https://atproto.com/specs/did)
