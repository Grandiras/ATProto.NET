# CLAUDE.md

Guidance for Claude Code (claude.ai/code) in this repository.

This is the **`next` branch: the 0.9 rewrite**, built in phases from the accepted design in
`design/` (start at `design/README.md`; the phases are `design/05-rewrite-plan.md`). The 0.8
code is on `main`, as reference material only: the specs, `design/03-api-guidelines.md` and the
decisions are the instructions. When a phase settles a spec's open point or deviates from it,
record the outcome in that spec in the same PR.

> **Changelog:** a user-visible change adds a bullet under `## [Unreleased]` in `CHANGELOG.md`, in
> the same commit. The rules (sections, style, migration notes) are in
> [CONTRIBUTING.md](CONTRIBUTING.md#changelog), the only place they are written down.

## Build and test

The SDK is pinned by `global.json` (.NET 11). CI is `build.sh`; the workflows only call it.

```bash
./build.sh build                 # Release build, warnings are errors, docs samples included
./build.sh test                  # unit, conformance and architecture tests
dotnet test tests/ATProtoNet.Conformance/Adapter.Legacy   # the conformance suite against 0.8
dotnet test tests/ATProtoNet.Tests --filter "FullyQualifiedName~FakeXrpcServerTests"
```

Other jobs: `lexicons`, `aot`, `api`, `integration-pds`, `integration-spaces`, `aspire`,
`changelog`, `nightly`, `release <version>`.

## Layout

- `src/ATProtoNet/`: the base package, namespace layers 0–4 enforced by
  `tests/ATProtoNet.Architecture.Tests` (`Layers.cs` classifies every namespace; a new one must be
  added there). The extension packages (`ATProtoNet.AspNetCore`, `ATProtoNet.EntityFrameworkCore`,
  `ATProtoNet.Aspire.Hosting`) and the tools appear in the phases that build them.
- `tests/ATProtoNet.Conformance/`: protocol vectors (`vectors/`, provenance in `MANIFEST.md`)
  behind `IProtocolAdapter`; `Adapter.Legacy` runs them against the 0.8 NuGet packages and must
  stay green, with known 0.8 failures listed in `LegacyKnownFailures.cs` by issue.
- `tests/ATProtoNet.TestKit/`: the shared fakes (`FakeXrpcServer`, `FakeIdentityNetwork`,
  `TestKeys`, `TestIds`, `FakeTimeProvider`) and the integration gate
  (`[RequiresFact(Requirement.X)]`, trait `Requirement=X`).
- `tests/ATProtoNet.Benchmarks/`: BenchmarkDotNet scenarios; `baseline/` holds the 0.8 results each
  phase gate compares against.
- `lexicons/` + `lexicons.json`: vendored Lexicons; `scripts/vendor-lexicons.py` maintains them.
- `tests/ATProtoNet.DocSnippets`: compiles every C# block of `README.md` and `docs/*.md`.

## Conventions

- Rules for code, tests and docs are in [CONTRIBUTING.md](CONTRIBUTING.md); public API rules in
  `design/03-api-guidelines.md`. Test names `Method_Scenario_Expected`, `[Trait("Spec", "ID")]` on
  spec-backed tests, no sleeps or wall clock in unit tests.
- Forgejo (`git.grandiras.net`, remote `origin`) is the only forge; `fj` is its CLI. Each work item
  is an issue, a branch and a PR into `next`; the procedure is
  [.claude/skills/issue-workflow/SKILL.md](.claude/skills/issue-workflow/SKILL.md).
