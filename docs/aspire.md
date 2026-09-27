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
using ATProtoNet.Server;

var builder = WebApplication.CreateBuilder(args);

// The client, bound from the "AtProto" section, and the atproto-pds health check
builder.AddAtProtoClient();

var app = builder.Build();

// AtProtoClient is now available via DI
var client = app.Services.GetRequiredService<AtProtoClient>();
```

`AddAtProtoClient()` returns the same `IAtProtoBuilder` as `services.AddAtProto()` (see
[ASP.NET Core: The AtProto Builder](aspnet-core.md#the-atproto-builder)), so the rest of the
registration chains on:

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
// Outside Aspire: add the same check to the builder
builder.Services.AddAtProto().WithHealthCheck();

var app = builder.Build();
app.MapHealthChecks("/health");   // or read it on the Aspire dashboard
```

Set `AtProto:DisableHealthChecks` to `true` to leave the check out under Aspire.

## Resilience

The SDK adds no resilience handler, and must not be combined with one that retries on its own: it
already retries a `429`, refreshes an expired session and signs a fresh DPoP proof for every
attempt, and a retry below it resends a non-idempotent `POST` and a single-use DPoP proof (see
[ASP.NET Core: HTTP handlers and resilience](aspnet-core.md#http-handlers-and-resilience)). For
transient server errors, wrap the call in your own retry (see
[Error Handling](error-handling.md#retry-pattern)).

Aspire's `ServiceDefaults` add the standard resilience handler to *every* client through
`ConfigureHttpClientDefaults`, retries, a 10-second attempt timeout (which also cuts off a large
`getRepo` or `getBlob` download) and all. Remove it from the SDK's clients (the method is
experimental in `Microsoft.Extensions.Http.Resilience`, hence the pragma):

```csharp
using ATProtoNet.Aspire;
using ATProtoNet.Server.Authentication;

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

## The AppHost side

`ATProtoNet.Aspire.Hosting` adds a PDS container (the reference Bluesky PDS, or Tranquil PDS) to
the AppHost and wires it to your projects with `WithAtProtoPds(pds)`, which also hands the project
the PDS URL as the connection string `pds` (see [From the AppHost](#from-the-apphost)). The
resources, their configuration methods, and administering the running server with
`PdsAdminClient` are on [Managed PDS](managed-pds.md).

With a PDS that runs elsewhere, the project needs nothing from the AppHost: add the client to the
service defaults as usual.

```csharp partial
builder.AddServiceDefaults();   // the template's ServiceDefaults project
builder.AddAtProtoClient();     // and remove the default resilience handler, as above
```

## Next Steps

- [Getting Started](getting-started.md) — Core SDK usage
- [ASP.NET Core](aspnet-core.md) — the rest of the registration
- [Managed PDS](managed-pds.md) — run the Bluesky PDS container and administer it
