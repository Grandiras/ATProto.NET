# ATProto.NET 0.9: rewrite design

This folder holds the design and specifications for a complete rewrite of ATProto.NET into a
cleaner, layered and largely generated shape, released as 0.9. It is the input for the rewrite
itself: each phase of the plan names the specs it implements, and each spec lists testable
requirements.

**Status: reviewed; ready for phase 0.** The maintainer's reviews (2026-10-17) accepted every
decision record except
[D-20](06-decisions.md#d-20-json-reads-are-case-sensitive-and-strict-about-number-types) (strict
JSON reads, pending more context; it blocks nothing before phase 3) and resolved every open
question in [06 — Decisions](06-decisions.md#open-questions).

## Reading order

| # | Document | What it answers |
|---|---|---|
| 00 | [Vision, goals and principles](00-vision.md) | Why rewrite, what success looks like, which principles settle disputes |
| 01 | [Current state](01-current-state.md) | What 0.8 does well, what is chaotic, defects found during the audit |
| 02 | [Target architecture](02-architecture.md) | Packages, layering, core abstractions, cross-cutting concerns, source tree |
| 03 | [API guidelines](03-api-guidelines.md) | Rules every public API follows (`AG-n`) |
| 04 | [Testing and verification](04-testing.md) | The conformance suite that carries behaviour across, and the test architecture |
| 05 | [Rewrite plan](05-rewrite-plan.md) | Phases, gates, working agreements, risks |
| 06 | [Decisions](06-decisions.md) | Decision records (`D-n`) and open questions (`Q-n`) |
| — | [Subsystem specs](specs/README.md) | Normative requirements per subsystem (`SYN-n`, `DAT-n`, …) |

If you have ten minutes: read 00, the package map in 02, and the decision records in 06.

## The design in one page

- **Few packages, clear layers inside.** `ATProtoNet` contains nearly everything: protocol
  primitives, repo, Lexicon, identity, the client, OAuth, streaming, every generated Lexicon family
  (including Bluesky, chat and Ozone), DI and worker integration, the bundled Lexicon source
  generator, and Spaces. Extension packages exist only for heavy framework dependencies:
  `ATProtoNet.AspNetCore` (replacing `ATProtoNet.Server`), `ATProtoNet.EntityFrameworkCore` and
  `ATProtoNet.Aspire.Hosting`, plus the `atproto-lexgen` tool. There is no Blazor package; its
  accessor and login form become sample code. Inside the base package, namespace layers with
  architecture tests provide the structure 0.8 lacks. No `InternalsVisibleTo` between production assemblies.
- **Generated Lexicons.** All `com.atproto`, `app.bsky`, `chat.bsky`, `tools.ozone` and
  `site.standard` code is generated from vendored Lexicon JSON by one generator. That replaces
  21k hand-written lines and a regex-based drift test. Users run the same generator on their own
  Lexicons. Namespaces are mechanical (`ATProtoNet.Lexicons.App.Bsky.Feed`), type names are
  friendly (`PostView`, `ThreadViewPost`). Unions become C# 15 `union` types, known values open
  structs, and XRPC methods typed descriptors that take one parameters record.
- **Two data protocols, both first class.** Public repositories and Spaces (targeting the
  November 2026 GA protocol) sit side by side in every layer: addressing, storage structures,
  credentials beside OAuth, one records API for both, space sync beside the firehose, and space
  server roles in `ATProtoNet.AspNetCore`. Spaces is not a separate phase.
- **A session is the authenticated transport.** `new AtProtoClient(session)`. Password, OAuth,
  service-auth and admin sessions are `IXrpcHandler`s that own their tokens and refresh.
  `AtProtoClient` is immutable and knows nothing about logins.
- **One way per mechanism.** One XRPC invoker, one untrusted-fetch layer, one MST, one signed-object
  helper, one stream runtime with in-band notices, one registration style, one exception
  hierarchy.
- **.NET 11 and C# 15.** Lexicon unions and stream items are C# 15 `union` types, SDK-owned
  alternatives are `closed` hierarchies, and Jetstream uses .NET 11's built-in Zstandard.
- **AOT-safe and observable.** Source-generated JSON, no process-global registries,
  `ActivitySource`/`Meter` per area.
- **Behaviour carried by tests, not code.** Vendored interop vectors and golden fixtures go into a
  conformance suite behind a protocol adapter. The adapter is proven green against 0.8 first,
  then re-implemented on the rewrite, phase by phase.
- **Twelve phases, bottom-up, each with a gate, shipping as 0.9.0.** Foundations, primitives, repo, Lexicon,
  identity, client, OAuth, streaming, Lexicon families, hosting, end-to-end and interop, release.

## Conventions

- Decisions are `D-n`, open questions `Q-n`, guidelines `AG-n`, requirements `{PREFIX}-n` (see
  [specs/README.md](specs/README.md#conventions)).
- IDs are stable: never renumbered, never reused.
- This folder is not compiled by the doc-snippet project (only `docs/*.md` and `README.md` are),
  so C# here is illustrative. Once a phase lands, `docs/` documents the real API and this folder
  keeps the reasoning.
