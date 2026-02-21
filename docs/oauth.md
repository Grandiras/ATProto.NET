# OAuth Authentication

ATProto.NET supports the [AT Protocol OAuth specification](https://atproto.com/specs/oauth) for secure, standards-based authentication. This includes DPoP (RFC 9449), Pushed Authorization Requests (RFC 9126), and PKCE (RFC 7636).

## Overview

OAuth is the recommended authentication method for AT Protocol applications — especially user-facing apps where you don't want to handle passwords directly. The flow works as follows:

1. Your app redirects the user to their PDS authorization endpoint
2. The user authenticates with their PDS
3. The PDS redirects back to your app with an authorization code
4. Your app exchanges the code for DPoP-bound access and refresh tokens

## When to Use OAuth vs App Passwords

| Scenario | Recommendation |
|----------|---------------|
| User-facing web app (Blazor, ASP.NET) | **OAuth** — no password handling |
| CLI tool / daemon / server-to-server | **App password** — simpler flow |
| Mobile app | **OAuth** — with PKCE for security |
| Automated scripts | **App password** — non-interactive |

## Setup

### 1. Define Your Client Metadata

AT Protocol OAuth uses [Client ID Metadata Documents](https://drafts.aaronpk.com/draft-parecki-oauth-client-id-metadata-document/draft-parecki-oauth-client-id-metadata-document.html). Your client metadata URL serves as your `client_id`.

Host a JSON document at a public URL (e.g., `https://myapp.example.com/client-metadata.json`):

```json
{
  "client_id": "https://myapp.example.com/client-metadata.json",
  "client_name": "My AT Proto App",
  "client_uri": "https://myapp.example.com",
  "redirect_uris": ["https://myapp.example.com/oauth/callback"],
  "grant_types": ["authorization_code", "refresh_token"],
  "response_types": ["code"],
  "scope": "atproto transition:generic",
  "token_endpoint_auth_method": "none",
  "application_type": "web",
  "dpop_bound_access_tokens": true
}
```

### 2. Configure OAuthClient

```csharp
using ATProtoNet.Auth.OAuth;

var oauthOptions = new OAuthOptions
{
    ClientMetadata = new OAuthClientMetadata
    {
        ClientId = "https://myapp.example.com/client-metadata.json",
        ClientName = "My AT Proto App",
        ClientUri = "https://myapp.example.com",
        RedirectUris = ["https://myapp.example.com/oauth/callback"],
        GrantTypes = ["authorization_code", "refresh_token"],
        ResponseTypes = ["code"],
        Scope = "atproto transition:generic",
        TokenEndpointAuthMethod = "none",
        ApplicationType = "web",
        DpopBoundAccessTokens = true,
    },
    Scope = "atproto transition:generic",
};
```

### 3. Create the OAuthClient

```csharp
var httpClient = new HttpClient();
var logger = loggerFactory.CreateLogger<OAuthClient>();
var oauthClient = new OAuthClient(oauthOptions, httpClient, logger);
```

### Development / Loopback Client

For local development, the AT Protocol OAuth spec provides a special [loopback client](https://atproto.com/specs/oauth#localhost-client-development) workflow. Use `http://localhost` as the `client_id` origin (no port number). Configuration is passed via query parameters:

- **`redirect_uri`** — declares allowed redirect paths (default: `http://127.0.0.1/` and `http://[::1]/`). Port numbers are ignored during matching; only the path must match exactly.
- **`scope`** — declares requested scopes (default: `atproto`).

```csharp
var oauthOptions = new OAuthOptions
{
    ClientMetadata = new OAuthClientMetadata
    {
        // Declare the /oauth/callback path via query parameter
        ClientId = "http://localhost?redirect_uri=http%3A%2F%2F127.0.0.1%2Foauth%2Fcallback",
        RedirectUris = ["http://127.0.0.1:5000/oauth/callback"],
        Scope = "atproto",
        TokenEndpointAuthMethod = "none",
        DpopBoundAccessTokens = true,
    },
    Scope = "atproto",
};
```

> **Note:** The redirect URI uses `127.0.0.1` (not `localhost`) per [RFC 8252](https://datatracker.ietf.org/doc/html/rfc8252). Port numbers are not matched by the Authorization Server, so you can use any available port.

See the [`samples/BlazorOAuthSample`](../samples/BlazorOAuthSample/) project for a complete working example.

## Authorization Flow

### Step 1: Start Authorization

```csharp
// The identifier can be a handle, DID, or PDS URL
var (authorizationUrl, state) = await oauthClient.StartAuthorizationAsync(
    identifier: "alice.bsky.social",
    redirectUri: "https://myapp.example.com/oauth/callback");

// Redirect the user to authorizationUrl
```

This performs:
- Identity resolution (handle → DID → PDS)
- Authorization server discovery (PDS → AS metadata)
- PKCE code verifier/challenge generation
- DPoP keypair generation (ES256/P-256)
- Pushed Authorization Request (PAR)

### Step 2: Handle the Callback

When the user is redirected back, your callback receives `code`, `state`, and `iss` query parameters:

```csharp
// In your callback handler
var session = await oauthClient.CompleteAuthorizationAsync(
    code: queryParams["code"],
    state: queryParams["state"],
    issuer: queryParams["iss"]);

Console.WriteLine($"Authenticated as {session.Handle} ({session.Did})");
Console.WriteLine($"PDS: {session.PdsUrl}");
```

This performs:
- State verification (CSRF protection)
- Issuer verification
- Authorization code exchange with DPoP proof
- DID-to-AS consistency verification
- Handle resolution from DID document

### Step 3: Use the Session

Apply the OAuth session to an `AtProtoClient`:

```csharp
var client = new AtProtoClient(new AtProtoClientOptions
{
    InstanceUrl = session.PdsUrl,
});

await client.ApplyOAuthSessionAsync(session);

// Now use the client normally
var profile = await client.Bsky.Actor.GetProfileAsync(session.Did);
await client.PostAsync("Hello from OAuth!");
```

### Step 4: Refresh Tokens

```csharp
var newTokens = await oauthClient.RefreshTokensAsync(session);

// Update session with new tokens
session.AccessToken = newTokens.AccessToken;
if (newTokens.RefreshToken is not null)
    session.RefreshToken = newTokens.RefreshToken;
```

## Dynamic PDS Selection

Users on the AT Protocol can use any PDS. Rather than hardcoding a PDS URL, resolve the user's PDS dynamically:

```csharp
// Set PDS URL at runtime
client.SetPdsUrl("https://custom-pds.example.com");

// Or let OAuth do it — StartAuthorizationAsync resolves the PDS automatically
var (url, state) = await oauthClient.StartAuthorizationAsync(
    "alice.custom-pds.example.com",
    "https://myapp.example.com/callback");
```

## Blazor Integration

ATProtoNet.Blazor provides cookie-based OAuth integration that works with standard Blazor authentication patterns.

### Setup

```csharp
// Program.cs
builder.Services.AddAuthentication("Cookies").AddCookie("Cookies");
builder.Services.AddAtProtoAuthentication();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapAtProtoOAuth();  // Maps /atproto/login, /atproto/callback, /atproto/logout
```

### Login Form

The `LoginForm` component renders a form that submits to the login endpoint:

```razor
<LoginForm ReturnUrl="/" ShowPdsOption="true" />
```

### OAuth Flow

1. User submits their handle via the `LoginForm`
2. `GET /atproto/login?handle=alice.bsky.social` resolves the user's PDS and starts OAuth
3. User authorizes at their PDS
4. `GET /atproto/callback` exchanges the code for tokens, creates claims, and issues a cookie via `HttpContext.SignInAsync()`
5. Standard Blazor `<AuthorizeView>` components work automatically

### Available Claims

After login, these claims are available on `context.User`:

| Claim | Description |
|-------|-------------|
| `ClaimTypes.NameIdentifier` | User's DID |
| `ClaimTypes.Name` | User's handle |
| `did` | User's DID |
| `handle` | User's handle |
| `pds_url` | User's PDS URL |
| `auth_method` | Always `"oauth"` |

### Production Configuration

For production, provide explicit client metadata instead of the auto-generated loopback client_id:

```csharp
builder.Services.AddAtProtoAuthentication(options =>
{
    options.ClientMetadata = new OAuthClientMetadata
    {
        ClientId = "https://myapp.example.com/client-metadata.json",
        ClientName = "My App",
        ClientUri = "https://myapp.example.com",
        RedirectUris = ["https://myapp.example.com/atproto/callback"],
        Scope = "atproto transition:generic",
    };
});
```

See [blazor.md](blazor.md) for complete documentation.

## Security Considerations

### DPoP (Demonstration of Proof-of-Possession)

All access tokens are DPoP-bound. Every API request includes a DPoP proof JWT signed with the session's ES256 key. This prevents token theft — even if an attacker intercepts the access token, they cannot use it without the private key.

### PKCE (Proof Key for Code Exchange)

The authorization code flow uses PKCE with S256 challenge method. The code verifier is generated with 32 bytes of cryptographic randomness and never sent to the authorization server directly.

### State Parameter

The state parameter is generated with 32 bytes of cryptographic randomness to prevent CSRF attacks. It is verified when the callback is received.

### Issuer Verification

The `iss` parameter from the callback is verified against the expected authorization server issuer to prevent mix-up attacks.

### DID Verification

After token exchange, the returned `sub` (DID) is verified against the expected DID (if identity was resolved before authorization). For flows starting from a PDS URL, a full DID → PDS → AS consistency check is performed.

### Handle Validation

Handles are validated against the AT Protocol handle format before being used in URL resolution to prevent SSRF attacks.

### Redirect URI Validation

Redirect URIs must use HTTPS. HTTP is only allowed for localhost during development.

### Pending Authorization Cleanup

Pending authorization states are automatically cleaned up after 10 minutes and limited to 100 concurrent entries to prevent resource exhaustion.

## Server Discovery

The `AuthorizationServerDiscovery` class handles the full resolution chain:

```
Handle → DID → PDS → Protected Resource Metadata → Authorization Server Metadata
```

### Resolution Methods

```csharp
var discovery = new AuthorizationServerDiscovery(httpClient, logger);

// Full resolution from any identifier type
var (pdsUrl, metadata, did) = await discovery.ResolveFromIdentifierAsync("alice.bsky.social");

// Resolve handle → DID (HTTPS well-known, DNS TXT fallback)
var did = await discovery.ResolveHandleToDidAsync("alice.bsky.social");

// Resolve DID → PDS URL
var pds = await discovery.ResolvePdsFromDidAsync("did:plc:abc123");

// Fetch authorization server metadata
var metadata = await discovery.ResolveAuthorizationServerAsync("https://pds.example.com");
```

## DPoP Key Management

Each OAuth session has its own ES256 (P-256) key pair:

```csharp
// Export key for persistence (store securely!)
byte[] keyBytes = session.DPoP.ExportPrivateKey();

// Import key in a new session
var dpop = new DPoPProofGenerator(keyBytes);
```

> **Warning:** The exported key is unencrypted PKCS#8. Store it in a secure location such as an OS keychain, encrypted database, or DPAPI-protected storage.

## Error Handling

OAuth-specific errors throw `OAuthException`:

```csharp
try
{
    var session = await oauthClient.CompleteAuthorizationAsync(code, state, issuer);
}
catch (OAuthException ex) when (ex.ErrorCode == "invalid_state")
{
    // Unknown or expired state parameter
}
catch (OAuthException ex) when (ex.ErrorCode == "issuer_mismatch")
{
    // Authorization server issuer doesn't match
}
catch (OAuthException ex) when (ex.ErrorCode == "did_mismatch")
{
    // Token DID doesn't match expected identity
}
catch (OAuthException ex)
{
    Console.WriteLine($"OAuth error ({ex.ErrorCode}): {ex.Message}");
}
```

### Error Codes

| Code | Description |
|------|-------------|
| `invalid_state` | Unknown or expired state parameter |
| `state_expired` | Authorization state exceeded 10-minute timeout |
| `issuer_mismatch` | Callback issuer doesn't match expected AS |
| `missing_sub` | Token response missing subject (DID) |
| `invalid_sub` | Token response subject is not a valid DID |
| `did_mismatch` | Token DID doesn't match expected identity |
| `invalid_scope` | Token doesn't include `atproto` scope |
| `par_error` | Pushed Authorization Request failed |
| `token_error` | Token exchange failed |
| `no_refresh_token` | Refresh attempted without refresh token |
| `invalid_handle` | Handle contains invalid characters or format |
| `invalid_did` | DID format or host is invalid |
| `discovery_error` | Failed to discover authorization server |
