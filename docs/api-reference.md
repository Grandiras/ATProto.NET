# API Reference

Complete listing of the ATProto.NET public API surface.

## AtProtoClient

The main entry point. Created via `AtProtoClientBuilder` or direct construction.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Session` | `Session?` | Current session (null if not authenticated) |
| `IsAuthenticated` | `bool` | Whether the client has an active session |
| `Did` | `Did?` | Authenticated user's DID |
| `Handle` | `Handle?` | Authenticated user's handle |
| `LatestRepoRev` | `Tid?` | Latest repo revision from server responses |
| `LatestRateLimitInfo` | `RateLimitInfo?` | Most recent rate limit info |
| `Server` | `ServerClient` | `com.atproto.server.*` methods |
| `Repo` | `RepoClient` | `com.atproto.repo.*` methods |
| `Identity` | `IdentityClient` | `com.atproto.identity.*` methods |
| `Sync` | `SyncClient` | `com.atproto.sync.*` methods |
| `Admin` | `AdminClient` | `com.atproto.admin.*` methods |
| `Label` | `LabelClient` | `com.atproto.label.*` methods |
| `Moderation` | `ModerationClient` | `com.atproto.moderation.*` methods |
| `Space` | `SpaceClient` | `com.atproto.space.*` permissioned data |
| `SimpleSpace` | `SimpleSpaceClient` | `com.atproto.simplespace.*` space management |
| `Bsky` | `BlueskyClients` | `app.bsky.*` sub-clients |
| `Chat` | `ChatClients` | `chat.bsky.*` sub-clients |
| `Ozone` | `OzoneClient` | `tools.ozone.*` sub-clients |
| `Site` | `StandardSiteClient` | `site.standard.*` records |
| `OAuthSession` | `OAuthSessionResult?` | The applied OAuth session, if any |
| `ServiceUrl` | `Uri` | The service URL requests currently go to |

### Custom Lexicon Methods

| Method | Description |
|--------|-------------|
| `GetCollection<T>(collection)` | Get a typed `RecordCollection<T>` for CRUD (`collection` is an `Nsid`) |
| `QueryAsync<T>(nsid, parameters?, options?)` | Call a custom XRPC query (GET) |
| `ProcedureAsync<T>(nsid, body?, options?)` | Call a custom XRPC procedure (POST) with response |
| `ProcedureAsync(nsid, body?, options?)` | Call a custom XRPC procedure (POST) without response |

`nsid` is an `Nsid`. `options` is an `XrpcCallOptions` (`Proxy`, `AcceptLabelers`, `Headers`,
`Timeout`) that applies to that one call only.

### Authentication Methods

| Method | Description |
|--------|-------------|
| `LoginAsync(identifier, password, authFactorToken?)` | Authenticate and create a session |
| `ResumeSessionAsync(session)` | Resume from a saved `Session` |
| `RefreshSessionAsync()` | Manually refresh session tokens |
| `LogoutAsync()` | Destroy the session |
| `ApplyOAuthSessionAsync(oauthSession)` | Adopt an `OAuthSessionResult` (sets PDS URL, DPoP, session) |
| `SetServiceUrl(uri)` | Point the client at a different PDS at runtime (HTTPS unless loopback) |

### Streaming, Proxying & Labelers

| Method | Description |
|--------|-------------|
| `CreateFirehoseClient()` | Low-level `FirehoseClient` bound to the configured relay |
| `CreateFirehoseConsumer(...)` | Reconnecting `FirehoseConsumer` |
| `SetProxy(header)` / `ClearProxy()` | Client-wide default `atproto-proxy` header (not applied to session calls) |
| `SetLabelers(dids)` / `ClearLabelers()` | Client-wide default `atproto-accept-labelers` header (strings: a DID, optionally with `;redact`) |

Both defaults are sent with or without a session. To vary them per call on a shared client, pass
`XrpcCallOptions` instead.

### Bluesky Convenience Methods

| Method | Description |
|--------|-------------|
| `PostAsync(text, facets?, embed?, reply?, langs?, labels?)` | Create a text post (returns `CreateRecordResponse`) |
| `LikeAsync(uri, cid)` | Like a post (`AtUri`, `Cid`) |
| `UnlikeAsync(likeUri)` | Unlike a post (`AtUri`) |
| `RepostAsync(uri, cid)` | Repost a post (`AtUri`, `Cid`) |
| `UndoRepostAsync(repostUri)` | Undo a repost (`AtUri`) |
| `FollowAsync(did)` | Follow an actor (`Did`) |
| `UnfollowAsync(followUri)` | Unfollow an actor (`AtUri`) |
| `DeletePostAsync(postUri)` | Delete a post (`AtUri`) |
| `UpdateProfileAsync(update)` | Read-modify-write the profile record (`p => p.DisplayName = "x"`); keeps every other field and retries on a concurrent edit |

---

## RecordCollection\<T\>

Typed CRUD interface for a specific Lexicon collection. `Collection` exposes the `Nsid` it is bound
to. Record keys are `RecordKey`, CIDs `Cid`, and `repo` an `AtIdentifier` (a `Did` or `Handle`
converts implicitly).

| Method | Description |
|--------|-------------|
| `CreateAsync(record, rkey?, validate?)` | Create a new record |
| `GetAsync(rkey, cid?)` | Get a record by key |
| `GetFromAsync(repo, rkey, cid?)` | Get a record from another user |
| `PutAsync(rkey, record, validate?, swapRecord?)` | Create or update a record |
| `DeleteAsync(rkey, swapRecord?)` | Delete a record |
| `ListAsync(reverse?, limit?, cursor?)` | List one page of records |
| `ListFromAsync(repo, reverse?, limit?, cursor?)` | List records from another user |
| `EnumerateAsync(pageSize?)` | Enumerate all records (auto-pagination) |
| `EnumerateFromAsync(repo, pageSize?)` | Enumerate from another user |
| `ExistsAsync(rkey)` | Check if a record exists |

---

## AtProtoRecord

Base class for custom record types.

| Property | Type | Description |
|----------|------|-------------|
| `Type` | `string` (abstract) | Lexicon NSID (`$type` field) |
| `CreatedAt` | `AtDatetime?` | Creation timestamp (set to now on construction; read values keep their text) |

---

## RecordRef

Reference to a created/updated record.

| Property | Type | Description |
|----------|------|-------------|
| `Uri` | `AtUri` | AT URI of the record |
| `Cid` | `Cid` | Content hash |
| `RecordKey` | `RecordKey` | Record key portion of the URI |

Returned by `RecordCollection<T>.CreateAsync` / `PutAsync`. The `RepoClient` methods return the raw
`CreateRecordResponse` / `PutRecordResponse` instead, which also carry `Commit` (`CommitMeta` with
`Cid` and `Rev`).

---

## RecordView\<T\>

A record fetched from the repository.

| Property | Type | Description |
|----------|------|-------------|
| `Uri` | `AtUri` | AT URI |
| `Cid` | `Cid?` | Content hash |
| `Value` | `T` | Deserialized record value |
| `RecordKey` | `RecordKey` | Record key |

---

## RecordPage\<T\>

Paginated list of records; an `ICursorPage<RecordView<T>>`.

| Property | Type | Description |
|----------|------|-------------|
| `Records` | `IReadOnlyList<RecordView<T>>` | Records in this page |
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
| `AtUri` | AT Protocol URI | `at://did:plc:abc/app.bsky.feed.post/3k2la` |
| `Tid` | Timestamp Identifier | `3k2la7rxjgs2t` |
| `RecordKey` | Record key | `self`, `3k2la7rxjgs2t` |
| `Cid` | Content Identifier | `bafyrei...` |

All are `sealed record`s supporting `Parse()`, `TryParse()`, `IParsable<T>`/`ISpanParsable<T>`, `IComparable<T>`, ordinal equality, implicit conversion to `string` and explicit conversion from it. `TidGenerator` mints strictly increasing TIDs; see [Identity Types](identity-types.md).

`AtDatetime` (a `readonly record struct`) is the Lexicon `datetime`: it keeps the exact text it was
read with, reads leniently (`IsValid`, `TryGetValue`, `Value`), and creates the canonical
`yyyy-MM-ddTHH:mm:ss.fffZ` form from `Now()`, `FromDateTimeOffset` and `FromDateTime`.

Every model and client in `ATProtoNet.Lexicon.Com.AtProto.*` and `ATProtoNet.Lexicon.App.Bsky.*` uses
these types for identifier and `datetime` fields.

## Pagination

Every cursored response implements `ICursorPage<T>` (`IReadOnlyList<T> Items`, `string? Cursor`).
`List*` / `Get*` methods return one page and take `limit` then `cursor`; `Enumerate*` methods return
an `IAsyncEnumerable<T>` over every page, take `int? pageSize = null`, and stop when the server
returns no cursor, an empty one, or one it already returned.

| Enumerator | Pages of |
|------------|----------|
| `Repo.EnumerateRecordsAsync(repo, collection, reverse?, pageSize?)` | `com.atproto.repo.listRecords` |
| `Repo.EnumerateMissingBlobsAsync(pageSize?)` | `com.atproto.repo.listMissingBlobs` |
| `Sync.EnumerateBlobsAsync(did, since?, pageSize?)` | `com.atproto.sync.listBlobs` |
| `Sync.EnumerateReposAsync(pageSize?)` | `com.atproto.sync.listRepos` |
| `Sync.EnumerateReposByCollectionAsync(collection, pageSize?)` | `com.atproto.sync.listReposByCollection` |
| `Sync.EnumerateHostsAsync(pageSize?)` | `com.atproto.sync.listHosts` |
| `Label.EnumerateLabelsAsync(uriPatterns, sources?, pageSize?)` | `com.atproto.label.queryLabels` |
| `Admin.EnumerateInviteCodesAsync(sort?, pageSize?)` | `com.atproto.admin.getInviteCodes` |
| `Bsky.Actor.EnumerateSuggestionsAsync(pageSize?)` | `app.bsky.actor.getSuggestions` |
| `Bsky.Actor.EnumerateSearchActorsAsync(q, pageSize?)` | `app.bsky.actor.searchActors` |
| `Bsky.Feed.EnumerateTimelineAsync(algorithm?, pageSize?)` | `app.bsky.feed.getTimeline` |
| `Bsky.Feed.EnumerateAuthorFeedAsync(actor, filter?, includePins?, pageSize?)` | `app.bsky.feed.getAuthorFeed` |
| `Bsky.Feed.EnumerateFeedAsync(feed, pageSize?)` | `app.bsky.feed.getFeed` |
| `Bsky.Feed.EnumerateListFeedAsync(list, pageSize?)` | `app.bsky.feed.getListFeed` |
| `Bsky.Feed.EnumerateActorLikesAsync(actor, pageSize?)` | `app.bsky.feed.getActorLikes` |
| `Bsky.Feed.EnumerateLikesAsync(uri, cid?, pageSize?)` | `app.bsky.feed.getLikes` |
| `Bsky.Feed.EnumerateRepostedByAsync(uri, cid?, pageSize?)` | `app.bsky.feed.getRepostedBy` |
| `Bsky.Feed.EnumerateQuotesAsync(uri, cid?, pageSize?)` | `app.bsky.feed.getQuotes` |
| `Bsky.Feed.EnumerateActorFeedsAsync(actor, pageSize?)` | `app.bsky.feed.getActorFeeds` |
| `Bsky.Feed.EnumerateSuggestedFeedsAsync(pageSize?)` | `app.bsky.feed.getSuggestedFeeds` |
| `Bsky.Feed.EnumerateSearchPostsAsync(q, sort?, since?, until?, …, pageSize?)` | `app.bsky.feed.searchPosts` |
| `Bsky.Graph.EnumerateFollowersAsync(actor, pageSize?)` | `app.bsky.graph.getFollowers` |
| `Bsky.Graph.EnumerateFollowsAsync(actor, pageSize?)` | `app.bsky.graph.getFollows` |
| `Bsky.Graph.EnumerateKnownFollowersAsync(actor, pageSize?)` | `app.bsky.graph.getKnownFollowers` |
| `Bsky.Graph.EnumerateBlocksAsync(pageSize?)` / `EnumerateMutesAsync(pageSize?)` | `app.bsky.graph.getBlocks` / `getMutes` |
| `Bsky.Graph.EnumerateListsAsync(actor, pageSize?)` | `app.bsky.graph.getLists` |
| `Bsky.Graph.EnumerateListMembersAsync(list, pageSize?)` | `app.bsky.graph.getList` (its `items`) |
| `Bsky.Graph.EnumerateListBlocksAsync(pageSize?)` / `EnumerateListMutesAsync(pageSize?)` | `app.bsky.graph.getListBlocks` / `getListMutes` |
| `Bsky.Graph.EnumerateActorStarterPacksAsync(actor, pageSize?)` | `app.bsky.graph.getActorStarterPacks` |
| `Bsky.Graph.EnumerateSearchStarterPacksAsync(query, pageSize?)` | `app.bsky.graph.searchStarterPacks` |
| `Bsky.Notification.EnumerateNotificationsAsync(priority?, seenAt?, pageSize?)` | `app.bsky.notification.listNotifications` |
| `RecordCollection<T>.EnumerateAsync` / `EnumerateFromAsync` | `com.atproto.repo.listRecords`, deserialized |

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
| `CreateInviteCodeAsync(useCount, forAccount?)` | Generate invite code |
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
| `GetServiceAuthAsync(aud, lxm?, exp?)` | Get service auth token (`lxm` is an `Nsid`) |
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
| `GetRecordAsync(uri, cid?)` / `GetRecordAsync<T>(uri, cid?)` | Get the record an `AtUri` names |
| `DeleteRecordAsync(uri, swapRecord?, swapCommit?)` | Delete the record an `AtUri` names |
| `ListRecordsAsync(repo, collection, reverse?, limit?, cursor?)` | List one page of records |
| `EnumerateRecordsAsync(repo, collection, reverse?, pageSize?)` | Enumerate all records |
| `DescribeRepoAsync(repo)` | Get repo info |
| `UploadBlobAsync(stream, mimeType)` | Upload blob from stream |
| `UploadBlobAsync(filePath, mimeType)` | Upload blob from file |
| `UploadBlobAsync(data, mimeType)` | Upload blob from bytes |
| `ApplyWritesAsync(repo, writes, validate?, swapCommit?)` | Batch write operations |
| `ListMissingBlobsAsync(limit?, cursor?)` | List one page of missing blobs |
| `EnumerateMissingBlobsAsync(pageSize?)` | Enumerate all missing blobs |

`repo` is an `AtIdentifier`, `collection` an `Nsid`, `rkey` a `RecordKey`, and `cid` / `swapRecord` /
`swapCommit` a `Cid`.

---

## Session & Auth

### Session

| Property | Type | Description |
|----------|------|-------------|
| `Did` | `Did` | User's DID |
| `Handle` | `Handle` | User's handle (`handle.invalid` when it did not verify) |
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
| `CreateInviteCodeAsync(useCount?, forAccount?, ct?)` | Mint one invite code (`forAccount` is a `Did`) |
| `CreateInviteCodesAsync(codeCount, useCount?, forAccounts?, ct?)` | Mint several invite codes |
| `CreateAccountAsync(request, ct?)` | Create an account, minting an invite code if required |
| `GetAccountAsync(did, ct?)` | Account details |
| `DeleteAccountAsync(did, ct?)` | Permanently delete an account and its repository |
| `TakedownAccountAsync(did, reference?, ct?)` | Take an account down |
| `RestoreAccountAsync(did, ct?)` | Reverse a takedown |
| `UpdateAccountHandleAsync(did, handle, ct?)` | Change an account's handle |
| `UpdateAccountEmailAsync(account, email, ct?)` | Change an account's email (`account` is an `AtIdentifier`) |
| `UpdateAccountPasswordAsync(did, password, ct?)` | Reset an account's password |
| `CreateClient()` | An `AtProtoClient` pointed at the same PDS |

---

## Exceptions

Every SDK exception derives from `AtProtoException`. See [Error Handling](error-handling.md) for
the full hierarchy.

### XrpcException

Thrown for every non-success XRPC response, and thrown by hosted XRPC endpoints to answer with a
named error.

| Property | Type | Description |
|----------|------|-------------|
| `Nsid` | `string?` | The method that failed (client side) |
| `Error` | `string` | XRPC error name (e.g., `RecordNotFound`); the status's generic name when the body had none |
| `ErrorMessage` | `string?` | Human-readable error message |
| `StatusCode` | `HttpStatusCode` | HTTP status code |
| `ResponseBody` | `string?` | Raw response body |
| `Headers` | `IDictionary<string, string>` | Response headers (client); headers to write (server) |
| `Is(error)` | `bool` | Whether `Error` equals the given name; use with `XrpcErrors` constants |

| Subtype | When |
|---------|------|
| `XrpcRateLimitException` | A 429 whose wait exceeds `XrpcRateLimitOptions.MaxDelay`, or retries ran out. Adds `RetryAfter` and `RateLimit` |
| `XrpcAuthenticationException` | A 401, or `ExpiredToken` / `InvalidToken` |

### XrpcResponseFormatException

A success response whose body does not deserialize into the expected type. `Nsid` names the
method; `InnerException` is the `JsonException`.

---

## BlueskyClients (`app.bsky.*`)

Accessed via `client.Bsky`. Actor parameters take an `AtIdentifier` (a `Did` or `Handle` converts
implicitly), post, feed and list parameters an `AtUri`, and `seenAt` an `AtDatetime`; the view models
carry the same types (`PostView.Uri` is an `AtUri`, `ProfileView.Did` a `Did`, `IndexedAt` an
`AtDatetime`).

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

## SpaceClient (`com.atproto.space.*`)

Accessed via `client.Space`, or via `SpaceReader.Space` when reading another member's repo with a
space credential. See [Spaces (Permissioned Data)](spaces.md).

| Method | Role | Description |
|--------|------|-------------|
| `GetDelegationTokenAsync(space)` | PDS | Mint a delegation token to exchange for a credential |
| `GetSpaceCredentialAsync(space, clientAttestation?)` | Host | The raw exchange; prefer `SpaceCredentialProvider` |
| `ListSpacesAsync(type?, did?, limit?, cursor?)` | PDS | Spaces the caller has **written** data to |
| `ListReposAsync(space, limit?, cursor?)` / `EnumerateReposAsync` | Host | The writer set, with each repo's `rev` and `hash` |
| `GetRecordAsync(space, repo, collection, rkey)` | Repo | One record's value |
| `ListRecordsAsync(...)` / `EnumerateRecordsAsync(...)` | Repo | List records; `excludeValues` for metadata only |
| `GetLatestCommitAsync(space, repo)` | Repo | The repo's current signed commit |
| `GetRepoAsync(space, repo, excludeValues?)` | Repo | Whole repo as a two-root CAR (`XrpcStreamResponse`; dispose it) |
| `ListRepoOpsAsync(space, repo, since?, …)` | Repo | The oplog — the primary incremental sync mechanism |
| `GetBlobAsync(space, repo, cid)` / `ListBlobsAsync(...)` | Repo | Blobs referenced by permissioned records (`GetBlobAsync` returns an `XrpcStreamResponse`) |
| `CreateRecordAsync` / `PutRecordAsync` / `DeleteRecordAsync` | PDS | Single-record writes (OAuth only) |
| `ApplyWritesAsync(space, repo, writes, validate?)` | PDS | Atomic batch (`SpaceCreateOp` / `SpaceUpdateOp` / `SpaceDeleteOp`) |
| `RegisterNotifyAsync` / `UnregisterNotifyAsync` | Host | Subscribe a service to write notifications |
| `NotifyWriteAsync` / `NotifySpaceDeletedAsync` | Syncer | Deliver a notification (service auth) |

---

## SimpleSpaceClient (`com.atproto.simplespace.*`)

Accessed via `client.SimpleSpace`. The space-management implementation every PDS must support.

| Method | Description |
|--------|-------------|
| `CreateSpaceAsync(type, skey?, readPolicy?, writePolicy?, appAccess?)` | Create a space owned by the authenticated user |
| `UpdateSpaceAsync(space, readPolicy?, writePolicy?, appAccess?)` | Replace any of the three policies; omitted ones are unchanged |
| `DeleteSpaceAsync(space)` | Delete a space (idempotent) |
| `GetSpaceAsync(space)` | Describe a space and its configuration |
| `PutMemberAsync(space, did, read, write)` / `RemoveMemberAsync` | Maintain the host-internal member list; `PutMemberAsync` is an upsert of both access flags |
| `ListMembersAsync(...)` / `EnumerateMembersAsync(...)` | List members and their `Read` / `Write` access (OAuth only, on the authority's PDS) |
| `CheckUserAccessAsync(space, user, access, clientId?)` | Served by a `ManagingAppPolicy` space's managing app; `access` is `SimpleSpaceAccess.Read` or `.Write` |

Read and write policies: `PublicPolicy`, `MemberListPolicy` *(default)*, `ManagingAppPolicy`;
app access (reads only): `OpenAppAccess` *(default)*, `AllowListAppAccess`.

---

## Spaces (`ATProtoNet.Spaces`)

| Type | Description |
|------|-------------|
| `SpaceUri` / `SpaceRecordUri` | `at://{authority}/space/{type}/{skey}[/{author}/{collection}/{rkey}]`; `IsSpaceUri` |
| `SpaceCredentialProvider` | Runs the delegation → credential exchange, caches credentials, hands out readers |
| `SpaceCredentialOptions` | `ClientAttestationFactory`, `RenewalWindow`, `HostResolver` |
| `SpaceCredential` / `SpaceReader` | A DPoP-bound credential, and a client bound to one repo host |
| `SpaceCredentialException` | Refusal or mismatch; `Error` carries the XRPC name (e.g. `SpaceDeleted`) |
| `SpaceTokens` / `SpaceToken` / `SpaceTokenType` | Create, parse, and verify delegation tokens, credentials, and client attestations |
| `SpaceSyncer` / `SpaceSyncResult` / `SpaceSyncOutcome` | Incremental sync with automatic full-state recovery |
| `SpaceRepoCursor` / `ISpaceRepoStore` | Persisted sync position + running set hash; the caller's copy |
| `LtHash` | The homomorphic set hash: `Add`, `Remove`, `GetState`, `Digest` |
| `SpaceRepoCommit` / `SignedSpaceCommit` / `SpaceCommitContext` | Build, sign, and compare a repo's commit |
| `SpaceCommitVerifier` | `Verify(commit, context, didKey)`, `ComputeMac(...)` |
| `SpaceRepoCar` / `VerifiedSpaceRepo` / `SpaceRepoRecord` | Serialize and verify the two-root repo CAR |
| `SpaceAuthority` | `#atproto_space` / `#atproto_space_host` resolution, with `#atproto` / `#atproto_pds` fallbacks |
| `SpaceTypeDeclaration` | The `"type": "space"` Lexicon definition; `FromLexicon`, `GetName(lang)` |
| `SpaceErrors` / `SimpleSpaceErrors` | The named XRPC errors these endpoints return |
| `AtProtoScopes.Space(...)` (`ATProtoNet.Auth.OAuth`) | Build `space:` scopes; `SpaceAction`, `SpaceManage` |

---

## Space server (`ATProtoNet.Server.Spaces`)

The other half of the protocol: serving a space rather than reading one. See
[Serving a space](spaces.md#serving-a-space). Registered with `AddAtProtoSpaces()` plus
`AddSpaceAuthority<T>(key)`, `AddSimpleSpace<T>()`, and/or `AddSpaceRepoHost<T>()`, and mapped by
the ordinary `MapXrpcEndpoints()`.

| Type | Description |
|------|-------------|
| `SpaceServerOptions` | `ServiceDid`, `PublicBaseUrl`, `ProofLifetime`, `MaxSingleUseTokenLifetime`, `CredentialLifetime`, `CredentialKeyId`, … |
| `SpaceRequestAuthenticator` | Pulls the credentials off an `HttpContext` and verifies them |
| `DPoPProofValidator` / `DPoPProof` | RFC 9449 proof verification: signature, thumbprint, `ath`, `htm`/`htu`, `iat`, replay |
| `SpaceDelegationTokenVerifier` / `VerifiedDelegationToken` | Audience pinned to `spaceHostAud(sub)`, issuer key from the DID document, single use |
| `SpaceCredentialVerifier` / `VerifiedSpaceCredential` | Signer resolved from the space URI, plus the DPoP binding |
| `SpaceClientAttestationVerifier` / `VerifiedClientAttestation` | Verified against the key the attestation's `kid` names in the client's published JWKS |
| `ISpaceClientMetadataResolver` / `HttpSpaceClientMetadataResolver` | `client_id` → `client-metadata.json` → `jwks` / `jwks_uri` |
| `ISpaceServiceAuthVerifier` / `SpaceServiceAuthVerifier` | Service auth on the notification endpoints, and the "does this service host that repo" check |
| `ISpaceReplayStore` / `InMemorySpaceReplayStore` | Single-use enforcement, keyed on `(iss, jti, exp)` |
| `ISpaceDidDocumentResolver` / `CachingSpaceDidDocumentResolver` | DID document resolution, with `#atproto` / `#atproto_space` key selection |
| `SpaceVerificationException` | An `XrpcException` carrying `InvalidDelegationToken`, `InvalidClientAttestation`, or `NotAuthorized` |
| `ISpaceAccessPolicy` / `SpaceAccessRequest` / `SpaceAccessKind` / `SpaceAccessDecision` | The authority's decisions: who gets a credential (`Read`), and whose write notifications it tracks and forwards (`Write`) |
| `ISpaceCredentialIssuer` / `SpaceCredentialIssuer` | Mints credentials bound to the requester's key |
| `ISpaceAuthorityStore` / `InMemorySpaceAuthorityStore` | Writer set and notification registrations |
| `ISpaceRepoHost` / `SpaceBlobContent` | The reads a repo host serves |
| `ISimpleSpaceStore` / `InMemorySimpleSpaceStore` / `SimpleSpaceRecord` | `simplespace` spaces and member lists |
| `SimpleSpaceAccessPolicy` | The baseline policy: read and write policies (member list / public / managing app), and open / allow-list app access for reads |
| `ISimpleSpaceManagingAppClient` / `SimpleSpaceManagingAppClient` | The `checkUserAccess` call out to a managing app |
| `SpaceWriteNotifier` | Best-effort `notifyWrite` / `notifySpaceDeleted` fan-out, the authority's forwarding of accepted writes, and first-write auto-registration |
| `ISpaceCallerResolver` / `ClaimsSpaceCallerResolver` | The DID behind a `simplespace` administration request |
| `SpaceNsids` | The NSID constants the endpoints are registered under |

Endpoint handlers (all `IXrpcEndpoint`, registered by the `Add…` extensions above):
`GetSpaceCredentialEndpoint`, `ListSpaceReposEndpoint`, `RegisterNotifyEndpoint`,
`UnregisterNotifyEndpoint`, `NotifyWriteEndpoint`; `GetSpaceRecordEndpoint`,
`ListSpaceRecordsEndpoint`, `GetSpaceLatestCommitEndpoint`, `GetSpaceRepoEndpoint`,
`ListSpaceRepoOpsEndpoint`, `GetSpaceBlobEndpoint`, `ListSpaceBlobsEndpoint`; and the seven
`com.atproto.simplespace` handlers.

---

## StandardSiteClient (`site.standard.*`)

Accessed via `client.Site`. See [Standard.site](standard-site.md). A flat client over
`com.atproto.repo.*` — every method takes the repository (`AtIdentifier`: DID or handle) first.

| Method | Description |
|--------|-------------|
| `CreatePublicationAsync(repo, record, rkey?)` | Create a publication |
| `GetPublicationAsync(repo, rkey)` / `GetPublicationAsync(uri)` | Get a publication (typed), by repo and record key or by AT URI |
| `PutPublicationAsync(repo, rkey, record, swapRecord?)` | Create or update a publication |
| `DeletePublicationAsync(repo, rkey)` | Delete a publication |
| `ListPublicationsAsync(repo, limit?, cursor?)` | One `RecordPage<PublicationRecord>` |
| `EnumeratePublicationsAsync(repo, pageSize?)` | Every publication, as `RecordView<PublicationRecord>` |
| `CreateDocumentAsync` / `GetDocumentAsync` / `PutDocumentAsync` / `DeleteDocumentAsync` / `ListDocumentsAsync` / `EnumerateDocumentsAsync` | The same operations for `site.standard.document` |
| `CreateSubscriptionAsync` / `GetSubscriptionAsync` / `DeleteSubscriptionAsync` / `ListSubscriptionsAsync` / `EnumerateSubscriptionsAsync` | Subscription records |

---

## RateLimitInfo

Tracked automatically on every XRPC response. Available via `client.LatestRateLimitInfo`, and on
`XrpcRateLimitException.RateLimit`. How 429s are retried is set with `AtProtoClientOptions.RateLimit`
(`MaxRetries`, default 3; `MaxDelay`, default 30 s).

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
| `DagCborEncoder` / `DagCborDecoder` | Deterministic CBOR encode/decode |
| `CidComputation` | `ComputeForDagCbor`, `ComputeForRaw`, `Verify`, `DecodeCidString`, `TryDecodeCidString` |
| `RepoCommit` / `SignedRepoCommit` | Build, sign, and verify repository commit objects |
| `PlcOperationBuilder` (`ATProtoNet.Identity`) | Build, sign, and derive a DID from a `did:plc` genesis operation |
