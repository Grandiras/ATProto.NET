# Session Management

ATProto.NET handles AT Protocol session lifecycle including authentication, token refresh, and persistence.

## Authentication Methods

ATProto.NET supports two authentication methods:

| Method | Use Case | Security |
|--------|----------|----------|
| **App Password** | CLI tools, scripts, server-to-server | Simple, direct |
| **OAuth** | User-facing web apps, mobile apps | DPoP-bound tokens, no password handling |

For OAuth, see the [OAuth Authentication guide](oauth.md).

## App Password Authentication

### Login

```csharp
var session = await client.LoginAsync("alice.example.com", "app-password");

Console.WriteLine($"DID: {session.Did}");
Console.WriteLine($"Handle: {session.Handle}");
Console.WriteLine($"Email: {session.Email}");
```

### Two-Factor Authentication

```csharp
var session = await client.LoginAsync(
    "alice.example.com",
    "app-password",
    authFactorToken: "123456");
```

### Check State

```csharp
client.IsAuthenticated  // bool
client.Did              // string? — authenticated user's DID
client.Handle           // string? — authenticated user's handle
client.Session          // Session? — full session object
```

## Automatic Token Refresh

By default, ATProto.NET automatically refreshes the access token before it expires:

```csharp
var client = new AtProtoClientBuilder()
    .WithInstanceUrl("https://your-pds.example.com")
    .WithAutoRefreshSession(true)  // Default: true
    .Build();
```

Access tokens are typically valid for ~2 hours. The SDK refreshes 5 minutes before expiry.

### Manual Refresh

```csharp
await client.RefreshSessionAsync();
```

## Session Persistence

### Built-in In-Memory Store

The default session store keeps tokens in memory only — they're lost when the process exits.

### Custom Session Store

Implement `ISessionStore` to persist sessions across app restarts:

```csharp
using ATProtoNet.Auth;

public class FileSessionStore : ISessionStore
{
    private readonly string _path;

    public FileSessionStore(string path) => _path = path;

    public async Task SaveAsync(Session session, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(session);
        await File.WriteAllTextAsync(_path, json, ct);
    }

    public async Task<Session?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return null;
        var json = await File.ReadAllTextAsync(_path, ct);
        return JsonSerializer.Deserialize<Session>(json);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }
}
```

Register it:

```csharp
var client = new AtProtoClientBuilder()
    .WithInstanceUrl("https://your-pds.example.com")
    .WithSessionStore(new FileSessionStore("session.json"))
    .Build();
```

### Resume a Session

Load a previously saved session and resume it:

```csharp
var store = new FileSessionStore("session.json");
var savedSession = await store.LoadAsync();

if (savedSession is not null)
{
    await client.ResumeSessionAsync(savedSession);
    // Client is now authenticated — validates token, refreshes if expired
}
else
{
    await client.LoginAsync("alice.example.com", "app-password");
}
```

`ResumeSessionAsync` validates the access token and automatically refreshes if it has expired.

## Logout

```csharp
await client.LogoutAsync();
// Session is destroyed on server, tokens cleared, session store cleared
```

## Session Properties

The `Session` object contains:

| Property | Type | Description |
|----------|------|-------------|
| `Did` | `string` | User's DID |
| `Handle` | `string` | User's handle |
| `AccessJwt` | `string` | Access token (short-lived) |
| `RefreshJwt` | `string` | Refresh token (longer-lived) |
| `Email` | `string?` | User's email address |
| `EmailConfirmed` | `bool?` | Whether email is confirmed |
| `EmailAuthFactor` | `bool?` | Whether email 2FA is enabled |
| `DidDoc` | `JsonElement?` | User's DID document |
| `Active` | `bool?` | Whether the account is active |
| `Status` | `string?` | Account status |

## ASP.NET Core Integration

For web apps, use scoped clients with per-request sessions:

```csharp
builder.Services.AddAtProtoScoped(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});
```

See [ASP.NET Core Integration](aspnet-core.md) for more details.

## OAuth Sessions

OAuth sessions are managed differently from app password sessions. They use DPoP-bound tokens and are created via the OAuth flow rather than direct password login.

### Apply an OAuth Session

```csharp
var session = await oauthClient.CompleteAuthorizationAsync(code, state, issuer);

// Apply to AtProtoClient — sets PDS URL, DPoP tokens, and creates Session
await client.ApplyOAuthSessionAsync(session);

// Client is now authenticated
Console.WriteLine($"Authenticated as {client.Handle}");
```

### Refresh OAuth Tokens

```csharp
var newTokens = await oauthClient.RefreshTokensAsync(session);
session.AccessToken = newTokens.AccessToken;
if (newTokens.RefreshToken is not null)
    session.RefreshToken = newTokens.RefreshToken;
```

### Dynamic PDS

Change the PDS URL at runtime:

```csharp
client.SetPdsUrl("https://different-pds.example.com");
```

For full OAuth documentation, see [OAuth Authentication](oauth.md).
