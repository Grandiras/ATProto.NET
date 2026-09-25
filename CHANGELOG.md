# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Breaking changes

- **`ATProtoNet` no longer depends on `Microsoft.Extensions.Http` or `Microsoft.Extensions.Options`** — the core package used neither, so its dependency closure shrinks from 14 packages to 3. ASP.NET Core apps are unaffected, since the shared framework carries both. Migration: an app outside ASP.NET Core that used `IHttpClientFactory`, `IOptions<T>` or `LoggerFactory` only through this transitive dependency adds `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options` or `Microsoft.Extensions.Logging` itself (#110)
- **Aspire hosting: `WithHostname` and `WithJwtSecret` are generic over the PDS resource type.** The reference-PDS and Tranquil overloads are replaced by one generic method each on `AtProtoPdsHostingExtensions`, constrained to the new `AtProtoPdsContainerResourceBase`; extension-method calls compile unchanged and still return the concrete builder. Migration: recompile against this release, and rewrite an explicit static call such as `AtProtoTranquilPdsHostingExtensions.WithHostname(pds, …)` as `pds.WithHostname(…)`. (#116)
- **`simplespace`: separate read and write policies, and `putMember` replaces `addMember`** — `com.atproto.simplespace` now follows upstream's read/write split ([bluesky-social/atproto#5496](https://github.com/bluesky-social/atproto/pull/5496)), which the spaces-alpha PDS and the bulletin reference app already run. Against them 0.6 got a 400 from `createSpace`, threw on `getSpace`, and had an `updateSpace` policy change silently ignored, and `addMember` no longer exists. The wire models follow: `ReadPolicy` / `WritePolicy` replace `Policy`, `PutSimpleSpaceMemberRequest` replaces `AddSimpleSpaceMemberRequest`, and `SimpleSpaceMember` gains required `Read` / `Write`. Migration: pass `readPolicy:` and `writePolicy:` instead of `policy:` (the same value to both keeps the old behaviour), and replace `AddMemberAsync(space, did)` with `PutMemberAsync(space, did, read: true, write: true)` (#113)
- **`SimpleSpaceClient.CheckUserAccessAsync` takes a required `access`** — inserted before `clientId`, as the Lexicon now requires it. Migration: `CheckUserAccessAsync(space, user, SimpleSpaceAccess.Read, clientId)`; a positional `clientId` argument must move (#113)
- **`ATProtoNet.Server`: the `simplespace` store and record carry both policies and per-member access** — `SimpleSpaceRecord` takes `ReadPolicy` and `WritePolicy` in place of `Policy`, `ISimpleSpaceStore.AddMemberAsync` / `IsMemberAsync` become `PutMemberAsync(space, did, read, write)` / `GetMemberAsync`, and `AddSimpleSpaceMemberEndpoint` / `SpaceNsids.AddSimpleSpaceMember` become `PutSimpleSpaceMemberEndpoint` / `SpaceNsids.PutSimpleSpaceMember`. Migration: a custom store implements the two new members and keeps both flags; construct records with both policies (#113)
- **EF Core `simplespace` schema: `Policy` is split and members gain access flags** — `AtProtoSimpleSpaces.Policy` becomes `ReadPolicy` and `WritePolicy`, and `AtProtoSimpleSpaceMembers` gains `Read` and `Write`. Migration: add a migration that copies `Policy` into both new columns before dropping it and defaults the member columns to `true` for existing rows, as upstream's `003-space-access` does. See [docs/spaces.md](docs/spaces.md#upgrading-a-simplespace-database-from-06) (#113)
- **`ISpaceAccessPolicy` also decides writes** — `SpaceAccessRequest` gains `Access` (`SpaceAccessKind.Read`, the default, or `Write`), and `notifyWrite` now asks the policy with `Write` and no attested client. `ISimpleSpaceManagingAppClient.CheckUserAccessAsync` takes the `SpaceAccessKind` too. Migration: a custom policy must handle `SpaceAccessKind.Write` without applying an app perimeter, or it refuses every writer with 403 (#113)
- **`ISpaceServiceAuthVerifier.VerifyAsync` takes the set of accepted audiences** — needed so `notifyWrite` can accept each way a space authority is addressed. Migration: a custom verifier checks `aud` against the collection; callers pass `[audience]`, or use the single-audience overload `SpaceServiceAuthVerifier` keeps (#113)
- **`NotifyWriteEndpoint`'s constructor takes an `ISpaceAccessPolicy`**, plus an optional `SpaceWriteNotifier` and logger. Migration: only code constructing the endpoint by hand is affected; DI resolves it (#113)
- **`MerkleSearchTree` only accepts valid MST keys, and `Create` rejects duplicates** (#111) — `Add` and `Create(entries)` throw `ArgumentException` for a key that is not `collection/rkey` (two non-empty segments of `A-Z a-z 0-9 _ ~ - : .`, at most 1024 characters), matching the reference implementation. `Create` used to build a corrupt tree from a repeated key; it now throws. Migration: key the tree by repo paths
- **`MerkleSearchTree.CreateFromEntries` removed** (#111) — it duplicated `Create(entries)`. Migration: call `MerkleSearchTree.Create(entries)`
- **`MstNodeData`, `MstTreeEntry` and `MstKeyDepth` are now internal** (#111) — they are the MST's wire codec and key-height helper, with no use outside the tree. Migration: build, read and serialize trees through `MerkleSearchTree`
- **`DagCborDecoder.Decode` throws `FormatException` for all malformed input** (#111) — it used to throw `InvalidOperationException` (floats, malformed CID links, non-string keys) or leak `CborContentException` (malformed CBOR). `MerkleSearchTree.Deserialize` likewise throws `FormatException`, not `InvalidOperationException`, for a tree that is too deep. Migration: catch `FormatException`
- **`DagCborDecoder.DecodeToNode`, `DagCborEncoder.ComputeCid` and `CarBlock.DataLength` removed** (#111) — unused duplicates. Migration: `JsonSerializer.SerializeToNode(DagCborDecoder.Decode(bytes))`, `CidComputation.ComputeForDagCbor(bytes)`, and `block.Data.Length`
- **Identifier parsing is strict** — `Parse` now throws, and `TryParse` returns `false`, for every value the atproto specs and the official `atproto-interop-tests` syntax fixtures reject (see *Fixed*), including any `Cid` that is not a base32 CIDv1 with the DRISL/DAG-CBOR or raw codec and a SHA-256 digest. JSON deserialization of such values fails the same way. Migration: read legacy or foreign data with `TryParse` and handle `false`. (#114)
- **Identifier types are `sealed record`s** — `Did`, `Handle`, `AtIdentifier`, `Nsid`, `Tid`, `RecordKey`, `Cid`, `AtUri`, `SpaceUri` and `SpaceRecordUri` changed from hand-written classes to sealed records. `Parse`/`TryParse`, `==`, `Equals`, `ToString` and the string conversions keep their source shape, so this is a binary break only; `Handle` equality is now ordinal, which is equivalent because handles are always lower-cased. Migration: recompile. (#114)
- **`AtUri` components are typed** — `Collection` is now `Nsid?` and `RecordKey` is `RecordKey?` (both convert implicitly to `string`), and `Repo` is parsed once instead of on every access. `AtUri.Create(AtIdentifier, string?, string?)` is replaced by `Create(AtIdentifier repo, Nsid? collection = null, RecordKey? rkey = null)`, which throws when a record key is given without a collection. Migration: `AtUri.Create(repo, Nsid.Parse(collection), RecordKey.Parse(rkey))`, and `uri.Collection?.Value` where a `string` member was used. (#114)
- **The SDK's Lexicon unions are open, with an `Unknown*` variant** (#115) — `EmbedBase`, `EmbedView`, `FacetFeature`, `ThreadNode`, `ReportSubject`, `ModEventType` and `ModerationSubject` are now `[AtProtoUnion]` bases, and a `$type` the SDK does not model reads as `UnknownEmbed`, `UnknownEmbedView`, `UnknownFacetFeature` and so on instead of throwing. `ApplyWriteOperation` stays strict, since its Lexicon marks it closed. Migration: add an `Unknown*` arm (or a discard) to every `switch` over these bases; the compiler will not flag a missing one.
- **Lexicon models derive from `LexObject`** (#115) — the models of Lexicon records, views and objects, and `AtProtoRecord`, now inherit `LexObject`, which adds an `ExtensionData` dictionary. This changes their base class. Migration: recompile; a record type that declares its own `[JsonExtensionData]` member must drop it and use the inherited `ExtensionData`.
- **`UpdateProfileAsync` takes an edit callback** (#115) — `UpdateProfileAsync(Action<ProfileRecord> update)` replaces the overload with four optional parameters, returns a `RecordRef`, and `ProfileRecord`'s properties are now settable rather than `init`. Migration: `UpdateProfileAsync(displayName: "x")` becomes `UpdateProfileAsync(p => p.DisplayName = "x")`.
- **`AtProtoJsonDefaults.Options` is read-only** (#115) — adding a converter or changing a setting now throws `InvalidOperationException`. Migration: register union variants on `LexiconTypeRegistry.Instance`, or copy the options with `new JsonSerializerOptions(AtProtoJsonDefaults.Options)` for different settings.
- **`LexiconTypeRegistry` is reduced to union variants** (#115) — removed `CreateOptions()`, the record-type registry (`RegisterRecordType`, `GetRecordType`, `RecordTypes`, and `ILexiconTypeRegistrar.RegisterRecordType`), `LoadPluginsFromAssembly`, `[LexiconPlugin]`, the public constructor, and the `IAtProtoUnion` marker interface. None of them affected serialization. Migration: use `LexiconTypeRegistry.Instance` with `AtProtoJsonDefaults.Options`; delete record-type registrations; call `LoadPlugin<T>()` instead of scanning an assembly; replace `IAtProtoUnion` with `[AtProtoUnion(...)]`.
- **One XRPC exception: `XrpcException` replaces `AtProtoHttpException`** — every XRPC error now throws `ATProtoNet.Http.XrpcException`, which derives from the new `AtProtoException` and no longer from `HttpRequestException`. It carries `Nsid`, `Error` (formerly `ErrorType`, now never null: a body without an error envelope gets the status's generic name), `ErrorMessage`, a non-nullable `StatusCode`, `ResponseBody`, the response `Headers` and `Is(name)`; a 429 surfaces as `XrpcRateLimitException` and a 401 or `ExpiredToken`/`InvalidToken` as `XrpcAuthenticationException`. Migration: `catch (AtProtoHttpException ex) when (ex.ErrorType == "X")` becomes `catch (XrpcException ex) when (ex.Is(XrpcErrors.X))`, and code that caught `HttpRequestException` to see XRPC errors catches `XrpcException` (#117)
- **`ATProtoNet.Server.Xrpc.XrpcException` is gone: hosted endpoints throw the core `ATProtoNet.Http.XrpcException`** — one type now serves client and server, and the routing still writes the error body, status and `Headers`. Its status is an `HttpStatusCode`, as is `SpaceVerificationException`'s, which now derives from the core type. Migration: add `using ATProtoNet.Http;` and pass `HttpStatusCode.NotFound` where `StatusCodes.Status404NotFound` was passed (#117)
- **SDK exceptions derive from `AtProtoException`, and `OAuthException.ErrorCode` is renamed `Error`** — `OAuthException`, `SpaceCredentialException`, `SpaceTokenException`, `SpaceRepoVerificationException`, `JetstreamConnectException` and `JetstreamArchiveException` change base class, so one `catch (AtProtoException)` covers every SDK failure. Migration: recompile, and read `ex.Error` instead of `ex.ErrorCode` (#117)
- **A response that does not match its type throws `XrpcResponseFormatException`** — instead of a raw `JsonException` or an `InvalidOperationException`, from every XRPC call, `RecordCollection<T>` and `RepoClient.GetRecordAsync<T>`. It names the method in `Nsid` and keeps the `JsonException` as its inner exception. Migration: catch `XrpcResponseFormatException` (#117)
- **`AtProtoClient.PdsUrl` / `SetPdsUrl(string)` are replaced by `ServiceUrl` / `SetServiceUrl(Uri)`** — the service URL is held by the client instead of written to `HttpClient.BaseAddress`. Migration: `client.SetServiceUrl(new Uri(url))`, and `client.ServiceUrl` (a `Uri` with a trailing slash) where `PdsUrl` was read (#117)
- **A supplied `HttpClient`'s `BaseAddress` is ignored** — `AtProtoClient` always starts at `AtProtoClientOptions.InstanceUrl`, and `PdsAdminClient` always sends to `PdsAdminOptions.Url`; before, an `HttpClient` that already had a `BaseAddress` silently overrode them. Migration: put the service URL in the options rather than on the `HttpClient` (#117)
- **`AtProtoClient.QueryAsync` / `ProcedureAsync` take an `XrpcCallOptions? options` before the cancellation token.** Migration: a call that passed the token positionally as the third argument passes it by name, `cancellationToken: ct` (#117)
- **Blob and repository downloads return `XrpcStreamResponse`** — `SyncClient.GetBlobAsync` / `GetRepoAsync` and `SpaceClient.GetBlobAsync` / `GetRepoAsync` return the stream together with `ContentType` and `ContentLength`, and disposing it releases the HTTP response. Migration: `await using var blob = await client.Sync.GetBlobAsync(did, cid);` and read `blob.Content` (#117)
- **`XrpcClient` is internal** — no public member of `AtProtoClient` exposed it, and its setters raced on shared clients. `MaxRateLimitRetries` moves to `AtProtoClientOptions.RateLimit.MaxRetries`; `BaseUrl`, `IsAuthenticated`, `UpdateDPoPNonce`, the body-less `QueryAsync` and the no-op `Dispose` are removed. Migration: call custom XRPC methods through `AtProtoClient.QueryAsync` / `ProcedureAsync` (#117)
- **XRPC endpoints declare their NSID once, as a static `Nsid`** — `IXrpcEndpoint.Nsid` is now `static abstract Nsid Nsid { get; }`, and `[XrpcEndpoint]` is removed. The instance property was read from an uninitialized handler, so one written as an auto-property or with a field initializer failed to register; the static property is read directly, and an invalid NSID fails at startup. Like any interface with a static abstract member, the endpoint interfaces can no longer be generic type arguments (CS8920). Migration: delete `[XrpcEndpoint(Nsid = …)]` and replace `public string Nsid => "…";` with `public static Nsid Nsid { get; } = Nsid.Parse("…");` (`using ATProtoNet.Identity;`) (#121)
- **`AddXrpcEndpointsFromAssembly` registers every class implementing `IXrpcEndpoint`**, exactly as `AddXrpcEndpoint<T>()` does, now that the attribute it looked for is gone. Registration also rejects a handler implementing no endpoint interface or several, and a second handler for an NSID that already has one (compared case-insensitively, as routes are); both used to surface only as a routing failure on the first request. Migration: keep handler classes that should not be mapped out of scanned assemblies, or register handlers one by one (#121)
- **`MapXrpcEndpoints()` returns the `/xrpc` `RouteGroupBuilder`** — so `.RequireAuthorization()`, `.RequireRateLimiting(…)`, `.RequireCors(…)` and `.WithMetadata(…)` apply to every XRPC endpoint; it used to return the builder it was called on. Migration: make any `Map…` call that was chained onto its result on the app instead, since on the group it would map under `/xrpc` (#121)
- **Typed identifiers across `com.atproto.*`, the facade, `RecordCollection<T>` and `PdsAdminClient`** — the models and client methods of `Lexicon/Com/AtProto/{Repo,Server,Identity,Sync,Admin,Label,Moderation}`, `StrongRef`, `Label`, `BlobLink`, `CidLink`, `RecordRef`, `RecordView<T>`, `RecordCollection<T>`, `PdsAdminClient`/`CreatePdsAccountRequest` and the session (now `AtProtoSession`, #124) take and return `Did`, `Handle`, `AtIdentifier`, `AtUri`, `Nsid`, `Cid`, `RecordKey` and `Tid` wherever the Lexicon field carries that format, and Lexicon-required fields that defaulted to an empty string are `required`. A response carrying an invalid identifier now throws `XrpcResponseFormatException`. Migration: values the API returns pass straight through; parse literals where they enter your code (`Did.Parse("did:plc:…")`, `RecordKey.Parse("self")`); typed values still convert implicitly to `string` (#119)
- **`AtProtoClient.Did` / `Handle` / `LatestRepoRev` are `Did?` / `Handle?` / `Tid?`, and `GetCollection<T>`, `QueryAsync` and `ProcedureAsync` take an `Nsid`** — Migration: `client.GetCollection<TodoItem>(Nsid.Parse("com.example.todo.item"))`; keep NSIDs in `static readonly` fields (#119)
- **Lexicon `datetime` fields are `AtDatetime`** — `AtProtoRecord.CreatedAt` is `AtDatetime?` (still set to the current time on construction), as are `createdAt`, `indexedAt`, `usedAt`, `cts`, `exp` and the other `datetime` fields of the converted models. Migration: set with `AtDatetime.Now()`, `AtDatetime.Parse("…")` or `AtDatetime.FromDateTimeOffset(value)`; read with `.Value` / `.TryGetValue(out …)`, or `.ToString()` for the text (#119)
- **A session's `Did` and `Handle` are typed and `required`** — on `AtProtoSession` (#124, below) they are `Did` and `Handle`, and an OAuth session whose handle did not verify carries `handle.invalid` rather than the DID. Migration: parse literals when building a session (`Did = Did.Parse(…)`, `Handle = Handle.Parse(…)`) (#119)
- **Parameters follow one order: subject, required inputs, filters, `limit`, `cursor`** — `RepoClient.ListRecordsAsync` and `RecordCollection<T>.ListAsync` / `ListFromAsync` take `reverse` before `limit` and `cursor`, `RepoClient.ListMissingBlobsAsync` takes `limit` before `cursor`, and `ModerationClient.CreateReportAsync` takes the `subject` first. Migration: positional calls no longer compile; reorder them or pass the arguments by name (#119)
- **`ListAllRecordsAsync` is renamed `EnumerateRecordsAsync`, and enumerators take `int? pageSize = null`** — `RecordCollection<T>.EnumerateAsync` / `EnumerateFromAsync` now use the server's default page size instead of 100. Migration: rename the call; pass `pageSize: 100` to keep the old page size (#119)
- **`ICursoredResponse` is replaced by `ICursorPage<T>`, and model collections are `IReadOnlyList<T>`** — every cursored `com.atproto.*` response and `RecordPage<T>` implements `ICursorPage<T>`; the lists in the converted models were `List<T>`. Method inputs take `IEnumerable<T>` (`ApplyWritesAsync`, `DisableInviteCodesAsync`, `QueryLabelsAsync`, `PostAsync`), and `GetInviteCodesResponse.Codes` is typed as `InviteCode` instead of `JsonElement`. Migration: copy a list you need to change (`[.. response.Records]`) (#119)
- **Single-use request bodies are internal** — `CreateRecordRequest`, `PutRecordRequest`, `DeleteRecordRequest`, `ApplyWritesRequest`, `CreateSessionRequest`, `CreateAppPasswordRequest`, `RequestPasswordResetRequest`, `ResetPasswordRequest`, `ConfirmEmailRequest`, `CreateInviteCodeRequest`, `RevokeAppPasswordRequest`, `ReserveSigningKeyRequest`, `UpdateHandleRequest`, `NotifyOfUpdateRequest`, `RequestCrawlRequest`, `AdminDeleteAccountRequest`, `DisableAccountInvitesRequest`, `EnableAccountInvitesRequest`, `UpdateAccountEmailRequest`, `UpdateAccountHandleRequest`, `UpdateAccountPasswordRequest`, `DisableInviteCodesRequest` and `CreateReportRequest` were only ever built inside one client method. Migration: call that method, which takes their fields as parameters (#119)
- **`atproto-lexgen csharp` emits typed identifiers and `IReadOnlyList<T>`** — `did`, `handle`, `at-identifier`, `at-uri`, `nsid`, `cid`, `record-key`, `tid` and `datetime` strings become the SDK's types (with `using ATProtoNet.Identity;`), and arrays `IReadOnlyList<T>` instead of `List<T>`, so generated code matches the SDK; `atproto-lexgen lexicon` maps them back to their formats. Migration: regenerate, then parse literals as above (#119)
- **`app.bsky.*` is typed** — its models and clients use `Did`, `Handle`, `AtIdentifier`, `AtUri`, `Cid` and `AtDatetime` wherever the Lexicon field carries that format (`actor` parameters, post, feed and list URIs, `PostView.Uri`/`Cid`, `ProfileView.Did`/`Handle`, the viewer-state record URIs, `FollowRecord.Subject`, `MentionFeature.Did`, every `createdAt`/`indexedAt`/`seenAt`), with `IReadOnlyList<T>` model lists and `IEnumerable<T>` inputs; `RichTextBuilder.Mention` takes `(Handle, Did)`. `GetTimelineAsync`, `GetAuthorFeedAsync` and `ListNotificationsAsync` take their filters before `limit` and `cursor`, and `PutPreferencesRequest`, `MuteActorRequest`, `MuteActorListRequest`, `MuteThreadRequest` and `UpdateSeenRequest` are internal. Migration: pass values from responses straight through, parse literals (`AtIdentifier.Parse("alice.bsky.social")`, `AtUri.Parse("at://…")`), stamp records with `AtDatetime.Now()`, pass the reordered arguments by name, and call the client method instead of building a request body (#119)
- **Typed identifiers in `chat.bsky.*`, `tools.ozone.*` and `site.standard.*`** — their models and client methods take and return `Did`, `Handle`, `AtIdentifier`, `AtUri`, `Cid`, `RecordKey` and `AtDatetime` wherever the Lexicon field carries that format (for example `MessageView.SentAt`, `ChatMemberView.Did`, `ModEventView.CreatedBy`, `RepoSubject.Did`, `DocumentRecord.PublishedAt`, `SubscriptionRecord.Publication`); their lists are `IReadOnlyList<T>`, list inputs are `IEnumerable<T>`, and every cursored response implements `ICursorPage<T>`. `ConvoClient.ListConvosAsync`, `ModerationClient.QueryEventsAsync` / `QuerySubjectsAsync` and `SignatureClient.SearchAccountsAsync` / `FindRelatedAccountsAsync` take their filters first, then `limit`, then `cursor`. Migration: parse literals (`Did.Parse("…")`, `AtUri.Parse("…")`, `AtDatetime.Now()`), and reorder positional calls or name the arguments (#119)
- **`StandardSiteClient` is typed end to end** — every method takes an `AtIdentifier` repository and a `RecordKey`, and `ListPublicationsAsync` / `ListDocumentsAsync` / `ListSubscriptionsAsync` return a `RecordPage<T>` of typed records instead of the raw `ListRecordsResponse`, whose values were `JsonElement`s. Migration: read `entry.Value` directly instead of deserializing it (#119)
- **The chat and Ozone single-use request bodies are internal** — `SendMessageRequest`, `SendMessageBatchRequest`, `DeleteMessageForSelfRequest`, `LeaveConvoRequest`, `MuteConvoRequest`, `UnmuteConvoRequest`, `UpdateReadRequest`, `AcceptConvoRequest`, `AddReactionRequest`, `RemoveReactionRequest`, `DeleteTemplateRequest`, `DeleteSetRequest`, `AddValuesRequest`, `DeleteValuesRequest` and `DeleteMemberRequest`; the never-sent `DeleteChatAccountRequest` is removed. Migration: call the client method, which takes their fields as parameters (#119)
- **Typed identifiers across Spaces: `com.atproto.space.*`, `com.atproto.simplespace.*`, `ATProtoNet.Spaces` and the space server** — a space is a `SpaceUri`, a record in one a `SpaceRecordUri`, a participant a `Did`, and collections, record keys, revisions and CIDs are `Nsid`, `RecordKey`, `Tid` and `Cid` in the client methods, models, `SpaceUri`/`SpaceRecordUri` components, `SpaceCommitContext`, `SpaceRepoOp`, `SpaceRepoRecord`, `SpaceRepoCommit`, `SpaceRepoCar.Verify`, `SpaceSyncer` (its signing-key resolver takes a `Did`), `SpaceRepoCursor`, `ISpaceRepoStore`, `SpaceCredentialProvider` and its `HostResolver` option, and on the server `ISpaceRepoHost`, `ISpaceAuthorityStore`, `ISimpleSpaceStore`, `SimpleSpaceRecord.Owner`, `SpaceAccessRequest`, the verified-token records, `ISpaceCallerResolver`, `ISpaceDidDocumentResolver`, `ISpaceServiceAuthVerifier` (`expectedMethod` is an `Nsid`), `SpaceWriteNotifier`, the query-parameter classes and `SpaceServerOptions.ServiceDid`. `RegisterNotifyResponse.ExpiresAt` is an `AtDatetime`, cursored responses implement `ICursorPage<T>`, and collections are `IReadOnlyList<T>`. `ListRecordsAsync` and `ListRepoOpsAsync` (client and `ISpaceRepoHost`) take their filters before `limit` and `cursor`; the `string` space overloads, `ToSpaceUri()` on `SpaceView`/`CreateSimpleSpaceResponse` and `SpaceWriteResult.ToRecordUri()` are gone, and the single-use request bodies (`CreateSpaceRecordRequest`, `PutSpaceRecordRequest`, `DeleteSpaceRecordRequest`, `ApplySpaceWritesRequest`, `NotifySpaceDeletedRequest`) are internal. The EF Core entities are unchanged — the stores convert at the boundary — so no migration is needed. Migration: pass the typed values the API returns (`created.Uri`, `writer.Did`); parse literals once (`Nsid.Parse("com.example.bookmark")`, `Did.Parse(…)`, `options.ServiceDid = Did.Parse(…)`); name the reordered arguments; read `.Uri` where you called `ToSpaceUri()`/`ToRecordUri()` (#119)
- **Typed identifiers in the firehose and Jetstream models** — `CommitEvent.Repo`/`Commit`/`Rev`/`Since`/`PrevData`/`Blobs` are `Did`/`Cid`/`Tid`/`Tid?`/`Cid?`/`IReadOnlyList<Cid>?`, `RepoOp.Cid`/`Prev` are `Cid?` and `RepoOp.Action` is the new `RepoOpAction` enum, the account and identity events carry a `Did` and `Handle`, and `FirehoseMessage.Time` is an `AtDatetime?`. `JetstreamCommitEvent.Collection` is an `Nsid`, `RKey` is renamed `Rkey` and typed `RecordKey` (`JetstreamArchiveRow.RKey` is renamed `Rkey` too, and keeps the raw column text), `Rev` is a `Tid?`, the identity event's `Handle` is a `Handle?`, the events' `Time` an `AtDatetime?`, and `JetstreamConsumerOptions.WantedDids` and `JetstreamSnapshotRequest.Dids` are `IReadOnlyList<Did>` (collection filters stay strings, since they accept `app.bsky.graph.*` wildcards). `JetstreamArchiveClient.ListAllSegmentsAsync` is renamed `EnumerateSegmentsAsync`, and `JetstreamSegmentPage` implements `ICursorPage<T>`. Migration: compare `op.Action` with `RepoOpAction.Create` rather than `"create"`; rename `RKey` to `Rkey` and `ListAllSegmentsAsync` to `EnumerateSegmentsAsync`; `WantedDids = [Did.Parse(…)]` (#119)
- **`ServiceAuthGenerator` takes a `Did` and an `Nsid`** — the constructor's `serviceDid` and `ServiceDid` are a `Did`, `CreateToken`'s `lxm` an `Nsid?`, and its `audience` must be a DID with an optional `#service` fragment (anything else throws `ArgumentException`). Migration: `new ServiceAuthGenerator(Did.Parse("did:web:…"), key)`, `CreateToken(aud, Nsid.Parse("app.bsky.feed.getFeedSkeleton"))` (#119)
- **`ProfileViewDetailed.AssociatedChat` is replaced by `Associated`** — `associatedChat` was never a field, so it was always `null`; the chat setting is `Associated.Chat`, and `ProfileViewBasic` and `ProfileView` carry `Associated` too. Migration: read `profile.Associated?.Chat?.AllowIncoming` (#123)
- **Chat names follow the Lexicon** — `GetConvoAvailabilityResponse.CanConvo` is `CanChat`, `ListConvosAsync` / `EnumerateConvosAsync` take `string? readState` instead of `bool? readOnly`, and `ConvoView.Opened` and `AcceptConvoResponse.Convo` are removed. None of the old names was ever on the wire. Migration: use `CanChat`, pass `readState: "unread"`, and drop `Opened` and `Convo` (#123)
- **`ConvoClient.UpdateAllReadAsync` returns `UpdateAllReadResponse`** — with the `UpdatedCount` the chat service sends, and takes an optional `status`. Migration: existing `await`s compile unchanged; recompile (#123)
- **`ChatActorClient.ExportAccountDataAsync` returns an `XrpcStreamResponse`** — the export is JSON Lines, which the old `Task<byte[]>` tried to read as a JSON value and failed on. Migration: read `Content` line by line and dispose the response (#123)
- **Ozone's review queue is `QueryStatusesAsync`** — `QuerySubjectsAsync`, `EnumerateSubjectsAsync` and `QuerySubjectsResponse` called `tools.ozone.moderation.querySubjects`, which never existed. `QueryStatusesAsync` / `EnumerateStatusesAsync` call `queryStatuses` with every filter it defines, in a `SubjectStatusFilter` (`Takendown` and `Appealed` are booleans), and return `QueryStatusesResponse.SubjectStatuses`. Migration: move the filters into a `SubjectStatusFilter` (#123)
- **`SignatureClient.SearchAccountsAsync` takes signature values and returns account views** — `values` is `IEnumerable<string>`, and `SearchAccountsResponse.Accounts` and `RelatedAccount.Account` are `AccountInfo` (`com.atproto.admin.defs#accountView`); `AccountResult`, whose `similarAccounts` never existed, is removed. Migration: pass the `SigDetail.Value`s (#123)
- **Notification methods drop `priority` and `seenAt`** — `ListNotificationsAsync` / `EnumerateNotificationsAsync` take `reasons` instead of both, and `GetUnreadCountAsync` drops `priority`. Upstream ignores `priority`, and since 2026-09-21 answers `seenAt` on `listNotifications` with an error; a parameter cannot carry `[Obsolete]`. Migration: remove the arguments (#123)
- **New Lexicon parameters shift positional arguments** — `SearchPostsAsync` / `EnumerateSearchPostsAsync` take `IEnumerable<string>? tags` where they took `string? tag`; `sort` precedes `limit` in `GetFollowersAsync` / `GetFollowsAsync` and their enumerators; `purposes` precedes `limit` in `GetListsAsync` / `EnumerateListsAsync`; `MuteActorAsync`, `CreateReportAsync`, `DeactivateAccountAsync`, `QueryEventsAsync` / `EnumerateEventsAsync`, `ListMembersAsync` / `EnumerateMembersAsync` and `QuerySetsAsync` / `EnumerateSetsAsync` gain the inputs and filters their Lexicons define before the paging arguments or the token. Migration: name the arguments after the first, or pass the new ones (#123)
- **`AtProtoScopes.PermissionSets` holds only published permission sets** — `DeletePosts`, `ManagePosts`, `ManageFollows`, `ManageListsAndPacks`, `ViewNotifications` and `ManagePreferences` named sets nobody published, so an `include:` scope built from them could not be resolved; they are removed, and `ManageNotifications` is now `app.bsky.authManageNotifications` (it was the unpublished `authManageNotifs`). Migration: `DeleteContent` for deleting posts, `ManageNotifications` for notifications, `ManageModeration` for preferences, `FullApp` for the rest; recompile, as the constants are inlined (#123)
- **Lexicon unions that were `JsonElement` are typed open unions** — `FeedViewPost.Reason` (`ReasonRepost`, `ReasonPin`), `SkeletonFeedPost.Reason`, `ThreadgateRecord.Allow` (`ThreadgateMentionRule`, …), `PostgateRecord.EmbeddingRules`, `RecordView.Record` (`EmbeddedRecordView`: a quoted post, the not-found, blocked and detached placeholders, and feed, list, labeler and starter-pack views), `GetRelationshipsResponse.Relationships` (`Relationship`, `NotFoundActor`) and the preferences of `GetPreferencesAsync` / `PutPreferencesAsync` (16 `Preference` types), each with an `Unknown*` variant that writes back unchanged. `Relationship.Type` and `NotFoundActor.Type` are removed, and `GeneratorView`, `ListView`, `LabelerView` and `StarterPackViewBasic` now write their `$type`. Migration: match the variant types; an unknown one's `Raw` is the old `JsonElement` (#123)
- **Lexicon references that were `JsonElement` are typed** — `PostView.Threadgate` and `GetPostThreadResponse.Threadgate` are `ThreadgateView`, `BlockedPost.Author` a required `BlockedAuthor`, `ViewerState.MutedByList` / `BlockingByList` `ListViewBasic`, `StarterPackView.Feeds` `GeneratorView`s, `LabelerView.Creator` / `LabelerViewDetailed.Creator` `ProfileView`, `ChatMemberView.Associated` `ProfileAssociated`, `ListRecord.Labels` `SelfLabels`, `ModEventViewDetail.SubjectBlobs` `BlobView`s, and `TeamMember.Profile` a `ProfileViewDetailed` (`TeamMemberProfile` is removed). Migration: read the typed properties (#123)
- **Ozone: `RecordViewDetail.BlobCids` is `Blobs`, and label and tag events need their lists** — Ozone sends `blobs`, a list of `BlobView`s with media details, so `BlobCids` was always `null`. `ModEventLabel.CreateLabelVals` / `NegateLabelVals` and `ModEventTag.Add` / `Remove` are `required`, as Ozone rejects events without them. Migration: read `Blobs[i].Cid`; set the lists, empty where there is nothing to change (#123)
- **One session model: `AtProtoSession`, with `PasswordSession` and `OAuthSession`** — replaces `Session`, `OAuthSessionResult` and `AtProtoTokenData`. Sessions are immutable records carrying `Did`, `Handle`, `ServiceEndpoint` and `ExpiresAt` (which replaces `ExpiresIn` + `TokenObtainedAt`); an `OAuthSession` holds its DPoP key as PKCS#8 bytes (`DPoPKey`) instead of a `DPoPProofGenerator`, so it owns nothing disposable, and an unverified handle is `handle.invalid` (`IsHandleVerified` is gone). `AtProtoClient.Session` is `AtProtoSession?`, `AtProtoClient.OAuthSession` is removed, and `Session.DidDoc` is dropped in favour of `ServiceEndpoint`. Migration: read `client.Session` and pattern-match `PasswordSession` / `OAuthSession`; build a saved password session as `new PasswordSession { Did, Handle, ServiceEndpoint, AccessJwt, RefreshJwt }` (#124)
- **`ApplyOAuthSessionAsync` is replaced by `ApplySessionAsync(session, oauthClient)`** — it installs either kind of session without a request; its `tokenStore` parameter is gone, since the client persists to its own session store. `ResumeSessionAsync` takes an `AtProtoSession` and an optional `OAuthClient`, and returns the session as installed. Migration: `ApplyOAuthSessionAsync(result, oauth, store)` becomes `ApplySessionAsync(session, oauth)` on a client built `WithSessionStore(store)` (#124)
- **`OAuthClient.CompleteAuthorizationAsync` returns an `OAuthSession`, and `RefreshTokensAsync` is replaced by `RefreshAsync`** — `RefreshAsync(session)` returns the refreshed session instead of a token response to copy into a mutable one. Migration: `var session = await oauth.CompleteAuthorizationAsync(…)` (no `using`: nothing to dispose), and `session = await oauth.RefreshAsync(session)` outside an `AtProtoClient`, which refreshes by itself (#124)
- **One persistence abstraction: `IAtProtoSessionStore`** — replaces `ISessionStore` (whose `LoadAsync` the SDK never called) and `IAtProtoTokenStore`, with `GetAsync(Did)`, `SetAsync(AtProtoSession)` and `RemoveAsync(Did)`. The implementations become `InMemoryAtProtoSessionStore` (now in the core package), `FileAtProtoSessionStore` (with `FileSessionStoreOptions`) and `EfCoreAtProtoSessionStore<TContext>` (registered with `AddAtProtoEfCoreSessionStore<TContext>()`); `InMemorySessionStore` and `InMemoryAtProtoTokenStore` are removed. The `AtProtoClient` constructor, `AtProtoClientBuilder.WithSessionStore`, `AddAtProto<TSessionStore>`, `AddAtProtoServer<TSessionStore>` and `AtProtoClientFactory` take the new interface, and a client with no store no longer writes to a hidden in-memory one. The file and EF Core stores read what their 0.6 versions wrote, and the EF Core table and entity (`AtProtoTokens`, `AtProtoTokenEntity`) are unchanged, so **no database migration is needed**. Migration: rename the store types and registration call; a custom store implements the three new members and serializes with `JsonSerializer.Serialize<AtProtoSession>` (#124)
- **`ServerClient` no longer changes the client's session** — `CreateSessionAsync` and `CreateAccountAsync` return tokens without installing them (and are sent without the installed session's credentials), and `RefreshSessionAsync(refreshJwt)` / `DeleteSessionAsync(refreshJwt)` take the refresh JWT explicitly instead of reading and clearing the client's. Migration: sign in with `client.LoginAsync` or the new `client.CreateAccountAndLoginAsync`, and sign out with `client.LogoutAsync`; a direct call passes `session.RefreshJwt` (#124)
- **`AutoRefreshSession` now means refreshing on demand, and the refresh timer is opt-in** — with it on (the default) the client refreshes before a request when the access token is about to expire, and once after the service rejects it; `BackgroundRefresh` (`WithBackgroundRefresh`, default off) adds the old idle timer. Turning `AutoRefreshSession` off now disables refreshing altogether. Migration: code that turned it off only to avoid the timer can drop the setting (#124)
- **`LogoutAsync` throws when the service-side sign-out fails** — after the session has been removed locally and from the store, a failed `deleteSession` or OAuth revocation (or store removal) is rethrown instead of logged, and an OAuth session installed without its `OAuthClient` throws `InvalidOperationException`, since it could not be revoked. Migration: catch `XrpcException` / `OAuthException` around `LogoutAsync` where a best-effort sign-out is wanted (#124)
- **Blazor `AtProtoOAuthServerOptions.ClaimsFactory` takes an `OAuthSession`** — Migration: read `session.Did.Value` and `session.Handle.Value` where `result.Did` and `result.Handle` were read (#124)
- **Ozone: `ModEventMuteReporter.DurationInHours` is `int?`, and `ModEventReport.ReportType` is `required`** — matching the Lexicon, where the duration is optional (absent for a permanent mute) and the report type is required; a report event without one no longer reads. Migration: read `DurationInHours` as nullable (`null` is permanent), and set `ReportType` when constructing a `ModEventReport` (#131)
- **Chat messages, log entries and message embeds are typed unions** — `GetMessagesResponse.Messages`, `EnumerateMessagesAsync` and `ConvoView.LastMessage` are `ConvoMessage`s (`MessageView`, `DeletedMessageView`, `SystemMessageView`, …) instead of `JsonElement`s. `ConvoLogEntry` is the abstract base of one type per `getLog` event (`LogCreateMessage`, `LogAddMember`, … and `UnknownConvoLogEntry`) and keeps only `Rev` and `ConvoId`, losing `Type` and `Message`. `MessageInput.Embed` and `MessageView.Embed` are `MessageEmbed` / `MessageEmbedView`, their `Facets` are `Facet`s, and `MessageView.Text` is required. Migration: pattern-match instead of reading `$type` (`msg is MessageView m`, `entry is LogCreateMessage { Message: MessageView m }`), and embed a post as `new MessageRecordEmbed { Record = strongRef }` (#128)
- **`ListConvosAsync` / `EnumerateConvosAsync` take `kind` and `lockStatus` before `limit` / `pageSize`** — the new filters go with the others. Migration: pass `limit`, `cursor` and `pageSize` by name (#128)

### Added

- **`TidGenerator`** — mints TIDs that strictly increase and never repeat, per the TID spec: one clock identifier per generator (random unless given) and a microsecond timestamp that advances by one when the clock has not moved or steps back. It is thread-safe and takes a `TimeProvider` for tests; `Tid.Next()` and `RecordKey.NewTid()` use a process-wide instance. (#114)
- **Generic parsing for identifiers** — all identifier types, including `SpaceUri` and `SpaceRecordUri`, implement `IParsable<T>`, `ISpanParsable<T>` and `IComparable<T>` (ordinal) and convert explicitly from `string`, so minimal-API parameter binding and generic `T.Parse` code accept them. The interface `Parse` members throw `FormatException`; the types' own `Parse` methods keep throwing `ArgumentException`. (#114)
- **`Cid.Codec`, `Cid.Digest` and `Cid.ToBytes()`** — the codec (`CidCodec.DagCbor` or `CidCodec.Raw`), the 32-byte SHA-256 digest, and the 36-byte binary CID. (#114)
- **Open unions and unknown-field round-tripping for your own Lexicons** (#115) — mark a union base `[AtProtoUnion(typeof(UnknownX))]` to get the same forward compatibility as the SDK's unions, and derive models from `LexObject` to keep fields they do not declare. See "Unions and unknown fields" in `docs/custom-records.md`.
- **`app.bsky.embed.gallery`** (#115) — `GalleryEmbed` / `GalleryImage` and `GalleryView` / `GalleryViewImage`, registered as post embeds, post-view embeds, and `recordWithMedia` media.
- **`ProfileRecord.Pronouns`, `Website`, `Labels` and `JoinedViaStarterPack`** (#115).
- **`SpaceTokens.Verify(SpaceToken, …)`** — verifies a token already returned by `SpaceTokens.Parse`, for a verifier that has to read the token's `iss` and `kid` before it knows which key to check. The space server's delegation-token and credential verifiers now use it, so each token is parsed once instead of twice (#118)
- **Per-call XRPC options** — `XrpcCallOptions` sets `Proxy`, `AcceptLabelers`, extra `Headers` and a `Timeout` (which throws `TimeoutException`) for one call without touching the client, so concurrent callers on a shared client no longer race on `SetProxy` / `SetLabelers`. The client-wide setters stay as defaults (#117)
- **`AtProtoClientOptions.UserAgent` and `AtProtoClientOptions.RateLimit`** — the `User-Agent` sent with every request (default `ATProtoNet/<version>`; `null` leaves the `HttpClient`'s own), and `XrpcRateLimitOptions` (`MaxRetries`, default 3; `MaxDelay`, default 30 s) for 429 handling (#117)
- **`XrpcErrors`** — constants for the generic XRPC error names and the common `com.atproto.*` ones (`RecordNotFound`, `InvalidSwap`, `ExpiredToken`, …), for matching with `XrpcException.Is`. `JetstreamConnectException` gains `Error` for the XRPC error name a refused request carried (#117)
- **XRPC procedures with no input or a binary body** — `IXrpcProcedure<TOutput>` and `IXrpcProcedureVoid` serve methods that take no input (any body is ignored), and `IXrpcBlobProcedure<TOutput>` serves blob-upload-style methods, receiving the unbuffered request body in `XrpcBlobInput` (`Content`, `ContentType`, `ContentLength`) (#121)
- **XRPC handler attributes apply to their endpoint** — `[Authorize]`, `[AllowAnonymous]`, `[EnableRateLimiting]`, `[RequestSizeLimit]` and any other attribute on a handler class become that endpoint's metadata, so `[AllowAnonymous]` exempts one handler from a group-wide `RequireAuthorization()` (#121)
- **`AtDatetime`** — the Lexicon `datetime` type. It keeps the exact text it was read with, so a record written back keeps its CID; it reads leniently (an invalid value is kept, with `IsValid`, `TryGetValue` and `Value` exposing the parse) and is strict when created from code (`Parse`, `Now()`, `FromDateTimeOffset`, `FromDateTime`, which write `yyyy-MM-ddTHH:mm:ss.fffZ`). It implements `ISpanParsable<T>`, orders by instant and is checked against the `atproto-interop-tests` datetime fixtures (#119)
- **`ICursorPage<T>` and `Enumerate*` for `com.atproto.*`** — `RepoClient.EnumerateMissingBlobsAsync`, `SyncClient.EnumerateBlobsAsync` / `EnumerateReposAsync` / `EnumerateReposByCollectionAsync` / `EnumerateHostsAsync`, `LabelClient.EnumerateLabelsAsync` and `AdminClient.EnumerateInviteCodesAsync` join `EnumerateRecordsAsync`, all over one paginator (#119)
- **AT URI overloads** — `RepoClient.GetRecordAsync(AtUri)`, `GetRecordAsync<T>(AtUri)` and `DeleteRecordAsync(AtUri)` address a record by its URI (#119)
- **`ICursorPage<T>` and `Enumerate*` for `app.bsky.*`** — every cursored `app.bsky` response implements `ICursorPage<T>`, and the timeline, author, custom and list feeds, actor likes, post likes, reposts and quotes, followers, follows, known followers, blocks, mutes, lists, list members, list blocks and mutes, actor feeds and starter packs, notifications, suggestions and the post, actor and starter-pack searches each have an `Enumerate*` method over the shared paginator (#119)
- **`Enumerate*` for chat and Ozone, and typed `site.standard` listings** — `ConvoClient.EnumerateConvosAsync` / `EnumerateMessagesAsync` / `EnumerateLogAsync`, `ModerationClient.EnumerateEventsAsync` / `EnumerateSubjectsAsync` / `EnumerateReposAsync`, `SetClient.EnumerateSetsAsync` / `EnumerateValuesAsync`, `TeamClient.EnumerateMembersAsync` and `StandardSiteClient.EnumeratePublicationsAsync` / `EnumerateDocumentsAsync` / `EnumerateSubscriptionsAsync` walk every page on the shared paginator. `StandardSiteClient.GetPublicationAsync` / `GetDocumentAsync` / `GetSubscriptionAsync` also take the record's `AtUri`, such as a subscription's `Publication` (#119)
- **Space enumerators and record-URI overloads** — `SpaceClient.EnumerateSpacesAsync` and `EnumerateBlobsAsync` join `EnumerateReposAsync`, `EnumerateRecordsAsync` (which gains `reverse`) and `SimpleSpaceClient.EnumerateMembersAsync`, and `SpaceClient.GetRecordAsync(SpaceRecordUri)` / `DeleteRecordAsync(SpaceRecordUri)` address a record by its space record URI (#119)
- **A drift test against a pinned snapshot of the upstream Lexicons** — `tests/ATProtoNet.Tests/Lexicon/Upstream` vendors bluesky-social/atproto's Lexicons (at `2e583a4`) and standard.site's published schemas, and checks every XRPC call's NSID and HTTP method, every NSID in the source, every model's JSON names against its upstream def in both directions, the permission-set constants and the knownValues classes, each with a commented allow-list. A convention test requires an explicit camelCase `[JsonPropertyName]` on every Lexicon model property; together they replace 54 tests that only serialized a model to look at its property names (#123)
- **Profile, viewer and suggestion fields** — the three profile views gain `Pronouns`, `Associated` (`ProfileAssociated`: lists, feeds, starter packs, labeler, chat, activity subscriptions, Germ), `Verification` (`VerificationState` / `VerificationView`, with `VerificationStatus`), `Status` (`StatusView`) and `Debug`, and `ProfileViewDetailed` also `Website` and `JoinedViaStarterPack`. `ViewerState` gains `MutedOnlyReposts`, `MutedOnlyQuoteposts` and `ActivitySubscription`; the suggestion responses gain `RecIdStr` (#123)
- **Post, feed and embed fields** — `PostView.BookmarkCount` and `Debug`; `PostViewerState.Bookmarked` and `KnownLikers`; `FeedViewPost.ReqId` and `GetFeedSkeletonResponse.ReqId`; `ThreadViewPost.ThreadContext`; `ContentMode` on feed generator views and records (`FeedContentMode`); `Via` on like, repost and follow records; `Presentation` on video embeds and views (`VideoPresentation`); `ExternalInfo.AssociatedRefs`, and the dates, reading time, labels, `Source` (`ExternalViewSource`, with its theme colors) and associated records and profiles of `ExternalViewInfo` (#123)
- **Notification, graph, labeler and video fields** — `NotificationView.StarterPack`, six more `NotificationReasons` (`Verified`, `Unverified`, `LikeViaRepost`, `RepostViaRepost`, `SubscribedPost`, `ContactMatch`) and `RegisterPushRequest.AgeRestricted`; `Relationship.Blocking` / `BlockedBy` / `BlockingByList` / `BlockedByList`, `ListItemView.SubjectOptedOut` and `ListViewerState.ReferenceListOptOut`; the labeler service record's `Labels`, `ReasonTypes`, `SubjectTypes` and `SubjectCollections` (the last three also on `LabelerViewDetailed`); `JobStatus.FailureCode` (`JobFailureCode`) and the `Encoded`, `Scanned`, `Uploading` and `Uploaded` job states (#123)
- **Well-known label values** — `StandardLabelValues.Hide` (`!hide`), `Warn` (`!warn`) and `Bot`, the global values the SDK lacked (#123)
- **Permission sets** — `AtProtoScopes.PermissionSets.DeleteContent`, `FullChatClient` (`chat.bsky.authFullChatClient`), `StandardSiteFull` and `StandardSiteSocial` (#123)
- **Chat, `com.atproto` and `site.standard` fields** — `ChatDeclarationRecord.AllowGroupInvites`; `ChatMemberView.Viewer`, `CreatedAt` and `Verification`; `GetMessagesResponse.RelatedProfiles`; `GetConvoAvailabilityResponse.Convo`; `DescribeServerResponse.BlobUploadLimit`; `AccountInfo.InviteNote`; `MissingBlob.RecordUri`; the report's `ModTool`; `DocumentRecord.Contributors` (`DocumentContributor`), `Links` and `Labels`, `PublicationRecord.Labels` and `SubscriptionRecord.CreatedAt` (#123)
- **Ozone fields and filters** — events carry their policies, severity level, strikes, target services and whether an email was delivered, and `ModEventLabel` / `ModEventTag` a `DurationInHours`; `ModEventView` and `ModEventViewDetail` name their `ModTool`; `SubjectStatusView` gains `Hosting` (`AccountHosting` / `RecordHosting`), `PriorityScore`, `AccountStats`, `RecordsStats`, `AccountStrike` and the age-assurance state; `ModerationSubject` reads chat messages and conversations (`MessageSubject`, `ConvoSubject`); `EmitEventRequest` gains `ModTool`, `ExternalId` and `ReportAction`; plus `RecordViewDetail.Labels`, `OzoneServerConfig.VerifierDid`, the templates' `Lang`, `TeamMemberRole.Verifier`, and the missing `queryEvents`, `querySets` and `listMembers` filters (#123)
- **`AtProtoClient.SessionChanged`** — raised when a session is created, refreshed, expires (its refresh token was refused) or is signed out, with the new and previous session and, for an expiry, the error (#124)
- **Signing in to a taken-down account** — `LoginAsync` and `ServerClient.CreateSessionAsync` take `allowTakendown` (the Lexicon's `allowTakendown`, sent only when true), which lets a taken-down account sign in to a session limited to migrating or exporting it. The parameter comes before the cancellation token, so a token passed positionally is now passed by name (#124)
- **`CreateAccountAndLoginAsync` and `TryRestoreSessionAsync`** — the first creates an account and installs its session; the second installs the session the client's store holds for a DID, without a request (#124)
- **OAuth revocation** — `OAuthClient.RevokeAsync(session)` revokes a session at its authorization server (RFC 7009, with a DPoP proof), and `OAuthSession.RevocationEndpoint` records the endpoint from the server's metadata (#124)
- **Signing in through an entryway lands on the account's PDS** — `LoginAsync`, `CreateAccountAndLoginAsync` and a refresh follow the `#atproto_pds` endpoint of the DID document the service returns, when it is the account's own document and an HTTPS URL, as the reference client does. A development PDS reached over plain HTTP is not followed (#124)
- **Ozone reports and queues** — `client.Ozone.Report` covers all of `tools.ozone.report`: `QueryReportsAsync` (a status plus a `ReportFilter`) / `EnumerateReportsAsync`, `GetReportAsync`, `GetLatestReportAsync`, moderator assignment, report activities (`CreateActivityAsync` with a typed `ReportActivity`: queue, assignment, escalation, close, reopen, note), `CloseReportsAsync`, `ReassignQueueAsync` and the live and daily statistics. `client.Ozone.Queue` covers all of `tools.ozone.queue`: creating, updating, listing and deleting custom queues, `RouteReportsAsync` and queue assignments. See "Reports" and "Queues" in `docs/ozone.md` (#131)
- **Ozone settings, URL safety rules, verifications and account history** — `client.Ozone.Setting` (`tools.ozone.setting`), `Safelink` (`tools.ozone.safelink`: rules and their audit log), `Verification` (`tools.ozone.verification`: grant, revoke and list) and `Hosting` (`tools.ozone.hosting.getAccountHistory`, with typed history details), with `Enumerate*` for every listing and constants for their known values (#131)
- **Ozone moderation lookups and scheduled actions** — `ModerationClient` gains `GetAccountPreferencesAsync`, `GetReposAsync`, `GetRecordsAsync`, `GetSubjectsAsync`, `GetAccountTimelineAsync`, `GetReporterStatsAsync`, `ScheduleActionAsync`, `ListScheduledActionsAsync` / `EnumerateScheduledActionsAsync` and `CancelScheduledActionsAsync`. The batch lookups return the new open `ModerationSubjectView` union: `RepoViewDetail` and `RecordViewDetail` derive from it (and so now write their `$type`), next to `RepoViewNotFound` and `RecordViewNotFound` (#131)
- **The Ozone event types the SDK lacked** — `ModEventType` gains `ModEventResolveAppeal`, `ModEventPriorityScore`, `AccountEvent`, `IdentityEvent`, `RecordEvent`, `AgeAssuranceEvent`, `AgeAssuranceOverrideEvent`, `AgeAssurancePurgeEvent`, `RevokeAccountCredentialsEvent`, `ScheduleTakedownEvent` and `CancelScheduledTakedownEvent`. The account, identity and record events, which Ozone records for every subject on its own, read typed instead of as `UnknownModEvent` (#131)
- **Granular report reasons** — `ReportReasons` gains the 40 `tools.ozone.report.defs#reason*` values upstream now prefers, grouped by category (`ViolenceThreats`, `SexualNcii`, `ChildSafetyCsam`, `MisleadingSpam`, `RuleBanEvasion`, …, plus `OzoneAppeal` and `OzoneOther`), and each coarse `com.atproto.moderation.defs#reason*` constant names its replacement (#131)
- **Group chats** — `client.Chat.Group` (`GroupClient`) covers all 16 `chat.bsky.group` methods: creating and renaming groups, adding and removing members, mutual groups, join links (create, edit, enable, disable, preview) and join requests (request, withdraw, list, approve, reject, mark read). A conversation's `Kind` is a `DirectConvo` or a `GroupConvo` (name, size, lock status, join link, join request counts), a member's `Kind` a `DirectConvoMember`, `GroupConvoMember` (role, who added it) or `PastGroupConvoMember`, and a `JoinLinkEmbed` shares a group in a message (#128)
- **Conversation endpoints and message fields** — `ConvoClient.GetUnreadCountsAsync`, `ListConvoRequestsAsync` / `EnumerateConvoRequestsAsync` (conversation requests and your own group join requests), `GetConvoMembersAsync` / `EnumerateConvoMembersAsync` and `LockConvoAsync` / `UnlockConvoAsync`, and `kind` and `lockStatus` filters on `ListConvosAsync`. Messages gain `Reactions` and `ReplyTo`, `MessageInput` gains `ReplyTo`, `ConvoView` gains `LastReaction`, and group events arrive as `SystemMessageView`s with twelve typed kinds of data. The known values are constants: `ConvoStatus`, `ConvoKinds`, `ConvoLockStatus`, `ConvoReadState`, `ChatMemberRole`, `JoinRule`, `JoinLinkEnabledStatus` and `RequestJoinStatus` (#128)
- **Chat status, notification preferences and moderation** — `ChatActorClient.GetStatusAsync` says whether the account may chat and create groups; `client.Chat.Notification` reads and sets the chat notification preferences that replace the deprecated `chat` entry of the Bluesky ones; `client.Chat.Moderation` wraps six `chat.bsky.moderation` methods for moderation services (conversation and member lookups, message context, actor metadata and chat access), which follow the client-wide proxy instead of the chat service's (#128)

### Changed

- **Shared build settings are set once** — the target framework, nullable reference types, implicit usings and the package metadata now come from `Directory.Build.props`, with `src/`, `tests/` and `samples/` layers for documentation files and `IsPackable`, instead of being repeated in every project file. The published package metadata is unchanged apart from the two dependencies above (#110)
- **Aspire hosting: one shared base for the two PDS container resources.** `AtProtoPdsContainerResource` and `AtProtoTranquilPdsContainerResource` now derive from `AtProtoPdsContainerResourceBase` (`HttpEndpointName`, `HealthCheckPath`, `JwtSecretParameter`, `ConnectionStringExpression`), so one AppHost helper constrained to the base covers either server. Every `With*` method and all Tranquil support are kept, and both servers produce the same environment, mounts, endpoints and publish manifest as before. (#116)
- **`MerkleSearchTree.SerializeProof` returns the reference covering proof** (#111) — besides the path to each key it now includes the nodes on the paths to the key's immediate neighbours, exactly as `getCoveringProof` in the reference implementation does. Relays need those nodes to replay a `#commit` in reverse; the result is a superset of the previous path-only proof
- **`MerkleSearchTree.Validate` checks the loaded tree is canonical** (#111) — after `Deserialize` it rebuilds the tree from its entries and throws `InvalidOperationException` if the root differs from the one it was loaded from. A tree built in memory is canonical by construction
- **Faster DAG-CBOR decoding and encoding** (#111) — `DagCborDecoder.Decode` and the firehose parser share one transcoder that writes JSON directly instead of building a `JsonNode` tree first, and the encoder no longer sorts every map twice. Encoder output is byte-for-byte unchanged
- **Null-safe identifier conversions** — implicit conversions from an identifier to `string`, and from `Did`/`Handle` to `AtIdentifier`, return `null` for a `null` input instead of throwing or building an empty `AtIdentifier`. (#114)
- **`SpaceUri` and `SpaceRecordUri` serialize to JSON as their URI string**, like every other identifier, instead of as an object that could not be read back. (#114)
- **A union variant always writes its `$type`** (#115) — also where it is not in union position, such as `RecordWithMediaEmbed.Record`, which the data model allows on any object. A `$type` read there is therefore written back rather than dropped, and one is added where it was absent.
- **Faster BLAKE3 and `LtHash`** — BLAKE3 now compresses XOF output blocks 8 (or 4) at a time with hardware vectors and allocates nothing, and `LtHash` adds and subtracts its lanes with vectors. `LtHash.Add`/`Remove` are about 16× faster and allocation-free, which makes verifying a space repo's index (`SpaceRepoCommit.FromIndex`, `SpaceRepoCar.Verify`) about 12× faster. The output is bit-identical, and hosts without vector hardware or with big-endian byte order take a scalar path (#112)
- **`AtProtoCrypto.VerifySignature` caches parsed `did:key`s** — up to 1,024 keys, least recently used evicted first, so repeated verification against the same signer skips base58 decoding, point decompression and key import. P-256 verification against a known key is about twice as fast; every verifier built on it (space tokens and commits, service auth, `FirehoseVerifier`) benefits without code changes (#112)
- **Minting JWTs allocates far less** — a DPoP proof encodes its header once per key, hashes the access token once per token rather than on every request, and writes its claims straight to UTF-8 instead of through dictionaries and reflection. A proof now allocates about 2.5 KB instead of 11 KB and is about 15% faster; `ServiceAuthGenerator.CreateToken` and `SpaceTokens.Create` are built the same way and allocate 30–50% of what they did (#118)
- **One `jti` format for every token the SDK mints** — DPoP proofs, service-auth tokens and space tokens all carry 128 bits from the system CSPRNG as 32 lower-case hex characters; DPoP and service auth used a GUID, which has the same shape (#118)
- **Compressed responses and bounded connection lifetimes** — every `HttpClient` the SDK creates shares one handler with `AutomaticDecompression`, a 5-minute `PooledConnectionLifetime` (so DNS changes behind a PDS are picked up) and a 10 s connect timeout, and the named clients registered by `AddAtProtoServer`, `AddAtProto`, `AddAtProtoScoped`, `AddAtProtoClient`, `AddAtProtoSpaces` and `AddAtProtoPdsAdmin` get the same settings. The SDK used to send no `Accept-Encoding`, so JSON arrived uncompressed (#117)
- **JSON responses are deserialized as they arrive** — instead of being buffered whole first, so a large page (a timeline, a `listRecords` page) no longer allocates a large-object-heap buffer on every call (#117)
- **Rate-limit waits are capped** — a 429 asking for longer than `RateLimit.MaxDelay` (30 s by default) throws `XrpcRateLimitException` with `RetryAfter` and `RateLimit` at once, where a daily window used to hold the call for hours. `Retry-After` is also read in its HTTP-date form, and retries carry up to 10 % jitter. Set `MaxRetries = 0` when the `HttpClient` already has a retrying resilience handler (#117)
- **`atproto-proxy` and `atproto-accept-labelers` are sent without a session too**, so public AppView reads carry labeler subscriptions; the session calls (`createSession`, `refreshSession`, `getSession`, `deleteSession`, `createAccount`) are never routed through the client-wide proxy (#117)
- **The Aspire client's named `HttpClient` no longer sets `User-Agent: ATProtoNet.Aspire/1.0`**, and the Blazor OAuth service no longer adds one to a caller-supplied `HttpClient` — the client sets it per request instead (#117)
- **`SpaceReader`s share their provider's `HttpClient`** rather than each creating one, as the transport no longer needs a `BaseAddress` per host (#117)
- **Service base URLs with a query or fragment are refused** — `AtProtoClientOptions.InstanceUrl`, `SetServiceUrl`, `PdsAdminOptions.Url`, the `PlcClient` directory URL and a space reader's host URL throw `ArgumentException`, where the query used to be kept in front of every request path. A DID document publishing such an endpoint fails credential minting with `SpaceCredentialException`, and a managing-app check against one is a refusal (#117)
- **Unmatched `/xrpc/{nsid}` requests answer with an XRPC error, as the reference `xrpc-server` does** — an NSID no handler serves answers `501 MethodNotImplemented` instead of an empty 404, a registered NSID called with the wrong HTTP method keeps its `405` but now carries `Allow` and an `InvalidRequest` body, and a path segment that is not an NSID answers `400 InvalidRequest`. Routes an application maps under `/xrpc` itself still take precedence (#121)
- **Stream and space readers check identifiers where they read them** — a firehose frame whose `repo`, `commit`, `rev` or other identifier does not parse is dropped like any other malformed frame; a Jetstream commit whose `collection` or `rkey` does not parse is skipped (an unparseable `rev` or `handle` is left `null` instead); `SpaceRepoCar.Verify` refuses an index path that is not a collection NSID and a record key, or an index link that is not a CID; `SpaceTypeDeclaration.FromLexicon` refuses a `collections` entry that is not an NSID, such as a `*` wildcard; a delegation token or service auth token whose `iss` is not a DID is refused; and the space endpoints refuse a malformed `since` or `cid` parameter with `InvalidRequest` where they used to pass it through (#119)
- **Deprecated upstream, now `[Obsolete]`** — `SyncClient.NotifyOfUpdateAsync` (use `RequestCrawlAsync`), `ListNotificationsResponse.Priority` and `GetSuggestedFollowsByActorResponse.IsFallback`, which the appview no longer fills, and `StandardLabelValues.Gore` and `ContentWarning`, which are not global label values, and `NotAvailable`, which was always the same string as `NoUnauthenticated` (#123)
- **`DisposeAsync` is the primary way to dispose `AtProtoClient`, and `Dispose` returns at once** — a token exchange already under way is never abandoned, since the authorization server has already rotated the refresh token: `DisposeAsync` waits for it to finish and be stored (within its 30-second limit), and `Dispose` lets it finish in the background and releases the DPoP key afterwards. The client disposes only what it created: never a supplied `HttpClient`, `OAuthClient` or session store, and it no longer takes ownership of an applied OAuth session. The thread-safety and ownership contract is documented in `docs/session-management.md` (#124)
- **A session store that fails to write no longer discards a successful refresh** — the refreshed session stays installed and the error is logged, since the old refresh token is already spent; the store catches up on the next write. Store writes that follow a session change are not cancelled with the call, so the store, the client and `SessionChanged` always agree (#124)
- **Blazor sign-out revokes the OAuth session** — `/atproto/logout` revokes the stored session at the authorization server before removing it; a failed revocation is logged (#124)

### Fixed

- **`atproto-lexgen --version` reports the tool's real version** — it printed a hard-coded `1.0.0`; it now prints the assembly's informational version, and so does the `--help` banner (#110)
- **The managing-app check sent `did=` where the Lexicon says `user`** — so every `ManagingAppPolicy` space refused everyone against a Lexicon-validating managing app such as bulletin. `checkUserAccess` now sends `user` and `access`, omits `clientId` on write checks, and addresses its service auth to the managing app's full service identifier (`did:web:…#forum`) rather than its bare DID (#113)
- **`notifyWrite` admitted any writer** — the authority now evaluates the write policy (the owner is always admitted, and app access is not applied) and answers 403 to a refused writer, which is neither recorded in the writer set nor forwarded. `listRepos` therefore lists only admitted writers, as upstream's authority already did (#113)
- **The authority never forwarded write notifications to the services registered with `registerNotify`** — an accepted `notifyWrite` is now fanned out in the background through `SpaceWriteNotifier.ForwardWriteAsync`, skipping the authority's own registration (#113)
- **Write notifications to an authority on an ordinary PDS were dropped** — `{authority}#atproto_space_host` now resolves with the `#atproto_pds` fallback (`SpaceAuthority.GetServiceEndpoint`), and the notification is addressed to the bare authority DID the reference authority checks. Before, an authority without a dedicated space-host entry never heard from SDK repo hosts (#113)
- **Outbound notifications addressed fragment-bearing subscribers by their bare DID** — each delivery's `aud` is now the service identifier the subscriber registered (e.g. `did:web:syncer.example.com#atproto_space_syncer`), which is what a syncer verifies (#113)
- **An authority configured with a `ServiceDid` refused notifications from reference PDSes** — inbound `notifyWrite` now accepts `aud` equal to the authority's DID, `{authority}#atproto_space_host`, or `ServiceDid` (#113)
- **Malformed `simplespace` and `notifyWrite` input is refused up front** — `notifyWrite` answers `InvalidRequest` before any auth check for a `rev` that is not a TID or a missing `hash`, and `createSpace` refuses a `null` policy instead of storing it (#113)
- **`docs/testing-spaces.md` claimed there is no spaces container image** (repeated in the 0.6.0 notes below) — the alpha is published as `ghcr.io/bluesky-social/atproto:pds-spaces-alpha`, as `@atproto/pds@alpha` / `@atproto/dev-env@alpha`, and as the invite-only `spaces-alpha.host.bsky.network`. The page now pins the image digest and the source commit, and gives a tested npm recipe for the dev network (#113)
- **MST root CIDs now match every other implementation** (#111) — nodes wrote absent `l`/`t` links by omitting them instead of as `null`, an empty subtree became an empty node instead of a null link, and `Add`/`Delete` produced a different tree than a bulk build of the same entries. Every root differed from the network's, so repos written with `MerkleSearchTree` + `RepoCommit` + `CarWriter` were rejected elsewhere. The tree is now derived from its sorted entries, so any sequence of edits yields the canonical root, pinned against the reference implementation's test vectors and interop fixtures
- **`MerkleSearchTree.Deserialize` reads real repositories** (#111) — the `null` links every other implementation writes threw, so no tree served by `com.atproto.sync.getRepo` could be loaded
- **DAG-CBOR key order for non-ASCII keys** (#111) — keys of equal UTF-8 length were compared in UTF-16 order, which differs from byte order where a character above U+FFFF meets one in U+E000–U+FFFF. This affected `SpaceRepoCar`'s record order and `DagCborDecoder.TryValidate`
- **`SpaceRepoCar.Verify` reports a malformed index block as `SpaceRepoVerificationException`** (#111) — a truncated index used to escape as `CborContentException`
- **`Tid.Next()` was neither monotonic nor unique** — it added a random clock id to a millisecond timestamp on every call, so values minted in the same millisecond came out of order and could collide (9,992 of 20,000 out of order, 22 duplicates). It is now strictly increasing, including across threads, and `RecordKey.NewTid()` inherits the fix. (#114)
- **Identifier types accepted invalid values** — AT URIs with an invalid authority, collection or record key, a trailing slash, a query or a fragment, record keys containing `@!$&'()*+,;=`, TIDs whose first character sets the high bit, DIDs ending in `%`, NSID names over 63 characters, and any string at all as a `Cid`. Every line of the interop syntax fixtures for TIDs, AT URIs, DIDs, NSIDs, handles, record keys and AT identifiers now parses or fails as specified; the fixtures are vendored in the test project. (#114)
- **Invalid identifiers in JSON escaped as `ArgumentException`** — they now fail as a `JsonException` carrying the JSON path, with the reason in its inner `FormatException`. (#114)
- **One gallery post no longer fails a whole feed** (#115) — `app.bsky.embed.gallery` was not a known embed, so a single gallery post made `GetTimelineAsync`, `GetFeedAsync`, `GetAuthorFeedAsync`, `GetPostThreadAsync`, `GetPostsAsync` and `SearchPostsAsync` throw `JsonException` for the entire page. The same applied to any other new union variant, such as the event types a current Ozone emits.
- **Signed labels deserialize** (#115) — `Label.Sig` did not read the Lexicon `{"$bytes": …}` form, so `QueryLabelsAsync` against `mod.bsky.app`, and any view carrying signed labels, threw.
- **`UpdateProfileAsync` no longer deletes profile data** (#115) — it rebuilt the record from four fields, dropping `pinnedPost`, `pronouns`, `website`, `labels`, `joinedViaStarterPack` and any newer field, and could overwrite a concurrent edit. It now edits the stored record in place, writes with `swapRecord`, and retries up to three times on `InvalidSwap`.
- **Unknown record fields survive a read-modify-write** (#115) — fields an SDK model or `AtProtoRecord` subclass did not declare were dropped when a record read through `RecordCollection<T>` or `GetRecordAsync<T>` was written back.
- **Registered union variants take effect** (#115) — `LexiconTypeRegistry.RegisterUnionVariant` only affected `CreateOptions()`, which no client used; `AtProtoJsonDefaults.Options` now consults the registry.
- **Jetstream `sync` events accept unpadded `$bytes`** (#115) — the parser and the archive segment reader rejected unpadded base64, which the data model specifies, and delivered the event with `Blocks` set to `null`.
- **Malformed keys fail consistently** — a `did:key` or multikey whose key is not a 33-byte compressed point now throws `FormatException`, as documented, instead of `ArgumentException`. An undefined `KeyCurve` value now throws `ArgumentOutOfRangeException` instead of being treated as K-256 (#112)
- **A K-256 key no longer signs DPoP proofs labelled ES256** — `new DPoPProofGenerator(byte[])` imported any EC key, so a stored secp256k1 key produced proofs whose header claimed `ES256` and `P-256`, which every server rejects. It now throws `ArgumentException` for a key that is not P-256, since AT Protocol DPoP is ES256 only (#118)
- **DPoP proofs no longer put userinfo in `htu`** — a proof for `https://user:pw@host/path` named `user:pw@` in its `htu`, which RFC 9110 keeps out of a target URI and the SDK's own validator strips, and which carried the credentials to the server. The generator and `DPoPProofValidator` now share one normalization, `scheme://host[:port]/path` with the scheme and host lower-cased and no default port, query, fragment or userinfo, and the RFC 7638 thumbprint and `ath` are likewise computed by one implementation. `GenerateProof` throws `ArgumentException` for a URL that is not absolute with a host, instead of sending it verbatim in a proof no server accepts (#118)
- **`SetPdsUrl` and `ApplyOAuthSessionAsync` threw once the `HttpClient` had sent a request** — they wrote `HttpClient.BaseAddress`, which .NET forbids after first use, so re-applying an OAuth session or switching PDS on a reused or DI-provided client failed with `InvalidOperationException`. The service URL is now the client's own (#117)
- **`User-Agent` grew without bound on a shared `HttpClient`** — every `AtProtoClient` and `PdsAdminClient` appended a product token to the `HttpClient`'s default headers (100 clients made a 1,899-character header), racing with requests in flight. The header is now set once per request (#117)
- **Query parameters were formatted with `ToString()`** — `DateTime`/`DateTimeOffset` went out as `09/24/2026 12:00:00 +00:00` (in the current culture) and enums as their C# names. Timestamps are now ISO 8601 UTC with millisecond precision, enums use their JSON names, and anonymous-type properties are read through a per-type cache (#117)
- **Uploads rewound the caller's stream to position 0** — `UploadBlobAsync` / `UploadVideoAsync` now send from, and retry from, the position the stream had, and a non-seekable stream that needs a retry (DPoP nonce, 429) fails with a clear `InvalidOperationException` instead of resending an empty body (#117)
- **`RecordCollection<T>.ExistsAsync` answered `false` for any `InvalidRequest`** — only `RecordNotFound` means absence now; a malformed key or a missing repository throws (#117)
- **A failed blob or repository download leaked its HTTP response** — the response is now disposed on failure, and with the `XrpcStreamResponse` on success (#117)
- **`AtProtoJsonDefaults.FormatTimestamp` and `NowTimestamp` followed the current culture** — under a Thai locale they wrote the Buddhist-calendar year (`2569-09-24T…`), and other locales can change the time separator, so every `createdAt` the SDK stamped (posts, likes, profiles, records) could be invalid. They now format in the invariant culture (#117)
- **A single value for an array query parameter failed to bind** — `?uris=x` against a `List<string>` or an array answered `InvalidRequest`. Parameters now bind by the property's declared type: a collection takes one value or many (`uris[]=` is accepted too), a repeated scalar is refused, `bool` binds without `XrpcBooleanConverter`, and `Did`, `AtUri`, `Nsid` and the other identifier types are validated by their parsers. A value that does not bind answers `InvalidRequest` naming the parameter rather than echoing the serializer's message (#121)
- **Every XRPC endpoint failure answers with the XRPC error envelope** — an exception other than `XrpcException` produced ASP.NET Core's default 500 response; it now answers `500 {"error":"InternalServerError"}` without the exception's message, and is logged under `ATProtoNet.Server.Xrpc`. A request body over the size limit answers `413 PayloadTooLarge`, a JSON procedure called with another `Content-Type` answers `InvalidRequest` saying so, and a handler returning a null output answers 500 rather than a `null` body (#121)
- **`Enumerate*` stopped only on a `null` cursor** — a server that repeated its cursor drove an endless request loop. Every enumerator in `com.atproto.*` and `RecordCollection<T>` now stops on a `null`, empty or repeated cursor; the spaces and Jetstream archive enumerators move onto the same paginator in the later parts of #119 (#119)
- **`AtProtoJsonDefaults.FormatTimestamp` read `DateTimeKind.Unspecified` as local time** — its documentation said UTC; it now treats it as UTC, like `XrpcParams` and `AtDatetime.FromDateTime` (#119)
- **`AtProtoAuthenticationHandler` issued an empty `did` claim** — the session it validated through `getSession` kept the placeholder DID it was created with. The `did` and `NameIdentifier` claims now carry the token's subject, and a token whose `sub` is not a DID is rejected before the PDS round trip (#119)
- **Space and Jetstream archive enumerators looped on a repeated cursor** — `SpaceClient.EnumerateReposAsync` / `EnumerateRecordsAsync`, `SimpleSpaceClient.EnumerateMembersAsync` and `JetstreamArchiveClient`'s segment enumerator stopped only on an empty page, so a host answering every page with items and the same cursor was asked for that page forever. They now run on the shared paginator, which stops on a `null`, empty or repeated cursor (#119)
- **Properties under names the Lexicons never used were always empty** — `GetConvoAvailabilityResponse` read `canConvo` (so it always said `false`) where the service sends `canChat`, `ProfileViewDetailed` read `associatedChat` where it sends `associated.chat`, `RecordViewDetail` read `blobCids` where Ozone sends `blobs`, and the `listConvos` read-state filter went out as `readOnly`, which the service ignored (#123)
- **Ozone's review queue called a method that does not exist** — `QuerySubjectsAsync` requested `tools.ozone.moderation.querySubjects`; `QueryStatusesAsync` requests `queryStatuses` (#123)
- **`SearchAccountsAsync` POSTed a query** — `tools.ozone.signature.searchAccounts` is a GET with `values` parameters, and the SDK sent a JSON body instead (#123)
- **Two procedures sent no body** — `DeactivateAccountAsync` and `UpdateAllReadAsync` posted nothing, not even a `Content-Type`, where their Lexicons declare a JSON input, and the reference PDS rejects that with "Request encoding (Content-Type) required but not provided". Both now send their JSON body, `{}` when every field is omitted (#123)
- **Five chat methods and the chat export read the wrong response shape** — `muteConvo`, `unmuteConvo` and `updateRead` answer `{convo}`, and `addReaction` and `removeReaction` `{message}`, but the SDK read the view itself, so each failed against the real chat service. `exportAccountData` answers JSON Lines, which the SDK tried to read as a single JSON value (#123)
- **Seven permission-set constants named sets nobody published** — so an OAuth client that included one asked for a scope the authorization server could not resolve (#123)
- **Parameters that did not match their Lexicons** — `SearchPostsAsync` could send one `tag` although the Lexicon takes several, and `ListNotificationsAsync` could still send `seenAt`, which `listNotifications` now rejects (#123)
- **Sign-out revoked nothing** — `LogoutAsync` sent `com.atproto.server.deleteSession` the access JWT, which the Lexicon refuses, and logged the error, so the refresh JWT stayed valid on the PDS; an OAuth session was sent the same call and never revoked. It now sends the refresh JWT, revokes an OAuth session at its authorization server with DPoP, and surfaces a failure after the local teardown (#124)
- **A failed OAuth refresh was stored as a token** — `RefreshTokensAsync` never checked the HTTP status, so an `invalid_grant` body became an empty access token that the client installed, persisted and sent. A refresh now fails with `OAuthException` on an error status or a response without an access token, and on `invalid_grant` the session expires instead (#124)
- **`ResumeSessionAsync` could not refresh on a fresh client** — it threw "No session to refresh" when the saved access token had expired; it now refreshes with the saved refresh token and retries, as documented (#124)
- **A password login after an OAuth session kept refreshing through OAuth** — the OAuth state was never cleared, and a password session was installed outside the refresh lock. Installing a session now replaces all of the previous one under the lock (#124)
- **Raw sub-clients changed the client's session as a side effect** — `client.Server.CreateSessionAsync` / `CreateAccountAsync` authenticated the transport while `Session` stayed `null`, and `CreateAccountAsync` on a signed-in client silently replaced its tokens (#124)
- **Nothing refreshed after `ExpiredToken` or a DPoP `invalid_token` challenge** — only a timer refreshed, so a woken laptop, a per-request server client or a token revoked early failed every call until the next tick. The client now refreshes and resends once, with the same account's new tokens to the same service; a call whose session was replaced by another account's meanwhile fails instead of being resent as that account (#124)
- **Concurrent refreshes spent the refresh token more than once** — callers that saw an expired token refreshed one after another, so with single-use OAuth refresh tokens all but the first were refused. Concurrent callers now share one refresh, and a caller whose session was refreshed while it waited does not refresh again (#124)
- **App-password sessions were refreshed on a fixed 115-minute timer** — the access JWT's `exp` is read instead, so a PDS with shorter-lived tokens no longer serves expired ones (#124)
- **`Dispose` could block a thread for up to 35 seconds** waiting for an in-flight timer refresh (#124)
- **A permanent reporter mute failed a whole page of Ozone events** — `ModEventMuteReporter.DurationInHours` was required, but Ozone omits `durationInHours` for a permanent mute, so `QueryEventsAsync` and `GetEventAsync` threw on any page that held one. It is now optional (#131)

### Removed

- **`samples/BlazorOAuthSample`** — folded into `samples/ServerIntegrationSample`, which already ran the same OAuth login and adds server-side AT Proto access on top. The remaining sample keeps only `bootstrap.min.css` from the vendored Bootstrap tree (87 files and 17 MB fewer in a checkout), uses Blazor's built-in reconnect UI instead of the template's copy, and drops unused template CSS and imports (#110)

### Security

- **GitHub → Forgejo sync workflows no longer paste event fields into shell scripts** — `sync-issues.yml`, `sync-comments.yml` and `sync-prs.yml` interpolated issue/PR titles, bodies, comment bodies, file paths and branch names into `run:` scripts with `${{ }}`, which is expanded into the script text before bash parses it. Anyone able to open an issue, PR or comment on the public GitHub mirror could therefore run commands with `FORGEJO_TOKEN` in the environment; wrapping a body in a quoted heredoc did not help, since the body could contain the delimiter. Every event field now reaches the script only through `env:` and a quoted variable, and each workflow sets `permissions: {}`. **Rotate `FORGEJO_TOKEN`** (it has write access to the canonical repository) as well as `GH_MIRROR_TOKEN` (see the next entry)
- **`sync-to-github.yml` (Forgejo) had the same injection through issue and PR titles** — and a GitHub issue title reaches it verbatim through the mirror, so a hostile title opened on GitHub ran with `GH_MIRROR_TOKEN` as soon as the mirrored issue was closed. Fixed the same way
- **`sync-prs.yml` never checks out the PR anymore** — it ran on `pull_request_target` (secrets available regardless of who opened the PR) and checked out the PR head with `actions/checkout`, which also persisted a GitHub token in `.git/config`. The PR's commits are now fetched into a bare repository with no working tree and only handed to `git push`; the Forgejo token is passed to that push rather than saved as a remote, and the Forgejo branch name is reduced to `[A-Za-z0-9._/-]`
- **PRs that change `.forgejo/` or `.gitea/` are no longer mirrored to Forgejo** — a mirrored PR is pushed as a branch of the canonical repository rather than a fork, and Forgejo runs a pushed commit's own workflow files with that repository's secrets (`NUGET_ORG_API_KEY` among them). The sync now refuses, with an error on the GitHub PR, when the PR's CI configuration differs from its base branch; such a PR has to be reviewed and pulled by hand
- **Crafted CAR and DAG-CBOR input no longer crashes the process** (#111) — `CarReader` parsed the CAR header with a recursive hand-written CBOR skipper, so a ~100 KB header of nested arrays overflowed the stack; it now uses `CborReader`. `DagCborDecoder`, `DagCborDecoder.TryValidate` and the firehose frame parser cap nesting at 64 levels, the `System.Text.Json` default, and throw `FormatException` (or drop the frame) beyond it. These paths run on relay data
- **MST prefix lengths are range-checked when decoding** (#111) — an entry's `p` must lie within the previous key, or the node is rejected with `FormatException` (the bug class indigo fixed as SEC-4). A crafted node used to drive a ~2 GB allocation before any check
- **`CarReader` validates every length and CID it reads** (#111) — header, block and digest lengths beyond the input throw `FormatException` rather than overflowing into a bad slice, varints must be minimal and at most 9 bytes, the header must carry a `version` and CID-link roots, and CIDv0 blocks are rejected: they used to bypass the unknown-codec rule in `VerifyBlockCid`
- **`MerkleSearchTree.Deserialize` rejects malformed trees** (#111) — invalid or out-of-order keys, a subtree linked more than once, and empty inner nodes throw `FormatException`, so a hostile tree can neither smuggle in keys nor make the walk revisit shared nodes
- **JWT and JWK base64url decoding is strict** — every decoder for space tokens, DPoP proofs, service-auth tokens, JWK coordinates and the bearer handler's pre-check now accepts only the URL-safe alphabet, unpadded or correctly padded, and rejects the standard alphabet's `+` and `/`, embedded whitespace and non-zero trailing bits, so a token has one spelling. A token's header and claims must be JSON objects, and the service-auth verifier now decodes the header as well (#118)
- **XRPC error bodies are read up to 64 KiB** — a failed response's body was buffered whole, so a service could answer an error with a body of any size; past the cap it is not read, and the status names the error (#117)

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
  - `EfCoreSpaceAuthorityStore<TContext>` — writer set and notification registrations, with `DeclareSpaceAsync` / `MarkDeletedAsync`. Pagination is by DID and evaluated by the database, so ordering and cursor comparison agree under any collation
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
