# .NET Aspire Integration

The `ATProtoNet.Server` package integrates ATProto.NET into .NET Aspire service defaults: the
`AtProtoClient` bound from configuration (or from the PDS resource's connection string), and a
health check for the PDS. It adds no resilience handler; see [Resilience](#resilience).

## Installation

```bash
dotnet add package ATProtoNet.Server
```

## Quick Start

```csharp
using ATProtoNet.Aspire;

var builder = WebApplication.CreateBuilder(args);

// The client, bound from the "AtProto" section, and the atproto-pds health check
builder.AddAtProtoClient();

var app = builder.Build();

// AtProtoClient is now available via DI
var client = app.Services.GetRequiredService<AtProtoClient>();
```

`AddAtProtoClient()` returns the same `IAtProtoBuilder` as `services.AddAtProto()` (see
[Server Integration](server.md#the-atproto-builder)), so the rest of the registration chains on:

```csharp
builder.AddAtProtoClient()
    .WithOAuth()
    .WithClientFactory()
    .WithFileSessionStore();
```

## Configuration

### Via appsettings.json

The section binds to `AtProtoClientOptions`; `DisableHealthChecks` leaves out the health check.

```json
{
  "AtProto": {
    "InstanceUrl": "https://bsky.social",
    "AutoRefreshSession": true,
    "UserAgent": "MyApp/1.0",
    "RateLimit": { "MaxRetries": 3, "MaxDelay": "00:00:30" },
    "DisableHealthChecks": false
  }
}
```

A bad value, such as an `InstanceUrl` that is not an absolute http(s) URL, stops the host at
startup with an `OptionsValidationException`.

### From the AppHost

An AppHost that references the PDS resource (`WithReference(pds)`, which `WithAtProtoPds(pds)`
does) gives the project the PDS URL as the connection string `ConnectionStrings:pds`. Name it, and
it becomes the `InstanceUrl`:

```csharp
builder.AddAtProtoClient(connectionName: "pds");
```

### Via Code

The callback runs after the configuration section and the connection string, so it wins, even over a `services.AddAtProto(o => …)` made before `AddAtProtoClient()`:

```csharp
builder.AddAtProtoClient(configure: options =>
{
    options.InstanceUrl = "https://my-pds.example.com";
    options.AutoRefreshSession = true;
});
```

### Custom Configuration Section

```csharp
builder.AddAtProtoClient(configurationSectionName: "MyApp:AtProto");
```

## Options Reference

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `InstanceUrl` | `string` | `"https://bsky.social"` | PDS / service instance URL |
| `AutoRefreshSession` | `bool` | `true` | Refresh the session on demand, before expiry and after an `ExpiredToken` |
| `BackgroundRefresh` | `bool` | `false` | Also refresh on a timer while idle |
| `UserAgent` | `string` | `ATProtoNet/<version>` | The `User-Agent` sent with every request |
| `RateLimit:MaxRetries` / `RateLimit:MaxDelay` | `int` / `TimeSpan` | `3` / `30s` | How a `429` is retried |
| `DisableHealthChecks` | `bool` | `false` | Leave out the PDS health check (read by `AddAtProtoClient` only) |

## Health Checks

By default, a health check named `atproto-pds` is registered. It verifies PDS connectivity by
calling `com.atproto.server.describeServer`, reports `Degraded` when the PDS is unreachable, and is
tagged with `atproto` and `ready`. Outside Aspire, add it with `.WithHealthCheck()` on the builder,
which also takes another name, failure status and tags.

```csharp
// Health check is automatically registered
// Access at /health or via Aspire dashboard

// To disable, set AtProto:DisableHealthChecks to true
```

## Resilience

The SDK adds no resilience handler, and must not be combined with one that retries on its own. The
SDK already retries a `429` (`AtProtoClientOptions.RateLimit`), refreshes an expired session, and
signs a fresh DPoP proof for every attempt. A handler below it that retries would send a
non-idempotent `POST` twice (a `createRecord` becomes two records) and resend a request with the
DPoP proof it already carries. A DPoP proof is single-use (its `jti` is spent the first time the
server sees it), so the resent request is refused, or worse accepted by a server that does not
track proofs, which is the replay DPoP exists to stop. The OAuth login's authorization codes and
refresh tokens are single-use too.

The named client `"ATProtoNet"` (`IAtProtoBuilder.HttpClient`) is shared by the registered
`AtProtoClient` and the client factory's per-user clients, whose OAuth sessions sign every request
with a DPoP proof. So there is no retry policy that is safe on it, not even one limited to `GET`:
leave retries to the SDK, and for transient server errors wrap the call in your own retry, where
each attempt is a new request with a new proof (see [Error Handling](error-handling.md#retry-pattern)).

Aspire's `ServiceDefaults` add the standard resilience handler to *every* client through
`ConfigureHttpClientDefaults`, retries, a 10-second attempt timeout (which also cuts off a large
`getRepo` or `getBlob` download) and all. Remove it from the SDK's clients (the method is
experimental in `Microsoft.Extensions.Http.Resilience`, hence the pragma):

```csharp
#pragma warning disable EXTEXP0001
var atproto = builder.AddAtProtoClient();
atproto.HttpClient.RemoveAllResilienceHandlers();
// With the hosted OAuth login (WithOAuth), its client too:
builder.Services.AddHttpClient(AtProtoOAuthExtensions.HttpClientName).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
```

## What It Registers

`AddAtProtoClient()` registers:

1. **`AtProtoClientOptions`** through `IOptions<T>`, bound from the configuration section (and the connection string), validated at startup
2. **Named HttpClient** (`AtProtoServiceCollectionExtensions.HttpClientName`, `"ATProtoNet"`) with the SDK's connection settings (response decompression, a 5-minute connection lifetime), exposed as `IAtProtoBuilder.HttpClient`
3. **`AtProtoClient`** as a singleton (`.WithLifetime(...)` changes it)
4. **Health check** (`atproto-pds`) verifying PDS connectivity, unless disabled

## Usage in Services

```csharp
public class MyService
{
    private readonly AtProtoClient _client;

    public MyService(AtProtoClient client) => _client = client;

    public async Task PostUpdateAsync(string text)
    {
        if (!_client.IsAuthenticated)
            await _client.LoginAsync("bot.bsky.social", "app-password");

        await _client.Bsky.PostAsync(text);
    }
}
```

## With Aspire AppHost

### Adding a PDS Container

The `ATProtoNet.Aspire.Hosting` package lets you add the official Bluesky PDS container directly to your Aspire AppHost, eliminating the need for manual Docker/Podman setup:

```bash
dotnet add package ATProtoNet.Aspire.Hosting
```

```csharp
using ATProtoNet.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Add a PDS container with auto-generated secrets and dev mode
var pds = builder.AddAtProtoPds("pds");

// Wire it to your API project — reference, admin configuration, and WaitFor in one call
var api = builder.AddProject<Projects.MyApi>("api")
    .WithAtProtoPds(pds);

builder.Build().Run();
```

The PDS container starts with:
- **Auto-generated** admin password (Aspire secret parameter), JWT secret, and PLC rotation key
- **Dev mode** enabled by default
- **Persistent volume** for PDS data at `/pds`
- **`IResourceWithConnectionString`** support — access the PDS URL via `builder.Configuration.GetConnectionString("pds")`

#### Configuration Options

```csharp
builder.AddAtProtoPds("pds", port: 2583, tag: "0.4")
    .WithHostname("pds.example.com")
    .WithPlcUrl("https://plc.directory")
    .WithAppView("https://api.bsky.app", "did:web:api.bsky.app")
    .WithCrawlers("https://bsky.network")
    .WithReportService("https://mod.bsky.app")
    .WithBlobUploadLimit(10 * 1024 * 1024)
    .WithEmail("smtps://user:pass@smtp.example.com", "noreply@example.com")
    .WithProductionMode();
```

| Method | Description |
|--------|-------------|
| `AddAtProtoPds(name, port?, tag?)` | Add a PDS container with optional port and image tag |
| `WithAtProtoPds(pds)` | *(on a project)* reference the PDS, supply admin configuration, and wait for its health check |
| `WithHostname(hostname)` | Set the PDS hostname (`PDS_HOSTNAME`); also takes a `ParameterResource` |
| `WithHandleDomains(domains)` | Set the domains new handles may be created under |
| `WithPlcUrl(url)` | Set the PLC directory URL |
| `WithAppView(url, did?)` | Configure Bluesky app view URL and DID |
| `WithCrawlers(crawlers)` | Set relay crawler URLs |
| `WithProductionMode()` | Disable dev mode for production deployments |
| `WithInviteCodeRequired()` | Gate signups behind invite codes |
| `WithBlobUploadLimit(bytes)` | Set max blob upload size (default: 5 MB) |
| `WithReportService(url, did?)` | Configure moderation/report service |
| `WithEmail(smtpUrl, fromAddress)` | Configure SMTP email settings |
| `WithAdminPassword(param)` / `WithJwtSecret(param)` / `WithPlcRotationKey(param)` | Supply secrets instead of the generated parameters |
| `WithDataVolume(name?)` / `WithDataBindMount(path)` | Replace the default `/pds` data mount |

See [Managed PDS](managed-pds.md) for what these settings mean in run mode versus a published
manifest, and for administering the running server with `PdsAdminClient`.

#### Tranquil PDS

The same package hosts [Tranquil](https://tangled.org/tranquil.farm/tranquil-pds), a
community PDS implementation that is a superset of the reference server:

```csharp
var pds = builder.AddAtProtoTranquilPds("pds");

builder.AddProject<Projects.MyApi>("api")
    .WithAtProtoTranquilPds(pds);
```

| Method | Description |
|--------|-------------|
| `AddAtProtoTranquilPds(name, port?, tag?)` | Add a Tranquil PDS container, plus the PostgreSQL server it needs |
| `WithAtProtoTranquilPds(pds)` | *(on a project)* reference the PDS, supply admin configuration, and wait for its health check |
| `WithDatabase(database)` / `WithDatabaseUrl(url)` | Use an existing PostgreSQL database instead of the generated one |
| `WithAdminAccount(handle, password?)` | Name the account the server is administered through |
| `WithDevelopmentMode(enabled?)` | Turn the local-development relaxations on or off |
| `WithHostname(hostname)` / `WithHandleDomains(domains)` | Public hostname and handle domains |
| `WithJwtSecret(param)` / `WithDPoPSecret(param)` / `WithMasterKey(param)` | Supply secrets instead of the generated parameters |
| `WithPlcRecoveryKey(didKey)` | Register an operator-held PLC recovery key (a *public* `did:key`) |
| `WithBlobVolume(name?)` / `WithBlobBindMount(path)` / `WithS3BlobStorage(bucket, endpoint?)` | Where blobs are stored |
| `WithPlcUrl(url)` / `WithCrawlers(urls)` / `WithReportService(url, did?)` / `WithBlobUploadLimit(bytes)` / `WithInviteCodeRequired(required?)` / `WithEmail(from, host, port?, user?, password?)` | As above |

Both resources derive from `AtProtoPdsContainerResourceBase`. `WithHostname` and
`WithJwtSecret` are the same generic methods for either server and return the concrete
builder, so they chain into the server-specific ones. Write your own AppHost helpers
against the base type (`where T : AtProtoPdsContainerResourceBase`) to cover both.

Two differences matter. Tranquil needs PostgreSQL, which the resource provisions for you;
and it has no server-wide admin password — administration goes through an *account* the
server has flagged as an administrator. `WithAtProtoTranquilPds` configures
`PdsAdminClient` for that automatically, but the account itself has to be created once by
your application. See [Managed PDS](managed-pds.md#tranquil-pds).

### Service Defaults Only

If you already have an external PDS and just want service defaults:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.MyApi>("api");

builder.Build().Run();
```

In your API project:

```csharp
builder.AddServiceDefaults();
builder.AddAtProtoClient();   // and remove the default resilience handler, as above
```

## Next Steps

- [Getting Started](getting-started.md) — Core SDK usage
- [Server Integration](server.md) — Backend AT Proto access patterns
- [Managed PDS](managed-pds.md) — run the Bluesky PDS container and administer it
