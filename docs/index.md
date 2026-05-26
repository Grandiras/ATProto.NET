# ATProto.NET Documentation

Welcome to the ATProto.NET documentation. This SDK enables you to build custom applications on the [AT Protocol](https://atproto.com) using .NET.

## Guides

### Getting Started
- [Installation & Setup](getting-started.md) — Install the SDK, create your first client, authenticate
- [Custom Lexicon Records](custom-records.md) — Define record types, use RecordCollection\<T\> for CRUD
- [Custom XRPC Endpoints](custom-xrpc.md) — Call your own query/procedure Lexicon methods

### Core Concepts
- [AT Protocol Overview](at-protocol-overview.md) — Understanding DIDs, handles, repositories, Lexicons
- [Identity Types](identity-types.md) — DID, Handle, AtUri, NSID, TID, RecordKey, CID
- [DID Resolution](did-resolution.md) — did:plc, did:web resolution, unified DidResolver
- [Session Management](session-management.md) — Authentication, token refresh, custom persistence
- [OAuth Authentication](oauth.md) — AT Protocol OAuth with DPoP, PAR, PKCE, dynamic PDS
- [Error Handling](error-handling.md) — XRPC errors, HTTP exceptions, retry patterns

### Bluesky Features
- [Chat & Direct Messages](chat.md) — Bluesky DM support via chat.bsky
- [Video Upload](video.md) — Upload and process videos via app.bsky.video
- [Labeler Services](labeler.md) — Label definitions, labeler info, header management
- [Ozone Moderation](ozone.md) — tools.ozone moderation client

### Integration
- [ASP.NET Core](aspnet-core.md) — Dependency injection, authentication handler, controllers
- [Server Integration](server.md) — IAtProtoClientFactory, OAuth token store, EF Core, backend AT Proto access
- [Blazor](blazor.md) — Components, cookie-based OAuth login, interactive apps
- [Aspire](aspire.md) — .NET Aspire service defaults, health checks, resilience
- [Standard.site](standard-site.md) — Long-form publishing integration

### Building Servers
- [PDS Hosting](pds.md) — Build your own AT Protocol Personal Data Server
- [XRPC Endpoint Handlers](xrpc-handlers.md) — Server-side XRPC endpoints with DI

### Advanced
- [Batch Operations](batch-operations.md) — ApplyWrites for atomic multi-record operations
- [Blob Upload](blob-upload.md) — Upload images, files, and binary data
- [Firehose Streaming](firehose.md) — Real-time event streaming, typed consumers, verification
- [Cryptography](crypto.md) — Key generation, signing, multikey encoding, service auth
- [Lexicon Code Generator](lexicon-codegen.md) — Generate C# from Lexicons and vice versa
- [Low-Level Repo API](low-level-repo.md) — Direct RepoClient, MST, DAG-CBOR, CAR files

### Reference
- [API Reference](api-reference.md) — Complete API surface with all methods and types
