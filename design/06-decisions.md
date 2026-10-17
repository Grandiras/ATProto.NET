# 06 — Decisions

Architecture decision records for the rewrite. Each has a status:

- **Proposed**: recommended by this design, awaiting the maintainer's sign-off.
- **Accepted**: signed off; changing it needs a new record that supersedes it.
- **Open**: needs input before the phase that depends on it starts.

Every record below is **Proposed** until the maintainer reviews this document. Open questions
are collected at the end.

---

## D-01 Rewrite in a fresh tree, not by refactoring in place

**Context.** Previous cleanup passes (#180, #185, #194–#208) refactored in place. Each recovered
some size, but the structure stayed: one big core assembly, hand-written Lexicons, internal
cross-package access. The public API changed with each pass anyway (0.7 needed a 1,472-line
migration guide), so in-place refactoring did not buy compatibility either.

**Decision.** Build the new code in a fresh source tree on a long-lived `next` branch in this
repository. Port behaviour through specs and the conformance suite, not by moving files. `main`
stays on 0.8.x for fixes until 1.0 replaces it. See [05 — Rewrite plan](05-rewrite-plan.md).

**Consequences.** Two codebases exist for a while. Fixes to 0.8 that touch protocol behaviour
must also land in the relevant spec, so the rewrite picks them up.

## D-02 Ship as 1.0, not 0.9

**Decision.** The rewrite's first stable release is **1.0.0**, preceded by `1.0.0-preview.N`
packages. The package IDs stay `ATProtoNet.*` (see D-03). Experimental packages (Spaces) ship
as `0.x` prerelease versions on their own cadence.

**Rationale.** The rewrite's point is a surface stable enough to promise compatibility for.
Calling it 0.9 would signal "more breaking changes coming", which is what we want to stop.

## D-03 Package map

**Decision.** Split by dependency weight, I/O and churn rate (details in
[02 — Architecture](02-architecture.md#package-map)):

| Package | Role | Stability |
|---|---|---|
| `ATProtoNet.Primitives` | identifiers, CID, DRISL-CBOR, JSON data model, CAR, crypto, JOSE. No I/O. | stable |
| `ATProtoNet.Repo` | MST, commits, proofs, repository verification. No I/O. | stable |
| `ATProtoNet.Lexicon` | Lexicon schema model, validation, runtime support for generated code. No I/O. | stable |
| `ATProtoNet.Identity` | untrusted-fetch policy, DID documents, DID and handle resolution, caching, PLC. | stable |
| `ATProtoNet` | XRPC client, sessions (password, OAuth, service auth), labels, records API, PDS admin, generated `com.atproto.*`. | stable |
| `ATProtoNet.Bluesky` | generated `app.bsky.*` and `chat.bsky.*`, plus rich text, posting, profile and video helpers. | stable (unspecced endpoints experimental) |
| `ATProtoNet.Ozone` | generated `tools.ozone.*`. | stable package, fast-moving surface |
| `ATProtoNet.StandardSite` | generated `site.standard.*`. | stable |
| `ATProtoNet.Streaming` | event streams: firehose with Sync 1.1 verification, label streams, Jetstream (+archive), Tap. | stable |
| `ATProtoNet.Hosting` | DI builder, client factory, stores, stream workers, health checks, Aspire client integration. No ASP.NET Core. | stable |
| `ATProtoNet.AspNetCore` | XRPC server endpoints, service-auth scheme, OAuth web login, labeler endpoints, Tap webhook. | stable |
| `ATProtoNet.EntityFrameworkCore` | EF Core implementations of the store interfaces. | stable |
| `ATProtoNet.Blazor` | per-circuit user client accessor and an unstyled login form (D-09). | stable |
| `ATProtoNet.Aspire.Hosting` | PDS container resources for an Aspire AppHost. | stable |
| `ATProtoNet.Spaces` (+ `.AspNetCore`, `.EntityFrameworkCore`) | permissioned data: client and syncer, space server, EF stores. | **experimental** |
| `ATProtoNet.Lexicon.Generator` | Roslyn source generator for users' Lexicons. | stable |
| `atproto-lexgen` (tool) | CLI: generate, install, update, lint, diff, publish, resolve. | stable |

**Rationale.** A Jetstream indexer should not pull in OAuth and Ozone, and a worker service
should not need the ASP.NET Core shared framework to get DI registration and a stream worker.
A Spaces wire change should not be a breaking change of `ATProtoNet`. Foundation packages
without I/O are usable by anyone building atproto infrastructure in .NET.

**Rejected.** *One package per namespace* (e.g. `ATProtoNet.Syntax`, `.Cbor`, `.Car`, `.Crypto`
separately): too much friction for little gain, since these are always used together.
*Keeping one core package*: it is the main source of today's chaos.

## D-04 Generate the Lexicon surface from vendored Lexicon JSON

**Context.** About 21k lines of hand-written Lexicon models and clients restate the upstream
schemas. A drift test compares them against vendored JSON in the test project. Every upstream
change is a manual port. Meanwhile `atproto-lexgen` emits a different style.

**Decision.**
1. Upstream Lexicon JSON is vendored under `lexicons/` at the repository root, pinned by a
   `lexicons.json` manifest (NSID → CID/source/commit), and updated with
   `atproto-lexgen install`/`update`.
2. One code generator (`ATProtoNet.Lexicon.CodeGen`, a `netstandard2.0` library) turns schemas
   into C#. The CLI and the Roslyn source generator both use it.
3. First-party packages (`ATProtoNet`, `.Bluesky`, `.Ozone`, `.StandardSite`, `.Spaces`)
   **check in** their generated code under `Generated/`, regenerated by a script and verified in
   CI (regenerate, then `git diff --exit-code`).
4. Users generate their own Lexicons with the source generator (`<AdditionalFiles>` items) or the
   CLI. Both use the same emitter, so the output is the same.
5. Hand-written code (helpers, the records API) sits on top of the generated types and never
   restates them.

**Rationale for checking in first-party code.** Diffs show exactly what an upstream sync changes
in the public API. The build has no generator in the hot path. Stack traces and "go to
definition" land in real files. The PublicAPI analyzer sees the generated surface.

**Consequences.** The generator must produce output as good as today's hand-written code: typed
identifier formats, `required`/nullable, open unions, known values, `ICursorPage<T>`, records
with collection NSIDs, XML docs from Lexicon descriptions. [specs/lexicon.md](specs/lexicon.md)
specifies that output. The drift test disappears: generated code cannot drift.

## D-05 Lexicon generated-type naming

**Decision.** Namespace `{root}.{NSID authority and name segments, PascalCase}` minus the last
segment. Root `ATProtoNet.Lexicons` for first-party code (for example `app.bsky.feed.post` →
`ATProtoNet.Lexicons.App.Bsky.Feed.Post`). The main definition takes the PascalCased last segment.
Other definitions in the same file are nested types (`Post.ReplyRef`). Definitions in a `defs`
file are top-level types (`app.bsky.feed.defs#postView` → `…Feed.PostView`). Queries and
procedures become a static class named after the method, with nested `Parameters`, `Input` and
`Output` types (`GetTimeline.Parameters`). Collisions are a generator error, resolved by an
explicit rename in the manifest, never silently.

**Rejected.** *Mirroring NSIDs without a prefix* (`App.Bsky.Feed`): `App` collides with the
application class in WPF, MAUI and WinForms projects. *Keeping today's `ATProtoNet.Lexicon.*`*:
`ATProtoNet.Lexicon` becomes the schema-model package (D-03), and the singular/plural split
(`Lexicon` = the language, `Lexicons` = the definitions) is deliberate.

## D-06 A session is the authenticated transport

**Context.** Today `AtProtoClient` owns login, refresh and sign-out through `SessionManager`
(1,092 lines), plugged into `XrpcClient` through a `SessionHandler` hook. The client has a
"logged out" state, and session options (`AutoRefreshSession`, `BackgroundRefresh`,
`RefreshCoordinator`) configure the client.

**Decision.** Authentication is an `IXrpcHandler` (the thing that sends an XRPC request).
Implementations: `AnonymousHandler` (public endpoints), `PasswordSession`, `OAuthSession`,
`ServiceAuthHandler`. `AtProtoClient` takes a handler and knows nothing about logins, tokens or
refresh. Session objects own their tokens, refresh (single-flight), DPoP and persistence hooks.

```csharp
var session = await PasswordSession.CreateAsync(new Uri("https://bsky.social"), "alice.bsky.social", appPassword);
var client = new AtProtoClient(session);

var oauthSession = await oauthClient.CompleteAuthorizationAsync(callbackUri);
var client = new AtProtoClient(oauthSession);
```

**Rationale.** It separates "who am I and how do I prove it" from "what do I call", the same
split the upstream TypeScript SDK made with `@atproto/lex` (`new Client(oauthSession)`). The
client becomes immutable and trivially thread-safe. A multi-user server creates one cheap client
per request around a restored session.

**Consequences.** No `client.LoginAsync` on a constructed client. Apps that log in later hold an
`AtProtoClient?` or swap one in. A static convenience `AtProtoClient.LoginAsync(...)` covers
scripts. See [specs/auth.md](specs/auth.md).

## D-07 One consumption model for every event stream

**Decision.** Firehose, label streams, Jetstream, Jetstream archive replay and Tap are all
consumed as `IAsyncEnumerable<TEvent>` over one shared event-stream core (connect, frame decode,
reconnect with backoff, cursor store). The cursor of event *n* is committed when event *n+1* is
pulled, which gives at-least-once delivery. Sync 1.1 verification is a composable stage, not a
different consumer class. ASP.NET Core gets a `StreamWorker<TEvent>` hosted-service adapter.

**Rejected.** .NET events or callback registration (no back-pressure, error handling scattered).
Public `Channel<T>` (leaks buffering decisions).

## D-08 Spaces is quarantined as experimental

**Context.** The permissioned-data protocol is alpha. 0.8 alone changed its credential signing
(#212), type naming (#211), revision model (#213) and revocation schema (#214), and each was a
breaking change for every `ATProtoNet` user.

**Decision.** Spaces lives in its own package family, `ATProtoNet.Spaces`,
`ATProtoNet.Spaces.AspNetCore` and `ATProtoNet.Spaces.EntityFrameworkCore`, versioned `0.x`
prerelease, with all public types `[Experimental("ATPROTO001")]`. Its generated
`com.atproto.space.*` and `com.atproto.simplespace.*` code is generated from vendored proposal
Lexicons into `ATProtoNet.Spaces`, not `ATProtoNet`. BLAKE3 moves with it (Spaces is its only
user). Nothing stable references a Spaces package, and the stable hosting packages contain no
Spaces knowledge (0.8's central in-memory-defaults warning knows about Spaces stores).

**Consequences.** Spaces work can move as fast as the protocol does without touching the stable
packages. Spaces is the last phase of the rewrite, so it ports against the protocol as it stands
then, not as it stood in 0.8.

## D-09 Blazor components

**Context.** `ATProtoNet.Blazor` ships `LoginForm` plus `FeedView`, `PostCard`, `ProfileCard`
and `ComposePost` (223 lines total). The feed and post widgets encode product decisions (layout,
styling, which embeds render) that apps replace in practice. The login itself is the server
package's.

**Decision (proposed).** Keep a minimal `ATProtoNet.Blazor`: the per-circuit
`AtProtoUserClientAccessor` (getting client lifetimes right in Blazor Server is genuinely
fiddly: same user → same client, sign-out or user change → dispose, re-check the store) and an
unstyled, localizable `LoginForm` that reads the login route from options. Move `FeedView`,
`PostCard`, `ProfileCard` and `ComposePost` into `samples/ServerIntegrationSample` as copyable
code. They are Bluesky product UI, hard-code English strings and Bootstrap classes, and shipped
broken until 0.7.

**Status: Open.** The alternative is dropping the package entirely. See Q-1.

## D-10 Serialization: System.Text.Json with source generation, generated union converters

**Decision.** All JSON goes through System.Text.Json with source-generated
`JsonSerializerContext`s (one per package, combined by a resolver chain). Unions get generated
converters ([D-18](#d-18-unions-are-interfaces)): read `$type` (anywhere in the object), dispatch
to known variants, fall back to `Unknown…` for open unions, fail for closed ones. Unknown object
fields round-trip through `[JsonExtensionData]`. User-defined union variants register through
a `LexiconTypeRegistry` *instance* passed when building serializer options, never a
process-global singleton.

Two decoding paths, one model mapping:

- **Protocol envelopes** that are hot and stable (event-stream frame headers, `#commit`,
  `#sync`, `#identity`, `#account`, `#info`, label frames, commit and MST nodes) are bound
  **directly from DRISL-CBOR** by hand-written binders. 0.8 transcodes them CBOR → JSON →
  objects, base64-encoding up to 2 MB of CAR blocks per commit on the way.
- **Record bodies** (Lexicon data inside CAR blocks) decode CBOR → `DataModelValue` → typed, via
  the same generated contracts used for JSON, so there is exactly one mapping per Lexicon type.
  A direct CBOR path for generated types is a later optimisation behind the same API.

**Rationale.** AOT/trim safety (G6), one mapping per Lexicon type, and no transcoding on the
firehose hot path.

## D-11 No implicit conversions from `string`

**Decision.** Identifier types have `Parse`/`TryParse` and an implicit conversion *to* `string`,
never *from* it. See [AG-8](03-api-guidelines.md#types).

**Rationale.** An implicit conversion that throws hides validation at call sites. The verbosity
cost is small and helpers accept the typed identifier.

**Status: Open** if the maintainer prefers literal ergonomics (`client.GetProfileAsync("alice.bsky.social")`). See Q-3.

## D-12 Generated XRPC methods take one parameters record

**Decision.** `client.Bsky.Feed.GetTimelineAsync(new() { Limit = 50 })`, never positional
optional parameters. See [AG-12, AG-13](03-api-guidelines.md#methods-and-parameters).

**Rationale.** Lexicons gain optional parameters all the time. With positional optional
parameters each sync is a binary break. With records it is an additive change. It also removes
any need for a hand-maintained parameter-order convention, and the generator needs no heuristics.

## D-13 Lexicon namespace access through extension properties

**Decision.** `AtProtoClient` exposes only `com.atproto.*` namespaces (`client.Repo`,
`client.Server`, …, generated). Other packages add theirs through C# 14 extension members
(`extension(AtProtoClient client) { public BskyNamespace Bsky => new(client); }`), so
`client.Bsky.Feed.GetTimelineAsync(…)` works once `ATProtoNet.Bluesky` is referenced, and the
core knows nothing about Bluesky. Users' generated Lexicons get the same treatment
(`client.Lexicons().Com.Example.Todo` or a configured accessor name).

**Rationale.** It removes the core's compile-time dependency on every Lexicon namespace, which
is what made `AtProtoClient.cs` an 884-line wiring file.

## D-14 Identity resolution is its own package with explicit network policy

**Decision.** `ATProtoNet.Identity` owns DID documents, `did:plc`/`did:web` resolution, handle
resolution (DNS TXT through a pluggable `IDnsTxtResolver`, defaulting to DNS-over-HTTPS as
today, plus HTTPS well-known), bidirectional verification, caching, and the PLC directory
client and operation builder. All identity HTTP goes through an `HttpClient` whose handler
enforces the SSRF policy. The client package depends on it. The foundation packages do not.

## D-15 Stores are interfaces in the package that uses them; implementations live elsewhere

**Decision.** `ISessionStore`, `IOAuthStateStore`, `ICursorStore`, `IReplayStore` and
`IRepoSyncStateStore` are defined next to their consumer. In-memory implementations ship with
the interface. File implementations (and, for OAuth state, a distributed-cache one) go in
`ATProtoNet.Hosting`. EF Core
implementations go in `ATProtoNet.EntityFrameworkCore`. Each interface has a shared contract
test suite that every implementation runs, **including concurrency cases** (0.8's EF session
store races on the first write for a DID because the contract never tested it). The session
store contract includes compare-and-swap (D-22). Details: [specs/stores.md](specs/stores.md).

## D-16 Experimental diagnostic IDs

<a id="experimental-diagnostic-ids"></a>

| ID | Surface |
|---|---|
| `ATPROTO001` | Spaces (permissioned data) |
| `ATPROTO002` | Bluesky `app.bsky.unspecced.*` endpoints |
| `ATPROTO003` | Jetstream v2 archive API |
| `ATPROTO004` | Lexicon runtime resolution (`com.atproto.lexicon.*`, until the spec settles) |

New IDs are appended here. An ID is retired (never reused) when its surface stabilises or is
removed.

## D-17 Repository and branch workflow for the rewrite

**Decision.** The `next` branch holds the new tree. Each phase lands as one or more PRs into
`next`, each tied to a tracker issue and following the existing issue workflow (CHANGELOG
excepted: `next` keeps its own `CHANGELOG.md` that starts at 1.0.0). `main` merges into `next`
only for vendored-Lexicon or spec-relevant fixes. When phase 11 passes, `next` replaces `main`.

## D-18 Unions are interfaces

**Context.** 0.8 models a Lexicon union as an abstract base class. A C# class has one base, but
a Lexicon definition can appear in several unions (`app.bsky.embed.record#view` is a member of
post-view embeds and chat message embeds). The result: duplicated types (`RecordEmbed` and
`MessageRecordEmbed`), wrong subtypes (`ThreadNode : PostEntry` makes a thread view a bookmark
item; `GeneratorView` derives from an embed view), and 43 copy-pasted `Unknown*` classes. The
current generator gave up and emits `JsonElement?` for union fields.

**Decision.** Each union is a generated interface (`IPostEmbed`). A variant type implements
every union interface it belongs to. Each open union gets a generated `Unknown…` variant holding
the `$type` and raw JSON, which writes back byte for byte. Closed unions have no unknown variant
and fail on an unrecognised `$type`. Generated converters do the dispatch. Pattern matching works
naturally: `if (post.Embed is Images images)`.

A variant defined in an *upstream* package cannot implement an interface generated in a
downstream one (22 of the 103 upstream unions have such members, for example
`com.atproto.label.defs#selfLabels` in `app.bsky.feed.post#labels`). For those, the downstream
package generates a derived record (`PostLabelsSelfLabels : SelfLabels, IPostLabels`) that the
converter produces, so `post.Labels is SelfLabels labels` still works and the wire shape is
unchanged. This requires generated named object and record types to be unsealed `partial record`s,
a documented exception to [AG-6](03-api-guidelines.md#types). See
[LEX-49](specs/lexicon.md#generated-clients).

## D-19 Lexicon `integer` maps to `int` when the bounds fit, otherwise `long`

**Decision.** A Lexicon `integer` with `minimum` and `maximum` both inside `Int32` range maps to
`int`; anything else maps to `long`. The rule is mechanical, so `limit` (1–100) is an `int` and
unbounded counts are `long`. 0.8's hand-written code used `int` in 189 places and `long` in 33
with no rule; the old generator used `long` everywhere.

## D-20 JSON reads are case-sensitive and strict about number types

**Decision.** Drop `PropertyNameCaseInsensitive` and `AllowReadingFromString`, which 0.8 enables.
Lexicon property names are case-sensitive, and both options can rebind or rewrite data on a
read-modify-write round trip. Identifier fields stay strict on read (an invalid DID fails
deserialization with the JSON path); `datetime` fields stay lenient on read (the original text is
kept, `IsValid` reports whether it parses), as in 0.8. Success bodies get a size cap like error
bodies already have.

## D-21 One untrusted-fetch layer

**Decision.** The SSRF-guarded HTTP machinery (connect-time address checks on every resolved
address, no proxy, no redirects, bounded bodies, timeouts, a concurrency cap, DNS TXT via
DNS-over-HTTPS) is a public layer in `ATProtoNet.Net` (package `ATProtoNet.Identity`), with its
own `UntrustedFetchException`. Identity resolution, OAuth discovery, Lexicon resolution and repo
fetchers all use it. In 0.8 it is `internal` in `Identity/`, used by OAuth and Lexicon code that
then has to catch `DidResolutionException` for non-identity failures, and it keeps process-wide
state (the concurrency cap, shared handlers) that cannot be configured or injected. In the
rewrite that state belongs to an instance the caller can supply.

## D-22 Session stores support compare-and-swap

**Decision.** `ISessionStore` gains a versioned compare-and-swap
(`TryReplaceAsync(Did, expectedVersion, next)`). Refresh, adoption of a newer stored session,
conditional removal on expiry and logout are expressed as CAS operations. This replaces 0.8's
hand-built optimistic concurrency in `SessionManager` and makes multi-instance deployments
correct without a distributed lock. The shipped implementations (memory, file, EF via row version
and upsert) pass the same concurrent contract tests. There is no `IDistributedCache` session store,
because `IDistributedCache` cannot compare-and-swap; shared stores such as Redis implement the
contract themselves, using the published contract tests.

## D-23 Stream side information is delivered in-band

**Context.** 0.8 reports stream side information through four callbacks (`OnStreamError`,
`OnEventDropped`, `OnInfo` on Jetstream only, `OnDesynchronized`), and treats the same signal
differently per stream: `#info`/`OutdatedCursor` is an in-band event on the firehose, a callback
on Jetstream and an exception on the PLC export.

**Decision.** Every stream yields a union of protocol events and **notices**: `StreamInfo`
(including `OutdatedCursor`), `RepoDesynchronized`, `RepoResynced`, and, when the caller opts in,
`EventDropped`. Notices are unsequenced and never move the cursor. Errors that end the stream are
one `StreamException` type (status, error name, `IsRetryable`, retry-after) for every stream
client. Metrics count drops and errors whether or not notices are requested.

## D-24 Spaces scope in the rewrite

**Context.** Spaces is about 11.5k source lines and 10.4k test lines, more than the whole
streaming stack, for a protocol that is explicitly not final, has had no security review, is not
served by any released PDS, and changed its wire format or stored schema five times across 0.7
and 0.8 (#113, #211–#214). The server
alone is 56% of `ATProtoNet.Server`'s public API.

**Decision (proposed).** Phase 10 ports, against the protocol as it stands then:

1. the **primitives** (space URIs, LtHash, BLAKE3, space commits, space repo CAR, strict record
   CBOR, tokens, HTTP message signatures) with their reference vectors;
2. the **client**: credential provider, signed requests through a generic per-request
   authenticator (no Spaces code in the core transport), and the syncer;
3. a **notification receiver** for forwarded `notifyWrite`/`notifySpaceDeleted`, which 0.8 lacks;
4. the **server** (authority and repo host) only if the protocol has stabilised by then.
   `simplespace` server endpoints, managing-app policy and client-attestation verification are
   deferred until upstream settles them.

**Status: Open.** See Q-11.

---

## Open questions

<a id="open-questions"></a>

| # | Question | Default if unanswered | Blocks |
|---|---|---|---|
| Q-1 | Keep a minimal Blazor package (accessor + login form) in 1.0, or drop it (D-09)? | Keep it minimal; widgets become sample code. | Phase 9 |
| Q-2 | Keep `PdsAdminClient` and `ATProtoNet.Aspire.Hosting` in scope? The non-goals exclude implementing a PDS, but administering and orchestrating one is in scope today. | Keep both. | Phase 11 |
| Q-3 | Accept explicit `Parse` for identifiers (D-11), or allow implicit conversion from `string` for ergonomics? | Explicit only. | Phase 1 |
| Q-4 | Check in generated first-party Lexicon code (D-04) or generate at build time? | Check in. | Phase 3 |
| Q-5 | Is `ATProtoNet.Ozone` worth keeping as a package, or should Ozone users generate it themselves from the vendored schemas? | Keep (it is cheap once generated). | Phase 8 |
| Q-6 | Should the GitHub mirror and issue sync stay as they are during the rewrite? | Yes, unchanged. | none |
| Q-7 | Minimum PDS versions to test against in CI (official `bluesky-social/pds` and Tranquil)? | Latest official release plus the pinned CI version. | Phase 5 |
| Q-8 | First-party generated namespaces: mechanical `ATProtoNet.Lexicons.App.Bsky.Feed` (D-05) or friendly `ATProtoNet.Bluesky.Feed` via a namespace map? | Mechanical. | Phase 3 |
| Q-9 | Keep the development-only loopback cookie relay of the OAuth login (about 150 lines in 0.8)? | Drop it; document running the login on `127.0.0.1` with a loopback client ID instead. | Phase 9 |
| Q-10 | Keep `atproto-lexgen lexicon` (C# → Lexicon JSON by reflection)? | Drop it; Lexicon JSON is the source of truth, C# is generated from it. | Phase 3 |
| Q-11 | How much of Spaces to port (D-24): primitives + client + syncer only, or the server too? | Primitives, client, syncer and notification receiver; server only if the protocol is stable by phase 10. | Phase 10 |
| Q-12 | Keep the Jetstream v1 wire protocol, or v2 only (keeping the v1 timestamp-cursor migration)? | Keep v1 as a second codec; public instances still serve it. | Phase 7 |
