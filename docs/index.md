# ATProto.NET Documentation

Welcome to the ATProto.NET documentation. This SDK enables you to build custom applications on the [AT Protocol](https://atproto.com) using .NET.

## Guides

### Getting Started
- [Installation & Setup](getting-started.md) — Install the SDK, create your first client, authenticate
- [Custom Lexicon Records](custom-records.md) — Define record types, use RecordCollection<T> for CRUD
- [Custom XRPC Endpoints](custom-xrpc.md) — Call your own query/procedure Lexicon methods

### Core Concepts
- [AT Protocol Overview](at-protocol-overview.md) — Understanding DIDs, handles, repositories, Lexicons
- [Identity Types](identity-types.md) — DID, Handle, AtUri, NSID, TID, RecordKey, CID
- [Session Management](session-management.md) — Authentication, token refresh, custom persistence
- [OAuth Authentication](oauth.md) — AT Protocol OAuth with DPoP, PAR, PKCE, dynamic PDS
- [Error Handling](error-handling.md) — XRPC errors, HTTP exceptions, retry patterns

### Integration
- [ASP.NET Core](aspnet-core.md) — Dependency injection, authentication handler, controllers
- [Blazor](blazor.md) — Components, auth state provider, interactive apps

### Advanced
- [Batch Operations](batch-operations.md) — ApplyWrites for atomic multi-record operations
- [Blob Upload](blob-upload.md) — Upload images, files, and binary data
- [Firehose Streaming](firehose.md) — Real-time WebSocket event streaming
- [Low-Level Repo API](low-level-repo.md) — Direct RepoClient usage for advanced scenarios

### Reference
- [API Reference](api-reference.md) — Complete API surface with all methods and types
