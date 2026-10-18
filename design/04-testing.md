# 04 — Testing and verification

The rewrite replaces the public API, so most of today's tests cannot come along unchanged.
Behaviour still has to carry across. This document describes how: a **conformance suite** that
does not depend on either codebase's API, plus a test architecture the new tree has from its
first commit.

## What exists today

| Asset | Size | Notes |
|---|---|---|
| `tests/ATProtoNet.Tests` | 256 files, 59.4k lines, 2,812 test methods (5,151 cases with theory rows), ~15 s | One assembly references all five `src` packages, the generator tool, bUnit, TestHost, EF/SQLite and Roslyn. |
| `tests/ATProtoNet.IntegrationTests` | 15 files, 85 tests | Gated by `[RequiresFact(IntegrationRequirement.X)]`. CI runs 30 of them, picked by class name. |
| `tests/ATProtoNet.DocSnippets` (+ generator) | 412 compiled C# fences | Works well; keep the approach. |
| Vendored vectors | see below | Split between data files and literals pasted into C#. |
| Benchmarks | none | The perf releases' gains (#191, #194–#205) are not reproducible. |

Classification of today's unit tests by test method:

| Class | Share | Carries over as |
|---|---|---|
| Portable conformance: stable bytes/strings in, verdict out (syntax, DAG-CBOR, CID, CAR, MST, commits, proofs, crypto, label signing, frame parsing, token verification, JSON data model) | 37% (1,039 methods) | Vectors in the conformance suite. |
| Hybrid: wire behaviour asserted through the current API (`EndpointRequestTests`, `WireFidelityTests`, `EmptyResponseBodyTests`, resolver hardening, OAuth callback) | 5% (+147 endpoint rows) | Golden wire fixtures extracted from the tests. |
| Behaviour coupled to the current API (DI builder, OAuth login, sessions, consumers, transport, generator) | 52% | Rewritten against the new API, guided by the specs, which list the scenarios. |
| Low value (DTO round-trips, cache internals, trivial forwarding) | 4% | Dropped. |

Today's main problems: one monolithic test assembly, 66 of 256 files touching internals, at
least seven different fake `HttpMessageHandler`s, 34 `Task.Delay` calls and real sleeps, vectors
loaded three different ways with inconsistent provenance, a drift test that regex-scans source
text, and no benchmarks.

## The behaviour bridge

The rewrite must not lose behaviour the current code gets right, but it cannot run the current
tests. The bridge:

1. **Extract** every vector and golden fixture into data files under
   `tests/ATProtoNet.Conformance/vectors/`, with a `MANIFEST.md` recording source repository,
   commit, licence and generator script for each. This includes the vectors that are pasted into
   C# today (`DagCborInteropTests`, `MstKeyDepthTests`, `MstReferenceVectorTests`, `Blake3Tests`,
   `LtHashTests`, `SpaceHttpSignatureTests`, `LabelSigningInteropTests`), the
   `EndpointRequestTests` rows, the `WireFidelityTests` fixtures, the verifier token tables and
   the regex oracle in `IdentifierSyntaxTests`.
2. **Define a protocol adapter**: a small interface of protocol operations that any
   implementation can satisfy:

   ```csharp
   public interface IProtocolAdapter
   {
       SyntaxVerdict ParseIdentifier(IdentifierKind kind, string input);
       byte[] EncodeDataModel(string json);                         // JSON data model → DRISL-CBOR
       string DecodeDataModel(ReadOnlySpan<byte> cbor);             // and back
       string ComputeCid(ReadOnlySpan<byte> block, CidCodec codec);
       string MstRoot(IEnumerable<KeyValuePair<string, string>> entries);
       ProofVerdict VerifyRecordProof(ReadOnlySpan<byte> car, string did, string path, string signingKey);
       CommitVerdict VerifyCommit(ReadOnlySpan<byte> block, string signingKey);
       SignatureVerdict VerifySignature(string publicKeyMultibase, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature);
       TokenVerdict VerifyServiceAuth(string jwt, ServiceAuthExpectations expectations, DateTimeOffset now);
       LabelVerdict VerifyLabel(string labelJson, string publicKeyMultibase);
       FrameParseResult ParseFirehoseFrame(ReadOnlySpan<byte> frame);
       SyncVerdict ApplySyncEvent(SyncState state, ReadOnlySpan<byte> frame);
       // … one member per conformance area; see the area list below
   }
   ```

3. **Implement the adapter over 0.8 first** (`Adapter.Legacy`, referencing the current packages).
   The conformance suite must be green against 0.8 before phase 1 starts. Failures here are bugs
   in 0.8 or in the extraction, and both are worth finding.
4. **Implement the adapter over the rewrite** (`Adapter.Next`) as each phase lands. A phase's
   conformance areas must be green before its gate passes.
5. Retire `Adapter.Legacy` when 0.9 ships.

The conformance suite references only the adapter interface and its vectors. It never uses
`InternalsVisibleTo`, and it never touches either SDK's types directly.

### Conformance areas

| Area | Vectors | Source |
|---|---|---|
| Identifier syntax | `syntax/*.txt` (24 files: valid/invalid for DID, handle, NSID, AT-URI, record key, TID, datetime, CID, language) + regex oracle + line-break cases | atproto-interop-tests `056e5741`, CC0. Today `cid_syntax_valid`, `language_*` and `uri_*` are vendored but never asserted; they will be. |
| Data model | `data-model-fixtures.json`; **new:** interop-tests data-model valid/invalid suites | interop-tests |
| CID and CAR | CAR read/write round trips, CIDv0 rejection, bounds | today's `CarReaderTests` cases |
| Crypto | `signature-fixtures.json`, `w3c_didkey_K256.json`, `w3c_didkey_P256.json`, scalar-range cases | interop-tests `crypto/` |
| MST | `key_heights.json`, `example_keys.txt`, `common_prefix.json`, mst.test.ts vectors, canonical empty node `a2616580616cf6` | interop-tests `mst/`, @atproto/repo |
| Commits and proofs | `commit-proof-fixtures.json`, `record-proof.json` | interop-tests `firehose/`, @atproto/repo 0.10.14 |
| Firehose and Sync 1.1 | `firehose-sync-frames.json` (6 live frames + keys) | captured |
| Jetstream | `jetstream-golden-{header,block}.bin` (provenance to be recorded) | captured |
| Labels | indigo and @atproto/ozone label signatures | pasted today |
| Tokens | service-auth verdict table, DPoP and PKCE cases, OAuth AS-metadata validation table | today's verifier tests |
| Lexicon | `lexicon-resolution.json`; **new:** interop-tests `lexicon/` suites | @atproto/lex-resolver 0.3.0 |
| XRPC wire | golden request/response fixtures (method, path, query, body, headers, response) from `EndpointRequestTests` and `WireFidelityTests` | extracted |
| Spaces | BLAKE3, LtHash, HTTP signatures, structured fields, space tokens, STAR, KEM commitment and MACs | the GA reference implementation (pinned in phase 0; 0.8's alpha vectors for the superseded commit and CAR formats are not carried) |
| Hostile input | the corpus in [Regression corpus](#regression-corpus) | today's regression tests |

## Test architecture of the new tree

```
tests/
├── ATProtoNet.Conformance/          vectors + suite, against IProtocolAdapter
│   ├── vectors/  (+ MANIFEST.md)
│   ├── Adapter.Legacy/              over 0.8 packages (temporary)
│   └── Adapter.Next/                over the rewrite
├── ATProtoNet.TestKit/              shared fakes; referenced by every test project
├── ATProtoNet.Tests/                the base package; one folder per namespace area
│                                    (Data/, Crypto/, Repo/, Lexicon/, Identity/, Xrpc/, Auth/,
│                                    Streaming/, Bluesky/, Spaces/, Hosting/, …)
├── ATProtoNet.AspNetCore.Tests/     TestHost
├── ATProtoNet.EntityFrameworkCore.Tests/   SQLite in memory
├── ATProtoNet.Lexicon.Tests/        CodeGen, source generator, CLI; incl. "generator compiles
│                                    every vendored Lexicon" (Roslyn)
├── ATProtoNet.Architecture.Tests/   dependency rules, conventions
├── ATProtoNet.IntegrationTests/
├── ATProtoNet.Benchmarks/
├── AotSmoke/
└── ATProtoNet.DocSnippets(+.Generator)/
```

### TestKit: one of each fake

- **`FakeXrpcServer`**: one HTTP stub (from today's `HttpStub`). Scripted responses per route,
  throws on unscripted routes, records requests for assertions, can serve golden fixtures.
- **`FakeIdentityNetwork`**: one fake for DID documents, handles and PLC, served through the
  real resolvers via `FakeXrpcServer`. It replaces three fakes: `StubDidResolver`, 24
  NSubstitute substitutes and a real `TcpListener`.
- **`FakeTimeProvider`** (Microsoft.Extensions.TimeProvider.Testing) only. A convention test
  bans `Task.Delay`, `Thread.Sleep` and `Stopwatch` in test code.
- **Store contract base classes**, one per store interface ([specs/stores.md](specs/stores.md)).
  Every implementation (in-memory, file, distributed cache where one exists, EF) derives a test class from the
  contract. Each arrives with its interface, in the phase that defines it (phase 0 outcome).
- **`TestKeys`**, **`TestIds`**: deterministic keys and identifiers.

### Rules

1. One unit-test project per package, so heavy test dependencies (TestHost, EF/SQLite, Roslyn)
   stay out of the base package's tests. Inside `ATProtoNet.Tests`, folders mirror the
   namespace areas. `InternalsVisibleTo` only from a package to its own test project, and only
   when a test genuinely needs it. The default is testing through the public API.
2. Test names: `Method_Scenario_Expected`, as today. Spec-backed tests carry
   `[Trait("Spec", "XRC-12")]` with the requirement ID, so a report can list requirements that
   no test covers.
3. `xUnit1051` stays enabled: tests pass `TestContext.Current.CancellationToken`.
4. No real network, no real sleeps, no wall clock in unit tests.
5. Every regression test names the bug it guards in a comment, with the issue number.

### Lexicon pipeline tests

- **Generator compiles everything**: generate C# for all vendored Lexicons and compile it with
  Roslyn in the test, with zero diagnostics. This extends #141, which compiled emitted code
  instead of asserting strings.
- **Generated code is current**: CI regenerates first-party code and runs
  `git diff --exit-code`. This replaces the drift test, since generated code cannot drift from
  its schemas.
- **Golden emitter output** for a small set of representative Lexicons (record, query with
  pagination, procedure with binary input, open union, closed union, known values, nested defs),
  reviewed as text diffs.
- **Wire fidelity**: the golden XRPC fixtures run against generated clients.

### Integration tests

- `RequiresFact` emits a `Requirement=<name>` trait. CI filters by trait, not by class name, so
  a new test class runs without editing the workflow. One gate implementation, shared through
  TestKit (today there are two).
- `ATPROTO_REQUIRE_INTEGRATION=1` and `TreatNoTestsAsError` keep their meaning.
- PR CI: the official PDS as a service container (pinned version) runs every `Pds` and
  `PdsAdmin` test.
- Nightly or manual: Jetstream (live), Jetstream archive (needs an API key), Bluesky AppView,
  Tranquil PDS. The Spaces reference PDS runs in PR CI next to the official PDS once a GA image
  exists, so Spaces integration is not nightly-only.

### Benchmarks

`ATProtoNet.Benchmarks` (BenchmarkDotNet) covers what the 0.7/0.8 perf passes optimised:
DRISL-CBOR encode/decode, CAR read, MST build and diff, firehose frame decode with
verification, Jetstream event decode, JSON model deserialization of large feed responses, an
XRPC request round trip against an in-process server, and identity resolution with a warm
cache. **Phase 0 runs them against 0.8** and records the baseline. Goal G8 compares against it.

### AOT smoke test

`tests/AotSmoke` is a console app published with NativeAOT in CI. It performs a login against
the fake server, a query, a record create, and decodes a firehose frame. The publish must
produce zero trim or AOT warnings.

## Regression corpus

<a id="regression-corpus"></a>

Bugs fixed in 0.x that the specs carry as requirements. The spec that owns each item is given
in brackets.

**Wire format**
- A record writes exactly one `$type`, first, and no stray `type` (#49) [lexicon].
- `$type` after other properties still resolves the union variant (#50) [lexicon].
- An unknown `$type` or field survives read → write; one unknown variant does not fail a page
  (#115) [lexicon].
- A procedure with no output accepts an empty 200 body (#69) [xrpc-client].
- A procedure with no arguments sends `{}`. Array query parameters repeat their key (#123)
  [xrpc-client].
- The generated code compiles; string assertions let CS0266 ship (#141) [lexicon].

**Identifiers**
- Parsers refuse trailing line breaks (a `$`-anchored regex accepted `did:plc:abc\n`) [syntax].
- DIDs ending in `%`, NSID name length 63, record-key charset [syntax].
- TIDs from one generator are strictly increasing with a stable clock ID (#114) [syntax].

**Repository and data model**
- An MST entry prefix out of range is rejected before allocation (indigo SEC-4) [repo].
- MST nodes written with omitted null links decode but are not canonical; the empty node is
  `a2616580616cf6` [repo].
- A CIDv0 block in a CAR is rejected [data-model].
- A deeply nested CBOR body is rejected, not a stack overflow (100k levels) [data-model].
- A CAR header nested past the depth limit is rejected by *our* limit, not a library default
  (today's `FromBytes_DeeplyNestedHeaderValue…` test fails on a newer `System.Formats.Cbor`)
  [data-model].
- A declared length over 2 GiB is not trusted or cast [data-model].
- A Jetstream block with an implausible event count is rejected [streaming].
- A declared content length that understates the body is still capped [xrpc-client].

**Crypto and tokens**
- A signature with S ≥ n verifies as false; it does not overflow during low-S normalisation
  [crypto].
- A K-256 key cannot sign a DPoP proof that claims ES256 [auth].
- Service-auth: time claims outside the representable range are refused, not thrown; missing
  or unusable `jti` is refused; a forged token does not spend the genuine token's `jti`; an
  audience refusal does not spend the `jti` [auth].

**OAuth, identity, sessions**
- The callback resolves the account afresh (no cache) and checks the issuer; a handle
  verification timeout yields `handle.invalid`, not a failed login (#42) [auth].
- The OAuth client metadata document omits unset fields (#41) [auth].
- No cross-origin redirect is followed during identity fetches; documents with nulls cannot
  poison lookups; invalidation beats a late cache write; fetches are capped (#120) [identity].
- Handle resolution prefers the DNS answer and does not wait for HTTPS; DNS publishing two
  different DIDs fails closed (#193, #52) [identity].
- After an account switch, a rejected call is never resent with the other account's token; a
  call waiting out a 429 is resent to its own service with its own token [auth].
- A rotated refresh token is stored even if the caller gives up or the client is disposed
  [auth].
- Pagination stops on a repeated cursor [xrpc-client].
- A `Retry-After` beyond the configured maximum throws at once [xrpc-client].
- `PlcClient` DID paths are not parsed as absolute URIs (#47) [identity].

**Hosting**
- A typed `HttpClient` registration must not capture the client as transient; configuration
  binds before the user callback; per-user clients get the configured options [server].
- A service-auth caller does not get the account's stored OAuth session (0.7 security fix)
  [server].
- A missing replay store is an error, not a silent in-memory fallback [server].

## CI shape

One build script (`build.sh`), run by Forgejo Actions, the only CI
([D-17](06-decisions.md#d-17-repository-and-branch-workflow-for-the-rewrite)), with these jobs:

| Job | Trigger | Content |
|---|---|---|
| `build` | PR, push | Restore with locked SDK (`global.json`), build with `-warnaserror`, docs and samples included |
| `test` | PR, push | Unit + conformance + architecture tests, coverage (Cobertura), TRX, `--blame-hang-timeout` |
| `lexicons` | PR, push | Regenerate first-party code, `git diff --exit-code`; generator compiles all vendored Lexicons |
| `integration-pds` | PR, push | PDS service container, `Requirement=Pds\|PdsAdmin`, REQUIRE=1 |
| `integration-spaces` | PR, push | Spaces reference PDS service container (GA image), `Requirement=Spaces`, REQUIRE=1 |
| `api` | PR | Public API baseline diff; from 0.9.0 on, package validation against the last release |
| `aot` | PR | Publish `AotSmoke` with NativeAOT, fail on warnings |
| `aspire` | PR | Aspire manifest publish + check |
| `changelog` | PR | `[Unreleased]` gained a bullet unless the PR is labelled `no-changelog` |
| `nightly` | schedule | Jetstream, archive, Bluesky, Tranquil integration; benchmarks vs baseline |
| `release` | tag | Everything above, then verify the CHANGELOG section is non-empty, pack, push |

CI lives in `build.sh`, not in workflow YAML: `.forgejo/workflows/*.yml` only call its jobs, so
the same checks run locally and a later move or mirror (Tangled, Q-6) carries no CI logic.

## Assets to copy verbatim into the new tree

From `tests/ATProtoNet.Tests/` unless noted:

- `Interop/` (syntax vectors, README, `.editorconfig`) → `Conformance/vectors/syntax/`
- `Crypto/TestData/`, `Repo/TestData/`, `Lexicon/TestData/` (with `.mjs` generator scripts),
  `Streaming/TestData/` → `Conformance/vectors/…`
- `Lexicon/Upstream/lexicons/` → `/lexicons/` (becomes the generator input), `refresh.sh` →
  replaced by `atproto-lexgen install`
- `TestSupport/HttpStub.cs`, `InstantTimeProvider.cs` → TestKit (as a starting point)
- The six `*StoreContractTests.cs` and `SpaceCredentialRevocationStoreContractTests` →
  TestKit contracts, adapted to the new store interfaces
- `IntegrationGate.cs` + `IntegrationTests/TestInfrastructure.cs` → one gate in TestKit
- `tests/ATProtoNet.DocSnippets.Generator/` and `SnippetContext.cs`
- `Conventions/*.cs` as templates for architecture tests
- From `.forgejo/workflows/ci.yml`: the PDS service-container block and the REQUIRE +
  `TreatNoTestsAsError` pattern

Not carried over: `.github/workflows/` (the GitHub mirror's CI and issue, comment and PR sync)
and `.forgejo/workflows/sync-to-github.yml`. The mirror is dropped (Q-6).
