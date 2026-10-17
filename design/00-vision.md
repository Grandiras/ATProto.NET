# 00 — Vision, goals and principles

## Why rewrite

ATProto.NET reached near-feature-completeness in eight months (0.1.0 in February 2026, 0.8.0 in
October 2026). It got there by accretion: each protocol area was added when it was needed, then
the code went through repeated "debloat" and "perf" passes (#180, #194–#208) to recover from the
growth. 0.7.0 alone carried 366 changelog entries. The behaviour is good and well tested
(5,151 unit tests), but the shape is not:

- **Nothing inside the core keeps areas apart.** `ATProtoNet` is 54k lines and 9,416 lines of
  `PublicAPI.Shipped.txt`. It holds the identifier types, CBOR, crypto, the MST, OAuth, five
  Lexicon namespaces, three streaming protocols, Tap and an alpha data protocol (Spaces), with no
  layering between them: the XRPC transport signs Spaces requests and knows PDS admin auth, the
  PLC client (identity) depends on streaming and Lexicon code, and five components each run their
  own HTTP stack.
- **The Lexicon surface is written by hand.** About 21k lines of clients and models restate
  upstream Lexicon JSON. A drift test compares them against vendored upstream schemas (kept in
  the *test* project), and every upstream change is a manual port. Meanwhile the repo ships its
  own code generator (`atproto-lexgen`) that emits a different style.
- **The production assemblies reach into each other's internals.** `ATProtoNet` grants
  `InternalsVisibleTo` to `ATProtoNet.Server` and `ATProtoNet.Server.EntityFrameworkCore`, and
  `ATProtoNet.Server` grants it to `ATProtoNet.Blazor` and the EF package. The public seams are
  not the real seams.
- **Mechanisms are tangled with each other.** Sessions, refresh and transport are coupled through
  `SessionManager` (1,092 lines) plugged into `XrpcClient` (928 lines). `AtProtoClient` builds
  29 sub-clients by hand. `ATProtoNet.Server` mixes OAuth web login, service auth, XRPC hosting,
  labels, the Tap webhook, the Spaces server, Aspire client wiring and PDS admin binding.
- **Alpha protocol churn breaks the stable core.** Spaces changed its wire format or stored
  schema five times across 0.7 and 0.8 (#113, #211, #212, #213, #214). Each change is a breaking
  change for *every* user of the `ATProtoNet` package, because nothing marks the Spaces API as
  unstable.
- **Package lines in the wrong places.** `ATProtoNet.Server` mixes eight jobs and forces the
  ASP.NET Core shared framework on worker services that only want DI and a stream worker. The EF
  stores were merged into it and split out again, and an Aspire package was merged in.

[01 — Current state](01-current-state.md) has the full audit with evidence.

## What the rewrite is

A new codebase, developed in this repository, that:

1. keeps the **protocol behaviour** the current library gets right: every spec rule, security
   hardening measure and bug fix that today's tests pin;
2. replaces the **shape**: internal layering, public API and internal mechanisms, with as few
   packages as possible (the base `ATProtoNet` package plus extensions for ASP.NET Core, EF Core,
   Blazor and Aspire);
3. is **generated where the protocol is declarative** (Lexicons) and hand-written only where
   code adds meaning;
4. is anchored to **written specs** (`design/specs/`), each requirement traceable to an
   atproto.com specification and to a test.

It is not source-compatible with 0.8. It ships as **0.9.0** (previews first). The specs and the
conformance suite carry the behaviour across; [05 — Rewrite plan](05-rewrite-plan.md) describes
how.

## Goals

| # | Goal | How we know |
|---|------|-------------|
| G1 | **One home per mechanism.** Every protocol mechanism (XRPC call, auth attach, refresh, cursor loop, union decoding, …) exists exactly once. | Architecture review per phase; no "second implementation" survives code review. |
| G2 | **Few packages, clear layers.** Consumers install `ATProtoNet` plus at most one or two integration packages. Inside, namespaces form layers: the foundation does no I/O, and dependencies only point down. | Architecture tests on namespaces fail the build on a forbidden reference. No `InternalsVisibleTo` between production assemblies. |
| G3 | **Generated Lexicon surface.** All `com.atproto`, `app.bsky`, `chat.bsky`, `tools.ozone`, `site.standard` models and clients come from vendored Lexicon JSON through one generator, which users also use for their own Lexicons. | Hand-written Lexicon LOC ≈ 0 outside helpers. Syncing upstream means updating JSON and regenerating. |
| G4 | **Spec-anchored behaviour.** Each subsystem has a normative spec in `design/specs/`. Each MUST is covered by a conformance or unit test. | Requirement IDs appear in test names or traits; a coverage report lists untested IDs. |
| G5 | **Stable core, marked churn.** Alpha protocols (Spaces) and unspecced upstream surfaces are `[Experimental]`, so callers opt in knowingly and changes there do not break anyone else. | A Spaces wire change touches only `[Experimental]` APIs; the API-compatibility check of the 0.9 line passes. |
| G6 | **AOT- and trim-safe.** No reflection-based registries or serializers on the hot path. JSON goes through source-generated `JsonSerializerContext`s. Since the base package contains all generated Lexicons, trimming is what keeps apps small. | `IsAotCompatible=true` on every runtime package; a NativeAOT smoke app builds with zero trim/AOT warnings. |
| G7 | **Small, learnable public API.** One way to do each thing. Fewer, more orthogonal types. | Per-package API baselines, reviewed at each phase gate. |
| G8 | **Performance at least as good as 0.8** on the paths the perf passes optimised (XRPC request path, JSON models, firehose and CAR decoding, Jetstream, identity lookups). | A BenchmarkDotNet suite compares 0.9 against 0.8 on the same scenarios. |

## Non-goals

- **Implementing a PDS, relay or AppView.** The SDK lets you *talk to* and *administer* them,
  *verify* their data, and *serve your own XRPC endpoints*. Hosting a PDS stays Aspire container
  orchestration of existing images.
- **Source compatibility with 0.8.** A migration guide maps old to new. Shims are not shipped.
- **Targets older than .NET 11.** The SDK uses C# 15 unions and closed hierarchies and .NET 11's
  built-in Zstandard ([D-25](06-decisions.md#d-25-target-net-11-and-c-15)). Exception: the Roslyn
  source generator, which must target `netstandard2.0`.
- **UI component libraries.** UI components that encode product opinions (feed widgets, post
  cards) are out of scope; see [decision D-09](06-decisions.md#d-09-blazor-components).
- **Porting every 0.8 convenience.** A helper survives only if it adds meaning beyond the
  generated call (rich-text facets, post composition, blob upload with MIME detection). Thin
  wrappers that rename a generated method do not.

## Design principles

These settle disagreements during the rewrite. When two principles conflict, the earlier one wins.

1. **The protocol is the spec.** Behaviour follows atproto.com specs and the upstream reference
   implementation. Where the spec is silent and we choose, the choice is written down in the
   relevant `design/specs/*.md`.
2. **Hostile input is the default.** Everything that crosses a network or file boundary is
   bounded (size, depth, count, time) before it is parsed. The bounds are explicit constants in
   our code, not library defaults; the CBOR depth regression described in
   [01 — Current state](01-current-state.md#build-health) is what happens otherwise.
3. **Make illegal states unrepresentable.** Identifiers are validated value types. A session is
   either live or absent; there is no "client in the logged-out state". Required Lexicon fields
   are `required`. A value that is one of a fixed set of alternatives is a C# 15 `union` or a
   `closed` hierarchy, so the compiler checks that every case is handled.
4. **Declarative in, generated out.** If a structure is fully described by a schema (Lexicon
   JSON), it is generated. Hand-written code sits *on top of* generated code and never restates
   it.
5. **One way to do it.** A second path to the same result needs a written reason. Overloads that
   differ only in convenience are avoided; optional data goes in records, not parameter lists.
6. **Few packages, pay at run time only for what you use.** A new package exists only where it
   would otherwise force a heavy framework dependency (ASP.NET Core, EF Core, Aspire) on
   everyone. Inside a package nothing costs anything until used: lazy over eager, no background
   work the caller did not ask for, and trimming removes code an app never touches.
7. **Composition over configuration.** Behaviour is assembled from small parts (an
   `IXrpcHandler`, an `IDidResolver`, a store) rather than toggled with option flags. Options
   exist for values (timeouts, limits, URLs), not for choosing code paths.
8. **Explicit dependencies, no ambient state.** No statics holding configuration or caches. Time
   comes from `TimeProvider`, HTTP from a supplied `HttpClient`/`IHttpClientFactory`, logging
   from `ILogger`. DI is supported everywhere and required nowhere.
9. **Public seams are the real seams.** If an extension package needs something from the base
   package, it is public (perhaps in an `*.Advanced` namespace) or the code is in the wrong
   place. `InternalsVisibleTo` is for test assemblies only.
10. **Docs and tests are part of the API.** Every public API is XML-documented, every guide
    sample compiles, and every spec requirement has a test.

## What "less chaotic" means, measurably

A phase does not pass its gate ([05 — Rewrite plan](05-rewrite-plan.md#phase-gates)) unless it
meets these:

- No hand-written source file over ~400 lines. Generated files are exempt.
- No type with more than one reason to change. In practice: no "manager", "helper" or "service"
  class that owns state *and* I/O *and* policy.
- No `InternalsVisibleTo` to a non-test assembly.
- No public mutable static state. No `static` caches.
- No namespace cycles and no references against the layer order (architecture tests).
- Every runtime package builds with `TreatWarningsAsErrors`, `IsAotCompatible`, nullable
  enabled, and a public API baseline.
- Hand-written runtime code (excluding generated Lexicon code and tests) is **under 30k lines**
  at feature parity. 0.8 has ~46k lines outside the Lexicon tree, ~68k counting the
  hand-written Lexicon tree.
