# Architecture

ATProto.NET is split into seven runtime packages plus one `dotnet tool`. They layer onto each other so you take only what you need — the core SDK has no ASP.NET dependency, and each integration package adds one capability on top.

## Package layering

```
                  ┌──────────────────────────────────────────────────────┐
                  │  ATProtoNet.Blazor                                   │
                  │  Components (LoginForm), OAuth login endpoints       │
                  └─────────────────────────┬────────────────────────────┘
                                            │ uses
┌──────────────────────────┐  ┌─────────────▼────────────────────────────┐
│  ATProtoNet.Aspire       │  │  ATProtoNet.Server                        │
│  Service defaults,       │  │  DI (AddAtProtoServer), JWT auth handler, │
│  health checks,          │  │  IAtProtoClientFactory, token stores,     │
│  resilience              │  │  server-side XRPC routing                 │
└────────────┬─────────────┘  └─────────────┬────────────────────────────┘
             │                              │ optionally backed by
             │                              ▼
             │                  ┌──────────────────────────────────────┐
             │                  │  ATProtoNet.Server.EntityFrameworkCore│
             │                  │  EF Core IAtProtoTokenStore           │
             │                  └──────────────────────────────────────┘
             │                              │
             └──────────┬───────────────────┘
                        │ all build on
                        ▼
              ┌──────────────────────────────────────────────┐
              │  ATProtoNet  (core SDK — no ASP.NET dep)     │
              │  AtProtoClient, RecordCollection<T>,         │
              │  XrpcClient, OAuth, crypto, MST, CAR,        │
              │  DAG-CBOR, firehose, identity types          │
              └──────────────────────────────────────────────┘

       ┌──────────────────────────────────────────────────────┐
       │  ATProtoNet.Pds  +  ATProtoNet.Aspire.Hosting        │
       │  Host your own PDS in-process / as an Aspire resource │
       └──────────────────────────────────────────────────────┘
```

## What each project does

| Package | Role |
|---------|------|
| **`ATProtoNet`** | Core SDK, zero ASP.NET dependency. `AtProtoClient` composes per-Lexicon-domain sub-clients (`Server`, `Repo`, `Identity`, `Sync`, `Admin`, `Label`, `Moderation`, `Bsky`, `Chat`, `Ozone`, `Site`) around a shared `XrpcClient`. Custom records flow through `RecordCollection<T>` / `GetCollection<T>(nsid)`; custom XRPC through `QueryAsync<T>` / `ProcedureAsync<T>`. |
| **`ATProtoNet.Server`** | ASP.NET Core integration: DI extensions (`AddAtProto`, `AddAtProtoServer`), JWT auth handler, `IAtProtoClientFactory`, `IAtProtoTokenStore` (in-memory + file implementations), and server-side XRPC handler routing. |
| **`ATProtoNet.Server.EntityFrameworkCore`** | EF Core-backed `IAtProtoTokenStore`. |
| **`ATProtoNet.Blazor`** | Blazor components (`LoginForm`, etc.) and the OAuth login endpoints registered by `MapAtProtoOAuth()`. |
| **`ATProtoNet.Aspire`** | .NET Aspire client integration: service defaults, resilience, health checks. |
| **`ATProtoNet.Aspire.Hosting`** | Aspire `AppHost`-side resource for running a PDS container. |
| **`ATProtoNet.Pds`** | Host your own PDS in-process via `AddAtProtoPds()` / `MapAtProtoPds()`. Pluggable `IAccountStore` / `IRepoStore` with in-memory defaults. |
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
│   │   ├── Repo/                              # CarReader, MerkleSearchTree, DAG-CBOR, CID
│   │   ├── Serialization/                     # JSON converters, defaults
│   │   ├── Streaming/                         # FirehoseClient, TypedFirehoseConsumer
│   │   ├── RecordCollection.cs                # Typed CRUD for custom records
│   │   ├── AtProtoClient.cs                   # Main client facade
│   │   └── Lexicon/
│   │       ├── Com/AtProto/                   # Protocol-level APIs (Server, Repo, Identity, Sync, Admin, Label, Moderation)
│   │       ├── App/Bsky/                      # Bluesky (Actor, Feed, Graph, Notification, RichText, Embed, Video, Labeler)
│   │       ├── Chat/Bsky/                     # Direct messaging (Convo, Actor)
│   │       ├── Site/Standard/                 # Long-form publishing
│   │       └── Tools/Ozone/                   # Moderation tooling
│   ├── ATProtoNet.Server/                     # ASP.NET Core integration
│   ├── ATProtoNet.Server.EntityFrameworkCore/ # EF Core token store
│   ├── ATProtoNet.Blazor/                     # Blazor components + OAuth endpoints
│   ├── ATProtoNet.Aspire/                     # Aspire client integration
│   ├── ATProtoNet.Aspire.Hosting/             # Aspire AppHost-side PDS resource
│   └── ATProtoNet.Pds/                        # In-process PDS hosting
├── tools/
│   └── ATProtoNet.LexiconGenerator/           # `atproto-lexgen` dotnet tool
├── samples/
│   ├── BlazorOAuthSample/                     # Blazor Server OAuth example
│   ├── FirehoseConsumerSample/                # Typed firehose with filtering
│   ├── PdsSample/                             # Minimal PDS hosting
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

JSON property names use `camelCase` (the AT Proto convention) — set `[JsonPropertyName("...")]` explicitly rather than relying on a global naming policy.

## Shared build config

Shared package metadata (`Version`, `Authors`, SourceLink, deterministic build flags) lives in `Directory.Build.props` — do not duplicate it in individual `.csproj` files. Set `<IsPackable>false</IsPackable>` on non-packable projects (tests, samples).

Nullable reference types are enabled everywhere. `<TreatWarningsAsErrors>` is intentionally `false` on the core project, but new warnings on touched files should be addressed. Public APIs are XML-documented (`GenerateDocumentationFile` is on for the core project).
