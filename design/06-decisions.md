# 06 — Decisions

Architecture decision records for the rewrite. Each has a status:

- **Proposed**: recommended by this design, awaiting the maintainer's sign-off.
- **Accepted**: signed off; changing it needs a new record that supersedes it.
- **Open**: needs input before the phase that depends on it starts.

Records are **Proposed** unless marked otherwise. The maintainer's first review (2026-10-17)
accepted D-02 (version 0.9) and D-03 (few packages) in revised form. Open questions are collected
at the end.

---

## D-01 Rewrite in a fresh tree, not by refactoring in place

**Context.** Previous cleanup passes (#180, #185, #194–#208) refactored in place. Each recovered
some size, but the structure stayed: one big core assembly, hand-written Lexicons, internal
cross-package access. The public API changed with each pass anyway (0.7 needed a 1,472-line
migration guide), so in-place refactoring did not buy compatibility either.

**Decision.** Build the new code in a fresh source tree on a long-lived `next` branch in this
repository. Port behaviour through specs and the conformance suite, not by moving files. `main`
stays on 0.8.x for fixes until 0.9 replaces it. See [05 — Rewrite plan](05-rewrite-plan.md).

**Consequences.** Two codebases exist for a while. Fixes to 0.8 that touch protocol behaviour
must also land in the relevant spec, so the rewrite picks them up.

## D-02 Ship as 0.9

**Status: Accepted** (maintainer, 2026-10-17).

**Decision.** The rewrite ships as **0.9.0**, preceded by `0.9.0-preview.N` packages. All
packages share one version. The package IDs stay `ATProtoNet` and `ATProtoNet.*` (D-03).

**Compatibility.** Within the 0.9 line, patch releases do not break the public API (the API
baseline and package validation enforce it). APIs marked `[Experimental]` (D-16) are excluded and
may change in any release. 1.0 follows once the 0.9 surface has been put through real use, the
same bar the README already sets for calling the SDK stable.

**Rationale.** The rewrite aims at a surface that can become 1.0 unchanged, but that should be
proven in use before it carries a 1.0 promise.

## D-03 Package map

**Status: Accepted** (maintainer, 2026-10-17), replacing the first draft's fifteen-package split.

**Decision.** As few packages as possible, each logically scoped
([02 — Architecture](02-architecture.md#packages)):

| Package | Role |
|---|---|
| `ATProtoNet` | Everything that does not need ASP.NET Core, EF Core or Aspire: identifiers, data model, crypto, repo, Lexicon schema model and validation, identity, the XRPC client, sessions, OAuth, service auth, labels, records API, PDS admin, **all generated Lexicons** (`com.atproto`, `app.bsky`, `chat.bsky`, `tools.ozone`, `site.standard`) with the Bluesky helpers, streaming (firehose, Sync 1.1, Jetstream, Tap, labels, PLC export), Spaces (experimental), the DI/hosting integration (builder, client factory, file session store, stream workers, health checks, Aspire client), and the bundled Lexicon source generator. |
| `ATProtoNet.AspNetCore` | Replaces `ATProtoNet.Server`: XRPC server endpoints, service-auth scheme, OAuth web login, labeler endpoints, Tap webhook, Spaces server endpoints (experimental). |
| `ATProtoNet.EntityFrameworkCore` | EF Core store implementations, including Spaces stores (experimental). |
| `ATProtoNet.Blazor` | Per-circuit user client accessor and an unstyled login form (D-09). |
| `ATProtoNet.Aspire.Hosting` | PDS container resources for an Aspire AppHost. |
| `atproto-lexgen` (tool) | CLI: generate, install, update, lint, diff, publish, resolve. |

Inside `ATProtoNet`, the structure comes from **namespace layers** enforced by architecture tests
(foundation without I/O → network → client → protocols and Lexicon families → hosting), not from
package boundaries.

**Rationale.** For consumers, many packages are tedious and confusing: which do I need, which
versions go together, where did a type move? A base package plus extensions for the frameworks
that bring heavy dependencies (ASP.NET Core, EF Core, Aspire, Blazor) is easy to explain and to
install. 0.8's own history (EF stores merged into Server, then split out again; an Aspire package
merged in) shows the cost of drawing package lines in the wrong place. The internal discipline the
split was meant to provide comes from namespace layers and architecture tests instead. Size
concerns are handled by trimming.

**Consequences.** Moving the DI and hosting integration into the base package means a worker
service needs only `ATProtoNet`; the base takes small `Microsoft.Extensions.*` and
`Microsoft.AspNetCore.DataProtection.Abstractions` dependencies, never the ASP.NET Core shared
framework. A Spaces change ships with the whole base package, which is acceptable because Spaces
APIs are `[Experimental]` (D-08). Generated code must be fully trimmable.

**Rejected.** The first draft's split into foundation, network, Lexicon-family, hosting and
Spaces packages: cleaner on paper, worse for every consumer.

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
3. All first-party generated code is **checked in** under `src/ATProtoNet/Generated/`,
   regenerated by a script and verified in CI (regenerate, then `git diff --exit-code`).
4. Users generate their own Lexicons with the source generator, which ships inside the
   `ATProtoNet` package as an analyzer (add Lexicon JSON files to the project), or with the CLI.
   Both use the same emitter, so the output is the same.
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
`ATProtoNet.Lexicon` becomes the schema-model namespace, and the singular/plural split
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
different consumer class. `ATProtoNet.Hosting` provides a `StreamWorker<TEvent>` hosted-service
adapter, so worker services need no ASP.NET Core.

**Rejected.** .NET events or callback registration (no back-pressure, error handling scattered).
Public `Channel<T>` (leaks buffering decisions).

## D-08 Spaces is experimental

**Context.** The permissioned-data protocol is alpha. 0.8 alone changed its credential signing
(#212), type naming (#211), revision model (#213) and revocation schema (#214), and each was a
breaking change for every `ATProtoNet` user, because nothing marked the Spaces API as unstable.

**Decision.** Spaces stays in the existing packages (client, syncer and receiver in `ATProtoNet`,
namespace `ATProtoNet.Spaces`; server endpoints in `ATProtoNet.AspNetCore`; EF stores in
`ATProtoNet.EntityFrameworkCore`), with every public Spaces type marked
`[Experimental("ATPROTO001")]`. Callers opt in by suppressing the diagnostic. Experimental APIs
may change in any release without counting as a breaking change of the 0.9 line (D-02). The
generated `com.atproto.space.*` and `com.atproto.simplespace.*` code is generated from vendored
proposal Lexicons and carries the same attribute. BLAKE3 lives with Spaces (its only user). Stable
code outside the Spaces namespaces contains no Spaces knowledge (0.8's transport signs Spaces
requests, and its central in-memory-defaults warning knows Spaces stores).

**Consequences.** Spaces can track the protocol without breaking anyone who has not opted in.
Spaces is the last phase of the rewrite, so it ports against the protocol as it stands then, not
as it stood in 0.8.

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
`JsonSerializerContext`s (the SDK's, plus one per set of user Lexicons, combined by a resolver
chain). Lexicon unions ([D-18](#d-18-lexicon-unions-are-c-15-union-types)) get a generated
converter each: read `$type` (anywhere in the object), dispatch to the case, fall back to
`LexiconUnknown` for open unions, fail for closed ones; write `$type` first in union position
only. Unknown object fields round-trip through `[JsonExtensionData]`. A union's case list is
fixed by its Lexicon, so a third-party variant of an SDK open union arrives as `LexiconUnknown`
and converts on demand (`unknown.TryAs<MyEmbed>(out var embed)`); writing one wraps it
(`LexiconUnknown.From(myEmbed)`), and generated extension members make both read like SDK cases
([LEX-86](specs/lexicon.md#generated-clients)). The NSID → type mapping that conversion and `unknown` fields
need comes from a `LexiconTypeRegistry` *instance* passed when building serializer options, never
a process-global singleton.

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

**Decision.** `AtProtoClient` itself exposes only the generated `com.atproto.*` namespaces
(`client.Repo`, `client.Server`, …). The other Lexicon families attach through C# 14 extension
members declared next to their generated code
(`extension(AtProtoClient client) { public BskyNamespace Bsky => new(client); }`), so
`client.Bsky.Feed.GetTimelineAsync(…)` works with no wiring in the client. Users' generated
Lexicons attach the same way (a configured accessor name).

**Rationale.** It keeps the client-core namespace free of references to the Lexicon families (the
layering rule in [02](02-architecture.md#rules)), removes the wiring that made 0.8's
`AtProtoClient.cs` 884 lines, and gives users' Lexicons exactly the same shape as the SDK's.

## D-14 Identity resolution is its own layer with explicit network policy

**Decision.** The `ATProtoNet.Identity` namespace (layer 1) owns DID documents,
`did:plc`/`did:web` resolution, handle resolution (DNS TXT through a pluggable `IDnsTxtResolver`,
defaulting to DNS-over-HTTPS as today, plus HTTPS well-known), bidirectional verification,
caching, and the PLC directory client and operations. All identity HTTP goes through an
`HttpClient` whose handler enforces the SSRF policy (D-21). The client layer depends on it; the
foundation layer does not.

## D-15 Store interfaces live next to their consumers; implementations live with their dependencies

**Decision.** `ISessionStore`, `IOAuthStateStore`, `ICursorStore`, `IReplayStore` and
`IRepoSyncStateStore` are defined next to their consumer. In-memory implementations ship with
the interface. File implementations (and, for OAuth state, a distributed-cache one) live in the
`ATProtoNet.Hosting` namespace of the base package. EF Core implementations live in
`ATProtoNet.EntityFrameworkCore`. Each interface has a shared contract
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
excepted: `next` keeps its own `CHANGELOG.md` that starts at 0.9.0). `main` merges into `next`
only for vendored-Lexicon or spec-relevant fixes. When phase 11 passes, `next` replaces `main`.

## D-18 Lexicon unions are C# 15 union types

**Context.** 0.8 models a Lexicon union as an abstract base class. A C# class has one base, but
a Lexicon definition can appear in several unions (`app.bsky.embed.record#view` is a member of
post-view embeds and chat message embeds). The result: duplicated types (`RecordEmbed` and
`MessageRecordEmbed`), wrong subtypes (`ThreadNode : PostEntry` makes a thread view a bookmark
item; `GeneratorView` derives from an embed view), and 43 copy-pasted `Unknown*` classes. The
current generator gave up and emits `JsonElement?` for union fields. The first draft of this
design used generated interfaces instead, which still forced every variant to implement every
union it joins, and needed derived records plus unsealed types for unions in users' assemblies
that name SDK types.

**Decision.** Each Lexicon union is a C# 15 `union` type
([D-25](#d-25-target-net-11-and-c-15)):

```csharp
[JsonConverter(typeof(PostEmbedConverter))]                    // generated
public union PostEmbed(Images, Video, External, Record, RecordWithMedia, LexiconUnknown);

Post post = new() { Text = "hi", Embed = new Images { Images = [image] }, CreatedAt = AtDatetime.Now() };
string summary = post.Embed switch
{
    Images i => $"{i.Images.Count} images",
    Video v => "a video",
    External e => e.External.Uri,
    Record or RecordWithMedia => "a quote",
    LexiconUnknown u => $"something new: {u.Type}",
    null => "no embed",
};
```

- **Case types need nothing.** A case type does not implement or derive from anything, so a type
  can be a case of any number of unions in any assembly. Generated object and record types are
  `sealed`. The derived-record workaround and the unsealed-types exception are gone.
- **Open unions** include the shared case `LexiconUnknown` (the received `$type` and the raw
  JSON, written back byte for byte). Closed unions do not, and fail on an unrecognised `$type`.
- **Optional union fields** are typed as the union struct itself: `default` (no active case)
  means absent, is omitted on write, and `is null` tests it. Required union fields are
  `required`.
- **The compiler checks exhaustiveness.** When a Lexicon sync adds a variant to a closed union,
  every `switch` that misses it gets a warning.
- **The wire format comes from a generated converter per union**, because System.Text.Json's
  built-in union serialization writes the active case without a discriminator and its case
  metadata has no per-case contract. The converter reads `$type` anywhere among the root
  properties (#50) and writes it as the first property in union position only. Outside union
  position (a plain `ref` to an object) `$type` is not written, as the Lexicon spec asks;
  record types always carry it. This is a deliberate change from 0.8, which writes `$type` for
  union-variant types everywhere. See [specs/lexicon.md](specs/lexicon.md#generated-clients).

**Revisit** if System.Text.Json gains discriminator support for unions: the converter could then
shrink to configuration.

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
DNS-over-HTTPS) is a public layer in the `ATProtoNet.Net` namespace (layer 1), with its
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

**Decision.** Every stream yields a C# 15 `union` of its protocol events and **notices**
(`public union FirehoseItem(CommitEvent, SyncEvent, IdentityEvent, AccountEvent, InfoEvent,
StreamInfo, EventDropped, RepoDesynchronized, RepoResynced)`), so one exhaustive `switch` handles
everything a stream can yield. Notices form a `closed` hierarchy under `StreamNotice`:
`StreamInfo` (including `OutdatedCursor`), `RepoDesynchronized`, `RepoResynced`, and, when the
caller opts in, `EventDropped`. Notices are unsequenced and never move the cursor. Errors that end the stream are
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

## D-25 Target .NET 11 and C# 15

**Status: Proposed** (raised by the maintainer, 2026-10-17).

**Context.** .NET 11 and C# 15 ship in November 2026, before the rewrite's first preview. .NET 11
is a standard-term release supported for 24 months, until November 2028, the same end date as
.NET 10 LTS. C# 15 and .NET 11 bring features that remove whole mechanisms from this design.

**Decision.** All runtime packages target `net11.0` only and use C# 15. The Roslyn generator and
the code generator stay on `netstandard2.0` (a compiler requirement), but the code they emit
targets C# 15 and `net11.0`. When .NET 12 (LTS) ships in November 2027, the SDK moves to it in a
minor release; the SDK targets one framework at a time.

| Feature | Where the design uses it |
|---|---|
| `union` types (C# 15; `UnionAttribute`/`IUnion` in the .NET 11 runtime) | Lexicon unions (D-18), stream items (D-23) |
| `closed` hierarchies (C# 15) | SDK-owned hierarchies whose cases the SDK enumerates: `DataModelValue`, sessions and session data, OAuth login hints, scopes, stream notices, verdicts ([AG-6a](03-api-guidelines.md#types)) |
| Extension members (C# 14) | Lexicon namespace accessors (D-13) |
| `System.IO.Compression.ZstandardDecoder` with dictionaries (.NET 11) | Jetstream live frames and archive blocks; replaces the `IZstdDecompressor` seam, so Jetstream compression needs no third-party package |
| Zstandard automatic HTTP decompression (.NET 11) | the SDK-owned `SocketsHttpHandler` |
| Runtime async (.NET 11) | no API impact; benefits the async-heavy stream and XRPC paths, measured by the benchmarks |

**Rejected.** *Multi-targeting `net10.0` and `net11.0`*: two representations of unions (or
polyfills), two Zstandard paths and a doubled test matrix, all to support a runtime whose
support ends on the same day as .NET 11's. Apps on .NET 10 stay on 0.8 until they upgrade.
*Staying on `net10.0`*: keeps the interface-based unions and their cross-assembly workaround, and
the third-party zstd dependency.

**Risks.** C# 15 union details could still shift before GA. Phases 0–2 do not depend on them; the
union shape is fixed in phase 3 against the released compiler, and since it is generated, a change
is a regeneration. See Q-14.

---

## Open questions

<a id="open-questions"></a>

| # | Question | Default if unanswered | Blocks |
|---|---|---|---|
| Q-1 | Keep a minimal Blazor package (accessor + login form) in 0.9, or drop it (D-09)? | Keep it minimal; widgets become sample code. | Phase 9 |
| Q-2 | Keep `PdsAdminClient` and `ATProtoNet.Aspire.Hosting` in scope? The non-goals exclude implementing a PDS, but administering and orchestrating one is in scope today. | Keep both. | Phase 11 |
| Q-3 | Accept explicit `Parse` for identifiers (D-11), or allow implicit conversion from `string` for ergonomics? | Explicit only. | Phase 1 |
| Q-4 | Check in generated first-party Lexicon code (D-04) or generate at build time? | Check in. | Phase 3 |
| Q-5 | ~~Is `ATProtoNet.Ozone` worth keeping as a package?~~ **Resolved by D-03** (maintainer, 2026-10-17): `tools.ozone` is generated into `ATProtoNet` like every other first-party family. | — | — |
| Q-6 | Should the GitHub mirror and issue sync stay as they are during the rewrite? | Yes, unchanged. | none |
| Q-7 | Minimum PDS versions to test against in CI (official `bluesky-social/pds` and Tranquil)? | Latest official release plus the pinned CI version. | Phase 5 |
| Q-8 | First-party generated namespaces: mechanical `ATProtoNet.Lexicons.App.Bsky.Feed` (D-05) or friendly `ATProtoNet.Bluesky.Feed` via a namespace map? | Mechanical. | Phase 3 |
| Q-9 | Keep the development-only loopback cookie relay of the OAuth login (about 150 lines in 0.8)? | Drop it; document running the login on `127.0.0.1` with a loopback client ID instead. | Phase 9 |
| Q-10 | Keep `atproto-lexgen lexicon` (C# → Lexicon JSON by reflection)? | Drop it; Lexicon JSON is the source of truth, C# is generated from it. | Phase 3 |
| Q-11 | How much of Spaces to port (D-24): primitives + client + syncer only, or the server too? | Primitives, client, syncer and notification receiver; server only if the protocol is stable by phase 10. | Phase 10 |
| Q-12 | Keep the Jetstream v1 wire protocol, or v2 only (keeping the v1 timestamp-cursor migration)? | Keep v1 as a second codec; public instances still serve it. | Phase 7 |
| Q-13 | Name the ASP.NET Core package `ATProtoNet.AspNetCore` (D-03), or keep 0.8's `ATProtoNet.Server`? | `ATProtoNet.AspNetCore`: "Server" reads like a PDS, and the package changes shape anyway. | Phase 9 |
| Q-14 | Target `net11.0` only (D-25), or also `net10.0` for apps staying on the LTS release? | `net11.0` only; .NET 10 and .NET 11 support both end in November 2028. | Phase 0 |
