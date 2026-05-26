# PDS Hosting

The `ATProtoNet.Pds` package lets you build your own AT Protocol Personal Data Server (PDS) using ASP.NET Core. It provides core business logic for account management, session handling, record CRUD, and blob operations.

## Installation

```bash
dotnet add package ATProtoNet.Pds
```

## Quick Start

```csharp
using ATProtoNet.Pds;

var builder = WebApplication.CreateBuilder(args);

// Register PDS services with in-memory stores (suitable for development)
builder.Services.AddAtProtoPds(options =>
{
    options.Hostname = "my-pds.example.com";
    options.PublicUrl = "https://my-pds.example.com";
    options.OpenRegistration = true;
});

var app = builder.Build();

// Map all AT Protocol XRPC endpoints
app.MapAtProtoPds();

app.Run();
```

This maps the following XRPC endpoints:

### Server Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `com.atproto.server.createAccount` | POST | Create a new account |
| `com.atproto.server.createSession` | POST | Login (create session) |
| `com.atproto.server.getSession` | GET | Get current session info |
| `com.atproto.server.refreshSession` | POST | Refresh session tokens |
| `com.atproto.server.describeServer` | GET | Server description |

### Repository Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `com.atproto.repo.createRecord` | POST | Create a record |
| `com.atproto.repo.getRecord` | GET | Get a record |
| `com.atproto.repo.putRecord` | POST | Create or update a record |
| `com.atproto.repo.deleteRecord` | POST | Delete a record |
| `com.atproto.repo.listRecords` | GET | List records with pagination |

### Blob Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `com.atproto.repo.uploadBlob` | POST | Upload a blob |
| `com.atproto.sync.getBlob` | GET | Download a blob |

## Configuration

```csharp
builder.Services.AddAtProtoPds(options =>
{
    options.Hostname = "my-pds.example.com";
    options.PublicUrl = "https://my-pds.example.com";
    options.OpenRegistration = false;       // Require invite codes for signup
    options.AvailableUserDomains = [".my-pds.example.com"];
    options.ContactEmail = "admin@my-pds.example.com";
});
```

## Custom Store Implementations

The in-memory stores are suitable for development and testing. For production, implement `IAccountStore` and `IRepoStore`:

### Account Store

```csharp
public class DatabaseAccountStore : IAccountStore
{
    private readonly MyDbContext _db;

    public DatabaseAccountStore(MyDbContext db) => _db = db;

    public async Task<AccountInfo?> GetByDidAsync(string did, CancellationToken ct)
    {
        var entity = await _db.Accounts.FindAsync([did], ct);
        return entity?.ToAccountInfo();
    }

    public async Task<AccountInfo?> GetByHandleAsync(string handle, CancellationToken ct)
    {
        var entity = await _db.Accounts.FirstOrDefaultAsync(a => a.Handle == handle, ct);
        return entity?.ToAccountInfo();
    }

    public async Task CreateAsync(AccountInfo account, CancellationToken ct)
    {
        _db.Accounts.Add(AccountEntity.FromAccountInfo(account));
        await _db.SaveChangesAsync(ct);
    }

    // ... VerifyPasswordAsync, etc.
}
```

### Repository Store

```csharp
public class DatabaseRepoStore : IRepoStore
{
    // Store records, blobs, with cursor-based pagination support
    // ...
}
```

### Register Custom Stores

```csharp
builder.Services.AddAtProtoPds<DatabaseAccountStore, DatabaseRepoStore>(options =>
{
    options.Hostname = "my-pds.example.com";
});
```

## Authentication

The PDS uses JWT Bearer tokens for authentication. `PdsSessionService` handles:

- Token issuance with HMAC-SHA256 signing
- Token validation on protected endpoints
- Session refresh

Clients authenticate by calling `com.atproto.server.createSession` with handle/password, then include the returned JWT in the `Authorization: Bearer` header on subsequent requests.

## Security

- Passwords are hashed using PBKDF2 with 100,000 iterations
- Record CIDs are computed using SHA-256
- Repo ownership is verified on all write operations — users can only write to their own repository
- Blob uploads require authentication

## Example: PDS with Custom Auth

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAtProtoPds(options =>
{
    options.Hostname = "my-pds.example.com";
    options.PublicUrl = "https://my-pds.example.com";
});

var app = builder.Build();

app.MapAtProtoPds();

// You can also add custom endpoints alongside the PDS
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
```

## Combining with ATProtoNet Client

You can test your PDS using the ATProto.NET client library:

```csharp
var client = new AtProtoClientBuilder()
    .WithInstanceUrl("https://my-pds.example.com")
    .Build();

// Create an account on your PDS
var session = await client.Server.CreateAccountAsync(
    email: "alice@example.com",
    handle: "alice.my-pds.example.com",
    password: "secure-password");

// Login
await client.LoginAsync("alice.my-pds.example.com", "secure-password");

// Create records on your PDS
var todos = client.GetCollection<TodoItem>("com.example.todo.item");
await todos.CreateAsync(new TodoItem { Title = "Test PDS" });
```

## Next Steps

- [XRPC Endpoint Handlers](xrpc-handlers.md) — Add custom XRPC endpoints alongside PDS routes
- [Server Integration](server.md) — Backend AT Proto access patterns
- [Aspire](aspire.md) — Deploy with .NET Aspire
