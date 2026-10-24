# 06 — Decisions

Architecture decision records for the rewrite. Each has a status:

- **Proposed**: recommended by this design, awaiting the maintainer's sign-off.
- **Accepted**: signed off; changing it needs a new record that supersedes it.
- **Open**: needs input before the phase that depends on it starts.

The maintainer's first review (2026-10-17) accepted D-02 (version 0.9) and D-03 (few packages)
in revised form, and D-08 and D-24 (Spaces). The second review answered every open question: Q-1,
Q-6 and Q-8 explicitly, the rest by accepting the defaults. The third review accepted every
remaining record, D-20 after more context. Every record is now Accepted. The
[questions](#open-questions) stay listed at the end with their outcomes.

---

## D-01 Rewrite in a fresh tree, not by refactoring in place

**Status: Accepted** (maintainer, 2026-10-17).

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
| `ATProtoNet` | Everything that does not need ASP.NET Core, EF Core or Aspire: identifiers, data model, crypto, repo, Lexicon schema model and validation, identity, the XRPC client, sessions, OAuth, service auth, labels, records API, PDS admin, **all generated Lexicons** (`com.atproto`, `app.bsky`, `chat.bsky`, `tools.ozone`, `site.standard`) with the Bluesky helpers, streaming (firehose, Sync 1.1, Jetstream, Tap, labels, PLC export), Spaces (both data protocols are first class, D-08), the DI/hosting integration (builder, client factory, file session store, stream workers, health checks, Aspire client), and the bundled Lexicon source generator. |
| `ATProtoNet.AspNetCore` | Replaces `ATProtoNet.Server`: XRPC server endpoints, service-auth scheme, OAuth web login, labeler endpoints, Tap webhook, Spaces server roles (space authority, repo host, simplespace, notifications). |
| `ATProtoNet.EntityFrameworkCore` | EF Core store implementations, including the Spaces stores. |
| `ATProtoNet.Aspire.Hosting` | PDS container resources for an Aspire AppHost. |
| `atproto-lexgen` (tool) | CLI: generate, install, update, lint, diff, publish, resolve. |

Inside `ATProtoNet`, the structure comes from **namespace layers** enforced by architecture tests
(foundation without I/O → network → client → protocols and Lexicon families → hosting), not from
package boundaries.

**Rationale.** For consumers, many packages are tedious and confusing: which do I need, which
versions go together, where did a type move? A base package plus extensions for the frameworks
that bring heavy dependencies (ASP.NET Core, EF Core, Aspire) is easy to explain and to
install. There is no Blazor package (D-09). 0.8's own history (EF stores merged into Server,
then split out again; an Aspire package merged in) shows the cost of drawing package lines in the
wrong place. The internal discipline the
split was meant to provide comes from namespace layers and architecture tests instead. Size
concerns are handled by trimming.

**Consequences.** Moving the DI and hosting integration into the base package means a worker
service needs only `ATProtoNet`; the base takes small `Microsoft.Extensions.*` and
`Microsoft.AspNetCore.DataProtection.Abstractions` dependencies, never the ASP.NET Core shared
framework. Generated code must be fully trimmable.

**Rejected.** The first draft's split into foundation, network, Lexicon-family, hosting and
Spaces packages: cleaner on paper, worse for every consumer.

## D-04 Generate the Lexicon surface from vendored Lexicon JSON

**Status: Accepted** (maintainer, 2026-10-17; Q-4: check in the generated code).

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

**Status: Accepted** (maintainer, 2026-10-17; Q-8): namespaces are mechanical, as long as the
types themselves have friendly names.

**Decision.** Namespace `{root}.{NSID authority and name segments, PascalCase}` minus the last
segment. Root `ATProtoNet.Lexicons` for first-party code (for example `app.bsky.feed.post` →
`ATProtoNet.Lexicons.App.Bsky.Feed.Post`). The main definition takes the PascalCased last segment.
Other definitions in the same file are nested types (`Post.ReplyRef`). Definitions in a `defs`
file are top-level types (`app.bsky.feed.defs#postView` → `…Feed.PostView`). Queries and
procedures become a static class named after the method, with nested `Parameters`, `Input` and
`Output` types (`GetTimeline.Parameters`). Collisions are a generator error, resolved by an
explicit rename in the manifest, never silently.

**Type names are friendly** ([LEX-89–91](specs/lexicon.md#naming)). Mechanical namespaces are
fine because users write them once per file in a `using`; type names appear on every line, so
they must read like hand-written code. The generator enforces a naming rule set (PascalCase noun
phrases, no `Main`/`Defs`/`Def`, no repeated words, no stacked suffixes, a length limit), names
union types without the method's leading verb (`GetPostThread.Output.thread` → `PostThread`),
warns on a name that breaks a rule and suggests a rename, and first-party generation runs with
zero warnings. A checked-in type-name inventory (`Generated/TypeNames.txt`) shows every
generated name and makes renames visible in review. The first-party rename table in
`lexgen.json` is short, each entry carries a reason, and it is reviewed in phase 3 against 0.8's
hand-written names (`FeedViewPost`, `PostView`, `ThreadViewPost`) as the bar.

**Rejected.** *Mirroring NSIDs without a prefix* (`App.Bsky.Feed`): `App` collides with the
application class in WPF, MAUI and WinForms projects. *Keeping today's `ATProtoNet.Lexicon.*`*:
`ATProtoNet.Lexicon` becomes the schema-model namespace, and the singular/plural split
(`Lexicon` = the language, `Lexicons` = the definitions) is deliberate. *A friendly namespace map*
(`ATProtoNet.Bluesky.Feed`, Q-8): one more table to maintain for every upstream family, a
namespace that no longer tells you the NSID, and users' Lexicons would follow different rules
than the SDK's.

## D-06 A session is the authenticated transport

**Status: Accepted** (maintainer, 2026-10-17).

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

**Status: Accepted** (maintainer, 2026-10-17).

**Decision.** Firehose, label streams, Jetstream, Jetstream archive replay and Tap are all
consumed as `IAsyncEnumerable<TEvent>` over one shared event-stream core (connect, frame decode,
reconnect with backoff, cursor store). The cursor of event *n* is committed when event *n+1* is
pulled, which gives at-least-once delivery. Sync 1.1 verification is a composable stage, not a
different consumer class. `ATProtoNet.Hosting` provides a `StreamWorker<TEvent>` hosted-service
adapter, so worker services need no ASP.NET Core.

**Rejected.** .NET events or callback registration (no back-pressure, error handling scattered).
Public `Channel<T>` (leaks buffering decisions).

## D-08 Spaces is a first-class part of the protocol

**Status: Accepted** (maintainer, 2026-10-17), replacing the first draft's
"experimental, ported last" treatment.

**Context.** Atproto Spaces (formerly "the permissioned data protocol") is AT Protocol's second
data protocol: a different way of storing and syncing data that shares everything else (DIDs,
OAuth, Lexicon, XRPC) with public repositories. The upstream team expects it to become the basis
for most experiences in the Atmosphere. Its alpha has run since August 2026. Per the Bluesky
protocol team's published timeline, October 2026 batches the remaining breaking changes into a
draft specification, and **general availability (GA) is targeted for November 2026**, with
reviewed specifications, stable SDKs and the reference PDS rollout. The rewrite ships after that.
0.8 bolted Spaces on: Spaces signing inside the generic transport, Spaces knowledge in central
hosting code, and every wire change a breaking change of the core package because nothing marked
it.

**Decision.** Both data protocols are designed in from the start and are equally first class:

- **Woven through every layer, as public repositories are.** Space addressing in the syntax layer,
  STAR serialization next to CAR, key agreement and HTTP message signatures in crypto, space
  repositories and commitments next to the MST, space host entries in identity, space
  credentials beside OAuth and service auth, a records API that addresses public repos and spaces
  alike, space sync beside the firehose, and space server roles in `ATProtoNet.AspNetCore`
  ([02 — Architecture](02-architecture.md#layers-inside-atprotonet)).
- **Built in the phase of each layer**, not in a separate final phase
  ([05 — Rewrite plan](05-rewrite-plan.md)).
- **Targets the GA protocol.** [specs/spaces.md](specs/spaces.md) tracks the proposal and draft
  specification, re-validates against the GA specification in phase 0 and again in phase 10,
  and takes its conformance vectors from the GA reference implementation.
- **Stable surface.** Spaces APIs are part of the 0.9 compatibility promise and carry no
  `[Experimental]` attribute. Fallback only: if the GA specification has not been published when
  0.9.0 ships, the Spaces APIs carry `[Experimental("ATPROTO001")]` until the 0.9.x release that
  follows GA.

**Consequences.** Upstream changes still landing before GA (STAR serialization, the KEM-based
commitment with a new DID document key, optional credential proofs, the URI scheme) are absorbed
into the specs now rather than retrofitted. Spaces work runs on the critical path. Spaces stays
out of code that has no reason to know it: the core XRPC invoker signs nothing Spaces-specific
(signed requests are an `IXrpcHandler`), and hosting knows Spaces only through its own feature
registration.

**Update (2026-10-22, alpha release).** The drafts listed above landed upstream (proposals #125)
and the specs follow them; DID documents gain only `#atproto_kem`, since authorities now use
`#atproto_pds` and `#atproto`. The timeline moved: one more breaking alpha release on
2026-10-29, a November shakeout and **beta at the end of November 2026**, with no GA date. The
decision stands; the experimental fallback is now the likely path if 0.9.0 ships before GA.

**Update (2026-10-24, maintainer).** The beta counts as GA for this decision. Upstream defines it
as "in-production on bsky PDSes with real users" (Daniel Holmgren, Bluesky, 2026-10-12), with no
major breaking changes planned after the 2026-10-29 release. If the beta specification is
published when 0.9.0 ships, the Spaces APIs ship without `[Experimental]`, pinned to the beta text,
and later revisions are absorbed by re-pins (SPC-2). The `ATPROTO001` fallback applies only if
0.9.0 ships before the beta.

## D-09 Blazor components

**Status: Accepted** (maintainer, 2026-10-17; Q-1).

**Context.** `ATProtoNet.Blazor` ships `LoginForm` plus `FeedView`, `PostCard`, `ProfileCard`
and `ComposePost` (223 lines total). The feed and post widgets encode product decisions (layout,
styling, which embeds render) that apps replace in practice. The login itself is the server
package's.

**Decision.** There is no Blazor package in 0.9. `samples/ServerIntegrationSample` carries all of
it as copyable code: the per-circuit user client accessor (same user → same client, sign-out or
user change → dispose, re-check the store), an unstyled login form that posts to the
`ATProtoNet.AspNetCore` login endpoint, and the `FeedView`, `PostCard`, `ProfileCard` and
`ComposePost` widgets. The sample builds with the solution in CI, so the code people copy keeps
compiling against the API. [specs/hosting.md](specs/hosting.md#blazor) describes it.

**Rationale.** The widgets are Bluesky product UI that hard-code English strings and Bootstrap
classes, and they shipped broken until 0.7. What remains (an accessor and a form) is too small
to justify a package with its own version, baseline and support promise, and apps adapt it
anyway. Everything the sample needs is public API of `ATProtoNet` and `ATProtoNet.AspNetCore`.

**Rejected.** *A minimal package with the accessor and the login form* (the first draft's
proposal).

## D-10 Serialization: System.Text.Json with source generation, generated union converters

**Status: Accepted** (maintainer, 2026-10-17).

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

**Status: Accepted** (maintainer, 2026-10-17; Q-3: explicit `Parse` only).

**Decision.** Identifier types have `Parse`/`TryParse` and an implicit conversion *to* `string`,
never *from* it. See [AG-8](03-api-guidelines.md#types).

**Rationale.** An implicit conversion that throws hides validation at call sites. The verbosity
cost is small and helpers accept the typed identifier.

## D-12 Generated XRPC methods take one parameters record

**Status: Accepted** (maintainer, 2026-10-17).

**Decision.** `client.Bsky.Feed.GetTimelineAsync(new() { Limit = 50 })`, never positional
optional parameters. See [AG-12, AG-13](03-api-guidelines.md#methods-and-parameters).

**Rationale.** Lexicons gain optional parameters all the time. With positional optional
parameters each sync is a binary break. With records it is an additive change. It also removes
any need for a hand-maintained parameter-order convention, and the generator needs no heuristics.

## D-13 Lexicon namespace access through extension properties

**Status: Accepted** (maintainer, 2026-10-17).

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

**Status: Accepted** (maintainer, 2026-10-17).

**Decision.** The `ATProtoNet.Identity` namespace (layer 1) owns DID documents,
`did:plc`/`did:web` resolution, handle resolution (DNS TXT through a pluggable `IDnsTxtResolver`,
defaulting to DNS-over-HTTPS as today, plus HTTPS well-known), bidirectional verification,
caching, and the PLC directory client and operations. All identity HTTP goes through an
`HttpClient` whose handler enforces the SSRF policy (D-21). The client layer depends on it; the
foundation layer does not.

## D-15 Store interfaces live next to their consumers; implementations live with their dependencies

**Status: Accepted** (maintainer, 2026-10-17).

**Decision.** `ISessionStore`, `IOAuthStateStore`, `ICursorStore`, `IReplayStore` and
`IRepoSyncStateStore` are defined next to their consumer. In-memory implementations ship with
the interface. File implementations (and, for OAuth state, a distributed-cache one) live in the
`ATProtoNet.Hosting` namespace of the base package. EF Core implementations live in
`ATProtoNet.EntityFrameworkCore`. Each interface has a shared contract
test suite that every implementation runs, **including concurrency cases** (0.8's EF session
store races on the first write for a DID because the contract never tested it). The session
store contract includes compare-and-swap (D-22). Details: [specs/stores.md](specs/stores.md).

## D-16 Experimental diagnostic IDs

**Status: Accepted** (maintainer, 2026-10-17).

<a id="experimental-diagnostic-ids"></a>

| ID | Surface |
|---|---|
| `ATPROTO001` | Spaces, only as a fallback if neither the beta nor the GA specification is published when 0.9.0 ships (D-08) |
| `ATPROTO002` | Bluesky `app.bsky.unspecced.*` endpoints |
| `ATPROTO003` | Jetstream v2 archive API |
| `ATPROTO004` | Lexicon runtime resolution (`com.atproto.lexicon.*`, until the spec settles) |

New IDs are appended here. An ID is retired (never reused) when its surface stabilises or is
removed.

## D-17 Repository and branch workflow for the rewrite

**Status: Accepted** (maintainer, 2026-10-17; Q-6: no GitHub mirror).

**Decision.** The `next` branch holds the new tree. Each phase lands as one or more PRs into
`next`, each tied to a tracker issue and following the existing issue workflow (CHANGELOG
excepted: `next` keeps its own `CHANGELOG.md` that starts at 0.9.0). `main` merges into `next`
only for vendored-Lexicon or spec-relevant fixes. When phase 11 passes, `next` replaces `main`.

**One forge.** Forgejo (`git.grandiras.net`) is the only forge: the tracker, CI and releases. The
GitHub mirror and its issue, comment and PR sync are dropped (Q-6), so `next` has no
`.github/workflows/` and no `sync-to-github.yml`, and `CONTRIBUTING.md` points at Forgejo only.
The issue-workflow skill moves from `.github/skills/` to `.claude/skills/`. A future mirror to
Tangled is out of scope for this design; when it comes, it is repository configuration, not a
build concern, since CI never depends on a mirror.

## D-18 Lexicon unions are C# 15 union types

**Status: Accepted** (maintainer, 2026-10-17).

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

**Status: Accepted** (maintainer, 2026-10-17).

**Decision.** A Lexicon `integer` with `minimum` and `maximum` both inside `Int32` range maps to
`int`; anything else maps to `long`. The rule is mechanical, so `limit` (1–100) is an `int` and
unbounded counts are `long`. 0.8's hand-written code used `int` in 189 places and `long` in 33
with no rule; the old generator used `long` everywhere.

## D-20 JSON reads are case-sensitive and strict about number types

**Status: Accepted** (maintainer, 2026-10-17, after the context below and the string-carried
numbers addition).

**Context.** 0.8's shared serializer options (`AtProtoJsonDefaults`) have enabled
`PropertyNameCaseInsensitive` and `JsonNumberHandling.AllowReadingFromString` since the initial
commit (0.1.0). No changelog entry or issue gives a reason, and the only test
(`CommonModelsTests.Options_CaseInsensitiveDeserialization`) checks the option itself, not a
producer that needs it. On the protocol side:

- Lexicon property names are case-sensitive. The reference implementation (TypeScript) reads
  properties by exact name, and DRISL-CBOR records, the authoritative form in a repository, are
  keyed by exact strings.
- The data model writes `integer` as a JSON number. The reference Lexicon validator rejects a
  string where an integer is expected, and the reference XRPC client validates responses, so it
  already fails a response that sends `"42"` for an integer.

What the two options do, measured on .NET 10 with a model shaped like 0.8's (a `text` string, an
`int64` count and `[JsonExtensionData]` for unknown fields):

| Input | 0.8 options | Strict |
|---|---|---|
| `{"text":"hello","Text":"spoofed"}` | reads `spoofed`, writes back `{"text":"spoofed"}` | reads `hello`; `Text` round-trips as an unknown field |
| `{"text":"hello","LikeCount":7}` | binds `LikeCount` to `likeCount`, writes back `likeCount` | `LikeCount` round-trips as an unknown field |
| `{"text":"hello","likeCount":"42"}` (or `"+42"`) | reads 42, writes back the number `42` | `JsonException` |

So, under 0.8's options:

- **A .NET client can show different content than everyone else.** In a record with a second key
  that differs only in case, .NET shows whichever comes last; the reference implementation, the
  AppView and labelers use the exact name. The record's author chooses which one .NET users see.
- **Read-modify-write changes data.** A case-variant unknown field is folded into a known property
  and lost; a numeric string is rewritten as a number. Both change the record's CBOR and so its
  CID, and the first breaks the unknown-field round trip ([D-10](#d-10-serialization-systemtextjson-with-source-generation-generated-union-converters)).
- **Invalid data looks valid.** A record that fails upstream validation deserialises cleanly.

`datetime` leniency, which this record keeps, is different in kind: it keeps the original text
and writes it back unchanged (0.8 #119), so it loses nothing and the CID survives. The two
dropped options are lenient *and* lossy.

**Numbers carried in strings.** The data model has no floating-point type, so Lexicons that need
fractions declare a plain `string` and write the number as text
(`community.lexicon.location.geo` does this for coordinates). This record does not affect them:
the Lexicon says `string`, so the generated property is a `string` in 0.8 and in 0.9 alike, and
`AllowReadingFromString` never applied. It only helped a *hand-written* `double` property, and
then only one way: 0.8 reads `"52.520"` as 52.52 but writes it back as the JSON number `52.52`, a
float that the Lexicon and the data model both forbid. The rewrite makes the convention explicit
instead (maintainer, 2026-10-17: it must stay possible, flagged by the developer):
`DecimalString` keeps the text exactly and parses it in the invariant culture
([SYN-82–87](specs/syntax.md#numbers-carried-in-strings)), and the generator uses it for a field
the configuration maps with `fieldTypes` ([LEX-92](specs/lexicon.md#generated-clients)).
Hand-written models can use the type, or System.Text.Json's per-property `[JsonNumberHandling]`.

**Decision.**

1. Reads are case-sensitive: no `PropertyNameCaseInsensitive`. *Change from 0.8.*
2. Numbers are not read from strings: `JsonNumberHandling.Strict`. *Change from 0.8.*
3. Identifier fields stay strict on read (an invalid DID fails deserialisation with the JSON
   path); `datetime` fields stay lenient (the text is kept, `IsValid` says whether it parses).
   *Unchanged from 0.8.*
4. JSON success bodies are capped (`MaxJsonResponseBytes`, 16 MiB by default, configurable), as
   error bodies already are ([XRC-26](specs/xrpc-client.md#responses)). *Change from 0.8*, from
   the hostile-input principle rather than from wire fidelity.

**What strictness costs.** A producer that sends a case variant or a numeric string now fails
where 0.8 accepted it:

- an XRPC response with such a field throws `XrpcResponseFormatException`, as the reference
  client does;
- a typed record read (`Records<T>()`, typed stream decoding) that meets such a record throws
  ([XRC-49](specs/xrpc-client.md)); enumeration does not skip it;
- record bodies embedded in views (`postView.record`) are Lexicon `unknown`, generated as
  `DataModelValue`, so a bad record in a feed page fails only the caller's conversion of that
  record, not the page.

No such producer is known. The integration suites (the official PDS, Tranquil, the Bluesky
AppView, Jetstream) are the check: a real producer that relies on either leniency is the evidence
that would reopen this record.

**Alternatives.**

- *Keep 0.8's options.* Tolerates producers nobody has identified, at the costs above.
- *Strict by default, lenient by opt-in.* Possible (the options belong to a serializer context),
  but it is a switch ([AG-22](03-api-guidelines.md#options-configuration-and-dependency-injection))
  that every read path would have to honour and test. Deferred until a producer needs it.
- *Tolerate bad records in enumeration.* If the concern is one bad record stopping a listing, the
  fix belongs in XRC-49 (for example an enumeration that yields undecodable records as errors
  instead of throwing), not in global JSON leniency.

## D-21 One untrusted-fetch layer

**Status: Accepted** (maintainer, 2026-10-17).

**Decision.** The SSRF-guarded HTTP machinery (connect-time address checks on every resolved
address, no proxy, no redirects, bounded bodies, timeouts, a concurrency cap, DNS TXT via
DNS-over-HTTPS) is a public layer in the `ATProtoNet.Net` namespace (layer 1), with its
own `UntrustedFetchException`. Identity resolution, OAuth discovery, Lexicon resolution and repo
fetchers all use it. In 0.8 it is `internal` in `Identity/`, used by OAuth and Lexicon code that
then has to catch `DidResolutionException` for non-identity failures, and it keeps process-wide
state (the concurrency cap, shared handlers) that cannot be configured or injected. In the
rewrite that state belongs to an instance the caller can supply.

## D-22 Session stores support compare-and-swap

**Status: Accepted** (maintainer, 2026-10-17).

**Decision.** `ISessionStore` gains a versioned compare-and-swap
(`TryReplaceAsync(Did, expectedVersion, next)`). Refresh, adoption of a newer stored session,
conditional removal on expiry and logout are expressed as CAS operations. This replaces 0.8's
hand-built optimistic concurrency in `SessionManager` and makes multi-instance deployments
correct without a distributed lock. The shipped implementations (memory, file, EF via row version
and upsert) pass the same concurrent contract tests. There is no `IDistributedCache` session store,
because `IDistributedCache` cannot compare-and-swap; shared stores such as Redis implement the
contract themselves, using the published contract tests.

## D-23 Stream side information is delivered in-band

**Status: Accepted** (maintainer, 2026-10-17).

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

**Status: Accepted** (maintainer, 2026-10-17): full scope, nothing deferred. Supersedes the
first draft's "primitives, client and syncer; server only if stable".

**Decision.** The rewrite implements the whole GA protocol, client and server side:

1. **Primitives:** space addressing (`SpaceUri`), STAR serialization, BLAKE3 and LtHash, key
   agreement for the KEM-based commitment and MACs, space token formats, RFC 9421 HTTP message
   signatures with RFC 8941 structured fields.
2. **Client:** space credentials (delegation token, space credential, client attestation, with
   the optional proofs that make public spaces readable without an account), revocation, signed
   requests through an `IXrpcHandler`, the records API for spaces, generated
   `com.atproto.space.*` and `com.atproto.simplespace.*`.
3. **Sync:** the space syncer (operation log, recovery, sequenced write notifications,
   `listRepos` checkpoints) and a notification receiver.
4. **Server roles:** space authority (credential issuing, revocation, notification sequencing and
   forwarding), repo host, `simplespace` (independent read and write policies, open write
   spaces, updatable policies), and their stores.

The Atmosphere groups standard layers on top of Spaces through Lexicons; it needs no SDK
machinery beyond generating its Lexicons once they are published.

## D-25 Target .NET 11 and C# 15

**Status: Accepted** (raised by the maintainer, 2026-10-17; Q-14: `net11.0` only).

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
is a regeneration.

---

## D-26 Floating point numbers: string-encoded decimals, strict data model

**Status: Accepted** (maintainer, 2026-10-18; upstream checked the same day).

**Context.** The data model lists float as not allowed and Lexicon has no float type; the spec
recommends strings or bytes and leaves room for future rules
([data model](https://atproto.com/specs/data-model#why-no-floats)). The community discussion of
May–June 2026 ([Floats on ATProto](https://discourse.atmosphere.community/t/floats-on-atproto/877))
moved to the IETF ATP working group. IETF 126 (2026-07-23) heard a proposal for finite binary64
CBOR floats, inferred from JSON syntax (`.` or `e` means float, whole floats written as `5.0`)
([slides](https://hackmd.io/@vmx/ietf-126-atp-floats)). Newbold and Holmgren called syntax-based
inference fragile; there was no decision, only an action item to refine it on the ATP list
([minutes](http://ietfminutes.org/minutes/ietf126/atp.html)). Meanwhile community Lexicons such
as `community.lexicon.location.geo` carry numbers as strings.

| Option | For | Against |
|---|---|---|
| A. String-encoded decimal helpers | Matches what existing Lexicons publish; lossless round trip | Opt-in per field; callers parse |
| B. Scaled integers | Exact, no parsing | The scale is not expressible in Lexicon, so codegen cannot know it |
| C. Wait on upstream | No speculative API | Leaves today's string-carried numbers unsupported for an unknown time |

**Decision.**

1. **A**: `DecimalString` ([SYN-82–87](specs/syntax.md#numbers-carried-in-strings)) stores the
   original text and exposes `double` and `decimal` accessors, so an untouched record round-trips
   byte for byte (`"-33.80"` does not become `"-33.8"`, the CID does not change). Only values the
   app sets are formatted: invariant culture, shortest round-trip text. Codegen maps a field only
   when the configuration names it ([LEX-92](specs/lexicon.md#generated-clients)); no guessing.
   The parser accepts plain decimals with an optional exponent and rejects `NaN`, infinities, hex,
   group separators and a leading `+`.
2. **B** as a small static helper plus a docs page; no codegen support.
3. The data model stays strict: the DRISL encoder never writes floats, the decoder rejects them
   (DAT-1, DAT-24, DAT-41). Number handling sits behind one internal seam, so a native float kind
   can be added when upstream defines one.

**Why not wait (C).** Upstream's outcome changes the wire format of future floats, not the fact
that existing Lexicons store numbers as strings, so A stays useful either way.

**Revisit** when the ATP working group publishes a float mapping draft
([data-model open point 5](specs/data-model.md#open-points)).

---

## Open questions

<a id="open-questions"></a>

All questions are resolved (maintainer, 2026-10-17). Q-1, Q-6 and Q-8 were answered explicitly;
the others took the default this design proposed. A new question gets the next free number
(Q-16 onward).

| # | Question | Outcome | Phase |
|---|---|---|---|
| Q-1 | Keep a minimal Blazor package (accessor + login form) in 0.9, or drop it (D-09)? | **Dropped.** The accessor, login form and widgets are sample code in `samples/ServerIntegrationSample` (D-09). | 9 |
| Q-2 | Keep `PdsAdminClient` and `ATProtoNet.Aspire.Hosting` in scope? The non-goals exclude implementing a PDS, but administering and orchestrating one is in scope today. | Keep both (default). | 11 |
| Q-3 | Accept explicit `Parse` for identifiers (D-11), or allow implicit conversion from `string` for ergonomics? | Explicit only (default, D-11). | 1 |
| Q-4 | Check in generated first-party Lexicon code (D-04) or generate at build time? | Check in (default, D-04). | 3 |
| Q-5 | ~~Is `ATProtoNet.Ozone` worth keeping as a package?~~ | **Resolved by D-03** (first review): `tools.ozone` is generated into `ATProtoNet` like every other first-party family. | — |
| Q-6 | Should the GitHub mirror and issue sync stay as they are during the rewrite? | **Dropped altogether.** Forgejo is the only forge; a mirror to Tangled may follow later and is out of scope (D-17). | 0 |
| Q-7 | Minimum PDS versions to test against in CI (official `bluesky-social/pds` and Tranquil)? | Latest official release plus the pinned CI version (default). | 5 |
| Q-8 | First-party generated namespaces: mechanical `ATProtoNet.Lexicons.App.Bsky.Feed` (D-05) or friendly `ATProtoNet.Bluesky.Feed` via a namespace map? | **Mechanical namespaces, friendly type names.** The generator enforces naming rules, writes a type-name inventory and takes a short, reviewed rename table (D-05, [LEX-89–91](specs/lexicon.md#naming)). | 3 |
| Q-9 | Keep the development-only loopback cookie relay of the OAuth login (about 150 lines in 0.8)? | Drop it; document running the login on `127.0.0.1` with a loopback client ID instead (default, [SRV-56](specs/server.md)). | 9 |
| Q-10 | Keep `atproto-lexgen lexicon` (C# → Lexicon JSON by reflection)? | Drop it; Lexicon JSON is the source of truth, C# is generated from it (default, [tooling](specs/tooling.md)). | 3 |
| Q-11 | ~~How much of Spaces to port?~~ | **Resolved** (first review): all of it, as a first-class part of the protocol (D-08, D-24). | — |
| Q-12 | Keep the Jetstream v1 wire protocol, or v2 only (keeping the v1 timestamp-cursor migration)? | Keep v1 as a second codec; public instances still serve it (default). | 7 |
| Q-13 | Name the ASP.NET Core package `ATProtoNet.AspNetCore` (D-03), or keep 0.8's `ATProtoNet.Server`? | `ATProtoNet.AspNetCore`: "Server" reads like a PDS, and the package changes shape anyway (default). | 9 |
| Q-14 | Target `net11.0` only (D-25), or also `net10.0` for apps staying on the LTS release? | `net11.0` only; .NET 10 and .NET 11 support both end in November 2028 (default, D-25). | 0 |
| Q-15 | Space address syntax: upstream is still debating `at://{spaceDid}/space/…` against a separate `ats://` scheme. | Follow upstream's GA choice; the syntax layer keeps the scheme in one place, so either is a contained change (default). Re-checked at the phase 0 Spaces pin. | 1 |
