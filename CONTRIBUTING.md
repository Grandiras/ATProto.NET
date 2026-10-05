# Contributing to ATProto.NET

Thank you for your interest in contributing! This guide will help you get started.

## Where to Contribute

The canonical source is hosted on [Forgejo](https://git.grandiras.net/Grandiras/ATProto.NET), but **issues and pull requests are accepted on [GitHub](https://github.com/Grandiras/ATProto.NET)** so you don't need an account on the Forgejo instance.

The GitHub repository is a push mirror — code pushed to Forgejo is automatically synced to GitHub.

## Code of Conduct

By participating in this project, you agree to maintain a respectful and inclusive environment.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- A text editor or IDE (VS Code, Rider, Visual Studio)
- [Podman](https://podman.io/) or Docker (for integration tests)

### Building

```bash
git clone https://github.com/Grandiras/ATProto.NET.git
cd ATProto.NET
dotnet build
```

### Running Tests

**Unit tests** (no external dependencies):

```bash
dotnet test tests/ATProtoNet.Tests/
```

The build also compiles every C# sample in `README.md` and `docs/` (see [Documentation](#documentation)),
so a change that breaks one shows up as a build error at its Markdown line.

**Integration tests** (requires a local PDS):

```bash
# Start a local PDS
podman run -d --name atproto-pds \
  -p 2583:3000 \
  -e PDS_HOSTNAME=pds.test \
  -e PDS_DATA_DIRECTORY=/pds \
  -e PDS_BLOBSTORE_DISK_LOCATION=/pds/blocks \
  -e PDS_JWT_SECRET=$(openssl rand -hex 16) \
  -e PDS_ADMIN_PASSWORD=admin-pass \
  -e PDS_PLC_ROTATION_KEY_K256_PRIVATE_KEY_HEX=$(openssl rand -hex 32) \
  -e PDS_DEV_MODE=true \
  -v pds-data:/pds \
  ghcr.io/bluesky-social/pds:0.4.5037   # the version CI runs against

# Run integration tests — AuthenticatedClientFixture and SpaceNetworkFixture provision their
# own throwaway accounts through the admin API, so no account needs creating by hand.
ATPROTO_PDS_URL=http://localhost:2583 \
ATPROTO_PDS_ADMIN_PASSWORD=admin-pass \
dotnet test tests/ATProtoNet.IntegrationTests/
```

Each integration test declares what it needs with `[RequiresFact(IntegrationRequirement.X)]` and is
skipped when the environment does not provide it:

| Requirement | Environment |
|-------------|-------------|
| `Pds` | `ATPROTO_PDS_ADMIN_PASSWORD` (and `ATPROTO_PDS_URL`, default `http://localhost:2583`): the tests provision their own account. `ATPROTO_TEST_HANDLE` / `ATPROTO_TEST_PASSWORD` optionally name an existing one instead |
| `Bluesky` | the above, plus `ATPROTO_HAS_BLUESKY=true` for a PDS with Bluesky app-view services |
| `PdsAdmin` | `ATPROTO_PDS_ADMIN_PASSWORD`; the tests provision their own accounts |
| `Jetstream` | `ATPROTO_TEST_JETSTREAM=true` and outbound internet (`ATPROTO_JETSTREAM_URL` overrides the host) |
| `JetstreamArchive` | the above, plus `ATPROTO_JETSTREAM_API_KEY` |
| `Spaces` | `ATPROTO_TEST_SPACES=true`, `ATPROTO_PDS_ADMIN_PASSWORD` and `ATPROTO_PLC_URL`, against a PDS serving the permissioned-data alpha (`ATPROTO_SPACES_PDS_URL`); see [Testing against a real space host](docs/testing-spaces.md) |

## How to Contribute

### Reporting Bugs

1. Check existing issues to avoid duplicates
2. On Forgejo, use the **Bug Report** issue template. On GitHub, which has no templates, give the
   same information: a description, steps to reproduce, the expected and the actual behaviour, the
   ATProtoNet, .NET and OS versions, and the PDS version where it matters

### Suggesting Features

1. On Forgejo, use the **Feature Request** issue template. On GitHub, cover the same ground: a
   summary, the use case, the proposed API or behaviour, and the alternatives you considered
2. Consider backward compatibility

### Submitting Code

1. **Fork** the [GitHub repository](https://github.com/Grandiras/ATProto.NET)
2. **Create a branch** from `main`: `git checkout -b feature/my-feature`
3. **Write code** following the project conventions (see below)
4. **Add tests** — both unit and integration tests where applicable
5. **Run all tests** and ensure they pass
6. **Commit** with clear, descriptive messages
7. **Open a Pull Request** against `main` on GitHub

### PR Guidelines

- Keep PRs focused — one feature or fix per PR
- Include a clear description of what changed and why
- Reference related issues with `Fixes #123` or `Closes #123`
- Ensure CI passes before requesting review
- Be responsive to review feedback

## Code Conventions

### General

- Target `net10.0` (latest LTS)
- Use C# latest language features where they improve clarity
- Enable nullable reference types everywhere
- XML-document all public APIs

### Public API tracking

Every packable project (all of `src/` except `ATProtoNet.Aspire.Hosting`'s tool dependency,
`tools/ATProtoNet.LexiconGenerator`, which has none) declares its surface in `PublicAPI.Shipped.txt`
and `PublicAPI.Unshipped.txt`. Adding, changing or removing a public member goes in
`PublicAPI.Unshipped.txt` in the same commit (RS0016/RS0017 fail the build otherwise); a release
moves `Unshipped`'s entries into `Shipped` and empties `Unshipped`.

### Naming

- Follow [.NET naming conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names)
- Use `Async` suffix for async methods
- Prefix interfaces with `I`
- Use `camelCase` for JSON serialization (matches AT Proto convention)

### Lexicon models and clients

These rules apply to every model and client method generated from, or written against, a Lexicon.
`tests/ATProtoNet.Tests/Conventions/TypedIdentifierGuardTests.cs` enforces the first one.

**Types in public signatures**

- A property or parameter carrying a Lexicon identifier format is typed, never `string`:
  `did` → `Did`, `handle` → `Handle`, `at-identifier` → `AtIdentifier`, `at-uri` → `AtUri`,
  `nsid` → `Nsid`, `cid` → `Cid`, `record-key` → `RecordKey`, `tid` → `Tid`, and `datetime` →
  `AtDatetime` (all in `ATProtoNet.Identity`). The generic `uri` and `language` formats stay
  `string`. A value that matches a name the guard watches but genuinely is not one format (an
  email subject, a header entry with parameters, a field the PDS fills with `admin`) goes in the
  guard's commented `Exceptions` list.
- Lexicon-required fields are `required`; optional ones are nullable (`Cid?`, `AtDatetime?`).
- Collections in models are `IReadOnlyList<T>`. Method parameters take `IEnumerable<T>` (or
  `IReadOnlyCollection<T>` when the method needs the count).
- A request body that only one client method builds is `internal`; the method takes its fields as
  parameters. A request type stays public only when a public method accepts it.

**Parameter order**

1. Subject identifiers: `repo`/`actor`/`did`, then `collection`, then `rkey` (or the whole `AtUri`).
2. Required inputs.
3. Optional filters.
4. `limit`, then `cursor` (one-page methods), or `pageSize` (enumerators).
5. `CancellationToken cancellationToken = default`.

**Pagination**

- Every cursored response implements `ICursorPage<T>`, with `Items` implemented explicitly over the
  Lexicon-named list so the wire shape does not change.
- `List*` / `Get*` / `Search*` return one page. The public `Http.Pagination.EnumerateAsync<TPage, T>`
  walks any of them, stopping on a `null`, empty or repeated cursor; do not write another cursor
  loop. Only add a dedicated `Enumerate*` wrapper for an endpoint a tracking issue explicitly asks
  for (the SDK keeps 13 of them, see `docs/migrating-to-0.7.md`) — everything else is one line at
  the call site: `Pagination.EnumerateAsync<TPage, T>((cursor, ct) => client.X.GetYAsync(..., cursor,
  ct))`. A kept `Enumerate*` returns `IAsyncEnumerable<T>` and takes `int? pageSize = null` (`null`
  is the server default) instead of `limit`/`cursor`.

**Before and after**

```csharp
// Before: strings, and a filter after the paging parameters
Task<ListRecordsResponse> ListRecordsAsync(
    string repo, string collection, int? limit = null, string? cursor = null, bool? reverse = null,
    CancellationToken cancellationToken = default);
IAsyncEnumerable<RecordEntry> ListAllRecordsAsync(
    string repo, string collection, int pageSize = 100, CancellationToken cancellationToken = default);

// After
Task<ListRecordsResponse> ListRecordsAsync(
    AtIdentifier repo, Nsid collection, bool? reverse = null, int? limit = null, string? cursor = null,
    CancellationToken cancellationToken = default);
IAsyncEnumerable<RecordEntry> EnumerateRecordsAsync(
    AtIdentifier repo, Nsid collection, bool? reverse = null, int? pageSize = null,
    CancellationToken cancellationToken = default) =>
    Pagination.EnumerateAsync<ListRecordsResponse, RecordEntry>(
        (cursor, ct) => ListRecordsAsync(repo, collection, reverse, pageSize, cursor, ct),
        cancellationToken);
```

```csharp
// Before: the subject after the reason
Task<CreateReportResponse> CreateReportAsync(
    string reasonType, ModerationSubject subject, string? reason = null, CancellationToken ct = default);

// After: subject first
Task<CreateReportResponse> CreateReportAsync(
    ModerationSubject subject, string reasonType, string? reason = null, CancellationToken ct = default);
```

```csharp
// Before
public sealed class ListBlobsResponse
{
    [JsonPropertyName("cursor")] public string? Cursor { get; init; }
    [JsonPropertyName("cids")] public required List<string> Cids { get; init; }
}

// After
public sealed class ListBlobsResponse : ICursorPage<Cid>
{
    [JsonPropertyName("cursor")] public string? Cursor { get; init; }
    [JsonPropertyName("cids")] public required IReadOnlyList<Cid> Cids { get; init; }

    IReadOnlyList<Cid> ICursorPage<Cid>.Items => Cids;
}
```

### Architecture

- **ATProtoNet** — Core SDK, zero ASP.NET dependency
- **ATProtoNet.Server** — ASP.NET Core integration (DI, OAuth cookie login, service auth, XRPC
  endpoints, the space server, the Aspire client), nothing beyond the ASP.NET Core shared framework
- **ATProtoNet.Server.EntityFrameworkCore** — EF Core stores for the server package
- **ATProtoNet.Blazor** — Blazor components acting as the signed-in user
- **ATProtoNet.Aspire.Hosting** — PDS containers (Bluesky and Tranquil) in an Aspire AppHost
- **`atproto-lexgen`** (`tools/ATProtoNet.LexiconGenerator`) — the Lexicon CLI

See [Architecture](docs/architecture.md) for how they layer.

### Testing

- Unit tests go in `tests/ATProtoNet.Tests/`
- Integration tests go in `tests/ATProtoNet.IntegrationTests/`
- Use `[RequiresFact(IntegrationRequirement.Pds)]` for tests that need a live PDS
- Use `[RequiresFact(IntegrationRequirement.Bluesky)]` for tests that need Bluesky app view services
- Setting `ATPROTO_REQUIRE_INTEGRATION=1` turns a missing prerequisite into a failure instead of a
  skip, so CI can catch an environment that silently stopped providing one
- Name tests: `MethodName_Scenario_ExpectedResult`

### Commits

- Use conventional commit format when possible:
  - `feat: add blob upload support`
  - `fix: handle null CID in record response`
  - `docs: update custom records guide`
  - `test: add integration tests for firehose`
- Add a bullet to `## [Unreleased]` in `CHANGELOG.md` in the same commit, for anything that changes
  behaviour, public API or the build: a **bold title** and 1–3 sentences, ending with the issue
  number; a breaking change adds a one-line migration note.

### Documentation

The documentation is in `docs/` (listed in [`docs/index.md`](docs/index.md)) and `README.md`. When
you change or remove an API, update the pages that name it.

Every ` ```csharp ` block is compiled: `tests/ATProtoNet.DocSnippets`, built with the solution, turns
each one into code through a source generator (`tests/ATProtoNet.DocSnippets.Generator`), with
`#line` directives pointing back at the Markdown, so a sample that no longer matches the API fails
the build at its own line. How a block is compiled:

- Type declarations go into a namespace shared by the page's blocks, so a later block can use a
  type an earlier one declared. Statements go into a method, and members with an access modifier
  into a class of their own. A `using` in any block applies to the whole page.
- A block may use the variables that `tests/ATProtoNet.DocSnippets/SnippetContext.cs` declares, as
  if from the surrounding code: `client` (a signed-in `AtProtoClient`), `builder`, `services`,
  `app`, `configuration`, `logger`, `httpClient`, `args`, and `ct` / `cancellationToken` /
  `stoppingToken`. Anything else it needs from around it, it declares in an HTML comment on the line
  before the fence, which Markdown does not render:

  ````markdown
  <!-- snippet: OAuthClient oauthClient; string code, state; -->
  ```csharp
  var session = await oauthClient.CompleteAuthorizationAsync(code, state, issuer: null);
  ```
  ````

- Words after `csharp` on the fence change how a block is compiled: `continued` puts it in the same
  method as the block before it, so it sees that block's variables; `partial` leaves out a fragment
  that cannot compile on its own; `before` leaves out the migration guide's code for an API that no
  longer exists. Use `partial` sparingly: a compiled sample is one that cannot go stale.

Check the docs on their own with `dotnet build tests/ATProtoNet.DocSnippets/`.

## Project Structure

```
ATProto.NET/
├── src/
│   ├── ATProtoNet/                              # Core SDK
│   │   ├── Auth/                                # Sessions, session stores, OAuth client
│   │   ├── Http/                                # XRPC transport, XrpcException family
│   │   ├── Identity/                            # Did, Handle, AtUri, …; DID and handle resolution
│   │   ├── Lexicon/                             # AT Proto Lexicon clients and models
│   │   ├── Repo/                                # CAR, MST, DAG-CBOR, commits
│   │   ├── Spaces/                              # Permissioned data
│   │   └── Streaming/                           # Firehose, Jetstream, label streams
│   ├── ATProtoNet.Server/                       # ASP.NET Core integration
│   ├── ATProtoNet.Server.EntityFrameworkCore/   # EF Core stores
│   ├── ATProtoNet.Blazor/                       # Blazor components
│   └── ATProtoNet.Aspire.Hosting/               # Aspire AppHost PDS resources
├── tools/ATProtoNet.LexiconGenerator/           # atproto-lexgen
├── tests/
│   ├── ATProtoNet.Tests/                        # Unit tests
│   ├── ATProtoNet.IntegrationTests/             # Integration tests
│   ├── ATProtoNet.DocSnippets/                  # Compiles the documentation's C# samples
│   └── ATProtoNet.DocSnippets.Generator/        # …through this source generator
├── docs/                                        # Documentation
└── samples/                                     # Example projects
```

The full tree is in [Architecture](docs/architecture.md#source-tree).

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
