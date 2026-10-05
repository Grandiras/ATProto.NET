# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Handle answers are cached** — `HandleResolver` remembers a handle's DID for `IdentityResolverOptions.HandleCacheTtl` (5 minutes; zero disables), so re-verifying a DID's handle no longer costs a lookup each time. `ResolveUncachedAsync` still looks it up afresh (#197)

### Changed

- **Leaner Lexicon clients** — the generated-style XRPC clients, the PDS admin client, the Aspire hosting extensions and the Lexicon tool build their requests as single expressions, with no change to the public API or behaviour. About 650 fewer lines (#196)
- **Leaner auth and server layers** — Braces, forwarding fields and form-building callbacks are gone from the OAuth client, session manager and ASP.NET Core integration; behavior and public API are unchanged (#204)
- **Identity fetches use HTTP/2** — DID document, PLC, well-known and DNS-over-HTTPS requests ask for HTTP/2 and fall back to 1.1, so concurrent lookups against one host share a connection; a burst of PLC lookups resolves about 40% faster (#197)
- **Leaner request path** — XRPC calls offer HTTP/2 (falling back to 1.1), send JSON bodies with a `Content-Length` instead of chunked, and allocate less per DPoP-signed request (#194)
- **Allocation-free DID document lookups** — `GetPdsEndpoint`, `GetSigningKey`, `GetServiceEndpoint` and `TryGetVerificationKey` parse and decode an entry once per document instead of on every call, and `GetHandle` no longer allocates an enumerator (#200)
- **Firehose, label and archive decoding allocate far less** — frame and record transcoding reuses its buffer and builds no string per key or value, so a commit frame allocates about a third of what it did; Spaces record checks shed their copies too (#195)
- **Leaner Spaces server, EF Core stores and streaming consumers** — the EF Core stores share one context-per-operation helper, and repeated checks, braces and single-use plumbing are gone, with no change to the public API or behaviour (#207)

## [0.7.0] - 2026-09-28

A large, deliberately breaking release. Identifiers are typed everywhere; sessions, session stores and errors each have one model; Lexicon unions are open and keep data the SDK does not model; the stream consumers share one core with Sync 1.1 verification; the OAuth client is hardened and its hosted login moved into `ATProtoNet.Server`; and inbound service auth replaces the bearer handler. The SDK also catches up with the protocol: the Spaces read/write split, group chats, Ozone reports and queues, label signing, Lexicon resolution and the current `app.bsky` surface.

**Upgrading from 0.6?** Start with the [migration guide](https://git.grandiras.net/Grandiras/ATProto.NET/src/tag/v0.7.0/docs/migrating-to-0.7.md) (`docs/migrating-to-0.7.md`), which walks through every breaking change below with before-and-after code.

### Breaking changes

- **`ATProtoNet` no longer depends on `Microsoft.Extensions.Http` or `Microsoft.Extensions.Options`** — ASP.NET Core apps are unaffected. Migration: an app outside ASP.NET Core that used `IHttpClientFactory`, `IOptions<T>` or `LoggerFactory` only through the SDK references those packages itself (#110)
- **The EF Core stores are their own package again: `ATProtoNet.Server.EntityFrameworkCore`** — the session, replay, space and Sync 1.1 stores leave `ATProtoNet.Server`; namespace, types and tables are unchanged. Migration: reference the package wherever one of them is used (#136)
- **The OAuth cookie login moves from `ATProtoNet.Blazor` to `ATProtoNet.Server`** — so any ASP.NET Core app can use it; the Blazor package still brings it along. Migration: `using ATProtoNet.Server.Authentication;` for the `ATProtoNet.Blazor` usings, and `AddAtProto().WithOAuth()` (#136)
- **`AddAtProto()` returns an `IAtProtoBuilder`** — `WithLifetime`, `WithOAuth`, `WithClientFactory`, `With…SessionStore` and `WithHealthCheck` replace `AddAtProtoScoped`, `AddAtProtoAuthentication`, `AddAtProtoServer`, `AddAtProto<TStore>` and `AddAtProtoEfCoreTokenStore`. Migration: follow the guide's table (#136)
- **The client factory's default session store is in memory, with a startup warning** — `AddAtProtoServer()` wrote tokens to disk unasked. Migration: `WithFileSessionStore()` for the old behaviour, `WithEfCoreSessionStore<TContext>()`, `WithSessionStore<T>()`, or `WithInMemorySessionStore()` (#136)
- **One named `HttpClient` per registration** — every `AtProtoClient` of `AddAtProto()` sends with `"ATProtoNet"`, exposed as `IAtProtoBuilder.HttpClient`, and `ServiceCollectionExtensions` is `AtProtoServiceCollectionExtensions`. Migration: customize `AddAtProto().HttpClient` (#136)
- **Every options class goes through `IOptions<T>` and is validated at startup** — a bad value fails `StartAsync` with an `OptionsValidationException`, code callbacks run after configuration binding, and every `AddAtProtoIdentity` / `AddAtProtoSpaces` callback applies. Migration: catch that exception (#136)
- **`services.AddAtProtoPdsAdmin(PdsAdminOptions)` is removed** — it duplicated the other overloads. Migration: `services.AddAtProtoPdsAdmin(url, adminPassword)`, plus `services.Configure<PdsAdminOptions>(…)` for the other settings (#180)
- **Aspire: `AddAtProtoClient` returns the `IAtProtoBuilder`, and `AtProtoClientSettings` is gone** — its keys bind to `AtProtoClientOptions`, and `DisableResilience` has nothing left to disable. Migration: `builder.AddAtProtoClient(configure: o => …)` for `configureSettings: s => …` (#136)
- **Aspire hosting: the settings both PDS servers share are generic** — one `AtProtoPdsHostingExtensions` method each (`WithHostname`, …), on `AtProtoPdsContainerResourceBase`, replaces the per-server overloads. Migration: recompile; write an explicit static call as `pds.WithHostname(…)` (#116, #180)
- **`AtProtoClientBuilder` is removed: one constructor builds the client** — `new AtProtoClient(options, httpClient, sessionStore, logger)`, every argument optional. Migration: `With…` settings become options or constructor arguments; `WithLoggerFactory(f)` is `logger: f.CreateLogger<AtProtoClient>()` (#122)
- **`AtProtoClient.PdsUrl` / `SetPdsUrl(string)` are `ServiceUrl` / `SetServiceUrl(Uri)`** — the client holds its service URL instead of writing `HttpClient.BaseAddress`. Migration: `client.SetServiceUrl(new Uri(url))`; `ServiceUrl` is a `Uri` with a trailing slash (#117)
- **A supplied `HttpClient`'s `BaseAddress` is ignored** — `AtProtoClient` starts at `AtProtoClientOptions.InstanceUrl`, and `PdsAdminClient` sends to `PdsAdminOptions.Url`. Migration: put the service URL in the options (#117)
- **`XrpcClient` is internal** — `MaxRateLimitRetries` moves to `AtProtoClientOptions.RateLimit.MaxRetries`, and `BaseUrl`, `IsAuthenticated`, `UpdateDPoPNonce` and the body-less `QueryAsync` are gone. Migration: call custom methods through `client.QueryAsync` / `ProcedureAsync` or `client.Transport` (#117)
- **`SetLabelers` is one `params IEnumerable<string>` method** — Migration: calls compile unchanged; recompile (#122)
- **`AtProtoClientOptions.OAuth` and `OAuthOptions.DefaultPdsUrl` are removed** — nothing read either. Migration: delete the assignments; OAuth is configured on the `OAuthClient` (#132)
- **Identifier parsing is strict** — `Parse` throws and `TryParse` returns `false` for every value the specs and the `atproto-interop-tests` fixtures reject, including any `Cid` but a base32 CIDv1 (DAG-CBOR or raw, SHA-256), and JSON reads fail alike. Migration: read legacy data with `TryParse` (#114)
- **Identifier types are `sealed record`s** — `Did`, `Handle`, `AtIdentifier`, `Nsid`, `Tid`, `RecordKey`, `Cid`, `AtUri`, `SpaceUri` and `SpaceRecordUri` keep their source shape, so this is a binary break only. Migration: recompile (#114)
- **`AtUri` components are typed** — `Collection` is `Nsid?`, `RecordKey` is `RecordKey?`, and `AtUri.Create` takes `(AtIdentifier, Nsid?, RecordKey?)`. Migration: `AtUri.Create(repo, Nsid.Parse(collection), RecordKey.Parse(rkey))`; `uri.Collection?.Value` where a string was used (#114)
- **Typed identifiers across `com.atproto.*`, the client, `RecordCollection<T>` and `PdsAdminClient`** — `Did`, `Handle`, `AtUri`, `Nsid`, `Cid`, `RecordKey`, `Tid` and the rest wherever the Lexicon says so. Migration: pass returned values through; parse literals where they enter your code (#119)
- **Typed identifiers in `app.bsky.*`** — its models and clients use the identifier types and `AtDatetime`, and `RichTextBuilder.Mention` takes `(Handle, Did)`. Migration: pass returned values through, and parse literals (`AtIdentifier.Parse("alice.bsky.social")`) (#119)
- **Typed identifiers in `chat.bsky.*`, `tools.ozone.*` and `site.standard.*`** — `StandardSiteClient` also takes an `AtIdentifier` and a `RecordKey`, and its listings return a typed `RecordPage<T>`. Migration: parse literals; read `entry.Value` directly instead of deserializing it (#119)
- **Typed identifiers across Spaces and the space server** — a space is a `SpaceUri`, a record in one a `SpaceRecordUri`, a participant a `Did`; the `string` overloads and `ToSpaceUri()` / `ToRecordUri()` are gone. Migration: read `.Uri`; parse literals once (`options.ServiceDid = Did.Parse(…)`) (#119)
- **Typed identifiers in the firehose and Jetstream models** — events carry `Did`, `Cid`, `Tid`, `Nsid`, `RecordKey` and `AtDatetime`, `RepoOp.Action` is the `RepoOpAction` enum, and `RKey` is renamed `Rkey`. Migration: compare with `RepoOpAction.Create`, not `"create"`; `WantedDids = [Did.Parse(…)]` (#119)
- **Lexicon `datetime` fields are `AtDatetime`** — `AtProtoRecord.CreatedAt` and every other `datetime` field of the typed models. Migration: set with `AtDatetime.Now()`, `Parse` or `FromDateTimeOffset`; read `.Value`, `TryGetValue` or `ToString()` (#119)
- **`AtProtoJsonDefaults.FormatTimestamp` and `NowTimestamp` are removed** — `AtDatetime` writes the same text. Migration: `AtDatetime.FromDateTime(value).ToString()` and `AtDatetime.Now().ToString()` (#180)
- **Parameters follow one order: subject, required inputs, filters, `limit`, `cursor`** — `ListRecordsAsync`, `RecordCollection<T>.ListAsync`, `CreateReportAsync`, `GetTimelineAsync`, `ListNotificationsAsync`, the Spaces listings and others. Migration: reorder positional calls, or name the arguments (#119)
- **New Lexicon parameters shift positional arguments** — `SearchPostsAsync` takes `tags` for `tag`, and `GetFollowersAsync`, `GetListsAsync`, `MuteActorAsync`, `ListConvosAsync`, `LoginAsync` and others gain inputs before paging or the token. Migration: name the arguments after the first (#123, #124, #128)
- **`ListAllRecordsAsync` is `EnumerateRecordsAsync`, and enumerators take `int? pageSize = null`** — the server's default, where `RecordCollection<T>` used 100; `ListAllSegmentsAsync` is `EnumerateSegmentsAsync`. Migration: rename; pass `pageSize: 100` to keep the old size (#119)
- **Cursored responses derive from `CursorPage<T>`, replacing `ICursoredResponse`** — `sealed record`s with a public `Items` (`ICursorPage<T>`); model lists are `IReadOnlyList<T>`, and `GetListResponse.Items` is `Members`. Migration: copy a list you need to change (`[.. page.Records]`) (#119, #180)
- **Single-use request bodies are internal** — request types only one method built (`CreateRecordRequest`, `CreateSessionRequest`, `SendMessageRequest` and many more), and the never-sent `DeleteChatAccountRequest` is removed. Migration: call the method, which takes their fields as parameters (#119)
- **`ProfileViewBasic` ⊂ `ProfileView` ⊂ `ProfileViewDetailed`, and `GetSessionResponse` ⊂ `SessionResponse`, by inheritance** — so `is ProfileView` also matches a `ProfileViewDetailed`. Migration: recompile, and put the most specific type first in a `switch` or chained `is` (#180)
- **`atproto-lexgen csharp` emits typed identifiers and `IReadOnlyList<T>`** — Lexicon string formats become the SDK's identifier types and `AtDatetime`, space declarations included, and `lexicon` maps them back. Migration: regenerate, then parse literals (#119, #141)
- **One session model: `AtProtoSession`, with `PasswordSession` and `OAuthSession`** — immutable records replacing `Session`, `OAuthSessionResult` and `AtProtoTokenData`, with a typed, required `Did` and `Handle`; `client.OAuthSession` is gone. Migration: pattern-match `client.Session` (#119, #124)
- **`ApplyOAuthSessionAsync` is replaced by `ApplySessionAsync(session, oauthClient)`** — it installs either kind of session and persists to the client's own store; `ResumeSessionAsync` takes an `AtProtoSession`. Migration: `ApplySessionAsync(session, oauth)` on a client constructed with the store (#124)
- **`CompleteAuthorizationAsync` returns an `OAuthAuthorizationResult`, and `RefreshTokensAsync` is `RefreshAsync(session)`** — the result carries the `OAuthSession` and `AppState`, and owns nothing disposable. Migration: `var (session, _) = await oauth.CompleteAuthorizationAsync(…)` without `using` (#124, #180)
- **One persistence interface: `IAtProtoSessionStore`** — it replaces `ISessionStore` and `IAtProtoTokenStore`, and the stores are `InMemoryAtProtoSessionStore`, `FileAtProtoSessionStore` and `EfCoreAtProtoSessionStore<T>`, which read 0.6 data. Migration: rename; a custom store implements the new members (#124)
- **`ServerClient` no longer changes the client's session** — `CreateSessionAsync` / `CreateAccountAsync` return tokens without installing them, and `RefreshSessionAsync` / `DeleteSessionAsync` take the refresh JWT. Migration: `client.LoginAsync`, `CreateAccountAndLoginAsync`, `LogoutAsync` (#124)
- **`AutoRefreshSession` refreshes on demand, and the refresh timer is opt-in** — before a request whose token is about to expire and once after a rejection; `BackgroundRefresh` adds the old timer. Migration: drop a setting that only turned the timer off, since that now disables refreshing (#124)
- **`LogoutAsync` throws when the service-side sign-out fails** — after removing the session locally, and for an OAuth session installed without its `OAuthClient`. Migration: catch `XrpcException` / `OAuthException` for a best-effort sign-out (#124)
- **`AtProtoOAuthServerOptions.ClaimsFactory` takes an `OAuthSession`** — Migration: read `session.Did.Value` and `session.Handle.Value` where `result.Did` and `result.Handle` were read (#124)
- **One XRPC exception: `XrpcException` replaces `AtProtoHttpException`** — it derives from `AtProtoException`, not `HttpRequestException`; `Error` (was `ErrorType`) is never null; 429 and 401 have their own subtypes. Migration: `catch (XrpcException ex) when (ex.Is(XrpcErrors.X))` (#117)
- **Hosted endpoints throw the core `ATProtoNet.Http.XrpcException`** — `ATProtoNet.Server.Xrpc.XrpcException` is gone, and its status (like `SpaceVerificationException`'s) is an `HttpStatusCode`. Migration: `using ATProtoNet.Http;`, and `HttpStatusCode.NotFound` for `StatusCodes.Status404NotFound` (#117)
- **Every SDK exception derives from `AtProtoException`, and `OAuthException.ErrorCode` is `Error`** — one `catch (AtProtoException)` covers every SDK failure. Migration: recompile, and read `ex.Error` (#117)
- **A response that does not match its type throws `XrpcResponseFormatException`** — instead of a raw `JsonException` or `InvalidOperationException`, from every XRPC call and typed record read. Migration: catch `XrpcResponseFormatException` (#117)
- **Calls that need a session throw `XrpcAuthenticationException` without one** — with `AuthenticationRequired` and before sending, instead of `InvalidOperationException`. Migration: `catch (XrpcAuthenticationException ex) when (ex.Is(XrpcErrors.AuthenticationRequired))` (#122)
- **One `RecordRef` and one `RecordView<T>` for every write and typed read** — sealed records built by constructor; `CreateRecordResponse`, `PutRecordResponse` and `GetRecordResponse` leave the public API. Migration: declare `RecordRef` where those were used, and use the constructors (#122)
- **`AtProtoRecord.CreatedAt` is no longer set on construction** — `RecordCollection<T>.CreateAsync` stamps it when unset, so a record read without `createdAt` is written back without one. Migration: set `CreatedAt` yourself before `PutAsync` or a `RepoClient` write (#122)
- **The Bluesky helpers move to `client.Bsky` and take typed subjects** — `PostAsync(RichText, PostOptions?)`, `LikeAsync(StrongRef)`, `RepostAsync` and `FollowAsync(Did)` return a `RecordRef`; `DeleteRecordAsync(AtUri)` replaces the undo methods. Migration: `client.Bsky.LikeAsync(posted.ToStrongRef())` (#122)
- **`UpdateProfileAsync` moves to `client.Bsky` and takes an edit callback** — `UpdateProfileAsync(Action<ProfileRecord>)` replaces the four optional parameters and keeps every field you do not touch. Migration: `client.Bsky.UpdateProfileAsync(p => p.DisplayName = "Alice")` (#115, #122)
- **`RichTextBuilder.Build()` returns `RichText`** — instead of a tuple, with empty rather than `null` `Facets`. Migration: it still deconstructs; test `facets.Count` instead of `null` (#122)
- **`Embed.RecordView` is renamed `RecordEmbedView`** — Migration: `post.Embed is RecordEmbedView quote` (#122)
- **`CreatePdsAccountRequest` is removed** — `PdsAdminClient.CreateAccountAsync` takes the Lexicon's `CreateAccountRequest`. Migration: `new CreateAccountRequest { … }` (`ATProtoNet.Lexicon.Com.AtProto.Server`) with the same properties (#122)
- **`BlobLink` is removed: `BlobRef.Ref` is a `CidLink`** — Migration: `CidLink.FromCid(cid)` (#122)
- **Custom XRPC takes `XrpcParams`, typed procedure inputs and `XrpcCallOptions`** — `QueryAsync<TOut>(nsid, XrpcParams?, …)` and `ProcedureAsync<TIn, TOut>(nsid, input, …)` replace anonymous-object parameters and `ProcedureAsync<T>`. Migration: `new XrpcParams { { "limit", 25 } }`; name `options:` (#117, #122, #180)
- **Blob, repository and chat-export downloads return `XrpcStreamResponse`** — the stream with its `ContentType` and `ContentLength`; disposing it releases the response. Migration: `await using var blob = await client.Sync.GetBlobAsync(did, cid);` and read `blob.Content` (#117, #123)
- **XRPC endpoints declare their NSID once, as a static `Nsid`** — `IXrpcEndpoint.Nsid` is `static abstract`, and `[XrpcEndpoint]` is removed. Migration: `public static Nsid Nsid { get; } = Nsid.Parse("…");` in place of the attribute and the instance property (#121)
- **`AddXrpcEndpointsFromAssembly` registers every class implementing `IXrpcEndpoint`** — and registration rejects a handler with no or several endpoint interfaces, or a second one for an NSID. Migration: keep handlers that must not be mapped out of scanned assemblies (#121)
- **`MapXrpcEndpoints()` returns the `/xrpc` `RouteGroupBuilder`** — so `.RequireAuthorization()` and the like apply to every endpoint. Migration: make a `Map…` call chained onto its result on the app instead, or it maps under `/xrpc` (#121)
- **The SDK's Lexicon unions are open, with an `Unknown*` variant** — a `$type` the SDK does not model reads as `UnknownEmbed`, `UnknownFacetFeature` and so on instead of throwing. Migration: add an `Unknown*` arm, or a discard, to every `switch` over a union; the compiler does not flag a missing one (#115)
- **Lexicon models derive from `LexObject`** — as does `AtProtoRecord`, whose `ExtensionData` keeps fields a model does not declare. Migration: recompile; a record type with its own `[JsonExtensionData]` member drops it (#115)
- **`AtProtoJsonDefaults.Options` is read-only** — Migration: register union variants on `LexiconTypeRegistry.Instance`, or copy the options with `new JsonSerializerOptions(AtProtoJsonDefaults.Options)` (#115)
- **`LexiconTypeRegistry` is reduced to union variants** — the record-type registry, `CreateOptions()`, `LoadPluginsFromAssembly`, `[LexiconPlugin]`, the constructor and `IAtProtoUnion` are gone; closed unions refuse variants. Migration: `LexiconTypeRegistry.Instance`, `LoadPlugin<T>()`, `[AtProtoUnion(…)]` (#115, #133)
- **Lexicon unions that were `JsonElement` are typed open unions** — `FeedViewPost.Reason`, `ThreadgateRecord.Allow`, `PostgateRecord.EmbeddingRules`, `RecordEmbedView.Record`, the relationships and the preferences. Migration: match the variant types; an unknown one's `Raw` is the old `JsonElement` (#123)
- **Chat messages, log entries and message embeds are typed unions** — `GetMessagesResponse.Messages` and `ConvoView.LastMessage` are `ConvoMessage`s, and `ConvoLogEntry` is the base of one type per event. Migration: pattern-match (`msg is MessageView m`) instead of reading `$type` (#128)
- **Lexicon references that were `JsonElement` are typed** — `PostView.Threadgate`, `BlockedPost.Author`, `ViewerState.MutedByList`, `StarterPackView.Feeds`, `LabelerView.Creator`, `TeamMember.Profile` and others; `TeamMemberProfile` is removed. Migration: read the typed properties (#123)
- **`PostView` writes its `$type`** — it derives from the new `PostEntry` union base, as `GeneratorView` and the other union members already did. Migration: none for reading; expect the `$type` in JSON you write from a `PostView` (#127)
- **`ProfileViewDetailed.AssociatedChat` is replaced by `Associated`** — `associatedChat` was never on the wire, so it was always `null`. Migration: read `profile.Associated?.Chat?.AllowIncoming` (#123)
- **Chat names follow the Lexicon** — `CanConvo` is `CanChat`, `ListConvosAsync` takes `readState` for `readOnly`, `ConvoView.Opened` and `AcceptConvoResponse.Convo` are removed, and `UpdateAllReadAsync` returns an `UpdateAllReadResponse`. Migration: `CanChat`, `readState: "unread"` (#123)
- **Ozone's review queue is `QueryStatusesAsync`** — `QuerySubjectsAsync` and `QuerySubjectsResponse` called a method that never existed. Migration: `QueryStatusesAsync(new SubjectStatusFilter { … })`, and read `SubjectStatuses` (#123)
- **`SignatureClient.SearchAccountsAsync` takes signature values and returns account views** — `values` is `IEnumerable<string>`, results are `AccountInfo`, and `AccountResult` is removed. Migration: pass the `SigDetail.Value`s (#123)
- **Notification methods drop `priority` and `seenAt`** — `ListNotificationsAsync` takes `reasons` instead, and `GetUnreadCountAsync` drops `priority`; upstream ignores one and rejects the other. Migration: remove the arguments (#123)
- **`AtProtoScopes.PermissionSets` holds only published permission sets** — six constants naming unpublished sets are removed, and `ManageNotifications` is `app.bsky.authManageNotifications`. Migration: `DeleteContent`, `ManageNotifications`, `ManageModeration` or `FullApp`; recompile (#123)
- **Ozone models match their Lexicons** — `RecordViewDetail.BlobCids` is `Blobs`, the label and tag event lists and `ModEventReport.ReportType` are `required`, and `ModEventMuteReporter.DurationInHours` is `int?`. Migration: read `Blobs[i].Cid`; set the lists, empty if unchanged (#123, #131)
- **One moderation-subject union, `ModerationSubject`** — in `ATProtoNet.Lexicon.Com.AtProto.Moderation`, replacing `ReportSubject` and the Ozone copy and typing report and status subjects; `RecordSubject.Cid` is required. Migration: import the namespace; `ModerationSubject` for `ReportSubject` (#122)
- **`VideoClient.UploadVideoAsync` uploads in parts and waits for processing** — it returns the completed `JobStatus`, throws `VideoUploadException` for a failed job, and takes `VideoUploadOptions?` before the token. Migration: drop your polling loop, or call `UploadVideoInOneRequestAsync` (#127)
- **One immutable `DidDocument`** — `ATProtoNet.Auth.OAuth.DidDocument` and `DidService` are removed; `Identity.DidDocument` is init-only, its entries are `DidDocumentService`s, and `GetHandle()` / `GetPdsEndpoint()` return a `Handle?` / `Uri?`. Migration: object initializers; `GetPdsEndpoint()?.OriginalString` (#120)
- **`DidDocument.GetSigningKey` / `GetVerificationKey` return `null` for key material that does not decode** — instead of throwing `FormatException`, as does `SpaceAuthority.GetSigningKey`'s fallback. Migration: `TryGetVerificationKey` tells a malformed entry from an absent one (#180)
- **`DidResolutionException` replaces `PlcException` and `DidWebException`** — every resolver throws it, network failures included, with a `DidResolutionErrorKind` (`Tombstoned` is `Deactivated`). Migration: `catch (DidResolutionException ex) when (ex.Kind == DidResolutionErrorKind.NotFound)` (#120)
- **DID resolvers implement `IDidResolver`** — `ResolveAsync(Did)` replaces `ResolveDidAsync(string)`, and `SpaceCredentialProvider` takes an `IDidResolver`. Migration: `await resolver.ResolveAsync(Did.Parse(did))`; pass a `CachingDidResolver` (#120)
- **`PlcClient` takes an explicit directory URL and typed arguments** — it no longer reads `BaseAddress`, methods take a `Did`, `PlcOperation` replaces `PlcData`, and `PlcOperationBuilder` is typed too. Migration: `new PlcClient(httpClient, new Uri("https://plc.directory"))` (#120)
- **`AuthorizationServerDiscovery` and `OAuthClient.Discovery` are internal** — `OAuthClient` walks the discovery chain itself, with the resolver `OAuthOptions.IdentityResolver` supplies. Migration: resolve an identity with an `IIdentityResolver` (`ResolveAsync(AtIdentifier.Parse(handle))`) (#120, #180)
- **`OAuthClient` takes its `HttpClient` from the options, and follows the identity fetch policy** — `OAuthClient(options, logger)`; without `OAuthOptions.HttpClient`, requests go to public HTTPS addresses only and fail as `OAuthException`. Migration: `AllowPrivateNetworks` for a local PDS (#120, #132)
- **`StartAuthorizationAsync` returns an `OAuthAuthorizationRequest` and takes `OAuthAuthorizationOptions`** — `AuthorizationUrl` (a `Uri`), `State` and `ExpiresAt` instead of a tuple, and `pdsUrl` is `ServerUrl`. Migration: `var (url, state, _) = …`; redirect to `url.AbsoluteUri` (#132)
- **`OAuthClient.GetPendingAuthorizationState` and `OAuthAuthorizationState` are removed** — they exported the PKCE verifier without the DPoP key. Migration: keep pending logins across instances with `OAuthOptions.StateStore` (#132)
- **`AtProtoScopes.Rpc` and `Include` refuse an audience that is not a service** — `aud` must be a DID with a service fragment, or `*` for `rpc`. Migration: append the fragment, for example with `AtProtoScopes.BlueskyAppView` (#132)
- **`IdentityAction` and `AtProtoScopes.Identity(attr, action)` are removed** — the permission spec dropped the `action` parameter. Migration: `AtProtoScopes.Identity(attr)` (#132, #180)
- **`AtProtoOAuthService`'s constructor is internal** — `WithOAuth()` builds the service from the container's identity resolver, state store, server and refresh coordinator. Migration: resolve it from dependency injection instead of constructing it (#120, #132, #136, #180)
- **The hosted login's HTTP and identity settings move to the shared registrations** — `AtProtoOAuthServerOptions.HttpClient`, `HttpClientTimeout` and `HandleResolutionTimeout` are removed. Migration: `AddAtProtoIdentity(o => …)` for the timeout; configure the client `AtProtoOAuthExtensions.HttpClientName` (#180)
- **`IOAuthClientProvider` is removed; the client factory takes the `OAuthClient`** — `WithOAuth()` registers the login's client as the `OAuthClient` singleton, and `TryGetClient()` is the `Client` property. Migration: with your own OAuth flow, register your `OAuthClient` as a singleton (#136)
- **`AtProtoOAuthService.CompleteCallbackAsync` returns an `AtProtoOAuthCallbackResult`** — `RedirectUrl`, `IsRelay` and `Did`, instead of a string the caller had to vet. Migration: redirect to `result.RedirectUrl` (#136)
- **The client factory, the Blazor widgets and sign-out act only for the OAuth login's user** — they ignore identities the login did not issue, service auth's included. Migration: a principal you build for the factory needs `AtProtoClaimTypes.AuthMethod` set to `oauth` (#136)
- **The development loopback client takes its callback from the server's address** — the plain HTTP loopback address fixed at startup, not the first request; without one the login fails. Migration: bind an address such as `http://127.0.0.1:5000`, or set `BaseUrl` (#136)
- **The Blazor components act as the signed-in user, and `FeedView.FeedSource` is a `FeedSource`** — through `AtProtoUserClientAccessor`, registered by `AddAtProtoBlazor()`. Migration: call `AddAtProtoBlazor()`; `FeedSource.Author(AtIdentifier.Parse("alice.bsky.social"))` for `"author:alice.bsky.social"` (#136)
- **The bearer-JWT `AtProtoAuthenticationHandler` is removed** — with `AtProtoAuthenticationOptions` and `AuthenticationBuilder.AddAtProto()`. Migration: authenticate other services with `AddAtProtoServiceAuth(…)` and `[RequireServiceAuth]`, and users with the OAuth cookie login (#136)
- **`ServiceAuthGenerator` takes a `Did`, and `CreateToken` requires an `Nsid` `lxm`** — per the 2026 service auth revision; `audience` must be a DID with an optional service fragment. Migration: `new ServiceAuthGenerator(Did.Parse(…), key)`; pass the NSID of the method called (#119, #125)
- **`ISpaceReplayStore` is generalized into `IJtiReplayStore`** — `InMemoryJtiReplayStore`, `EfCoreJtiReplayStore<T>` and `JtiReplayEntity` on table `AtProtoJtiReplay` replace the `Space…Replay…` names. Migration: rename, and add an EF migration for the renamed table; dropping the old one loses nothing (#125)
- **`VerifiedServiceAuth` moves to `ATProtoNet.Server.Authentication`** — a class with init-only properties instead of a positional record, with the token's `lxm` as `Method` and its `KeyId`, `TokenId`, `IssuedAt` and `ExpiresAt`. Migration: read properties instead of deconstructing (#125)
- **`notifyWrite` must be signed by its writer, and more strictly** — `iss` must be the account whose repo advanced, and a token without `jti`, `lxm` or `iat`, with a fragment in `iss`, or with a `kid` other than `#atproto` is refused. Migration: sign as the writer through `ISpaceAccountSigner` (#125, #133)
- **`ISpaceServiceAuthVerifier` and `SpaceServiceAuthVerifier` are removed** — `notifyWrite` verifies its service auth with `ServiceAuthVerifier` under `SpaceServerOptions.ClockSkew`. Migration: verify service auth elsewhere with `ServiceAuthVerifier` or `AddAtProtoServiceAuth()` (#113, #133, #180)
- **Firehose construction leaves `AtProtoClient`** — `CreateFirehoseClient`, `CreateFirehoseConsumer` and `RelayUrl` (options, builder and Aspire settings) are removed. Migration: `new TypedFirehoseConsumer(new TypedFirehoseConsumerOptions { ServiceUrl = "wss://bsky.network" })` (#122)
- **`FirehoseClient`, `FirehoseConsumer`, `FirehoseFrame` and `JetstreamClient` are removed** — `TypedFirehoseConsumer`, `LabelStreamConsumer` and `JetstreamConsumer` read every stream, reconnecting, and are not `IDisposable`. Migration: `ConsumeAsync(cursor)` for `SubscribeAsync` (#126, #180)
- **Stream consumers share `StreamConsumerOptions` and a `StreamReconnectPolicy`** — `Reconnect` replaces `ReconnectDelay` and `MaxReconnectAttempts`, and the cursor settings sit on `CursorStreamConsumerOptions`. Migration: `Reconnect = new() { MaxAttempts = null }` for `MaxReconnectAttempts = -1` (#126, #180)
- **A consumer whose reconnect attempts run out throws `EventStreamException`** — instead of completing, and cancelling the token ends every consumer's enumeration normally. Migration: catch `EventStreamException`, or set `MaxAttempts = null` to reconnect forever (#126)
- **`IFirehoseCursorStore` is `IStreamCursorStore`, with `ValueTask` methods** — shared by every cursor-keeping consumer; `InMemoryFirehoseCursorStore` is `InMemoryStreamCursorStore`. Migration: rename, and return `ValueTask` from a custom store (#126)
- **`FirehoseEventParser` keeps `Parse(ReadOnlyMemory<byte>)`** — `Parse(FirehoseFrame)` and `TryParse` are removed, and an error frame throws `EventStreamException`. Migration: `FirehoseEventParser.Parse(bytes)` (#126)
- **Firehose verification is `RepoSyncVerifier`** — `FirehoseVerifier`, `VerificationResult` and the options' `Verifier` and `VerifySignatures` are removed, and `CollectionFilter` is an `IReadOnlySet<Nsid>`. Migration: `SyncVerifier = new RepoSyncVerifier()`; `VerifyCids` still checks CIDs alone (#126, #180)
- **Firehose messages follow the current `subscribeRepos` Lexicon** — `HandleEvent` and `TombstoneEvent` are gone, `Seq` and `Time` move to a new `FirehoseEvent` base, and `TooBig`, `Rebase` and `Blobs` are `[Obsolete]`. Migration: `(message as FirehoseEvent)?.Seq`; handle `#identity` and `#account` (#126)
- **Jetstream uses `RepoOpAction`** — `JetstreamOperation` is removed, and `JetstreamCommitEvent.Operation` is the firehose's enum. Migration: rename `JetstreamOperation` to `RepoOpAction` (#126)
- **One `JetstreamException`** — it replaces `JetstreamConnectException` and `JetstreamArchiveException` and derives from `EventStreamException`, and `EventStreamError` replaces `JetstreamStreamError` in `OnStreamError`. Migration: catch `JetstreamException` (#126)
- **`JetstreamDictionaryClient` is folded into `JetstreamArchiveClient`** — as `GetZstdDictionaryAsync(id)`. Migration: `new JetstreamArchiveClient(url).GetZstdDictionaryAsync()` (#126)
- **`JetstreamEventParser` keeps `ParseFrame(ReadOnlyMemory<byte>, JetstreamProtocol)`** — the other `Parse` and `ParseFrame` overloads are removed. Migration: `ParseFrame(Encoding.UTF8.GetBytes(json), protocol).Event` (#126)
- **Unused Jetstream members are removed** — `JetstreamPlanStats`, `JetstreamSnapshotPlan.Stats`, `JetstreamConsumer.IsConnected` and `Dispose`, and `JetstreamReplayConsumer.PinnedTipSeq`; `JetstreamSegmentReader.DecodeBlock` is internal. Migration: decode with `DecodeBlockFrame` (#126)
- **`JetstreamReplayConsumer` is configured by its options alone** — the `(options, archiveClient)` constructor and `JetstreamArchiveOptions.AfterSeq` are removed. Migration: set the archive settings on `JetstreamArchiveOptions`, and pass the resume position to `ReplayAsync(afterSeq)` (#180)
- **`MerkleSearchTree` only accepts valid MST keys, and `Create` rejects duplicates** — `Add` and `Create` throw `ArgumentException` for a key that is not `collection/rkey`, and for a repeated key rather than building a corrupt tree. Migration: key the tree by repo paths (#111)
- **Unused repository helpers are removed or internal** — `CreateFromEntries`, `DecodeToNode`, `TryValidate`, `DagCborEncoder.ComputeCid` and `CarBlock.DataLength` go; the MST node types are internal. Migration: `MerkleSearchTree.Create`, `CidComputation.ComputeForDagCbor`, `Data.Length`, `Decode` (#111, #180)
- **`DagCborDecoder.Decode` throws `FormatException` for all malformed input** — it threw `InvalidOperationException` or leaked `CborContentException`, and so does `MerkleSearchTree.Deserialize` for a tree too deep. Migration: catch `FormatException` (#111)
- **`simplespace` separates read and write policies, and `putMember` replaces `addMember`** — following upstream's read/write split ([bluesky-social/atproto#5496](https://github.com/bluesky-social/atproto/pull/5496)), which 0.6 failed against. Migration: pass `readPolicy:` and `writePolicy:` for `policy:`, and `PutMemberAsync(space, did, read: true, write: true)` for `AddMemberAsync` (#113)
- **`SimpleSpaceClient.CheckUserAccessAsync` takes a required `access`** — before `clientId`, as the Lexicon now requires. Migration: `CheckUserAccessAsync(space, user, SimpleSpaceAccess.Read, clientId)` (#113)
- **The `simplespace` store and record carry both policies and per-member access** — `SimpleSpaceRecord` takes `ReadPolicy` and `WritePolicy`, `ISimpleSpaceStore` has `PutMemberAsync` / `GetMemberAsync`, and `SpaceNsids.AddSimpleSpaceMember` is `PutSimpleSpaceMember`. Migration: implement both in a custom store (#113)
- **EF Core `simplespace` schema: `Policy` is split and members gain access flags** — `ReadPolicy` and `WritePolicy` replace `Policy`, and members gain `Read` and `Write`. Migration: a migration that copies `Policy` into both columns and defaults the flags to `true`; see `docs/spaces.md` (#113)
- **`ISpaceAccessPolicy` also decides writes** — `SpaceAccessRequest.Access` is `SpaceAccessKind.Read` or `Write`, and `ISimpleSpaceManagingAppClient.CheckUserAccessAsync` takes the kind too. Migration: a custom policy handles `Write` without an app perimeter, or it refuses every writer (#113)
- **Space server endpoint handlers and query-parameter types are internal** — `NotifyWriteEndpoint`, `AddSimpleSpaceMemberEndpoint` and the other handlers, their bases and `*Parameters` types; the `Add…` registrations map them as before. Migration: implement your own `IXrpcEndpoint` (#113, #133)
- **The space server resolves DIDs through an `IDidResolver`** — a keyed `CachingDidResolver` set by `SpaceServerOptions.DidCache` replaces `ISpaceDidDocumentResolver` and `DidDocumentCacheLifetime`. Migration: register a custom one under `SpaceServerExtensions.DidResolverKey`; set `DidCache.StaleAfter` (#120)
- **`SpaceSyncer` takes an `IDidResolver`** — instead of a signing-key delegate, and `ResolveSigningKeyAsync` is removed; `SpaceAuthority.GetHostEndpoint` and `GetServiceEndpoint` return `Uri?`. Migration: `new SpaceSyncer(space, store, new CachingDidResolver())` (#120)
- **Space server constructors take new optional parameters** — `AddSpaceAuthority<T>` gains `serviceAuthKey`, `SpaceCredentialVerifier` `options`, the notifier and managing-app client an `ISpaceAccountSigner`; `SpaceCredentialIssuer` is not `IDisposable`. Migration: name later arguments; dispose the key yourself (#133)
- **`ISpaceCredentialIssuer` is removed** — credential signing uses `SpaceCredentialIssuer` over the in-process key given to `AddSpaceAuthority`. Migration: resolve `SpaceCredentialIssuer`; an out-of-process signer has no replacement in 0.7 (#180)
- **Space server token types carry only what they establish** — the `Verified…` token records drop unused members, `DPoPProof` keeps only `KeyThumbprint`, and both `BuildRequestUri` methods are gone. Migration: `auth.Delegation.Space`, `credential.Space.Authority` (#133, #180)
- **Test-only public members are removed or internal** — `SpaceTokens.TryParse` and `Verify(type, jwt, …)`, `SpaceToken.ToSpaceUri()`, `DPoPProofValidator.ProofType`, `ClaimsSpaceCallerResolver.DidClaimType` and `SimpleSpaceAuthorityStore.Inner`. Migration: `Verify(SpaceTokens.Parse(type, jwt), …)`, `AtProtoClaimTypes.Did` (#180)
- **Space and SimpleSpace models follow the open-union pattern** — `SimpleSpaceUserPolicy` and `SimpleSpaceAppAccess` are open, so a newer variant reads as `Unknown…` instead of failing `getSpace`, and the models derive from `LexObject`. Migration: add an `Unknown…` (or `_`) arm to a `switch` over a policy (#133)
- **The EF Core space and replay stores no longer run on the EF Core in-memory provider** — they write with `ExecuteUpdate` / `ExecuteDelete`, which it does not translate. Migration: test on SQLite in memory (`Mode=Memory;Cache=Shared`, connection held open) (#133)
- **One startup warning for in-process stores; `WarnOnInMemoryStores` and `MaxClientMetadataBytes` are removed** — it covers the replay store `AddAtProtoSpaces()` and `AddAtProtoServiceAuth()` fall back to. Migration: register `InMemoryJtiReplayStore` yourself to keep it quietly (#133, #180)
- **`atproto-lexgen publish` publishes to a PDS, and `migrate` is removed** — `publish` writes `com.atproto.lexicon.schema` records, and its `--output`, `--baseline`, `--assembly` and `--no-bump` options and the `Migrations` types are gone. Migration: gate changes with `diff --strict` (#130, #180)
- **Unused `atproto-lexgen` helpers are removed** — `CSharpEmitter.Emit(LexiconDocument)`, `LexiconEmitter.JsonOptions` and five `TypeMapper` helpers. Migration: `EmitAll([document])`, `LexiconJson.WriteOptions`, and `TypeMapper.SplitRef` for `ParseTypeValue` (#130)

### Added

- **`TidGenerator`** — mints strictly increasing, never-repeating TIDs per the spec, thread-safe with an injectable `TimeProvider`; `Tid.Next()` and `RecordKey.NewTid()` use a shared instance (#114)
- **Generic parsing for identifiers** — every identifier type implements `IParsable<T>`, `ISpanParsable<T>` and `IComparable<T>`, so minimal-API binding and generic `T.Parse` code accept them; `Cid` gains `Codec`, `Digest` and `ToBytes()` (#114)
- **`AtDatetime`** — the Lexicon `datetime` type: it keeps the text it was read with, so a record written back keeps its CID, reads leniently (`IsValid`, `TryGetValue`) and is strict when created from code (#119)
- **Open unions and unknown-field round-tripping for your own Lexicons** — mark a union base `[AtProtoUnion(typeof(UnknownX))]` and derive models from `LexObject`; see `docs/custom-records.md` (#115)
- **`app.bsky.embed.gallery` and new profile record fields** — `GalleryEmbed` and `GalleryView` as post and `recordWithMedia` embeds, and `ProfileRecord.Pronouns`, `Website`, `Labels` and `JoinedViaStarterPack` (#115)
- **Per-call XRPC options** — `XrpcCallOptions` sets `Proxy`, `AcceptLabelers`, `Headers` and a `Timeout` for one call, so concurrent callers on a shared client no longer race on `SetProxy` / `SetLabelers` (#117)
- **`AtProtoClientOptions.UserAgent` and `RateLimit`** — the `User-Agent` sent with every request, and `XrpcRateLimitOptions` (`MaxRetries`, `MaxDelay`) for 429 handling (#117)
- **`XrpcErrors`** — constants for the generic and common `com.atproto.*` XRPC error names, for `XrpcException.Is` (#117)
- **XRPC procedures with no input or a binary body** — `IXrpcProcedure<TOutput>`, `IXrpcProcedureVoid`, and `IXrpcBlobProcedure<TOutput>`, which receives the unbuffered body as an `XrpcBlobInput` (#121)
- **XRPC handler attributes apply to their endpoint** — `[Authorize]`, `[AllowAnonymous]`, `[EnableRateLimiting]` and any other attribute on a handler class become its endpoint's metadata (#121)
- **Enumerators and a public paginator** — `Enumerate*` walks the timeline, author feed, actor likes, followers, follows, list members, notifications, messages and bookmarks, and `Pagination.EnumerateAsync` any other cursored method (#119, #127, #180)
- **AT URI overloads** — `RepoClient.GetRecordAsync` / `DeleteRecordAsync`, `SpaceClient.GetRecordAsync` / `DeleteRecordAsync` and the `StandardSiteClient` getters also take the record's `AtUri` or `SpaceRecordUri` (#119)
- **A drift test against the upstream Lexicons** — every XRPC call's NSID and method, every model's JSON names and the known-values constants are checked against a pinned snapshot of bluesky-social/atproto and standard.site (#123)
- **Current `app.bsky` fields** — profile views gain `Pronouns`, `Associated`, `Verification` and `Status`, and posts, feeds, embeds, notifications, graph views, labelers and video jobs gain the fields their Lexicons added, with new `NotificationReasons` and `StandardLabelValues` (#123)
- **Current chat, `com.atproto`, `site.standard` and Ozone fields** — chat member views and declarations, `DescribeServerResponse.BlobUploadLimit`, `DocumentRecord.Contributors`, Ozone event and subject-status details, and the missing `queryEvents`, `querySets` and `listMembers` filters (#123)
- **Permission sets** — `AtProtoScopes.PermissionSets.DeleteContent`, `FullChatClient`, `StandardSiteFull` and `StandardSiteSocial` (#123)
- **`AtProtoClient.SessionChanged`** — raised when a session is created, refreshed, expires or is signed out, with the new and the previous session (#124)
- **Taken-down accounts can sign in** — `LoginAsync` and `CreateSessionAsync` take `allowTakendown`, for a session limited to migrating or exporting the account (#124)
- **`CreateAccountAndLoginAsync` and `TryRestoreSessionAsync`** — create an account and install its session, or install the session the store holds for a DID without a request (#124)
- **OAuth revocation** — `OAuthClient.RevokeAsync(session)` revokes a session at its authorization server (RFC 7009) (#124)
- **Signing in through an entryway lands on the account's PDS** — login and refresh follow the `#atproto_pds` endpoint of the account's own DID document when it is HTTPS (#124)
- **Ozone reports and queues** — `client.Ozone.Report` covers `tools.ozone.report` (queries, assignment, typed activities, closing, and live and daily statistics, the live ones as `QueueStats`), and `client.Ozone.Queue` covers `tools.ozone.queue`; see `docs/ozone.md` (#131, #180)
- **Ozone settings, safelink rules, verifications and account history** — `client.Ozone.Setting`, `Safelink`, `Verification` and `Hosting` (#131)
- **Ozone moderation lookups and scheduled actions** — batch repo, record and subject lookups returning the open `ModerationSubjectView` union, account timelines, reporter stats, and scheduling and cancelling actions (#131)
- **The Ozone event types the SDK lacked** — appeals, priority scores, age assurance, credential revocation, scheduled takedowns, and the account, identity and record events Ozone records for every subject (#131)
- **Granular report reasons** — `ReportReasons` gains the `tools.ozone.report.defs#reason*` values upstream prefers, and each coarse `com.atproto` reason names its replacement (#131)
- **Group chats** — `client.Chat.Group` covers `chat.bsky.group`: groups, members, join links and join requests; conversations and members carry a typed `Kind`, and a `JoinLinkEmbed` shares a group (#128)
- **Conversation endpoints and message fields** — unread counts, conversation requests, members, locking, the `kind` and `lockStatus` filters, reactions, replies, typed system messages, and constants for the known values (#128)
- **Chat status, notification preferences and moderation** — `ChatActorClient.GetStatusAsync`, `client.Chat.Notification`, and `client.Chat.Moderation` for moderation services (#128)
- **Verified record reads** — `SyncClient.GetVerifiedRecordAsync` and `RecordProof.Verify` check a `sync.getRecord` proof against the repository's signed commit, returning a `VerifiedRecord` or proof of absence (#129)
- **`SyncClient.GetBlocksAsync` and `RepoClient.ImportRepoAsync`** — fetch repository blocks by CID, and upload a repository CAR when migrating an account (#129)
- **`AdminClient.SearchAccountsAsync` and `UpdateAccountSigningKeyAsync`** — and `PdsAdminClient.SearchAccountsAsync` for PDSs that serve it, such as Tranquil (#129)
- **`AtProtoClient.Temp`** — `com.atproto.temp.*`: handle availability with suggestions, the signup queue, scope references, phone verification and credential revocation (#129)
- **`site.standard.graph.recommend`** — `RecommendRecord` and the `StandardSiteClient` recommendation methods (#129)
- **Bookmarks and drafts** — `client.Bsky.Bookmark` and `client.Bsky.Draft` manage the account's private bookmarks and post drafts; a bookmark's item is a `PostEntry`, the new open union of post views (#127)
- **Multipart video upload** — `VideoClient.StartUploadAsync`, `UploadPartAsync`, `FinishUploadAsync`, `GetUploadStatusAsync` and `AbortUploadAsync`, which `UploadVideoAsync` drives (#127)
- **`FeedClient.SearchPostsV2Async`** — `app.bsky.feed.searchPostsV2`, with every filter in a `PostSearchFilters` (#127)
- **Notification preferences and activity subscriptions** — `GetPreferencesAsync` / `PutPreferencesV2Async`, activity subscriptions, `UnregisterPushAsync` and `NotificationDeclarationRecord` (#127)
- **Actor and graph records** — `StatusRecord`, `ContentVisibilityDeclarationRecord`, `VerificationRecord` and `ReferenceListOptOutRecord` (#127)
- **Graph membership and starter pack search** — `GetListsWithMembershipAsync`, `GetStarterPacksWithMembershipAsync` and `SearchStarterPacksV2Async` (#127)
- **Threads, link cards, feed feedback and age assurance** — `GetPostThreadV2Async`, `GetEmbedExternalViewAsync`, `SendInteractionsAsync` (with `ServiceProxy.BskyFeedGenerator`) and `client.Bsky.AgeAssurance` (#127)
- **Record types name their collection** — a record implementing `IAtProtoRecord` declares `static Nsid Collection`, so `client.GetCollection<PostRecord>()` needs no NSID; the SDK's records and `atproto-lexgen` output implement it (#122)
- **`RecordCollection<T>.FindAsync` / `FindFromAsync`** — the record, or `null` on `RecordNotFound` (#122)
- **A public XRPC transport: `AtProtoClient.Transport`** — an `IXrpcTransport` that a sub-client for another Lexicon builds on, sharing the client's session, refresh and errors; see `docs/custom-xrpc.md` (#122)
- **Identity resolution API** — `IDidResolver`, `IHandleResolver` and `IIdentityResolver`; `ResolveAsync(AtIdentifier)` returns a bidirectionally verified `ResolvedIdentity`, and `IdentityResolver.CreateDefault()` builds the stack (#120)
- **`CachingDidResolver`** — a bounded cache for any `IDidResolver`, with stale-while-revalidate, remembered failures, one fetch per DID, invalidation and an optional `IDistributedCache` (#120)
- **`AddAtProtoIdentity()`** — registers the resolvers as singletons, optionally over the registered `IDistributedCache` (#120)
- **`resolveIdentity`, `resolveDid` and `refreshIdentity`** — on `IdentityClient`, with `IdentityInfo` and the matching `XrpcErrors` (#120)
- **PLC export** — `PlcClient.ExportAsync` pages the directory's `/export`, and `StreamExportAsync` follows `/export/stream` (#120, #180)
- **A configurable DNS-over-HTTPS endpoint** — `IdentityResolverOptions.DnsOverHttpsUrl`; `null` resolves handles over HTTPS only (#120)
- **Event-stream error frames are a typed error** — an `op = -1` frame is an `EventStreamException` with `Error` and `IsRetryable`, reported to `OnStreamError`, and consumers reconnect after a retryable one (#126)
- **Label streams and chat moderation events** — `LabelStreamConsumer` reads `subscribeLabels`, and `ChatModerationEventConsumer` reads `chat.bsky.moderation.subscribeModEvents` into typed events (#126)
- **Jetstream timestamp cursors** — `JetstreamCursor.FromTimestamp` builds a v2 cursor that seeks by time, so a stored v1 cursor resumes a v2 consumer (#126)
- **`IRecordEvent`** — one record change from either stream: `JetstreamCommitEvent` implements it, and `CommitEvent.GetRecordEvents()` splits a commit into decoded records (#126)
- **`OnEventDropped`** — every consumer reports the events it skips, with a `StreamDropReason` and the cursor (#126)
- **Jetstream archive probes and health check** — `ProbeSegmentAsync`, `ProbeBlockAsync` and `GetHealthAsync`, and `AddHealthChecks().AddJetstream(url)` in `ATProtoNet.Server` (#126)
- **Inbound service auth: `AddAtProtoServiceAuth()` and `ServiceAuthVerifier`** — services built on `ATProtoNet.Server` accept calls carrying service auth tokens, checked as the 2026 spec requires; require it with `[RequireServiceAuth]`, and tune it through `AtProtoServiceAuthOptions.Verifier` (#125, #180)
- **`XrpcMethodMetadata` and `JtiReplayDbContext`** — the NSID each mapped XRPC endpoint serves, for accepting service auth on routes you map yourself, and the replay table on a context of its own (#125)
- **`AtProtoClaimTypes`** — constants for the claims the OAuth login and service auth issue: `Did`, `Handle`, `HandleVerified`, `PdsUrl`, `AuthMethod`, `LexiconMethod` and `Audience` (#136)
- **Confidential OAuth clients (`private_key_jwt`)** — `OAuthClientKey` and `OAuthOptions.ClientKeys` sign every request with a client assertion, which earns longer sessions; `OAuthSession.ClientKeyId` survives key rotation (#132)
- **A store for pending OAuth authorizations** — `IOAuthStateStore`, in memory and bounded per requester by default, or `DistributedCacheOAuthStateStore`, which requires `Protect` / `Unprotect` (or `StoreSecretsUnencrypted`), so a login can complete on another instance (#132)
- **`OAuthAuthorizationOptions.RequesterIdFor` and `AppState`** — a requester id from a remote address, IPv6 grouped by its /64, and application data carried through the flow and handed back at completion (#132)
- **`OAuthOptions.RequestTimeout`** — bounds a whole pushed-authorization, token, refresh or revocation exchange, retry and body read included (#132)
- **A process-wide DPoP nonce cache** — every client shares each server's latest nonce, so first requests skip a `use_dpop_nonce` round trip (#132)
- **Scope presets built on permission sets** — `AtProtoScopes.Presets`, and the service audiences `AtProtoScopes.BlueskyAppView` and `BlueskyChat` (#132)
- **OAuth sign-in can start at an entryway** — a URL that serves no protected-resource metadata is tried as an authorization server, as upstream atproto#4896 allows (#132)
- **Lexicon resolution** — `LexiconResolver` resolves an NSID through its `_lexicon` DNS record and verifies the published schema against its commit proof; also `CachingLexiconResolver`, `client.Lexicon.ResolveLexiconAsync` and `AddAtProtoIdentity` registration (#130)
- **Permission-set checks** — `ResolvePermissionSetAsync` and `ResolveIncludeScopeAsync` confirm that the set an `include:` scope names exists before an app requests it (#130)
- **`atproto-lexgen` permission sets and `lint`** — `csharp` emits a class per `permission-set` with an `Include(aud)` helper, and `lint` checks documents against the Lexicon and permission specs (#130)
- **`atproto-lexgen publish` and `resolve` on the network** — `publish` lints, signs in, skips unchanged schemas, refuses breaking changes unless `--force` and prints the `_lexicon` TXT records; `resolve` fetches and verifies published schemas (#130)
- **Refresh coordination for clients that share a session store** — `ISessionRefreshCoordinator` (`InProcessSessionRefreshCoordinator`, `AtProtoClientOptions.RefreshCoordinator`) makes clients of one account share a refresh instead of spending the refresh token twice; `WithClientFactory()` registers one (#136)
- **Confidential clients in the hosted OAuth login** — `AtProtoOAuthServerOptions.ClientKeys`, and `ServeClientMetadata` has `MapAtProtoOAuth` serve the client metadata and the public key set (#136)
- **`PostCard` likes and reposts, and `ProfileCard` loads by `Actor`** — as the signed-in user, when no callback or profile is given (#136)
- **Default styles for the Blazor components** — the opt-in stylesheet `_content/ATProtoNet.Blazor/atproto-components.css` (#136)
- **`AddAtProtoClient(connectionName:)`** — takes the PDS URL from the connection string an Aspire `WithReference(pds)` supplies (#136)
- **Label signing and verification** — `LabelSigner` signs per the label spec, byte for byte like `@atproto/ozone`, and `LabelVerifier` checks against the issuer's `#atproto_label` key (`DidDocument.LabelKeyId`), reporting a `LabelVerificationStatus` (#135)
- **Verified label streams** — `LabelStreamConsumerOptions.Verifier` verifies every streamed label into `LabelsEvent.Verification` (#135)
- **Serving labels** — `QueryLabelsEndpoint` serves `queryLabels` over your `ILabelSource`, signing on the way out, and `LabelStreamFrames` encodes `subscribeLabels` frames (#135)
- **Sync 1.1 firehose verification: `RepoSyncVerifier`** — runs the sync spec's checklist on `#commit` and `#sync` (limits, CIDs, signature, operations inverted onto `prevData`), keeping each repository's state in an `IRepoSyncStateStore`, in memory or EF Core (#134)
- **Sync 1.1 mode for `TypedFirehoseConsumer`** — `SyncVerifier` delivers only events that verify and chain, and with `Resync` a broken repository is refetched, verified as a `RepoSnapshot` and delivered as a `RepoResyncEvent` (#134)
- **Tap client** — `TapClient` reads Tap's `/channel` with explicit acknowledgements, reconnecting, and calls its admin endpoints (#134)
- **Tap webhook receiver** — `MapTapWebhook(pattern, (evt, ct) => …)` receives Tap's webhook deliveries, checks the admin password, and acknowledges an unreadable event with a warning (#134, #180)
- **`ISpaceAccountSigner`** — signs space service auth as the account it speaks for, as the reference authority and managing apps require; an authority with a dedicated `#atproto_space` key passes its `#atproto` key to `AddSpaceAuthority` (#133)
- **`SpaceTokens.Verify(SpaceToken, …)`** — verifies a token already returned by `SpaceTokens.Parse`, for verifiers that read `iss` and `kid` first (#118)
- **Public API tracking** — every package carries `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`, checked by the public API analyzers, so an undeclared API change fails the build (#139)
- **A migration guide for 0.7** — `docs/migrating-to-0.7.md` covers every breaking change, with compiled before-and-after code (#138)

### Changed

- **Shared build settings are set once** — target framework, nullable, implicit usings and package metadata come from `Directory.Build.props`; published metadata is unchanged apart from the core package's dependencies (#110)
- **Aspire hosting: one base for both PDS container resources** — `AtProtoPdsContainerResource` and `AtProtoTranquilPdsContainerResource` derive from `AtProtoPdsContainerResourceBase`, so one AppHost helper covers either; environment and manifest are unchanged (#116)
- **`MerkleSearchTree.SerializeProof` returns the reference covering proof** — including the nodes on the paths to each key's neighbours, which relays need to invert a `#commit` (#111)
- **`MerkleSearchTree.Validate` checks that a loaded tree is canonical** — it rebuilds the tree and throws `InvalidOperationException` when the root differs (#111)
- **Faster DAG-CBOR, BLAKE3, `LtHash` and `did:key` verification** — the decoder writes JSON directly, BLAKE3 and `LtHash` use hardware vectors, and `AtProtoCrypto.VerifySignature` caches parsed keys; output is unchanged (#111, #112)
- **Identifier conversions are null-safe, and `SpaceUri` / `SpaceRecordUri` serialize as strings** — a `null` identifier converts to a `null` string, and the space URIs round-trip through JSON like every other identifier (#114)
- **A union variant always writes its `$type`** — also outside union position, as the data model allows, so a `$type` read there is written back (#115)
- **Minted JWTs allocate far less and share one `jti` format** — DPoP proofs, service auth and space tokens are written straight to UTF-8, and each carries a random `jti` from the system CSPRNG in the same hex form (#118)
- **Compressed responses and bounded connection lifetimes** — every `HttpClient` the SDK creates or registers decompresses responses, recycles pooled connections so DNS changes are picked up, and bounds connecting (#117)
- **JSON responses are deserialized as they arrive** — a large page no longer allocates a large-object-heap buffer, and `RecordCollection<T>` reads each record straight into `T` (#117, #122)
- **Rate-limit waits are capped** — a 429 asking for longer than `RateLimit.MaxDelay` throws `XrpcRateLimitException` at once, `Retry-After` dates are read, and retries carry jitter (#117)
- **`atproto-proxy` and `atproto-accept-labelers` are sent without a session too** — and the session calls themselves are never proxied (#117)
- **The SDK sets `User-Agent` per request** — the Aspire client and the OAuth login no longer add one to the `HttpClient`, and `SpaceReader`s share their provider's `HttpClient` (#117)
- **Service base URLs with a query or fragment are refused** — `InstanceUrl`, `SetServiceUrl`, `PdsAdminOptions.Url`, the PLC directory and space host URLs throw `ArgumentException` (#117)
- **Unmatched `/xrpc/{nsid}` requests answer with an XRPC error** — `501 MethodNotImplemented` for an unserved NSID, `405` with `Allow`, and `400 InvalidRequest` for a malformed one, as the reference `xrpc-server` does (#121)
- **Stream and space readers check identifiers where they read them** — a frame, commit, index entry, token or query parameter whose identifier does not parse is dropped or refused (#119)
- **Deprecated upstream, now `[Obsolete]`** — `SyncClient.NotifyOfUpdateAsync`, `ListNotificationsResponse.Priority`, `GetSuggestedFollowsByActorResponse.IsFallback`, and `StandardLabelValues.Gore`, `ContentWarning` and `NotAvailable` (#123)
- **`DisposeAsync` is the primary way to dispose `AtProtoClient`** — it waits for a token exchange under way to be stored, `Dispose` returns at once, and the client disposes only what it created (#124)
- **A session store that fails to write no longer discards a successful refresh** — the refreshed session stays installed, and the error is logged (#124)
- **Signing out of the hosted login revokes the OAuth session** — `/atproto/logout` revokes it at the authorization server before removing it (#124)
- **Identity resolution is cached wherever it repeats** — `RepoSyncVerifier`, `SpaceCredentialProvider` and the space server resolve through a `CachingDidResolver`, refetching once when a cached key fails, as the sync spec requires (#120)
- **Handle resolution fails closed on a conflict, and OAuth login asks only a handle's own authorities** — two DIDs in DNS, or DNS and HTTPS disagreeing, fail, and the `bsky.social` AppView is no longer asked (#120)
- **Streaming spends less CPU per event** — filtered consumers skip decoding dropped commits, a verified commit is parsed once, and the Jetstream archive filters rows before decoding them (#126)
- **Cursors are saved in the background** — every `CursorPersistInterval` events, off the read loop (#126)
- **A v2 `JetstreamConsumer` skips the replay of its start cursor** — the server replays `?cursor=N` inclusively (#126)
- **Service auth signatures are checked as the reference implementation checks them** — `alg` must match the key, high-S signatures are accepted, and a key a cached DID document lacks is found by one refresh (#125)
- **Space endpoints that authenticate their own callers opt out of host authorization** — a group-wide `RequireAuthorization()` or `RequireServiceAuth()` no longer blocks the credential, repo-host and `notifyWrite` endpoints (#125)
- **An OAuth session's `ServiceEndpoint` is the account's PDS** — also for a login started from a server URL, and a refresh follows a PDS move (#132)
- **OAuth sign-in fetches less** — metadata is cached briefly, the callback reuses the identity resolved at the start, and a PDS's protected-resource metadata is read once (#132, #136)
- **The transitional scopes are documented as legacy** — `transition:*` remains the default for now (#132)
- **The client factory caches each account's imported DPoP key** — per-request clients are much cheaper, and a signed-out session takes its key with it (#136)
- **`FileAtProtoSessionStore` reads without a lock and writes under a lock per account** (#136)
- **`ATProtoNet.Blazor` no longer declares browser support** — it runs on the server, on top of `ATProtoNet.Server` (#136)
- **`ATProtoNet.Server` depends on nothing beyond the ASP.NET Core shared framework** — EF Core moved to its own package, and `StackExchange.Redis` and the resilience handler are gone (#136)
- **The client factory resolves the login's `OAuthClient` when a client first needs it** — resolving the factory before the server starts no longer builds the loopback client (#136)
- **The hosted login's `HttpClient` comes from `IHttpClientFactory`** — the named client `AtProtoOAuthExtensions.HttpClientName`, under the identity fetch policy, so logging and telemetry handlers see its requests (#136)
- **Per-user clients take `UserAgent` and `RateLimit` from `AddAtProto(o => …)`** — they were created with default options (#136)
- **`atproto-lexgen`: the `null` type and space `collections`** — `csharp` reports a `null` definition as removed from the language, and leaves non-NSID space collections out with a warning (#130)
- **`atproto-lexgen lexicon` and `diff --assembly` load the assembly in its own context** — bound to its own dependencies, and `diff` reports unreadable files instead of skipping them (#130)
- **Space credentials and client attestation keys are cached** — a repo host verifies a credential's signature once until it expires, and attestation keys are cached per `client_id` with rate-limited refetches (#133)
- **`SpaceCredentialProvider` mints one credential per space at a time** — concurrent callers share one mint, and replaced credentials release their keys (#133)
- **The EF Core space stores update in one statement, and the replay stores sweep in the background** (#133)
- **`SpaceSyncer` recovery buffers the repo once, and the space verifiers are activated by the container** — so a registered `TimeProvider` reaches them (#133)
- **Integration tests use one enum-driven `[RequiresFact]` and provision their own accounts** — `ATPROTO_REQUIRE_INTEGRATION=1` covers every requirement, and CI runs the PDS suites on every pull request (#137)
- **Every await in the libraries calls `ConfigureAwait(false)`** — enforced by CA2007, which matters for hosts with a synchronization context (#139)
- **The documentation's C# samples are compiled** — `tests/ATProtoNet.DocSnippets` fails the build at the Markdown line of a sample that drifts from the API (#138)
- **`CidComputation.DecodeCidString` and `TryDecodeCidString` accept only canonical base32** — as `Cid.Parse` does (#180)
- **One loop reads every stream** — Jetstream and the Tap channel reconnect through the shared loop and send the SDK's `User-Agent`, and an abnormal close fails with an `EventStreamException` carrying its reason (#180)
- **Bodies from untrusted senders go through one bounded reader** — the Tap webhook, record proofs, repository downloads and space recovery (#180)
- **Signature checks against a DID document share one refetch-once path** — and `SpaceCommitVerifier.Verify` returns `false` for a curve the platform lacks (#180)
- **Every bounded cache evicts the least recently used entry** (#180)
- **Faster JSON models, XRPC calls, Jetstream parsing and CAR reading** — identifiers and datetimes are validated from UTF-8 without regular expressions, and their parts are built on first use; what is accepted and produced is unchanged (#190)

### Fixed

- **Rate-limit back-off follows the client's clock** — 429 retry delays ran on the system clock rather than the clock the client's session refresh uses (#187)
- **A scoped `IJtiReplayStore` no longer stops the host under scope validation** — the startup warning reads the registrations instead of resolving the store (#180)
- **`atproto-lexgen --version` reports the tool's real version** — instead of a hard-coded `1.0.0` (#110)
- **The managing-app check follows the Lexicon** — `checkUserAccess` sends `user` and `access` (it sent `did`, so every `ManagingAppPolicy` space refused everyone) and addresses the app's full service identifier (#113)
- **`notifyWrite` admitted any writer** — the authority now evaluates the write policy, and answers 403 to a refused writer, which it neither records nor forwards (#113)
- **Write notifications reach their subscribers** — an accepted `notifyWrite` is forwarded to the services registered with `registerNotify`, each addressed by the identifier it registered (#113)
- **Write notifications to an authority on an ordinary PDS were dropped** — `#atproto_space_host` falls back to `#atproto_pds`, and inbound notifications accept every way the authority is addressed (#113)
- **Malformed `simplespace` and `notifyWrite` input is refused up front** — a `rev` that is not a TID, a missing `hash`, or a `null` policy (#113)
- **`docs/testing-spaces.md` claimed there is no spaces container image** — the alpha is `ghcr.io/bluesky-social/atproto:pds-spaces-alpha`, pinned by digest on the page (#113)
- **MST root CIDs now match every other implementation** — null links, empty subtrees and incremental edits were encoded differently, so repos written with `MerkleSearchTree` were rejected elsewhere; roots are pinned against the reference vectors (#111)
- **`MerkleSearchTree.Deserialize` reads real repositories** — the `null` links other implementations write threw (#111)
- **DAG-CBOR sorts non-ASCII keys by their UTF-8 bytes** — keys of equal length were compared in UTF-16 order, which differs for characters above U+FFFF (#111)
- **`SpaceRepoCar.Verify` reports a malformed index block as `SpaceRepoVerificationException`** (#111)
- **`Tid.Next()` is monotonic and unique** — values minted in the same millisecond came out of order and could collide, `RecordKey.NewTid()` included (#114)
- **Identifier types accepted invalid values** — every line of the interop syntax fixtures now parses or fails as specified, and an invalid identifier in JSON fails as a `JsonException` with its path (#114)
- **One unknown embed or union variant no longer fails a whole page** — a single gallery post made timeline, feed, thread and search calls throw for the entire page (#115)
- **Signed labels deserialize** — `Label.Sig` did not read the `{"$bytes": …}` form (#115)
- **`UpdateProfileAsync` no longer deletes profile data** — it rebuilt the record from four fields; it now edits the stored record and writes with `swapRecord` (#115)
- **Unknown record fields survive a read-modify-write** (#115)
- **Registered union variants take effect** — `AtProtoJsonDefaults.Options` consults the registry (#115)
- **`$bytes` is unpadded base64, as the data model specifies** — Jetstream `sync` events and archive rows accepted only padded input, and `DagCborDecoder` wrote padding (#115, #126)
- **Malformed keys fail consistently** — a `did:key` or multikey that is not a compressed point throws `FormatException`, and an undefined `KeyCurve` throws rather than meaning K-256 (#112)
- **DPoP proofs are ES256 and carry no userinfo** — a K-256 key is refused rather than labelled ES256, and the generator and validator normalize `htu` the same way (#118)
- **Changing the service URL or re-applying a session on a used `HttpClient` works** — both wrote `BaseAddress`, which .NET forbids after the first request (#117)
- **`User-Agent` no longer grows on a shared `HttpClient`** — it is set per request (#117)
- **Query parameters and stamped timestamps are culture-invariant** — dates go out as ISO 8601 UTC and enums by their JSON names, and `createdAt` no longer follows the current culture's calendar (#117)
- **Uploads send from the stream's current position** — instead of rewinding it, and a non-seekable stream that needs a retry fails clearly (#117)
- **`RecordCollection<T>.ExistsAsync` answers `false` only for `RecordNotFound`** (#117)
- **A failed blob or repository download no longer leaks its HTTP response** (#117)
- **XRPC query parameters bind by their declared type** — a single value binds to a collection, and identifier parameters are validated by their parsers (#121)
- **Every XRPC endpoint failure answers with the XRPC error envelope** — `500 InternalServerError` without the exception's message, `413 PayloadTooLarge`, and `InvalidRequest` for a wrong `Content-Type` (#121)
- **Enumerators stop on a repeated cursor** — a server repeating its cursor drove an endless loop in the record, Spaces and Jetstream archive enumerators (#119)
- **Properties under names the Lexicons never used were always empty** — `canConvo`, `associatedChat`, `blobCids` and the `readOnly` filter (#123)
- **Calls that did not match their Lexicons** — the Ozone review queue called a nonexistent method, `SearchAccountsAsync` POSTed a query, and `SearchPostsAsync` and `ListNotificationsAsync` sent parameters the service refuses (#123)
- **Two procedures sent no body** — `DeactivateAccountAsync` and `UpdateAllReadAsync` now send their JSON input (#123)
- **Five chat methods and the chat export read the wrong response shape** (#123)
- **Seven permission-set constants named sets nobody published** (#123)
- **Sign-out revokes the session** — `LogoutAsync` sent `deleteSession` the access JWT, which the Lexicon refuses, and never revoked an OAuth session (#124)
- **A failed OAuth refresh is no longer stored as a token** — an error response became an empty access token (#124)
- **`ResumeSessionAsync` refreshes an expired session on a fresh client** (#124)
- **Installing a session replaces all of the previous one** — a password login after an OAuth session kept refreshing through OAuth, and raw `ServerClient` calls changed the transport's session (#124)
- **The client refreshes after `ExpiredToken` or a DPoP `invalid_token` challenge** — and resends once, only as the same account (#124)
- **Concurrent refreshes share one exchange** — single-use OAuth refresh tokens were spent more than once (#124)
- **App-password sessions refresh by the token's `exp`** — not on a fixed timer (#124)
- **`Dispose` no longer blocks waiting for a timer refresh** (#124)
- **A permanent reporter mute no longer fails a page of Ozone events** (#131)
- **DID resolution failures stay within the Spaces error contracts** — they escaped as `PlcException`, `JsonException` or `HttpRequestException`, and one failing subscriber could stop `deleteSpace` (#120)
- **DID documents that DID Core allows parse** — an `@context` given as a string, and a structured `serviceEndpoint` (#120)
- **Collection-filtered firehose consumers no longer replay history after a restart** — every event moves the cursor, and the final cursor is saved when enumeration ends (#126)
- **A Jetstream frame with a non-numeric `time_us` or `seq` is skipped** — instead of throwing out of the parser (#126)
- **Malformed `exp`, `iat` or `jti` answer 401, not 500** — tokens and DPoP proofs with out-of-range times or unusable `jti`s are refused as malformed (#125)
- **The OAuth callback completes after a restart or on another instance** — it no longer needs the process that started the login (#132)
- **Stored OAuth sessions refresh after a restart** — the client factory gets the login's `OAuthClient` from the options alone (#136)
- **`FeedView`, `PostCard`, `ProfileCard` and `ComposePost` render** — they injected a client nobody registered; `FeedView` reloads only when needed, and `ComposePost` counts graphemes (#136)
- **OAuth requests dispose their responses and read errors correctly** — a pushed request is retried only on `use_dpop_nonce`, and a non-JSON error is an `OAuthException` (#132)
- **The 0.4.0 notes claimed "Sync v1.1 support"** — they now say "Sync v1.1 message fields"; this release adds the verification (#134)
- **Indefinite-length CBOR is refused in CAR headers, commits and MST nodes** (#134)
- **`SpaceSyncer` verifies commits against the author's `#atproto` key** — not a `#atproto_space` key (#133)
- **Space type declarations refuse a wildcard collection clearly** — as proposal 0016 requires (#133)
- **`services.AddAtProto()` registers one configured `AtProtoClient`** — it handed out a new client for `bsky.social` on every resolution (#136)
- **The PDS health check reports its registered failure status, and registering it twice no longer breaks `/health`** (#136)

### Removed

- **`samples/BlazorOAuthSample`** — folded into `samples/ServerIntegrationSample`, which runs the same OAuth login and adds server-side access (#110)
- **`RedisSpaceReplayStore` and `AddAtProtoRedisSpaceReplayStore()`**, and with them the `StackExchange.Redis` dependency — `EfCoreJtiReplayStore<T>` remains the shared store. Migration: copy the short `RedisJtiReplayStore` from `docs/spaces.md` and register it as the `IJtiReplayStore` (#136)
- **`Microsoft.Extensions.Http.Resilience` and the Aspire client's resilience handler** — a retrying handler below the SDK resends `POST`s and spent DPoP proofs, and the SDK retries 429 itself. Migration: `RemoveAllResilienceHandlers()` on the SDK's clients under Aspire service defaults (#136)

### Security

- **The GitHub ↔ Forgejo sync workflows no longer run event text as shell** — issue, PR and comment fields reached `run:` scripts through `${{ }}`, so anyone on the public mirror could run commands with the sync tokens; they now pass through `env:`. **Rotate `FORGEJO_TOKEN` and `GH_MIRROR_TOKEN`** (#108)
- **Mirrored pull requests can no longer reach repository secrets** — `sync-prs.yml` fetches a PR into a bare repository instead of checking it out, and refuses to mirror a PR that changes `.forgejo/` or `.gitea/` (#108)
- **Crafted CAR and DAG-CBOR input no longer crashes the process** — a deeply nested CAR header overflowed the stack; nesting is capped and parsing uses `CborReader` (#111)
- **MST prefix lengths are range-checked** — a crafted node drove a huge allocation before any check (#111)
- **`CarReader` validates every length and CID it reads** — and rejects CIDv0 blocks, which bypassed the codec check (#111)
- **`MerkleSearchTree.Deserialize` rejects malformed trees** — invalid or out-of-order keys, shared subtrees and empty inner nodes (#111)
- **JWT and JWK base64url decoding is strict** — only the URL-safe alphabet, correct padding and zero trailing bits, so a token has one spelling (#118)
- **XRPC error bodies are read up to a cap** — a service could answer an error with a body of any size (#117)
- **One SSRF policy for every identity fetch** — `did:web`, PLC, handle and DNS-over-HTTPS requests are HTTPS-only, refuse private addresses after DNS, follow no redirects, cap bodies and time out; `IdentityResolverOptions.AllowPrivateNetworks` opts out (#120)
- **OAuth and the space server's outbound requests no longer reach private addresses** — metadata, pushed-authorization, token and revocation requests follow the fetch policy, as does the space server's client, and a URL breaking its rules is refused before sending (#120, #132)
- **Single-use space tokens could be replayed just after expiry** — every verifier now keeps the `jti` until the token stops being accepted (#125)
- **Identifiers no longer parse with a trailing line break** — `$`-anchored patterns accepted `did:plc:abc` followed by a line feed (#125)
- **A `did:web` document must name exactly the DID it was fetched for** — a case-insensitive comparison let one signer present several identities (#125)
- **OAuth refresh and code-exchange responses are checked against a freshly resolved issuer** — and the expected DID, the `atproto` scope and DPoP binding; tokens failing a check are revoked (#132)
- **Authorization server metadata is held to the AT Protocol profile** — exact issuer, HTTPS endpoints, required PAR and `iss`, and a PDS and authorization server that name each other (#132)
- **Anyone could block every OAuth login** — pending logins are limited per requester, with a total bound that displaces rather than refuses (#132)
- **Reading an authorization server's response is bounded** — `OAuthOptions.RequestTimeout` covers the whole exchange, and connection failures are `OAuthException`s (#132)
- **A login could be completed by anyone holding the callback URL** — `/atproto/login` binds the pending authorization to an HttpOnly cookie that the callback requires, and `returnUrl` must be a local path (#132)
- **A failed login no longer puts exception messages in the login URL** — the redirect carries an error code. Migration: a login page of your own reads `error` as a code (#136)
- **A service auth caller could drive an account's stored OAuth session** — the client factory and sign-out now honour only the OAuth login's identity (#136)
- **The OAuth callback re-checks the stored login state** — a return URL that is not local is dropped, and a malformed origin fails the login (#136)
- **`PostCard` links only `http` and `https` URLs** — a `javascript:` URL in an external embed ran script in the application's origin (#136)
- **An unredeemed relayed login is revoked** — tokens waiting on a loopback relay code are revoked when it expires or is evicted (#136)
- **The hosted login's own `HttpClient` never uses a proxy** — so the address the fetch policy checks is the real target; behind an egress proxy, configure the named client `AtProtoOAuthExtensions.HttpClientName` (#132)
- **Malformed `#atproto_space` and `#atproto_space_host` entries are errors, not fallbacks** — per proposal 0016, instead of silently using `#atproto` / `#atproto_pds` (#133)
- **Space tokens must name their key, and only that key is tried** — Migration: an authority signing with a dedicated key sets `SpaceServerOptions.CredentialKeyId = "#atproto_space"` (#133)
- **Space DPoP proofs are `ES256` only, and the exchange's proof carries no `ath`** — as proposal 0016 and the reference require (#133)
- **`SpaceRepoCar.Verify` checks what the reference checks** — exact index keys, DAG-CBOR links, and strict DAG-CBOR record blocks, so each value has one byte form (#133)
- **`SpaceSyncer.RecoverAsync` bounds a repo download** — `SpaceSyncer.MaxRepoSize` refuses a larger repository before anything is replaced (#133)
- **A signed commit with a malformed link no longer ends a Sync 1.1 consumer** — it is an invalid event (#180)

## [0.6.0] - 2026-08-21

### Breaking changes

- **`AtProtoScopes.Repo(...)` now throws `ArgumentException` for `RepoAction.None`** (Issue #94) — the scope grammar has no marker for an empty action list, so `RepoAction.None` previously emitted `repo:<nsid>`: a full create/update/delete grant, the opposite of what the caller asked for. Migration: drop the `repo:` scope entirely if no record writes are needed, or name the narrowest action that is (`Create`, `Update`, `Delete`). `RepoAction.All` and partial combinations are unaffected

### Added

- **Spaces: the permissioned data protocol** (Issue #89) — client-side support for [permissioned data](https://atproto.com/blog/atproto-spaces-alpha) ([proposal 0016](https://github.com/bluesky-social/proposals/tree/main/0016-permissioned-data)): the familiar AT Protocol shape — DID authority, per-user repos, Lexicon-typed records — behind an access perimeter called a **space**. This is an alpha proposal with no security review, and it provides **access control, not confidentiality**: the data is not end-to-end encrypted and every service handling it can read it
  - `SpaceUri` / `SpaceRecordUri` — `at://{authority}/space/{type}/{skey}[/{author}/{collection}/{rkey}]`. Authority splits in two: the URI's authority gates access, the record's author DID signed it. Neither may be a handle
  - `LtHash` — the homomorphic set hash a permissioned repo commits to in place of an MST root. Order-independent, so a write is one cheap pass over the lanes; verified lane-for-lane against the reference implementation
  - **BLAKE3 in XOF mode**, implemented from scratch since .NET ships none and the SDK carries no third-party cryptography. Checked against 27 reference test vectors
  - `SpaceRepoCommit` / `SignedSpaceCommit` / `SpaceCommitVerifier` — deliberately not a rebroadcastable proof: the signature covers only the commit context, and the digest is bound to it by a *symmetric* MAC, so a leaked commit proves nothing to a third party. Context encoding and MAC match the reference implementation byte-for-byte
  - `SpaceRepoCar` — the two-root CAR form `getRepo` serves (signed commit, then a DAG-CBOR path→CID index, then the record blocks). `Verify` authenticates the whole thing in one pass; `excludeValues` yields an index-only CAR for diffing against a local copy
  - `SpaceCredentialProvider` / `SpaceReader` / `SpaceTokens` — the credential exchange over two independent axes: *which user* (a delegation token from their PDS) and *which application* (a self-signed client attestation). A credential is DPoP-bound rather than a bearer token, since it reads a whole space and is presented to every host in it; cached per space and renewed ahead of expiry
  - `SpaceSyncer` / `SpaceRepoCursor` / `ISpaceRepoStore` — there is no relay for permissioned data, so an application pulls from each repo host directly. Comparing digests rather than tracking individual operations is what makes sync self-healing: dropped writes, compacted oplogs, and corrupted copies all surface as a mismatch and repair by full download
  - `com.atproto.space.*` on `AtProtoClient.Space` and `com.atproto.simplespace.*` on `AtProtoClient.SimpleSpace` — the full endpoint surface, plus the baseline space-management implementation every PDS must support. `listRepos` returns the **writer set** (accounts that have written), which is the sync boundary and not an access-control list; readers are never enumerated at the protocol level
  - `AtProtoScopes.Space(...)` with `SpaceAction` / `SpaceManage` — `space:` OAuth scopes granted by space *type*, `authority` defaulting to `self`. `Read` also confers `getDelegationToken` and therefore the whole space, while `ReadSelf` reaches only the holder's own repo — the right grant for an export tool
  - `SpaceAuthority` (resolves `#atproto_space` / `#atproto_space_host` with fallbacks, so any ordinary account works as an authority), `SpaceTypeDeclaration`, and `LexBytesJsonConverter` for the `{"$bytes": "…"}` wrapper
  - `docs/spaces.md`, `samples/SpacesSample`, and 212 unit tests pinning every cryptographic construction against the reference implementation's own outputs

- **Spaces: server-side support in `ATProtoNet.Server`** (Issue #91) — the other half of #89: the ASP.NET Core layer that lets a .NET service act as a **space authority**, a **repo host**, or both. Registered with `AddAtProtoSpaces()` plus `AddSpaceAuthority<T>(key)` / `AddSimpleSpace<T>()` / `AddSpaceRepoHost<T>()`, mapped by the ordinary `MapXrpcEndpoints()`. The three register separately so a service that only needs to *verify* takes `AddAtProtoSpaces()` alone
  - `DPoPProofValidator` — six checks: signature against the proof's own `jwk`, that key's thumbprint against the credential's `cnf.jkt`, `ath` against the credential presented, `htm`/`htu` against the request as received, a recent `iat`, and an unseen `jti`. `htu` is compared with query and fragment stripped per RFC 9449 §4.3; a non-absolute `htu` is refused rather than string-compared, and `alg` is pinned to the key's curve
  - `SpaceDelegationTokenVerifier` — `aud` must equal `spaceHostAud(spaceDid)` for the authority in the token's **own** `sub`, so a token minted for another authority cannot be presented here. The `jti` is consumed last, only after every other check passes
  - `SpaceCredentialVerifier` — the signer is resolved from the **space URI**, not the credential's `iss`, so only a space's own authority can mint credentials for it
  - `SpaceClientAttestationVerifier` — resolves `client_id` to its `client-metadata.json`, follows `jwks_uri`, and verifies against the key the `kid` names (trying every published key would let one compromised key be laundered through another). The fetch is `https`-only and bounded by `MaxClientMetadataBytes`
  - `ISpaceReplayStore` — single-use enforcement keyed on `(iss, jti, exp)`, with an in-process default. **Replace it in a multi-instance deployment**, or a replay is caught only by the instance that saw the original
  - `SpaceServerOptions.MaxSingleUseTokenLifetime` (default five minutes) caps how far ahead an inbound single-use token's `exp` may sit, bounding both replay window and replay-store occupancy. `SpaceServerOptions.PublicBaseUrl` is **not optional behind a reverse proxy**, since DPoP `htu` is compared against the request as received
  - The endpoint surface: `getSpaceCredential`, `listRepos`, `registerNotify`, `unregisterNotify`, `notifyWrite` (authority side); `getRecord`, `listRecords`, `getLatestCommit`, `getRepo`, `listRepoOps`, `getBlob`, `listBlobs` (repo side, over an `ISpaceRepoHost`). `RepoNotFound` deliberately does not distinguish a silent member from a non-member — saying more would leak membership
  - `com.atproto.simplespace.*` — all seven administration methods over an `ISimpleSpaceStore`, plus `SimpleSpaceAccessPolicy`: a user perimeter (`MemberListPolicy` / `PublicPolicy` / `ManagingAppPolicy`) and an app perimeter (`OpenAppAccess` / `AllowListAppAccess`, evaluated against the *attested* client ID). A `ManagingAppPolicy` whose app is unreachable refuses, since failing open would turn an outage into an open space
  - `SpaceWriteNotifier` — fans `notifyWrite` / `notifySpaceDeleted` out with service auth, best-effort by design because the syncer's `listRepos` sweep is the correctness guarantee. `EnsureAuthoritySubscribedAsync` auto-registers a space's authority on first write, which is what populates the writer set
  - Key resolution reads every verification-method type the SDK understands (see #98), so an authority is not accepted or refused based on whether a token carried a `kid`
  - 105 new unit tests including a `TestServer` end-to-end pass; `docs/spaces.md` gains a *Serving a space* section

- **Spaces: durable and multi-instance implementations of the space server stores** (Issue #102) — #91 shipped all three stores in-memory, which is right for a test host and wrong for anything else
  - `RedisSpaceReplayStore` — the replay store was a *correctness* gap across instances, not a durability one: two replicas behind a load balancer each accepted the same delegation token. Consuming a token is one atomic `SET key value NX EX ttl`, with the entry's TTL being the token's own remaining lifetime. Registered with `AddAtProtoRedisSpaceReplayStore()`; adds a `StackExchange.Redis` dependency to `ATProtoNet.Server`
  - `EfCoreSimpleSpaceStore<TContext>` — a member list is the one piece of space state that cannot be rebuilt, so a restart that lost it lost the space's access control. Policy unions are stored as their Lexicon JSON, so a new variant needs no schema change
  - `EfCoreSpaceAuthorityStore<TContext>` — writer set and notification registrations, with `DeclareSpaceAsync` / `MarkDeletedAsync`. Pagination is by DID and evaluated by the database, so ordering and cursor comparison agree under any collation. A writer's revision only moves forward: revisions are compared ordinally in-process, never by the column's collation (under which two TIDs can sort the other way), and advanced by an `UPDATE` conditioned on the revision just read, so an out-of-order `notifyWrite` never overwrites a newer one
  - `EfCoreSpaceReplayStore<TContext>` — for deployments with no Redis; the primary key *is* the replay check. A failed save is confirmed against the table before being reported as a replay, so a storage fault does not masquerade as one. Expired rows are swept opportunistically, at most once a minute
  - All four take an `IDbContextFactory<TContext>` and sit in `ATProtoNet.Server.EntityFrameworkCore` alongside the token store. Use `SpaceDbContext` or call `SpaceDbContext.ConfigureSpaceModel()` from your own context
  - `AddAtProtoSpaces()` now warns at startup while the replay and `simplespace` stores are in-process defaults; suppress with `SpaceServerOptions.WarnOnInMemoryStores = false`

- **Spaces: integration tests against a real permissioned-data PDS** (Issue #93) — #89's 212 unit tests all stubbed the HTTP layer, proving the SDK agrees with a *reading* of the spec, not that a server accepts what it sends. 26 tests in `tests/ATProtoNet.IntegrationTests/` now talk to a live space host behind a `[RequiresSpacesFact]` gate (`ATPROTO_TEST_SPACES=true`), so CI is unaffected
  - `SpaceNetworkFixture` provisions three accounts (authority, member, outsider) through the admin API, since a space is a three-party arrangement a stub cannot tell apart. `ATPROTO_PLC_URL` points DID resolution at the test network's own directory
  - `SpaceCredentialTests` — the two-hop exchange end to end, then the refusals: replayed token, wrong space, wrong key, wrong host, credential presented as a bearer token, and `SpaceDeleted` on renewal
  - `SpaceRepoSyncTests` — the CAR round trip verified against a real server's own commit and index, plus incremental sync, cursor resumption, divergence detection, and full-recovery fallback
  - `SimpleSpacePolicyTests` — non-member refusal, `#allowList` refusal, attestation retry, revocation at renewal, and the repo boundary between two accounts on one host
  - `docs/testing-spaces.md` covers standing a host up. No PDS release serves `com.atproto.space.*` yet — it lives on [bluesky-social/atproto#5187](https://github.com/bluesky-social/atproto/pull/5187), which these tests were run against

- **`atproto-lexgen` understands `"type": "space"` Lexicon definitions** (Issue #92) — previously a space-type Lexicon produced an empty file and no diagnostic
  - **JSON → C#**: a `space` definition emits a static holder (`com.atmoboards.forum` → `ForumSpace`) exposing `Nsid`, a `SpaceTypeDeclaration Declaration`, and `Key` / `Name` / `LocalizedNames` / `Collections` forwarders. A declaration missing a required field still emits compiling code and says what it substituted
  - **C# → JSON**: `atproto-lexgen lexicon` emits a `space` definition for every static `SpaceTypeDeclaration`, taking the NSID from a sibling `Nsid` constant, so declarations round-trip. Ambiguous or unattributable declarations are reported through the new `LexiconEmitter.Warnings`
  - **Diffing**: `atproto-lexgen diff` compares declarations. Because a bare `space:` grant resolves its collection set when the grant is *evaluated*, adding a collection widens every existing grant (reported) and removing one narrows them (breaking); key changes are breaking, `name` changes are not
  - An unrecognized definition type is now a `WARN` naming the NSID and type instead of an empty file

- **XRPC handler routing: named errors and binary responses** (Issue #91) — `XrpcException(error, message, statusCode)` thrown from a handler is written as the `{"error", "message"}` body XRPC clients branch on rather than escaping as a 500, and carries a `Headers` dictionary for things like `WWW-Authenticate`. `IXrpcBlobQuery<TParams>` is the counterpart of `IXrpcQuery<,>` for non-JSON output encodings (`getBlob`, `getRepo`, the CAR methods), streaming rather than buffering. Query-parameter binding failures now answer `InvalidRequest`, and `XrpcBooleanConverter` binds Lexicon `boolean` parameters

- **Jetstream v2 is now supported alongside v1** (Issue #83) — the second wire protocol (atproto proposal 0015) serves at `/xrpc/network.bsky.jetstream.subscribeEvents` and differs from v1 in nearly every particular: a self-describing envelope, flat commit fields, `collections`/`dids`/`kinds` filters, a sequence-number cursor, a `sync` event kind, and out-of-band `#info`/`error` frames. `JetstreamClient` and `JetstreamConsumer` speak both, selected by `JetstreamConsumerOptions.Protocol`
  - `Protocol` defaults to `JetstreamProtocol.V1`, so existing configurations are unchanged. `JetstreamEndpoints` names the hosts: `UsEast` / `UsWest` (v2) and `LegacyUsEast1` / `LegacyUsEast2` / `LegacyUsWest1` / `LegacyUsWest2`
  - `WantedKinds` — the v2 `kinds` filter. A collection filter constrains *commit* events only, so a commits-only stream needs `WantedKinds = [JetstreamEventKind.Commit]`; combining `WantedCollections` with a `WantedKinds` that excludes `Commit` now throws before the socket opens
  - `JetstreamSyncEvent` — a repo resynchronization marker (v2 only); handle it as you would an account deletion
  - `JetstreamEvent.Cursor` — the sequence number and v2 resume position, unaffected by operator timestamp imports. `JetstreamEvent.Timestamp` exposes `TimeUs` as a `DateTimeOffset`
  - Cursor handling follows the protocol: v2 cursors replay *inclusively*, so `ReconnectRewind` is ignored, and an event with no sequence number is not persisted
  - A rejected subscription is no longer retried in a loop — v2 validates before the WebSocket upgrade, and `JetstreamConnectException` (with `StatusCode` / `IsRetryable`) surfaces `CursorTooOld`, `UnknownZstdDictionary`, and malformed filters. The consumer persists progress and rethrows rather than silently skipping the gap
  - `OnInfo` / `OnStreamError` for v2's advisory and terminal frames; `JetstreamDictionaryClient` fetches the versioned zstd dictionary and reads its ID from the dictionary's own header. Setting only one of `ZstdDictionaryId` / `Decompressor` now throws
  - `JetstreamEventParser.ParseFrame(json, protocol)` returns a `JetstreamFrame`; the existing `Parse(...)` overloads still read v1. Both wires stay forward-tolerant
  - `JetstreamV2Tests` checks the protocol against a live instance, gated by `[RequiresJetstreamFact]` (`ATPROTO_TEST_JETSTREAM=true`); needs no PDS and no credentials. Documented in `docs/jetstream.md`

- **Jetstream v2 archive: replay and snapshot** (Issue #85) — the HTTP archive alongside the live tail, so an indexer gets *the records that already exist* and *every new one* with no gap at the seam. `JetstreamReplayConsumer.ReplayAsync()` backfills history and cuts over into the live tail in a single `await foreach`
  - `JetstreamSegmentReader` — decoder for the sealed segment format (`.jss`): fixed header, length-prefixed zstd frames, columnar block body. `ReadRowsAsync` / `ReadEventsAsync` stream block by block rather than materializing segments that run to hundreds of megabytes. `JetstreamArchiveRow` exposes the untouched CBOR so a mirror stays byte-auditable; `ToEvent()` projects to the live tail's `JetstreamEvent`. Verified against Jetstream's own golden fixtures
  - `JetstreamArchiveClient` — typed wrappers for `planSnapshot`, `listSegments`, `getSegment`, `getBlock` with bearer auth, `Range`, and ETags. The endpoints are metered in **response bytes**, so a `429` waits out exactly its `Retry-After` and `DownloadSegmentAsync` resumes from the byte offset it stopped at. `JetstreamArchiveException` carries `StatusCode` / `Error` / `RetryAfter` / `IsRetryable`, so a revoked key is not retried
  - **The plan loop pins the tip**: the first `planSnapshot`'s `sealedTipSeq` is the ceiling for the whole backfill, so the range cannot float mid-download. A page that fails to advance while the ceiling is ahead is re-planned with backoff `MaxStalledPlanAttempts` times (default 5) and then fails, rather than leaving a permanent silent gap. Downloads run `DownloadParallelism` deep but decode in plan order; since the planner works from bloom filters, the exact filters are re-applied to what was decoded
  - **The cutover is inclusive and deduplicated.** If the backfill outran the socket's 36-hour lookback, the refused connect sends the consumer back into the plan loop (up to `MaxCutoverAttempts`) rather than skipping the gap
  - `JetstreamConsumerOptions.Archive` (new `JetstreamArchiveOptions`) configures it all; filters and `CursorStore` are shared with the live tail, so one store spans both phases and a restart resumes the backfill
  - `IJetstreamBlockDecompressor` is a separate seam from `IJetstreamDecompressor` on purpose: segment blocks carry **no dictionary**. The SDK still bundles no zstd. Segment checksums are exposed rather than recomputed (they are xxh3 metadata checksums, and a compaction rewrites them)
  - `JetstreamArchiveTests` gated by `[RequiresJetstreamArchiveFact]` (`ATPROTO_JETSTREAM_API_KEY`), deliberately small since the endpoints bill real bytes. New `samples/JetstreamReplaySample`

- **`JetstreamEventParser.ParseFrame(ReadOnlyMemory<byte>, JetstreamProtocol)`** (Issue #87) — a no-copy counterpart to the span overload for callers already holding the frame on the heap. `JsonDocument` cannot parse a span without copying it; the memory overload reads in place, taking ~600 bytes per event off `JetstreamClient`'s receive loop

- **Tranquil PDS is now supported alongside the reference Bluesky PDS** (Issue #78) — [Tranquil](https://tangled.org/tranquil.farm/tranquil-pds) is a community PDS (single Rust binary; passkeys, 2FA, SSO, `did:web` accounts, granular OAuth scopes, a web UI) and a superset of the reference server
  - `AddAtProtoTranquilPds(name, port?, tag?)` (`ATProtoNet.Aspire.Hosting`) — adds the container plus a `{name}-postgres` server and `{name}-db` database, since Tranquil keeps repositories in PostgreSQL. `WithDatabase(database)` / `WithDatabaseUrl(url)` point it at an existing one and drop the generated resources. Tranquil is handed a `postgres://` URI, not an ADO.NET connection string
  - `WithAtProtoTranquilPds(pds)` wires a project to the container exactly as `WithAtProtoPds` does for the reference server
  - Configuration methods on `AtProtoTranquilPdsContainerResource`: `WithAdminAccount`, `WithDevelopmentMode`, `WithHostname`, `WithHandleDomains`, `WithJwtSecret`, `WithDPoPSecret`, `WithMasterKey`, `WithPlcRecoveryKey`, `WithBlobVolume`, `WithBlobBindMount`, `WithS3BlobStorage`, `WithPlcUrl`, `WithCrawlers`, `WithReportService`, `WithBlobUploadLimit`, `WithInviteCodeRequired`, `WithEmail`. `WithPlcRecoveryKey` takes a *public* `did:key` rather than the reference server's hex private key, because Tranquil adds it only to the rotation keys
  - **`PdsAdminClient` can authenticate as an administrator account**, not just with a server-wide admin password: `PdsAdminOptions.Authentication` (new `PdsAdminAuthentication` enum, default `AdminPassword`) and `PdsAdminOptions.AdminIdentifier`. Under `AdminAccount` the client signs in lazily on first use, reuses the session, and re-authenticates once if the server later rejects it. New `EnsureAdminSessionAsync()` does the same for callers using the raw `Admin` / `Server` clients
  - `AddAtProtoPdsAdmin()` binds the two new configuration keys and fails at host build time when `AdminAccount` is selected without an identifier
  - **The administrator account is not created for you, by design** — Tranquil flags the first account on an empty instance as admin, so the application creates it once with `PdsAdminClient.CreateAccountAsync`. The handle defaults to `pdsadmin.{hostname}` (not `admin.`, which Tranquil reserves)
  - Running locally the container gets the relaxations a development instance needs (`INVITE_CODE_REQUIRED=false`, `DISABLE_ACCOUNT_VERIFICATION_GATE=true`, rate limiting off, `SERVER_HOST=[::]`) and none of them when publishing; without them no account could be created or signed in. `WithDevelopmentMode(false)` turns the set off
  - Secrets are generated at 48 alphanumeric characters and persisted to user secrets locally; unlike the reference PDS's hex secrets these are shapes an Aspire manifest `generate` block can describe, so a published deployment produces its own
  - Documented in `docs/managed-pds.md`, `docs/aspire.md`, `docs/api-reference.md`, `docs/architecture.md`

- **The core package's public API is now fully XML-documented** (Issue #72) — `ATProtoNet` emitted 1114 CS1591 warnings, so most of the public surface arrived in IntelliSense with an empty tooltip. All 1114 members now carry a `<summary>`. Insert-only apart from one `cref` fix

### Changed

- **Performance and memory pass over the streaming, repository, and CID hot paths** (Issue #87) — no public behaviour changes. Measured on a 4693-byte `#commit` frame, a 1000-entry MST, and a 1000-block CAR, tiered compilation disabled (median of three runs):

  | Operation | Time before → after | Allocated before → after |
  | --- | --- | --- |
  | `FirehoseEventParser.Parse` (one `#commit`) | 147 µs → 22 µs (**6.6×**) | 102 KB → 29 KB (**3.5×**) |
  | `MerkleSearchTree.Create` (1000 entries) | 3.44 ms → 0.61 ms (**5.7×**) | 743 KB → 161 KB (**4.6×**) |
  | `MerkleSearchTree.Get` × 1000 | 3.10 ms → 0.10 ms (**31×**) | 362 KB → **0** |
  | `CarReader.FindBlock` × 1000 | 6.10 ms → 0.03 ms (**187×**) | 40 KB → **0** |
  | `CidComputation.EncodeCidToString` | 88 ns → 67 ns (**1.3×**) | 432 B → 144 B (**3×**) |
  | `MerkleSearchTree.Serialize` (1000 entries) | 961 µs → 903 µs | 1348 KB → 1190 KB (**1.13×**) |

  - `FirehoseEventParser` transcodes DAG-CBOR straight into the JSON the models bind from, replacing five passes over every frame (and a deep clone at every nesting level) with one. The CAR blob never becomes a UTF-16 string
  - `CarReader.FindBlock` builds a deferred CID index on first use instead of scanning every block, which made a repo walk quadratic
  - `MerkleSearchTree.Get` / `TryUpdate` no longer call `List.IndexOf` on the entry they are iterating, and `Get` / `Remove` no longer compute an unused SHA-256 per node visited; lookups now allocate nothing
  - `MerkleSearchTree.Create` hashes each key once instead of once per layer, and partitions on index bounds rather than copying entry ranges into fresh lists
  - `CidComputation.EncodeCidToString` builds the string in one pass instead of three allocations; `CarBlock.CidHex` uses `Convert.ToHexStringLower`
  - The `FirehoseClient` / `JetstreamClient` WebSocket read loops take a single-receive fast path instead of staging every message through a `MemoryStream`; multi-frame messages still spool
  - `CarReader.FromStreamAsync` parses the buffer it already filled instead of copying the whole CAR onto the large object heap; `FirehoseVerifier.ExtractSignedView` writes into one exact-sized array (`WriteMapHeader` now takes a `Span<byte>`, alongside a new `MapHeaderLength`)
  - `AtProtoJsonDefaults.Options` is initialized by the type initializer rather than a racy `??=`, which could give concurrent first calls their own reflection-derived contract cache. It stays mutable until first use
  - `XrpcClient` joins the `atproto-accept-labelers` header once when the subscription is set; `DagCborEncoder` counts arrays without materializing a list and sorts keys in place; `Did.Method` slices instead of `Split(':')`
- **Codebase cleanup sweep: 323 net lines removed from `src/`**, no intended behaviour change beyond the fixes listed below — Lexicon clients build query parameters with a new internal `XrpcParams` builder at 78 call sites instead of hand-rolled dictionaries (this is what fixes the comma-joining bug); `XrpcClient`'s eight near-identical overloads delegate to one `SendAsync` pair; `RecordCollection`'s `*`/`*From` pairs no longer duplicate bodies; `AtProtoClient`'s four delete-by-AT-URI bodies, two relay-URL guards, and three `new Session { … }` blocks collapse to helpers. Dead code removed: `XrpcQueryBuilder.BuildQueryString`, its `ToDictionary` alias, `ModerationClient.AddListParams`, and an unreachable `AdminClient` list
- **`Session.With(...)` added** — returns a copy with selected fields replaced, so a token rotation need not restate nine untouched fields. Additive
- **`AtProtoHttpException.ResponseBody` is now `init`-settable** — additive; no existing signature changed
- **21 Lexicon client classes no longer take an `ILogger`** they never wrote to. Their constructors are `internal`, so this is not a public API change; `ServerClient` and `PdsAdminClient`, which do log, are unchanged
- **`LoginForm` default copy now says "username" instead of "handle"** (Issue #80) — the field label defaults to `"Username"` and the hint to `"Your Atmosphere account username — your PDS is detected automatically."`. Same split as Issue #32: the community's word in the copy, the protocol's word in the code. Nothing below the copy changed (`HandleLabel` / `HandlePlaceholder` / `HandleHint`, the `handle` parameter, the `atproto-handle` input id are all as they were), so this is not source- or binary-breaking. Tests asserting on the old strings must update or pass explicit values
- **`ATProtoNet.Aspire.Hosting` now depends on `Aspire.Hosting.PostgreSQL`** (Issue #78) — Tranquil PDS does not run without PostgreSQL. AppHosts using only `AddAtProtoPds` are unaffected apart from the extra restore
- **CS1591 is now a build error on all four documented packages** (Issue #72), so a new undocumented public member fails the build. `TreatWarningsAsErrors` stays `false`
- **All NuGet dependencies updated to their latest stable versions** (Issue #73) — `--outdated`, `--deprecated`, and `--vulnerable --include-transitive` all come back empty. Notably `System.Formats.Cbor` 9.0.4 → 10.0.10, moving the last package off the .NET 9 line, plus the `Microsoft.Extensions.*`, EF Core, and resilience packages. No source change was needed
- **Test suite migrated from `xunit` 2.9.3 to `xunit.v3` 3.2.2** (Issue #73) — `xunit` 2.x is deprecated on nuget.org with no non-deprecated 2.x release, so a version bump could not clear it. The migration was small: four `IAsyncLifetime` implementations return `ValueTask`, and five custom `Fact`/`Theory` subclasses forward `[CallerFilePath]`/`[CallerLineNumber]`. No test was rewritten. xUnit1051 is `NoWarn`ed with a comment, since adopting `TestContext.Current.CancellationToken` at ~260 call sites belongs in its own change
- **CI actions updated** (Issue #73) — the GitHub mirror workflows move to `actions/checkout@v7` and `actions/setup-dotnet@v6`. The Forgejo workflows pin no actions

### Fixed

- **A space created through `com.atproto.simplespace` is now one its authority answers for** (Issue #105) — `ISimpleSpaceStore` and `ISpaceAuthorityStore` held separate state and nothing bridged them, so a service registered the documented way minted credentials correctly but refused every write notification for its own spaces with `SpaceNotFound`. The writer set could therefore never be populated, and since it is the sync boundary, no syncer could find anything. `deleteSpace` had the mirror gap: `listRepos` kept returning the writer set instead of `SpaceDeleted`. `AddSpaceAuthority<T>()` now wraps the store in the new `SimpleSpaceAuthorityStore` whenever an `ISimpleSpaceStore` is registered, in either order. Existence and deletion are *read* from the space-management store rather than copied, so there is no second write to keep in step. Spaces the `simplespace` store has never heard of fall through to the inner store
- **`SpaceSyncer` reported `Partial` forever for a member who had never written to the space** (Issue #99) — a missing commit was read as "the page stopped short of the head", but an account with no repo state produces one too, so the pass applied nothing, advanced nothing, and reported the outcome documented as "sync again to continue". `Partial` is now reported only when the pass has somewhere left to go; a page with neither operations nor a cursor is `SpaceSyncOutcome.NoRepo`. A cursor already standing at a revision takes the existing repair path instead. No signature changed and no enum member was added
- **DID document signing keys are read from the legacy verification-method types too** (Issue #98) — both places the SDK pulled a signing key required `type == "Multikey"`, so against a document publishing `EcdsaSecp256k1VerificationKey2019` / `EcdsaSecp256r1VerificationKey2019` the key came back absent: no permissioned commit could be verified, and `FirehoseVerifier` treated every commit from such an account as unverifiable. The encodings differ by more than the type string (multicodec-tagged compressed bytes vs a bare uncompressed point), so the point is now compressed and re-tagged with the curve the type names. plc.directory serves `Multikey`, so production `did:plc` was never affected; `dev-env` networks and hand-written `did:web` documents publish the legacy form. New public API: `VerificationMethod.ToDidKey()`, `DidDocument.GetSigningKey()`, `DidDocument.GetVerificationKey(fragment)`, `AtProtoCrypto.FormatDidKey(...)`, `AtProtoCrypto.CompressPublicKey(...)`
- **DAG-CBOR map keys were sorted bytewise rather than length-first** — DRISL orders keys by length and only then by bytes, so `{"b":…,"ab":…}` must encode `b` first. Every CID the SDK computed for a record whose keys spanned more than one length was therefore wrong — including any `app.bsky.feed.post` carrying both `text` and `createdAt` — so it would not match the CID the rest of the network computed. `DagCborDecoder`'s matching validation would have rejected valid blocks. Regression-pinned against a post fetched from a live PDS
- **DPoP proofs named the full request URL in `htu`** — RFC 9449 §4.2 requires query and fragment stripped, so every proof for a request carrying a query string — which is every XRPC query — named an `htu` no conforming resource server would match. Now normalized to origin plus path, which also means one proof covers any query on a path
- **XRPC array query parameters are now sent as repeated keys instead of one comma-joined value** — 21 call sites built `?dids=a,b` where XRPC specifies `?dids=a&dids=b`, so any endpoint taking an array silently saw one malformed element. Affected `getPosts`, `getFeedGenerators`, `getProfiles`, `getServices`, `getRelationships`, `getStarterPacks`, `getAccountInfos`, `queryLabels`, `getConvoForMembers`, `getConvoAvailability`, `searchAccounts`, and the eight array filters on `queryEvents` / `querySubjects`. Callers passing a single element are unaffected
- **`RecordCollection` list operations no longer hand back records whose `Value` is null** — `ListAsync`/`ListFromAsync` suppressed a failed deserialization with `value!`, so an entry not matching `T` violated its own `required` contract and threw a `NullReferenceException` downstream. They now throw `InvalidOperationException` naming the record URI and target type, as `GetAsync` already did
- **A failed per-request client build no longer leaks an ECDSA key handle** — `AtProtoClientFactory.CreateClientForUserAsync` built the client and DPoP session before `ApplyOAuthSessionAsync` took ownership; if that threw, the native key handle survived until finalization. Both are now disposed on the failure path
- **`FileAtProtoTokenStore` writes are atomic and deletes are serialized against them** — `StoreAsync` wrote in place, so a crash mid-write could truncate the file and lose the refresh token; it now writes a temp file and moves it. `RemoveAsync` now takes the same lock as writes, so a logout racing a rotation cannot leave the just-written file behind. Token files are created owner-only on Unix
- **XRPC error responses that are not an XRPC error envelope now keep their body** — the fallback `AtProtoHttpException` discarded text it had already read, leaving `ResponseBody` null exactly when it was most useful. The parse failure is also no longer caught with a bare `catch (Exception)`
- **Non-integral query parameters are formatted with the invariant culture** — a client under a culture such as `de-DE` no longer sends `1,5` where the server expects `1.5`
- **`Cid.Parse`/`TryParse` documentation matches what they do** — both were described as validating, and the type carried a `GeneratedRegex` nothing called. The dead regex is gone and the summaries say plainly that only null/blank input is rejected
- **`AtProtoClientFactory` no longer claims a refresh that does not happen** — a comment described on-demand refresh in `XrpcClient` that does not exist, so per-request clients silently never refreshed. The comment now states the actual contract
- **`README.md` and `docs/` audited against the actual public API** (Issue #74) — every type, method, parameter, and constant named in the documentation was cross-checked against `src/` and `tools/`. The larger corrections: `docs/crypto.md` documented an `AtProtoKey.Generate` / `EncodeMultikey` / `GenerateDidKey` / `Base58Encode` surface that does not exist, a static `ServiceAuthGenerator.CreateToken`, and an MST taking string CIDs; `docs/standard-site.md` omitted the repository argument every `StandardSiteClient` method takes and used record fields that were never in the Lexicon models; `docs/ozone.md` named moderation events `ModerationEvent*` instead of `ModEvent*`. Smaller fixes across `api-reference.md`, `did-resolution.md`, `firehose.md`, `identity-types.md`, `blob-upload.md`, `video.md`, `labeler.md`, `batch-operations.md`, `server.md`, `aspnet-core.md`, `managed-pds.md`, `oauth.md`, and `lexicon-codegen.md`
- **Documentation added for the 0.5.0 repository-authoring APIs** (Issue #74) — `CarWriter`, `RepoCommit`/`SignedRepoCommit`, `PlcOperationBuilder`, `MerkleSearchTree.SerializeProof`, `Tid.FromInt64`/`ToInt64`, `CidComputation.TryDecodeCidString`, `DidDocument.Context`, and `XrpcClient.SetAdminCredentials` shipped without prose documentation and are now covered. `docs/architecture.md` also claimed five runtime packages (there are four) and that only the core project generates documentation (all four do)
- **The solution now builds with zero warnings** (Issue #72) — beyond CS1591: a missing `<param>` on `XrpcClient.SendWithDPoPRetryAsync` (CS1573), an ambiguous `<see cref>` in `PlcOperationBuilder` (CS0419), and a missing `@using` in `ServerIntegrationSample` (RZ10012)

## [0.5.0] - 2026-07-26

### Breaking changes

- **`ATProtoNet.Pds` package removed** — the in-process PDS implementation is gone; this project does not maintain a PDS. Use `ATProtoNet.Aspire.Hosting` to run the official Bluesky PDS container and the new `PdsAdminClient` to administer it (see `docs/managed-pds.md`). There is no in-process replacement for `AddAtProtoPds()` / `MapAtProtoPds()`, `IAccountStore`, `IRepoStore`, `IRepoCommitStore`, `IInviteCodeStore`, `PdsService`, or the EF Core stores. The published set drops from 6 packages to 5
- **`ATProtoNet.Aspire.Hosting` now targets Aspire 13** — `Aspire.Hosting` 9.5.2 → 13.4.6. AppHosts referencing it must be on Aspire 13
- **`AtProtoPdsContainerResource` constructor gained three `ParameterResource` arguments** — `(name, adminPassword, jwtSecret, plcRotationKey)`. Source- and binary-breaking for direct construction; `AddAtProtoPds()` is unaffected
- **`ATProtoNet.Server.EntityFrameworkCore` package merged into `ATProtoNet.Server`** (Issue #33) — remove the `<PackageReference>`; the namespace, `AddAtProtoEfCoreTokenStore<TContext>()`, `AtProtoTokenDbContext`, and `AtProtoTokenEntity` are unchanged, so only the package reference changes. `ATProtoNet.Server` now transitively depends on `Microsoft.EntityFrameworkCore.Relational`
- **`ATProtoNet.Aspire` package merged into `ATProtoNet.Server`** (Issue #33) — remove the `<PackageReference>`; the `ATProtoNet.Aspire` namespace, `AddAtProtoClient(...)`, `AtProtoClientSettings`, and `AtProtoPdsHealthCheck` are unchanged. `ATProtoNet.Server` now depends on `Microsoft.Extensions.Http.Resilience`

### Added

- **Managed PDS** — run the official Bluesky PDS as a container your app owns, and administer it from .NET. The reference implementation does the serving, ATProto.NET does the orchestration
  - `PdsAdminClient` (namespace `ATProtoNet.Admin`) — administers any PDS you hold the admin password for. `CreateAccountAsync` calls `describeServer`, mints an invite code when the server requires one, then signs the account up — so an app can provision accounts on its own server. The signup call is sent unauthenticated, so the admin password never reaches a public endpoint. Also wraps invite codes, account lookup, takedown/restore, handle/email/password updates, and deletion. The constructor rejects a non-loopback `http://` URL
  - `PdsAdminOptions.AllowInsecureHttp` — opt-in for plaintext HTTP at a non-loopback host (the shape an Aspire container network produces). Default `false`, and it validates the *effective* base address, so a supplied `HttpClient` cannot slip past it
  - `AddAtProtoPdsAdmin()` (`ATProtoNet.Server`) — registers `PdsAdminClient` as a typed `HttpClient` binding the Aspire-supplied configuration keys. Missing configuration throws while the host is built, naming the key. Typed rather than a captured singleton, so the handler rotates and a long-running deployment picks up DNS changes
  - `WithAtProtoPds(pds)` (`ATProtoNet.Aspire.Hosting`) — wires a project to the container in one call: reference, configuration keys, and `WaitFor` on the health check. In run mode it also sets `AllowInsecureHttp`, since a containerized consumer resolves the PDS over the container network; it does **not** when publishing
  - `WithHandleDomains`, `WithInviteCodeRequired`, `WithAdminPassword`, `WithJwtSecret`, `WithPlcRotationKey`, `WithDataBindMount`, plus `AdminPasswordParameter` / `JwtSecretParameter` / `PlcRotationKeyParameter` on the resource
  - The container now gets an HTTP health check on `/xrpc/_health` so `WaitFor(pds)` works, and `PDS_HOSTNAME` defaults to `localhost`
  - `XrpcClient.SetAdminCredentials(password, user = "admin")` / `ClearAdminCredentials()` / `HasAdminCredentials` — HTTP Basic admin auth on the low-level client; a session token still takes priority
  - `WithHostname` gained a `ParameterResource` overload. Locally it defaults to `localhost`; when publishing `AddAtProtoPds` creates a `{name}-hostname` parameter, since the hostname fixes the server's `did:web` identity and a PDS deployed as `localhost` would issue unresolvable identities. `PDS_DEV_MODE=true` is likewise set only when running locally
  - **Overriding a generated parameter now removes it from the application model** — a superseded parameter previously stayed in the model and appeared in a published manifest, prompting a deployment for a secret nothing would read
  - **CI now runs both untested seams**: a `pds-integration` job runs `PdsAdminTests` against the real PDS container, and a second step publishes the AppHost sample's manifest and asserts on it. Both set `ATPROTO_REQUIRE_INTEGRATION=1`, which turns a skipped gate into a failure — `dotnet test --filter` exits 0 when everything it matched skipped, so a drifted environment variable would otherwise leave a green check that verified nothing
  - New `docs/managed-pds.md` and samples `samples/ManagedPdsSample` and `samples/ManagedPdsSample.AppHost`
- **`CarWriter`** (Issue #40) — CAR v1 producer, the counterpart to `CarReader`. `Write(root, blocks)` for a block dictionary or explicit `CarBlock` sequence, plus `WriteTo`/`WriteToAsync`
- **`RepoCommit` / `SignedRepoCommit`** (Issue #40) — builds and signs commit objects. `EncodeUnsigned()` produces exactly the bytes that get signed, prefix-preserving so `FirehoseVerifier.ExtractSignedView` recovers them intact
- **`PlcOperationBuilder`** (Issue #40) — builds, signs, and derives DIDs from `did:plc` genesis operations, with `PlcClient.SubmitOperationAsync` to publish them. Adds `PlcErrorKind.InvalidOperation`
- **`MerkleSearchTree.SerializeProof(keys)`** (Issue #40) — serializes only the root and the root→key search paths, the covering proof a `#commit` carries. `Serialize()` is unchanged
- **`Tid.FromInt64(long)` / `Tid.ToInt64()`** (Issue #40) — convert between a TID and its raw 64-bit value
- **`CidComputation.TryDecodeCidString`** (Issue #40) — non-throwing CID string decoding
- **`DidDocument.Context`** (Issue #40) — the `@context` field, omitted unless set. Required when *publishing* a document, ignorable when consuming one
- **Jetstream consumer** (Issue #43) — JSON event streaming with server-side filtering, the bandwidth-friendly alternative to the binary firehose
  - `JetstreamClient` — one WebSocket to `/subscribe` with `wantedCollections` (max 100), `wantedDids` (max 10,000), `cursor`, and `maxMessageSizeBytes`
  - `JetstreamConsumer` — automatic reconnection, cursor persistence through `IFirehoseCursorStore`, reconnect rewind with duplicate suppression, at-least-once delivery across restarts
  - `JetstreamEventParser` — forward-tolerant parser; unknown kinds, operations, and fields are skipped
  - `JetstreamCommitEvent.GetRecord<T>()` — typed deserialization honouring `LexiconTypeRegistry`, with a computed `Uri`
  - `IJetstreamDecompressor` — optional zstd seam; the SDK ships no zstd dependency, and `docs/jetstream.md` includes a copy-paste implementation
  - Jetstream events carry no MST proofs or signatures and cannot be cryptographically verified — use the binary firehose where verification matters
- **`AuthorizationServerDiscovery.HandleResolutionTimeout`** (Issue #52) — per-round budget for handle resolution, default 5 s, `Timeout.InfiniteTimeSpan` to restore the old unbounded behaviour. Configurable via `OAuthOptions` and `AtProtoOAuthServerOptions`
- **`AtProtoOAuthServerOptions.HttpClient`** (Issue #52) — supply the `HttpClient` used for OAuth discovery and token requests. Its `Timeout` is left untouched and it is not disposed with the service
- **`AtProtoOAuthServerOptions.HttpClientTimeout`** (Issue #52) — timeout for the SDK-created OAuth client. Default 30 s
- **`OAuthClientMetadata.ToJson(bool writeIndented = false)`** (Issue #41) — renders the client-metadata document exactly as it must be served at the `client_id` URL, with unset optional fields omitted
- **`AtProtoJsonDefaults.ApplyRecordTypeDiscriminator(JsonTypeInfo)`** (Issue #49) — public contract modifier guaranteeing `AtProtoRecord`-derived types serialize exactly one `$type`. Applied automatically by the SDK's options; add it to hand-built ones via `DefaultJsonTypeInfoResolver.Modifiers`
- **Typed unions, nested inline objects, and token families in `atproto-lexgen csharp`** (Issue #45)
  - A union whose variants are all `object` defs emits an `abstract class <Property>Union` with `[JsonPolymorphic]`, so `attribution` round-trips as `AttributionWebsite` instead of a raw `JsonElement`. Variants another union already claimed still fall back to `JsonElement?` and say so as a `WARN`
  - A union of tokens is typed as `string`; inline `object` schemas become nested classes, and an array of them singularizes its element type
  - `knownValues`/token families collapse into one static class per family plus an `All` list — the recipe.exchange `defs` document drops from 101 generated classes to 9
  - `CSharpEmitter.Warnings` exposes the diagnostics; the CLI prints them as `WARN` lines

### Removed

- **`ATProtoNet.Pds` NuGet package, `samples/PdsSample`, and `docs/pds.md`** — see **Breaking changes**. The implementation remains in git history; maintaining a second PDS was never the goal
- **`ATProtoNet.Server.EntityFrameworkCore` and `ATProtoNet.Aspire` NuGet packages** (Issue #33) — consolidated into `ATProtoNet.Server`, cutting the published set from 8 packages to 6

### Fixed

- **`WithDataBindMount` produced a container that could not start** — it added a second mount on `/pds`, which Docker and Podman reject outright, so the documented usage never came up. A data mount now replaces the default volume instead of adding to it, and `WithDataVolume(name?)` is added as the counterpart
- **The Aspire PDS container never started** — `AddAtProtoPds()` set no blobstore, and the reference PDS exits with `Must configure either S3 or disk blobstore`. It now sets `PDS_BLOBSTORE_DISK_LOCATION` under the same data volume. Present since 0.4.0, uncaught because nothing ran the container
- **Seven `AdminClient` methods threw `JsonException` against a real PDS** — `DeleteAccountAsync`, `UpdateAccountHandleAsync`, `UpdateAccountEmailAsync`, `UpdateAccountPasswordAsync`, `DisableAccountInvitesAsync`, `EnableAccountInvitesAsync`, and `DisableInviteCodesAsync` asked for a deserialized response from endpoints the PDS answers with an empty body, so every call failed regardless of what the server did. They now use the body-only overload. The same defect affected 19 further procedures, fixed separately under Issue #69
- **Aspire PDS container regenerated its JWT secret and PLC rotation key on every run** — both were generated while the AppHost graph was built, but the data volume persists, so every restart invalidated all sessions and left existing accounts with `did:plc` identities whose rotation key the server no longer held. Both are now Aspire parameters persisted to user secrets. **Existing volumes were written under a key that was already lost each run**, so a volume from a previous version should be recreated. Generation happens in run mode only, since an Aspire manifest's `generate` block can only describe an alphanumeric string and the PDS reads both as hex
- **XRPC procedures with no declared output threw `JsonException` on every call** (Issue #69) — 19 methods across `IdentityClient`, `SyncClient`, `ActorClient`, `GraphClient`, `NotificationClient`, and the Ozone clients asked for a deserialized response from endpoints whose Lexicon declares no output, so the empty body a real server returns failed before the caller saw anything. This is not a corner of the API: `PutPreferencesAsync`, the six mute/unmute methods, `UpdateSeenAsync`, `RegisterPushAsync`, and `UpdateHandleAsync` are ordinary calls. No public signatures change. The defect survived because the test doubles returned `{}`; `EmptyResponseBodyTests` now drives all 19 NSIDs through a handler answering 200 with an empty body
- **`LoginForm` threw when the app had not called `services.AddLocalization()`** (Issue #35) — the optional `IStringLocalizer<LoginForm>` was wired with `[Inject]`, and Blazor's property injection requires the service regardless of nullability, so rendering `<LoginForm />` without localization threw before parameters were applied. It is now resolved through `IServiceProvider.GetService<...>()`, so it is genuinely optional
- **`atproto-lexgen csharp` emitted C# that did not compile** (Issue #45) — found by generating the published `exchange.recipe.*` Lexicons. All fixed with coverage in `CSharpEmitterTests`:
  - **Stray closing brace** — every generated file ended with an extra `}`, so nothing compiled
  - **CS0542 member/type name collisions** — members colliding with their enclosing type, another member, or `AtProtoRecord`'s `Type`/`CreatedAt` are renamed while `[JsonPropertyName]` keeps the wire format. Names that are not legal identifiers are sanitized
  - **CS0101 duplicate types** — sibling documents share a namespace, so later defs are prefixed with their document name and the rename is reported
  - **Unqualified `BlobRef`/SDK types** — files emit the `using` directives they need plus `#nullable enable`, and cross-namespace references are rooted at `global::`
  - **Non-nullable `JsonElement` fallbacks** — optional members are always nullable; serializing `default(JsonElement)` threw
  - **Cross-namespace refs landed in the consumer's namespace** — well-known `com.atproto.*`/`app.bsky.*` defs now map to the SDK's own models (`SdkTypeMap`); unresolvable refs fall back to `JsonElement?` and are reported as `WARN` rather than emitting a dangling type name
  - **`record` defs did not extend `AtProtoRecord`** — they now subclass it, override `Type`, and inherit `CreatedAt`
  - **`atproto` NSID segment cased as `Atproto`** — namespaces now read `Com.AtProto.*`, matching the SDK layout
  - Lexicon `"type": "number"` maps to `double` instead of `JsonElement`
- **`[JsonPropertyName("$type")]` is now repeated on every `Type` override** (Issue #45) — System.Text.Json does not carry the attribute from the abstract base onto an override, so records serialized with hand-built options emitted a spurious `"type"` field
- **`OAuthClientMetadata` serialized unset optional fields as JSON `null`, so authorization servers rejected the client-metadata document** (Issue #41) — the spec distinguishes *absent* from *null*, and the reference provider fails a document containing `"jwks_uri": null` with `invalid_client_metadata`, breaking PAR for any app serving `Results.Json(metadata)`. Every optional property now carries `[JsonIgnore(WhenWritingNull)]`, so the document is compliant under any options
- **`AtProtoRecord` subclasses serialized a stray `type` property alongside `$type`** (Issue #49) — System.Text.Json neither inherits `[JsonPropertyName]` through an override nor collapses base and override into one contract property, so a record written the documented way emitted both, polluting records other AT Protocol apps read. `AtProtoJsonDefaults.ApplyRecordTypeDiscriminator` now collapses them, wired into the SDK's own options
- **Implementing `IAtProtoUnion` broke all (de)serialization of records containing the union** (Issue #46) — `UnionJsonConverterFactory` claimed every assignable type from `CanConvert` but returned `null` from `CreateConverter`, which System.Text.Json rejects. The factory never did anything (discrimination comes from `[JsonPolymorphic]` plus `LexiconTypeRegistry.RegisterUnionVariant`), so it has been removed. `IAtProtoUnion` remains as a documentation marker
- **`PlcClient` DID-path requests bypassed the directory `BaseAddress`** (Issue #47) — `did:plc:…` parses as an *absolute* URI, so five methods failed with `NotSupportedException: The 'did' scheme is not supported` instead of querying the directory. Requests now use RFC 3986 `./`-prefixed relative references; regression tests added
- **OAuth handle resolution no longer stalls on a dead handle domain** (Issue #52) — a handle whose domain drops packets on 443 blocked for the full 100 s default before the flow continued. `ResolveHandleToDidAsync` now races the HTTPS well-known lookup against the DNS-over-HTTPS TXT lookup, and both bound each round with `HandleResolutionTimeout`. Caller cancellation still propagates; only budget expiry is treated as "no answer"
- **SDK-created OAuth `HttpClient` no longer inherits the 100 s default timeout** (Issue #52) — `AtProtoOAuthService` applies `HttpClientTimeout` (30 s), and `DidWebResolver`'s parameterless constructor applies 10 s. `AtProtoClient`'s own client is unchanged, since it carries blob uploads where 100 s can be legitimate
- **A timed-out handle probe no longer aborts OAuth sign-in** (Issue #42) — handle verification is best-effort, but it distinguished failures by exception type, so a refused connection left `IsHandleVerified = false` while a *timed-out* probe failed the whole login even though the authoritative DID was already in hand. Both now yield an unverified handle; only the caller's own token aborts the flow, and it now disposes the pending DPoP key when it does
- **Polymorphic payloads with a non-leading `$type` failed to deserialize** (Issue #50) — the appview serializes embed views with the discriminator anywhere in the object, which System.Text.Json needs `AllowOutOfOrderMetadataProperties` for. Now set in both options instances, fixing `getPosts`/`getPostThread`/timeline reads containing embeds
- **`Directory.Build.props` `RepositoryUrl` dropped the `.git` suffix** so Forgejo's NuGet registry matches it against the canonical repo URL on first upload. Without this every new packable project published as an *orphan*, requiring a manual relink after each first release. Affects new packages only; the five already-orphaned v0.4.0 packages were relinked manually

### Security

- **`/.well-known/atproto-did` responses are now capped and redirect-checked** (Issue #52) — the endpoint lives on a host derived from untrusted input. Responses are read with `ResponseHeadersRead` and capped at 1 KiB (by `Content-Length` *and* during the read, so a chunked body cannot bypass it), and a response whose final request URI landed on a different host than the handle is ignored — a hostile handle domain can no longer redirect resolution at an arbitrary host. Same-host redirects still resolve
- **Handle resolution now queries DNS-over-HTTPS on every attempt** (Issue #52) — because the two lookups race rather than running in sequence, `dns.google` is contacted for every handle resolution. Deployments treating the handle as sensitive should note the additional third-party disclosure

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

- **Missing sync endpoints & Sync v1.1 message fields** (Issue #21) — Complete com.atproto.sync coverage and the Sync v1.1 message fields (not inductive verification, which arrived in 0.7.0)
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
