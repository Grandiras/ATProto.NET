# Installation & Setup

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later
- A PDS (Personal Data Server) account — either your own or a hosted one like [bsky.social](https://bsky.social)

## Install the Package

```bash
# Core SDK
dotnet add package ATProtoNet

# ASP.NET Core integration (optional)
dotnet add package ATProtoNet.Server

# Blazor components (optional)
dotnet add package ATProtoNet.Blazor
```

## Create a Client

The simplest way to create a client:

```csharp
using ATProtoNet;

var client = new AtProtoClientBuilder()
    .WithInstanceUrl("https://your-pds.example.com")
    .Build();
```

### Builder Options

```csharp
var client = new AtProtoClientBuilder()
    .WithInstanceUrl("https://your-pds.example.com")  // Required: PDS URL
    .WithAutoRefreshSession(true)                       // Auto-refresh tokens (default: true)
    .WithSessionStore(new FileSessionStore("sess.json"))// Custom session persistence
    .WithHttpClient(httpClient)                         // Custom HttpClient
    .WithLoggerFactory(loggerFactory)                   // Logging
    .Build();
```

### Direct Construction

```csharp
var client = new AtProtoClient(new AtProtoClientOptions
{
    InstanceUrl = "https://your-pds.example.com",
    AutoRefreshSession = true,
});
```

## Authenticate

### Login with Handle & Password

```csharp
var session = await client.LoginAsync("alice.example.com", "app-password");

Console.WriteLine($"Logged in as: {session.Handle}");
Console.WriteLine($"DID: {session.Did}");
```

> **Tip:** Use [App Passwords](https://bsky.app/settings/app-passwords) instead of your main password.

### Check Authentication State

```csharp
if (client.IsAuthenticated)
{
    Console.WriteLine($"Authenticated as {client.Handle} ({client.Did})");
}
```

### Session Properties

After login, you can access:

```csharp
client.Did       // "did:plc:abc123..."
client.Handle    // "alice.example.com"
client.Session   // Full Session object with tokens, email, etc.
```

## What's Next?

- [Custom Lexicon Records](custom-records.md) — Build your own AT Protocol app
- [Session Management](session-management.md) — Token refresh, persistence, resume
- [ASP.NET Core](aspnet-core.md) — Use in web applications
