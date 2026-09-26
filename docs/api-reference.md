# API Reference

Complete listing of the ATProto.NET public API surface.

## AtProtoClient

The main entry point: `new AtProtoClient(options?, httpClient?, sessionStore?, logger?)`, every
argument optional (`AtProtoClientOptions`: `InstanceUrl`, `UserAgent`, `RateLimit`,
`AutoRefreshSession`, `BackgroundRefresh`).

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Session` | `AtProtoSession?` | Current session: a `PasswordSession` or `OAuthSession` (null if not authenticated) |
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
| `Temp` | `TempClient` | `com.atproto.temp.*`: handle availability, signup queue, scope references |
| `Lexicon` | `LexiconClient` | `com.atproto.lexicon.*`: Lexicon resolution through the service |
| `Bsky` | `BlueskyClients` | `app.bsky.*` sub-clients |
| `Chat` | `ChatClients` | `chat.bsky.*` sub-clients |
| `Ozone` | `OzoneClient` | `tools.ozone.*` sub-clients |
| `Site` | `StandardSiteClient` | `site.standard.*` records |
| `Transport` | `IXrpcTransport` | The XRPC transport, for sub-clients of Lexicons the SDK does not ship |
| `ServiceUrl` | `Uri` | The service URL requests currently go to |

### Custom Lexicon Methods

| Method | Description |
|--------|-------------|
| `GetCollection<T>()` | Get a typed `RecordCollection<T>` for a record type that implements `IAtProtoRecord` |
| `GetCollection<T>(collection)` | The same for a collection chosen at run time (`collection` is an `Nsid`; throws if `T` declares another) |
| `QueryAsync<TOut>(nsid, parameters?, options?)` | Call a custom XRPC query (GET) |
| `QueryAsync<TOut>(nsid, object parameters, options?)` | The same with an anonymous object or dictionary for parameters (`[RequiresUnreferencedCode]`) |
| `ProcedureAsync<TIn, TOut>(nsid, input, parameters?, options?)` | Call a custom XRPC procedure (POST) and read its output |
| `ProcedureAsync<TIn>(nsid, input, parameters?, options?)` | Call a procedure with an input, ignoring any output |
| `ProcedureAsync(nsid, parameters?, options?)` | Call a procedure without input, ignoring any output |

`nsid` is an `Nsid` and `parameters` an `XrpcParams`. `options` is an `XrpcCallOptions` (`Proxy`,
`AcceptLabelers`, `Headers`, `Timeout`) that applies to that one call only. `Transport` offers the
same calls, plus `DownloadAsync` and `UploadAsync<TOut>` for binary bodies; see
[Custom XRPC](custom-xrpc.md#building-a-sub-client-for-another-lexicon).

### Authentication Methods

| Method | Description |
|--------|-------------|
| `LoginAsync(identifier, password, authFactorToken?, allowTakendown?)` | Sign in with a password; returns the `PasswordSession` |
| `CreateAccountAndLoginAsync(request)` | Create an account and install its session |
| `ApplySessionAsync(session, oauthClient?)` | Install a session you hold (an OAuth callback's, or a saved one) without a request |
| `ResumeSessionAsync(session, oauthClient?)` | Install a saved session and check it with `getSession`, refreshing if expired |
| `TryRestoreSessionAsync(did, oauthClient?)` | Install the session the session store holds for `did` |
| `RefreshSessionAsync()` | Refresh the session now (concurrent calls share one refresh) |
| `LogoutAsync()` | Sign out locally, then revoke at the service (`deleteSession` / RFC 7009) |
| `SetServiceUrl(uri)` | Point the client at a different PDS at runtime (HTTPS unless loopback) |

| Event | Description |
|-------|-------------|
| `SessionChanged` | The session was created, refreshed, expired or removed (`AtProtoSessionChangedEventArgs`) |

### Proxying & Labelers

| Method | Description |
|--------|-------------|
| `SetProxy(header)` / `ClearProxy()` | Client-wide default `atproto-proxy` header (not applied to session calls) |
| `SetLabelers(dids)` / `ClearLabelers()` | Client-wide default `atproto-accept-labelers` header (strings: a DID, optionally with `;redact`) |

Both defaults are sent with or without a session. To vary them per call on a shared client, pass
`XrpcCallOptions` instead.

Firehose and Jetstream clients are constructed on their own (`new FirehoseClient(relayUrl)`,
`new JetstreamClient(…)`), independent of a user-session client.

### Bluesky Helpers (`client.Bsky`)

Writes to the signed-in account's repository. Each returns a `RecordRef` (except the delete) and
throws `XrpcAuthenticationException` without a session.

| Method | Description |
|--------|-------------|
| `PostAsync(text, options?)` | Create a post. `text` is a `RichText`: a `string` converts, and `RichTextBuilder.Build()` makes one with facets. `PostOptions`: `Embed`, `Reply`, `Langs`, `Labels`, `Tags`, `CreatedAt` |
| `LikeAsync(subject)` | Like a record (`StrongRef`; `RecordRef.ToStrongRef()` makes one) |
| `RepostAsync(subject)` | Repost a post (`StrongRef`) |
| `FollowAsync(subject)` | Follow an account (`Did`) |
| `DeleteRecordAsync(uri)` | Delete a post, like, repost or follow by its `AtUri`, which must name a collection and a record key |
| `UpdateProfileAsync(update)` | Read-modify-write the profile record (`p => p.DisplayName = "x"`); keeps every other field and retries on a concurrent edit |

---

## RecordCollection\<T\>

Typed CRUD interface for a specific Lexicon collection. `Collection` exposes the `Nsid` it is bound
to. Record keys are `RecordKey`, CIDs `Cid`, and `repo` an `AtIdentifier` (a `Did` or `Handle`
converts implicitly).

| Method | Description |
|--------|-------------|
| `CreateAsync(record, rkey?, validate?)` | Create a new record (stamps an unset `AtProtoRecord.CreatedAt`) |
| `GetAsync(rkey, cid?)` | Get a record by key |
| `GetFromAsync(repo, rkey, cid?)` | Get a record from another user |
| `FindAsync(rkey, cid?)` | Get a record by key, or `null` on `RecordNotFound` |
| `FindFromAsync(repo, rkey, cid?)` | The same from another user |
| `PutAsync(rkey, record, validate?, swapRecord?)` | Create or update a record |
| `DeleteAsync(rkey, swapRecord?)` | Delete a record |
| `ListAsync(reverse?, limit?, cursor?)` | List one page of records |
| `ListFromAsync(repo, reverse?, limit?, cursor?)` | List records from another user |
| `EnumerateAsync(pageSize?)` | Enumerate all records (auto-pagination) |
| `EnumerateFromAsync(repo, pageSize?)` | Enumerate from another user |
| `ExistsAsync(rkey)` | Check if a record exists (`FindAsync(rkey) is not null`) |

---

## AtProtoRecord

Base class for custom record types. Implement `IAtProtoRecord` too (`static Nsid Collection`), so
`GetCollection<T>()` knows the collection.

| Property | Type | Description |
|----------|------|-------------|
| `Type` | `string` (abstract) | Lexicon NSID (`$type` field) |
| `CreatedAt` | `AtDatetime?` | Creation timestamp: `null` until set (`CreateAsync` stamps it when unset); read values keep their text, and a record read without one stays `null` |

---

## RecordRef

Reference to a created/updated record: `new RecordRef(uri, cid, commit?)`.

| Property | Type | Description |
|----------|------|-------------|
| `Uri` | `AtUri` | AT URI of the record |
| `Cid` | `Cid` | Content hash |
| `Commit` | `CommitMeta?` | The commit that wrote it (`Cid` and `Rev`) |
| `RecordKey` | `RecordKey` | Record key portion of the URI |
| `ValidationStatus` | `string?` | `valid`, or `unknown` when the service does not know the Lexicon |

`ToStrongRef()` returns the `StrongRef` to this version. Returned by every write:
`RecordCollection<T>.CreateAsync` / `PutAsync`, `RepoClient.CreateRecordAsync` / `PutRecordAsync`,
the `client.Bsky` helpers and the `StandardSiteClient` create/put methods.

---

## RecordView\<T\>

A record fetched from the repository: `new RecordView<T>(uri, cid, value)`. Returned by every
typed read: `RecordCollection<T>`, `RepoClient.GetRecordAsync<T>` (and the untyped
`GetRecordAsync`, as a `RecordView<JsonElement>`), and `StandardSiteClient`.

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

### Identity Resolution

See [Identity Resolution](did-resolution.md).

| Type | Description |
|------|-------------|
| `IDidResolver` / `DidResolver` / `CachingDidResolver` | DID → `DidDocument`; the cache adds `RefreshAsync` and `InvalidateAsync` |
| `IHandleResolver` / `HandleResolver` | Handle → DID over DNS TXT (DNS-over-HTTPS) and HTTPS well-known |
| `IIdentityResolver` / `IdentityResolver` | DID or handle → `ResolvedIdentity` with a bidirectionally verified handle |
| `PlcClient` / `DidWebResolver` | The two DID methods; `PlcClient` also reads logs and the export |
| `IdentityResolverOptions` / `DidCacheOptions` | Directory URL, DNS-over-HTTPS endpoint, timeouts, size caps, the development opt-out, cache lifetimes |
| `DidResolutionException` | Every resolution failure, with a `DidResolutionErrorKind` |
| `AddAtProtoIdentity(...)` (`ATProtoNet.Server`) | Registers the resolvers as shared singletons |

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
| `Admin.EnumerateSearchAccountsAsync(email?, pageSize?)` | `com.atproto.admin.searchAccounts` |
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
| `Bsky.Notification.EnumerateNotificationsAsync(reasons?, pageSize?)` | `app.bsky.notification.listNotifications` |
| `RecordCollection<T>.EnumerateAsync` / `EnumerateFromAsync` | `com.atproto.repo.listRecords`, deserialized |

---

## ServerClient (`com.atproto.server.*`)

| Method | Description |
|--------|-------------|
| `CreateSessionAsync(identifier, password, authFactorToken?, allowTakendown?)` | Create a session; the client's session is not changed |
| `GetSessionAsync()` | Get current session info |
| `RefreshSessionAsync(refreshJwt)` | Exchange a refresh JWT for new tokens |
| `DeleteSessionAsync(refreshJwt)` | Delete the session the refresh JWT belongs to |
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

### AtProtoSession

An immutable session, `PasswordSession` or `OAuthSession`. See [session-management.md](session-management.md).

| Property | Type | Description |
|----------|------|-------------|
| `Did` | `Did` | User's DID |
| `Handle` | `Handle` | User's handle (`handle.invalid` when it did not verify) |
| `ServiceEndpoint` | `Uri` | The PDS the session's requests go to |
| `ExpiresAt` | `DateTimeOffset?` | When the access token expires |

`PasswordSession` adds `AccessJwt`, `RefreshJwt`, `Email`, `EmailConfirmed`, `EmailAuthFactor`,
`Active` and `Status`. `OAuthSession` adds `AccessToken`, `RefreshToken`, `DPoPKey` (PKCS#8),
`Issuer`, `TokenEndpoint`, `RevocationEndpoint`, `Scope` and, for a confidential client,
`ClientKeyId`.

### IAtProtoSessionStore

Session persistence, keyed by DID. Implementations: `InMemoryAtProtoSessionStore` (core),
`FileAtProtoSessionStore` and `EfCoreAtProtoSessionStore<TContext>` (Server). See [server.md](server.md).

| Method | Description |
|--------|-------------|
| `GetAsync(did, ct?)` | Read the stored session of an account |
| `SetAsync(session, ct?)` | Store a session, replacing the account's previous one |
| `RemoveAsync(did, ct?)` | Remove the stored session of an account |

### IAtProtoClientFactory

Creates authenticated `AtProtoClient` instances from stored sessions. See [server.md](server.md).

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
| `SearchAccountsAsync(email?, limit?, cursor?, ct?)` / `EnumerateSearchAccountsAsync(email?, pageSize?, ct?)` | Search accounts by email (served by Tranquil, not by the reference PDS) |
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
| `Notification` | `NotificationClient` | Notifications, notification preferences, activity subscriptions, push |
| `Labeler` | `LabelerClient` | Label service declarations |
| `Video` | `VideoClient` | Video upload (`app.bsky.video.*`); see [Video Upload](video.md) |
| `Bookmark` | `BookmarkClient` | Private bookmarks; see [Bookmarks](getting-started.md#bookmarks) |
| `Draft` | `DraftClient` | Private post drafts |
| `Embed` | `EmbedClient` | Enhanced link cards from records |
| `AgeAssurance` | `AgeAssuranceClient` | Age assurance state and regional rules |
| `Unspecced` | `UnspeccedClient` | Endpoints the Bluesky app uses before they are specified; may change without notice |

### Post search

`Feed.SearchPostsV2Async` (`app.bsky.feed.searchPostsV2`) takes an optional query and a
`PostSearchFilters` of includes and excludes (authors, mentions, domains, URLs, embedded records,
hashtags, languages, media, replies, thread, date range, `Following`, `QueryLanguage`). A list
matches any of its entries; the filters combine. The response has `HitsTotal` and
`DetectedQueryLanguages`; `EnumerateSearchPostsV2Async` walks every page.

```csharp
var page = await client.Bsky.Feed.SearchPostsV2Async("atproto", new PostSearchFilters
{
    Authors = [Handle.Parse("alice.bsky.social")],
    Hashtags = ["dev"],
    HasMedia = true,
}, sort: PostSearchSort.Recent);
```

### Threads

`Unspecced.GetPostThreadV2Async(anchor)` returns a thread the way the Bluesky app shows it: a flat
list of `ThreadItem`s whose `Depth` places them (0 is the anchor, parents are negative). Each
`Value` is a `ThreadItemPost` (with `OpThread`, `MoreReplies`, `HiddenByThreadgate`, …) or a
placeholder: `ThreadItemBlocked`, `ThreadItemNotFound`, `ThreadItemNoUnauthenticated`. When
`HasOtherReplies` is set, `GetPostThreadOtherV2Async(anchor)` returns the rest.

### Drafts

`Draft.CreateDraftAsync(draft)` stores a `Draft` of one or more `DraftPost`s and returns its `Tid`;
`UpdateDraftAsync(id, draft)`, `DeleteDraftAsync(id)`, `GetDraftsAsync` and `EnumerateDraftsAsync`
manage them. Draft media are on-device paths (`DraftEmbedLocalRef`), so they only resolve on the
device that made the draft. `DraftErrors.DraftLimitReached` marks a full account.

### Notification preferences and activity subscriptions

- `Notification.GetPreferencesAsync()` returns the `NotificationPreferences`, one per notification
  kind. `PutPreferencesV2Async(new PutPreferencesV2Request { Like = … })` changes the ones set and
  returns all of them.
- `PutActivitySubscriptionAsync(did, post, reply)` subscribes to an account's posts and replies
  (both `false` unsubscribes); `ListActivitySubscriptionsAsync` / `EnumerateActivitySubscriptionsAsync`
  list the accounts subscribed to. Who may subscribe to an account is its
  `NotificationDeclarationRecord` (`app.bsky.notification.declaration`, key `self`).
- `UnregisterPushAsync(serviceDid, token, platform, appId)` undoes `RegisterPushAsync`.

### Graph

- `Graph.GetListsWithMembershipAsync(actor)` and `GetStarterPacksWithMembershipAsync(actor)` list the
  viewer's lists and starter packs, each with the actor's `ListItem` when the actor is on it.
- `Graph.SearchStarterPacksV2Async(query)` returns full `StarterPackView`s and `HitsTotal`.

### Link cards and feed feedback

- `Embed.GetEmbedExternalViewAsync(url, uris)` resolves the records behind a page (such as a
  `site.standard.document` and its publication) into an `ExternalView` plus the `AssociatedRefs` to
  put into the post's `ExternalInfo.AssociatedRefs`. An empty response means: render an ordinary
  link card.
- `Feed.SendInteractionsAsync(interactions, feed, feedGenerator)` tells a feed generator how the
  viewer reacted to its items (`InteractionEvent.RequestLess`, `Seen`, `Like`, …), passing back each
  item's `FeedContext` and `ReqId`. With `feedGenerator` set, the call is proxied to that service.

### Age assurance

`AgeAssurance.GetConfigAsync()` returns each region's minimum age and ordered rules
(`AgeAssuranceRule`: `DefaultAgeRule`, `DeclaredOverAgeRule`, `AssuredUnderAgeRule`,
`AccountNewerThanRule`, …). `GetStateAsync(countryCode, regionCode)` returns the account's
`AgeAssuranceState` (`Status`, `Access`) and the metadata to compute it client-side, and
`BeginAsync(email, language, countryCode)` starts the process.

### Records

Records without a client method of their own are written through `client.Repo` or
`RecordCollection<T>`:

| Model | Collection | Key |
|-------|------------|-----|
| `StatusRecord` | `app.bsky.actor.status` (e.g. `ActorStatus.Live`) | `self` |
| `ContentVisibilityDeclarationRecord` | `app.bsky.actor.contentVisibilityDeclaration` | `self` |
| `NotificationDeclarationRecord` | `app.bsky.notification.declaration` | `self` |
| `VerificationRecord` | `app.bsky.graph.verification` | TID |
| `ReferenceListOptOutRecord` | `app.bsky.graph.referencelistoptout` | TID |

---

## ChatClients (`chat.bsky.*`)

Accessed via `client.Chat`. See [Chat & Direct Messages](chat.md).

| Property | Type | Description |
|----------|------|-------------|
| `Convo` | `ConvoClient` | Conversations (direct and group), messages, reactions, the log |
| `Actor` | `ChatActorClient` | Chat status and account data |
| `Group` | `GroupClient` | Group conversations, members, join links and join requests |
| `Notification` | `ChatNotificationClient` | Chat notification preferences |
| `Moderation` | `ChatModerationClient` | Conversation lookups and chat access, for moderation services (no fixed proxy) |

---

## OzoneClient (`tools.ozone.*`)

Accessed via `client.Ozone`. See [Ozone Moderation](ozone.md).

Type names below live under `ATProtoNet.Lexicon.Tools.Ozone.*`.

| Property | Type | Description |
|----------|------|-------------|
| `Moderation` | `ModerationClient` | Events, subject review, account and record lookups, scheduled actions |
| `Report` | `ReportClient` | Individual reports: query, assign, activities, close, statistics |
| `Queue` | `QueueClient` | Moderation queues, report routing, queue assignments |
| `Communication` | `CommunicationClient` | Email templates and user emails |
| `Team` | `TeamClient` | Team member management |
| `Set` | `SetClient` | Named sets of DIDs/URIs |
| `Setting` | `SettingClient` | Instance and personal settings |
| `Safelink` | `SafelinkClient` | URL safety rules and their audit log |
| `Verification` | `VerificationClient` | Verifications the Ozone service issues |
| `Hosting` | `HostingClient` | Account history from the account's host |
| `Signature` | `SignatureClient` | Signature search and correlation |
| `Server` | `OzoneServerClient` | Server config |

---

## SpaceClient (`com.atproto.space.*`)

Accessed via `client.Space`, or via `SpaceReader.Space` when reading another member's repo with a
space credential. See [Spaces (Permissioned Data)](spaces.md). A space is a `SpaceUri`, a repo a
`Did`, and collections, record keys, revisions and CIDs are `Nsid`, `RecordKey`, `Tid` and `Cid`.

| Method | Role | Description |
|--------|------|-------------|
| `GetDelegationTokenAsync(space)` | PDS | Mint a delegation token to exchange for a credential |
| `GetSpaceCredentialAsync(space, clientAttestation?)` | Host | The raw exchange; prefer `SpaceCredentialProvider` |
| `ListSpacesAsync(type?, did?, limit?, cursor?)` / `EnumerateSpacesAsync` | PDS | Spaces the caller has **written** data to |
| `ListReposAsync(space, limit?, cursor?)` / `EnumerateReposAsync` | Host | The writer set, with each repo's `rev` and `hash` |
| `GetRecordAsync(space, repo, collection, rkey)` / `GetRecordAsync(uri)` | Repo | One record's value |
| `ListRecordsAsync(space, repo, collection?, reverse?, excludeValues?, limit?, cursor?)` / `EnumerateRecordsAsync(...)` | Repo | List records; `excludeValues` for metadata only |
| `GetLatestCommitAsync(space, repo)` | Repo | The repo's current signed commit |
| `GetRepoAsync(space, repo, excludeValues?)` | Repo | Whole repo as a two-root CAR (`XrpcStreamResponse`; dispose it) |
| `ListRepoOpsAsync(space, repo, since?, excludeValues?, limit?, cursor?)` | Repo | The oplog — the primary incremental sync mechanism |
| `GetBlobAsync(space, repo, cid)` / `ListBlobsAsync(...)` / `EnumerateBlobsAsync(...)` | Repo | Blobs referenced by permissioned records (`GetBlobAsync` returns an `XrpcStreamResponse`) |
| `CreateRecordAsync` / `PutRecordAsync` / `DeleteRecordAsync` (also by `SpaceRecordUri`) | PDS | Single-record writes (OAuth only) |
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
app access (reads only): `OpenAppAccess` *(default)*, `AllowListAppAccess`. Both unions are open: a
variant this SDK does not model reads as `UnknownSimpleSpaceUserPolicy` / `UnknownSimpleSpaceAppAccess`.

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
| `SpaceAuthority` | `#atproto_space` / `#atproto_space_host` resolution, with `#atproto` / `#atproto_pds` fallbacks when an entry is absent; a malformed one throws `FormatException` |
| `SpaceTypeDeclaration` | The `"type": "space"` Lexicon definition; `FromLexicon` (refuses a wildcard collection), `GetName(lang)` |
| `SpaceErrors` / `SimpleSpaceErrors` | The named XRPC errors these endpoints return |
| `AtProtoScopes.Space(...)` (`ATProtoNet.Auth.OAuth`) | Build `space:` scopes; `SpaceAction`, `SpaceManage` |

---

## Service auth (`ATProtoNet.Server.Authentication`)

Verifies the service auth tokens other services and apps call an XRPC service with. See
[Serving XRPC to other services](xrpc-handlers.md#serving-xrpc-to-other-services).

| Type | Description |
|------|-------------|
| `AddAtProtoServiceAuth(options => …)` | The authentication scheme: `did`, `lxm` and `aud` claims, the token bound to the endpoint's NSID |
| `AtProtoServiceAuthOptions` | `Audiences` (required), `AllowedKeyIds`, `RequireLexiconMethod`, `ClockSkew`, `MaxTokenLifetime` |
| `[RequireServiceAuth]` / `RequireServiceAuth()` | Require the scheme on a handler, or on the `MapXrpcEndpoints()` group |
| `ServiceAuthVerifier` / `ServiceAuthVerifierOptions` / `VerifiedServiceAuth` | The verification itself, usable without ASP.NET Core |
| `ServiceAuthException` / `ServiceAuthErrors` | A refusal: 401 with the reference implementation's error names |
| `IJtiReplayStore` / `InMemoryJtiReplayStore` | Spends each token's `jti` once |
| `EfCoreJtiReplayStore<T>` / `JtiReplayDbContext` / `AddAtProtoEfCoreJtiReplayStore<T>()` | The replay store over EF Core, shared across instances (`ATProtoNet.Server.EntityFrameworkCore`) |
| `XrpcMethodMetadata` (`ATProtoNet.Server.Xrpc`) | The NSID an endpoint serves, attached by `MapXrpcEndpoints()` |

## Space server (`ATProtoNet.Server.Spaces`)

The other half of the protocol: serving a space rather than reading one. See
[Serving a space](spaces.md#serving-a-space). Registered with `AddAtProtoSpaces()` plus
`AddSpaceAuthority<T>(key)`, `AddSimpleSpace<T>()`, and/or `AddSpaceRepoHost<T>()`, and mapped by
the ordinary `MapXrpcEndpoints()`.

| Type | Description |
|------|-------------|
| `SpaceServerOptions` | `ServiceDid`, `PublicBaseUrl`, `ProofLifetime`, `MaxSingleUseTokenLifetime`, `CredentialLifetime`, `CredentialKeyId`, `VerifiedCredentialCacheCapacity`, `ClientMetadataCacheLifetime`, … |
| `SpaceRequestAuthenticator` | Pulls the credentials off an `HttpContext` and verifies them |
| `DPoPProofValidator` / `DPoPProof` | RFC 9449 proof verification as proposal 0016 narrows it: `ES256` only, signature, thumbprint, `ath` (and none on the exchange), `htm`/`htu`, `iat`, replay |
| `SpaceDelegationTokenVerifier` / `VerifiedDelegationToken` | Audience pinned to `spaceHostAud(sub)`, `kid` `#atproto` only, issuer key from the DID document, single use |
| `SpaceCredentialVerifier` / `VerifiedSpaceCredential` | Signer resolved from the space URI and the `kid` (`#atproto_space` or `#atproto`, no fallback), plus the DPoP binding; verified credentials are cached |
| `SpaceClientAttestationVerifier` / `VerifiedClientAttestation` | Verified against the key the attestation's `kid` names in the client's published JWKS, cached per `client_id` |
| `ISpaceClientMetadataResolver` / `HttpSpaceClientMetadataResolver` | `client_id` → `client-metadata.json` → `jwks` / `jwks_uri` |
| `SpaceServiceAuthVerifier` | Service auth on the notification endpoints (through `ServiceAuthVerifier`); `notifyWrite` also requires `iss` to be the writer |
| `ISpaceAccountSigner` | Signs outbound notifications and managing-app checks as the account they speak for; the service key is the fallback |
| `IJtiReplayStore` / `InMemoryJtiReplayStore` (`ATProtoNet.Server.Authentication`) | Single-use enforcement, keyed on `(iss, jti, exp)`, shared with service auth |
| `IDidResolver` (keyed `SpaceServerExtensions.DidResolverKey`) | DID document resolution for every verifier, cached per `SpaceServerOptions.DidCache` (a hard 5 minutes by default) |
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

The endpoint handlers themselves are internal: the `Add…` extensions above register them, and
`MapXrpcEndpoints()` maps them.

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
| `CreateRecommendationAsync` / `GetRecommendationAsync` / `DeleteRecommendationAsync` / `ListRecommendationsAsync` / `EnumerateRecommendationsAsync` | Recommendation records (`site.standard.graph.recommend`) |

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
| `BskyFeedGenerator` | `#bsky_fg` |
| `BskyAppViewDid` | `did:web:api.bsky.app` |
| `BskyChatDid` | `did:web:api.bsky.chat` |

---

## AtProtoScopes

Permission NSIDs for OAuth scope negotiation:

| Constant | Value | Description |
|----------|-------|-------------|
| `AtProto` | `atproto` | Base AT Protocol scope |
| `TransitionGeneric` | `transition:generic` | Legacy broad scope |
| `TransitionChatBsky` | `transition:chat.bsky` | Legacy chat messaging scope |
| `TransitionEmail` | `transition:email` | Legacy access to the account's email address |
| `Default` | `atproto transition:generic` | The SDK's default scope string (legacy) |
| `BlueskyAppView` | `did:web:api.bsky.app#bsky_appview` | Audience of the `app.bsky` permission sets |
| `BlueskyChat` | `did:web:api.bsky.chat#bsky_chat` | Audience of `chat.bsky.authFullChatClient` |

`Presets` holds complete scope strings built on Bluesky's permission sets (`BlueskyApp`,
`BlueskyAppWithChat`, `BlueskyReadOnly`, `BlueskyPosting`), and `PermissionSets` the published set
NSIDs. The builders `Repo`, `Rpc`, `Blob`, `Account`, `Identity`, `Include` and `Space` compose
granular scopes; `Rpc` and `Include` require an `aud` that is a DID with a service fragment (`Rpc`
also accepts `*`). See [OAuth](oauth.md#scopes).

---

## Labels (`ATProtoNet.Labeling`, `ATProtoNet.Server.Labeling`)

See [Labeler Services](labeler.md#signing-labels).

| Type | Description |
|------|-------------|
| `LabelSigner` | Signs a labeler's labels with its `#atproto_label` key: `Sign(label)`, `Sign(subject, value, cid?, negate?, expiresAt?)`; `Labeler`, `SigningKey` |
| `LabelVerifier` | `VerifyAsync(label)` / `VerifyAllAsync(labels)` against the issuer's `#atproto_label` key, refetching the DID document once on failure |
| `LabelVerificationResult`, `LabelVerificationStatus` | `Label`, `Status` (`Valid`, `Unsigned`, `UnsupportedVersion`, `Malformed`, `NoLabelKey`, `IssuerUnresolved`, `InvalidSignature`), `IsValid`, `SigningKey`, `Error` |
| `LabelSigning` | `GetSigningBytes(label)` (the DRISL bytes a signature covers) and `Verify(label, didKey)` against a known key |
| `LabelStreamFrames` | `Encode(LabelStreamMessage)` / `EncodeError(error, message?)`: `subscribeLabels` frames for a labeler to send |
| `QueryLabelsEndpoint`, `ILabelSource`, `LabelQuery` | Serves `com.atproto.label.queryLabels` from your storage, signing the labeler's unsigned labels when a `LabelSigner` is registered |

---

## Streaming

See [Firehose](firehose.md) and [Jetstream](jetstream.md).

| Type | Description |
|------|-------------|
| `FirehoseClient` | Single-connection subscription to `com.atproto.sync.subscribeRepos` (`SubscribeAsync`) or a labeler's `com.atproto.label.subscribeLabels` (`SubscribeLabelsAsync`), yielding typed messages; `IAsyncDisposable` |
| `TypedFirehoseConsumer` | Reconnecting firehose consumer: parses each frame once, filters by collection before parsing, verifies when a `Verifier` is set, persists the cursor |
| `LabelStreamConsumer` | Reconnecting label-stream consumer: `LabelsEvent` / `LabelInfoEvent`, cursor persistence; verifies each label when a `Verifier` is set (`LabelsEvent.Verification`) |
| `ChatModerationEventConsumer` | Authenticated `chat.bsky.moderation.subscribeModEvents` stream with typed events and an unknown fallback |
| `FirehoseEventParser` | CBOR frame → `CommitEvent` / `SyncEvent` / `IdentityEvent` / `AccountEvent` / `InfoEvent`; throws `EventStreamException` for an error frame |
| `FirehoseVerifier` | `VerifyCid(...)` (local) and `VerifySignatureAsync(...)` (needs DID resolution; also checks every block's CID) |
| `IRecordEvent`, `FirehoseRecordEvent`, `GetRecordEvents()` | One record change from either stream: a `JetstreamCommitEvent`, or a firehose commit split per operation with its record decoded |
| `StreamConsumerOptions`, `StreamReconnectPolicy` | Shared consumer settings: URL, cursor store, reconnect backoff (`InitialDelay`, `MaxDelay`, `MaxAttempts`), `OnStreamError`, `OnEventDropped` |
| `IStreamCursorStore` | `GetCursorAsync` / `StoreCursorAsync` (`ValueTask`); `InMemoryStreamCursorStore` included |
| `EventStreamException`, `EventStreamError`, `EventStreamErrors` | Error frames (`FutureCursor`, `ConsumerTooSlow`), refused subscriptions and exhausted reconnects; `Error`, `StatusCode`, `IsRetryable` |
| `JetstreamClient` / `JetstreamConsumer` | JSON streaming with server-side collection/DID/kind filtering, on either wire protocol (`JetstreamProtocol.V1` / `V2`) |
| `JetstreamCursor` | Timestamp cursors (`FromTimestamp`, `IsTimestamp`): a v2 cursor of 10^15 or more seeks by time |
| `JetstreamEventParser`, `IJetstreamDecompressor` | Forward-tolerant parsing (`ParseFrame`); optional zstd seam |
| `JetstreamCommitEvent` / `IdentityEvent` / `AccountEvent` / `SyncEvent` | Typed events; `SyncEvent` is v2 only |
| `JetstreamEndpoints` | Public instance URLs |
| `JetstreamReplayConsumer` | v2 archive backfill (`ReplayAsync`) with an inclusive, dedup'd cutover into the live tail; snapshot mode with `SnapshotOnly` |
| `JetstreamArchiveClient` | `PlanSnapshotAsync` / `ListSegmentsAsync` / `EnumerateSegmentsAsync` / `GetSegmentAsync` / `GetBlockAsync`, with bearer auth, `Range` resume, and `Retry-After`-aware 429 handling; free `HEAD` probes (`ProbeSegmentAsync` / `ProbeBlockAsync`); `GetZstdDictionaryAsync` and `GetHealthAsync` |
| `JetstreamArchiveOptions`, `IJetstreamBlockDecompressor` | Replay configuration on `JetstreamConsumerOptions.Archive`; zstd seam for `.jss` blocks |
| `JetstreamSegmentReader`, `JetstreamArchiveRow` | Streaming `.jss` decoder (`ReadRowsAsync` / `ReadEventsAsync` / `DecodeBlockFrame`) and the raw columnar row, including untouched CBOR payloads |
| `JetstreamSegmentHeader`, `JetstreamSegmentInfo`, `JetstreamSnapshotPlan` | Segment metadata for mirrors: checksums, sequence and witnessed-at bounds, plan pages |
| `JetstreamException` | Any Jetstream failure: a refused subscription (`CursorTooOld`, …), an error frame, an archive HTTP or decode failure; `StatusCode`, `Error`, `RetryAfter`, `IsRetryable` |
| `JetstreamHealthCheck` (`ATProtoNet.Server`) | `IHealthCheck` over `/xrpc/_health`; register with `AddHealthChecks().AddJetstream(url)` |

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
| `RecordProof` / `VerifiedRecord` / `RepoVerificationException` | Verify a `com.atproto.sync.getRecord` proof against the signed commit (see `Sync.GetVerifiedRecordAsync`) |
| `PlcOperationBuilder` (`ATProtoNet.Identity`) | Build, sign, and derive a DID from a `did:plc` genesis operation |
