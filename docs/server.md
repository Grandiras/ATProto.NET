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

### Entity Framework Core Token Store

ATProtoNet provides a ready-made EF Core implementation, included in the `ATProtoNet.Server` package. (It previously shipped as the separate `ATProtoNet.Server.EntityFrameworkCore` package, which has been merged into `ATProtoNet.Server`; the `ATProtoNet.Server.EntityFrameworkCore` namespace and the `AddAtProtoEfCoreTokenStore<T>()` API are unchanged.)

```bash
dotnet add package ATProtoNet.Server
```

Register with your `DbContext`:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=app.db"));

// Add the EF Core token store
builder.Services.AddAtProtoEfCoreTokenStore<AppDbContext>();
```

Your `DbContext` must inherit from `AtProtoTokenDbContext` or include the `AtProtoTokenEntity` set:

```csharp
public class AppDbContext : AtProtoTokenDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Your other DbSets...
}
```

Or add the entity to an existing context:

```csharp
public class AppDbContext : DbContext
{
    public DbSet<AtProtoTokenEntity> AtProtoTokens => Set<AtProtoTokenEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new AtProtoTokenEntityConfiguration());
    }
}
```

The `EfCoreAtProtoTokenStore` handles encryption via ASP.NET Core Data Protection before persisting DPoP keys.

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

## XRPC Endpoint Handlers

ATProtoNet.Server supports defining server-side XRPC endpoint handlers using a DI-friendly interface pattern. This is useful for building AT Protocol services (PDS, appview, relay, etc.) that expose `/xrpc/{nsid}` routes.

### Defining a Query Endpoint

```csharp
[XrpcEndpoint(Nsid = "com.example.getStatus")]
public class GetStatusEndpoint : IXrpcQuery<StatusOutput>
{
    public string Nsid => "com.example.getStatus";

    public Task<StatusOutput> HandleAsync(HttpContext context, CancellationToken ct)
    {
        return Task.FromResult(new StatusOutput { Status = "ok" });
    }
}
```

For queries with parameters:

```csharp
[XrpcEndpoint(Nsid = "app.bsky.feed.getTimeline")]
public class GetTimelineEndpoint : IXrpcQuery<TimelineParams, TimelineOutput>
{
    public string Nsid => "app.bsky.feed.getTimeline";

    public Task<TimelineOutput> HandleAsync(TimelineParams parameters, HttpContext context, CancellationToken ct)
    {
        // parameters are bound from ?key=value query string
        // ...
    }
}
```

### Defining a Procedure Endpoint

```csharp
[XrpcEndpoint(Nsid = "com.atproto.repo.createRecord")]
public class CreateRecordEndpoint : IXrpcProcedure<CreateRecordInput, CreateRecordOutput>
{
    public string Nsid => "com.atproto.repo.createRecord";

    public Task<CreateRecordOutput> HandleAsync(CreateRecordInput input, HttpContext context, CancellationToken ct)
    {
        // input is deserialized from the JSON request body
        // ...
    }
}
```

For procedures that return no output:

```csharp
[XrpcEndpoint(Nsid = "com.example.ping")]
public class PingEndpoint : IXrpcProcedureVoid<PingInput>
{
    public string Nsid => "com.example.ping";

    public Task HandleAsync(PingInput input, HttpContext context, CancellationToken ct)
    {
        // ...
        return Task.CompletedTask;
    }
}
```

### Registering Endpoints

Register individual endpoints or scan an entire assembly:

```csharp
// Individual registration
builder.Services.AddXrpcEndpoint<GetStatusEndpoint>();

// Assembly scanning (finds all [XrpcEndpoint]-attributed handlers)
builder.Services.AddXrpcEndpointsFromAssembly(typeof(Program).Assembly);

var app = builder.Build();

// Map all registered endpoints as /xrpc/{nsid} routes
app.MapXrpcEndpoints();
```

Query endpoints map as `GET /xrpc/{nsid}`, procedures map as `POST /xrpc/{nsid}`. Invalid or missing request bodies return a `400 Bad Request` with an XRPC-style error response.

For a comprehensive guide covering dependency injection, combining with PDS hosting, and more examples, see [XRPC Endpoint Handlers](xrpc-handlers.md).
