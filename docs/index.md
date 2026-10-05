# ATProto.NET Documentation

ATProto.NET is a .NET 10 SDK for the [AT Protocol](https://atproto.com) — the open protocol behind Bluesky, where one account can power many apps. These docs cover building your own AT Protocol apps in .NET, running a managed PDS, and integrating with Bluesky.

## Start here

1. **[Installation & Setup](getting-started.md)** — install the packages, create a client, authenticate.
2. **[Custom Lexicon Records](custom-records.md)** — the SDK's headline feature: define your own record types and use `RecordCollection<T>` for typed CRUD.
3. **Pick your integration** — [ASP.NET Core](aspnet-core.md), [Blazor](blazor.md), [Aspire](aspire.md), or run a [managed PDS](managed-pds.md).

Upgrading from 0.7? Read **[Migrating to 0.8](migrating-to-0.8.md)**; from 0.6, **[Migrating to 0.7](migrating-to-0.7.md)** first. For a map of how the packages compose, see **[Architecture](architecture.md)**. Every type and method is documented in its XML comments, which IntelliSense shows.

## Guides

### Core Concepts
- [AT Protocol Overview](at-protocol-overview.md) — DIDs, handles, repositories, Lexicons
- [Identity Types](identity-types.md) — `Did`, `Handle`, `AtUri`, `Nsid`, `Tid`, `RecordKey`, `Cid`, `AtDatetime`
- [Identity Resolution](did-resolution.md) — DID and handle resolvers, caching, the SSRF fetch policy, and resolving lexicons
- [Session Management](session-management.md) — sessions, refresh, persistence with `IAtProtoSessionStore`, sign-out
- [OAuth Authentication](oauth.md) — DPoP, PAR, PKCE, scopes, and the hosted login for ASP.NET Core
- [Error Handling](error-handling.md) — the `XrpcException` family, rate limits, retry patterns

### Building Your Own App
- [Custom Lexicon Records](custom-records.md) — `RecordCollection<T>` for typed CRUD, unions and unknown fields
- [Custom XRPC Endpoints](custom-xrpc.md) — call your own query / procedure methods, and build sub-clients for other Lexicons
- [Batch Operations](batch-operations.md) — `ApplyWrites` for atomic multi-record operations
- [Blob Upload](blob-upload.md) — upload images, files, binary data
- [Spaces (Permissioned Data)](spaces.md) — the access-controlled data protocol: spaces, permissioned repos, credentials, sync, and [serving a space](spaces.md#serving-a-space)
- [Testing Against a Real Space Host](testing-spaces.md) — standing up a permissioned-data PDS for the space integration tests

### Bluesky Features
- [Bluesky](bluesky.md) — posting, likes and follows, profiles, feeds, threads, search, bookmarks, notifications
- [Chat & Direct Messages](chat.md) — `chat.bsky` direct and group chats
- [Video Upload](video.md) — `app.bsky.video` upload and processing
- [Labeler Services](labeler.md) — label definitions, labeler info, header management, and signing, verifying and serving labels
- [Ozone Moderation](ozone.md) — `tools.ozone` moderation client
- [Standard.site](standard-site.md) — long-form publishing integration

### ASP.NET Core and Blazor
- [ASP.NET Core](aspnet-core.md) — the `AddAtProto()` builder, options, HTTP handlers, authentication, controllers
- [Acting as the Signed-In User](server.md) — `IAtProtoClientFactory` and the session stores
- [Blazor](blazor.md) — the login form and the widgets that act as the signed-in user
- [Aspire](aspire.md) — the client from configuration, health checks, and keeping service-default retries off the SDK
- [XRPC Endpoint Handlers](xrpc-handlers.md) — serve `/xrpc/{nsid}` endpoints, and accept service auth from other services

### Running Servers
- [Managed PDS](managed-pds.md) — run the Bluesky or Tranquil PDS container and administer it from .NET

### Advanced
- [Firehose Streaming](firehose.md) — real-time event streaming, typed consumers, Sync 1.1 verification and resync
- [Tap](tap.md) — a client for Tap, Bluesky's Sync 1.1 consumer and backfill service: channel, admin API, webhooks
- [Jetstream Streaming](jetstream.md) — JSON event streaming with server-side collection/DID/kind filtering, on both the v1 and v2 wire protocols, plus the v2 archive (historical replay and snapshots)
- [Cryptography](crypto.md) — key generation, signing, multikey encoding, service auth tokens
- [Low-Level Repo API](low-level-repo.md) — direct `RepoClient`, verified reads, DAG-CBOR, CIDs, CAR files, the MST, commits
- [Lexicon Code Generator](lexicon-codegen.md) — generate C# from Lexicons (and vice versa), lint, diff, publish and resolve

### Reference
- [Architecture](architecture.md) — package layering, source tree, conventions
- [Migrating to 0.8](migrating-to-0.8.md) — every breaking change of 0.8
- [Migrating to 0.7](migrating-to-0.7.md) — every breaking change of 0.7, with before and after
