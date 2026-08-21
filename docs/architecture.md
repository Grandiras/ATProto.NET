# Architecture

ATProto.NET is split into four runtime packages plus one `dotnet tool`. They layer onto each other so you take only what you need — the core SDK has no ASP.NET dependency, and each integration package adds one capability on top.

## Package layering

```
              ┌──────────────────────────────────────────────────────┐
              │  ATProtoNet.Blazor                                   │
              │  Components (LoginForm), OAuth login endpoints       │
              └─────────────────────────┬────────────────────────────┘
                                        │ uses
              ┌─────────────────────────▼────────────────────────────┐
              │  ATProtoNet.Server                                   │
              │  DI (AddAtProtoServer), JWT auth handler,            │
              │  IAtProtoClientFactory, token stores (in-memory,     │
              │  file, EF Core), Aspire client integration           │
              │  (AddAtProtoClient — health checks, resilience),     │
              │  server-side XRPC routing                            │
              └─────────────────────────┬────────────────────────────┘
                                        │ builds on
                                        ▼
              ┌──────────────────────────────────────────────┐
              │  ATProtoNet  (core SDK — no ASP.NET dep)     │
              │  AtProtoClient, RecordCollection<T>,         │
              │  XrpcClient, OAuth, crypto, MST, CAR,        │
              │  DAG-CBOR, firehose, identity types          │
              └──────────────────────────────────────────────┘

       ┌──────────────────────────────────────────────────────┐
       │  ATProtoNet.Aspire.Hosting                            │
       │  Run the Bluesky PDS container as an Aspire resource  │
       └──────────────────────────────────────────────────────┘
```

## What each project does

| Package | Role |
|---------|------|
| **`ATProtoNet`** | Core SDK, zero ASP.NET dependency. `AtProtoClient` composes per-Lexicon-domain sub-clients (`Server`, `Repo`, `Identity`, `Sync`, `Admin`, `Label`, `Moderation`, `Bsky`, `Chat`, `Ozone`, `Site`) around a shared `XrpcClient`. Custom records flow through `RecordCollection<T>` / `GetCollection<T>(nsid)`; custom XRPC through `QueryAsync<T>` / `ProcedureAsync<T>`. |
| **`ATProtoNet.Server`** | ASP.NET Core integration: DI extensions (`AddAtProto`, `AddAtProtoServer`), JWT auth handler, `IAtProtoClientFactory`, `IAtProtoTokenStore` (in-memory, file, and EF Core implementations), server-side XRPC handler routing, the [space server](spaces.md#serving-a-space) (`AddAtProtoSpaces` — credential verification plus the space authority and repo host endpoints), and .NET Aspire client integration (`AddAtProtoClient` with health checks and resilience). |
| **`ATProtoNet.Blazor`** | Blazor components (`LoginForm`, etc.) and the OAuth login endpoints registered by `MapAtProtoOAuth()`. |
| **`ATProtoNet.Aspire.Hosting`** | Aspire `AppHost`-side resources for running a PDS container: the official Bluesky one (`AddAtProtoPds`, `WithAtProtoPds`) or Tranquil (`AddAtProtoTranquilPds`, `WithAtProtoTranquilPds`, which also provisions the PostgreSQL server it needs). Administer either with `PdsAdminClient` from the core package. |
| **`tools/ATProtoNet.LexiconGenerator`** | `dotnet tool` (binary `atproto-lexgen`) for bidirectional Lexicon JSON ↔ C# generation, schema diffing, and publishing. |

## Source tree

```
ATProto.NET/
├── src/
│   ├── ATProtoNet/                            # Core SDK
│   │   ├── Identity/                          # Did, Handle, AtUri, Nsid, Tid, PlcClient
│   │   ├── Auth/                              # Session, ISessionStore, ServiceAuthGenerator
│   │   │   └── OAuth/                         # OAuth client, DPoP, PKCE, discovery
│   │   ├── Crypto/                            # AtProtoCrypto, AtProtoKey (P-256 / K-256)
│   │   ├── Http/                              # XrpcClient, AtProtoHttpException
│   │   ├── Models/                            # BlobRef, StrongRef, Label, …
│   │   ├── Repo/                              # CarReader/CarWriter, MerkleSearchTree, RepoCommit, DAG-CBOR, CID
│   │   ├── Serialization/                     # JSON converters, defaults
│   │   ├── Streaming/                         # FirehoseClient, TypedFirehoseConsumer, Jetstream
│   │   ├── Admin/                             # PdsAdminClient — administer a PDS you operate
│   │   ├── RecordCollection.cs                # Typed CRUD for custom records
│   │   ├── AtProtoClient.cs                   # Main client facade
│   │   └── Lexicon/
│   │       ├── Com/AtProto/                   # Protocol-level APIs (Server, Repo, Identity, Sync, Admin, Label, Moderation)
│   │       ├── App/Bsky/                      # Bluesky (Actor, Feed, Graph, Notification, RichText, Embed, Video, Labeler)
│   │       ├── Chat/Bsky/                     # Direct messaging (Convo, Actor)
│   │       ├── Site/Standard/                 # Long-form publishing
│   │       └── Tools/Ozone/                   # Moderation tooling
│   ├── ATProtoNet.Server/                     # ASP.NET Core integration (incl. EF Core token store + Aspire client)
│   ├── ATProtoNet.Blazor/                     # Blazor components + OAuth endpoints
│   └── ATProtoNet.Aspire.Hosting/             # Aspire AppHost-side PDS container resource
├── tools/
│   └── ATProtoNet.LexiconGenerator/           # `atproto-lexgen` dotnet tool
├── samples/
│   ├── BlazorOAuthSample/                     # Blazor Server OAuth example
│   ├── FirehoseConsumerSample/                # Typed firehose with filtering
│   ├── ManagedPdsSample/                      # Account provisioning via PdsAdminClient
│   ├── ManagedPdsSample.AppHost/              # Aspire AppHost running the PDS container
│   └── ServerIntegrationSample/               # Blazor + server-side AT Proto access
└── tests/
    ├── ATProtoNet.Tests/                      # Unit tests
    └── ATProtoNet.IntegrationTests/           # Integration tests (requires PDS)
```

## Lexicon layout convention

Every AT Protocol namespace under `src/ATProtoNet/Lexicon/` follows the same two-file pattern:

```
Lexicon/<TopDomain>/<SubDomain>/<Service>/
    <Service>Client.cs    # methods bound to the XRPC client
    <Service>Models.cs    # DTOs / records
```

Examples: `Lexicon/Com/AtProto/Repo/{RepoClient.cs, RepoModels.cs}`, `Lexicon/App/Bsky/Feed/{FeedClient.cs, FeedModels.cs}`. The five top-level domains are `Com/AtProto/*`, `App/Bsky/*`, `Chat/Bsky/*`, `Site/Standard/*`, and `Tools/Ozone/*`. New Lexicons should follow this pattern and register as a property on `AtProtoClient`.

The permissioned data protocol follows the same pattern for its two namespaces (`Lexicon/Com/AtProto/Space/*` and `Lexicon/Com/AtProto/SimpleSpace/*`), but its protocol machinery — space URIs, the `LtHash` set hash and commit construction, the DPoP-bound credential exchange, and the syncer — lives outside the Lexicon tree under `Spaces/`, since none of it is a wrapper over an XRPC endpoint. See [Spaces (Permissioned Data)](spaces.md).

JSON property names use `camelCase` (the AT Proto convention) — set `[JsonPropertyName("...")]` explicitly rather than relying on a global naming policy.

## Shared build config

Shared package metadata (`Version`, `Authors`, SourceLink, deterministic build flags) lives in `Directory.Build.props` — do not duplicate it in individual `.csproj` files. Set `<IsPackable>false</IsPackable>` on non-packable projects (tests, samples).

Nullable reference types are enabled everywhere. `<TreatWarningsAsErrors>` is intentionally `false`, but the solution builds warning-free and new warnings on touched files should be addressed.

Public APIs are XML-documented: `ATProtoNet`, `ATProtoNet.Server`, `ATProtoNet.Blazor`, and `ATProtoNet.Aspire.Hosting` all set `GenerateDocumentationFile` and promote **CS1591 to an error**, so a new public member without an XML comment fails the build.
