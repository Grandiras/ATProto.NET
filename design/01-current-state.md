# 01 — Current state (0.8.0)

An audit of the codebase at `6aee361` (v0.8.0, 2026-10-05), done to ground the rewrite. Seven
areas were audited in parallel: core client and transport; authentication; protocol
primitives; streaming, Tap and Spaces; the Lexicon surface and generator; hosting packages; and
tests, docs and CI. The behaviour each area gets right is carried into the specs
(`design/specs/`). This document records the shape of the code and the problems the rewrite
fixes.

## Size

| Project | Files | Lines | Notes |
|---|---|---|---|
| `src/ATProtoNet` | 231 | 53,864 | 9,416 lines of `PublicAPI.Shipped.txt`, 74% of it `ATProtoNet.Lexicon.*` |
| … of which `Lexicon/**` | 92 | 21,200 | hand-written; ~817 public types, 356 client methods |
| `src/ATProtoNet.Server` | 65 | 10,161 | Spaces server is ~49% of it, and 56% of its public API |
| `src/ATProtoNet.Server.EntityFrameworkCore` | 15 | 1,972 | 61% Spaces stores |
| `src/ATProtoNet.Aspire.Hosting` | 7 | 1,373 | |
| `src/ATProtoNet.Blazor` | 3 (+ .razor) | ~1,140 | |
| `tools/ATProtoNet.LexiconGenerator` | 12 | 4,549 | |
| `tests/ATProtoNet.Tests` | 256 | 59,439 | 2,812 test methods, 5,151 cases, ~15 s |
| `tests/ATProtoNet.IntegrationTests` | 15 | 2,861 | 85 tests; CI runs 30 |
| `docs/` | 35 pages | 13,238 | 412 compiled C# samples |

By area, the hand-written runtime code outside the Lexicon tree is roughly: auth 5.6k core +
2.9k server, streaming 6.6k, Spaces 11.5k (client, server, EF), identity 4.4k, repo 3.7k,
crypto 1.4k, transport and serialization 3.5k.

## What is good and must survive

The behaviour is the asset. The audits found the protocol work careful, well tested and in
places better hardened than the reference implementations:

- **Identifier syntax** checked against the official interop vectors and against reference
  regexes with 2,000+ seeded mutations. Line breaks are refused everywhere. `TidGenerator` is
  monotonic across threads.
- **DRISL-CBOR, CID, CAR and MST** pass official vectors. Hostile input is bounded (nesting
  depth, prefix lengths, declared sizes), including fixes for issues found in indigo (SEC-4).
- **Signatures** are low-S on signing and strict on verification, with scalar range checks.
- **The identity fetch layer** checks every resolved address at connect time (no proxy, no
  redirects), with SSRF ranges covering mapped and NAT64 addresses. Caching matches
  `@atproto/identity` semantics: stale-while-revalidate, negative caching, single-flight that
  survives cancellation, refresh floors.
- **The OAuth client** implements the atproto profile completely. That includes uncached
  issuer re-verification at the callback and before every refresh, revoking tokens it refuses,
  confidential clients with key pinning per session, and login CSRF binding on the server.
- **Session concurrency** is careful: single-flight refresh keyed by credential generation,
  exchanges that always land and persist, never resending a request with another account's
  token. `SessionRaceTests` pins all of it.
- **Service auth** issues and verifies tokens with the reference implementation's exact claim set
  and error names. A replay `jti` is spent only after the signature verifies.
- **Sync 1.1 verification** inverts operations on a partial MST to check `prevData`, with
  bounded resync that holds and replays events. The rule table (`Tampered`) and live-frame
  fixtures make it portable.
- **Jetstream archive replay** pins its tip, never skips stalls, filters rows exactly, downloads
  in parallel and delivers in order, then cuts over to live without a gap.
- **The XRPC transport** reads target and credentials once per call, never mutates a shared
  `HttpClient`, bounds error bodies, and deliberately retries nothing but DPoP nonces, 429s and
  one post-refresh resend. The last point is a lesson: a resilience handler under the SDK was
  removed in 0.7 because it resent POSTs and spent DPoP proofs.
- **Wire fidelity**: open unions round-trip unknown variants byte for byte, unknown fields
  survive read-modify-write, and `$type` is read anywhere and written once, first.
- **Engineering hygiene**: PublicAPI tracking, CS1591 and CA2007 as errors, compiled doc
  samples, contract test suites for stores, and an integration gate that can turn skips into
  failures.

## Problems

### 1. No internal layering, cross-package internals, package lines in the wrong places

- `ATProtoNet` contains everything from identifier syntax to an alpha data protocol, which is
  fine for consumers. The problem is that nothing inside it keeps the areas apart: the transport
  knows Spaces signing and PDS admin auth, `PlcClient` (identity) depends on streaming and Lexicon
  code, the identifier types depend on a base32 helper that lives in `Repo`, and five components
  run their own HTTP stacks. Spaces is bolted on rather than designed in: its signing lives in the
  generic transport and its stores in central hosting code, and each of its wire or schema changes
  (five in 0.7/0.8: #113, #211–#214) was a breaking change of the core package.
- Production assemblies use each other's internals. `ATProtoNet` grants `InternalsVisibleTo`
  to `Server` and `EntityFrameworkCore`, and `Server` grants it to `Blazor` and EF. The server
  package uses about a dozen core internals (`AtProtoSessionJson`, `KeyedLock`, `Jwt`,
  `IdentityNetworkPolicy`, `DPoPProofGenerator.CreateView`,
  `AtProtoClient.InstallStoredSessionAsync`, …). Blazor uses `OAuthUser.DidOf`, EF uses
  `SpacePaging`. Mixing package versions breaks.
- `ATProtoNet.Server` mixes eight jobs: OAuth login, service auth, XRPC hosting, labels, Tap,
  the Spaces server, Aspire client integration and PDS admin. Several need no ASP.NET at all,
  yet using any of them pulls in `Microsoft.AspNetCore.App`. That includes the EF repo-sync
  store a firehose worker needs. The package layout has flip-flopped: EF stores merged into
  Server and split out again, and Aspire merged in.

### 2. The Lexicon surface is hand-written

- 21,200 lines restate 407 vendored upstream schemas (kept in the *test* project), covering 87%
  of upstream methods (missing: all `app.bsky.contact.*`, 28 `unspecced.*`, a few `com.atproto`).
  Every upstream sync is manual: #215 touched 35 files, #159 touched 470.
- A drift test **regex-scans `src/*.cs` text** for call sites and needs 86 hand-kept
  `DefOf` mappings. It does not check required-ness, formats, union variant sets, encodings or
  errors. `TypedIdentifierGuardTests` guesses formats from property names, with about 60
  exceptions.
- Unions are abstract base classes. A definition that belongs to two unions gets duplicated
  (`RecordEmbed`/`MessageRecordEmbed`) or a wrong supertype (`ThreadNode : PostEntry` makes a
  thread view a bookmark item). 43 `Unknown*` classes are copy-pasted.
- Models come in three styles (`LexObject` classes, `CursorPage<T>` records that drop unknown
  fields, positional internal records). Records come in two patterns (`LexObject + IAtProtoRecord`
  vs `AtProtoRecord`, the latter needing a reflection contract modifier to fix a duplicate
  `$type`, #49). Types sit outside their NSID namespace, and 11 type names collide across
  namespaces.
- Positional optional parameters make every upstream optional parameter a binary break
  ("new optional parameters sit before `limit`" in 0.8). Ozone methods take up to 22 positional
  parameters.
- **The generator cannot produce the SDK.** Run over the vendored Lexicons it emits 12.5k lines
  with no XRPC methods ("Skip XRPC methods for now"), turns union fields into `JsonElement?`,
  drops unknown fields, maps `cid-link` to `string` and ignores `knownValues` and `nullable`.
  There are three overlapping schema object models (tool, runtime resolver, tests) plus a
  hand-maintained `SdkTypeMap`.

### 3. God classes and duplicated mechanisms

- `XrpcClient` (928 lines) holds target state, five auth schemes (including Spaces HTTP
  signatures and PDS admin Basic), proxy defaults, three retry loops, timeouts, uploads and
  downloads. The Spaces audience is guessed from a query parameter named `repo`, and
  `Did.Parse(repo)` throws on a handle.
- `SessionManager` (1,092 lines, ~14 responsibilities) switches on session kind in seven places
  and hand-builds optimistic concurrency because the store has no compare-and-swap. An OAuth
  session cannot refresh itself: every install must pass the `OAuthClient` alongside, and
  omitting it silently keeps the previous one.
- **Two transport APIs.** Built-in clients use the internal `XrpcClient` with string NSIDs (308
  call sites). Third parties get the public `IXrpcTransport`. Built-in methods cannot take
  per-call options, so shared clients rely on mutable client-wide `SetProxy`/`SetLabelers`.
- **Four credential lifecycles**: `SessionManager`, `PdsAdminClient`'s re-login loop,
  `SpaceCredentialProvider`'s cache, and outbound service auth on a raw `HttpClient`.
  **Five HTTP stacks**, each with its own error mapping: transport, Jetstream archive, Tap,
  Spaces credential exchange, repo fetchers.
- **"Encode without `sig`, then sign" is implemented five times** (commit, commit splice,
  labels, PLC, space commits). **Four MST walkers** across two MST implementations. Key order on
  write is enforced four ways. CIDs exist as `Cid`, `byte[]` and base32 `string`.
- `AtProtoClient` (884 lines) wires 29 sub-clients by hand, uses two-phase initialisation for
  the Bluesky helpers (`Bind(this)`), and duplicates its transport as forwarding methods.

### 4. Global and ambient state

`LexiconTypeRegistry.Instance` (process-global and add-only), the static
`AtProtoJsonDefaults.Options`, a `[ThreadStatic]` union re-read trick (70 lines of reentrancy
logic), `DPoPNonceCache.Shared`, `DidKeyCache.Shared`, `TidGenerator.Shared`, the identity fetch
concurrency cap and shared handlers, and a thread-static CBOR buffer. Client-wide
`LatestRepoRev`/`LatestRateLimitInfo` are not scoped per account and are not reset on sign-out.
Several components read the wall clock instead of the injected `TimeProvider` (DPoP proofs,
service-auth tokens, the OAuth service, handle cache, EF timestamps).

### 5. Inconsistent consumption models and APIs

- Streams return `IAsyncEnumerable`, but entry points, position properties and side channels
  differ per stream. Four callbacks carry side information. `OutdatedCursor` is an in-band event
  on the firehose, a callback on Jetstream and an exception on the PLC export. Verification
  failures drop firehose events but only annotate labels.
- The replay consumer nests a live consumer that opens **a second cursor tracker on the same
  store key**, so two writers race and the store can see non-monotonic writes.
- Registration in `ATProtoNet.Server` has 32 entry points on 7 receiver types, with `With*` and
  `Add*` mixed, an inconsistent `AtProto` prefix, and four different replace semantics.
- Identifiers have two error contracts (`ArgumentException` from `T.Parse`, `FormatException` from
  `IParsable.Parse`) and duplicate entry points (explicit casts, `RecordKey.NewTid`,
  `Tid.NextString`). `AtDatetime` is a struct with an invalid `default`; the rest are records
  whose `with` is meaningless.
- JSON reads are case-insensitive and accept numbers in strings, against Lexicon's
  case-sensitive, round-trip promise. Nothing uses source-generated serialization, so nothing is
  AOT- or trim-safe. Success bodies have no size cap.

### 6. Missing pieces

- No durable `IStreamCursorStore` (only in-memory). No receiver for forwarded Spaces
  notifications. No XRPC subscription hosting, though `docs/xrpc-handlers.md` says
  `subscribeLabels` is served. No `ActivitySource` or `Meter` anywhere. No benchmarks, though
  three releases claimed performance wins. No PLC genesis test vector. The `language_*`,
  `uri_*` and data-model valid/invalid interop vectors are vendored or available but unused.

### 7. Spec drift without a spec

The history shows decisions flip-flopping where no written spec held the line:

- **did:web id comparison:** case-insensitive in 0.4, strict ordinal in 0.7 (#125).
- **DNS/HTTPS disagreement in handle resolution:** an error in 0.7 (#120); DNS wins in 0.8 (#193).
- **MST canonical form:** fixed twice.
- **Low-S normalisation:** a no-op until a later fix.

The rewrite's specs exist to stop this.

## Defects found during the audit

These were found by reading code. The first three were confirmed against the source while
writing this document; the rest are reported by the audits with file and line references and
should be checked on `main` independently of the rewrite.

| # | Defect | Where |
|---|---|---|
| 1 | **The solution does not build on a current SDK.** `ATProtoNet.DocSnippets` treats warnings as errors and a new advisory on a transitive `MessagePack` 2.5.302 trips NU1902. | `tests/ATProtoNet.DocSnippets` (SDK 10.0.402) |
| 2 | **A unit test fails on a current runtime.** `CarReaderTests.FromBytes_DeeplyNestedHeaderValue_IsSkippedWithoutOverflowingTheStack`: `System.Formats.Cbor` now enforces a nesting depth of 64 in `SkipToAncestor`. The CAR reader relied on a library default instead of its own limit. | `Repo/CarReader.cs:271` |
| 3 | `PdsAdminClient` re-authenticates only on HTTP 401, but a PDS reports an expired token as 400 `ExpiredToken`. | `Admin/PdsAdminClient.cs:235` |
| 4 | `JetstreamHealthCheck` always reports `Unhealthy`, so the `failureStatus` argument of `AddJetstream` has no effect (the same bug was fixed for the PDS check in #136). | `Server/Aspire/JetstreamHealthCheck.cs:40` |
| 5 | `EfCoreAtProtoSessionStore.SetAsync` does find-then-add with no conflict handling: two instances writing the first session for a DID race to a key violation. | `EfCoreAtProtoSessionStore.cs:62` |
| 6 | A manual dispatch of `release.yml` creates the Forgejo release with `tag_name` set to the branch name. | `.forgejo/workflows/release.yml:100` |
| 7 | The Tap channel leaves an unreadable event unacknowledged so Tap redelivers it, but Tap holds a repository's later events until the in-flight one is acknowledged, so that repository stalls permanently. The webhook does the opposite. | `Tap/TapClient.cs:272`, `TapWebhookExtensions.cs:169` |
| 8 | The replay consumer and its nested live consumer write the same cursor key from two trackers. | `JetstreamReplayConsumer.cs:126,188` |
| 9 | Under Aspire service defaults, the docs remove the resilience handler from two of the four SDK `HttpClient`s; admin `createAccount` POSTs and Spaces calls are still retried. | `docs/aspire.md:118` |
| 10 | The OAuth login cookie lasts 7 days and nothing checks the session store, so a user whose session expired appears signed in while the client factory returns null. | `AtProtoOAuthExtensions.cs` |
| 11 | An `auth_server_mismatch` during refresh is not treated as session-ending, so the session is retried indefinitely. | `SessionManager.cs:666` |
| 12 | `DagCborEncoder.Encode(object)` serializes with `JsonSerializerOptions.Default`, not the SDK's options, so it produces different bytes and CIDs from what the SDK writes. | `Repo/DagCborEncoder.cs:39` |
| 13 | `SpaceServerOptions.ClockSkew` is ignored for credentials. | `SpaceRepoEndpoints.cs:313` |
| 14 | `RepoSnapshot.Records` silently drops paths that are valid MST keys but not a valid NSID plus record key. | `RepoSnapshot.cs:212` |

## Build health

Building `main` on .NET SDK 10.0.402 in a fresh environment:

- `dotnet build` fails (defect 1).
- `dotnet test tests/ATProtoNet.Tests` runs 5,151 tests in about 15 s: **5,145 pass, 1 fails
  (defect 2), 5 skipped**.
- There is no `global.json`, and the CI image tag floats (`mcr.microsoft.com/dotnet/sdk:10.0`),
  so CI will hit both as soon as its image updates.

The rewrite pins the SDK, uses central package management with source mapping, and sets its own
explicit decoder limits ([00 — Vision, principle 2](00-vision.md#design-principles)).
