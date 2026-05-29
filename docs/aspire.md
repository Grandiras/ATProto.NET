# .NET Aspire Integration

The `ATProtoNet.Server` package integrates ATProto.NET into .NET Aspire service defaults with health checks, resilience policies, and configuration binding. (This client integration previously shipped as the separate `ATProtoNet.Aspire` package; it has been merged into `ATProtoNet.Server`. The `ATProtoNet.Aspire` namespace and the `AddAtProtoClient()` API are unchanged.)

## Installation

```bash
dotnet add package ATProtoNet.Server
```

## Quick Start

```csharp
using ATProtoNet.Aspire;

var builder = WebApplication.CreateBuilder(args);

// One-line registration with health checks and resilience
builder.AddAtProtoClient();

var app = builder.Build();

// AtProtoClient is now available via DI
var client = app.Services.GetRequiredService<AtProtoClient>();
```

## Configuration

### Via appsettings.json

```json
{
  "AtProto": {
    "InstanceUrl": "https://bsky.social",
    "RelayUrl": "wss://bsky.network",
    "AutoRefreshSession": true,
    "DisableHealthChecks": false,
    "DisableResilience": false
  }
}
```

### Via Code

```csharp
builder.AddAtProtoClient(configureSettings: settings =>
{
    settings.InstanceUrl = "https://my-pds.example.com";
    settings.RelayUrl = "wss://bsky.network";
    settings.AutoRefreshSession = true;
    settings.DisableHealthChecks = false;
    settings.DisableResilience = false;
});
```

### Custom Configuration Section

```csharp
builder.AddAtProtoClient(configurationSectionName: "MyApp:AtProto");
```

## Settings Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `InstanceUrl` | `string` | `"https://bsky.social"` | PDS / service instance URL |
| `RelayUrl` | `string?` | `"wss://bsky.network"` | WebSocket relay URL for firehose |
| `AutoRefreshSession` | `bool` | `true` | Auto-refresh session tokens |
| `DisableHealthChecks` | `bool` | `false` | Disable PDS connectivity health check |
| `DisableResilience` | `bool` | `false` | Disable standard HTTP resilience |

## Health Checks

By default, a health check named `atproto-pds` is registered. It verifies PDS connectivity by calling `com.atproto.server.describeServer`. The check is tagged with `atproto` and `ready`.

```csharp
// Health check is automatically registered
// Access at /health or via Aspire dashboard

// To disable:
builder.AddAtProtoClient(configureSettings: s => s.DisableHealthChecks = true);
```

## Resilience

Standard HTTP resilience (retry with exponential backoff, circuit breaker) is added via `Microsoft.Extensions.Http.Resilience`. This protects against transient network failures.

```csharp
// To disable resilience handlers:
builder.AddAtProtoClient(configureSettings: s => s.DisableResilience = true);
```

## What It Registers

`AddAtProtoClient()` registers:

1. **Named HttpClient** (`"ATProtoNet"`) with a `User-Agent` header and optional resilience handler
2. **`AtProtoClient`** as a singleton, configured from `IConfiguration` and `IHttpClientFactory`
3. **Health check** (`atproto-pds`) verifying PDS connectivity

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

        await _client.PostAsync(text);
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

// Wire it to your API project
var api = builder.AddProject<Projects.MyApi>("api")
    .WithReference(pds);

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
| `WithHostname(hostname)` | Set the PDS hostname (`PDS_HOSTNAME`) |
| `WithPlcUrl(url)` | Set the PLC directory URL |
| `WithAppView(url, did?)` | Configure Bluesky app view URL and DID |
| `WithCrawlers(crawlers)` | Set relay crawler URLs |
| `WithProductionMode()` | Disable dev mode for production deployments |
| `WithBlobUploadLimit(bytes)` | Set max blob upload size (default: 5 MB) |
| `WithReportService(url, did?)` | Configure moderation/report service |
| `WithEmail(smtpUrl, fromAddress)` | Configure SMTP email settings |

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
builder.AddAtProtoClient();
```

## Next Steps

- [Getting Started](getting-started.md) — Core SDK usage
- [Server Integration](server.md) — Backend AT Proto access patterns
- [PDS Hosting](pds.md) — Build your own PDS
