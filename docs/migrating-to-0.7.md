# Migrating to 0.7

0.7 is a large, deliberately breaking release. Identifiers are typed everywhere, there is one session
model and one persistence interface, one exception family, open unions that keep data the SDK does
not model, one set of streaming options, a hardened OAuth client whose hosted login moved into
`ATProtoNet.Server`, service auth in place of the old bearer handler, and a builder for dependency
injection. This page walks through every breaking change, grouped by what it touches. The
[changelog](../CHANGELOG.md) lists the same changes release note by release note, alongside the
additions and fixes.

Most of the work is mechanical: the compiler finds nearly all of it. The exceptions — changes that
compile but behave differently — are called out, and collected under
[Behaviour changes](#behaviour-changes) at the end.

## Upgrade checklist

1. **Package references.** Add `ATProtoNet.Server.EntityFrameworkCore` where you use an EF Core
   store. `ATProtoNet.Server` no longer brings EF Core, `StackExchange.Redis` or
   `Microsoft.Extensions.Http.Resilience`, and the core package no longer brings
   `Microsoft.Extensions.Http` or `Microsoft.Extensions.Options`. See
   [Packages and dependencies](#packages-and-dependencies).
2. **Dependency injection.** Rewrite `AddAtProtoServer` / `AddAtProtoScoped` /
   `AddAtProtoAuthentication` / `AddAtProto<TStore>` as `AddAtProto()` plus `With…` calls, and choose
   a session store: the client factory's default is now in memory. See
   [Dependency injection](#dependency-injection).
3. **Identifiers.** Parse string literals into `Did`, `Handle`, `AtIdentifier`, `AtUri`, `Nsid`,
   `Cid`, `RecordKey`, `Tid` and `AtDatetime` where they enter your code; values the API returns pass
   straight through. See [Typed identifiers](#typed-identifiers).
4. **Sessions.** `Session`, `OAuthSessionResult` and `AtProtoTokenData` become `AtProtoSession`
   (`PasswordSession`, `OAuthSession`); `ISessionStore` and `IAtProtoTokenStore` become
   `IAtProtoSessionStore`. See [Sessions](#sessions).
5. **Exceptions.** Catch `XrpcException` (and `XrpcAuthenticationException`,
   `XrpcRateLimitException`, `XrpcResponseFormatException`) where you caught `AtProtoHttpException`
   or `HttpRequestException`; every SDK exception derives from `AtProtoException`. See
   [Errors](#errors).
6. **OAuth hosting.** The cookie login is in `ATProtoNet.Server` (`ATProtoNet.Server.Authentication`),
   registered with `AddAtProto().WithOAuth()`. The bearer-JWT handler is gone: other services call
   you with service auth. See [The hosted OAuth login](#the-hosted-oauth-login) and
   [Service auth](#service-auth).
7. **Unions.** Add an `Unknown…` (or `_`) arm to every `switch` over a Lexicon union; the compiler
   does not flag a missing one. See [Unions and unknown fields](#unions-and-unknown-fields).
8. **Database migrations.** Only two schemas changed: the `simplespace` tables (split policies,
   per-member flags) and the replay table (`AtProtoSpaceReplay` → `AtProtoJtiReplay`). The session
   table is unchanged. See [Spaces](#spaces) and [Service auth](#service-auth).

Code in the "before" samples below is 0.6; everything else compiles against 0.7.

## Packages and dependencies

- **The core package depends on less.** `ATProtoNet` no longer references
  `Microsoft.Extensions.Http` or `Microsoft.Extensions.Options`; its dependencies are
  `Microsoft.Extensions.Caching.Abstractions`, `Microsoft.Extensions.Logging.Abstractions` and
  `System.Formats.Cbor`. ASP.NET Core applications are unaffected (the shared framework has both). An
  application outside ASP.NET Core that used `IHttpClientFactory`, `IOptions<T>` or `LoggerFactory`
  only through the SDK references `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options` or
  `Microsoft.Extensions.Logging` itself.
- **The EF Core stores are their own package again.** `EfCoreAtProtoSessionStore<T>` and
  `AtProtoTokenDbContext`, `EfCoreJtiReplayStore<T>` and `JtiReplayDbContext`, the space stores and
  `SpaceDbContext`, and `EfCoreRepoSyncStateStore<T>` and `RepoSyncStateDbContext` are in
  `ATProtoNet.Server.EntityFrameworkCore`, with their `Configure…Model` helpers and `Add…`
  methods. The namespace (`ATProtoNet.Server.EntityFrameworkCore`), the types and the tables are
  unchanged:

  ```bash
  dotnet add package ATProtoNet.Server.EntityFrameworkCore
  ```

- **`ATProtoNet.Server` depends on nothing beyond the ASP.NET Core shared framework.**
  `RedisSpaceReplayStore` and `AddAtProtoRedisSpaceReplayStore()` are removed with the
  `StackExchange.Redis` dependency: [the whole store](spaces.md#a-replay-store-on-redis) is about
  fifteen lines to copy into your application, registered with
  `services.Replace(ServiceDescriptor.Singleton<IJtiReplayStore, RedisJtiReplayStore>())`.
  `Microsoft.Extensions.Http.Resilience` is removed with the standard resilience handler the Aspire
  client added (see [Aspire](#aspire)).
- **The OAuth cookie login moved from `ATProtoNet.Blazor` to `ATProtoNet.Server`**, so MVC, Razor
  Pages and minimal-API applications no longer need the Blazor package (see
  [The hosted OAuth login](#the-hosted-oauth-login)). `ATProtoNet.Blazor` still brings
  `ATProtoNet.Server` along, and no longer declares browser support.
- **`samples/BlazorOAuthSample` is folded into `samples/ServerIntegrationSample`.**

## Dependency injection

`AddAtProto()` returns an `IAtProtoBuilder`, and the rest of the registration hangs off it (see
[ASP.NET Core: The AtProto Builder](aspnet-core.md#the-atproto-builder)). `ServiceCollectionExtensions`
is renamed `AtProtoServiceCollectionExtensions`.

| 0.6 | 0.7 |
|-----|-----|
| `services.AddAtProtoServer()` | `services.AddAtProto().WithClientFactory().WithFileSessionStore()` |
| `services.AddAtProtoServer("/var/tokens")` | `services.AddAtProto().WithClientFactory().WithFileSessionStore(o => o.Directory = "/var/tokens")` |
| `services.AddAtProtoServer<MyStore>()` | `services.AddAtProto().WithClientFactory().WithSessionStore<MyStore>()` |
| `services.AddAtProtoEfCoreTokenStore<TContext>()` | `services.AddAtProto().WithClientFactory().WithEfCoreSessionStore<TContext>()` (package `ATProtoNet.Server.EntityFrameworkCore`) |
| `services.AddAtProtoAuthentication(o => …)` (Blazor) | `services.AddAtProto().WithOAuth(o => …)` |
| `services.AddAtProtoScoped(o => …)` | `services.AddAtProto(o => …).WithLifetime(ServiceLifetime.Scoped)` |
| `services.AddAtProto<MyStore>(o => …)` | `services.AddAtProto(o => …).WithSessionStore<MyStore>()` |
| `builder.AddAtProtoClient(configureSettings: s => s.InstanceUrl = …)` | `builder.AddAtProtoClient(configure: o => o.InstanceUrl = …)` |
| `services.AddHttpClient("AtProtoClient")` / `("ATProtoNet")` customizations | `services.AddAtProto().HttpClient…` |
| `authentication.AddAtProto(…)` (the bearer handler) | `authentication.AddAtProtoServiceAuth(…)`; see [Service auth](#service-auth) |

```csharp before
builder.Services.AddAtProtoAuthentication(options => options.ClientName = "My App");
builder.Services.AddAtProtoServer("/var/tokens");
```

```csharp
using ATProtoNet.Server;
using ATProtoNet.Server.Authentication;

builder.Services.AddAtProto()
    .WithOAuth(options => options.ClientName = "My App")
    .WithClientFactory()
    .WithFileSessionStore(options => options.Directory = "/var/tokens");
```

- **The store is chosen on the builder**, whichever order the calls run in:
  `WithSessionStore<T>()`, `WithSessionStore(factory)`, `WithInMemorySessionStore()`,
  `WithFileSessionStore()` or `WithEfCoreSessionStore<TContext>()`. `WithHealthCheck()` adds the PDS
  health check outside Aspire.
- **The client factory's default session store is in memory, with a warning at startup.**
  `AddAtProtoServer()` wrote every user's tokens to `{LocalApplicationData}/ATProtoNet/tokens`
  without being asked; `WithClientFactory()` keeps them in memory unless you choose a store. Choose
  one: `WithFileSessionStore()` for the old behaviour, `WithEfCoreSessionStore<TContext>()`,
  `WithSessionStore<T>()`, or `WithInMemorySessionStore()` to keep memory without the warning.
- **One named `HttpClient` for every `AtProtoClient` of the registration**,
  `AtProtoServiceCollectionExtensions.HttpClientName` (`"ATProtoNet"`), exposed as
  `IAtProtoBuilder.HttpClient`. The client factory's client was named `"AtProtoClient"`.
- **Every options class goes through `IOptions<T>` and is validated when the host starts.**
  `AtProtoClientOptions`, `AtProtoOAuthServerOptions`, `FileSessionStoreOptions` (0.6
  `FileTokenStoreOptions`), `IdentityResolverOptions`, `SpaceServerOptions` and `PdsAdminOptions`
  bind from configuration with `services.Configure<T>(section)`, and a bad value (an instance URL
  that is not an absolute http(s) URL, OAuth scopes without `atproto`, a non-positive timeout, a PDS
  admin client without its URL or password, a space authority without `ServiceDid`…) stops
  `StartAsync` with an `OptionsValidationException` naming it. The callbacks passed to `AddAtProto`,
  `WithOAuth`, `WithFileSessionStore`, `AddAtProtoIdentity`, `AddAtProtoSpaces` and
  `AddAtProtoPdsAdmin` run after configuration, whichever order the calls are made in, so code wins.
- **Every `AddAtProtoIdentity(configure)` and `AddAtProtoSpaces(configure)` call applies**, in
  order; the first call's options used to win and the others were dropped.
- **`AddAtProtoPdsAdmin()` reports a missing setting at startup** (`OptionsValidationException`)
  instead of throwing `InvalidOperationException` from the registration call, and a space authority
  without `ServiceDid` fails options validation instead of throwing from its startup check. Catch
  `OptionsValidationException` where you caught the old exceptions.
- **`AtProtoClientSettings` is gone.** Its `InstanceUrl` and `AutoRefreshSession` keys bind to
  `AtProtoClientOptions` from the same section, `DisableHealthChecks` still applies, and
  `DisableResilience` has nothing left to disable. `RelayUrl` (`AtProto:RelayUrl`) is gone with it;
  see [Streaming](#streaming).

## The client

- **`AtProtoClientBuilder` is removed: one constructor builds the client**, every argument
  optional: `new AtProtoClient(options, httpClient, sessionStore, logger)`. `WithInstanceUrl` /
  `WithAutoRefreshSession` become `AtProtoClientOptions` properties,
  `WithHttpClient` / `WithSessionStore` the `httpClient:` / `sessionStore:` arguments,
  `WithLoggerFactory(f)` the `logger: f.CreateLogger<AtProtoClient>()` argument, and
  `WithRelayUrl` is gone (see [Streaming](#streaming)).

  ```csharp before
  var client = new AtProtoClientBuilder()
      .WithInstanceUrl("https://pds.example.com")
      .WithSessionStore(store)
      .WithLoggerFactory(loggerFactory)
      .Build();
  ```

  <!-- snippet: ATProtoNet.Auth.IAtProtoSessionStore store; ILoggerFactory loggerFactory; -->
  ```csharp
  using ATProtoNet.Auth;

  var client = new AtProtoClient(
      new AtProtoClientOptions { InstanceUrl = "https://pds.example.com" },
      sessionStore: store,
      logger: loggerFactory.CreateLogger<AtProtoClient>());
  ```

- **`PdsUrl` / `SetPdsUrl(string)` are `ServiceUrl` / `SetServiceUrl(Uri)`.** The service URL is
  held by the client instead of written to `HttpClient.BaseAddress`, and `ServiceUrl` is a `Uri` with
  a trailing slash.
- **A supplied `HttpClient`'s `BaseAddress` is ignored.** `AtProtoClient` always starts at
  `AtProtoClientOptions.InstanceUrl`, and `PdsAdminClient` always sends to `PdsAdminOptions.Url`; put
  the service URL in the options.
- **`XrpcClient` is internal.** No public member of `AtProtoClient` exposed it. `MaxRateLimitRetries`
  moves to `AtProtoClientOptions.RateLimit.MaxRetries`; `BaseUrl`, `IsAuthenticated`,
  `UpdateDPoPNonce`, the body-less `QueryAsync` and the no-op `Dispose` are removed. Call custom XRPC
  methods through `AtProtoClient.QueryAsync` / `ProcedureAsync`, or `AtProtoClient.Transport`.
- **`SetLabelers` is one `params IEnumerable<string>` method**: calls compile unchanged, recompile.
- **`AtProtoClientOptions.OAuth` is removed**; nothing read it. OAuth is configured on the
  `OAuthClient`.

```csharp
using ATProtoNet.Http;

client.SetServiceUrl(new Uri("https://pds.example.com"));
Console.WriteLine(client.ServiceUrl);   // https://pds.example.com/

var withRetries = new AtProtoClient(new AtProtoClientOptions
{
    RateLimit = new XrpcRateLimitOptions { MaxRetries = 5 },
});
```

## Typed identifiers

Every model and client method takes and returns `Did`, `Handle`, `AtIdentifier`, `AtUri`, `Nsid`,
`Cid`, `RecordKey`, `Tid` and `AtDatetime` wherever the Lexicon field carries that format: the
`com.atproto.*`, `app.bsky.*`, `chat.bsky.*`, `tools.ozone.*` and `site.standard.*` models and
clients, `RecordCollection<T>`, `StrongRef`, `Label`, `CidLink`, `RecordRef`, `RecordView<T>`,
`PdsAdminClient`, the sessions, the Spaces client and server, and the firehose and Jetstream models.
Lexicon-required fields that defaulted to an empty string are `required`. See
[Identity Types](identity-types.md).

Values from the API pass straight through; typed values convert implicitly to `string`. Parse
literals once, where they enter your code, and keep constants in `static readonly` fields:

```csharp before
var todos = client.GetCollection<TodoItem>("com.example.todo.item");
var record = await client.Repo.GetRecordAsync("did:plc:abc123", "com.example.todo.item", "self");
```

```csharp
var alice = Did.Parse("did:plc:abc123");
var collection = Nsid.Parse("com.example.todo.item");

var record = await client.Repo.GetRecordAsync(alice, collection, RecordKey.Parse("self"));
string text = record.Uri;   // identifiers convert to string implicitly
```

- **`AtProtoClient.Did` / `Handle` / `LatestRepoRev` are `Did?` / `Handle?` / `Tid?`**, and
  `GetCollection<T>(…)`, `QueryAsync` and `ProcedureAsync` take an `Nsid`.
- **Parsing is strict.** `Parse` throws, and `TryParse` returns `false`, for every value the atproto
  specs and the official `atproto-interop-tests` syntax fixtures reject, including any `Cid` that is
  not a base32 CIDv1 with the DRISL/DAG-CBOR or raw codec and a SHA-256 digest. JSON deserialization
  fails the same way, and a response carrying an invalid identifier throws
  `XrpcResponseFormatException`. Read legacy or foreign data with `TryParse`.
- **The identifier types are `sealed record`s** (`Did`, `Handle`, `AtIdentifier`, `Nsid`, `Tid`,
  `RecordKey`, `Cid`, `AtUri`, `SpaceUri`, `SpaceRecordUri`). Source-compatible; recompile. `Handle`
  equality is ordinal, which is equivalent since handles are lower-cased.
- **`AtUri` components are typed.** `Collection` is `Nsid?` and `RecordKey` is `RecordKey?`, and
  `Repo` is parsed once. `AtUri.Create(AtIdentifier, string?, string?)` is replaced by
  `Create(AtIdentifier repo, Nsid? collection = null, RecordKey? rkey = null)`, which throws when a
  record key is given without a collection. Use `uri.Collection?.Value` where a `string` member was
  used.
- **Lexicon `datetime` fields are `AtDatetime`**: `AtProtoRecord.CreatedAt` (`AtDatetime?`),
  `createdAt`, `indexedAt`, `usedAt`, `cts`, `exp`, `seenAt`, `sentAt` and the rest. Set with
  `AtDatetime.Now()`, `AtDatetime.Parse("…")` or `AtDatetime.FromDateTimeOffset(value)`; read with
  `.Value` / `.TryGetValue(out …)`, or `.ToString()` for the text.
- **`RichTextBuilder.Mention` takes `(Handle, Did)`**, `ServiceAuthGenerator` a `Did` and an `Nsid`
  (see [Service auth](#service-auth)), and `PlcOperationBuilder.CreateGenesisOperation` a `Handle`.

```csharp
var uri = AtUri.Create(Did.Parse("did:plc:abc123"), Nsid.Parse("com.example.todo.item"), RecordKey.Parse("3k2la"));
string? collectionText = uri.Collection?.Value;

var dueDate = AtDatetime.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(3));
if (dueDate.TryGetValue(out var instant))
    Console.WriteLine(instant);
```

### One API shape

The typed identifiers came with one convention for every client method (see
[CONTRIBUTING](../CONTRIBUTING.md#lexicon-models-and-clients)):

- **Parameters follow one order**: subject, required inputs, filters, `limit`, `cursor`, token.
  `RepoClient.ListRecordsAsync` and `RecordCollection<T>.ListAsync` / `ListFromAsync` take `reverse`
  before `limit` and `cursor`; `RepoClient.ListMissingBlobsAsync` takes `limit` before `cursor`;
  `ModerationClient.CreateReportAsync` takes the `subject` first; `GetTimelineAsync`,
  `GetAuthorFeedAsync`, `ListNotificationsAsync`, `ConvoClient.ListConvosAsync`,
  `ModerationClient.QueryEventsAsync`, `SignatureClient.SearchAccountsAsync` /
  `FindRelatedAccountsAsync`, and the Spaces `ListRecordsAsync` / `ListRepoOpsAsync` (client and
  `ISpaceRepoHost`) take their filters first. Positional calls no longer compile: reorder them or
  name the arguments.
- **`ListAllRecordsAsync` is `EnumerateRecordsAsync`, and enumerators take `int? pageSize = null`**,
  the server's default. `RecordCollection<T>.EnumerateAsync` / `EnumerateFromAsync` used 100; pass
  `pageSize: 100` to keep it. `JetstreamArchiveClient.ListAllSegmentsAsync` is
  `EnumerateSegmentsAsync`.
- **Only 13 `Enumerate*` helpers remain** — the ones the tracking issues named, plus the ones
  0.6 already had: `RecordCollection<T>.EnumerateAsync` / `EnumerateFromAsync`,
  `RepoClient.EnumerateRecordsAsync`, `SimpleSpaceClient.EnumerateMembersAsync`,
  `SpaceClient.EnumerateReposAsync` / `EnumerateRecordsAsync`,
  `JetstreamArchiveClient.EnumerateSegmentsAsync`, `FeedClient.EnumerateTimelineAsync` /
  `EnumerateAuthorFeedAsync` / `EnumerateActorLikesAsync`, `GraphClient.EnumerateFollowersAsync` /
  `EnumerateFollowsAsync` / `EnumerateListMembersAsync`, `NotificationClient.EnumerateNotificationsAsync`,
  `ConvoClient.EnumerateMessagesAsync` and `BookmarkClient.EnumerateBookmarksAsync`. Every other
  `Enumerate*` / `ListAll*` wrapper (feeds, blocks, mutes, lists, starter packs, convos, Ozone's
  queues, reports, sets, team, safelink, `sync`, `label`, `admin`, `standard-site`, …) is gone; the
  `Http.Pagination` class behind all of them is now **public**, so any endpoint's `GetXAsync(...,
  cursor, cancellationToken)` walks with one line:
  `Pagination.EnumerateAsync<GetFooResponse, FooView>((cursor, ct) => client.Foo.GetFooAsync(id,
  cursor: cursor, cancellationToken: ct))`.
- **`ICursoredResponse` is replaced by `ICursorPage<T>`**, which every cursored response and
  `RecordPage<T>` implements, and **model collections are `IReadOnlyList<T>`** (they were
  `List<T>`). Copy a list you need to change: `[.. response.Records]`. Method inputs take
  `IEnumerable<T>` (`ApplyWritesAsync`, `DisableInviteCodesAsync`, `QueryLabelsAsync`, …), and
  `GetInviteCodesResponse.Codes` is typed as `InviteCode` instead of `JsonElement`.
- **`ProfileViewBasic` ⊂ `ProfileView` ⊂ `ProfileViewDetailed` by inheritance**, and
  `GetSessionResponse` is the base of `SessionResponse` the same way, instead of each redeclaring
  the fields it shares with the others. `is ProfileView` now also matches a `ProfileViewDetailed`
  (`is ProfileViewBasic` matches all three). Migration: recompile; where the distinction matters,
  match the most specific type first or check a property unique to that view.
- **`Tools.Ozone.Report.LiveStats` is gone.** `GetLiveStatsResponse.Stats` is
  `Tools.Ozone.Queue.QueueStats`, which was already structurally identical. Migration: replace
  `LiveStats` with `QueueStats`.
- **The 72 cursored responses derive from `Models.CursorPage<T>`** instead of implementing
  `ICursorPage<T>` by hand, and are `sealed record`s rather than `sealed class`es. `Items` is a
  public `override`, so `response.Items` works without a cast to `ICursorPage<T>`; the wire shape
  is unchanged. `GraphClient.GetListAsync`'s response renames `Items` to `Members`, since the
  Lexicon's own `items` field collided with the new override. Migration: recompile; read
  `GetListResponse.Members` in place of `Items`.
- **Single-use request bodies are internal.** A request type that only one client method built is
  internal, and the method takes its fields as parameters: `CreateRecordRequest`,
  `PutRecordRequest`, `DeleteRecordRequest`, `ApplyWritesRequest`, `CreateSessionRequest`,
  `CreateAppPasswordRequest`, `RequestPasswordResetRequest`, `ResetPasswordRequest`,
  `ConfirmEmailRequest`, `CreateInviteCodeRequest`, `RevokeAppPasswordRequest`,
  `ReserveSigningKeyRequest`, `UpdateHandleRequest`, `NotifyOfUpdateRequest`, `RequestCrawlRequest`,
  `AdminDeleteAccountRequest`, `DisableAccountInvitesRequest`, `EnableAccountInvitesRequest`,
  `UpdateAccountEmailRequest`, `UpdateAccountHandleRequest`, `UpdateAccountPasswordRequest`,
  `DisableInviteCodesRequest`, `CreateReportRequest`; on `app.bsky.*` `PutPreferencesRequest`,
  `MuteActorRequest`, `MuteActorListRequest`, `MuteThreadRequest` and `UpdateSeenRequest`; on chat
  and Ozone `SendMessageRequest`, `SendMessageBatchRequest`, `DeleteMessageForSelfRequest`,
  `LeaveConvoRequest`, `MuteConvoRequest`, `UnmuteConvoRequest`, `UpdateReadRequest`,
  `AcceptConvoRequest`, `AddReactionRequest`, `RemoveReactionRequest`, `DeleteTemplateRequest`,
  `DeleteSetRequest`, `AddValuesRequest`, `DeleteValuesRequest` and `DeleteMemberRequest`; and on
  Spaces `CreateSpaceRecordRequest`, `PutSpaceRecordRequest`, `DeleteSpaceRecordRequest`,
  `ApplySpaceWritesRequest` and `NotifySpaceDeletedRequest`. The never-sent
  `DeleteChatAccountRequest` is removed.

```csharp before
await foreach (var item in client.Repo.ListAllRecordsAsync(repo, "com.example.todo.item"))
    Console.WriteLine(item.Uri);
var page = await todos.ListAsync(25, null, reverse: true);
```

<!-- snippet: RecordCollection<TodoItem> todos; -->
```csharp
await foreach (var item in client.Repo.EnumerateRecordsAsync(client.Did!, Nsid.Parse("com.example.todo.item")))
    Console.WriteLine(item.Uri);

var page = await todos.ListAsync(reverse: true, limit: 25);
var mutable = page.Records.ToList();   // the page's list is read-only
```

## Sessions

One session model replaces three. See [Session Management](session-management.md).

- **`AtProtoSession`, with `PasswordSession` and `OAuthSession`**, replaces `Session`,
  `OAuthSessionResult` and `AtProtoTokenData`. Sessions are immutable records carrying `Did`,
  `Handle`, `ServiceEndpoint` and `ExpiresAt` (which replaces `ExpiresIn` + `TokenObtainedAt`); both
  `Did` and `Handle` are typed and `required`. An `OAuthSession` holds its DPoP key as PKCS#8 bytes
  (`DPoPKey`) instead of a `DPoPProofGenerator`, so it owns nothing disposable, and an unverified
  handle is `handle.invalid` (`IsHandleVerified` is gone). `Session.DidDoc` is dropped in favour of
  `ServiceEndpoint`.
- **`AtProtoClient.Session` is `AtProtoSession?`, and `AtProtoClient.OAuthSession` is removed**:
  pattern-match the session instead.
- **`ApplyOAuthSessionAsync` is replaced by `ApplySessionAsync(session, oauthClient)`**, which
  installs either kind of session without a request. Its `tokenStore` parameter is gone: the client
  persists to its own session store. `ResumeSessionAsync` takes an `AtProtoSession` and an optional
  `OAuthClient`, and returns the session as installed.
- **`OAuthClient.CompleteAuthorizationAsync` returns an `OAuthSession`, and `RefreshTokensAsync` is
  replaced by `RefreshAsync(session)`**, which returns the refreshed session. Nothing to dispose:
  drop the `using`. Outside an `AtProtoClient`, which refreshes by itself, call
  `session = await oauth.RefreshAsync(session)`.

```csharp before
using var result = await oauth.CompleteAuthorizationAsync(code, state, issuer);
await client.ApplyOAuthSessionAsync(result, oauth, tokenStore);
if (client.OAuthSession is { } oauthSession)
    Console.WriteLine(oauthSession.Did);
```

<!-- snippet: ATProtoNet.Auth.OAuth.OAuthClient oauth; string code, state, issuer; -->
```csharp
using ATProtoNet.Auth;

var session = await oauth.CompleteAuthorizationAsync(code, state, issuer);
await client.ApplySessionAsync(session, oauth);   // persisted to the client's own session store

switch (client.Session)
{
    case OAuthSession o:
        Console.WriteLine($"{o.Did} via {o.Issuer}");
        break;
    case PasswordSession p:
        Console.WriteLine($"{p.Did}, email {p.Email}");
        break;
}

// A password session you saved
await client.ApplySessionAsync(new PasswordSession
{
    Did = Did.Parse("did:plc:abc123"),
    Handle = Handle.Parse("alice.example.com"),
    ServiceEndpoint = new Uri("https://pds.example.com"),
    AccessJwt = "…",
    RefreshJwt = "…",
});
```

- **One persistence interface: `IAtProtoSessionStore`**, with `GetAsync(Did)`,
  `SetAsync(AtProtoSession)` and `RemoveAsync(Did)`, replaces `ISessionStore` (whose `LoadAsync` the
  SDK never called) and `IAtProtoTokenStore`. The implementations are `InMemoryAtProtoSessionStore`
  (now in the core package), `FileAtProtoSessionStore` (with `FileSessionStoreOptions`) and
  `EfCoreAtProtoSessionStore<TContext>`, replacing `InMemorySessionStore`,
  `InMemoryAtProtoTokenStore`, `FileAtProtoTokenStore` (`FileTokenStoreOptions`) and
  `EfCoreAtProtoTokenStore<TContext>`. The `AtProtoClient` constructor, `AddAtProto`, the builder's
  `WithSessionStore` and `AtProtoClientFactory` take the new interface, and a client with no store no
  longer writes to a hidden in-memory one. **No data migration**: the file and EF Core stores read
  what their 0.6 versions wrote, and the EF Core table and entity (`AtProtoTokens`,
  `AtProtoTokenEntity`) are unchanged. A custom store implements the three members and serializes
  with `JsonSerializer.Serialize<AtProtoSession>` (see
  [Persisting sessions](session-management.md#persisting-sessions)).
- **`ServerClient` no longer changes the client's session.** `CreateSessionAsync` and
  `CreateAccountAsync` return tokens without installing them (and are sent without the installed
  session's credentials), and `RefreshSessionAsync(refreshJwt)` / `DeleteSessionAsync(refreshJwt)`
  take the refresh JWT explicitly. Sign in with `client.LoginAsync` or the new
  `client.CreateAccountAndLoginAsync`, and sign out with `client.LogoutAsync`; a direct call passes
  `session.RefreshJwt`.
- **`AutoRefreshSession` means refreshing on demand, and the refresh timer is opt-in.** With it on
  (the default) the client refreshes before a request when the access token is about to expire, and
  once after the service rejects it; `BackgroundRefresh` (default off) adds the old idle timer.
  Turning `AutoRefreshSession` off now disables refreshing altogether; code that turned it off only
  to avoid the timer can drop the setting.
- **`LogoutAsync` throws when the service-side sign-out fails.** After the session is removed
  locally and from the store, a failed `deleteSession` or OAuth revocation (or store removal) is
  rethrown instead of logged, and an OAuth session installed without its `OAuthClient` throws
  `InvalidOperationException`, since it could not be revoked. Catch `XrpcException` /
  `OAuthException` where a best-effort sign-out is wanted.

```csharp
using ATProtoNet.Auth.OAuth;

try
{
    await client.LogoutAsync();
}
catch (Exception ex) when (ex is XrpcException or OAuthException)
{
    logger.LogWarning(ex, "Signed out locally; the service did not confirm the sign-out");
}
```

## Errors

One exception family replaces the HTTP one. See [Error Handling](error-handling.md).

- **`XrpcException` replaces `AtProtoHttpException`.** Every XRPC error throws
  `ATProtoNet.Http.XrpcException`, which derives from the new `AtProtoException` and no longer from
  `HttpRequestException`. It carries `Nsid`, `Error` (formerly `ErrorType`, now never null: a body
  without an error envelope gets the status's generic name), `ErrorMessage`, a non-nullable
  `StatusCode`, `ResponseBody`, the response `Headers` and `Is(name)`. A 429 surfaces as
  `XrpcRateLimitException`, and a 401 or `ExpiredToken` / `InvalidToken` as
  `XrpcAuthenticationException`. Code that caught `HttpRequestException` to see XRPC errors catches
  `XrpcException`.
- **Every SDK exception derives from `AtProtoException`**: `OAuthException`,
  `SpaceCredentialException`, `SpaceTokenException`, `SpaceRepoVerificationException`,
  `DidResolutionException`, `EventStreamException` and `JetstreamException` included, so one
  `catch (AtProtoException)` covers every SDK failure. **`OAuthException.ErrorCode` is `Error`.**
- **A response that does not match its type throws `XrpcResponseFormatException`** instead of a raw
  `JsonException` or `InvalidOperationException`, from every XRPC call, `RecordCollection<T>` and
  `RepoClient.GetRecordAsync<T>`. It names the method in `Nsid` and keeps the `JsonException` as its
  inner exception.
- **Calls that need a session throw `XrpcAuthenticationException` without one** —
  `RecordCollection<T>` calls on the signed-in account's repository and the `client.Bsky` helpers,
  with `AuthenticationRequired`, before sending anything — instead of
  `InvalidOperationException("Not authenticated. Call LoginAsync first.")`.
- **`ATProtoNet.Server.Xrpc.XrpcException` is gone**: hosted endpoints throw the same
  `ATProtoNet.Http.XrpcException`, whose status is an `HttpStatusCode`, as is
  `SpaceVerificationException`'s, which now derives from it. Add `using ATProtoNet.Http;` and pass
  `HttpStatusCode.NotFound` where `StatusCodes.Status404NotFound` was passed.
- The exceptions of identity resolution, DAG-CBOR decoding and streaming changed too: see
  [Identity resolution](#identity-resolution), [Repositories](#repositories) and
  [Streaming](#streaming).

```csharp before
try { await todos.GetAsync("missing"); }
catch (AtProtoHttpException ex) when (ex.ErrorType == "RecordNotFound") { }
catch (InvalidOperationException) { /* not signed in */ }
```

<!-- snippet: RecordCollection<TodoItem> todos; -->
```csharp
using System.Net;
using ATProtoNet.Http;

try
{
    await todos.GetAsync(RecordKey.Parse("missing"));
}
catch (XrpcException ex) when (ex.Is(XrpcErrors.RecordNotFound))
{
    // or todos.FindAsync, which returns null
}
catch (XrpcAuthenticationException ex) when (ex.Is(XrpcErrors.AuthenticationRequired))
{
    // not signed in
}

// On the server side, in an XRPC endpoint
void Refuse() => throw new XrpcException(XrpcErrors.RecordNotFound, "No such todo.", HttpStatusCode.NotFound);
```

## Records and the Bluesky helpers

- **One `RecordRef` and one `RecordView<T>` for every write and every typed read.** Both are sealed
  records built by constructor: `RecordRef(AtUri uri, Cid cid, CommitMeta? commit)` (with
  `RecordKey`, `ValidationStatus` and `ToStrongRef()`) and `RecordView<T>(AtUri uri, Cid? cid, T value)`
  (with `RecordKey`). `RepoClient.CreateRecordAsync` / `PutRecordAsync` return a `RecordRef`,
  `RepoClient.GetRecordAsync<T>` a `RecordView<T>` and the untyped `GetRecordAsync` a
  `RecordView<JsonElement>`, as do the `StandardSiteClient` methods; `CreateRecordResponse`,
  `PutRecordResponse` and `GetRecordResponse<T>` are internal and the non-generic
  `GetRecordResponse` is gone. The `Uri`, `Cid`, `Value` and `Commit` properties read as before;
  replace object initializers with the constructors, and `CreateRecordResponse` /
  `PutRecordResponse` in declarations with `RecordRef`.
- **`AtProtoRecord.CreatedAt` is no longer set when the object is constructed.** It is `null` until
  you set it, and `RecordCollection<T>.CreateAsync` stamps the current time only when it is unset, so
  a record read without `createdAt` is written back without one. Set `CreatedAt` yourself before
  writing a new record through `PutAsync` or `RepoClient`; `CreateAsync` needs nothing.
- **The Bluesky helpers move to `client.Bsky` and take typed subjects.** `client.PostAsync` is
  `client.Bsky.PostAsync(RichText, PostOptions?)`: `RichText` carries the text and its facets and
  converts implicitly from `string`, and `PostOptions` holds `Embed`, `Reply`, `Langs`, `Labels`,
  `Tags` and `CreatedAt`. `LikeAsync(StrongRef)`, `RepostAsync(StrongRef)` and `FollowAsync(Did)`
  return a `RecordRef`. `UnlikeAsync`, `UndoRepostAsync`, `UnfollowAsync` and `DeletePostAsync` are
  replaced by `client.Bsky.DeleteRecordAsync(AtUri)`, which throws `ArgumentException` without a
  request when the URI names no collection and record key.
- **`RichTextBuilder.Build()` returns `RichText`** instead of a `(string Text, IReadOnlyList<Facet>?
  Facets)` tuple; it still deconstructs, and `Facets` is empty, not `null`, when there are none.
- **`UpdateProfileAsync` moves to `client.Bsky` and takes an edit callback.**
  `UpdateProfileAsync(Action<ProfileRecord> update)` replaces the overload with four optional
  parameters, returns a `RecordRef`, and keeps every field you do not touch; `ProfileRecord`'s
  properties are settable rather than `init`.

```csharp before
var (text, facets) = new RichTextBuilder().Text("Hello ").Tag("atproto").Build();
var posted = await client.PostAsync(text, facets, embed: embed);
var like = await client.LikeAsync(posted.Uri, posted.Cid);
await client.UnlikeAsync(like.Uri);
await client.UpdateProfileAsync(displayName: "Alice");
```

<!-- snippet: ATProtoNet.Lexicon.App.Bsky.Embed.EmbedBase embed; -->
```csharp
using ATProtoNet.Lexicon.App.Bsky.RichText;

var text = new RichTextBuilder().Text("Hello ").Tag("atproto").Build();
var posted = await client.Bsky.PostAsync(text, new PostOptions { Embed = embed });

var like = await client.Bsky.LikeAsync(posted.ToStrongRef());
await client.Bsky.DeleteRecordAsync(like.Uri);

await client.Bsky.UpdateProfileAsync(profile => profile.DisplayName = "Alice");
```

- **`Embed.RecordView` is renamed `RecordEmbedView`** (the view of a quoted record,
  `app.bsky.embed.record#view`), so it no longer shares a name with `RecordView<T>`;
  `RecordWithMediaView.Record` has the new type.
- **`BlobLink` is removed: `BlobRef.Ref` is a `CidLink`** (`new CidLink { Link = cid }` or
  `CidLink.FromCid(cid)`).
- **`CreatePdsAccountRequest` is removed**: `PdsAdminClient.CreateAccountAsync` takes the
  `com.atproto.server.createAccount` input, `CreateAccountRequest`
  (`ATProtoNet.Lexicon.Com.AtProto.Server`), with the same properties plus `VerificationCode`,
  `VerificationPhone` and `PlcOp`. A password is still required.
- **`StandardSiteClient` is typed end to end.** Every method takes an `AtIdentifier` repository and a
  `RecordKey`, and `ListPublicationsAsync` / `ListDocumentsAsync` / `ListSubscriptionsAsync` return a
  `RecordPage<T>` of typed records instead of the raw `ListRecordsResponse`; read `entry.Value`
  directly instead of deserializing it.

## Custom XRPC

See [Custom XRPC Endpoints](custom-xrpc.md).

- **Queries take `XrpcParams`, procedures a typed input.** `QueryAsync<TOut>(Nsid, XrpcParams?,
  XrpcCallOptions?, CancellationToken)`; `ProcedureAsync<T>(nsid, object? body, …)` is replaced by
  `ProcedureAsync<TIn, TOut>(nsid, input, parameters?, options?, …)`, `ProcedureAsync<TIn>(nsid,
  input, …)` and `ProcedureAsync(nsid, parameters?, …)`. An anonymous object or dictionary as query
  parameters still works through a `QueryAsync<TOut>(Nsid, object, …)` overload, now marked
  `[RequiresUnreferencedCode]`.
- **`XrpcCallOptions` comes before the cancellation token** (`Proxy`, `AcceptLabelers`, `Headers`,
  `Timeout`, for one call). A call that passed the token positionally as the third argument passes
  it by name, `cancellationToken: ct`; since procedures now take `parameters` third, pass options by
  name too (`options: …`).
- **Blob and repository downloads return `XrpcStreamResponse`**: `SyncClient.GetBlobAsync` /
  `GetRepoAsync` and `SpaceClient.GetBlobAsync` / `GetRepoAsync` return the stream with its
  `ContentType` and `ContentLength`, and disposing it releases the HTTP response. So does
  `ChatActorClient.ExportAccountDataAsync`, whose JSON Lines the old `Task<byte[]>` failed to read.

```csharp before
var result = await client.ProcedureAsync<BatchResult>("com.example.todo.markAllComplete", body);
var page = await client.QueryAsync<SearchResult>("com.example.todo.search", new { q = "milk" }, ct);
byte[] blob = await client.Sync.GetBlobAsync(did, cid);
```

<!-- snippet: MarkCompleteInput body; Did did; Cid cid; -->
```csharp
using ATProtoNet.Http;

var result = await client.ProcedureAsync<MarkCompleteInput, BatchResult>(
    Nsid.Parse("com.example.todo.markAllComplete"), body);

var page = await client.QueryAsync<SearchResult>(
    Nsid.Parse("com.example.todo.search"), new XrpcParams { { "q", "milk" } }, cancellationToken: ct);

await using var blob = await client.Sync.GetBlobAsync(did, cid);
await using var file = File.Create("blob.bin");
await blob.Content.CopyToAsync(file);

public sealed record MarkCompleteInput([property: JsonPropertyName("before")] AtDatetime Before);
public sealed record BatchResult([property: JsonPropertyName("processed")] int Processed);
public sealed record SearchResult([property: JsonPropertyName("items")] IReadOnlyList<AtUri> Items);
```

### Hosted XRPC endpoints

See [XRPC Endpoint Handlers](xrpc-handlers.md).

- **Endpoints declare their NSID once, as a static `Nsid`.** `IXrpcEndpoint.Nsid` is
  `static abstract Nsid Nsid { get; }`, and `[XrpcEndpoint]` is removed: the instance property was
  read from an uninitialized handler, so one written as an auto-property or with a field initializer
  failed to register. Like any interface with a static abstract member, the endpoint interfaces can
  no longer be generic type arguments (CS8920).
- **`AddXrpcEndpointsFromAssembly` registers every class implementing `IXrpcEndpoint`**, exactly as
  `AddXrpcEndpoint<T>()` does, now that the attribute it looked for is gone. Registration also
  rejects a handler implementing no endpoint interface or several, and a second handler for an NSID
  that already has one. Keep handlers that should not be mapped out of scanned assemblies.
- **`MapXrpcEndpoints()` returns the `/xrpc` `RouteGroupBuilder`**, so `.RequireAuthorization()`,
  `.RequireRateLimiting(…)`, `.RequireCors(…)` and `.WithMetadata(…)` apply to every XRPC endpoint.
  It used to return the builder it was called on: make a `Map…` call that was chained onto its result
  on the app instead, or it maps under `/xrpc`.

```csharp before
[XrpcEndpoint(Nsid = "com.example.getStatus")]
public class GetStatusEndpoint : IXrpcQuery<StatusOutput>
{
    public string Nsid => "com.example.getStatus";
    // ...
}
```

```csharp
using ATProtoNet.Server.Xrpc;

public class GetStatusEndpoint : IXrpcQuery<StatusOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.getStatus");

    public Task<StatusOutput> HandleAsync(HttpContext context, CancellationToken ct) =>
        Task.FromResult(new StatusOutput("ok"));
}

public sealed record StatusOutput([property: JsonPropertyName("status")] string Status);
```

## Unions and unknown fields

See [Custom Lexicon Records: Unions and unknown fields](custom-records.md#unions-and-unknown-fields).

- **The SDK's Lexicon unions are open, with an `Unknown*` variant.** A `$type` the SDK does not model
  reads as `UnknownEmbed`, `UnknownEmbedView`, `UnknownFacetFeature`, `UnknownThreadNode`,
  `UnknownModerationSubject`, `UnknownModEvent`, `UnknownConvoLogEntry` and so on instead of
  throwing. `EmbedBase`, `EmbedView`, `FacetFeature`, `ThreadNode`, `ModEventType`,
  `ModerationSubject` (formerly `ReportSubject`), the Spaces policies and the chat unions are
  `[AtProtoUnion]` bases. Add an `Unknown*` arm, or a discard, to every `switch` over them; the
  compiler will not flag a missing one. `ApplyWriteOperation` and `SpaceWriteOp` stay strict, since
  their Lexicons mark them closed.
- **Lexicon unions that were `JsonElement` are typed open unions**: `FeedViewPost.Reason`
  (`ReasonRepost`, `ReasonPin`), `SkeletonFeedPost.Reason`, `ThreadgateRecord.Allow`
  (`ThreadgateMentionRule`, …), `PostgateRecord.EmbeddingRules`, `RecordEmbedView.Record`
  (`EmbeddedRecordView`: a quoted post, the not-found, blocked and detached placeholders, and feed,
  list, labeler and starter-pack views), `GetRelationshipsResponse.Relationships` (`Relationship`,
  `NotFoundActor`), the preferences of `GetPreferencesAsync` / `PutPreferencesAsync` (16
  `Preference` types), and on chat `GetMessagesResponse.Messages`, `EnumerateMessagesAsync` and
  `ConvoView.LastMessage` (`ConvoMessage`: `MessageView`, `DeletedMessageView`,
  `SystemMessageView`, …). An unknown variant's `Raw` is the old `JsonElement`, and writes back
  unchanged. `Relationship.Type` and `NotFoundActor.Type` are removed, and `GeneratorView`,
  `ListView`, `LabelerView` and `StarterPackViewBasic` now write their `$type`.
- **Lexicon models derive from `LexObject`**, and so does `AtProtoRecord`: an `ExtensionData`
  dictionary keeps fields a model does not declare, and writes them back. This changes their base
  class; recompile. A record type that declared its own `[JsonExtensionData]` member drops it and uses
  the inherited `ExtensionData`.
- **`PostView` writes its `$type`**: `PostView` and `ThreadNode` derive from the new `PostEntry`
  union base, so a serialized `PostView` leads with `"$type":"app.bsky.feed.defs#postView"`.
- **`AtProtoJsonDefaults.Options` is read-only.** Adding a converter or changing a setting throws
  `InvalidOperationException`: register union variants on `LexiconTypeRegistry.Instance`, or copy the
  options (`new JsonSerializerOptions(AtProtoJsonDefaults.Options)`) for different settings.
- **`LexiconTypeRegistry` is reduced to union variants.** `CreateOptions()`, the record-type registry
  (`RegisterRecordType`, `GetRecordType`, `RecordTypes`, `ILexiconTypeRegistrar.RegisterRecordType`),
  `LoadPluginsFromAssembly`, `[LexiconPlugin]`, the public constructor and the `IAtProtoUnion` marker
  interface are removed; none of them affected serialization. Use `LexiconTypeRegistry.Instance`
  with `AtProtoJsonDefaults.Options`, delete record-type registrations, call `LoadPlugin<T>()`
  instead of scanning an assembly, and replace `IAtProtoUnion` with `[AtProtoUnion(...)]`.
- **`RegisterUnionVariant` refuses a closed union** (`[AtProtoUnion(Closed = true)]`) with
  `ArgumentException`, and a closed union's reads no longer consult the registry. Remove such a
  registration.

<!-- snippet: ATProtoNet.Lexicon.App.Bsky.Feed.PostView post; -->
```csharp
using ATProtoNet.Lexicon.App.Bsky.Embed;

var description = post.Embed switch
{
    null => "no embed",
    ImagesView images => $"{images.Images.Count} images",
    RecordEmbedView { Record: EmbeddedRecord quoted } => $"a quote of {quoted.Uri}",
    UnknownEmbedView unknown => $"an embed of type {unknown.Type}",
    _ => "another embed",
};
```

## Bluesky, chat and Ozone models

These follow the Lexicons now, where 0.6 had fields that were never on the wire:

- **`ProfileViewDetailed.AssociatedChat` is replaced by `Associated`** (`associatedChat` was never a
  field): read `profile.Associated?.Chat?.AllowIncoming`. `ProfileViewBasic` and `ProfileView` carry
  `Associated` too.
- **Lexicon references that were `JsonElement` are typed**: `PostView.Threadgate` and
  `GetPostThreadResponse.Threadgate` are `ThreadgateView`, `BlockedPost.Author` a required
  `BlockedAuthor`, `ViewerState.MutedByList` / `BlockingByList` `ListViewBasic`,
  `StarterPackView.Feeds` `GeneratorView`s, `LabelerView.Creator` / `LabelerViewDetailed.Creator`
  `ProfileView`, `ChatMemberView.Associated` `ProfileAssociated`, `ListRecord.Labels` `SelfLabels`,
  `ModEventViewDetail.SubjectBlobs` `BlobView`s, and `TeamMember.Profile` a `ProfileViewDetailed`
  (`TeamMemberProfile` is removed).
- **Notification methods drop `priority` and `seenAt`.** `ListNotificationsAsync` /
  `EnumerateNotificationsAsync` take `reasons` instead of both, and `GetUnreadCountAsync` drops
  `priority`: upstream ignores `priority`, and since 2026-09-21 answers `seenAt` on
  `listNotifications` with an error. Remove the arguments.
- **New Lexicon parameters shift positional arguments.** `SearchPostsAsync` takes
  `IEnumerable<string>? tags` where it took `string? tag`; `sort` precedes `limit` in
  `GetFollowersAsync` / `GetFollowsAsync` and their enumerators; `purposes` precedes `limit` in
  `GetListsAsync`; `MuteActorAsync`, `CreateReportAsync`, `DeactivateAccountAsync`,
  `QueryEventsAsync`, `ListMembersAsync` and `QuerySetsAsync` gain the inputs and filters their
  Lexicons define before the paging arguments or the token. Name the arguments after the first.
- **`VideoClient.UploadVideoAsync` uploads in parts and waits for processing.** It runs the
  multipart flow (`startUpload`, `uploadPart`, `finishUpload`), retries transient failures, polls
  the job and returns the completed `JobStatus`, whose `Blob` goes into a `VideoEmbed`; a failed job
  throws `VideoUploadException`, and it takes a `VideoUploadOptions?` before the token. Drop your
  polling loop, or call `UploadVideoInOneRequestAsync` for the old single-request behaviour. See
  [Video Upload](video.md).
- **Chat names follow the Lexicon.** `GetConvoAvailabilityResponse.CanConvo` is `CanChat`,
  `ListConvosAsync` takes `string? readState` (`"unread"`) instead of `bool? readOnly`, and
  `ConvoView.Opened` and `AcceptConvoResponse.Convo` are removed. `ListConvosAsync` also takes
  `kind` and `lockStatus` before `limit` / `cursor`: pass the paging arguments by name.
- **`ConvoClient.UpdateAllReadAsync` returns `UpdateAllReadResponse`** with the `UpdatedCount`, and
  takes an optional `status`. Existing `await`s compile unchanged; recompile.
- **Chat log entries and message embeds are typed.** `ConvoLogEntry` is the abstract base of one
  type per `getLog` event (`LogCreateMessage`, `LogAddMember`, … and `UnknownConvoLogEntry`) and keeps
  only `Rev` and `ConvoId`, losing `Type` and `Message`. `MessageInput.Embed` and `MessageView.Embed`
  are `MessageEmbed` / `MessageEmbedView`, their `Facets` are `Facet`s, and `MessageView.Text` is
  required. Pattern-match instead of reading `$type`, and embed a post as
  `new MessageRecordEmbed { Record = strongRef }`.
- **One moderation-subject union, `ModerationSubject`, in `ATProtoNet.Lexicon.Com.AtProto.Moderation`.**
  It replaces `ReportSubject` and the `tools.ozone.moderation` copy, and carries `RepoSubject`,
  `RecordSubject`, the new `RepoBlobSubject`, `MessageSubject`, `ConvoSubject` and
  `UnknownModerationSubject`. `CreateReportResponse.Subject`,
  `GetSubjectStatusResponse.Subject` and `UpdateSubjectStatusRequest` / `Response.Subject` are typed
  with it, and `RecordSubject.Cid` is required. Import the namespace where the Ozone subject types
  were used.
- **Ozone's review queue is `QueryStatusesAsync`.** `QuerySubjectsAsync`, `EnumerateSubjectsAsync`
  and `QuerySubjectsResponse` called `tools.ozone.moderation.querySubjects`, which never existed.
  `QueryStatusesAsync` takes every filter of `queryStatuses` in a `SubjectStatusFilter`
  (`Takendown` and `Appealed` are booleans) and returns `QueryStatusesResponse.SubjectStatuses`.
- **`SignatureClient.SearchAccountsAsync` takes signature values and returns account views**:
  `values` is `IEnumerable<string>` (pass the `SigDetail.Value`s), and `SearchAccountsResponse.Accounts`
  and `RelatedAccount.Account` are `AccountInfo`; `AccountResult` is removed.
- **Ozone events match their Lexicons.** `RecordViewDetail.BlobCids` is `Blobs`, the `BlobView`s
  Ozone sends (read `Blobs[i].Cid`). `ModEventLabel.CreateLabelVals` / `NegateLabelVals` and
  `ModEventTag.Add` / `Remove` are `required` (set them, empty where there is nothing to change).
  `ModEventMuteReporter.DurationInHours` is `int?` (`null` is permanent), and
  `ModEventReport.ReportType` is `required`.

```csharp before
var allowIncoming = profile.AssociatedChat?.AllowIncoming;
var convos = await client.Chat.Convo.ListConvosAsync(readOnly: true);
await foreach (var subject in client.Ozone.Moderation.EnumerateSubjectsAsync(reviewState: "open")) { }
```

<!-- snippet: ATProtoNet.Lexicon.App.Bsky.Actor.ProfileViewDetailed profile; -->
```csharp
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;

var allowIncoming = profile.Associated?.Chat?.AllowIncoming;
var convos = await client.Chat.Convo.ListConvosAsync(readState: "unread");

var queue = await client.Ozone.Moderation.QueryStatusesAsync(new SubjectStatusFilter { Takendown = false });
foreach (var status in queue.SubjectStatuses)
    Console.WriteLine(status.ReviewState);
```

## Identity resolution

See [Identity Resolution](did-resolution.md).

- **One immutable `DidDocument`.** `ATProtoNet.Auth.OAuth.DidDocument` and `DidService` are
  removed, leaving `ATProtoNet.Identity.DidDocument`, whose properties are init-only: `Id` is a
  `Did`, the lists are `IReadOnlyList<T>`, and service entries are `DidDocumentService` (renamed from
  `ServiceEndpoint`, with a nullable `Endpoint`). `GetHandle()` returns a validated `Handle?`,
  `GetPdsEndpoint()` a `Uri?`, and `GetServiceEndpoint(fragment, type?)` finds any service; lookups
  take the first entry with a matching id, and `TryGetServiceEndpoint` and `TryGetVerificationKey`
  tell an absent entry from a malformed one. Build documents with object initializers, and read
  `GetPdsEndpoint()?.OriginalString` where a string was used.
- **`DidResolutionException` replaces `PlcException` and `DidWebException`.** Every resolver throws
  it — network failures, timeouts and bodies that break off included — with a
  `DidResolutionErrorKind`: `Tombstoned` is `Deactivated`, `ParseError` and `ValidationError` are
  `InvalidDocument`, and `InvalidOperation` is `OperationRejected`.
- **DID resolvers implement `IDidResolver`, and their consumers take it.** `DidResolver`,
  `PlcClient` and `DidWebResolver` resolve with `ResolveAsync(Did)` instead of
  `ResolveDidAsync(string)`; `FirehoseVerifier(IDidResolver)` and
  `SpaceCredentialProvider(…, IDidResolver? didResolver)` replace the `DidResolver` overloads, and the
  parameterless `FirehoseVerifier()` is `FirehoseVerifier(IdentityResolverOptions? options = null)`.
  Pass a `CachingDidResolver`.
- **`PlcClient` takes an explicit directory URL and typed arguments.** `PlcClient(string)` is
  `PlcClient(IdentityResolverOptions?)` and `PlcClient(HttpClient)` is `PlcClient(HttpClient, Uri
  directoryUrl, IdentityResolverOptions?)`; the client's `BaseAddress` is no longer read. Every
  method takes a `Did`, `SubmitOperationAsync` returns one, and `GetPlcDataAsync` returns a
  `PlcOperation`. `PlcData` and `PlcOperationService` are removed in favour of `PlcOperation` and the
  Lexicon `DidService`, and `PlcOperation` and `PlcAuditEntry` are immutable and typed (`Did`, `Cid`;
  `Nullified` is `bool?`).
- **`PlcOperationBuilder` is typed**: `CreateGenesisOperation` takes a `Handle`, and `DeriveDid` and
  `PlcSignedOperation.Did` are `Did`s.

```csharp before
var resolver = new DidResolver();
try { var doc = await resolver.ResolveDidAsync("did:plc:abc123"); }
catch (PlcException ex) when (ex.Kind == PlcErrorKind.NotFound) { }
var plc = new PlcClient(httpClient);   // BaseAddress = https://plc.directory
```

```csharp
using var resolver = new CachingDidResolver();
try
{
    var doc = await resolver.ResolveAsync(Did.Parse("did:plc:abc123"));
    Console.WriteLine(doc.GetPdsEndpoint()?.OriginalString);
}
catch (DidResolutionException ex) when (ex.Kind == DidResolutionErrorKind.NotFound)
{
}

using var plc = new PlcClient(httpClient, new Uri("https://plc.directory"));
```

## OAuth

See [OAuth Authentication](oauth.md).

- **`OAuthClient` takes its HTTP client from the options.** The constructor is
  `OAuthClient(OAuthOptions options, ILogger? logger = null)`, and the `HttpClient` it took is
  `OAuthOptions.HttpClient`: it carries every request to a PDS or an authorization server
  (metadata, pushed authorization, token, refresh, revocation), and when it is `null`, which is
  what you want, they all go through the SDK's fetch policy. Set it only to a client that is already
  safe for URLs taken from DID documents, such as a test stub.
- **OAuth requests follow the identity fetch policy**: HTTPS only, public addresses only (checked
  after DNS), no redirects, bodies capped at 64 KiB, and every failure an `OAuthException`
  (`invalid_server_url`, `metadata_fetch_failed`, `invalid_metadata`, `request_failed`,
  `request_timeout`) rather than an `HttpRequestException` or `JsonException`. For a local PDS set
  `OAuthOptions.AllowPrivateNetworks` (or `AtProtoOAuthServerOptions.AllowPrivateNetworks`).
- **Discovery resolves identities through an `IIdentityResolver`.** `AuthorizationServerDiscovery`
  takes an optional resolver (exposed as `IdentityResolver`), is `IDisposable`, takes a nullable
  `HttpClient` (`null` fetches under the policy) and an `allowPrivateNetworks` flag, and loses
  `ResolveHandleToDidAsync`, `ResolveHandleAuthoritativeAsync`, `ResolvePdsFromDidAsync`,
  `FetchDidDocumentAsync` and `HandleResolutionTimeout`. `OAuthOptions.IdentityResolver` supplies the
  resolver; use `discovery.IdentityResolver.ResolveAsync(AtIdentifier.Parse(handle))`.
- **`StartAuthorizationAsync` returns an `OAuthAuthorizationRequest` and takes
  `OAuthAuthorizationOptions`.** The record carries `AuthorizationUrl` (a `Uri`), `State` and
  `ExpiresAt` instead of a `(string, string)` tuple, and deconstructs; the `pdsUrl` parameter becomes
  `OAuthAuthorizationOptions.ServerUrl`, next to the new `RequesterId`.
- **`OAuthClient.GetPendingAuthorizationState` and `OAuthAuthorizationState` are removed**: they
  exported the PKCE verifier without the DPoP key, so no flow could be resumed from them. Keep
  pending logins across instances or restarts with `OAuthOptions.StateStore`.
- **`OAuthOptions.DefaultPdsUrl` is removed**; nothing read it.
- **`DistributedCacheOAuthStateStore` requires an options instance, and requires it to protect
  entries.** The constructor throws unless both `Protect` and `Unprotect` are set, or the new
  `StoreSecretsUnencrypted` opt-out is: the store holds every pending login's PKCE verifier and DPoP
  private key. Pass an `IDataProtector`'s methods.
- **`AtProtoScopes.Rpc` and `Include` refuse an audience that is not a service**: an `aud` is a DID
  with a service fragment (`did:web:api.bsky.app#bsky_appview`, `AtProtoScopes.BlueskyAppView`), or
  `*` for `rpc`; a bare DID and `include:…?aud=*` throw `ArgumentException`.
- **`AtProtoScopes.PermissionSets` holds only published permission sets.** `DeletePosts`,
  `ManagePosts`, `ManageFollows`, `ManageListsAndPacks`, `ViewNotifications` and
  `ManagePreferences` named sets nobody published and are removed, and `ManageNotifications` is now
  `app.bsky.authManageNotifications` (it was the unpublished `authManageNotifs`). Use `DeleteContent` for deleting posts, `ManageNotifications`
  for notifications, `ManageModeration` for preferences, and `FullApp` for the rest; recompile, as
  the constants are inlined.

```csharp before
var oauth = new OAuthClient(options, httpClient, logger);
var (url, state) = await oauth.StartAuthorizationAsync("alice.example.com", redirectUri, pdsUrl);
return Results.Redirect(url);
```

<!-- snippet: ATProtoNet.Auth.OAuth.OAuthOptions options; string redirectUri, pdsUrl; string? remoteIp; -->
```csharp
using ATProtoNet.Auth.OAuth;

using var oauth = new OAuthClient(options, logger);
var (url, state, _) = await oauth.StartAuthorizationAsync(
    "alice.example.com", redirectUri,
    new OAuthAuthorizationOptions { ServerUrl = pdsUrl, RequesterId = remoteIp });
var redirect = Results.Redirect(url.AbsoluteUri);

var scope = AtProtoScopes.Combine(
    AtProtoScopes.AtProto,
    AtProtoScopes.Rpc("app.bsky.actor.getProfile", AtProtoScopes.BlueskyAppView));
```

## The hosted OAuth login

See [OAuth: Hosted Login](oauth.md#hosted-login-aspnet-core).

- **The OAuth cookie login moves from `ATProtoNet.Blazor` to `ATProtoNet.Server`.**
  `AtProtoOAuthService`, `AtProtoOAuthServerOptions`, the registration and `MapAtProtoOAuth()` live
  in namespace `ATProtoNet.Server.Authentication`. Reference `ATProtoNet.Server` (the Blazor package
  still brings it), replace `using ATProtoNet.Blazor;` and `using ATProtoNet.Blazor.Authentication;`
  with `using ATProtoNet.Server.Authentication;`, and register with `AddAtProto().WithOAuth()`.
- **The client factory, the Blazor widgets and sign-out act only for the OAuth login's user.**
  `IAtProtoClientFactory.CreateClientForUserAsync`, `AtProtoUserClientAccessor` and
  `/atproto/logout` read the DID from an identity the OAuth login issued (authentication type
  `ATProto`) or one carrying `auth_method` = `oauth`, and ignore every other identity, including
  service auth's, which carries the same `did` claim. A principal you build yourself for the factory
  needs the `auth_method` claim (`AtProtoClaimTypes.AuthMethod`) with the value `oauth`; the login's
  own principals, a custom `ClaimsFactory` included, are unaffected.
- **`IOAuthClientProvider` is removed; the client factory takes the `OAuthClient`.**
  `AtProtoClientFactory`'s constructor takes an optional `OAuthClient` and an optional
  `ISessionRefreshCoordinator` in its place, `WithOAuth()` registers the login's client as the
  `OAuthClient` singleton, and `AtProtoOAuthService.TryGetClient()` becomes the `Client` property,
  which is never `null`. With your own OAuth flow, register your `OAuthClient` as a singleton.
- **`AtProtoOAuthService.CompleteCallbackAsync` returns an `AtProtoOAuthCallbackResult`**:
  `RedirectUrl` (the login's local return URL, or the relay URL on its loopback origin), `IsRelay` and
  `Did`, instead of a string the caller had to vet. Redirect to `result.RedirectUrl`.
- **`AtProtoOAuthService`'s constructor takes more optional parameters**: an `IOAuthStateStore`
  (after `identityResolver`), and `IServer` and `ISessionRefreshCoordinator`, all resolved from
  dependency injection. Recompile code that constructs the service.
- **`AtProtoOAuthServerOptions.ClaimsFactory` takes an `OAuthSession`**: read `session.Did.Value` and
  `session.Handle.Value` where `result.Did` and `result.Handle` were read.
- **The claim types have constants**, on `AtProtoClaimTypes` (`ATProtoNet.Server.Authentication`):
  the login's `Did`, `Handle`, `HandleVerified`, `PdsUrl` and `AuthMethod`, and service auth's
  `LexiconMethod` and `Audience`. The claim values (`did`, `handle`, `handle_verified`, `pds_url`,
  `auth_method`) are unchanged; use the constants where you wrote the strings.
- **The development loopback client takes its callback from the server's address**, the plain HTTP
  address on `127.0.0.1` (or `[::1]`), fixed at startup, instead of whatever the first request looked
  like. A server with no plain HTTP loopback address fails the login with an
  `InvalidOperationException`: bind an address such as `http://127.0.0.1:5000`, or set `BaseUrl`.
- **A failed login redirects with an error code, never a message**: the `error` query parameter of
  `LoginPath` is the `OAuthException.Error`, the authorization server's `error`, or `login_failed`.
  A login page of your own reads it as a code.

```csharp before
using ATProtoNet.Blazor;
using ATProtoNet.Blazor.Authentication;

builder.Services.AddAtProtoAuthentication(o => o.ClaimsFactory = r => [new Claim("did", r.Did)]);
```

```csharp
using System.Security.Claims;
using ATProtoNet.Server;
using ATProtoNet.Server.Authentication;

builder.Services.AddAtProto().WithOAuth(o => o.ClaimsFactory = session =>
[
    new Claim(AtProtoClaimTypes.Did, session.Did.Value),
    new Claim(ClaimTypes.Name, session.Handle.Value),
]);
```

### Blazor components

- **The components act as the signed-in user.** `FeedView`, `PostCard`, `ProfileCard` and
  `ComposePost` get the user's client from the scoped `AtProtoUserClientAccessor`, which
  `AddAtProtoBlazor()` registers, instead of injecting an `AtProtoClient` nobody registered. Call
  `builder.Services.AddAtProtoBlazor()` next to `AddAtProto().WithClientFactory()`.
- **`FeedView.FeedSource` is a `FeedSource`**: `FeedSource.Timeline`, `Author(actor)`,
  `Feed(generatorUri)` or `List(listUri)` instead of an `"author:…"` string. Write
  `FeedSource="@FeedSource.Author(AtIdentifier.Parse("alice.bsky.social"))"` for
  `FeedSource="author:alice.bsky.social"`. `ProfileCard.Profile` is no longer `[EditorRequired]`.

See [Blazor](blazor.md).

## Service auth

- **The bearer-JWT `AtProtoAuthenticationHandler` is removed**, with `AtProtoAuthenticationOptions`
  and `AuthenticationBuilder.AddAtProto()`. It had services accept their users' PDS access tokens,
  validating each by calling the PDS with a new client per request. Authenticate calls from other
  services and apps with service auth, and sign users in with the OAuth cookie login. See
  [Serving XRPC to other services](xrpc-handlers.md#serving-xrpc-to-other-services).

```csharp before
builder.Services.AddAuthentication().AddAtProto(o => o.PdsUrl = "https://bsky.social");
```

```csharp
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Xrpc;

builder.Services.AddAuthentication()
    .AddAtProtoServiceAuth(o => o.Audiences.Add("did:web:api.example.com#my_service"));

app.MapXrpcEndpoints().RequireServiceAuth();   // or [RequireServiceAuth] on a handler
```

- **`ServiceAuthGenerator` takes a `Did` and an `Nsid`, and `CreateToken` requires an `lxm`.** The
  constructor's `serviceDid` and `ServiceDid` are a `Did`, and `CreateToken(audience, Nsid lxm,
  expiresIn)` requires the method, as the service auth spec revised in 2026 does. The `audience` must
  be a DID with an optional `#service` fragment, and one whose fragment holds a second `#` or
  whitespace is refused (`ArgumentException`); prefer the `did#serviceId` form. The constructor
  gains an optional `keyId`, sent as the `kid` header.

```csharp before
var generator = new ServiceAuthGenerator("did:web:my-service.example.com", key);
var token = generator.CreateToken("did:web:feed.example.com");
```

<!-- snippet: ATProtoNet.Crypto.AtProtoKey key; -->
```csharp
using ATProtoNet.Auth;

using var generator = new ServiceAuthGenerator(Did.Parse("did:web:my-service.example.com"), key);
var token = generator.CreateToken("did:web:feed.example.com#bsky_fg", Nsid.Parse("app.bsky.feed.getFeedSkeleton"));
```

- **`ISpaceReplayStore` is generalized into `IJtiReplayStore`** (`ATProtoNet.Server.Authentication`),
  the one single-use-token store behind service auth and the space server. `InMemorySpaceReplayStore`
  is `InMemoryJtiReplayStore`, `EfCoreSpaceReplayStore<T>` is `EfCoreJtiReplayStore<T>`,
  `AddAtProtoEfCoreSpaceReplayStore<T>()` is `AddAtProtoEfCoreJtiReplayStore<T>()`, and
  `SpaceReplayEntity` is `JtiReplayEntity` on table `AtProtoJtiReplay` (was `AtProtoSpaceReplay`),
  configured by `JtiReplayDbContext.ConfigureJtiReplayModel` (was
  `SpaceDbContext.ConfigureSpaceReplayModel`; `SpaceDbContext.AtProtoSpaceReplay` is now
  `AtProtoJtiReplay`). Add an EF migration for the renamed table; its rows live only as long as the
  tokens they guard, so dropping the old table loses nothing (see
  [Upgrading the replay table](spaces.md#upgrading-the-replay-table-from-06)).
- **`VerifiedServiceAuth` moves to `ATProtoNet.Server.Authentication`** and becomes a class with
  init-only properties rather than a positional record. It reports the token's own `lxm` as
  `Method`, and gains `KeyId`, `TokenId`, `IssuedAt` and `ExpiresAt`: read properties instead of
  deconstructing.

## Streaming

See [Firehose Streaming](firehose.md) and [Jetstream Streaming](jetstream.md).

- **Firehose construction leaves `AtProtoClient`.** `CreateFirehoseClient`,
  `CreateFirehoseConsumer`, `AtProtoClientOptions.RelayUrl`, `AtProtoClientBuilder.WithRelayUrl` and
  the Aspire `AtProtoClientSettings.RelayUrl` are removed; a relay subscription needs no user session.
  Construct `new FirehoseClient("wss://bsky.network", logger)`, or a `TypedFirehoseConsumer`.
- **`FirehoseConsumer` and `FirehoseFrame` are removed; `TypedFirehoseConsumer` reconnects itself.**
  It parses each frame once, and is no longer `IDisposable`. `FirehoseClient.SubscribeAsync` yields
  parsed `FirehoseMessage`s and `SubscribeLabelsAsync` yields `LabelStreamMessage`s; `FirehoseClient`
  and `JetstreamClient` are `IAsyncDisposable` instead of `IDisposable`, and `DisconnectAsync` is
  removed (disposing, or cancelling, ends the subscription). `await using` the clients, and drop
  `Dispose` calls on the consumers.
- **Stream consumers share `StreamConsumerOptions` and a `StreamReconnectPolicy`.**
  `TypedFirehoseConsumerOptions` and `JetstreamConsumerOptions` derive from `StreamConsumerOptions`
  (`ServiceUrl`, `CursorStore`, `StreamId`, `CursorPersistInterval`, `Reconnect`, `Logger`,
  `OnStreamError`, `OnEventDropped`). `ReconnectDelay` and `MaxReconnectAttempts` (with `-1` for
  unlimited) are replaced by `Reconnect`: exponential backoff from `InitialDelay` (5 s) to `MaxDelay`
  (30 s) for at most `MaxAttempts` (10, `null` for unlimited) consecutive failures.
- **A consumer whose reconnect attempts run out throws** an `EventStreamException` whose
  `InnerException` is the last failure, instead of completing as though the stream had ended.
  Cancelling the token ends every consumer's enumeration normally, `JetstreamReplayConsumer`
  included (it threw `OperationCanceledException` during a backfill). Catch `EventStreamException`,
  or set `MaxAttempts = null` to reconnect forever.
- **`IFirehoseCursorStore` is `IStreamCursorStore`, with `ValueTask` methods** (`GetCursorAsync`,
  `StoreCursorAsync`), shared by the firehose, label and Jetstream consumers;
  `InMemoryFirehoseCursorStore` is `InMemoryStreamCursorStore`.
- **`TypedFirehoseConsumerOptions.VerifySignatures` is removed, and `CollectionFilter` is an
  `IReadOnlySet<Nsid>`.** A consumer verifies signatures exactly when it has a `Verifier`.
- **Firehose messages follow the current `subscribeRepos` Lexicon.** `HandleEvent` and
  `TombstoneEvent`, removed upstream, are gone: handle `#identity` and `#account` instead. `Seq` and
  `Time` move from `FirehoseMessage` to a new `FirehoseEvent` base of `CommitEvent`, `SyncEvent`,
  `IdentityEvent` and `AccountEvent`, since `InfoEvent` has neither (`message is FirehoseEvent e ?
  e.Seq : null`). `CommitEvent.TooBig`, `Rebase` and `Blobs` are `[Obsolete]`, and `LabelsEvent`
  derives from the new `LabelStreamMessage`.
- **The models are typed.** `CommitEvent.Repo` / `Commit` / `Rev` / `Since` / `PrevData` / `Blobs` are
  `Did` / `Cid` / `Tid` / `Tid?` / `Cid?` / `IReadOnlyList<Cid>?`, `RepoOp.Cid` / `Prev` are `Cid?`, and
  `RepoOp.Action` is the `RepoOpAction` enum (compare with `RepoOpAction.Create`, not `"create"`); the
  account and identity events carry a `Did` and `Handle`, and `FirehoseMessage.Time` is an
  `AtDatetime?`.
- **`FirehoseEventParser` keeps one entry point**, `Parse(ReadOnlyMemory<byte>)`; `Parse(FirehoseFrame)`
  and `TryParse` are removed, and an error frame throws `EventStreamException` instead of returning
  `null`.

```csharp before
using var consumer = client.CreateFirehoseConsumer(new TypedFirehoseConsumerOptions
{
    VerifySignatures = true,
    CollectionFilter = ["app.bsky.feed.post"],
    ReconnectDelay = TimeSpan.FromSeconds(5),
    MaxReconnectAttempts = -1,
    CursorStore = new InMemoryFirehoseCursorStore(),
});
```

```csharp
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Streaming;

var consumer = new TypedFirehoseConsumer(new TypedFirehoseConsumerOptions
{
    ServiceUrl = "wss://bsky.network",
    Verifier = new FirehoseVerifier(),   // verifies signatures and CIDs
    CollectionFilter = new HashSet<Nsid> { Nsid.Parse("app.bsky.feed.post") },
    Reconnect = new StreamReconnectPolicy { InitialDelay = TimeSpan.FromSeconds(5), MaxAttempts = null },
    CursorStore = new InMemoryStreamCursorStore(),
});

try
{
    await foreach (var message in consumer.ConsumeAsync(cancellationToken: stoppingToken))
    {
        if (message is CommitEvent commit)
            foreach (var op in commit.Ops ?? [])
                if (op.Action == RepoOpAction.Create)
                    Console.WriteLine($"{commit.Repo} created {op.Path}");
    }
}
catch (EventStreamException ex)
{
    logger.LogError(ex, "The firehose gave up after its reconnect attempts");
}
```

### Jetstream

- **Jetstream uses `RepoOpAction`.** `JetstreamOperation` is removed, and
  `JetstreamCommitEvent.Operation` is the firehose's `RepoOpAction`
  (`ATProtoNet.Lexicon.Com.AtProto.Sync`), with the same three members.
- **The models are typed.** `JetstreamCommitEvent.Collection` is an `Nsid`, `RKey` is renamed `Rkey`
  and typed `RecordKey` (`JetstreamArchiveRow.RKey` is `Rkey` too, and keeps the raw column text),
  `Rev` is a `Tid?`, the identity event's `Handle` a `Handle?`, the events' `Time` an `AtDatetime?`,
  and `JetstreamConsumerOptions.WantedDids` and `JetstreamSnapshotRequest.Dids` are
  `IReadOnlyList<Did>` (`WantedDids = [Did.Parse(…)]`). Collection filters stay strings, since they
  accept `app.bsky.graph.*` wildcards. `JetstreamSegmentPage` implements `ICursorPage<T>`.
- **One `JetstreamException`** replaces `JetstreamConnectException` and
  `JetstreamArchiveException`, with `StatusCode`, `Error`, `RetryAfter` and `IsRetryable`, and derives
  from the new `EventStreamException`. `JetstreamStreamError` is replaced by `EventStreamError`, so
  `OnStreamError` is an `Action<EventStreamError>` and `JetstreamFrame.Error` an
  `EventStreamError?`; `JetstreamClient` throws on an error frame instead of ending normally.
- **`JetstreamDictionaryClient` is folded into `JetstreamArchiveClient`**, as
  `GetZstdDictionaryAsync(id)`, which sends no API key.
- **`JetstreamEventParser` keeps `ParseFrame(ReadOnlyMemory<byte>, JetstreamProtocol)`**; the `Parse`
  overloads and the `string` and `ReadOnlySpan<byte>` `ParseFrame` overloads are removed.
- **Unused Jetstream members are removed**: `JetstreamPlanStats` and `JetstreamSnapshotPlan.Stats`,
  `JetstreamConsumer.IsConnected` and its no-op `Dispose` (it is no longer `IDisposable`), and
  `JetstreamReplayConsumer.PinnedTipSeq`; `JetstreamSegmentReader.DecodeBlock` is internal (decode with
  `DecodeBlockFrame`).

<!-- snippet: string json; -->
```csharp
using System.Text;

var parsed = JetstreamEventParser.ParseFrame(Encoding.UTF8.GetBytes(json), JetstreamProtocol.V2).Event;
if (parsed is JetstreamCommitEvent { Operation: RepoOpAction.Delete } deleted)
    Console.WriteLine($"{deleted.Collection}/{deleted.Rkey} deleted");

using var archive = new JetstreamArchiveClient(JetstreamEndpoints.UsEast);
var dictionary = await archive.GetZstdDictionaryAsync();
```

## Repositories

See [Low-Level Repo API](low-level-repo.md#repository-data-structures).

- **`MerkleSearchTree` only accepts valid MST keys, and `Create` rejects duplicates.** `Add` and
  `Create(entries)` throw `ArgumentException` for a key that is not `collection/rkey` (two non-empty
  segments of `A-Z a-z 0-9 _ ~ - : .`, at most 1024 characters), and `Create` throws for a repeated
  key instead of building a corrupt tree.
- **`MerkleSearchTree.CreateFromEntries` is removed**: call `MerkleSearchTree.Create(entries)`.
- **`MstNodeData`, `MstTreeEntry` and `MstKeyDepth` are internal**: build, read and serialize trees
  through `MerkleSearchTree`.
- **`DagCborDecoder.Decode` throws `FormatException` for all malformed input** (it threw
  `InvalidOperationException` or leaked `CborContentException`), and `MerkleSearchTree.Deserialize`
  throws `FormatException` for a tree that is too deep.
- **`DagCborDecoder.DecodeToNode`, `DagCborEncoder.ComputeCid` and `CarBlock.DataLength` are
  removed**: use `JsonSerializer.SerializeToNode(DagCborDecoder.Decode(bytes))`,
  `CidComputation.ComputeForDagCbor(bytes)` and `block.Data.Length`.

<!-- snippet: byte[] bytes; -->
```csharp
using ATProtoNet.Repo;

try
{
    var node = JsonSerializer.SerializeToNode(DagCborDecoder.Decode(bytes));
    var cid = CidComputation.ComputeForDagCbor(bytes);
}
catch (FormatException ex)
{
    logger.LogWarning(ex, "Not DAG-CBOR");
}
```

## Spaces

See [Spaces](spaces.md).

### Reading and writing

- **`simplespace` separates read and write policies, and `putMember` replaces `addMember`**,
  following upstream's read/write split: against the spaces-alpha PDS, 0.6 got a 400 from
  `createSpace`, threw on `getSpace`, and had an `updateSpace` policy change silently ignored. Pass
  `readPolicy:` and `writePolicy:` instead of `policy:` (the same value to both keeps the old
  behaviour), and replace `AddMemberAsync(space, did)` with `PutMemberAsync(space, did, read: true,
  write: true)`. The wire models follow: `ReadPolicy` / `WritePolicy` replace `Policy`,
  `PutSimpleSpaceMemberRequest` replaces `AddSimpleSpaceMemberRequest`, and `SimpleSpaceMember` gains
  required `Read` / `Write`.
- **`SimpleSpaceClient.CheckUserAccessAsync` takes a required `access`** before `clientId`:
  `CheckUserAccessAsync(space, user, SimpleSpaceAccess.Read, clientId)`. A positional `clientId`
  argument moves.
- **Spaces are typed.** A space is a `SpaceUri`, a record in one a `SpaceRecordUri`, a participant a
  `Did`, and collections, record keys, revisions and CIDs are `Nsid`, `RecordKey`, `Tid` and `Cid` —
  in the client methods, the models, the `SpaceUri` / `SpaceRecordUri` components,
  `SpaceCommitContext`, `SpaceRepoOp`, `SpaceRepoRecord`, `SpaceRepoCommit`, `SpaceRepoCar.Verify`,
  `SpaceSyncer`, `SpaceRepoCursor`, `ISpaceRepoStore`, `SpaceCredentialProvider` and its
  `HostResolver` option. `RegisterNotifyResponse.ExpiresAt` is an `AtDatetime`. The `string` space
  overloads, `ToSpaceUri()` on `SpaceView` / `CreateSimpleSpaceResponse` and
  `SpaceWriteResult.ToRecordUri()` are gone: read `.Uri`.
- **The Space and SimpleSpace models follow the open-union pattern.** `SimpleSpaceUserPolicy` and
  `SimpleSpaceAppAccess` are open `[AtProtoUnion]`s, so a variant from a newer schema reads as
  `UnknownSimpleSpaceUserPolicy` / `UnknownSimpleSpaceAppAccess`; `SpaceWriteOp` is a closed
  `[AtProtoUnion]`; and the policies, `SimpleSpaceMember`, `SpaceView`, `SpaceRepoView`,
  `SpaceRecordView`, `SpaceRepoOpEntry`, `SpaceWriteOpResult` and `SignedSpaceCommit` derive from
  `LexObject`. Add an `Unknown…` (or `_`) arm to a `switch` over a policy. A host answers
  `UnsupportedPolicy` / `UnsupportedAppAccess` to an unknown variant on `createSpace` /
  `updateSpace`.
- **`SpaceSyncer` takes an `IDidResolver`** instead of a signing-key delegate
  (`new SpaceSyncer(space, store, new CachingDidResolver())`), and `SpaceSyncer.ResolveSigningKeyAsync`
  is removed; `SpaceAuthority.GetHostEndpoint` and `GetServiceEndpoint` return `Uri?`.

```csharp before
var created = await client.SimpleSpace.CreateSpaceAsync("com.example.forum", policy: new MemberListPolicy());
await client.SimpleSpace.AddMemberAsync(created.ToSpaceUri(), "did:plc:member");
```

```csharp
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;

var created = await client.SimpleSpace.CreateSpaceAsync(
    Nsid.Parse("com.example.forum"), readPolicy: new MemberListPolicy(), writePolicy: new MemberListPolicy());
await client.SimpleSpace.PutMemberAsync(created.Uri, Did.Parse("did:plc:member"), read: true, write: true);

var access = await client.SimpleSpace.CheckUserAccessAsync(
    created.Uri, Did.Parse("did:plc:member"), SimpleSpaceAccess.Read);
```

### Serving a space

- **The `simplespace` store and record carry both policies and per-member access.**
  `SimpleSpaceRecord` takes `ReadPolicy` and `WritePolicy` in place of `Policy`;
  `ISimpleSpaceStore.AddMemberAsync` / `IsMemberAsync` become `PutMemberAsync(space, did, read,
  write)` / `GetMemberAsync`; `SpaceNsids.AddSimpleSpaceMember` becomes
  `SpaceNsids.PutSimpleSpaceMember`, and `AddSimpleSpaceMemberEndpoint` is gone with the other
  public handlers (below). A custom store implements the two new members and keeps both flags.
- **The space server is typed** like the client: `ISpaceRepoHost`, `ISpaceAuthorityStore`,
  `ISimpleSpaceStore`, `SimpleSpaceRecord.Owner`, `SpaceAccessRequest`, the verified-token records,
  `ISpaceCallerResolver`, `SpaceServiceAuthVerifier` (`expectedMethod` is an `Nsid`),
  `SpaceWriteNotifier` and `SpaceServerOptions.ServiceDid` (`options.ServiceDid = Did.Parse(…)`) take
  and return `Did`, `SpaceUri`, `Nsid`, `RecordKey`, `Tid` and `Cid`.
- **The EF Core `simplespace` schema changes**: `AtProtoSimpleSpaces.Policy` becomes `ReadPolicy` and
  `WritePolicy`, and `AtProtoSimpleSpaceMembers` gains `Read` and `Write`. Add a migration that copies
  `Policy` into both columns before dropping it and defaults the member columns to `true`, as
  upstream's `003-space-access` does; see
  [Upgrading a simplespace database](spaces.md#upgrading-a-simplespace-database-from-06). (The other
  space entities are unchanged: the stores convert typed values at the boundary.)
- **`ISpaceAccessPolicy` also decides writes.** `SpaceAccessRequest` gains `Access`
  (`SpaceAccessKind.Read`, the default, or `Write`), and `notifyWrite` asks the policy with `Write`
  and no attested client. `ISimpleSpaceManagingAppClient.CheckUserAccessAsync` takes the
  `SpaceAccessKind` too. A custom policy must handle `Write` without applying an app perimeter, or it
  refuses every writer with 403.
- **The space server resolves DIDs through an `IDidResolver`.** `ISpaceDidDocumentResolver`,
  `CachingSpaceDidDocumentResolver`, `SpaceDidDocumentResolverExtensions` and
  `SpaceServerOptions.DidDocumentCacheLifetime` are removed. The verifiers, `SpaceWriteNotifier` and
  `SimpleSpaceManagingAppClient` take an `IDidResolver`, which `AddAtProtoSpaces` registers under
  `SpaceServerExtensions.DidResolverKey` as a `CachingDidResolver` configured by
  `SpaceServerOptions.DidCache`. Register a custom one with
  `AddKeyedSingleton<IDidResolver>(SpaceServerExtensions.DidResolverKey, …)`, and set
  `options.DidCache.StaleAfter` where `DidDocumentCacheLifetime` was set.
- **The endpoint handlers and query-parameter types are internal**: the nineteen handlers
  (`GetSpaceCredentialEndpoint` … `ListSimpleSpaceMembersEndpoint`), `NotifyWriteEndpoint` among them,
  their bases `SpaceRepoEndpointBase<T>` and `SimpleSpaceEndpointBase`, and `SpaceRepoParameters` with
  the `*Parameters` types they bind. `AddSpaceAuthority`, `AddSpaceRepoHost` and `AddSimpleSpace`
  register them as before; code that constructed or subclassed one implements its own
  `IXrpcEndpoint`.
- **`ISpaceServiceAuthVerifier` is removed**; resolve `SpaceServiceAuthVerifier` directly.
- **`notifyWrite` must be signed by its writer, and more strictly.** As in the reference authority,
  the service auth's `iss` must be the account whose repo advanced, so
  `SpaceServiceAuthVerifier.IsRepoHostAsync` is removed. A token without a `jti`, `lxm` or `iat`, one
  whose `iss` carries a fragment, and one naming a `kid` other than `#atproto` are refused. A repo host
  signs its notifications as the writer through `ISpaceAccountSigner`.
- **Verified space-token records carry only what they establish**:
  `VerifiedDelegationToken(Space, UserDid)`, `VerifiedSpaceCredential(Space, Proof)` and
  `VerifiedClientAttestation(ClientId)` drop their unused `Token` (and `AuthorityDid`), and
  `SpaceCredentialRequestAuth.Space`, `DPoPProof.AccessTokenHash` / `Nonce` and
  `SpaceRequestAuthenticator.BuildRequestUri` are removed. Use `auth.Delegation.Space`,
  `credential.Space.Authority` and `SpaceServerOptions.BuildRequestUri`.
- **Space server constructors take new optional parameters.** `AddSpaceAuthority<T>(signingKey,
  serviceAuthKey?)` gains the service's `#atproto` key; `SpaceCredentialVerifier(resolver,
  proofValidator, options?, timeProvider?)` gains `options` before `timeProvider`;
  `SpaceWriteNotifier` and `SimpleSpaceManagingAppClient` gain an `ISpaceAccountSigner? accountSigner`
  before their logger; `SpaceCredentialIssuer` drops `ownsKey` and `IDisposable`. Pass
  `timeProvider:` / `logger:` by name, and dispose an issuer's key yourself.
- **Space tokens must name their key.** A delegation token's `kid` must be `#atproto`, and a
  credential's `#atproto_space` or `#atproto`; only the named key is tried. An authority signing with
  a dedicated `#atproto_space` key sets `SpaceServerOptions.CredentialKeyId = "#atproto_space"`.
- **The EF Core space and replay stores no longer run on the EF Core in-memory provider**: they write
  with `ExecuteUpdate` / `ExecuteDelete`, which `Microsoft.EntityFrameworkCore.InMemory` does not
  translate. Tests use SQLite in memory: `UseSqlite` over a `SqliteConnection` to
  `Data Source=<name>;Mode=Memory;Cache=Shared`, held open for the test.
- The replay store rename is under [Service auth](#service-auth).

## Lexicon tooling

See [Lexicon Code Generator](lexicon-codegen.md).

- **`atproto-lexgen csharp` emits typed identifiers and `IReadOnlyList<T>`**: `did`, `handle`,
  `at-identifier`, `at-uri`, `nsid`, `cid`, `record-key`, `tid` and `datetime` strings become the
  SDK's types (with `using ATProtoNet.Identity;`), and arrays `IReadOnlyList<T>`; `atproto-lexgen
  lexicon` maps them back. Regenerate, then parse literals.
- **`atproto-lexgen publish` publishes to a PDS; the old directory `publish` and `migrate` are
  removed.** `publish` writes each schema as a `com.atproto.lexicon.schema` record (record key = NSID)
  to the signed-in account's repository and checks the `_lexicon` DNS records; its `--output`,
  `--baseline`, `--assembly` and `--no-bump` options are gone. `migrate` is removed, with the
  `ATProtoNet.LexiconGenerator.Migrations` types (`LexiconPublisher`, `PublishResult`,
  `MigrationBuilder`, `LexiconMigrationRunner`, `ILexiconMigration`, …): the Lexicon evolution rules
  (new fields optional, nothing removed, renamed or retyped, breaking changes under a new NSID) leave
  no record migration to run. Keep published copies under version control, gate changes with
  `atproto-lexgen diff --strict`, and publish with
  `atproto-lexgen publish --input <dir> --identifier <handle>` and `ATPROTO_PASSWORD`.
- **The `migrate` and old-`publish`-option messages explaining the removal above are themselves
  removed**, now that the removal has shipped: `atproto-lexgen migrate` and
  `publish --output`/`--baseline`/`--assembly`/`--no-bump` fail with the same generic "Unknown
  command"/"Unknown option" every other unrecognized one does, instead of the specific explanation.
  Run with `--help` for the current commands and options.
- **Unused `atproto-lexgen` helpers are removed**: `CSharpEmitter.Emit(LexiconDocument)` (use
  `EmitAll([document])`), `LexiconEmitter.JsonOptions` (use `LexiconJson.WriteOptions`), and
  `TypeMapper.GetLexiconType`, `InferStringFormat`, `ToCamelCase`, `NsidToFilePath` and
  `ParseTypeValue` (use `TypeMapper.SplitRef`).

## Aspire

See [.NET Aspire](aspire.md) and [Managed PDS](managed-pds.md).

- **`WithHostname` and `WithJwtSecret` are generic over the PDS resource type**: the reference-PDS
  and Tranquil overloads are replaced by one generic method each on `AtProtoPdsHostingExtensions`,
  constrained to the new `AtProtoPdsContainerResourceBase`. Extension-method calls compile unchanged
  and still return the concrete builder. Rewrite an explicit static call such as
  `AtProtoTranquilPdsHostingExtensions.WithHostname(pds, …)` as `pds.WithHostname(…)`.
- **`AddAtProtoClient` returns the `IAtProtoBuilder`** and takes `configure:` for
  `AtProtoClientOptions` (see [Dependency injection](#dependency-injection)).
- **The Aspire client adds no resilience handler**, and `Microsoft.Extensions.Http.Resilience` is no
  longer a dependency: a retrying handler below the SDK resends non-idempotent `POST`s (a
  `createRecord` twice) and DPoP proofs the server refuses the second time, while the SDK already
  retries `429` itself and signs every attempt afresh. Under Aspire service defaults, which add a
  retrying handler to every client, remove it from the SDK's clients with
  `RemoveAllResilienceHandlers()` (see [Aspire: Resilience](aspire.md#resilience)); for transient
  server errors, retry above the SDK.
- **The named `HttpClient` no longer sets `User-Agent: ATProtoNet.Aspire/1.0`**, and the OAuth
  service no longer adds one to a caller-supplied `HttpClient`: the client sets
  `AtProtoClientOptions.UserAgent` per request.

## Behaviour changes

These compile unchanged and behave differently:

- **Identity fetches refuse private networks.** `did:web` documents, PLC lookups, handle
  well-knowns, DNS-over-HTTPS and OAuth's discovery, token and revocation requests are HTTPS-only,
  reach public addresses only (checked after DNS), follow no redirects and read bounded bodies. A
  local PDS or PLC needs `IdentityResolverOptions.AllowPrivateNetworks`,
  `OAuthOptions.AllowPrivateNetworks` or `AtProtoOAuthServerOptions.AllowPrivateNetworks`. See
  [The fetch policy](did-resolution.md#the-fetch-policy-ssrf).
- **Rate-limit waits are capped.** A 429 asking for longer than `RateLimit.MaxDelay` (30 s) throws
  `XrpcRateLimitException` at once, where a daily window used to hold the call for hours.
- **Handle resolution fails closed on a conflict** (DNS naming two DIDs, or DNS and HTTPS
  disagreeing), and OAuth login no longer asks the `bsky.social` AppView when a handle's own
  authorities do not answer.
- **An OAuth session's `ServiceEndpoint` is the account's PDS**, also for a login started at an
  entryway, and a refresh moves the session when the DID document names a new PDS.
- **`DisposeAsync` is the primary way to dispose `AtProtoClient`**: it waits for a token exchange
  under way to finish and be stored. `Dispose` returns at once.
- **A v2 `JetstreamConsumer` skips the replay of its start cursor**, which the server replays
  inclusively, and **`#sync` events are CID-checked whenever a `Verifier` is set**.
- **Unmatched `/xrpc/{nsid}` requests answer with an XRPC error**: `501 MethodNotImplemented` for an
  NSID no handler serves, where 0.6 answered an empty 404.
- **A `did:web` document must name exactly the DID it was fetched for** (the comparison was
  case-insensitive), and **identifiers no longer parse with a trailing line break**.

The changelog's `Changed`, `Fixed` and `Security` sections list the rest.

## The record type on this page

`TodoItem` is a custom record type (see [Custom Lexicon Records](custom-records.md)):

```csharp
public class TodoItem : AtProtoRecord, IAtProtoRecord
{
    public static Nsid Collection { get; } = Nsid.Parse("com.example.todo.item");
    public override string Type => Collection;

    [JsonPropertyName("title")] public string Title { get; set; } = "";
}
```
