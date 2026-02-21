# Server-Side AT Protocol Integration

ATProtoNet.Server provides tools for integrating AT Protocol access into ASP.NET Core applications.
It works alongside ATProtoNet.Blazor to enable authenticated backend API calls using stored OAuth tokens.

## Quick Start

### 1. Register Services

```csharp
// Program.cs
builder.Services.AddAuthentication("Cookies").AddCookie();
builder.Services.AddAtProtoAuthentication();  // Blazor OAuth login
builder.Services.AddAtProtoServer();           // Backend AT Proto access
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapAtProtoOAuth();
```

### 2. Use in API Endpoints

```csharp
app.MapGet("/api/profile", async (ClaimsPrincipal user, IAtProtoClientFactory factory) =>
{
    await using var client = await factory.CreateClientForUserAsync(user);
    if (client is null) return Results.Unauthorized();

    var profile = await client.Bsky.Actor.GetProfileAsync(client.Session!.Did);
    return Results.Ok(new { profile.DisplayName, profile.Handle, profile.Description });
}).RequireAuthorization();
```

### 3. Use in Blazor Components

```razor
@page "/profile"
@using ATProtoNet.Server.Services
@attribute [Authorize]
@inject IAtProtoClientFactory ClientFactory

@code {
    [CascadingParameter]
    private Task<AuthenticationState> AuthState { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        var auth = await AuthState;
        await using var client = await ClientFactory.CreateClientForUserAsync(auth.User);
        if (client is null) return;

        var profile = await client.Bsky.Actor.GetProfileAsync(client.Session!.Did);
        // Use profile data...
    }
}
```

## How It Works

```
┌───────────────────── Browser ─────────────────────┐
│  User clicks "Sign In" → <LoginForm> submits to   │
│  GET /atproto/login?handle=alice.bsky.social       │
└──────────────┬────────────────────────────────────┘
               │
┌──────────────▼────────────────────────────────────┐
│               ASP.NET Core Server                  │
│                                                    │
│  1. /atproto/login → AtProtoOAuthService           │
│     → Resolves PDS, starts OAuth, redirects        │
│                                                    │
│  2. /atproto/callback ← Authorization Server       │
│     → Exchanges code for DPoP-bound tokens         │
│     → Creates claims (DID, handle, PDS URL)        │
│     → Issues cookie via SignInAsync()               │
│     → Stores tokens in IAtProtoTokenStore ────┐    │
│                                               │    │
│  3. API endpoint or Blazor component           │    │
│     → IAtProtoClientFactory                    │    │
│       → Reads DID from cookie claims           │    │
│       → Looks up tokens ◄─────────────────────┘    │
│       → Reconstructs DPoP key                      │
│       → Creates authenticated AtProtoClient        │
│       → Calls AT Proto APIs on user's PDS          │
└────────────────────────────────────────────────────┘
```

Key security points:
- **No tokens in the browser.** OAuth tokens and DPoP private keys stay server-side.
- **Cookie is encrypted** by ASP.NET Core Data Protection.
- **DPoP-bound tokens** — even if intercepted, tokens can't be used without the private key.
- **Per-request clients** — `IAtProtoClientFactory` creates a new `AtProtoClient` per call, avoiding token leakage between requests.

## `IAtProtoClientFactory`

Creates authenticated `AtProtoClient` instances from stored OAuth tokens.

```csharp
public interface IAtProtoClientFactory
{
    Task<AtProtoClient?> CreateClientForUserAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
}
```

Returns `null` when:
- The user has no `did` claim (not authenticated)
- No tokens are stored for the user's DID (not logged in via OAuth, or tokens expired/removed)

The returned client is **disposable** — always use `await using`:

```csharp
await using var client = await factory.CreateClientForUserAsync(user);
```

## `IAtProtoTokenStore`

Interface for server-side OAuth token storage. Tokens are stored keyed by DID.

```csharp
public interface IAtProtoTokenStore
{
    Task StoreAsync(string did, AtProtoTokenData data, CancellationToken ct = default);
    Task<AtProtoTokenData?> GetAsync(string did, CancellationToken ct = default);
    Task RemoveAsync(string did, CancellationToken ct = default);
}
```

### Default: `FileAtProtoTokenStore`

The default implementation stores tokens as encrypted files using ASP.NET Core Data Protection.
Tokens persist across app restarts. Suitable for single-server deployments.

```csharp
// Default — stores in {LocalApplicationData}/ATProtoNet/tokens/
builder.Services.AddAtProtoServer();

// Custom directory
builder.Services.AddAtProtoServer("/var/data/atproto-tokens");
```

### In-Memory Store

For development or testing, use the in-memory store (tokens are lost on restart):

```csharp
builder.Services.AddAtProtoServer<InMemoryAtProtoTokenStore>();
```

### Custom Implementation

For production, implement `IAtProtoTokenStore` with a durable, encrypted store:

```csharp
public class DatabaseTokenStore : IAtProtoTokenStore
{
    private readonly MyDbContext _db;

    public DatabaseTokenStore(MyDbContext db) => _db = db;

    public async Task StoreAsync(string did, AtProtoTokenData data, CancellationToken ct)
    {
        // Encrypt data.DPoPPrivateKey before storing!
        var entity = await _db.TokenEntries.FindAsync([did], ct);
        if (entity is null)
        {
            entity = new TokenEntry { Did = did };
            _db.TokenEntries.Add(entity);
        }
        entity.SetFromTokenData(data); // Map and encrypt
        await _db.SaveChangesAsync(ct);
    }

    // ... GetAsync, RemoveAsync
}

// Register:
builder.Services.AddAtProtoServer<DatabaseTokenStore>();
```

> **Security:** `AtProtoTokenData.DPoPPrivateKey` contains an unencrypted PKCS#8 private key.
> Always encrypt it before persisting to a database or external store.

## Standalone Client (Server-to-Server)

For bot or service scenarios where you authenticate with app passwords (not user OAuth):

```csharp
builder.Services.AddAtProto(options =>
{
    options.InstanceUrl = "https://bsky.social";
});

// In a controller or service:
public class MyService
{
    private readonly AtProtoClient _client;

    public MyService(AtProtoClient client) => _client = client;

    public async Task PostAsync(string text)
    {
        await _client.LoginAsync("my-bot.bsky.social", "app-password-here");
        await _client.Bsky.Feed.CreatePostAsync(text);
    }
}
```

## Sample

See [samples/ServerIntegrationSample/](../samples/ServerIntegrationSample/) for a complete working example with:
- Blazor OAuth login
- Profile page using `IAtProtoClientFactory`
- Timeline page with live AT Proto data
- Minimal API endpoints (`/api/profile`, `/api/timeline`)
