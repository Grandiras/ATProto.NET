# Tooling: `atproto-lexgen`

**Status:** Accepted (maintainer, 2026-10-17) · **Package:** `atproto-lexgen` `dotnet tool` (`tools/ATProtoNet.Lexicon.Cli`), the
one SDK package that is not a library. It depends on the internal `ATProtoNet.Lexicon.CodeGen`
project (the same emitter the analyzer inside `ATProtoNet` uses), the `ATProtoNet` package
(schema model in the `ATProtoNet.Lexicon` namespace; network commands) and `System.CommandLine` · **Phase:** 3 (`generate`, `lint`, `install`/`update` from git and
directory sources), 8 (`install`/`update` from the network, needed for `site.standard.*`), 11
(`diff`, `publish`, `resolve`) · **Normative references:**
[Lexicon](https://atproto.com/specs/lexicon),
[Lexicon versioning](https://atproto.com/guides/lexicon#versioning),
[Publishing Lexicons](https://atproto.com/guides/publishing-lexicons),
[Installing Lexicons](https://atproto.com/guides/installing-lexicons)

## Scope

The command-line tool for working with Lexicon schemas: vendoring them (`install`, `update`),
checking them (`lint`, `diff`), turning them into C# (`generate`), and publishing and resolving
them on the network (`publish`, `resolve`). Every rule the tool applies lives in a library
([lexicon.md](lexicon.md)): the CLI parses arguments, calls the library and formats results.
It owns no schema model, lint rule, compatibility rule or emitter of its own.

Out of scope: the rules themselves (LEX-10–LEX-17), the emitter (LEX-25 onward), Lexicon
resolution (LEX-81).

## Public API sketch

```text
atproto-lexgen generate [--manifest lexicons.json] [--lexicons <dir>] [--config lexgen.json]
                        [--out <dir>] [--reference <assembly.dll>]... [--check] [--clean]
atproto-lexgen install  <nsid|pattern>... [--source <name>] [--manifest lexicons.json]
atproto-lexgen update   [<nsid|pattern>...] [--commit <sha>] [--dry-run] [--manifest lexicons.json]
atproto-lexgen lint     [<file|dir>...] [--strict] [--format text|json]
atproto-lexgen diff     --baseline <dir|network> [--current <dir>] [--strict] [--format text|json]
atproto-lexgen publish  [<file|dir>...] [--identifier <handle|did>] [--pds <url>] [--force] [--dry-run]
atproto-lexgen resolve  <nsid>... [--did <did>] [--out <dir>] [--format text|json]
atproto-lexgen --version | --help | <command> --help
```

```csharp
// The tool is a thin host over a testable application object.
namespace ATProtoNet.Lexicon.Cli;
public sealed class LexgenApp
{
    public LexgenApp(LexgenEnvironment environment);   // console, file system, env vars, HttpClient, TimeProvider
    public Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default);
}
```

## Requirements

### Common behaviour

- **TOO-1** (MUST) The CLI is built on `System.CommandLine`. Every command has `--help`; the root
  prints the command list with no arguments or with `--help`.
  *Source:* Decision · 0.8 `Program.cs` (965-line hand-rolled parser) ·
  `Main_NoCommandOrHelp_PrintsTheCommands`, `Main_CommandHelp_PrintsTheCommandsUsage`
- **TOO-2** (MUST) `--version` prints the package's informational version.
  *Source:* 0.8 #110 · `Main_VersionFlag_PrintsPackageVersion`
- **TOO-3** (MUST) Exit codes are fixed for every command:

  | Code | Meaning |
  |---|---|
  | 0 | success (including warnings without `--strict`) |
  | 1 | findings: lint errors, breaking changes under `--strict`, `generate --check` would change files, `publish` refused a breaking change |
  | 2 | usage error: unknown command or option, missing required option, invalid value |
  | 3 | input error: missing directory, unreadable or malformed file, manifest/CID mismatch, no Lexicons in the input |
  | 4 | network or remote failure: resolution failed, sign-in refused, a write rejected |

  *Source:* Decision (0.8 used 0 and 1 only) · `Main_UnknownCommand_Fails`,
  `Main_UnknownOption_Fails`, `Csharp_RequiredOptionMissing_Fails`
- **TOO-4** (MUST) Results go to stdout, diagnostics and progress to stderr. Commands that report
  findings (`lint`, `diff`, `resolve`) accept `--format json`, a stable machine-readable schema
  documented in the tool's README. Colour is used only on an interactive terminal and never when
  `NO_COLOR` is set. Output order is deterministic (sorted by NSID, then JSON pointer).
  *Source:* Decision
- **TOO-5** (MUST) Every diagnostic names its file and JSON pointer, and lint and generator
  diagnostics carry their rule ID (`LXL0xx`, `ATLEX0xx`).
  *Source:* LEX-10, LEX-80
- **TOO-6** (MUST) Commands are implemented against `LexgenEnvironment` (console, file system,
  environment variables, `HttpClient`, `TimeProvider`), so tests run them in-process with fakes
  and no real network.
  *Source:* [04 — Testing](../04-testing.md#rules) · 0.8 `LexiconGeneratorProgramTests` (injectable
  `GetEnvironmentVariable`)

### `generate` (phase 3)

- **TOO-7** (MUST) `generate` runs the shared emitter (LEX-25) over the manifest's Lexicons (or
  every `*.json` under `--lexicons` when no manifest is given) with the options in `--config`,
  resolving external refs from `--reference` assemblies through their metadata, without loading
  them (LEX-8). Its output is byte-identical to the source generator's for the same inputs.
  *Source:* LEX-8, LEX-25
- **TOO-8** (MUST) Output is one file per Lexicon document, plus one file each for the assembly
  manifest (`{Name}Lexicons`, attributes), the `JsonSerializerContext` and the namespace clients.
  Generated files carry the header of LEX-29 and nothing time-dependent. The emitted code is
  C# 15 and needs `net11.0` (LEX-83); the tool itself also targets `net11.0`.
  *Source:* 0.8 `Csharp_Schemas_WritesOneFilePerDocument` · LEX-29, LEX-83
- **TOO-9** (MUST) Before generating, the tool verifies each vendored file against its manifest
  CID (LEX-23) and fails with exit 3 on a mismatch, naming the file.
  *Source:* LEX-23 · 0.8 `Upstream/README.md` ("Do not edit the files by hand")
- **TOO-10** (MUST) A malformed Lexicon in the input set fails generation (exit 3). JSON files that
  are not Lexicon documents are skipped with a warning only in directory mode; in manifest mode
  every listed file must parse.
  *Source:* Decision · 0.8 `Csharp_MalformedAndForeignFiles_AreReportedAndSkipped` (skipping
  malformed Lexicons is dropped: it produced a partial API silently)
- **TOO-11** (MUST) `--check` writes nothing and exits 1 if any file would be created, changed or
  removed, listing them. CI uses it to keep checked-in code current (LEX-28).
  *Source:* LEX-28
- **TOO-12** (MUST) `--clean` removes files in `--out` that carry the generated header but are no
  longer produced. It never deletes a file without the header.
  *Source:* Decision
- **TOO-13** (MUST) A missing input directory or an input with no Lexicons is exit 3.
  *Source:* `Csharp_InputDirectoryMissing_Fails`, `Csharp_NoLexiconsInInput_Fails`

### `install` and `update` (phases 3 and 8)

- **TOO-14** (MUST) `install` adds Lexicons to `lexicons/` and `lexicons.json`. Sources are
  declared in the manifest (LEX-22): `git` (repository URL, commit, path; patterns such as
  `app.bsky.*` allowed), `directory` (a local path, for proposals), and `network` (phase 8;
  exact NSIDs, or an authority prefix listed through the authority repository's
  `com.atproto.lexicon.schema` records).
  *Source:* LEX-22 · 0.8 `Upstream/refresh.sh` (git archive for atproto, record listing for
  `standard.site`)
- **TOO-15** (MUST) Network installs resolve through the verified `LexiconResolver` (LEX-81):
  `_lexicon` DNS TXT, DID document, `com.atproto.sync.getRecord` with proof checked against the
  signing key. The stored file is the record value without its `$type`, with `lexicon` and `id`
  first; the manifest stores the record CID.
  *Source:* LEX-81 · `refresh.sh` (record value without `$type`, `lexicon` and `id` first)
- **TOO-16** (MUST) Git installs copy files byte for byte from the archive at the pinned commit,
  for the requested namespaces only.
  *Source:* `refresh.sh` · 0.8 `Upstream/README.md` ("copied unchanged")
- **TOO-17** (MUST) `install` also installs the transitive refs of what it installs, except NSIDs
  provided by `--reference` assemblies or matched by the manifest's `provided` patterns, so a
  user project vendors its own schemas and none of the SDK's.
  *Source:* Decision · LEX-79
- **TOO-18** (MUST) `update` re-resolves each source (the latest record CID for network sources;
  the commit given by `--commit` for git sources), rewrites changed files and manifest entries,
  and prints a per-NSID summary classified by the compatibility rules (LEX-14–LEX-16): added,
  removed, compatible, breaking. `--dry-run` prints the summary and writes nothing. Provenance
  (commit, commit date, fetch date) is recorded per source only when content changes, so a no-op
  update leaves the manifest untouched.
  *Source:* `refresh.sh` (printed provenance by hand for the README) · Decision

### `lint` (phase 3)

- **TOO-19** (MUST) `lint` runs `LexiconLint` (LEX-10–LEX-13) over the given files or directories
  (default: the manifest's Lexicons). Errors exit 1; warnings exit 0 unless `--strict`; an
  unreadable file exits 3.
  *Source:* `Lint_CleanSchemas_Succeeds`, `Lint_PermissionSetErrors_FailsAndNamesThem`,
  `Lint_WarningsOnly_SucceedUnlessStrict`, `Lint_UnreadableFile_Fails`

### `diff` (phase 11)

- **TOO-20** (MUST) `diff` compares a baseline (a directory, or `network` to resolve each current
  NSID from the network) with the current set (default: the manifest's Lexicons) using
  `LexiconCompatibility` (LEX-14–LEX-17). The report lists every change with its path, kind and
  classification, and the suggested `revision` bumps (LEX-17). Breaking changes exit 1 under
  `--strict`, 0 otherwise. No current schemas is exit 3.
  *Source:* `Diff_OptionalPropertyAdded_SucceedsAndSuggestsARevision`,
  `Diff_BreakingChangeUnderStrict_Fails`, `Diff_NoCurrentSchemas_Fails`
- **TOO-21** (MUST) Space defs follow LEX-16: an added collection is reported as compatible (it
  widens grants), a removed one as breaking.
  *Source:* 0.8 #92

### `publish` (phase 11)

- **TOO-22** (MUST) `publish` lints first and refuses on lint errors before signing in.
  *Source:* `Publish_LintErrors_RefusesBeforeSigningIn`
- **TOO-23** (MUST) The account comes from `--identifier` or `ATPROTO_IDENTIFIER`, the PDS from
  `--pds`, `ATPROTO_PDS_URL`, or the identifier's DID document. The password comes only from
  `ATPROTO_PASSWORD` or an interactive prompt that does not echo. A `--password` argument is
  refused without echoing its value. No identifier, or no password with no terminal to prompt
  on, is exit 2.
  *Source:* `Publish_PasswordAsAnArgument_IsRefusedWithoutEchoingIt`,
  `Publish_NoPasswordInTheEnvironment_PromptsForOne`, `Publish_NoPasswordAtAll_Fails`,
  `Publish_NoIdentifier_Fails`, `Publish_IdentifierAndPdsOptions_OverrideTheEnvironment`,
  `Publish_PdsFromTheEnvironment_IsUsed`
- **TOO-24** (MUST) The tool signs in only over HTTPS, or plain HTTP to a loopback host, and
  checks this before the password is sent.
  *Source:* `Publish_PlainHttpPdsOffThisMachine_IsRefusedBeforeThePasswordIsSent`,
  `MaySignInAt_OnlyHttpsOrLoopbackHttp`
- **TOO-25** (MUST) For each schema the tool reads the currently published
  `com.atproto.lexicon.schema/{nsid}` record: an identical schema is left alone; a new one is
  created; a compatible change replaces the record with `swapRecord` set to the CID it read; a
  record there that is not a Lexicon is replaced.
  *Source:* `Publish_NewSchemas_WritesThemAndChecksDns`, `Publish_SchemaAlreadyPublished_IsLeftAlone`,
  `Publish_CompatibleChange_ReplacesTheRecordItRead`, `Publish_PublishedRecordIsNoLexicon_IsReplaced`
- **TOO-26** (MUST) If any schema has a breaking change, nothing at all is written and the tool
  exits 1, unless `--force`.
  *Source:* `Publish_BreakingChange_WritesNothingAtAll`, `Publish_BreakingChangeForced_IsWritten`
- **TOO-27** (SHOULD) All writes go in one `com.atproto.repo.applyWrites` call (batched by the
  PDS's write limit when larger), so a failure part-way does not leave a mix of old and new
  schemas.
  *Source:* Decision (0.8 wrote records one at a time)
- **TOO-28** (MUST) After writing, the tool checks each authority's `_lexicon` TXT record. When it
  is missing or names another DID, the tool prints the exact record to create
  (`_lexicon.{authority} TXT "did={did}"`); when the lookup fails, it says it could not check.
  `--dry-run` prints the plan (creates, replacements, skips, DNS records) after reading, without
  signing in.
  *Source:* `Publish_DnsRecordMissingOrElsewhere_PrintsTheRecordToCreate`,
  `Publish_DnsLookupFails_SaysItCouldNotCheck`
- **TOO-29** (MUST) A rejected sign-in or write is exit 4 with the XRPC error name and message, and
  never the password.
  *Source:* `Publish_SignInRefused_ReportsIt` · [AG-29](../03-api-guidelines.md#security)

### `resolve` (phase 11)

- **TOO-30** (MUST) `resolve` resolves NSIDs with the verified `LexiconResolver` and prints each
  schema document (the record value without `$type`, localized text as written) to stdout, or
  writes one file per schema under `--out` in the `lexicons/` layout. `--did` reads from that
  repository instead of looking up `_lexicon`. An unresolvable NSID is reported with the reason
  and exits 4.
  *Source:* `Resolve_PublishedSchema_PrintsTheLexiconFile`, `Resolve_OutputDirectory_WritesEachSchema`,
  `Resolve_WithDid_ReadsFromThatRepository`, `Resolve_Unresolvable_ReportsItAndFails`,
  `ToDocumentJson_SchemaRecord_DropsTheRecordType`, `ToDocumentJson_LocalizedText_IsWrittenAsIs`

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `MaxLexiconDocumentBytes` | 1 MiB | every file read or resolved (LEX limits) |
| `MaxGitArchiveBytes` | 256 MiB | git source download |
| `MaxSchemasPerPublish` | 1,000 | `publish` input set |
| `MaxNetworkInstallListing` | 1,000 records | authority prefix listing |

Network fetches use the untrusted-fetch layer and its limits ([identity.md](identity.md)).

## Errors

Failures map to exit codes (TOO-3) with a one-line message on stderr: the file and pointer for
input errors, the NSID and XRPC error name for remote errors. Stack traces print only with
`--verbose`.

## Conformance and tests

- Command tests run `LexgenApp` in-process with a fake file system, environment and network
  (TestKit's `FakeXrpcServer`, `FakeIdentityNetwork`), covering every scenario named in the
  requirements. Port the scenarios of 0.8 `LexiconGeneratorProgramTests` except the `lexicon`
  and assembly-baseline ones.
- `generate` golden test: CLI output equals source-generator output for the representative set
  (LEX-25).
- `install`/`update` against a local git fixture repository and a fake authority repository with
  proofs.

## Changes from 0.8

| Change | Reason |
|---|---|
| `csharp` is renamed `generate` and uses the shared emitter | LEX-25 |
| `lexicon` (C# → Lexicon JSON by reflection in an isolated load context) is removed, with `diff -a` against an assembly | [Q-10](../06-decisions.md#open-questions): Lexicon JSON is the source of truth |
| New `install` and `update` replace `refresh.sh` and the hand-maintained README provenance | LEX-22 |
| Malformed Lexicons fail generation instead of being skipped | TOO-10 |
| Exit codes distinguish findings, usage, input and network failures | TOO-3 |
| `publish` writes atomically through `applyWrites` | TOO-27 |
| System.CommandLine replaces the hand-rolled parser | TOO-1 |

## Open points

Defaults accepted (maintainer, 2026-10-17). The implementing phase applies them without further
sign-off, settles any point left to it, follows upstream where a point waits on upstream, and
records the outcome here in the same PR.

| # | Question | Default |
|---|---|---|
| TOO-O1 | Offer OAuth (loopback redirect) for `publish` instead of app passwords? | Not for 0.9; app passwords from the environment are enough for CI. |
| TOO-O2 | A `diff --baseline git:<ref>` convenience that reads the manifest's Lexicons at a git ref? | Add it if `diff` against a checked-out directory proves clumsy in CI. |
| TOO-O3 | Should `generate` also emit server handler stubs for a Lexicon set ([server.md](server.md))? | No; handlers implement interfaces keyed by the generated descriptors, which need no stubs. |

*Phase 3 outcomes (`install`/`update`):* `update`'s NSID arguments select whole sources, which are
re-read at their pin (or `--commit`, which must select exactly one git source), so a source's files
always match its recorded commit. A namespace pattern given to `install` joins the source's
`namespaces`; for an NSID several sources' namespaces cover, the most specific one owns it. Refs no
source, manifest entry, `provided` pattern or `--reference` (an `install` option) accounts for are
exit 3. `network` sources are exit 2 until phase 8, and a bare `update` skips them. Provenance is
`commit` and `fetched`; the commit date is not recorded.
`generate --unit` checks and cleans only those units' folders under `--out`, so units can share one output directory.
