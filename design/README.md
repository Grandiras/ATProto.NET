# ATProto.NET 1.0: rewrite design

This folder holds the design and specifications for a complete rewrite of ATProto.NET into a
cleaner, layered and largely generated shape, released as 1.0. It is the input for the rewrite
itself: each phase of the plan names the specs it implements, and each spec lists testable
requirements.

**Status: draft for review.** Every decision is *proposed* until the maintainer accepts it. The
open questions in [06 — Decisions](06-decisions.md#open-questions) need answers (or accepted
defaults) before the phases they block.

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

If you have ten minutes: read 00, the package map in 02, and the open questions in 06.

## The design in one page

- **Packages by weight and churn.** Today one 54k-line assembly holds everything. The rewrite
  splits it:
  - **Foundation packages with no I/O:** `ATProtoNet.Primitives`, `ATProtoNet.Repo` and
    `ATProtoNet.Lexicon`.
  - **Network packages:** `ATProtoNet.Identity`, the `ATProtoNet` client and
    `ATProtoNet.Streaming`.
  - **One generated package per Lexicon family:** `ATProtoNet.Bluesky`, `ATProtoNet.Ozone` and
    `ATProtoNet.StandardSite`.
  - **Hosting:** `ATProtoNet.Hosting` with no ASP.NET dependency, then `ATProtoNet.AspNetCore`,
    `ATProtoNet.EntityFrameworkCore` and a minimal `ATProtoNet.Blazor`.
  - **An experimental Spaces family.**
  - **No `InternalsVisibleTo` between production assemblies.**
- **Generated Lexicons.** All `com.atproto`, `app.bsky`, `chat.bsky`, `tools.ozone` and
  `site.standard` code is generated from vendored Lexicon JSON by one generator. That replaces
  21k hand-written lines and a regex-based drift test. Users run the same generator on their own
  Lexicons. Unions become interfaces, known values open structs, and XRPC methods typed
  descriptors that take one parameters record.
- **A session is the authenticated transport.** `new AtProtoClient(session)`. Password, OAuth,
  service-auth and admin sessions are `IXrpcHandler`s that own their tokens and refresh.
  `AtProtoClient` is immutable and knows nothing about logins.
- **One way per mechanism.** One XRPC invoker, one untrusted-fetch layer, one MST, one signed-object
  helper, one stream runtime with in-band notices, one registration style, one exception
  hierarchy.
- **AOT-safe and observable.** Source-generated JSON, no process-global registries,
  `ActivitySource`/`Meter` per package.
- **Behaviour carried by tests, not code.** Vendored interop vectors and golden fixtures go into a
  conformance suite behind a protocol adapter. The adapter is proven green against 0.8 first,
  then re-implemented on the rewrite, phase by phase.
- **Twelve phases, bottom-up, each with a gate.** Foundations, primitives, repo, Lexicon,
  identity, client, OAuth, streaming, Lexicon packages, hosting, Spaces, release.

## Conventions

- Decisions are `D-n`, open questions `Q-n`, guidelines `AG-n`, requirements `{PREFIX}-n` (see
  [specs/README.md](specs/README.md#conventions)).
- IDs are stable: never renumbered, never reused.
- This folder is not compiled by the doc-snippet project (only `docs/*.md` and `README.md` are),
  so C# here is illustrative. Once a phase lands, `docs/` documents the real API and this folder
  keeps the reasoning.
