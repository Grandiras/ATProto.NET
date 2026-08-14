# ATProto.NET Documentation

ATProto.NET is a .NET 10 SDK for the [AT Protocol](https://atproto.com) — the open protocol behind Bluesky, where one account can power many apps. These docs cover building your own AT Protocol apps in .NET, running a managed PDS, and integrating with Bluesky.

## Start here

1. **[Installation & Setup](getting-started.md)** — install the packages, create a client, authenticate.
2. **[Custom Lexicon Records](custom-records.md)** — the SDK's headline feature: define your own record types and use `RecordCollection<T>` for typed CRUD.
3. **Pick your integration** — [ASP.NET Core](aspnet-core.md), [Blazor](blazor.md), [Aspire](aspire.md), or run a [managed PDS](managed-pds.md).

If you want a map of how the packages compose, see **[Architecture](architecture.md)**.

## Guides

### Core Concepts
- [AT Protocol Overview](at-protocol-overview.md) — DIDs, handles, repositories, Lexicons
- [Identity Types](identity-types.md) — `Did`, `Handle`, `AtUri`, `Nsid`, `Tid`, `RecordKey`, `Cid`
- [DID Resolution](did-resolution.md) — `did:plc`, `did:web`, unified `DidResolver`
- [Session Management](session-management.md) — authentication, token refresh, custom persistence
- [OAuth Authentication](oauth.md) — DPoP, PAR, PKCE, dynamic PDS selection
- [Error Handling](error-handling.md) — XRPC errors, HTTP exceptions, retry patterns

### Building Your Own App
- [Custom Lexicon Records](custom-records.md) — `RecordCollection<T>` for typed CRUD
- [Custom XRPC Endpoints](custom-xrpc.md) — call your own query / procedure methods
- [Batch Operations](batch-operations.md) — `ApplyWrites` for atomic multi-record operations
- [Blob Upload](blob-upload.md) — upload images, files, binary data

### Bluesky Features
- [Chat & Direct Messages](chat.md) — `chat.bsky` DMs
- [Video Upload](video.md) — `app.bsky.video` upload and processing
- [Labeler Services](labeler.md) — label definitions, labeler info, header management
- [Ozone Moderation](ozone.md) — `tools.ozone` moderation client

### Integration
- [ASP.NET Core](aspnet-core.md) — dependency injection, authentication handler, controllers
- [Server Integration](server.md) — `IAtProtoClientFactory`, token store, backend AT Proto access
- [Blazor](blazor.md) — components, cookie-based OAuth login, interactive apps
- [Aspire](aspire.md) — service defaults, health checks, resilience
- [Standard.site](standard-site.md) — long-form publishing integration

### Building Servers
- [Managed PDS](managed-pds.md) — run the Bluesky PDS container and administer it from .NET
- [XRPC Endpoint Handlers](xrpc-handlers.md) — server-side XRPC endpoints with DI

### Advanced
- [Firehose Streaming](firehose.md) — real-time event streaming, typed consumers, verification
- [Jetstream Streaming](jetstream.md) — JSON event streaming with server-side collection/DID/kind filtering, on both the v1 and v2 wire protocols, plus the v2 archive (historical replay and snapshots)
- [Cryptography](crypto.md) — key generation, signing, multikey encoding, service auth
- [Lexicon Code Generator](lexicon-codegen.md) — generate C# from Lexicons (and vice versa)
- [Low-Level Repo API](low-level-repo.md) — direct `RepoClient`, MST, DAG-CBOR, CAR files

### Reference
- [Architecture](architecture.md) — package layering, source tree, conventions
- [API Reference](api-reference.md) — complete public API surface
