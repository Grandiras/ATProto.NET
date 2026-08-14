# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> **⚠ ALWAYS update `CHANGELOG.md` when committing.** Every commit that changes runtime behavior, public API, or build/CI surface MUST add a bullet under `## [Unreleased]` in the correct subsection (`Added` / `Changed` / `Fixed` / `Breaking changes` / `Removed` / `Security`). Trivial doc-only commits, comment cleanups, or whitespace-only changes are the only exceptions. Binary-incompatible API changes (signature changes, constructor parameter additions even when source-compatible) MUST go under `Breaking changes` with a one-line migration note. Update CHANGELOG in the SAME commit as the code change — don't batch it into a separate "update changelog" commit.

## Build & Test

Target framework is `net10.0`. The repo uses a `.slnx` solution (`ATProto.NET.slnx`) — `dotnet build`/`test` will pick it up automatically from the repo root.

**Always pass `-p:EnableSourceControlManagerQueries=false`** to `dotnet build` and `dotnet test`. This is a workaround for a `.gitmodules` access error that surfaces in this environment.

```bash
# Build everything
dotnet build -p:EnableSourceControlManagerQueries=false

# Unit tests (no external deps; this is the canonical pre-merge check)
dotnet test tests/ATProtoNet.Tests/ -p:EnableSourceControlManagerQueries=false

# Single test class
dotnet test tests/ATProtoNet.Tests/ -p:EnableSourceControlManagerQueries=false \
  --filter "FullyQualifiedName~RecordCollectionTests"
```

Integration tests in `tests/ATProtoNet.IntegrationTests/` need a live PDS and are gated by `[RequiresPdsFactAttribute]` / `[RequiresBlueskyFactAttribute]` (see `TestInfrastructure.cs`). Required env vars:

- `ATPROTO_PDS_URL` (default `http://localhost:2583`)
- `ATPROTO_TEST_HANDLE`, `ATPROTO_TEST_PASSWORD`
- `ATPROTO_HAS_BLUESKY=true` for app-view tests
- `ATPROTO_TEST_JETSTREAM=true` for the live Jetstream v2 protocol tests (`[RequiresJetstreamFact]`) — these need outbound internet but no PDS and no credentials; `ATPROTO_JETSTREAM_URL` overrides the host

Without these, the attribute sets `Skip` rather than failing — CI runs unit tests only.

CI (`.forgejo/workflows/ci.yml`) builds on `mcr.microsoft.com/dotnet/sdk:10.0` and runs `dotnet test tests/ATProtoNet.Tests/ --configuration Release`. The `package` job pushes `dotnet pack` output to a Forgejo NuGet feed on `main`.

## Repository layout (forge & remotes)

The **canonical remote is Forgejo** at `git.grandiras.net` (origin), with GitHub as a push mirror for community issues/PRs. Two implications:

- The Forgejo CLI `fj` is the issue tool — `fj issue view -R origin <N>`, `fj issue close -R origin <N> -w "..."`. There is also a `gh`-compatible mirror flow but Forgejo is authoritative.
- The repo follows a defined issue → implement → CHANGELOG → commit → push → close workflow documented in `.github/skills/issue-workflow/SKILL.md`. When implementing tracker issues, follow that procedure (one commit per issue with `closes #N`, update `CHANGELOG.md` under `[Unreleased]`).

## Solution structure

Shared package metadata (`Version`, `Authors`, SourceLink, deterministic build flags) lives in `Directory.Build.props` — do not duplicate it in individual `.csproj` files. Set `<IsPackable>false</IsPackable>` on non-packable projects (tests, samples).

The four `src/` projects layer onto each other:

- **`ATProtoNet`** — core SDK, zero ASP.NET dependency. The `AtProtoClient` facade composes per-Lexicon-domain sub-clients (`Server`, `Repo`, `Identity`, `Sync`, `Admin`, `Label`, `Moderation`, `Bsky`, `Chat`, `Ozone`, `Site`), each wrapping the shared `XrpcClient`. Custom records flow through `RecordCollection<T>` and `GetCollection<T>(nsid)`; custom XRPC through `QueryAsync<T>` / `ProcedureAsync<T>`. `InternalsVisibleTo` is granted to `ATProtoNet.Tests`.
- **`ATProtoNet.Server`** — ASP.NET Core integration: DI extensions (`AddAtProto`, `AddAtProtoServer`), JWT auth handler, `IAtProtoClientFactory`, `IAtProtoTokenStore` (in-memory + file implementations in `TokenStore/`, plus the EF Core implementation in `TokenStore/EntityFrameworkCore/` — namespace `ATProtoNet.Server.EntityFrameworkCore`), the server-side XRPC handler routing in `Xrpc/`, and .NET Aspire client integration in `Aspire/` (`AddAtProtoClient`, health checks, resilience — namespace `ATProtoNet.Aspire`). The EF Core token store and Aspire client were merged in from separate packages; both keep their original namespaces.
- **`ATProtoNet.Blazor`** — Blazor components (`LoginForm`, etc.) and the OAuth login endpoints registered by `MapAtProtoOAuth()`.
- **`ATProtoNet.Aspire.Hosting`** — Aspire `AppHost`-side resource for the official Bluesky PDS container (`AddAtProtoPds`, `WithAtProtoPds`). This repo does **not** implement a PDS; `PdsAdminClient` in the core package administers the container over HTTP Basic admin auth, and `AddAtProtoPdsAdmin()` in `ATProtoNet.Server` binds it from Aspire-supplied configuration.

Tool: **`tools/ATProtoNet.LexiconGenerator`** is a `dotnet tool` (binary `atproto-lexgen`) for bidirectional Lexicon JSON ↔ C# generation, schema diffing, and migrations.

## Lexicon layout convention

Each AT Protocol namespace under `src/ATProtoNet/Lexicon/` follows a strict path/file pattern:

```
Lexicon/<TopDomain>/<SubDomain>/<Service>/
    <Service>Client.cs    # methods bound to the XRPC client
    <Service>Models.cs    # DTOs / records
```

Examples: `Lexicon/Com/AtProto/Repo/{RepoClient.cs,RepoModels.cs}`, `Lexicon/App/Bsky/Feed/{FeedClient.cs,FeedModels.cs}`. Five top-level domains exist: `Com/AtProto/*`, `App/Bsky/*`, `Chat/Bsky/*`, `Site/Standard/*`, `Tools/Ozone/*`. New Lexicons should follow this two-file pattern and be registered as a property on `AtProtoClient`.

JSON property names use `camelCase` (AT Proto convention) — set `[JsonPropertyName("...")]` explicitly rather than relying on a global naming policy.

## Conventions worth knowing

- **Nullable reference types are enabled everywhere**; `<TreatWarningsAsErrors>` is intentionally `false` on the core project, but new warnings on touched files should be addressed.
- **Public APIs are XML-documented**; `GenerateDocumentationFile` is on for the core project.
- Tests use **xUnit + NSubstitute**. Naming: `MethodName_Scenario_ExpectedResult`.
- Commit messages follow **conventional commits** (`feat:`, `fix:`, `docs:`, `test:`).
- Indent: 4 spaces for C#, 2 for csproj/props/yml/json (`.editorconfig`).
