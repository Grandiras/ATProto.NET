# Contributing to ATProto.NET

This is the `next` branch, where the 0.9 rewrite is built. The design it implements is in
[`design/`](design/README.md); start there. 0.8 fixes go to `main`.

Everything happens on [Forgejo](https://git.grandiras.net/Grandiras/ATProto.NET): the tracker,
pull requests, CI and releases. There is no mirror.

## Getting started

- The .NET SDK that [`global.json`](global.json) pins (.NET 11; a release candidate until the
  November 2026 GA).
- Python 3, for `scripts/vendor-lexicons.py` and the `lexicons` job.
- [Podman](https://podman.io/) or Docker for integration tests, once they exist (phase 5).

CI is one script, so every check runs the same way locally:

```bash
./build.sh build      # restore and build everything in Release, warnings are errors
./build.sh test       # unit, conformance and architecture tests (after build)
./build.sh lexicons   # lexicons/ matches lexicons.json
./build.sh aot        # publish tests/AotSmoke with NativeAOT
```

`build.sh` lists the other jobs (`integration-pds`, `integration-spaces`, `api`, `aspire`,
`changelog`, `nightly`, `release`); `.forgejo/workflows/` only calls them. For a quick loop,
`dotnet build` and `dotnet test <project>` work as usual.

## Workflow

Each work item is a Forgejo issue with a branch and a pull request into `next`; each phase has a
tracking issue ([design/05-rewrite-plan.md](design/05-rewrite-plan.md#issues-and-prs)). The
procedure, from branch to squash-merge, is the issue-workflow skill in
[`.claude/skills/issue-workflow/SKILL.md`](.claude/skills/issue-workflow/SKILL.md).

- A PR is one component or one spec section, ideally under ~1,500 changed lines excluding
  generated code and vectors, and lists the requirement IDs it implements.
- A spec change goes in the same PR as the code that needs it, with its reason. Requirement IDs
  are never renumbered.
- A PR is merged once CI is green and the review of its current head approves.

### Commits

Conventional commits (`feat:`, `fix:`, `docs:`, `test:`, `chore:`), ending with the issue
reference: `(#N)`, or `(closes #N)` for the commit that finishes it.

### Changelog

<a id="changelog"></a>

This is the one statement of the changelog rules; `CLAUDE.md` and the skill link here.

- A change that users will notice adds a bullet under `## [Unreleased]` in `CHANGELOG.md`, in
  the same commit, under `Breaking changes` / `Added` / `Changed` / `Fixed` / `Removed` /
  `Security`, in that order. During the rewrite it records the decisions per phase, not every
  commit. Doc-only, comment and test-only changes need none; label such a PR `no-changelog`
  (the `changelog` job checks the rest).
- One bullet per logical change, at most ~300 characters: a **bold title** and 1–3 sentences of
  user-visible effect, ending with `(#N)`. No tables, numbers or implementation narration; those
  belong in the PR description.
- A breaking change against 0.8 ends with a one-line `Migration: …`. The 0.9.0 release notes are
  the migration guide (`docs/migrating-to-0.9.md`, written in phase 11).

## Code

The rules every public API follows are [design/03-api-guidelines.md](design/03-api-guidelines.md);
the layering and shape rules (no file over ~400 lines, no `InternalsVisibleTo` to non-test
assemblies, no mutable statics, dependencies only point down) are checked by
`tests/ATProtoNet.Architecture.Tests`.

- Settings live in the root `Directory.Build.props` (target framework, nullable, warnings as
  errors) and `src/Directory.Build.props` (XML docs, AOT and trimming, public API analyzers).
  Package versions live only in `Directory.Packages.props`. A project sets only what differs.
- Every public member has XML docs (CS1591 fails the build) and appears in its project's
  `PublicAPI.Unshipped.txt` (RS0016/RS0017).
- Library code awaits with `ConfigureAwait(false)` (CA2007 is an error under `src/`).
- Style: 4-space indent for C#, 2 for project files, YAML and JSON (`.editorconfig`). No braces
  where a single statement does, guard clauses over nesting, small files. Comments explain why,
  not what.

## Tests

[design/04-testing.md](design/04-testing.md) has the test architecture. In short:

- One unit-test project per package, folders mirroring its namespace areas. Test through the
  public API; `InternalsVisibleTo` only from a package to its own test project.
- Names are `Method_Scenario_Expected`. A test that pins a spec requirement carries
  `[Trait("Spec", "SYN-12")]`.
- Pass `TestContext.Current.CancellationToken` (xUnit1051 is on). No real network, sleeps,
  `Stopwatch` or wall clock in unit tests: use `FakeTimeProvider` and the TestKit fakes
  (`FakeXrpcServer`, `FakeIdentityNetwork`, `TestKeys`, `TestIds`). The architecture tests check
  this.
- Integration tests use `[RequiresFact(Requirement.X)]` from `ATProtoNet.TestKit.Integration`:
  skipped when the environment lacks what they need, failing instead under
  `ATPROTO_REQUIRE_INTEGRATION=1`, and tagged `Requirement=X` for CI's filters.
- Every regression test names the bug it guards, with its issue number.

### The conformance suite

`tests/ATProtoNet.Conformance` runs protocol vectors against an `IProtocolAdapter`, without
referencing any SDK. `Adapter.Legacy` implements it over the 0.8 packages from nuget.org and must
stay green; `Adapter.Next` implements it over the rewrite as each phase lands. Vectors are data
files under `vectors/`, each listed with its provenance in `vectors/MANIFEST.md`; regenerate them,
never edit them. A vector an implementation fails is listed in its adapter's `KnownFailures`
with the issue filed for it.

## Lexicons

`lexicons/` holds the vendored Lexicon JSON, `lexicons.json` its sources, pins and per-NSID CIDs.
Never edit them by hand: `scripts/vendor-lexicons.py --fetch [--atproto <commit>] [--spaces
<commit>]` re-copies every source at its pin, and without `--fetch` it only recomputes the CIDs.
`atproto-lexgen install|update` replaces the script in phase 3.

## Documentation

`docs/` (listed in [`docs/index.md`](docs/index.md)) and `README.md`. Every ` ```csharp ` block is
compiled: `tests/ATProtoNet.DocSnippets` turns each into code through a source generator, with
`#line` directives pointing back at the Markdown, so a sample that no longer matches the API
fails the build at its own line.

- Type declarations go into a namespace shared by the page's blocks; statements into a method;
  members with an access modifier into a class of their own. A `using` in any block applies to
  the whole page.
- A block may use the variables `tests/ATProtoNet.DocSnippets/SnippetContext.cs` declares
  (`builder`, `services`, `app`, `configuration`, `logger`, `httpClient`, `args`, `ct`,
  `cancellationToken`). Anything else it declares in an HTML comment on the line before the
  fence: `<!-- snippet: OAuthClient oauthClient; string code, state; -->`.
- Words after `csharp` on the fence: `continued` (same method as the block before), `partial`
  (a fragment, not compiled), `before` (old API in a migration guide, not compiled).

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
