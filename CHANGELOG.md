# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Security hardening** — Comprehensive SSRF prevention, TLS enforcement, and input validation
  - Accurate private IP range detection using `IPAddress.TryParse` covering RFC 1918, CGN (100.64/10), loopback, link-local, and IPv6 private ranges
  - IPv6 bracket host blocking in DID:web resolution (all bracketed IPs rejected — use domain names)
  - TLS enforcement in `XrpcClient.SetBaseUrl()` — HTTP only allowed for localhost/loopback
  - Exact token matching for `atproto` scope validation (prevents substring false-positives)
  - Open redirect prevention in OAuth callback return URLs
  - Error message sanitization (truncation to 200 chars) to prevent leaking internal details
  - DPoP key disposal on all OAuth error paths (prevents cryptographic key leaks)
  - Concurrent session refresh guard via `SemaphoreSlim` in `AtProtoClient`
  - Restrictive Unix file permissions (700) on `FileAtProtoTokenStore` directory
  - 54 new security-focused tests (362 total)

- **Aspire auto-detection** — Automatic HTTP loopback URL discovery for AT Proto OAuth
  - `TryGetLoopbackHttpUrl()` inspects `IServerAddressesFeature` for HTTP bindings when request arrives on HTTPS
  - Normalizes `localhost` → `127.0.0.1` for AT Proto loopback compatibility
  - Zero-config: works automatically with Aspire, Kestrel multi-bind, and reverse proxy setups

- **Transparent cross-origin cookie relay** — Automatic auth cookie relay for localhost/127.0.0.1 mismatch
  - AT Proto loopback OAuth requires `http://127.0.0.1` for the callback, but the user's browser may be on `https://localhost` (e.g., in Aspire). The auth cookie set on `127.0.0.1` is invisible on `localhost`.
  - The SDK now detects when the callback origin differs from the login origin, generates a one-time relay code (128-bit, 2-minute expiry), and redirects to `{loginOrigin}/atproto/relay?code=xxx` to issue the cookie on the correct domain.
  - Return URL is stored server-side (keyed by OAuth state) instead of only in a cookie, fixing the cross-domain cookie loss.
  - Zero-config: No `BaseUrl`, `OnSigningIn` hooks, or relay middleware needed. Just `AddAtProtoAuthentication()` + `MapAtProtoOAuth()`.
  - 22 new cookie relay tests (384 total)

- **Lexicon code generator** — Bidirectional `dotnet tool` (`atproto-lexgen`) for AT Protocol Lexicon schemas
  - `atproto-lexgen csharp` — Generate C# classes from Lexicon JSON schema files (records, objects, enums, tokens)
  - `atproto-lexgen lexicon` — Generate Lexicon JSON schemas from compiled .NET assemblies via reflection
  - `atproto-lexgen diff` — Compare baseline and current Lexicon schemas, detect breaking changes per AT Protocol evolution rules
  - Matches existing SDK patterns: `sealed class`, `required`/`init` properties, `[JsonPropertyName]`, `$type` expression-body
  - Supports all Lexicon types: record, object, string enum, token, ref, union, array, blob
  - Schema evolution validation: detects added/removed properties, type changes, required status changes, constraint tightening
  - `--strict` mode exits with code 1 on breaking changes (for CI integration)
  - Automatic revision bump suggestions for non-breaking changes

- **Cryptography utilities** (`AtProtoCrypto`, `AtProtoKey`) — AT Protocol cryptographic operations
  - P-256 (NIST secp256r1) and K-256 (secp256k1) key pair generation
  - ECDSA signing and verification with SHA-256 and low-S normalization
  - Compressed public key export/import with EC point decompression (modular arithmetic)
  - Multikey encoding/decoding (base58btc with multicodec prefix)
  - `did:key` generation and parsing (round-trips through multikey)
  - PKCS#8 private key export/import
  - Base58 Bitcoin encoding/decoding

- **CAR file reader** (`CarReader`) — Parse Content Addressable aRchive (CAR v1) files
  - Used for consuming `com.atproto.sync.getRepo` responses
  - CID parsing (CIDv0 and CIDv1), DAG-CBOR header decoding
  - Block lookup by CID, root block access
  - Stream and byte array input support

- **PLC directory client** (`PlcClient`) — Interact with PLC directory servers
  - DID document resolution (`ResolveDidAsync`) with 404/410 error handling
  - Operation log, audit log, and latest operation retrieval
  - Current PLC data access
  - Health check endpoint
  - Full DID document model: `DidDocument`, `VerificationMethod`, `ServiceEndpoint`
  - PLC operation model: `PlcOperation`, `PlcAuditEntry`, `PlcData`
  - Convenience methods: `GetHandle()`, `GetPdsEndpoint()` on `DidDocument`

- **Service auth JWT generation** (`ServiceAuthGenerator`) — Inter-service authentication
  - JWT generation with `iss` (service DID), `aud` (target), `exp`, `iat`, `jti`, `lxm` claims
  - ES256 (P-256) and ES256K (K-256) signing via `AtProtoKey`
  - 60-second default expiry, 5-minute maximum enforcement
  - Used for Feed Generators, Labelers, and relay services

### Fixed

- **Issue templates** — Converted from invalid hybrid format (YAML frontmatter + Markdown body in `.yml` files) to proper Forgejo YAML form templates with structured `body:` sections

## [0.3.0] - 2026-02-21

### Added

- **Cookie-based OAuth for Blazor** — Standard cookie authentication that works with `<AuthorizeView>`, `[Authorize]`, and all built-in Blazor auth patterns
  - `AddAtProtoAuthentication()` — registers OAuth service and options
  - `MapAtProtoOAuth()` — maps `/atproto/login`, `/atproto/callback`, `/atproto/logout` endpoints
  - Auto-generated loopback `client_id` for zero-config development
  - Configurable claims via `ClaimsFactory` option
  - Default claims: DID, handle, PDS URL, auth method

- **Server-side AT Protocol access** — Backend API integration via `IAtProtoClientFactory`
  - `AddAtProtoServer()` — registers token store, client factory, and HTTP client
  - `IAtProtoClientFactory` — creates per-request authenticated `AtProtoClient` from stored OAuth tokens
  - `IAtProtoTokenStore` — interface for multi-user server-side token storage
  - `FileAtProtoTokenStore` (default) — persistent file-based token storage with ASP.NET Core Data Protection encryption
  - `InMemoryAtProtoTokenStore` — volatile in-memory store for development/testing
  - `AddAtProtoServer(string tokenDirectory)` overload for custom token storage directory
  - `AtProtoTokenData` — serializable token data including DPoP private key
  - Blazor OAuth service automatically stores/removes tokens when `IAtProtoTokenStore` is registered

- **Rewritten `LoginForm` component** — Pure HTML form that submits to the login endpoint
  - Fully customizable labels for localization
  - Optional PDS URL input for custom PDS connections
  - Auto-displays OAuth callback errors

- **ServerIntegrationSample** — New sample showing Blazor OAuth + backend AT Proto access
  - Minimal API endpoints (`/api/profile`, `/api/timeline`)
  - Blazor pages using `IAtProtoClientFactory` directly
  - Profile and timeline views

### Fixed

- **DPoP nonce handling** — `AtProtoClientFactory` now passes `null` DPoP nonces instead of stale stored values; the XRPC client's retry logic acquires fresh nonces on first request, preventing `use_dpop_nonce` 401 errors

### Changed

- **ATProtoNet.Blazor.csproj** — Replaced individual NuGet package references with `<FrameworkReference Include="Microsoft.AspNetCore.App" />`
- **ATProtoNet.Server `ServiceCollectionExtensions`** — Added `AddAtProtoServer()` for OAuth-based multi-user access; default token store changed from `InMemoryAtProtoTokenStore` to `FileAtProtoTokenStore`; improved docs on existing `AddAtProto()` and `AddAtProtoScoped()` methods

### Removed

- **BREAKING:** `AddAtProtoBlazor()` extension method — replaced by `AddAtProtoAuthentication()`
- **BREAKING:** `AtProtoAuthStateProvider` — no longer needed; standard `ServerAuthenticationStateProvider` works via cookies
- **BREAKING:** `OAuthCallback` component — callback is now an HTTP endpoint mapped by `MapAtProtoOAuth()`
- **BREAKING:** `PdsOption` model — PDS selection is now a simple text input in `LoginForm`
- **BREAKING:** `BlazorServiceCollectionExtensions` class — replaced by `AtProtoAuthenticationExtensions`

## [0.2.0] - 2026-02-20

### Added

- **OAuth Authentication** — Full [AT Protocol OAuth](https://atproto.com/specs/oauth) implementation
  - DPoP (RFC 9449) — proof-of-possession bound tokens with ES256 (P-256) key pairs
  - Pushed Authorization Requests (RFC 9126) — secure authorization initiation
  - PKCE (RFC 7636) — S256 code challenge for public clients
  - Authorization Server Discovery — full resolution chain (Handle → DID → PDS → AS)
  - Identity verification — DID/issuer consistency checks after token exchange
  - Token refresh with DPoP binding
  - `OAuthClient` orchestrator with `StartAuthorizationAsync()` / `CompleteAuthorizationAsync()`
  - `AuthorizationServerDiscovery` for handle, DID, and PDS resolution
  - `DPoPProofGenerator` for ES256 DPoP proof JWT generation
  - `PkceGenerator` for PKCE S256 code verifier and challenge generation
  - Complete `OAuthModels` — client metadata, server metadata, token responses, DID documents

- **Dynamic PDS Selection** — Connect to any AT Protocol PDS at runtime
  - `AtProtoClient.SetPdsUrl()` — change PDS URL dynamically
  - `AtProtoClient.ApplyOAuthSessionAsync()` — apply OAuth session with DPoP tokens
  - `XrpcClient.SetBaseUrl()` — runtime base URL changes
  - OAuth flow automatically resolves user's PDS from their identity

- **Blazor OAuth Components**
  - `LoginForm` — redesigned with PDS selector, OAuth toggle, custom PDS URL input
  - `OAuthCallback` — callback handler component for OAuth redirect
  - `PdsOption` — model for PDS dropdown options
  - `AtProtoAuthStateProvider` — OAuth-aware auth state with `StartOAuthLoginAsync()` and `CompleteOAuthLoginAsync()`
  - `AddAtProtoBlazor()` — now registers `OAuthClient` when OAuth options are configured

- **Security hardening**
  - Handle format validation (SSRF prevention)
  - DID:web host validation (private IP blocking)
  - Redirect URI HTTPS enforcement (localhost exception for dev)
  - DID format validation on token response `sub` claim
  - Pending authorization cleanup (10-minute expiry, 100 max entries)
  - DPoP private key export security documentation

- **Sample project**
  - `samples/BlazorOAuthSample` — minimal Blazor Server app demonstrating OAuth login with loopback client

- **Documentation**
  - OAuth authentication guide (`docs/oauth.md`) with loopback client development section
  - Updated Blazor, session management, and getting started guides
  - Updated README with OAuth sections

- **Tests**
  - 50 new unit tests for OAuth components (DPoP, PKCE, models, dynamic PDS)
  - Total: 268 unit tests

## [0.1.1] - 2026-02-20

### Fixed

- **Timestamp formatting** — All timestamps now use AT Protocol-preferred millisecond precision (`yyyy-MM-ddTHH:mm:ss.fffZ`) instead of .NET's round-trip format with 7 fractional digits. This improves compatibility with PDS/AppView implementations.

### Added

- `AtProtoJsonDefaults.FormatTimestamp()` and `NowTimestamp()` helpers for generating spec-compliant ISO 8601 timestamps.

## [0.1.0] - 2026-02-19

### Added

- **Core SDK (`ATProtoNet`)**
  - `AtProtoClient` — main facade with session management, auto-refresh
  - `RecordCollection<T>` — typed CRUD for custom lexicon records
  - `AtProtoRecord` base class for custom record types
  - Custom XRPC endpoints via `QueryAsync<T>()` and `ProcedureAsync()`
  - Identity types: `Did`, `Handle`, `AtIdentifier`, `Nsid`, `AtUri`, `Cid`, `Tid`, `RecordKey`
  - Repository operations: create, get, put, delete, list records, apply writes
  - Blob upload support
  - Firehose / event stream client
  - Server administration, identity resolution, label and moderation clients
  - Bluesky convenience methods (post, like, repost, follow, profile, feed, notifications)
  - Full System.Text.Json serialization with custom converters
  - Session persistence via `ISessionStore` interface
  - Comprehensive XML documentation on all public APIs

- **ASP.NET Core Integration (`ATProtoNet.Server`)**
  - `AddAtProto()` / `AddAtProtoClient()` DI extensions
  - `AtProtoAuthenticationHandler` for AT Proto bearer token authentication
  - Built-in `ISessionStore` using `IDistributedCache`

- **Blazor Integration (`ATProtoNet.Blazor`)**
  - `AtProtoLoginForm` component
  - `AtProtoProfileCard` component
  - `AtProtoFeed` component
  - `AtProtoAuthStateProvider` for Blazor auth integration
  - Cascading authentication state

- **Testing**
  - 218 unit tests
  - 23 integration tests (20 pass on bare PDS, 3 Bluesky-specific skipped)
  - Integration test infrastructure with `RequiresPdsFact` / `RequiresBlueskyFact`

- **Documentation**
  - Getting started guide
  - Custom records & lexicons guide
  - Custom XRPC endpoints guide
  - AT Protocol overview
  - Identity types reference
  - Session management guide
  - Error handling guide
  - ASP.NET Core integration guide
  - Blazor integration guide
  - Batch operations, blob upload, firehose, low-level repo guides
  - Full API reference
