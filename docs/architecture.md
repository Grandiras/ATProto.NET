# Architecture

ATProto.NET is split into five runtime packages plus one `dotnet tool`. They layer onto each other so you take only what you need — the core SDK has no ASP.NET dependency, and each integration package adds one capability on top.

## Package layering

```
              ┌──────────────────────────────────────────────────────┐
              │  ATProtoNet.Blazor                                   │
              │  Components (LoginForm, FeedView, PostCard, …)       │
              └─────────────────────────┬────────────────────────────┘
                                        │ uses
              ┌─────────────────────────▼────────────────────────────┐   ┌──────────────────────────────────┐
              │  ATProtoNet.Server                                   │◄──┤  ATProtoNet.Server.              │
              │  DI (AddAtProto → IAtProtoBuilder), OAuth cookie     │   │  EntityFrameworkCore             │
              │  login, service auth, refresh coordination,          │   │  EF Core stores: sessions, the   │
              │  IAtProtoClientFactory, session stores (in-memory,   │   │  replay table, space stores,     │
              │  file), Aspire client integration (AddAtProtoClient  │   │  Sync 1.1 repository state       │
              │  — health checks), XRPC routing, the space server    │   └──────────────────────────────────┘
              │  — nothing beyond the ASP.NET Core shared framework  │
              └─────────────────────────┬────────────────────────────┘
                                        │ builds on
                                        ▼
              ┌──────────────────────────────────────────────┐
              │  ATProtoNet  (core SDK — no ASP.NET dep)     │
              │  AtProtoClient, RecordCollection<T>, OAuth,  │
              │  identity resolution, crypto, MST, CAR,      │
              │  DAG-CBOR, streaming, Spaces, Tap            │
              └──────────────────────────────────────────────┘

       ┌──────────────────────────────────────────────────────┐
       │  ATProtoNet.Aspire.Hosting                            │
       │  Run a PDS container (Bluesky or Tranquil) in Aspire  │
       └──────────────────────────────────────────────────────┘
```

## What each project does

| Package | Role |
|---------|------|
| **`ATProtoNet`** | Core SDK, zero ASP.NET dependency. `AtProtoClient` composes per-Lexicon-domain sub-clients (`Server`, `Repo`, `Identity`, `Sync`, `Admin`, `Label`, `Moderation`, `Space`, `SimpleSpace`, `Temp`, `Lexicon`, `Bsky`, `Chat`, `Ozone`, `Site`) around one internal XRPC transport, exposed to sub-clients of other Lexicons as `IXrpcTransport`. Custom records flow through `RecordCollection<T>` / `GetCollection<T>()`; custom XRPC through `QueryAsync` / `ProcedureAsync`. Also: the OAuth client, identity resolution, the firehose, label-stream and Jetstream consumers, the Tap client, repository structures, labels, and the Spaces client and syncer. |
| **`ATProtoNet.Server`** | ASP.NET Core integration: `AddAtProto()` and the `IAtProtoBuilder` the rest hangs off (`WithOAuth` for the OAuth cookie login with `MapAtProtoOAuth`, `WithClientFactory` for `IAtProtoClientFactory`, the session store, `WithHealthCheck`), service auth (`AddAtProtoServiceAuth`), the in-memory default and file `IAtProtoSessionStore`s, server-side XRPC handler routing, the label endpoint, the Tap webhook, the [space server](spaces.md#serving-a-space) (`AddAtProtoSpaces`), and .NET Aspire client integration (`AddAtProtoClient` with a health check). No NuGet dependency beyond the core package and the ASP.NET Core shared framework. |
| **`ATProtoNet.Server.EntityFrameworkCore`** | The EF Core stores, namespace `ATProtoNet.Server.EntityFrameworkCore`: the encrypted session store (`WithEfCoreSessionStore<T>()`), the single-use token replay store (`AddAtProtoEfCoreJtiReplayStore<T>()`), the space authority and `simplespace` stores, and Sync 1.1 repository state, each with its `DbContext` and `Configure…Model` helper. |
| **`ATProtoNet.Blazor`** | Blazor components: `LoginForm`, and `FeedView`, `PostCard`, `ProfileCard` and `ComposePost`, which act as the signed-in user through `AddAtProtoBlazor()`. |
| **`ATProtoNet.Aspire.Hosting`** | Aspire `AppHost`-side resources for running a PDS container: the official Bluesky one (`AddAtProtoPds`, `WithAtProtoPds`) or Tranquil (`AddAtProtoTranquilPds`, `WithAtProtoTranquilPds`, which also provisions the PostgreSQL server it needs). Administer either with `PdsAdminClient` from the core package. |
| **`tools/ATProtoNet.LexiconGenerator`** | `dotnet tool` (binary `atproto-lexgen`) for bidirectional Lexicon JSON ↔ C# generation, linting and diffing schemas, and publishing and resolving them on the network. |

## Source tree

```
ATProto.NET/
├── src/
│   ├── ATProtoNet/                              # Core SDK
│   │   ├── AtProtoClient.cs                     # The client facade
│   │   ├── RecordCollection.cs                  # Typed CRUD for custom records
│   │   ├── BlueskyClients.Helpers.cs            # client.Bsky.PostAsync, LikeAsync, …
│   │   ├── Admin/                               # PdsAdminClient — administer a PDS you operate
│   │   ├── Auth/                                # AtProtoSession, IAtProtoSessionStore, refresh coordination, ServiceAuthGenerator
│   │   │   └── OAuth/                           # OAuthClient, DPoP, PKCE, discovery, scopes, state stores
│   │   ├── Crypto/                              # AtProtoCrypto, AtProtoKey (P-256 / K-256), BLAKE3
│   │   ├── Http/                                # XRPC transport, XrpcException family, XrpcParams, pagination
│   │   ├── Identity/                            # Did, Handle, AtUri, Nsid, Tid, Cid, AtDatetime; resolvers, PlcClient
│   │   ├── Labeling/                            # LabelSigner, LabelVerifier, label stream frames
│   │   ├── Models/                              # BlobRef, StrongRef, Label, …
│   │   ├── Repo/                                # CAR, MerkleSearchTree, DAG-CBOR, CIDs, commits, Sync 1.1 verifier
│   │   ├── Serialization/                       # JSON defaults, union converters, LexiconTypeRegistry
│   │   ├── Spaces/                              # SpaceUri, LtHash, commits, credentials, SpaceSyncer
│   │   ├── Streaming/                           # TypedFirehoseConsumer, Jetstream, label streams, the shared stream loop
│   │   ├── Tap/                                 # TapClient
│   │   └── Lexicon/
│   │       ├── Com/AtProto/                     # Admin, Identity, Label, Lexicon, Moderation, Repo, Server, SimpleSpace, Space, Sync, Temp
│   │       ├── App/Bsky/                        # Actor, AgeAssurance, Bookmark, Draft, Embed, Feed, Graph, Labeler, Notification, RichText, Unspecced, Video
│   │       ├── Chat/Bsky/                       # Actor, Convo, Embed, Group, Moderation, Notification
│   │       ├── Site/Standard/                   # Document, Graph, Publication
│   │       └── Tools/Ozone/                     # Communication, Hosting, Moderation, Queue, Report, Safelink, Server, Set, Setting, Signature, Team, Verification
│   ├── ATProtoNet.Server/                       # ASP.NET Core integration
│   │   ├── Extensions/                          # AddAtProto, IAtProtoBuilder, AddAtProtoIdentity
│   │   ├── Authentication/                      # OAuth cookie login, service auth, IJtiReplayStore, claim types
│   │   ├── Services/                            # IAtProtoClientFactory
│   │   ├── TokenStore/                          # FileAtProtoSessionStore
│   │   ├── Xrpc/                                # IXrpcEndpoint, MapXrpcEndpoints
│   │   ├── Spaces/                              # The space server: verifiers, stores, endpoints
│   │   ├── Labeling/                            # QueryLabelsEndpoint, ILabelSource
│   │   ├── Tap/                                 # MapTapWebhook
│   │   ├── Admin/                               # AddAtProtoPdsAdmin
│   │   └── Aspire/                              # AddAtProtoClient, health checks
│   ├── ATProtoNet.Server.EntityFrameworkCore/   # EF Core stores: TokenStore/, Authentication/, Spaces/, Sync/
│   ├── ATProtoNet.Blazor/                       # Components/, AtProtoUserClientAccessor, FeedSource
│   └── ATProtoNet.Aspire.Hosting/               # PDS container resources (Bluesky and Tranquil)
├── tools/
│   └── ATProtoNet.LexiconGenerator/             # `atproto-lexgen`: CodeGen/, Schema/, Validation/, Publishing/
├── samples/
│   ├── FirehoseConsumerSample/                  # Typed firehose with filtering
│   ├── JetstreamReplaySample/                   # Jetstream v2 archive backfill and cutover
│   ├── ManagedPdsSample/                        # Account provisioning via PdsAdminClient
│   ├── ManagedPdsSample.AppHost/                # Aspire AppHost running the PDS container
│   ├── ServerIntegrationSample/                 # OAuth login, Blazor widgets + server-side AT Proto access
│   └── SpacesSample/                            # Permissioned data: a space, its members and a syncer
├── tests/
│   ├── ATProtoNet.Tests/                        # Unit tests
│   ├── ATProtoNet.IntegrationTests/             # Integration tests (need a PDS, Jetstream, …)
│   ├── ATProtoNet.DocSnippets/                  # Compiles every C# sample in README.md and docs/
│   └── ATProtoNet.DocSnippets.Generator/        # The source generator that extracts them
└── docs/                                        # This documentation
```

## Lexicon layout convention

Every AT Protocol namespace under `src/ATProtoNet/Lexicon/` follows the same two-file pattern:

```
Lexicon/<TopDomain>/<SubDomain>/<Service>/
    <Service>Client.cs    # methods bound to the XRPC client
    <Service>Models.cs    # DTOs / records
```

Examples: `Lexicon/Com/AtProto/Repo/{RepoClient.cs, RepoModels.cs}`, `Lexicon/App/Bsky/Feed/{FeedClient.cs, FeedModels.cs}`. The five top-level domains are `Com/AtProto/*`, `App/Bsky/*`, `Chat/Bsky/*`, `Site/Standard/*`, and `Tools/Ozone/*`. New Lexicons should follow this pattern and register as a property on `AtProtoClient`.

The permissioned data protocol follows the same pattern for its two namespaces (`Lexicon/Com/AtProto/Space/*` and `Lexicon/Com/AtProto/SimpleSpace/*`), but its protocol machinery — space URIs, the `LtHash` set hash and commit construction, the key-bound credentials and their HTTP Message Signatures, and the syncer — lives outside the Lexicon tree under `Spaces/`, since none of it is a wrapper over an XRPC endpoint. See [Spaces (Permissioned Data)](spaces.md).

JSON property names use `camelCase` (the AT Proto convention) — set `[JsonPropertyName("...")]` explicitly rather than relying on a global naming policy.

## Shared build config

The root `Directory.Build.props` holds what every project shares: the target framework, nullable reference types and implicit usings, and the package metadata (`Version`, `Authors`, license, URLs, readme, SourceLink, deterministic build flags). Don't duplicate any of it in a `.csproj`; a project file sets only what differs, such as `Description`, `PackageTags` and its references. `src/`, `tests/` and `samples/` each add a `Directory.Build.props` that imports the root one: `src/` turns on the documentation file, and `tests/` and `samples/` set `<IsPackable>false</IsPackable>`.

Nullable reference types are enabled everywhere. `<TreatWarningsAsErrors>` stays at its default of `false`, but the solution builds warning-free and new warnings on touched files should be addressed.

Public APIs are XML-documented: every `src/` project sets `GenerateDocumentationFile` and promotes **CS1591 to an error**, so a new public member without an XML comment fails the build.

The documentation's C# samples are compiled too: `tests/ATProtoNet.DocSnippets` turns every ` ```csharp ` block of `README.md` and `docs/*.md` into code, so a sample that drifts from the API fails the build at its own Markdown line. See [CONTRIBUTING](../CONTRIBUTING.md#documentation).
