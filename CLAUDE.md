# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> **⚠ ALWAYS update `CHANGELOG.md` when committing.** Every commit that changes runtime behavior, public API, or build/CI surface MUST add a bullet under `## [Unreleased]` in the correct subsection (`Breaking changes` / `Added` / `Changed` / `Fixed` / `Removed` / `Security`, in that order). Trivial doc-only commits, comment cleanups, or whitespace-only changes are the only exceptions. Binary-incompatible API changes (signature changes, constructor parameter additions even when source-compatible) MUST go under `Breaking changes` with a one-line migration note. Update CHANGELOG in the SAME commit as the code change — don't batch it into a separate "update changelog" commit.
>
> **CHANGELOG style:** one bullet per logical change, at most ~300 characters: a **bold title** plus 1–3 sentences of user-visible effect, ending with the issue reference `(#N)`. No tables, no benchmark numbers, no implementation narration — that belongs in the PR description. A breaking change's bullet includes a one-line `Migration: …`, and the change also gets its entry in the release's migration guide (`docs/migrating-to-0.7.md` for 0.7).

## Build & Test

Target framework is `net10.0`. The repo uses a `.slnx` solution (`ATProto.NET.slnx`) — `dotnet build`/`test` will pick it up automatically from the repo root.

**Always pass `-p:EnableSourceControlManagerQueries=false`** to `dotnet build` and `dotnet test`. This is a workaround for a `.gitmodules` access error that surfaces in this environment.

```bash
# Build everything (the solution must build with 0 warnings)
dotnet build -p:EnableSourceControlManagerQueries=false

# Unit tests (no external deps; this is the canonical pre-merge check)
dotnet test tests/ATProtoNet.Tests/ -p:EnableSourceControlManagerQueries=false

# Single test class
dotnet test tests/ATProtoNet.Tests/ -p:EnableSourceControlManagerQueries=false \
  --filter "FullyQualifiedName~RecordCollectionTests"

# Just the documentation samples
dotnet build tests/ATProtoNet.DocSnippets/ -p:EnableSourceControlManagerQueries=false
```

**The docs are compiled.** `tests/ATProtoNet.DocSnippets` compiles every ` ```csharp ` block of `README.md` and `docs/*.md` through a source generator (`tests/ATProtoNet.DocSnippets.Generator`), and reports a sample that no longer matches the API as a build error at its Markdown line. When you change or remove an API, the build tells you which pages to fix. The conventions (the `SnippetContext` variables, `partial` / `before` / `continued` on the fence, `<!-- snippet: … -->` for a sample's assumed variables) are in [CONTRIBUTING.md](CONTRIBUTING.md#documentation).

Integration tests in `tests/ATProtoNet.IntegrationTests/` need live services and are gated by the enum-driven `[RequiresFact(IntegrationRequirement.X)]` (see `TestInfrastructure.cs`). Environment variables:

- `ATPROTO_PDS_URL` (default `http://localhost:2583`)
- `ATPROTO_PDS_ADMIN_PASSWORD` — the PDS's admin password. It is all the PDS tests need (`IntegrationRequirement.Pds`, `IntegrationRequirement.PdsAdmin`): `AuthenticatedClientFixture` provisions its own account through the admin API, and the admin tests theirs
- `ATPROTO_TEST_HANDLE`, `ATPROTO_TEST_PASSWORD` — optional overrides: an existing account for the PDS tests to use instead
- `ATPROTO_HAS_BLUESKY=true` for app-view tests (`IntegrationRequirement.Bluesky`)
- `ATPROTO_TEST_JETSTREAM=true` for the live Jetstream v2 protocol tests (`IntegrationRequirement.Jetstream`) — these need outbound internet but no PDS and no credentials; `ATPROTO_JETSTREAM_URL` overrides the host
- `ATPROTO_JETSTREAM_API_KEY` additionally enables the Jetstream v2 archive tests (`IntegrationRequirement.JetstreamArchive`); the replay HTTP endpoints are authenticated and metered in response bytes, so those tests stay deliberately small
- `ATPROTO_TEST_SPACES=true` for the permissioned-data tests (`IntegrationRequirement.Spaces`) — these need a PDS that serves `com.atproto.space.*`, which no release does yet, plus `ATPROTO_PDS_ADMIN_PASSWORD` (they provision their own accounts) and `ATPROTO_PLC_URL` (that network's own PLC directory); `ATPROTO_SPACES_PDS_URL` points them at another PDS than `ATPROTO_PDS_URL`. See `docs/testing-spaces.md`

Without these, the attribute sets `Skip` rather than failing. Setting `ATPROTO_REQUIRE_INTEGRATION=1` turns a missing prerequisite into a failure instead of a skip, so a CI job whose environment quietly stopped providing one gets caught rather than passing on skipped tests.

## CI and releases

- **`.forgejo/workflows/ci.yml`** runs on pushes to `main` and on pull requests, in `mcr.microsoft.com/dotnet/sdk:10.0`: it builds the whole solution in Release (the doc samples included), runs `tests/ATProtoNet.Tests`, generates the Aspire publish manifest of `samples/ManagedPdsSample.AppHost` and checks it (`AspireManifestTests`), and a `pds-integration` job runs the integration tests that need only a PDS (`PdsAdminTests|AuthenticationTests|RepositoryTests|CustomRecordTests|IdentityResolutionTests`) against the reference PDS as a service container, with `ATPROTO_REQUIRE_INTEGRATION=1`. It publishes nothing.
- **`.forgejo/workflows/release.yml`** publishes. On a `v*` tag (or a manual dispatch with a version) it builds and tests with that version, packs, pushes the `.nupkg`s and `.snupkg`s to the Forgejo NuGet feed and to nuget.org, and creates the Forgejo release from that version's `CHANGELOG.md` section. Rename `## [Unreleased]` to the version before tagging.
- **`.github/workflows/`** runs on the GitHub mirror: the same build and unit tests (`ci.yml`), and the issue, comment and PR sync into Forgejo. `.forgejo/workflows/sync-to-github.yml` mirrors closures back.

## Repository layout (forge & remotes)

The **canonical remote is Forgejo** at `git.grandiras.net` (origin), with GitHub as a push mirror for community issues/PRs. Two implications:

- The Forgejo CLI `fj` is the issue and PR tool — `fj issue view -R origin <N>`, `fj pr create`, `fj issue close -R origin <N> -w "..."`. There is also a `gh`-compatible mirror flow but Forgejo is authoritative.
- Work never goes straight to `main`: each issue gets a branch and a pull request, which is reviewed and merged once the review approves and CI is green. The whole procedure — branch, implement, CHANGELOG, commit (`closes #N`), push the branch, open the PR, wait for review and CI, squash-merge, delete the branch — is in `.github/skills/issue-workflow/SKILL.md`. Follow it when implementing tracker issues.

## Solution structure

Shared settings live in the root `Directory.Build.props`: `TargetFramework`, `Nullable`, `ImplicitUsings`, and the package metadata (`Version`, `Authors`, license, URLs, readme, SourceLink, deterministic build flags). Do not duplicate them in individual `.csproj` files; a project sets only `Description`, `PackageTags` and what else differs. `src/Directory.Build.props` turns on `GenerateDocumentationFile` with CS1591 as an error; `tests/Directory.Build.props` and `samples/Directory.Build.props` set `<IsPackable>false</IsPackable>` (tests also get the global `using Xunit;` and the `xUnit1051` suppression). Each imports the root file.

The five `src/` packages layer onto each other:

- **`ATProtoNet`** — core SDK, zero ASP.NET dependency (its only dependencies are `Microsoft.Extensions.Caching.Abstractions`, `Microsoft.Extensions.Logging.Abstractions` and `System.Formats.Cbor`). The `AtProtoClient` facade (one constructor: options, `HttpClient`, `IAtProtoSessionStore`, logger — all optional) composes per-Lexicon-domain sub-clients (`Server`, `Repo`, `Identity`, `Sync`, `Admin`, `Label`, `Moderation`, `Space`, `SimpleSpace`, `Temp`, `Lexicon`, `Bsky`, `Chat`, `Ozone`, `Site`) over one internal XRPC transport, exposed as `IXrpcTransport` (`client.Transport`) for sub-clients of other Lexicons. Custom records flow through `RecordCollection<T>` and `GetCollection<T>()`; custom XRPC through `QueryAsync` / `ProcedureAsync`. Identifiers are typed (`Did`, `Handle`, `AtIdentifier`, `AtUri`, `Nsid`, `Cid`, `RecordKey`, `Tid`, `AtDatetime` in `ATProtoNet.Identity`); sessions are `AtProtoSession` (`PasswordSession`, `OAuthSession`) persisted through `IAtProtoSessionStore`; errors derive from `AtProtoException` (`XrpcException` and its subtypes for XRPC). Also here: the OAuth client (`Auth/OAuth`), identity resolution with the SSRF fetch policy, streaming (`TypedFirehoseConsumer` with Sync 1.1 verification, Jetstream, label streams, `IStreamCursorStore`), the Tap client, labels, repository structures, and the Spaces client and syncer. `InternalsVisibleTo` is granted to `ATProtoNet.Tests`.
- **`ATProtoNet.Server`** — ASP.NET Core integration, depending on nothing beyond the ASP.NET Core shared framework. `AddAtProto()` registers the `AtProtoClient` and returns an `IAtProtoBuilder` (`Extensions/`) the rest hangs off: `WithLifetime`, `WithOAuth` (the OAuth cookie login in `Authentication/`, namespace `ATProtoNet.Server.Authentication`, mapped with `MapAtProtoOAuth()`), `WithClientFactory` (`IAtProtoClientFactory` in `Services/` plus the refresh coordinator), the session store (`WithInMemorySessionStore` / `WithFileSessionStore` / `WithSessionStore<T>`; `FileAtProtoSessionStore` in `TokenStore/`), and `WithHealthCheck`. Calls from other services are authenticated with service auth (`AddAuthentication().AddAtProtoServiceAuth(…)`, `[RequireServiceAuth]`, `IJtiReplayStore`); there is no bearer-JWT handler. Also: the server-side XRPC handler routing (`Xrpc/`, `AddXrpcEndpoint<T>`, `MapXrpcEndpoints()`), the label endpoint (`Labeling/`), the Tap webhook (`Tap/`), the space server (`Spaces/`, `AddAtProtoSpaces`), `AddAtProtoIdentity`, `AddAtProtoPdsAdmin`, and the .NET Aspire client integration in `Aspire/` (`builder.AddAtProtoClient()`, health checks — namespace `ATProtoNet.Aspire`; no resilience handler).
- **`ATProtoNet.Server.EntityFrameworkCore`** — the EF Core stores, namespace `ATProtoNet.Server.EntityFrameworkCore`: the encrypted session store (`WithEfCoreSessionStore<T>()`, `AtProtoTokenDbContext`), the replay store (`AddAtProtoEfCoreJtiReplayStore<T>()`, `JtiReplayDbContext`), the space authority and `simplespace` stores (`SpaceDbContext`), and Sync 1.1 repository state (`RepoSyncStateDbContext`). Test them on SQLite in memory: the stores use `ExecuteUpdate`/`ExecuteDelete`, which the EF Core in-memory provider does not translate.
- **`ATProtoNet.Blazor`** — Blazor components (`LoginForm`, and `FeedView`, `PostCard`, `ProfileCard`, `ComposePost`, which act as the signed-in user through `AtProtoUserClientAccessor`, registered by `AddAtProtoBlazor()`). The login itself is `ATProtoNet.Server`'s.
- **`ATProtoNet.Aspire.Hosting`** — Aspire `AppHost`-side resources for a PDS container: the official Bluesky PDS (`AddAtProtoPds`, `WithAtProtoPds`) or Tranquil (`AddAtProtoTranquilPds`, `WithAtProtoTranquilPds`), both on `AtProtoPdsContainerResourceBase`. This repo does **not** implement a PDS; `PdsAdminClient` in the core package administers the container, and `AddAtProtoPdsAdmin()` in `ATProtoNet.Server` binds it from Aspire-supplied configuration.

Tool: **`tools/ATProtoNet.LexiconGenerator`** is a `dotnet tool` (binary `atproto-lexgen`) for bidirectional Lexicon JSON ↔ C# generation (`csharp`, `lexicon`), and for `lint`, `diff`, `publish` (schemas as `com.atproto.lexicon.schema` records on a PDS) and `resolve` (from the network, verified).

Tests: `tests/ATProtoNet.Tests` (unit), `tests/ATProtoNet.IntegrationTests` (gated, above), and `tests/ATProtoNet.DocSnippets` with its generator (the compiled documentation). Samples live in `samples/`.

## Lexicon layout convention

Each AT Protocol namespace under `src/ATProtoNet/Lexicon/` follows a strict path/file pattern:

```
Lexicon/<TopDomain>/<SubDomain>/<Service>/
    <Service>Client.cs    # methods bound to the XRPC client
    <Service>Models.cs    # DTOs / records
```

Examples: `Lexicon/Com/AtProto/Repo/{RepoClient.cs,RepoModels.cs}`, `Lexicon/App/Bsky/Feed/{FeedClient.cs,FeedModels.cs}`. Five top-level domains exist: `Com/AtProto/*`, `App/Bsky/*`, `Chat/Bsky/*`, `Site/Standard/*`, `Tools/Ozone/*`. New Lexicons should follow this two-file pattern and be registered as a property on `AtProtoClient`. The rules for their signatures (typed identifiers, parameter order, `ICursorPage<T>`, `Enumerate*`) are in [CONTRIBUTING.md](CONTRIBUTING.md#lexicon-models-and-clients).

JSON property names use `camelCase` (AT Proto convention) — set `[JsonPropertyName("...")]` explicitly rather than relying on a global naming policy.

## Conventions worth knowing

- **Nullable reference types are enabled everywhere**; `<TreatWarningsAsErrors>` stays at its default of `false`, but the solution builds with 0 warnings and new warnings on touched files should be addressed.
- **Public APIs are XML-documented**; `GenerateDocumentationFile` is on for every `src/` project, and a missing XML comment (CS1591) is a build error.
- **Docs describe the code as it is.** A change to a public API updates the pages that name it (the doc-snippet build finds the samples; grep finds the prose), and `docs/index.md` lists every page.
- Tests use **xUnit v3 + NSubstitute**. Naming: `MethodName_Scenario_ExpectedResult`.
- Commit messages follow **conventional commits** (`feat:`, `fix:`, `docs:`, `test:`), ending with `(closes #N)` for tracker issues.
- Indent: 4 spaces for C#, 2 for csproj/props/yml/json (`.editorconfig`).
