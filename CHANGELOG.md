# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Breaking changes

- **`ATProtoNet.Server.EntityFrameworkCore` package merged into `ATProtoNet.Server`** (Issue #33) — The EF Core-backed `IAtProtoTokenStore` now ships inside `ATProtoNet.Server`. Remove the `ATProtoNet.Server.EntityFrameworkCore` `<PackageReference>` (it is replaced by `ATProtoNet.Server`); the `ATProtoNet.Server.EntityFrameworkCore` namespace, `AddAtProtoEfCoreTokenStore<TContext>()`, `AtProtoTokenDbContext`, and `AtProtoTokenEntity` are unchanged, so only the package reference changes. `ATProtoNet.Server` now transitively depends on `Microsoft.EntityFrameworkCore.Relational`
- **`ATProtoNet.Aspire` package merged into `ATProtoNet.Server`** (Issue #33) — The .NET Aspire client integration now ships inside `ATProtoNet.Server`. Remove the `ATProtoNet.Aspire` `<PackageReference>` (it is replaced by `ATProtoNet.Server`); the `ATProtoNet.Aspire` namespace, `AddAtProtoClient(...)`, `AtProtoClientSettings`, and `AtProtoPdsHealthCheck` are unchanged. `ATProtoNet.Server` now depends on `Microsoft.Extensions.Http.Resilience`

### Removed

- **`ATProtoNet.Server.EntityFrameworkCore` and `ATProtoNet.Aspire` NuGet packages** (Issue #33) — Consolidated into `ATProtoNet.Server`, cutting the published set from 8 packages to 6. See **Breaking changes** above for the (reference-only) migration

### Added

- **`AuthorizationServerDiscovery.HandleResolutionTimeout`** (Issue #52) — Per-round budget for handle resolution, enforced by a `CancellationTokenSource` linked to the caller's token. Default 5 s (`AuthorizationServerDiscovery.DefaultHandleResolutionTimeout`); `Timeout.InfiniteTimeSpan` restores the old unbounded behaviour. Configurable through the new `OAuthOptions.HandleResolutionTimeout` and `AtProtoOAuthServerOptions.HandleResolutionTimeout`
- **`AtProtoOAuthServerOptions.HttpClient`** (Issue #52) — Lets a consuming app supply the `HttpClient` used for OAuth discovery and token requests (e.g. an `IHttpClientFactory` client, a proxy, custom handlers). The supplied client's `Timeout` is left untouched and it is not disposed with the service
- **`AtProtoOAuthServerOptions.HttpClientTimeout`** (Issue #52) — Timeout applied to the SDK-created OAuth `HttpClient`. Default 30 s

### Fixed

- **OAuth handle resolution no longer stalls on a dead handle domain** (Issue #52) — Starting the OAuth flow for a handle whose domain silently drops packets on port 443 blocked for the full 100 s `HttpClient` default before the flow continued, so sign-in appeared to hang. `ResolveHandleToDidAsync` now races the HTTPS well-known lookup against the DNS-over-HTTPS TXT lookup (first DID wins) instead of trying them in sequence, and both it and `ResolveHandleAuthoritativeAsync` bound each round with `HandleResolutionTimeout` (5 s by default) so one unresponsive authority cannot dominate the flow. Caller cancellation still propagates; only budget expiry is treated as "no answer". The appview fallback gets its own fresh budget
- **SDK-created OAuth `HttpClient` no longer inherits the 100 s default timeout** (Issue #52) — `AtProtoOAuthService` now applies `AtProtoOAuthServerOptions.HttpClientTimeout` (30 s) to the client it creates, and only overwrites the `User-Agent` header when one isn't already set. `DidWebResolver`'s parameterless constructor applies a 10 s timeout for the same reason (the target host is embedded in the DID). `AtProtoClient`'s own client is unchanged — it carries blob uploads, where 100 s can be legitimate; pass a pre-configured `HttpClient` to shorten it
- **Polymorphic payloads with a non-leading `$type` failed to deserialize** (Issue #50) — The Bluesky appview serializes embed views (and real-world writers serialize record-internal unions) with the `$type` discriminator anywhere in the object, not necessarily first; System.Text.Json requires `AllowOutOfOrderMetadataProperties` for that. Now set in both `AtProtoJsonDefaults.Options` and `LexiconTypeRegistry.CreateOptions()`, fixing `getPosts`/`getPostThread`/timeline reads that contain embeds (previously threw `NotSupportedException`)
- **`Directory.Build.props` `RepositoryUrl` dropped the `.git` suffix** so Forgejo's NuGet registry can match the URL against the canonical repo URL on first upload. Without this, every new packable project published as an *orphan* (resolvable by `dotnet add package` but absent from the repo's Packages tab in the Forgejo UI), requiring a manual `POST /api/v1/packages/Grandiras/nuget/{name}/-/link/ATProto.NET` to relink after each first release. Affects new packages only; the five v0.4.0 packages already orphaned (`Aspire`, `Aspire.Hosting`, `Pds`, `LexiconGenerator`, `Server.EntityFrameworkCore`) were relinked manually post-release

### Security

- **`/.well-known/atproto-did` responses are now capped and redirect-checked** (Issue #52) — The endpoint lives on a host derived from untrusted user input. Responses are read with `HttpCompletionOption.ResponseHeadersRead` and capped at 1 KiB (by `Content-Length` and during the read, so a chunked body can't bypass it) instead of being buffered in full, and a response whose final request URI landed on a different host than the handle is ignored rather than trusted — a hostile handle domain can no longer redirect resolution at an arbitrary host and have that host's DID accepted. Same-host redirects (http→https, trailing slash) still resolve
- **Handle resolution now queries DNS-over-HTTPS on every attempt** (Issue #52) — Because `ResolveHandleToDidAsync` races the two lookups instead of trying HTTPS first, `dns.google` is contacted for every handle resolution, not only when the HTTPS well-known lookup fails. Deployments that treat the handle being resolved as sensitive should note the additional third-party disclosure. `ResolveHandleAuthoritativeAsync` already queried both on every call

## [0.4.0] - 2026-05-26

### Breaking changes

- **`LoginForm` default copy switched to "Atmosphere account" terminology** (Issue #32) — Default English copy now reads "Sign in with your Atmosphere account" / "Your Atmosphere account handle — your PDS is detected automatically." instead of "Sign in with AT Proto" / "Your AT Protocol handle…". Apps that relied on the previous strings (e.g. UI tests asserting on button text, screenshot tests, translation overlays keyed on the old defaults) must update their expectations or pass explicit `ButtonText`/`HandleHint` values to restore the old wording. `LoginForm`'s string parameters also changed type from `string` to `string?` to enable `IStringLocalizer<LoginForm>` resolution — source-compatible, no behaviour change for callers passing explicit values
- **`AtProtoClient.ApplyOAuthSessionAsync` signature change** — The method gained an optional `IAtProtoTokenStore? tokenStore` parameter inserted between `oauthClient` and `cancellationToken`. Source-compatible for callers using named arguments; **binary-incompatible** for positional callers — recompile required. Positional callers that previously passed `(session, client, ct)` must now pass `(session, client, null, ct)` or switch to named arguments. Required so factory-built clients can persist OAuth-refresh-rotated tokens back to the durable token store
- **`AtProtoClientFactory` constructor change** — Constructor gained an `IOAuthClientProvider? oauthClientProvider = null` parameter. Source-compatible for DI callers (Microsoft.Extensions.DependencyInjection auto-resolves the optional dependency); **binary-incompatible** for hand-rolled instantiation — recompile required. Without a registered `IOAuthClientProvider`, factory-built per-request clients cannot refresh expired OAuth tokens

### Added

- **"Atmosphere account" terminology & i18n in `LoginForm`** (Issue #32) — Default copy now uses the community-facing "Atmosphere account" umbrella term
  - New `HeadingText` and `SubtitleText` parameters for an optional heading/subtitle rendered above the form (no default — rendered only when set)
  - Optional `IStringLocalizer<LoginForm>` injection: when registered (e.g. via `services.AddLocalization()` with a `.resx` source), default copy is resolved by parameter name (`ButtonText`, `HandleLabel`, `HandleHint`, etc.); explicit parameter values still take precedence
  - String parameters changed from `string` to `string?` so consumers can opt in to localizer-resolved defaults

- **Aspire hosting integration for PDS containers** (Issue #31) — New `ATProtoNet.Aspire.Hosting` package for adding the official Bluesky PDS container to .NET Aspire AppHosts
  - `AtProtoPdsContainerResource` — Aspire container resource representing a `ghcr.io/bluesky-social/pds` instance with `IResourceWithConnectionString` support
  - `AddAtProtoPds()` extension on `IDistributedApplicationBuilder` — Adds the PDS container with auto-generated secrets (admin password, JWT secret, PLC rotation key), dev mode enabled by default, and a persistent data volume
  - Fluent configuration: `WithHostname()`, `WithPlcUrl()`, `WithAppView()`, `WithCrawlers()`, `WithProductionMode()`, `WithBlobUploadLimit()`, `WithReportService()`, `WithEmail()`
  - Configurable port mapping and image tag selection
  - Replaces the need for manual Docker/Podman PDS setup during development

- **PDS hosting package** (Issue #2) — New `ATProtoNet.Pds` package for building AT Protocol Personal Data Servers
  - `PdsService` — core business logic for account management, session handling, record CRUD, and blob operations
  - `PdsSessionService` — JWT token issuing and validation with HMAC-SHA256 signing
  - `IAccountStore` / `InMemoryAccountStore` — pluggable account persistence with DID, handle, email, and signing key management
  - `IRepoStore` / `InMemoryRepoStore` — pluggable repository storage for records and blobs with cursor-based pagination
  - `PdsHostingExtensions` — `AddAtProtoPds()` DI registration and `MapAtProtoPds()` XRPC endpoint mapping
  - Full XRPC endpoint support: `com.atproto.server.createAccount`, `createSession`, `getSession`, `refreshSession`, `describeServer`
  - Repository endpoints: `com.atproto.repo.createRecord`, `getRecord`, `putRecord`, `deleteRecord`, `listRecords`
  - Blob endpoints: `com.atproto.repo.uploadBlob`, `com.atproto.sync.getBlob`
  - Custom store implementations via `AddAtProtoPds<TAccountStore, TRepoStore>()`
  - PBKDF2 password hashing with 100k iterations, SHA-256 based CID computation
  - Bearer token authentication with authorization checks for repo ownership

- **Native Standard.site integration** (Issue #9) — First-class support for Standard.site long-form publishing lexicons
  - `PublicationRecord` model for `site.standard.publication` — blog/site identity with URL, name, description, icon, theme, and preferences
  - `DocumentRecord` model for `site.standard.document` — published documents with title, path, tags, content union, cover image, and Bluesky post reference
  - `SubscriptionRecord` model for `site.standard.graph.subscription` — follow/subscribe to publications
  - `BasicTheme`, `ThemeColorRgb`, `ThemeColorRgba` models for `site.standard.theme.basic` and `site.standard.theme.color`
  - `StandardSiteClient` with full CRUD for publications, documents, and subscriptions via AT Protocol repo operations
  - Exposed as `AtProtoClient.Site` property, following the same pattern as `Bsky`, `Chat`, and `Ozone`
- **Lexicon migrations and publishing** (Issue #14) — Schema migration pipeline and publishing workflow for the `atproto-lexgen` CLI tool
  - `ILexiconMigration` interface and `DelegateMigration` for record transforms between schema revisions
  - `MigrationBuilder` fluent API for composing migrations: `AddProperty`, `RemoveProperty`, `RenameProperty`, `Apply`
  - `LexiconMigrationRunner` — builds and executes ordered migration chains, validates continuity, scaffolds migrations from `DiffResult`
  - `LexiconPublisher` — publishes schemas to directories with baseline diff validation, auto-revision bumping, and breaking change detection
  - `atproto-lexgen migrate` CLI command — scaffold migrations from schema diffs or apply migration files to JSON records
  - `atproto-lexgen publish` CLI command — publish schemas with version tracking, `--force` for breaking changes, `--no-bump` option
  - JSON migration file format with `addProperty`, `removeProperty`, `renameProperty` operations

- **Ozone moderation client** (Issue #18) — Full `tools.ozone.*` namespace support via `client.Ozone`
  - `OzoneClient` top-level client aggregating all Ozone sub-clients
  - `ModerationClient` — emitEvent, getEvent, getRecord, getRepo, queryEvents, querySubjects, searchRepos
  - `CommunicationClient` — createTemplate, deleteTemplate, listTemplates, updateTemplate
  - `TeamClient` — addMember, deleteMember, listMembers, updateMember
  - `SetClient` — upsertSet, deleteSet, addValues, deleteValues, getValues, querySets
  - `OzoneServerClient` — getConfig
  - `SignatureClient` — findCorrelation, searchAccounts, findRelatedAccounts
  - Polymorphic moderation event types (takedown, label, comment, mute, email, tag, etc.)
  - `SubjectReviewState` and `TeamMemberRole` constants

- **Aspire integration package** (Issue #5) — `ATProtoNet.Aspire` package for .NET Aspire service defaults
  - `AddAtProtoClient()` extension on `IHostApplicationBuilder` — registers `AtProtoClient` as a singleton with configuration binding, `IHttpClientFactory`, and optional standard resilience
  - `AtProtoClientSettings` for `IConfiguration` binding (InstanceUrl, RelayUrl, AutoRefreshSession, DisableHealthChecks, DisableResilience)
  - `AtProtoPdsHealthCheck` — health check verifying PDS connectivity via `com.atproto.server.describeServer`
  - Standard HTTP resilience (retry, circuit breaker) via `Microsoft.Extensions.Http.Resilience`

- **Lexicon plugin support for custom types** (Issue #6) — Runtime registration of custom record types and union variants via NuGet packages
  - `ILexiconPlugin` interface for plugins to register custom types at startup
  - `ILexiconTypeRegistrar` for registering record types and union variants
  - `[LexiconPlugin]` assembly attribute for auto-discovery
  - `LexiconTypeRegistry` — Singleton registry with `LoadPlugin<T>()`, `LoadPluginsFromAssembly()`, and `CreateOptions()` for plugin-aware JSON serialization
  - Runtime union variant registration augments built-in `[JsonDerivedType]` attributes via `JsonTypeInfo` modifier

- **Missing sync endpoints & Sync v1.1 support** (Issue #21) — Complete com.atproto.sync coverage and Sync v1.1 fields
  - `SyncClient.GetRepoStatusAsync` — Get repository hosting status
  - `SyncClient.ListHostsAsync` — Enumerate upstream hosts consumed by a relay
  - `SyncClient.GetHostStatusAsync` — Get status of a specified upstream host
  - `SyncClient.ListReposByCollectionAsync` — Enumerate DIDs with records in a given collection
  - `AccountHostingStatus` constants: takendown, suspended, deleted, deactivated, desynchronized, throttled
  - `HostStatus` constants: active, idle, offline, throttled, banned
  - `SyncEvent` (#sync) firehose message type for Sync v1.1 repo state recovery
  - `CommitEvent.PrevData` and `CommitEvent.Blobs` Sync v1.1 fields
  - `RepoOp.Prev` field for inductive firehose verification

- **Auto-register XRPC endpoints with DI** (Issue #15) — Server-side XRPC endpoint handler infrastructure
  - `IXrpcEndpoint` — Base interface for XRPC endpoint handlers with NSID identification
  - `IXrpcQuery<TParams, TOutput>` / `IXrpcQuery<TOutput>` — Interfaces for XRPC query endpoints (GET)
  - `IXrpcProcedure<TInput, TOutput>` / `IXrpcProcedureVoid<TInput>` — Interfaces for XRPC procedure endpoints (POST)
  - `[XrpcEndpoint]` attribute — Assembly scanning marker with optional NSID override
  - `AddXrpcEndpoint<T>()` — Register a single XRPC endpoint handler in DI
  - `AddXrpcEndpointsFromAssembly()` — Assembly scanning for `[XrpcEndpoint]`-attributed handlers
  - `MapXrpcEndpoints()` — Maps all registered handlers as ASP.NET Core minimal API routes at `/xrpc/{nsid}`
  - Query parameter binding, JSON body deserialization, and XRPC error format support

- **Firehose event parsing, verification, and typed consumer** (Issue #27) — Full firehose commit verification pipeline and advanced consumer
  - `FirehoseEventParser` — Decodes raw CBOR firehose frames into typed `FirehoseMessage` objects with DAG-CBOR JSON normalization
  - `FirehoseVerifier` — CID integrity verification for commit and sync events, commit signature verification against DID document signing keys
  - `VerificationResult` — Structured verification result with error details
  - `TypedFirehoseConsumer` — High-level consumer with CBOR parsing, collection filtering, CID/signature verification, and periodic cursor persistence
  - `TypedFirehoseConsumerOptions` — Configuration for verification, collection filters, cursor persistence interval, and reconnection
  - `IFirehoseCursorStore` — Interface for persistent cursor storage enabling resumable firehose consumption
  - `InMemoryFirehoseCursorStore` — In-memory cursor store for development and testing

- **Merkle Search Tree (MST) implementation** (Issue #19) — Full in-memory MST for AT Protocol repository data structure
  - `MstKeyDepth` — Computes key depth via SHA-256 leading-zero counting (fanout 4), matching AT Protocol spec
  - `MstNodeData` — CBOR-serializable MST node with deterministic DAG-CBOR encoding/decoding via tag 42 CID links
  - `MerkleSearchTree` — Complete MST with `Add`, `Update`, `Delete`, `Get`, `GetEntries`, `Serialize`/`Deserialize`, `ComputeRootCid`, and `Validate` operations
  - Layer-based top-down construction, DoS protection limits (max 256 entries/node, max 64 depth)

- **chat.bsky DM support** (Issue #17) — Full Bluesky direct messaging client
  - `ConvoClient` — 17 endpoints: `ListConvos`, `GetConvo`, `GetConvoForMembers`, `GetConvoAvailability`, `GetMessages`, `SendMessage`, `SendMessageBatch`, `DeleteMessageForSelf`, `LeaveConvo`, `MuteConvo`, `UnmuteConvo`, `UpdateRead`, `UpdateAllRead`, `AcceptConvo`, `AddReaction`, `RemoveReaction`, `GetLog`
  - `ChatActorClient` — `DeleteAccount`, `ExportAccountData`
  - `ChatClients` grouping accessible via `AtProtoClient.Chat`
  - All chat requests automatically proxied via per-request `atproto-proxy` header (requires `transition:chat.bsky` OAuth scope)
  - Per-request proxy override support in `XrpcClient` — chat proxy doesn't affect other XRPC calls
  - Complete model types: `ConvoView`, `MessageView`, `DeletedMessageView`, `ChatMemberView`, `MessageInput`, reaction models, all request/response types
  - `ChatDeclarationRecord` and `ChatAllowIncoming` constants (all/none/following)

- **Labeler service support** (Issue #25) — Labeler service information, label definitions, and header management
  - `LabelerClient.GetServicesAsync` — Fetch labeler service info and label value definitions
  - `LabelerServiceRecord` — Record type for declaring labeler services with policies
  - `LabelValueDefinition` — Custom label definitions with severity, blur behavior, default settings, and localized strings
  - `LabelerViewDetailed`, `LabelerView`, `LabelerViewerState` — View types for labeler services
  - `SetLabelers()`/`ClearLabelers()` on `XrpcClient` and `AtProtoClient` — Automatic `atproto-accept-labelers` header injection
  - `StandardLabelValues` constants: porn, sexual, nudity, graphic-media, gore, spam, impersonation, etc.
  - `LabelSeverity`, `LabelBlurs`, `LabelDefaultSetting` constant classes
  - `LabelerClient` wired into `BlueskyClients.Labeler`

- **atproto-proxy header support** (Issue #24) — Route XRPC requests through AT Protocol service proxies
  - `ServiceProxy` static helper with `Build()` method and well-known constants (`BskyAppView`, `BskyChat`, `AtProtoLabeler`, `AtProtoPds`)
  - Pre-built header values: `BskyAppViewHeader`, `BskyChatHeader`, `BskyAppViewDid`, `BskyChatDid`
  - `SetProxy()` / `ClearProxy()` methods on both `XrpcClient` and `AtProtoClient`

- **did:web resolver & unified DID resolution** (Issue #28) — Resolve `did:web` identifiers and dispatch to correct resolver
  - `DidWebResolver` — Fetches `https://<domain>/.well-known/did.json`, validates document ID matches, SSRF prevention (IP address blocking), HTTPS enforcement, localhost exception for development
  - `DidResolver` — Unified dispatcher: `did:plc` → `PlcClient`, `did:web` → `DidWebResolver`
  - `DidWebException` with typed `DidWebErrorKind` (InvalidDid, NotFound, HttpError, NetworkError, ParseError, ValidationError)

- **Missing Bluesky graph features** (Issue #26) — Starter packs, relationships, thread muting, and postgate support
  - Records: `StarterPackRecord`, `StarterPackFeedItem`, `PostgateRecord`
  - Views: `StarterPackView`, `StarterPackViewBasic`
  - Relationships: `Relationship`, `NotFoundActor`, `GetRelationshipsResponse`, `GetKnownFollowersResponse`
  - Starter pack responses: `GetStarterPackResponse`, `GetStarterPacksResponse`, `GetActorStarterPacksResponse`, `SearchStarterPacksResponse`
  - `GraphClient` methods: `GetRelationshipsAsync`, `GetKnownFollowersAsync`, `MuteThreadAsync`, `UnmuteThreadAsync`, `GetStarterPackAsync`, `GetStarterPacksAsync`, `GetActorStarterPacksAsync`, `SearchStarterPacksAsync`

- **Video upload & processing client** (Issue #22) — `app.bsky.video.*` XRPC endpoints
  - `VideoClient` with `UploadVideoAsync`, `GetJobStatusAsync`, `GetUploadLimitsAsync`
  - `VideoModels`: `JobStatus`, `JobState` constants, `GetJobStatusResponse`, `UploadVideoResponse`, `GetUploadLimitsResponse`
  - `VideoClient` wired into `BlueskyClients.Video`

- **Well-known Bluesky permission set NSIDs** (Issue #29) — `AtProtoScopes.PermissionSets` constants
  - Constants for all `app.bsky.auth*` permission sets: `FullApp`, `ManageProfile`, `CreatePosts`, `DeletePosts`, `ManagePosts`, `ManageFollows`, `ManageListsAndPacks`, `ViewNotifications`, `ManageNotifications`, `ManageFeedDeclarations`, `ManageLabelerService`, `ManagePreferences`, `ManageModeration`, `ViewAll`

- **Atproto-Repo-Rev header tracking** (Issue #30) — Automatic extraction and exposure of repository revision headers
  - `LatestRepoRev` property on `XrpcClient` and `AtProtoClient`
  - Extracted from all XRPC responses via the `Atproto-Repo-Rev` header

- **HTTP rate limiting with automatic retry** (Issue #23) — Built-in 429 handling with configurable retry behavior
  - `RateLimitInfo` model with Limit, Remaining, Reset, and IsExceeded properties
  - Automatic retry on HTTP 429 with `Retry-After` / `RateLimit-Reset` header support and exponential backoff fallback
  - `LatestRateLimitInfo` property on `XrpcClient` and `AtProtoClient`
  - Configurable `MaxRateLimitRetries` (default: 3, set to 0 to disable)

- **DAG-CBOR encoding/decoding layer** (Issue #20) — DRISL-CBOR implementation for AT Protocol data model
  - `DagCborEncoder` — Deterministic CBOR encoding with sorted map keys, `$link` → CID tag 42, `$bytes` → byte string, float rejection
  - `DagCborDecoder` — CBOR decoding with CID tag 42 → `$link`, byte string → `$bytes`, validation of sorted keys and no-float constraints
  - `CidComputation` — CIDv1 computation with SHA-256, DAG-CBOR (0x71) and raw (0x55) codecs, Base32Lower encoding/decoding, CID verification

- **OAuth scope constants & granular permission builders** (`AtProtoScopes`) — Full AT Protocol Permissions spec support
  - Transitional scope constants: `AtProto`, `TransitionGeneric`, `TransitionChatBsky`, `TransitionEmail`
  - Convenience presets: `Default`, `WithChat`, `AuthOnly`
  - `Repo()` — Record collection permissions with `RepoAction` flags (Create, Update, Delete), single or multiple collections, wildcard support
  - `Rpc()` — Service authentication (RPC) permissions with Lexicon method and audience parameters, DID fragment encoding
  - `Blob()` — Blob upload permissions with MIME type patterns (`*/*`, `video/*`, etc.)
  - `Account()` — Account attribute permissions (email, repo, status) with Read/Manage actions
  - `Identity()` — Identity attribute permissions (handle, wildcard) with Manage/Submit actions
  - `Include()` — Permission set references for published Lexicon-based permission bundles with optional audience inheritance
  - `Combine()` — Merge and deduplicate multiple scope strings
  - Replaced hardcoded scope strings in `OAuthModels` and `AtProtoOAuthServerOptions` with `AtProtoScopes.Default`

- **Custom relay URL configuration** (Issue #8) — Configurable relay WebSocket URL for firehose
  - `WithRelayUrl()` on `AtProtoClientBuilder` (default: `wss://bsky.network`)
  - `RelayUrl` property on `AtProtoClientOptions`
  - `CreateFirehoseClient()` and `CreateFirehoseConsumer()` convenience methods on `AtProtoClient`

- **EF Core token store** (`ATProtoNet.Server.EntityFrameworkCore`) — New package for database-backed token storage (Issue #3)
  - `EfCoreAtProtoTokenStore<TContext>` — Generic `IAtProtoTokenStore` implementation using `IDbContextFactory<TContext>`
  - ASP.NET Core Data Protection encryption for stored tokens
  - `AtProtoTokenEntity` with DID primary key
  - `AtProtoTokenDbContext` with `ConfigureAtProtoTokenModel()` for use in custom DbContexts
  - `AddAtProtoEfCoreTokenStore<TContext>()` DI extension

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

- **Lexicon code generator packaging** — `atproto-lexgen` is now a publishable `dotnet tool`
  - NuGet package metadata: `PackageId`, `Version`, `Authors`, `PackageTags`, `License`, `RepositoryUrl`
  - Install globally via `dotnet tool install -g ATProtoNet.LexiconGenerator`

- **Documentation** — Comprehensive documentation for all new features
  - New guides: PDS Hosting, Chat & DMs, Ozone Moderation, Standard.site, .NET Aspire, Video Upload, Labeler Services, Cryptography, DID Resolution, Lexicon Code Generator, XRPC Endpoint Handlers
  - Updated guides: Firehose Streaming (TypedFirehoseConsumer, verification, cursor persistence), Getting Started (new packages, builder options), Server Integration (EF Core token store), API Reference (all new client types)

### Fixed

- **OAuth, firehose, and repo correctness pass (F1–F15 + G1–G14 + review follow-up)** — Series of fixes addressing review findings across the OAuth, firehose, and repo subsystems
  - **Commit signature verification (`FirehoseVerifier`)** — Use `CborConformanceMode.Strict` instead of `Ctap2Canonical`. The previous mode forbade all CBOR tags, but DAG-CBOR requires tag 42 for CIDs, so every real commit threw `CborContentException` and verification failed for the wrong reason. Canonical-form integrity is preserved by the byte-for-byte splice of the original buffer
  - **MST canonical form (`MerkleSearchTree`)** — Restored empty parent-layer wrapping in `SplitAndInsert` and added matching empty-parent wrapping in `BuildLayerTopDown` so incremental `Add` and bulk `CreateFromEntries` produce the same root CID as atproto/ts. `Create(entries)` now delegates to `CreateFromEntries` so both public factories use the spec-conformant builder
  - **Firehose at-least-once semantics (`FirehoseConsumer`)** — Reconnect cursor only advances when the consumer calls `Acknowledge(seq)`. When `Acknowledge` is never called, the cursor falls back to the current frame's seq (at-most-once); the docstring spells out the contract explicitly. The monotonic floor is now pre-seeded with the caller's resume cursor so a hostile first frame can't rewind below the intended resume point
  - **CAR block CID codec policy (`CarReader` + `FirehoseVerifier`)** — `VerifyAllBlockCids` now throws on `UnknownCodec` in addition to `Mismatch`. The static `FirehoseVerifier.VerifyCarBlockCids` path also fails closed on `UnknownCodec`, so the cheap pre-check and the full signature path apply the same policy
  - **OAuth refresh persistence (`AtProtoClient`)** — Rotated tokens are written to `IAtProtoTokenStore` BEFORE the in-memory session is mutated. A store-write failure now surfaces immediately rather than silently desyncing memory and disk (the old failure mode left the persisted store with the dead refresh token, logging users out on next process restart)
  - **OAuth refresh token store wired** — `AtProtoClient.ApplyOAuthSessionAsync` gained an optional `IAtProtoTokenStore? tokenStore` parameter that `AtProtoClientFactory` passes through, so refresh-rotated tokens land in durable storage instead of only the per-request `InMemorySessionStore`
  - **Refresh-lock around `ApplyOAuthSessionAsync`** — The session swap now holds `_refreshLock`, preventing a timer-driven refresh from racing the swap and corrupting state
  - **Bounded timer-driven refresh** — `OnRefreshTimerElapsed` uses a 30-second `CancellationTokenSource` so a slow token endpoint can't pin `_refreshLock` indefinitely and block foreground `LogoutAsync`/`ApplyOAuthSessionAsync`
  - **`Dispose` race with timer callback** — Sync `Dispose()` drains in-flight callbacks via `Timer.Dispose(WaitHandle)`; the callback's `Release` is wrapped in `try`/`catch ObjectDisposedException` so a late-firing release on a disposed semaphore can no longer escape `async void` and crash the process. `_oauthSession` and `_refreshLock` are now disposed in `Dispose` and `DisposeAsync`
  - **`LogoutAsync` clears `_oauthTokenStore`** — Defensive cleanup so a subsequent re-login with a different `tokenStore` arg doesn't inherit a stale reference
  - **`OAuthClient` constructed lazily on `IOAuthClientProvider.TryGetClient`** — Only when explicit `ClientMetadata` is configured (the production case). Loopback callers must still drive `StartLoginAsync` to materialize a client, since the loopback `client_id` encodes the live request's callback URL
  - **JWT pre-validator algorithm allowlist (`AtProtoAuthenticationHandler`)** — Now allowlists `ES256`/`ES256K`/`ES384`/`ES512`/`EdDSA`/`RS256`/`RS384`/`RS512`/`PS256`/`PS384`/`PS512` only. Previously only rejected `alg=none`, so symmetric HS256 forgeries reached the PDS unchallenged
  - **Handle resolution requires HTTPS + DNS agreement (`AuthorizationServerDiscovery`)** — `ResolveHandleAuthoritativeAsync` now runs HTTPS well-known and DNS-over-HTTPS lookups concurrently and fails closed when they return different DIDs. (Note: both transports currently share the same TLS trust root via `dns.google` — true authority diversification needs a system DNS path)
  - **`did:web` id comparison is case-insensitive for host** (`AuthorizationServerDiscovery`) — DNS host names are case-insensitive per RFC 1035; the prior strict `Ordinal` compare rejected valid `did:web:Example.com` documents. `did:plc` remains strictly case-sensitive
  - **`AtProtoTokenData` / `OAuthSessionResult` gained `IsHandleVerified`** — Persisted and restored across factory hydration. Default-claims now emit `"handle.invalid"` as `ClaimTypes.Name` when the handle isn't bidirectionally verified, with an explicit `handle_verified` claim alongside the actual `did` and `handle`. **Behavior change for existing OAuth sessions:** tokens persisted before this release deserialize with `IsHandleVerified=false`, so `User.Identity.Name` shows `"handle.invalid"` until users re-login
  - **`TryReadSeq` propagates `OperationCanceledException`** — Previously swallowed by an unfiltered catch, breaking cancellation propagation through the cursor-advance logic
  - **`WriteMapHeader` rejects oversized counts** — Throws `ArgumentOutOfRangeException` on negative counts and now emits the 4-byte (CBOR 0x1a) header for counts ≥ 65536. Previously silently truncated to 16 bits, producing malformed CBOR
  - **`AtProtoClient.Dispose`/`DisposeAsync` releases `_oauthSession` and `_refreshLock`** — DPoP ECDsa key and SemaphoreSlim wait handles no longer leak to GC finalization

- **Packaging & release pipeline** — Release artifact hygiene
  - `Aspire.Hosting` dependency in `ATProtoNet.Aspire.Hosting` upgraded from `9.2.1` to `9.5.2`, picking up `KubernetesClient 17.0.14` and resolving the transitive moderate-severity NU1902 advisory (GHSA-w7r3-mgwf-4mqq)
  - `Microsoft.EntityFrameworkCore.Relational` dependency in `ATProtoNet.Server.EntityFrameworkCore` upgraded from `10.0.0-preview.4.25258.110` to stable `10.0.0` (resolves NU5104 "stable release should not have a prerelease dependency")
  - `FirehoseConsumerSample` marked `IsPackable=false` so it no longer leaks into `dotnet pack` output
  - Removed duplicate `README.md` `<None Include>` items from `ATProtoNet`, `ATProtoNet.Server`, and `ATProtoNet.Blazor` csprojs — `Directory.Build.props` already packs the root README into every package (resolves NU5118)
  - Removed stale hardcoded `<Version>0.3.0</Version>` and duplicated package metadata from `ATProtoNet.LexiconGenerator.csproj` so it inherits the shared version from `Directory.Build.props`
  - Removed the `package` job from `.forgejo/workflows/ci.yml`; publishing is now driven exclusively by the `release` workflow (triggered by `v*` tags or manual `workflow_dispatch`), so version bumps on `main` no longer publish to the Forgejo NuGet feed before a release tag is cut

- **Issue templates** — Converted from invalid hybrid format (YAML frontmatter + Markdown body in `.yml` files) to proper Forgejo YAML form templates with structured `body:` sections

- **Cryptographic security hardening** — Fixes from security audit of crypto primitives
  - **Low-S normalization** — `NormalizeLowS` was a complete no-op (dead code). Now compares S against the actual curve half-order and computes `order - S` when needed. Prevents signature malleability.
  - **High-S signature rejection** — `Verify()` now rejects signatures with S > half-order, enforcing AT Protocol's low-S requirement
  - **`ImportPrivateKey` curve validation** — Validates the imported key's curve OID matches the declared `KeyCurve` parameter. Prevents silent identity corruption from curve mismatch.
  - **`DecompressPoint` range check** — Validates X coordinate is in range `[0, p)` before modular arithmetic
  - **JWT `audience` validation** — `ServiceAuthGenerator.CreateToken` now rejects null/whitespace audience
  - **Base58 performance** — Replaced LINQ `.Any()` with a `for` loop in hot path
  - 4 new crypto security tests (455 total)

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
