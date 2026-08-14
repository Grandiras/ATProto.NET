# API Reference

Complete listing of the ATProto.NET public API surface.

## AtProtoClient

The main entry point. Created via `AtProtoClientBuilder` or direct construction.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Session` | `Session?` | Current session (null if not authenticated) |
| `IsAuthenticated` | `bool` | Whether the client has an active session |
| `Did` | `string?` | Authenticated user's DID |
| `Handle` | `string?` | Authenticated user's handle |
| `LatestRepoRev` | `string?` | Latest repo revision from server responses |
| `LatestRateLimitInfo` | `RateLimitInfo?` | Most recent rate limit info |
| `Server` | `ServerClient` | `com.atproto.server.*` methods |
| `Repo` | `RepoClient` | `com.atproto.repo.*` methods |
| `Identity` | `IdentityClient` | `com.atproto.identity.*` methods |
| `Sync` | `SyncClient` | `com.atproto.sync.*` methods |
| `Admin` | `AdminClient` | `com.atproto.admin.*` methods |
| `Label` | `LabelClient` | `com.atproto.label.*` methods |
| `Moderation` | `ModerationClient` | `com.atproto.moderation.*` methods |
| `Bsky` | `BlueskyClients` | `app.bsky.*` sub-clients |
| `Chat` | `ChatClients` | `chat.bsky.*` sub-clients |
| `Ozone` | `OzoneClient` | `tools.ozone.*` sub-clients |
| `Site` | `StandardSiteClient` | `site.standard.*` records |
| `OAuthSession` | `OAuthSessionResult?` | The applied OAuth session, if any |
| `PdsUrl` | `string` | The service URL requests currently go to |

### Custom Lexicon Methods

| Method | Description |
|--------|-------------|
| `GetCollection<T>(collection)` | Get a typed `RecordCollection<T>` for CRUD |
| `QueryAsync<T>(nsid, parameters?)` | Call a custom XRPC query (GET) |
| `ProcedureAsync<T>(nsid, body?)` | Call a custom XRPC procedure (POST) with response |
| `ProcedureAsync(nsid, body?)` | Call a custom XRPC procedure (POST) without response |

### Authentication Methods

| Method | Description |
|--------|-------------|
| `LoginAsync(identifier, password, authFactorToken?)` | Authenticate and create a session |
| `ResumeSessionAsync(session)` | Resume from a saved `Session` |
| `RefreshSessionAsync()` | Manually refresh session tokens |
| `LogoutAsync()` | Destroy the session |
| `ApplyOAuthSessionAsync(oauthSession)` | Adopt an `OAuthSessionResult` (sets PDS URL, DPoP, session) |
| `SetPdsUrl(url)` | Point the client at a different PDS at runtime |

### Streaming, Proxying & Labelers

| Method | Description |
|--------|-------------|
| `CreateFirehoseClient()` | Low-level `FirehoseClient` bound to the configured relay |
| `CreateFirehoseConsumer(...)` | Reconnecting `FirehoseConsumer` |
| `SetProxy(header)` / `ClearProxy()` | Set the `atproto-proxy` header for subsequent calls |
| `SetLabelers(dids)` / `ClearLabelers()` | Set the `atproto-accept-labelers` header |

### Bluesky Convenience Methods

| Method | Description |
|--------|-------------|
| `PostAsync(text, facets?, embed?, reply?, langs?, labels?)` | Create a text post (returns `CreateRecordResponse`) |
| `LikeAsync(uri, cid)` | Like a post |
| `UnlikeAsync(likeUri)` | Unlike a post |
| `RepostAsync(uri, cid)` | Repost a post |
| `UndoRepostAsync(repostUri)` | Undo a repost |
| `FollowAsync(did)` | Follow an actor |
| `UnfollowAsync(followUri)` | Unfollow an actor |
| `DeletePostAsync(postUri)` | Delete a post |
| `UpdateProfileAsync(displayName?, description?, avatar?, banner?)` | Update profile |

---

## RecordCollection\<T\>

Typed CRUD interface for a specific Lexicon collection. `Collection` exposes the NSID it is bound to.

| Method | Description |
|--------|-------------|
| `CreateAsync(record, rkey?, validate?)` | Create a new record |
| `GetAsync(rkey, cid?)` | Get a record by key |
| `GetFromAsync(repo, rkey, cid?)` | Get a record from another user |
| `PutAsync(rkey, record, validate?, swapRecord?)` | Create or update a record |
| `DeleteAsync(rkey, swapRecord?)` | Delete a record |
| `ListAsync(limit?, cursor?, reverse?)` | List records with pagination |
| `ListFromAsync(repo, limit?, cursor?, reverse?)` | List records from another user |
| `EnumerateAsync(pageSize?)` | Enumerate all records (auto-pagination) |
| `EnumerateFromAsync(repo, pageSize?)` | Enumerate from another user |
| `ExistsAsync(rkey)` | Check if a record exists |

---

## AtProtoRecord

Base class for custom record types.

| Property | Type | Description |
|----------|------|-------------|
| `Type` | `string` (abstract) | Lexicon NSID (`$type` field) |
| `CreatedAt` | `string` | ISO 8601 timestamp (auto-populated) |

---

## RecordRef

Reference to a created/updated record.

| Property | Type | Description |
|----------|------|-------------|
| `Uri` | `string` | AT URI of the record |
| `Cid` | `string` | Content hash |
| `RecordKey` | `string` | Record key portion of the URI |

Returned by `RecordCollection<T>.CreateAsync` / `PutAsync`. The `RepoClient` methods return the raw
`CreateRecordResponse` / `PutRecordResponse` instead, which also carry `Commit` (`CommitMeta` with
`Cid` and `Rev`).

---

## RecordView\<T\>

A record fetched from the repository.

| Property | Type | Description |
|----------|------|-------------|
| `Uri` | `string` | AT URI |
| `Cid` | `string?` | Content hash |
| `Value` | `T` | Deserialized record value |
| `RecordKey` | `string` | Record key |

---

## RecordPage\<T\>

Paginated list of records.

| Property | Type | Description |
|----------|------|-------------|
| `Records` | `List<RecordView<T>>` | Records in this page |
| `Cursor` | `string?` | Cursor for next page |
| `HasMore` | `bool` | Whether more pages exist |

---

## AtProtoClientBuilder

Fluent builder for `AtProtoClient`.

| Method | Description |
|--------|-------------|
| `WithInstanceUrl(url)` | Set the PDS/service URL |
| `WithRelayUrl(url)` | Set the relay WebSocket URL for firehose |
| `WithAutoRefreshSession(bool)` | Enable/disable auto token refresh |
| `WithSessionStore(store)` | Set custom session persistence |
| `WithHttpClient(client)` | Use a custom HttpClient |
| `WithLoggerFactory(factory)` | Set logging factory |
| `Build()` | Create the `AtProtoClient` |

---

## Identity Types

| Type | Description | Example |
|------|-------------|---------|
| `Did` | Decentralized Identifier | `did:plc:abc123` |
| `Handle` | Domain-name identifier | `alice.bsky.social` |
| `AtIdentifier` | DID or Handle union | Either of the above |
| `Nsid` | Namespaced Identifier | `com.example.todo.item` |
| `AtUri` | AT Protocol URI | `at://did:plc:abc/col/rkey` |
| `Tid` | Timestamp Identifier | `3k2la7rxjgs2t` |
| `RecordKey` | Record key | `self`, `3k2la7rxjgs2t` |
| `Cid` | Content Identifier | `bafyrei...` |

All support: `Parse()`, `TryParse()`, equality, implicit string conversion.

---

## ServerClient (`com.atproto.server.*`)

| Method | Description |
|--------|-------------|
| `CreateSessionAsync(identifier, password, authFactorToken?)` | Login |
| `GetSessionAsync()` | Get current session info |
| `RefreshSessionAsync()` | Refresh tokens |
| `DeleteSessionAsync()` | Logout |
| `CreateAccountAsync(email, handle, password, inviteCode?)` | Create new account |
| `CreateAppPasswordAsync(name)` | Create an app password |
| `ListAppPasswordsAsync()` | List app passwords |
| `RevokeAppPasswordAsync(name)` | Revoke an app password |
| `CreateInviteCodeAsync(useCount)` | Generate invite code |
| `CreateInviteCodesAsync(codeCount, useCount)` | Generate multiple invite codes |
| `GetAccountInviteCodesAsync()` | List account's invite codes |
| `RequestPasswordResetAsync(email)` | Request password reset |
| `ResetPasswordAsync(token, password)` | Reset password with token |
| `ConfirmEmailAsync(email, token)` | Confirm email |
| `RequestEmailConfirmationAsync()` | Request confirmation email |
| `RequestEmailUpdateAsync()` | Request email update |
| `UpdateEmailAsync(email, emailAuthFactor?, token?)` | Update email |
| `ReserveSigningKeyAsync(did?)` | Reserve a signing key |
| `DescribeServerAsync()` | Get server description |
| `GetServiceAuthAsync(aud, exp?)` | Get service auth token |
| `ActivateAccountAsync()` | Activate a deactivated account |
| `DeactivateAccountAsync(deleteAfter?)` | Deactivate account |
| `DeleteAccountAsync(did, password, token)` | Delete account permanently |
| `CheckAccountStatusAsync()` | Check account status |

---

## RepoClient (`com.atproto.repo.*`)

| Method | Description |
|--------|-------------|
| `CreateRecordAsync(repo, collection, record, rkey?, validate?, swapCommit?)` | Create record |
| `GetRecordAsync(repo, collection, rkey, cid?)` | Get record (untyped) |
| `GetRecordAsync<T>(repo, collection, rkey, cid?)` | Get record (typed) |
| `PutRecordAsync(repo, collection, rkey, record, validate?, swapRecord?, swapCommit?)` | Put record |
| `DeleteRecordAsync(repo, collection, rkey, swapRecord?, swapCommit?)` | Delete record |
| `ListRecordsAsync(repo, collection, limit?, cursor?, reverse?)` | List records |
| `ListAllRecordsAsync(repo, collection, pageSize?)` | Enumerate all records |
| `DescribeRepoAsync(repo)` | Get repo info |
| `UploadBlobAsync(stream, mimeType)` | Upload blob from stream |
| `UploadBlobAsync(filePath, mimeType)` | Upload blob from file |
| `UploadBlobAsync(data, mimeType)` | Upload blob from bytes |
| `ApplyWritesAsync(repo, writes, validate?, swapCommit?)` | Batch write operations |
| `ListMissingBlobsAsync(limit?, cursor?)` | List missing blobs |

---

## Session & Auth

### Session

| Property | Type | Description |
|----------|------|-------------|
| `Did` | `string` | User's DID |
| `Handle` | `string` | User's handle |
| `AccessJwt` | `string` | Access token |
| `RefreshJwt` | `string` | Refresh token |
| `Email` | `string?` | Email |
| `EmailConfirmed` | `bool?` | Email confirmed |
| `EmailAuthFactor` | `bool?` | 2FA enabled |
| `DidDoc` | `object?` | DID document as returned by the server |
| `Active` | `bool?` | Account active |
| `Status` | `string?` | Account status |

### ISessionStore

| Method | Description |
|--------|-------------|
| `SaveAsync(session, ct?)` | Persist session |
| `LoadAsync(ct?)` | Load saved session |
| `ClearAsync(ct?)` | Clear saved session |

### IAtProtoTokenStore

Server-side OAuth token storage for multi-user scenarios. See [server.md](server.md).

| Method | Description |
|--------|-------------|
| `StoreAsync(did, data, ct?)` | Store token data for a user |
| `GetAsync(did, ct?)` | Retrieve stored token data |
| `RemoveAsync(did, ct?)` | Remove stored token data |

### IAtProtoClientFactory

Creates authenticated `AtProtoClient` instances from stored OAuth tokens. See [server.md](server.md).

| Method | Description |
|--------|-------------|
| `CreateClientForUserAsync(user, ct?)` | Create a client for the authenticated user |

---

## PdsAdminClient

Administers a PDS you operate. Authenticates with the server's admin password over HTTP
Basic (the reference PDS), or as an administrator account (Tranquil PDS). See
[managed-pds.md](managed-pds.md).

| Property | Type | Description |
|----------|------|-------------|
| `PdsUrl` | `Uri` | The PDS being administered |
| `Authentication` | `PdsAdminAuthentication` | `AdminPassword` (HTTP Basic) or `AdminAccount` (session) |
| `Admin` | `AdminClient` | Raw `com.atproto.admin.*`, admin-authenticated |
| `Server` | `ServerClient` | Raw `com.atproto.server.*`, admin-authenticated |

| Method | Description |
|--------|-------------|
| `EnsureAdminSessionAsync(ct?)` | Sign in as the administrator account, if that is how the server authenticates them; needed only before using `Admin` / `Server` directly |
| `DescribeServerAsync(ct?)` | Server DID, handle domains, invite policy |
| `CreateInviteCodeAsync(useCount?, forAccount?, ct?)` | Mint one invite code |
| `CreateInviteCodesAsync(codeCount, useCount?, forAccounts?, ct?)` | Mint several invite codes |
| `CreateAccountAsync(request, ct?)` | Create an account, minting an invite code if required |
| `GetAccountAsync(did, ct?)` | Account details |
| `DeleteAccountAsync(did, ct?)` | Permanently delete an account and its repository |
| `TakedownAccountAsync(did, reference?, ct?)` | Take an account down |
| `RestoreAccountAsync(did, ct?)` | Reverse a takedown |
| `UpdateAccountHandleAsync(did, handle, ct?)` | Change an account's handle |
| `UpdateAccountEmailAsync(account, email, ct?)` | Change an account's email |
| `UpdateAccountPasswordAsync(did, password, ct?)` | Reset an account's password |
| `CreateClient()` | An `AtProtoClient` pointed at the same PDS |

---

## Exceptions

### AtProtoHttpException

| Property | Type | Description |
|----------|------|-------------|
| `ErrorType` | `string?` | XRPC error type (e.g., "RecordNotFound") |
| `ErrorMessage` | `string?` | Human-readable error message |
| `StatusCode` | `HttpStatusCode?` | HTTP status code (shadows `HttpRequestException.StatusCode`) |
| `ResponseBody` | `string?` | Raw response body |

---

## BlueskyClients (`app.bsky.*`)

Accessed via `client.Bsky`.

| Property | Type | Description |
|----------|------|-------------|
| `Actor` | `ActorClient` | Profile and actor operations |
| `Feed` | `FeedClient` | Posts, likes, reposts, timelines |
| `Graph` | `GraphClient` | Follows, blocks, lists, starter packs |
| `Notification` | `NotificationClient` | Notifications |
| `Labeler` | `LabelerClient` | Label service declarations |
| `Video` | `VideoClient` | Video upload (`app.bsky.video.*`) |

---

## ChatClients (`chat.bsky.*`)

Accessed via `client.Chat`. See [Chat & Direct Messages](chat.md).

| Property | Type | Description |
|----------|------|-------------|
| `Convo` | `ConvoClient` | Conversations and messages |
| `Actor` | `ChatActorClient` | Chat actor preferences |

---

## OzoneClient (`tools.ozone.*`)

Accessed via `client.Ozone`. See [Ozone Moderation](ozone.md).

Type names below live under `ATProtoNet.Lexicon.Tools.Ozone.*`.

| Property | Type | Description |
|----------|------|-------------|
| `Moderation` | `ModerationClient` | Subject review, reports, actions |
| `Communication` | `CommunicationClient` | Email templates and user emails |
| `Team` | `TeamClient` | Team member management |
| `Set` | `SetClient` | Named sets of DIDs/URIs |
| `Signature` | `SignatureClient` | Signature search and correlation |
| `Server` | `OzoneServerClient` | Server config |

---

## StandardSiteClient (`site.standard.*`)

Accessed via `client.Site`. See [Standard.site](standard-site.md). A flat client over
`com.atproto.repo.*` — every method takes the repository (DID or handle) first.

| Method | Description |
|--------|-------------|
| `CreatePublicationAsync(repo, record, rkey?)` | Create a publication |
| `GetPublicationAsync(repo, rkey)` | Get a publication (typed) |
| `PutPublicationAsync(repo, rkey, record, swapRecord?)` | Create or update a publication |
| `DeletePublicationAsync(repo, rkey)` | Delete a publication |
| `ListPublicationsAsync(repo, limit?, cursor?)` | List publications (untyped values) |
| `CreateDocumentAsync` / `GetDocumentAsync` / `PutDocumentAsync` / `DeleteDocumentAsync` / `ListDocumentsAsync` | The same five operations for `site.standard.document` |
| `CreateSubscriptionAsync` / `GetSubscriptionAsync` / `DeleteSubscriptionAsync` / `ListSubscriptionsAsync` | Subscription records |

---

## RateLimitInfo

Tracked automatically on every XRPC response. Available via `client.LatestRateLimitInfo`.

| Property | Type | Description |
|----------|------|-------------|
| `Limit` | `int?` | Maximum requests per window |
| `Remaining` | `int?` | Requests remaining |
| `Reset` | `DateTimeOffset?` | When the window resets (UTC) |
| `IsExceeded` | `bool` | Whether `Remaining` has hit zero |

---

## ServiceProxy

Constants for the `atproto-proxy` header, used to route a request at a specific service. Pass one to
`client.SetProxy(...)`, optionally prefixed with the service DID:

| Constant | Value |
|----------|-------|
| `BskyAppView` | `#bsky_appview` |
| `BskyChat` | `#bsky_chat` |
| `AtProtoLabeler` | `#atproto_labeler` |
| `AtProtoPds` | `#atproto_pds` |
| `BskyAppViewDid` | `did:web:api.bsky.app` |
| `BskyChatDid` | `did:web:api.bsky.chat` |

---

## AtProtoScopes

Permission NSIDs for OAuth scope negotiation:

| Constant | Value | Description |
|----------|-------|-------------|
| `AtProto` | `atproto` | Base AT Protocol scope |
| `TransitionGeneric` | `transition:generic` | Generic transition scope |
| `TransitionChatBsky` | `transition:chat.bsky` | Chat messaging scope |
| `TransitionEmail` | `transition:email` | Access to the account's email address |
| `Default` | `atproto transition:generic` | The SDK's default scope string |

---

## Streaming

See [Firehose](firehose.md) and [Jetstream](jetstream.md).

| Type | Description |
|------|-------------|
| `FirehoseClient` | Raw WebSocket subscription to `com.atproto.sync.subscribeRepos` |
| `FirehoseConsumer` / `TypedFirehoseConsumer` | Reconnecting consumers; the typed one parses frames, filters by collection, and verifies |
| `FirehoseEventParser` | CBOR frame → `CommitEvent` / `SyncEvent` / `IdentityEvent` / `AccountEvent` |
| `FirehoseVerifier` | `VerifyCid(...)` (local) and `VerifySignatureAsync(...)` (needs DID resolution) |
| `IFirehoseCursorStore` | `GetCursorAsync` / `StoreCursorAsync`; `InMemoryFirehoseCursorStore` included |
| `JetstreamClient` / `JetstreamConsumer` | JSON streaming with server-side collection/DID/kind filtering, on either wire protocol (`JetstreamProtocol.V1` / `V2`) |
| `JetstreamEventParser`, `IJetstreamDecompressor` | Forward-tolerant parsing (`ParseFrame`); optional zstd seam |
| `JetstreamCommitEvent` / `IdentityEvent` / `AccountEvent` / `SyncEvent` | Typed events; `SyncEvent` is v2 only |
| `JetstreamEndpoints`, `JetstreamDictionaryClient` | Public instance URLs; v2 zstd dictionary fetch |
| `JetstreamConnectException` | Subscription rejected pre-upgrade (`CursorTooOld`, …); `IsRetryable` |
| `JetstreamReplayConsumer` | v2 archive backfill (`ReplayAsync`) with an inclusive, dedup'd cutover into the live tail; snapshot mode with `SnapshotOnly` |
| `JetstreamArchiveClient` | `PlanSnapshotAsync` / `ListSegmentsAsync` / `GetSegmentAsync` / `GetBlockAsync`, with bearer auth, `Range` resume, and `Retry-After`-aware 429 handling |
| `JetstreamArchiveOptions`, `IJetstreamBlockDecompressor` | Replay configuration on `JetstreamConsumerOptions.Archive`; zstd seam for `.jss` blocks |
| `JetstreamSegmentReader`, `JetstreamArchiveRow` | Streaming `.jss` decoder (`ReadRowsAsync` / `ReadEventsAsync` / `DecodeBlockFrame`) and the raw columnar row, including untouched CBOR payloads |
| `JetstreamSegmentHeader`, `JetstreamSegmentInfo`, `JetstreamSnapshotPlan` | Segment metadata for mirrors: checksums, sequence and witnessed-at bounds, plan pages |
| `JetstreamArchiveException` | Archive HTTP or decode failure; `StatusCode`, `Error`, `RetryAfter`, `IsRetryable` |

---

## Repository Primitives (`ATProtoNet.Repo`)

See [Low-Level Repo API](low-level-repo.md) and [Cryptography](crypto.md).

| Type | Description |
|------|-------------|
| `CarReader` / `CarWriter` | Parse and produce CAR v1 files |
| `MerkleSearchTree` | In-memory MST; `Serialize()`, `SerializeProof(keys)`, `Deserialize(root, blocks)` |
| `MstKeyDepth` | `ComputeDepth(key)` |
| `DagCborEncoder` / `DagCborDecoder` | Deterministic CBOR encode/decode |
| `CidComputation` | `ComputeForDagCbor`, `ComputeForRaw`, `Verify`, `DecodeCidString`, `TryDecodeCidString` |
| `RepoCommit` / `SignedRepoCommit` | Build, sign, and verify repository commit objects |
| `PlcOperationBuilder` (`ATProtoNet.Identity`) | Build, sign, and derive a DID from a `did:plc` genesis operation |
