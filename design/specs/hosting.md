# Hosting, DI and integrations — spec

**Status:** Draft · **Packages:** `ATProtoNet` (namespace `ATProtoNet.Hosting`; registration
extension methods in `Microsoft.Extensions.DependencyInjection` and, for Aspire,
`Microsoft.Extensions.Hosting`), `ATProtoNet.Aspire.Hosting` ·
**Phase:** 9 (hosting layer including Spaces registration), 11 (Aspire.Hosting port) ·
**Normative references:** [.NET generic host](https://learn.microsoft.com/dotnet/core/extensions/generic-host),
[Options pattern](https://learn.microsoft.com/dotnet/core/extensions/options),
[`IHttpClientFactory`](https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory),
[Aspire integrations](https://learn.microsoft.com/dotnet/aspire/fundamentals/integrations-overview),
[OpenTelemetry semantic conventions](https://opentelemetry.io/docs/specs/semconv/)

## Scope

In scope:

- The hosting layer, namespace `ATProtoNet.Hosting` inside the base `ATProtoNet` package: the
  `services.AddAtProto()` builder and its verbs, options binding and validation, the named
  `HttpClient` registry, the per-user client factory, session-store selection, the file session
  store and distributed-cache OAuth state store, refresh coordination, identity registration, PDS
  admin registration, stream workers, Spaces registration (`AddSpaces(…)`: credential provider,
  space-scoped clients, the space syncer and its worker; HST-50…HST-53), health checks, in-memory
  fallback warnings, telemetry wiring, and the Aspire **client** integration. For it, `ATProtoNet` depends on small
  `Microsoft.Extensions.*` packages (Logging, Caching and DependencyInjection abstractions,
  Options, Http, Hosting.Abstractions, Diagnostics.HealthChecks.Abstractions) and
  `Microsoft.AspNetCore.DataProtection.Abstractions` (the encrypted file store), never on the
  ASP.NET Core shared framework. Worker services and console apps need only `ATProtoNet`.
- No Blazor package ([D-09](../06-decisions.md#d-09-blazor-components)): the per-circuit user
  client accessor and the login form are sample code in `samples/` (see [Blazor](#blazor)).
- `ATProtoNet.Aspire.Hosting`: AppHost resources for the official Bluesky PDS and Tranquil PDS
  containers.

Out of scope: XRPC endpoints, the service-auth and space-credential schemes, the OAuth web login,
labeler, Tap and Spaces server-role endpoints ([server.md](server.md)); Spaces protocol behaviour
([spaces.md](spaces.md)); store contracts and the file/distributed/EF implementations
([stores.md](stores.md)); stream semantics ([streaming.md](streaming.md)); session and OAuth
behaviour ([auth.md](auth.md)).

## Public API sketch

```csharp
// ── ATProtoNet (namespace ATProtoNet.Hosting) ─────────────────────────
builder.Services.AddAtProto(o => o.ServiceUrl = new Uri("https://bsky.social"))   // binds "AtProto" too
    .AddClientFactory()                                   // IAtProtoClientFactory
    .UseFileSessionStore(o => o.Directory = "/var/lib/app/sessions")   // replace semantics (AG-25)
    .UseCursorStore<MyCursorStore>()
    .AddIdentity(o => o.Cache.DocumentTtl = TimeSpan.FromHours(1))
    .AddPdsAdmin()                                        // binds "AtProto:Pds"
    .AddStreamWorker<FirehoseItem, MyFirehoseHandler>(o => o.StreamId = "firehose")   // TEvent: the stream's union
    .AddSpaces()                                          // credential provider, space clients (HST-50)
    .AddSpaceSyncWorker<MySpaceRepoStore>(o => o.Follow(spaceUri))   // HST-51
    .AddHealthChecks()                                    // atproto-pds
    .ConfigureHttpClients((name, http) => http.AddHttpMessageHandler<MyLoggingHandler>());

public sealed class AtProtoBuilder
{
    public IServiceCollection Services { get; }
}

public static class AtProtoHttpClientNames
{
    public const string Xrpc = "ATProtoNet.Xrpc";
    public const string Identity = "ATProtoNet.Identity";   // SSRF-guarded primary handler
    public const string OAuth = "ATProtoNet.OAuth";         // SSRF-guarded primary handler
    public const string Streaming = "ATProtoNet.Streaming";
    public const string Admin = "ATProtoNet.Admin";
    public const string Spaces = "ATProtoNet.Spaces";       // SSRF-guarded primary handler; space hosts come from DID documents
    public static IReadOnlyList<string> All { get; }
}

public interface IAtProtoClientFactory
{
    ValueTask<AtProtoClient?> CreateForUserAsync(Did user, CancellationToken cancellationToken = default);
    AtProtoClient CreateAnonymous();
}

public interface IAtProtoStreamHandler<TEvent>
{
    ValueTask HandleAsync(TEvent streamEvent, CancellationToken cancellationToken);
}

public sealed class StreamWorker<TEvent> : BackgroundService { /* runs one stream, calls the handler */ }

public static class AtProtoConfigurationKeys
{
    public const string Section = "AtProto";
    public const string PdsSection = "AtProto:Pds";
    public const string PdsUrl = "AtProto:Pds:Url";
    public const string PdsAdminPassword = "AtProto:Pds:AdminPassword";
    public const string PdsAuthentication = "AtProto:Pds:Authentication";
    public const string PdsAdminIdentifier = "AtProto:Pds:AdminIdentifier";
    public const string PdsAllowInsecureHttp = "AtProto:Pds:AllowInsecureHttp";
    public static string ToEnvironmentVariable(string key);   // "AtProto:Pds:Url" → "AtProto__Pds__Url"
}

// Aspire client integration (namespace Microsoft.Extensions.Hosting, per Aspire convention)
builder.AddAtProtoClient(connectionName: "pds");          // returns AtProtoBuilder

// ── ATProtoNet.Aspire.Hosting (AppHost) ───────────────────────────────
var pds = builder.AddAtProtoPds("pds").WithDataVolume();
var tranquil = builder.AddAtProtoTranquilPds("tranquil").WithAdminAccount("admin");
builder.AddProject<Projects.Api>("api").WithReference(pds);   // or WithAtProtoPds(pds)
```

Aspire resource builders use Aspire's own `With*` convention. AG-25 (`Add*`/`Use*`, no `With*`)
applies to the SDK's DI builder, not to AppHost resource builders.

## Requirements

### The builder

- **HST-1** (MUST) `AddAtProto()` is idempotent: calling it more than once returns a builder over
  the same registrations and applies every configuration callback.
  *Source:* 0.8 · test `AtProtoBuilderTests.AddAtProto_CalledTwice_KeepsOneClientWithBothConfigurations`
- **HST-2** (MUST) On `AtProtoBuilder`, `Add*` adds a feature and `Use*` selects the
  implementation of a seam with replace semantics: the last `Use*` call wins regardless of the
  order of other calls, and a `Use*` call replaces the in-memory default without a warning.
  *Source:* [AG-25](../03-api-guidelines.md#options-configuration-and-dependency-injection) ·
  0.8 `AtProtoBuilderExtensions.cs:114-119` · test `WithSessionStore_OfAType_IsTheStoreEitherWayRound`
- **HST-3** (MUST) Every registration extension method of `ATProtoNet` (its hosting layer),
  `ATProtoNet.AspNetCore` and `ATProtoNet.EntityFrameworkCore` hangs off
  `AtProtoBuilder`, except
  `AddAtProtoServiceAuth` and `AddAtProtoSpaceCredentials` (on `AuthenticationBuilder`), `MapAtProto*` (on
  `IEndpointRouteBuilder`) and `AddAtProtoClient` (on `IHostApplicationBuilder`, Aspire
  convention). *Source:* **Decision**: 0.8 has 32 entry points on 7 receivers with mixed verbs.
- **HST-4** (MUST) `AddAtProto()` registers `AtProtoClient` as a singleton built on an
  `AnonymousHandler` over the configured `ServiceUrl` (default `https://bsky.social`), sending
  through the `AtProtoHttpClientNames.Xrpc` client. Per-user clients come from the factory
  (HST-14), never from this registration. *Source:* 0.8 · tests `AddAtProto_ResolvesOneClientBuiltFromTheOptions`,
  `AddAtProto_TheClientSendsThroughTheBuildersHttpClient` · anonymous handler per
  [D-06](../06-decisions.md#d-06-a-session-is-the-authenticated-transport)
- **HST-5** (MUST NOT) No lifetime switch (`WithLifetime` in 0.8) exists. A scoped or transient
  client is unnecessary because `AtProtoClient` is immutable and per-user clients come from the
  factory. *Source:* **Decision** ([D-06](../06-decisions.md#d-06-a-session-is-the-authenticated-transport))

### Options and configuration

- **HST-6** (MUST) Every options type is registered through `IOptions<T>` with
  `ValidateOnStart()` and an `IValidateOptions<T>`; options are never also registered as raw
  singletons, and options never carry services (0.8 puts `ISessionRefreshCoordinator` into
  `AtProtoClientOptions`). Invalid configuration stops the host at startup with a message naming
  the configuration key. *Source:* 0.8 `AtProtoOptionsRegistration.cs:36`,
  `AtProtoServiceCollectionExtensions.cs:59-60` · tests `AddAtProto_AnInvalidInstanceUrl_StopsTheHost`,
  `PdsAdminExtensionsTests.AddAtProtoPdsAdmin_WithoutUrl_StopsTheHostWithAnActionableMessage`
- **HST-7** (MUST) Code callbacks win over bound configuration in either registration order
  (callbacks run as post-configure). *Source:* 0.8 `AtProtoServiceCollectionExtensions.cs:57-60` ·
  tests `AddAtProto_ThenAConfigurationSection_CodeWins`, `AddAtProto_ThenAddAtProtoClient_CodeWinsOverConfiguration`
- **HST-8** (MUST) The configuration layout is:

  | Section | Options type |
  |---|---|
  | `AtProto` | client options (`ServiceUrl`, `UserAgent`, rate-limit values) |
  | `AtProto:Identity` | identity resolution options |
  | `AtProto:OAuth` | OAuth login options ([server.md](server.md)) |
  | `AtProto:Pds` | PDS admin options |
  | `AtProto:Streams:{StreamId}` | one stream worker's options |
  | `AtProto:HealthChecks` | health-check switches (`Enabled`) |

  Key strings are defined once in `AtProtoConfigurationKeys` and used by both the hosting layer
  of `ATProtoNet` and `ATProtoNet.Aspire.Hosting` (through a linked source file, since the
  AppHost package does not reference the SDK). *Source:* **Decision**: 0.8 duplicates
  `"AtProto:Pds"` and `"AtProto__Pds__Url"` across packages, and puts `DisableHealthChecks`
  inside the client options section.

### HTTP clients

- **HST-9** (MUST) Every `HttpClient` the SDK uses under DI is a named client from
  `AtProtoHttpClientNames`. The SDK registers each one it needs; `ConfigureHttpClients` applies a
  configuration callback to all of them. *Source:* **Decision**: 0.8 has four ad-hoc names
  (`"ATProtoNet"`, `"ATProtoNet.OAuth"`, `"AtProtoSpaces"`, `nameof(PdsAdminClient)`).
- **HST-10** (MUST) The `Identity`, `OAuth` and `Spaces` clients use the untrusted-fetch primary handler
  ([identity.md](identity.md#network-policy)); `ConfigureHttpClients` may add delegating
  handlers to them but cannot replace that primary handler unless the identity options allow
  private networks. *Source:* 0.8 · test `WithOAuth_TheIdentityOptionsAllowPrivateNetworks_LiftsTheAddressCheck` ·
  0.8 `SpaceServerExtensions.cs:67-78` (the Spaces client already used the identity network policy)
- **HST-11** (MUST NOT) The SDK never adds a resilience or retry handler to its clients.
  *Source:* 0.7 #136 (resilience handlers resend POSTs and spend DPoP proofs) · test
  `AddAtProtoClient_AddsNoResilienceHandler`
- **HST-12** (MUST) `AddAtProtoClient` (Aspire) removes resilience handlers that service defaults
  added through `ConfigureHttpClientDefaults` from **every** name in `AtProtoHttpClientNames.All`.
  *Source:* **Decision**: 0.8 documents removing them from two clients only, so admin
  `createAccount` POSTs and Spaces calls are still retried under service defaults
  (`docs/aspire.md:118-142`).
- **HST-13** (MUST) The SDK never sets `BaseAddress` or default headers on a named client; every
  header is per request ([02 — Architecture](../02-architecture.md#http)).

### Client factory and per-user clients

- **HST-14** (MUST) `IAtProtoClientFactory.CreateForUserAsync(did)` restores the user's session
  from `ISessionStore` and returns a new `AtProtoClient` over it, or `null` when no session is
  stored. Clients for the same user share the restored session object (and therefore its
  single-flight refresh and DPoP key); they do not each rebuild one.
  *Source:* 0.8 `AtProtoClientFactory.cs` · tests `CreateClientForUserAsync_ReturnsAClientOnTheStoredSession`,
  `ClientsOfOneAccount_ShareTheImportedDPoPKey`, `ConcurrentRequestsOfOneUser_SpendTheRefreshTokenOnce`
- **HST-15** (MUST) Restored sessions are cached per DID in a bounded cache
  (`HostingLimits.SessionCacheCapacity`); a session removed or replaced in the store (detected by
  version, [stores.md](stores.md)) is evicted, and a newer session's key replaces an older
  one. Evicted sessions release their keys. *Source:* 0.8 · tests `TheKeyCache_IsBounded`,
  `ASessionGoneFromTheStore_TakesItsCachedKeyWithIt`, `ASessionWithAnotherKey_ReplacesTheCachedOne`,
  `TheEndOfAnOlderSession_KeepsTheNewSessionsKey`
- **HST-16** (MUST) Per-user clients use the `Xrpc` named client and the client options of
  `AddAtProto` (user agent, rate-limit values); session behaviour comes from the session.
  *Source:* 0.8 · test `WithClientFactory_PerUserClientsTakeTheTransportOptionsOfAddAtProto`
- **HST-17** (MUST) The factory takes a `Did`, not a `ClaimsPrincipal`. The `ClaimsPrincipal`
  adapter lives in `ATProtoNet.AspNetCore` (`CreateForUserAsync(ClaimsPrincipal)`) and accepts
  only OAuth-login identities (`GetAtProtoUser()`); a service-auth caller with the same DID gets
  `null`. *Source:* 0.8 `AtProtoClientFactory.cs:110` · tests `AServiceAuthCaller_DoesNotGetTheAccountsStoredSession`,
  `APrincipalOfBothSchemes_GetsTheSignedInUsersSession` · CHANGELOG 0.7 Security
- **HST-18** (MUST) OAuth sessions are restored with the registered `OAuthClient`'s grant
  configuration resolved lazily, so the factory resolves before the server has started.
  *Source:* 0.8 · test `WithClientFactory_WithTheDevelopmentLogin_ResolvesBeforeTheServerHasStarted`
- **HST-19** (MUST) `AddClientFactory` registers one `IRefreshCoordinator` shared with the OAuth
  login. *Source:* 0.8 · test `WithOAuth_AndTheClientFactory_ShareOneRefreshCoordinator`

### Stream workers

- **HST-20** (MUST) `AddStreamWorker<TEvent, THandler>()` registers a `StreamWorker<TEvent>`
  hosted service that enumerates the configured stream ([streaming.md](streaming.md)) and calls
  `THandler` (resolved in a new scope per event) for each item. `TEvent` is the stream's item
  union (`FirehoseItem`, `JetstreamItem`, …, STR-26), so a handler sees notices as well as events
  and can `switch` over them exhaustively. The cursor commit rule
  (event *n* committed when *n+1* is pulled) means "committed after the handler returned".
  *Source:* [D-07](../06-decisions.md#d-07-one-consumption-model-for-every-event-stream)
- **HST-21** (MUST) A handler exception stops the worker by default (the event is not
  committed and is redelivered on restart); `StreamWorkerOptions.OnHandlerError` may choose to
  skip and continue instead. A `StreamException` that is not retryable stops the worker and is
  logged at `Critical`. *Source:* **Decision**
- **HST-22** (MUST) Stopping the host cancels the enumeration and flushes the cursor of the last
  handled event. *Source:* STR flush-on-exit rule · 0.8 `ConsumeAsync_HoweverItEnds_SavesTheCursorOfTheLastHandledEvent`
- **HST-23** (MUST) Several workers may run in one host, each with its own `StreamId`; two
  workers with the same `StreamId` fail at startup. *Source:* **Decision** (0.8 replay consumer
  wrote one cursor key from two trackers)

### Health checks

- **HST-24** (MUST) `AddHealthChecks()` registers `atproto-pds`, calling
  `com.atproto.server.describeServer`, tagged `atproto` and `ready`, reporting the
  registration's failure status (default `Degraded`) when the PDS is unreachable. A second
  registration under the same name replaces the first. With `AtProto:HealthChecks:Enabled =
  false` nothing is registered. *Source:* 0.8 `AtProtoBuilderExtensions.cs:186-215`,
  `AtProtoPdsHealthCheck.cs:38-43` · tests `WithHealthCheck_AnUnreachablePds_ReportsTheFailureStatus`,
  `WithHealthCheck_AfterAddAtProtoClient_ReplacesItsCheck`, `AddAtProtoClient_DisableHealthChecks_DoesNotRegisterHealthCheck`
- **HST-25** (MUST) The Jetstream health check (`AddJetstreamHealthCheck`, in
  `ATProtoNet.Hosting`, not under an `Aspire` namespace) calls `/xrpc/_health` and reports the
  registration's failure status when unreachable. *Source:* **Decision**: 0.8's
  `JetstreamHealthCheck.cs:40` always returns `Unhealthy`, ignoring `failureStatus`.
- **HST-26** (MUST) Both health checks treat cancellation the same way: cancellation of the
  check's token propagates; any other failure (timeouts included) is the failure status.
  *Source:* **Decision** (0.8 treats it differently per check)

### Identity and PDS admin registration

- **HST-27** (MUST) `AddIdentity()` registers the identity resolver stack (untrusted-fetch
  client, DID and handle resolvers, cache) as singletons with `TryAdd` semantics, so an
  application-registered resolver wins; the OAuth login and service-auth scheme resolve through
  the same registered stack. *Source:* 0.8 · tests `IdentityServiceCollectionExtensionsTests`,
  `WithOAuth_ADidResolverRegisteredWithoutAddAtProtoIdentity_IsNotWhatTheLoginResolvesThrough`
- **HST-28** (MUST) `AddPdsAdmin()` has one overload (`Action<PdsAdminOptions>? configure`)
  binding `AtProto:Pds`; it registers `PdsAdminClient` over the `Admin` named client. Startup
  validation names the configuration key for a missing URL or password, refuses plain HTTP to a
  non-loopback host unless `AllowInsecureHttp` is set, and requires `AdminIdentifier` for
  account authentication. *Source:* 0.8 `PdsAdminExtensions.cs:84-174` · tests `PdsAdminExtensionsTests`
  (all) · single overload is **Decision**

### In-memory fallback warnings

- **HST-29** (MUST) Each feature that falls back to an in-memory store contributes its own
  startup warning through `IAtProtoStartupDiagnostic`; no central component knows every store
  (0.8's `InMemoryDefaultsWarning` knows Spaces and service-auth stores; in 0.9 the Spaces
  features contribute their own warnings like every other feature). An explicitly chosen
  store, including an explicitly chosen in-memory one, logs nothing. *Source:* 0.8 · tests
  `WithClientFactory_TheDefaultInMemoryStore_WarnsAtStartup`, `WithClientFactory_AChosenStore_StartsWithoutAWarning`
- **HST-30** (MUST NOT) Warning checks never resolve stores (scoped stores under scope
  validation must not fail startup). *Source:* 0.8 · test
  `HostStart_WithAScopedReplayStore_UnderScopeValidation_StartsWithoutAWarning`

### Spaces registration

Spaces is a core data protocol, so its client side registers on the same builder as everything
else and needs only `ATProtoNet`; the server roles and EF stores add to the same builder from
their packages ([server.md](server.md#spaces-server-roles), [stores.md](stores.md)).

- **HST-50** (MUST) `AddSpaces(…)` registers the `Spaces` named client, a
  `SpaceCredentialProvider` per account client (created by `IAtProtoClientFactory` alongside the
  user's client, disposed with it), and the options shared by every Spaces feature (service DID,
  client attestation factory, lifetimes and limits from [spaces.md](spaces.md#limits)). It is
  idempotent like `AddAtProto()` (HST-1). `ATProtoNet.AspNetCore`'s `AddSpaceAuthority`,
  `AddSimpleSpace`, `AddSpaceRepoHost<T>` and `AddSpaceNotifications<T>` call it.
  *Source:* **Decision** (Spaces is core; maintainer review 2026-10-17) · 0.8 `AddAtProtoSpaces`
  (in the ASP.NET Core package only)
- **HST-51** (MUST) `AddSpaceSyncWorker<TStore>(…)` runs the space syncer as a hosted service:
  it catches up the followed spaces through `listRepos` from the checkpoint in the registered
  `ICursorStore` ([streaming.md](streaming.md), STR-97), syncs each changed repo into the
  `ISpaceRepoStore` `TStore`, and reacts to notifications that `AddSpaceNotifications<T>`
  delivers. It follows the stream workers' rules (HST-21…HST-23) for stop, flush, error
  policy and duplicate IDs, and needs no ASP.NET Core unless notifications are received.
  *Source:* **Decision** · 0.8 `SpaceSyncer` (no hosted integration)
- **HST-52** (MUST) Spaces stores are selected with `Use*` verbs (`UseSpaceRepoStore<T>()`,
  and from `ATProtoNet.EntityFrameworkCore` `UseEfCoreSpaceStores<TContext>()`), with replace
  semantics (HST-2). A Spaces feature that falls back to an in-memory store contributes its own
  startup warning (HST-29).
  *Source:* HST-2, HST-29 · 0.8 `SpaceStoreExtensions.cs`
- **HST-53** (MUST) Spaces features add their activities and metrics (`ATProtoNet.Spaces`) to the
  telemetry wiring of HST-31 like every other area; nothing extra to enable.
  *Source:* AG-32

### Telemetry

- **HST-31** (MUST) `AddAtProto()` registers the SDK's `ActivitySource`s and `Meter`s
  ([02 — Architecture](../02-architecture.md#diagnostics)) with `AddOpenTelemetry()` when the
  application uses OpenTelemetry (through `WithTracing`/`WithMetrics` extension
  `AddAtProtoInstrumentation()`), and the Aspire client integration enables them by default.
  *Source:* **Decision**: 0.8 has no `ActivitySource` or `Meter` despite shipping an Aspire
  integration.

### Aspire client integration

- **HST-32** (MUST) `AddAtProtoClient(connectionName)` calls `AddAtProto()`, binds the `AtProto`
  section, takes `ServiceUrl` from the connection string when one is configured (otherwise keeps
  the section's value), registers the health check unless disabled, enables telemetry, and
  applies HST-12. *Source:* 0.8 `AtProtoAspireExtensions.cs` · tests
  `AddAtProtoClient_WithAConnectionName_TakesTheInstanceUrlFromTheConnectionString`,
  `AddAtProtoClient_WithAConnectionNameNotConfigured_KeepsTheSection`

### Blazor

<a id="blazor"></a>

There is no `ATProtoNet.Blazor` package (Q-1, [D-09](../06-decisions.md#d-09-blazor-components)).
`samples/ServerIntegrationSample` contains a per-circuit user client accessor, an unstyled login
form and the feed, post, profile and compose widgets as copyable code built on the public API of
`ATProtoNet.AspNetCore` (`IAtProtoClientFactory`, the `ClaimsPrincipal` adapter, the login
routes).

- ~~**HST-33**~~ Superseded by Q-1 (package dropped). The sample's accessor keeps the 0.8
  behaviour as guidance: one client per circuit and user, dispose on user change or sign-out,
  re-check the store on each call, no client for service-auth principals.
- ~~**HST-34**~~ Superseded by Q-1 (package dropped). The sample's login form posts `identifier`
  and `returnUrl` to `{OAuthLoginOptions.RoutePrefix}/login`.
- ~~**HST-35**~~ Superseded by Q-1 (package dropped).

### Aspire.Hosting: shared behaviour

- **HST-36** (MUST) Both resources derive from one base (`AtProtoPdsContainerResourceBase`, a
  container resource with a connection string `http://{host}:{port}`), expose endpoint `http`
  on container port 3000, and declare an HTTP health check on `/xrpc/_health`.
  *Source:* 0.8 `AtProtoPdsHostingExtensions.cs:128-129`
- **HST-37** (MUST) Default image tags are pinned to a tested version (the version CI runs the
  integration tests against), never `latest`, and are overridable per resource. *Source:*
  **Decision**: 0.8 defaults to `latest` while CI pins `pds:0.4.5037`.
- **HST-38** (MUST) Secrets are Aspire parameters marked secret, never plain strings in the
  model: methods that take a secret take an `IResourceBuilder<ParameterResource>`.
  *Source:* **Decision**: 0.8's `WithPlcRecoveryKey(string)` and Bluesky `WithEmail(smtpUrl, …)`
  put secrets into the manifest.
- **HST-39** (MUST) An override replaces the parameter it overrides and removes the generated one
  from the model. A storage mount replaces the default mount for that target rather than adding a
  second one (Docker and Podman reject duplicate targets). *Source:* 0.8 CHANGELOG (mount fix,
  persisted secrets) · tests `WithAdminPassword_OverridesGeneratedParameter`,
  `PublishMode_OverriddenSecretIsDroppedFromTheModel`
- **HST-40** (MUST) All numbers and booleans written into container environments are formatted
  with the invariant culture. *Source:* **Decision**: 0.8 `maxBytes.ToString()` is
  culture-dependent (`AtProtoPdsHostingExtensions.cs:485`).
- **HST-41** (MUST) `WithAtProtoPds(pds)` / `WithAtProtoTranquilPds(pds)` on a consuming resource
  write `AtProto__Pds__Url` (the `http` endpoint), `AtProto__Pds__AdminPassword` and, for
  Tranquil, `AtProto__Pds__Authentication=AdminAccount` and `AtProto__Pds__AdminIdentifier`,
  using `AtProtoConfigurationKeys`; they add a wait on the PDS. They write
  `AtProto__Pds__AllowInsecureHttp=true` in run mode only, never into a publish manifest.
  *Source:* 0.8 `AtProtoPdsHostingExtensions.cs:206-234` · test `Manifest_WiresTheConsumerWithoutAllowingPlaintextHttp`
- **HST-42** (MUST) Method names are consistent across the two resources: the same concept has
  the same name and argument order (`WithEmail`, `WithDataVolume`/`WithBlobVolume` for the
  storage each image has, `WithDevelopmentMode(bool)` only, no `WithProductionMode`).
  *Source:* **Decision** (0.8 diverges)

### Aspire.Hosting: Bluesky PDS

- **HST-43** (MUST) Image `ghcr.io/bluesky-social/pds`; environment `PDS_DATA_DIRECTORY=/pds`,
  `PDS_BLOBSTORE_DISK_LOCATION=/pds/blocks`, `PDS_HOSTNAME`, `PDS_ADMIN_PASSWORD`,
  `PDS_JWT_SECRET`, `PDS_PLC_ROTATION_KEY_K256_PRIVATE_KEY_HEX`; volume `{name}-data` at `/pds`.
  *Source:* 0.8 `AtProtoPdsHostingExtensions.cs:130-148` · tests `AddAtProtoPds_SetsBaselineEnvironment`,
  `Manifest_ConfiguresABlobstore`
- **HST-44** (MUST) In run mode, `PDS_JWT_SECRET` (16 random bytes, hex) and the PLC rotation
  key (32 random bytes, hex) are generated once and persisted to user secrets, and
  `PDS_DEV_MODE=true` is set. In publish mode the two hex secrets are required inputs with **no**
  generated default, `PDS_DEV_MODE` is absent, `PDS_HOSTNAME` is a parameter placeholder (never
  `localhost`), and `pds-admin-password` keeps its `generate` default.
  *Source:* 0.8 · tests `AddAtProtoPds_InPublishMode_DoesNotGenerateHexSecrets`,
  `HexSecretParameterDefault_RefusesToDescribeItselfInAManifest`, `Manifest_DoesNotCarryLocalOnlyDefaults`,
  `Manifest_HexSecretsHaveNoGeneratedDefault`, `Manifest_AdminPasswordKeepsItsGeneratedDefault`

### Aspire.Hosting: Tranquil PDS

- **HST-45** (MUST) Image `atcr.io/tranquil.farm/tranquil-pds`; `SERVER_HOST=[::]`,
  `SERVER_PORT=3000`, `BLOB_STORAGE_PATH=/var/lib/tranquil-pds/blobs` with volume
  `{name}-blobs`; `PDS_HOSTNAME`. *Source:* 0.8 `AtProtoTranquilPdsHostingExtensions.cs:125-137` ·
  tests `AddAtProtoTranquilPds_BindsToAllInterfaces`
- **HST-46** (MUST) By default it provisions a PostgreSQL server `{name}-postgres` and database
  `{name}-db`, waits for the database, and passes `DATABASE_URL` as the Postgres URI expression
  (not the ADO.NET connection string). `WithDatabase(db)` uses the given database and removes the
  generated resources and the wait; `WithDatabaseUrl` passes a URL (or parameter) verbatim.
  *Source:* 0.8 · tests `AddAtProtoTranquilPds_PassesAPostgresUriAsDatabaseUrl`,
  `WithDatabase_UsesTheGivenDatabaseAndDropsTheGeneratedOne`, `WithDatabaseUrl_PassesTheUrlThroughVerbatim`
- **HST-47** (MUST) `JWT_SECRET`, `DPOP_SECRET` and `MASTER_KEY` are 48-character alphanumeric
  secret parameters with a manifest `generate` default. *Source:* 0.8 · tests
  `AddAtProtoTranquilPds_GeneratesSecretsTranquilAccepts`, `AddAtProtoTranquilPds_InPublishMode_StillDescribesItsSecrets`
- **HST-48** (MUST) In run mode only, development defaults are applied, each overridable:
  `INVITE_CODE_REQUIRED=false`, `DISABLE_ACCOUNT_VERIFICATION_GATE=true`,
  `PDS_AGE_ASSURANCE_OVERRIDE=true`, `ALLOW_HTTP_PROXY=true`, `DISABLE_RATE_LIMITING=true`.
  Publish mode applies none. *Source:* 0.8 `AtProtoTranquilPdsHostingExtensions.cs:163-177` · tests
  `AddAtProtoTranquilPds_InRunMode_MakesTheServerUsableWithoutMail`, `AddAtProtoTranquilPds_InPublishMode_AppliesNoRelaxations`,
  `WithInviteCodeRequired_WinsOverTheDevelopmentDefault`
- **HST-49** (MUST) The default admin handle is `pdsadmin.{hostname}` and follows a hostname
  override, including the hostname parameter in publish mode. *Source:* 0.8 · tests
  `WithHostname_MovesTheDerivedAdminHandleWithIt`, `InPublishMode_TheDerivedAdminHandleFollowsTheHostnameParameter`

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `HostingLimits.SessionCacheCapacity` | 1,024 sessions | factory cache of restored sessions (HST-15) |
| `HostingLimits.HealthCheckTimeout` | 5 s | each health check request |
| `StreamWorkerOptions.ShutdownFlushTimeout` | 10 s | cursor flush on host stop (HST-22) |

## Errors

| Failure | Result |
|---|---|
| Invalid options | `OptionsValidationException` at host start, message names the key |
| Duplicate stream worker `StreamId` | `InvalidOperationException` at host start |
| Factory: no stored session | `null` |
| Factory: stored session unreadable | `null`, the entry is left for the store's own corruption handling ([stores.md](stores.md)), logged |
| Stream worker: handler exception | worker stops (default) or skips per `OnHandlerError`; logged |
| Health check failure | the registration's failure status, with the exception attached |

## Conformance and tests

- `AspireManifestTests` stays an integration test gated by `Requirement=AspireManifest`, run in
  CI with `ATPROTO_REQUIRE_INTEGRATION=1` and `TreatNoTestsAsError`
  ([04 — Testing](../04-testing.md#ci-shape)).
- Port these 0.8 test classes as scenarios: `AtProtoBuilderTests` (minus `WithLifetime`),
  `AtProtoClientFactoryTests`, `InMemoryDefaultsWarningTests` (its Spaces cases move to the Spaces
  features' own warnings), `SpaceStoreRegistrationTests`, `PdsAdminExtensionsTests`,
  `IdentityServiceCollectionExtensionsTests`, `AtProtoAspireExtensionsTests`,
  `AtProtoPdsHealthCheckTests`, `AtProtoPdsHostingExtensionsTests`,
  `AtProtoTranquilPdsHostingExtensionsTests`. (`AtProtoUserClientAccessorTests` and
  `LoginFormTests` are dropped with the Blazor package.)
- New tests: Jetstream health check failure status, resilience handlers removed from every named
  client under Aspire service defaults, culture-invariant environment formatting (run under
  `de-DE`), stream worker commit-after-handler and flush-on-stop, duplicate `StreamId`, a worker
  service host referencing only `ATProtoNet` (proves the hosting layer needs no ASP.NET Core).

## Changes from 0.8

| Change | Reason |
|---|---|
| The hosting layer moves from the ASP.NET Core package into the base `ATProtoNet` package | Workers and console apps get DI, factory, stores, stream workers from one package, without the shared framework |
| `Add*`/`Use*` on one builder; no `With*`, no `WithLifetime` | AG-25; immutable clients |
| Factory keyed by `Did`; principal adapter in AspNetCore | Hosting has no ASP.NET dependency; one OAuth-only rule |
| Named-client registry and `ConfigureHttpClients` | One place to configure every SDK client; Aspire removes retries from all |
| Per-feature startup warnings | No central component knows every feature's stores; Spaces features warn for themselves like the rest |
| `AddSpaces(…)`, the space sync worker and Spaces store selection live on the one builder in the base package | Spaces is a core data protocol; 0.8 registered Spaces only from the ASP.NET Core package |
| Stream workers (new) | 0.8 consumers have no hosted-service integration |
| Telemetry | 0.8 has none |
| Pinned image tags; secrets as parameters only; invariant formatting; consistent AppHost names | Reproducibility and security |
| Jetstream health check honours failure status | Bug in 0.8 |
| `ATProtoNet.Blazor` dropped; its accessor, login form and widgets are sample code | Q-1, D-09 |

## Open points

- **Stream worker error policy** default: stop vs skip-and-log. Proposed: stop, because skipping
  silently loses data and at-least-once redelivery after restart is the safe default.
- **`AddAtProtoClient` return type**: `AtProtoBuilder` (proposed) vs Aspire's usual `void`.
  Returning the builder lets service projects chain `AddClientFactory()` and stores.
- **Tranquil `WithS3BlobStorage`** keeps its 0.8 shape (backend switch and credentials as
  parameters). Proposed: keep, with credentials as secret parameters (HST-38).
