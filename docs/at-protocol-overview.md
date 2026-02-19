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

DIDs never change, even if the user changes their handle or moves to a different PDS.

```csharp
var did = Did.Parse("did:plc:z72i7hdynmk6r22z27h6tvur");
Console.WriteLine(did.Method);             // "plc"
Console.WriteLine(did.MethodSpecificId);   // "z72i7hdynmk6r22z27h6tvur"
```

### Handle

A human-readable domain-name identifier:

```
alice.bsky.social
bob.example.com
```

Handles map to DIDs via DNS or HTTP resolution. They can change.

```csharp
var handle = Handle.Parse("alice.example.com");
// Handles are normalized to lowercase, @ prefix stripped
```

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

Each Lexicon has an **NSID** (Namespaced Identifier):

```
com.atproto.repo.createRecord    ← Protocol-level method
app.bsky.feed.post               ← Bluesky record type
com.example.todo.item            ← Your custom record type
```

```csharp
var nsid = Nsid.Parse("com.example.todo.item");
Console.WriteLine(nsid.Authority);  // "example.com"
Console.WriteLine(nsid.Name);       // "item"
```

### AT URI

A URI scheme identifying a specific record:

```
at://did:plc:abc123/com.example.todo.item/3k2la7r
     ───────────── ──────────────────────── ───────
     authority      collection               record key
```

```csharp
var uri = AtUri.Parse("at://did:plc:abc/com.example.todo.item/3k2la");
Console.WriteLine(uri.Authority);    // "did:plc:abc"
Console.WriteLine(uri.Collection);   // "com.example.todo.item"
Console.WriteLine(uri.RecordKey);    // "3k2la"
```

### TID (Timestamp Identifier)

A 13-character, base32-sortable identifier used as the default record key:

```
3k2la7rxjgs2t
```

TIDs encode a microsecond timestamp and a random clock ID, ensuring uniqueness and chronological sorting.

```csharp
var tid = Tid.Next();  // Generate a new TID
Console.WriteLine(tid.Value);  // "3k2la7rxjgs2t"
```

### Record Key

The unique key for a record within a collection. Usually a TID, but can be:

- A TID (auto-generated): `3k2la7rxjgs2t`
- A literal: `self` (used by profile records)
- A custom string following AT Protocol naming rules

```csharp
var rkey = RecordKey.Parse("self");
var rkey2 = RecordKey.NewTid();  // Generate a TID-based key
```

### CID (Content Identifier)

A hash-based content identifier for a specific version of a record:

```
bafyreidfayvfkicgmdl3ebhqhvvd3oevkmpoh5eoyltcy73c6aaoqc7srca
```

Used for content addressing and optimistic concurrency (CAS operations).

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

AT Protocol uses JWT-based session authentication:

1. **Login** with handle + password → receive access + refresh tokens
2. **Access token** is sent with each request (short-lived, ~2 hours)
3. **Refresh token** is used to get new access tokens (longer-lived)

ATProto.NET handles token management automatically when `AutoRefreshSession` is enabled (the default).

## Further Reading

- [AT Protocol Specification](https://atproto.com/specs/atp)
- [Lexicon Guide](https://atproto.com/guides/lexicon)
- [Repository Structure](https://atproto.com/specs/repository)
- [Identity](https://atproto.com/specs/did)
