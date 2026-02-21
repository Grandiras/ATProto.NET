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
| `Server` | `ServerClient` | `com.atproto.server.*` methods |
| `Repo` | `RepoClient` | `com.atproto.repo.*` methods |
| `Identity` | `IdentityClient` | `com.atproto.identity.*` methods |
| `Sync` | `SyncClient` | `com.atproto.sync.*` methods |
| `Admin` | `AdminClient` | `com.atproto.admin.*` methods |
| `Label` | `LabelClient` | `com.atproto.label.*` methods |
| `Moderation` | `ModerationClient` | `com.atproto.moderation.*` methods |
| `Bsky` | `BlueskyClients` | `app.bsky.*` sub-clients |

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
| `ResumeSessionAsync(session)` | Resume from a saved session |
| `RefreshSessionAsync()` | Manually refresh session tokens |
| `LogoutAsync()` | Destroy the session |

### Bluesky Convenience Methods

| Method | Description |
|--------|-------------|
| `PostAsync(text, facets?, embed?, reply?, langs?, labels?)` | Create a text post |
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

Typed CRUD interface for a specific Lexicon collection.

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
| `DidDoc` | `JsonElement?` | DID document |
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

## Exceptions

### AtProtoHttpException

| Property | Type | Description |
|----------|------|-------------|
| `ErrorType` | `string?` | XRPC error type (e.g., "RecordNotFound") |
| `ErrorMessage` | `string?` | Human-readable error message |
| `StatusCode` | `HttpStatusCode` | HTTP status code |
| `ResponseBody` | `string?` | Raw response body |
